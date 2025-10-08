#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(OverlayDataAsset))]
    [CanEditMultipleObjects]
    public class OverlayDataAssetInspector : Editor
    {
        // Delayed save fields
        private SerializedProperty _overlayName;
        private SerializedProperty _overlayType;
        private SerializedProperty _umaMaterial;
        private SerializedProperty _textureList;
        private SerializedProperty _textureNames;
        private SerializedProperty _blendList;
        private SerializedProperty _channels;
        private SerializedProperty _rect;
        private SerializedProperty _alphaMask;
        private SerializedProperty _tags;
        private SerializedProperty _occlusionEntries;
        private SerializedProperty _noAutoAdd;
        private SerializedProperty _dontMergeDuplicates;

        private static bool IsEditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;

        void OnEnable()
        {
            // Defer initialization if editor is compiling/updating or target not ready
            if (IsEditorBusy || target == null)
            {
                EditorApplication.delayCall += () => { if (this != null) OnEnable(); };
                return;
            }

            // Before assembly reload, detach and avoid calling into disposed state
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;

            if (serializedObject == null || serializedObject.targetObject == null) return;

            _overlayName = serializedObject.FindProperty("overlayName");
            _overlayType = serializedObject.FindProperty("overlayType");
            _umaMaterial = serializedObject.FindProperty("material");
            _textureList = serializedObject.FindProperty("textureList");
            _textureNames = serializedObject.FindProperty("textureNames");
            _blendList = serializedObject.FindProperty("overlayBlend");
            _dontMergeDuplicates = serializedObject.FindProperty("dontMergeDuplicates");
            _rect = serializedObject.FindProperty("rect");
            _alphaMask = serializedObject.FindProperty("alphaMask");
            _tags = serializedObject.FindProperty("tags");
            _occlusionEntries = serializedObject.FindProperty("OcclusionEntries");
            _noAutoAdd = serializedObject.FindProperty("noAutoAdd");

            var od = target as OverlayDataAsset;
            if (od != null)
            {
                // tagsList init can run during reload
                try { od.tagsList = GUIHelper.InitTagsList("tags", serializedObject); } catch { }
            }

            EditorApplication.update -= DoDelayedSave;
            EditorApplication.update += DoDelayedSave;
        }

        private void HandleBeforeAssemblyReload()
        {
            // Detach update loop proactively to avoid calls into disposed state
            try { EditorApplication.update -= DoDelayedSave; } catch { }
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            try { EditorApplication.update -= DoDelayedSave; } catch { }
        }

        void OnDestroy()
        {
            try { EditorApplication.update -= DoDelayedSave; } catch { }
        }

        void DoDelayedSave()
        {
            // Protect against reload/compilation and null targets
            if (IsEditorBusy || target == null) return;

            var od = target as OverlayDataAsset;
            if (od == null) return;

            try
            {
                if (od.doSave && Time.realtimeSinceStartup > (od.lastActionTime + 0.5f))
                {
                    od.doSave = false;
                    od.lastActionTime = Time.realtimeSinceStartup;
                    EditorUtility.SetDirty(target);
                    UMAUpdateProcessor.UpdateOverlay(target as OverlayDataAsset);
                }
            }
            catch
            {
                // Ignore during reload
            }
        }

        public override void OnInspectorGUI()
        {
            // Busy or invalid state protections
            if (IsEditorBusy)
            {
                EditorGUILayout.HelpBox("Unity is compiling/reloading. Please wait…", MessageType.Info);
                return;
            }
            if (target == null || serializedObject == null || serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox("Inspector target is not available (asset reloading).", MessageType.Info);
                return;
            }

            var od = target as OverlayDataAsset;
            if (od == null)
            {
                EditorGUILayout.HelpBox("Overlay asset is not available.", MessageType.Info);
                return;
            }

            if (od.lastActionTime == 0) { od.lastActionTime = Time.realtimeSinceStartup; }

            // Validate blend list may resize arrays internally, guard it
            try { od.ValidateBlendList(); } catch { }

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            // overlayName + "Use Obj Name" button (multi-object safe)
            GUILayout.BeginHorizontal();
            if (_overlayName != null) { EditorGUILayout.PropertyField(_overlayName); }
            if (GUILayout.Button("Use Obj Name", GUILayout.Width(90)))
            {
                foreach (var t in targets)
                {
                    var overlayDataAsset = t as OverlayDataAsset;
                    if (overlayDataAsset == null) continue;
                    overlayDataAsset.overlayName = overlayDataAsset.name;
                    EditorUtility.SetDirty(overlayDataAsset);
                    GUI.changed = true;
                }
            }
            GUILayout.EndHorizontal();

            if (_overlayType != null) { EditorGUILayout.PropertyField(_overlayType); }
            EditorGUILayout.LabelField("Note: It is recommended to use UV coordinates (0.0 -> 1.0) in 2.10+ for rect fields.", EditorStyles.helpBox);
            if (_rect != null) { EditorGUILayout.PropertyField(_rect); }
            if (_noAutoAdd != null) { EditorGUILayout.PropertyField(_noAutoAdd); }
            if (_dontMergeDuplicates != null)
            {
                EditorGUILayout.PropertyField(_dontMergeDuplicates, new GUIContent("Don't Merge Duplicates", "If this is true, this overlay will not removed if it's a duplicate"));
            }

            // Material copy drop area
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop a Material here to copy textures to texture channels");
            CopyMaterialDropArea(dropArea);
            EditorGUILayout.Space();

            // UMA Material and its channels
            if (_umaMaterial != null) { EditorGUILayout.PropertyField(_umaMaterial); }

            if (_umaMaterial != null && _umaMaterial.objectReferenceValue != null)
            {
                int textureChannelCount = 0;
                try
                {
                    var umaMatSO = new SerializedObject(_umaMaterial.objectReferenceValue);
                    _channels = umaMatSO.FindProperty("channels");

                    if (_channels == null)
                    {
                        EditorGUILayout.HelpBox("Channels not found on UMA Material!", MessageType.Error);
                    }
                    else
                    {
                        textureChannelCount = _channels.arraySize;
                    }
                }
                catch
                {
                    EditorGUILayout.HelpBox("Failed to read UMA Material channels (reloading).", MessageType.Info);
                }

                int overlayTextureCount = _textureList != null ? _textureList.arraySize : 0;
                od.textureFoldout = GUIHelper.FoldoutBar(od.textureFoldout, $"Texture Channels ({textureChannelCount}) Material Channels ({overlayTextureCount})");

                if (od.textureFoldout && _textureList != null && _blendList != null)
                {
                    GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                    // Show Array.size editor safely
                    var arraySizeProp = _textureList.FindPropertyRelative("Array.size");
                    if (arraySizeProp != null)
                    {
                        EditorGUILayout.PropertyField(arraySizeProp);
                        _blendList.arraySize = _textureList.arraySize;
                    }

                    for (int i = 0; i < _textureList.arraySize; i++)
                    {
                        SerializedProperty textureElement = _textureList.GetArrayElementAtIndex(i);
                        SerializedProperty blendElement = (i < _blendList.arraySize) ? _blendList.GetArrayElementAtIndex(i) : null;
                        string materialName = "Unknown";

                        string texName = "";
                        if (_textureNames != null && i < _textureNames.arraySize)
                        {
                            var texNameProp = _textureNames.GetArrayElementAtIndex(i);
                            if (texNameProp != null)
                            {
                                texName = texNameProp.stringValue;
                            }
                        }
                        // Try to resolve channel display name
                        try
                        {
                            if (_channels != null && i < _channels.arraySize)
                            {
                                SerializedProperty channel = _channels.GetArrayElementAtIndex(i);
                                if (channel != null)
                                {
                                    SerializedProperty materialPropertyName = channel.FindPropertyRelative("materialPropertyName");
                                    if (materialPropertyName != null)
                                    {
                                        materialName = materialPropertyName.stringValue;
                                    }
                                }
                            }
                        }
                        catch { /* ignore */ }

                        string textureLabel = (textureElement != null && textureElement.objectReferenceValue != null) ? "" : "(Texture is Unloaded)";

                        GUILayout.BeginHorizontal();
                        if (textureElement != null)
                        {
                            EditorGUILayout.PropertyField(textureElement, new GUIContent(materialName), GUILayout.ExpandWidth(true));
                        }

                        if (blendElement != null)
                        {
                            EditorGUILayout.PropertyField(blendElement, GUIContent.none, GUILayout.Width(110));
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        if (textureElement != null)
                        {
                            if (!string.IsNullOrEmpty(texName))
                            {
                                EditorGUILayout.LabelField($"Texture Name: {texName}", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                            }
                            else
                            {
                                EditorGUILayout.LabelField("Texture Name: (not set)", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                            }
                            EditorGUILayout.LabelField(textureLabel, EditorStyles.miniLabel, GUILayout.Width(150));
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUIHelper.EndVerticalPadded(10);
                }

                // Warn about mismatched counts (if we could read channels)
                if (_textureList != null && _channels != null)
                {
                    if (_textureList.arraySize != _channels.arraySize)
                    {
                        EditorGUILayout.HelpBox($"Overlay Texture count {_textureList.arraySize} and UMA Material channel count {_channels.arraySize} don't match!", MessageType.Error);
                    }
                }

                // Warn when textures missing
                if (_textureList != null && !_textureList.hasMultipleDifferentValues)
                {
                    bool allValid = true;
                    for (int i = 0; i < _textureList.arraySize; i++)
                    {
                        if (_textureList.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        {
                            allValid = false;
                            break;
                        }
                    }
                    if (_textureNames != null && _textureNames.arraySize == _textureList.arraySize)
                    {
                        allValid = true;
                        for (int i = 0; i < _textureNames.arraySize; i++)
                        {
                            if (_textureNames.GetArrayElementAtIndex(i).stringValue == null)
                            {
                                allValid = false;
                                break;
                            }
                        }
                        if (!allValid)
                        {
                            EditorGUILayout.HelpBox("Not all texture names in Texture Names set. This overlay will only work as an additional overlay in a recipe", MessageType.Warning);
                        }
                    }
                    else
                    {
                        if (!allValid)
                        {
                            EditorGUILayout.HelpBox("Not all textures in Texture List set. This overlay will only work as an additional overlay in a recipe", MessageType.Warning);
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No UMA Material selected!", MessageType.Warning);
            }

            // Alpha mask
            od.additionalFoldout = GUIHelper.FoldoutBar(od.additionalFoldout, "Alpha mask Parameters");
            if (od.additionalFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                EditorGUILayout.HelpBox("The alpha mask is optional. If it is not set the texture[0].alpha is used instead.", MessageType.Info);
                if (_alphaMask != null) { EditorGUILayout.PropertyField(_alphaMask); }
                GUIHelper.EndVerticalPadded(10);
            }

            // Tags
            od.tagsFoldout = GUIHelper.FoldoutBar(od.tagsFoldout, "Tags");
            if (od.tagsFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                try { (target as OverlayDataAsset)?.tagsList?.DoLayoutList(); } catch { }
                GUIHelper.EndVerticalPadded(10);
            }

            // Occlusion
            od.occlusionFoldout = GUIHelper.FoldoutBar(od.occlusionFoldout, "Occlusion");
            if (od.occlusionFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                if (_occlusionEntries != null) { EditorGUILayout.PropertyField(_occlusionEntries, true); }
                GUIHelper.EndVerticalPadded(10);
            }

            serializedObject.ApplyModifiedProperties();

            if (EditorGUI.EndChangeCheck())
            {
                od.lastActionTime = Time.realtimeSinceStartup;
                od.doSave = true;
            }
        }

        private void CopyMaterialDropArea(Rect dropArea)
        {
            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    var draggedObjects = DragAndDrop.objectReferences;
                    if (draggedObjects == null || draggedObjects.Length == 0)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                        return;
                    }
                    var obj = draggedObjects[0];
                    DragAndDrop.visualMode = (obj is Material) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    Event.current.Use();
                }
            }
            if (evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    var draggedObjects = DragAndDrop.objectReferences;
                    if (draggedObjects != null && draggedObjects.Length > 0)
                    {
                        var obj = draggedObjects[0];
                        if (obj is Material)
                        {
                            DragAndDrop.AcceptDrag();
                            Material material = obj as Material;
                            var od = target as OverlayDataAsset;
                            if (od == null || od.material == null || od.material.channels == null)
                            {
                                EditorUtility.DisplayDialog("Error", "Overlay UMA Material or its channels are missing.", "OK");
                                return;
                            }

                            int channelCount = 0;
                            try { channelCount = od.material.channels.Length; } catch { channelCount = 0; }

                            // Ensure lists exist
                            if (od.textureList == null) od.textureList = new Texture[0];
                            if (od.overlayBlend == null) od.overlayBlend = new OverlayDataAsset.OverlayBlend[0];

                            // Resize texture/blend arrays to match channel count
                            if (channelCount > 0 && (od.textureList.Length != channelCount || od.overlayBlend.Length != channelCount))
                            {
                                var oldTextureList = od.textureList;
                                var oldBlendList = od.overlayBlend;

                                od.textureList = new Texture[channelCount];
                                od.overlayBlend = new OverlayDataAsset.OverlayBlend[channelCount];

                                for (int i = 0; i < channelCount; i++)
                                {
                                    od.textureList[i] = (i < oldTextureList.Length) ? oldTextureList[i] : null;
                                    od.overlayBlend[i] = (i < oldBlendList.Length) ? oldBlendList[i] : OverlayDataAsset.OverlayBlend.Normal;
                                }
                            }

                            // Copy textures from material by UMA channel property names
                            for (int i = 0; i < channelCount; i++)
                            {
                                string propertyName = null;
                                try { propertyName = od.material.channels[i].materialPropertyName; } catch { propertyName = null; }
                                if (string.IsNullOrEmpty(propertyName)) continue;

                                try
                                {
                                    Texture tex = material.GetTexture(propertyName);
                                    if (tex != null)
                                    {
                                        od.textureList[i] = tex;
                                    }
                                }
                                catch { /* ignore access errors */ }
                            }

                            // Flag for delayed save
                            od.lastActionTime = Time.realtimeSinceStartup;
                            od.doSave = true;
                        }
                    }
                    Event.current.Use();
                }
            }
        }
    }
}
#endif