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
        private bool _showOverlayValueCopy;
        private OverlayDataAsset _sourceOverlay;
        private bool _copyOverlayMaterial = true;
        private bool _copyOverlayTextureChannels = true;
        private static GUIContent _editTextureButtonContent;

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

            _overlayName = serializedObject.FindProperty("_oldOverlayName");
            _overlayType = serializedObject.FindProperty("overlayType");
            _umaMaterial = serializedObject.FindProperty("material");
            _textureList = serializedObject.FindProperty("_textureList");
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
                EditorGUILayout.HelpBox("Unity is compiling/reloading. Please wait.", MessageType.Info);
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
            if (GUILayout.Button("Clear legacy overlay name", GUILayout.Width(190)))
            {
                foreach (var t in targets)
                {
                    var overlayDataAsset = t as OverlayDataAsset;
                    if (overlayDataAsset == null) continue;
                    overlayDataAsset._oldOverlayName = "";
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


            // Overlay Group
            var overlayGroupProp = serializedObject.FindProperty("overlayGroup");
            if (overlayGroupProp != null)
            {
                UMASettings settings = UMASettings.GetOrCreateSettings();
                string[] groupNames = (settings != null) ? settings.groupNames : null;
                if (groupNames == null)
                {
                    groupNames = Array.Empty<string>();
                }

                int selectedGroupIndex = 0;
                for (int i = 0; i < groupNames.Length; i++)
                {
                    if (string.Equals(groupNames[i], overlayGroupProp.stringValue, StringComparison.Ordinal))
                    {
                        selectedGroupIndex = i;
                        break;
                    }
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(overlayGroupProp, new GUIContent("Overlay Group"), GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(groupNames.Length == 0))
                {
                    EditorGUI.BeginChangeCheck();
                    int newSelectedGroupIndex = EditorGUILayout.Popup(selectedGroupIndex, groupNames, GUILayout.Width(110));
                    if (EditorGUI.EndChangeCheck())
                    {
                        string value = groupNames[Mathf.Clamp(newSelectedGroupIndex, 0, groupNames.Length - 1)];
                        foreach (var t in targets)
                        {
                            var oda = t as OverlayDataAsset;
                            if (oda == null)
                            {
                                continue;
                            }
                            oda.overlayGroup = value;
                            EditorUtility.SetDirty(oda);
                        }
                        overlayGroupProp.stringValue = value;
                        od.lastActionTime = Time.realtimeSinceStartup;
                        od.doSave = true;
                        GUI.changed = true;
                    }
                }
                GUILayout.EndHorizontal();
            }

            // Material copy drop area
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop a Material here to copy textures to texture channels");
            CopyMaterialDropArea(dropArea);
            EditorGUILayout.Space();
            DrawOverlayValueCopyUI(od);

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
				od.textureFoldout = GUIHelper.FoldoutBar(od.textureFoldout, $"Texture Channels ({overlayTextureCount}) Material Channels ({textureChannelCount})");

                if (od.textureFoldout)
                {
                    GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                    if (_textureList != null && _blendList != null)
                    {
                        // Show Array.size editor safely
                        var arraySizeProp = _textureList.FindPropertyRelative("Array.size");
                        if (arraySizeProp != null)
                        {
                            EditorGUILayout.PropertyField(arraySizeProp);
                            _blendList.arraySize = _textureList.arraySize;
                            if (_textureNames != null)
                            {
                                _textureNames.arraySize = _textureList.arraySize;
                            }
                        }

                        bool removedTextureChannel = false;
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

                            Texture2D editableTexture = textureElement != null ? textureElement.objectReferenceValue as Texture2D : null;
                            using (new EditorGUI.DisabledScope(editableTexture == null))
                            {
                                if (GUILayout.Button(GetEditTextureButtonContent(), GUILayout.Width(22), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                                {
                                    UMATextureUtilitiesWindow.Open(new[] { editableTexture });
                                }
                            }

                            if (GUILayout.Button("x", GUILayout.Width(22)))
                            {
                                RemoveTextureChannelAt(i);
                                removedTextureChannel = true;
                            }
                            GUILayout.EndHorizontal();

                            if (removedTextureChannel)
                            {
                                break;
                            }

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
                    }
                    else if (_textureNames != null)
                    {
                        EditorGUILayout.HelpBox("Textures have been removed. To reload, select the reload button below", MessageType.Info);
                        for (int i = 0; i < _textureNames.arraySize; i++)
                        {
                            SerializedProperty textureElement = _textureNames.GetArrayElementAtIndex(i);
                            EditorGUILayout.PropertyField(textureElement);
                        }

                        if (GUILayout.Button("Reload Textures"))
                        {
                            var ovl = target as OverlayDataAsset;
                            if (ovl == null) return;

                            Undo.RecordObject(ovl, "Reload Textures");

                            // Ensure arrays exist
                            ovl.textureNames = ovl.textureNames ?? Array.Empty<string>();
                            ovl.textureList = ovl.textureList ?? Array.Empty<Texture>();
                            ovl.overlayBlend = ovl.overlayBlend ?? Array.Empty<OverlayDataAsset.OverlayBlend>();

                            // Resize backing arrays on the object (SerializedProperty can be null when all refs are null)
                            if (ovl.textureList.Length != ovl.textureNames.Length)
                            {
                                // textureList: resize via local, then assign back (properties cannot be passed by ref)
                                var texArray = ovl.textureList;
                                Array.Resize(ref texArray, ovl.textureNames.Length);
                                ovl.textureList = texArray;
                            }
                            if (ovl.overlayBlend.Length != ovl.textureNames.Length)
                            {
                                int oldLen = ovl.overlayBlend.Length;
                                Array.Resize(ref ovl.overlayBlend, ovl.textureNames.Length);
                                // Initialize new entries to Normal
                                for (int i = oldLen; i < ovl.overlayBlend.Length; i++)
                                    ovl.overlayBlend[i] = OverlayDataAsset.OverlayBlend.Normal;
                            }

                            // Optionally try to resolve textures by name
                            for (int i = 0; i < ovl.textureNames.Length; i++)
                            {
                                string texName = ovl.textureNames[i];
                                if (string.IsNullOrEmpty(texName)) continue;

                                Texture2D tex = UMAAssetIndexer.Instance.GetAsset<Texture2D>(texName);
                                ovl.textureList[i] = tex; // may be null if not found
                            }

                            EditorUtility.SetDirty(ovl);
                            AssetDatabase.SaveAssetIfDirty(ovl);
                            UMAUpdateProcessor.UpdateOverlay(ovl);
                            serializedObject.Update();
                            // refind properties, since serializedObject.Update may reset them, and unity can be weird about it
                            _textureList = serializedObject.FindProperty("_textureList");
                            _textureNames = serializedObject.FindProperty("textureNames");
                            _blendList = serializedObject.FindProperty("overlayBlend");
                            Repaint();
                        }
                    }

                    if (GUILayout.Button("Add Texture Channel"))
                    {
                        AddTextureChannel();
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

        private static GUIContent CreateEditTextureButtonContent()
        {
            GUIContent content = EditorGUIUtility.IconContent("d_editicon.sml");
            if (content == null || content.image == null)
            {
                content = EditorGUIUtility.IconContent("editicon.sml");
            }
            if (content == null || content.image == null)
            {
                content = new GUIContent("E");
            }
            content.tooltip = "Open texture in UMA Texture Utilities";
            return content;
        }

        private static GUIContent GetEditTextureButtonContent()
        {
            if (_editTextureButtonContent == null)
            {
                _editTextureButtonContent = CreateEditTextureButtonContent();
            }

            return _editTextureButtonContent;
        }

        private void DrawOverlayValueCopyUI(OverlayDataAsset targetOverlay)
        {
            if (targetOverlay == null)
            {
                return;
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox("Value copy is disabled while editing multiple overlays.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Copy Values from another Overlay"))
            {
                _showOverlayValueCopy = !_showOverlayValueCopy;
            }

            if (!_showOverlayValueCopy)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _sourceOverlay = EditorGUILayout.ObjectField(
                "Source Overlay",
                _sourceOverlay,
                typeof(OverlayDataAsset),
                false
            ) as OverlayDataAsset;

            _copyOverlayMaterial = EditorGUILayout.ToggleLeft("Material", _copyOverlayMaterial);
            _copyOverlayTextureChannels = EditorGUILayout.ToggleLeft("Texture Channel Data", _copyOverlayTextureChannels);

            bool hasSource = _sourceOverlay != null;
            bool isSelfReference = _sourceOverlay == targetOverlay;
            bool hasAnySelection = _copyOverlayMaterial || _copyOverlayTextureChannels;

            if (!hasSource)
            {
                EditorGUILayout.HelpBox("Assign a source overlay to copy from.", MessageType.Info);
            }
            else if (isSelfReference)
            {
                EditorGUILayout.HelpBox("Source and target overlays are the same. Choose a different source overlay.", MessageType.Warning);
            }
            else if (!hasAnySelection)
            {
                EditorGUILayout.HelpBox("Select at least one value to copy.", MessageType.Info);
            }
            else if (_copyOverlayTextureChannels && !_copyOverlayMaterial && targetOverlay.material != _sourceOverlay.material)
            {
                EditorGUILayout.HelpBox("Material is not being copied and source/target materials differ. Texture channels may not align.", MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(!hasSource || isSelfReference || !hasAnySelection);
            if (GUILayout.Button("Copy Selected Values"))
            {
                Undo.RecordObject(targetOverlay, "Copy Overlay Values");

                if (_copyOverlayMaterial)
                {
                    targetOverlay.material = _sourceOverlay.material;
                    targetOverlay.materialName = _sourceOverlay.materialName;
                }
                if (_copyOverlayTextureChannels)
                {
                    targetOverlay.textureList = CloneTextureArray(_sourceOverlay.textureList);
                    targetOverlay.textureNames = CloneStringArray(_sourceOverlay.textureNames);
                    targetOverlay.overlayBlend = CloneOverlayBlendArray(_sourceOverlay.overlayBlend);
                }

                targetOverlay.ValidateBlendList();
                targetOverlay.lastActionTime = Time.realtimeSinceStartup;
                targetOverlay.doSave = true;
                EditorUtility.SetDirty(targetOverlay);
                AssetDatabase.SaveAssetIfDirty(targetOverlay);
                UMAUpdateProcessor.UpdateOverlay(targetOverlay);
                serializedObject.Update();
                _textureList = serializedObject.FindProperty("_textureList");
                _textureNames = serializedObject.FindProperty("textureNames");
                _blendList = serializedObject.FindProperty("overlayBlend");
                Repaint();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private static Texture[] CloneTextureArray(Texture[] source)
        {
            if (source == null)
            {
                return Array.Empty<Texture>();
            }

            var cloned = new Texture[source.Length];
            Array.Copy(source, cloned, source.Length);
            return cloned;
        }

        private static string[] CloneStringArray(string[] source)
        {
            if (source == null)
            {
                return Array.Empty<string>();
            }

            var cloned = new string[source.Length];
            Array.Copy(source, cloned, source.Length);
            return cloned;
        }

        private static OverlayDataAsset.OverlayBlend[] CloneOverlayBlendArray(OverlayDataAsset.OverlayBlend[] source)
        {
            if (source == null)
            {
                return Array.Empty<OverlayDataAsset.OverlayBlend>();
            }

            var cloned = new OverlayDataAsset.OverlayBlend[source.Length];
            Array.Copy(source, cloned, source.Length);
            return cloned;
        }

        private static void DeleteArrayElementAndCompact(SerializedProperty arrayProperty, int index)
        {
            if (arrayProperty == null || !arrayProperty.isArray || index < 0 || index >= arrayProperty.arraySize)
            {
                return;
            }

            int oldSize = arrayProperty.arraySize;
            arrayProperty.DeleteArrayElementAtIndex(index);
            if (arrayProperty.arraySize == oldSize && index < arrayProperty.arraySize)
            {
                arrayProperty.DeleteArrayElementAtIndex(index);
            }
        }

        private void RemoveTextureChannelAt(int index)
        {
            if (_textureList == null)
            {
                _textureList = serializedObject.FindProperty("_textureList");
            }
            if (_textureNames == null)
            {
                _textureNames = serializedObject.FindProperty("textureNames");
            }
            if (_blendList == null)
            {
                _blendList = serializedObject.FindProperty("overlayBlend");
            }

            if (_textureList == null || _textureNames == null || _blendList == null || index < 0 || index >= _textureList.arraySize)
            {
                return;
            }

            Undo.RecordObjects(targets, "Remove Texture Channel");

            DeleteArrayElementAndCompact(_textureList, index);
            if (index < _textureNames.arraySize)
            {
                DeleteArrayElementAndCompact(_textureNames, index);
            }
            if (index < _blendList.arraySize)
            {
                DeleteArrayElementAndCompact(_blendList, index);
            }

            _textureNames.arraySize = _textureList.arraySize;
            _blendList.arraySize = _textureList.arraySize;

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            _textureList = serializedObject.FindProperty("_textureList");
            _textureNames = serializedObject.FindProperty("textureNames");
            _blendList = serializedObject.FindProperty("overlayBlend");

            foreach (var selectedTarget in targets)
            {
                var overlay = selectedTarget as OverlayDataAsset;
                if (overlay == null)
                {
                    continue;
                }

                overlay.lastActionTime = Time.realtimeSinceStartup;
                overlay.doSave = true;
                EditorUtility.SetDirty(overlay);
            }

            Repaint();
        }

        private void AddTextureChannel()
        {
            if (_textureList == null)
            {
                _textureList = serializedObject.FindProperty("_textureList");
            }
            if (_textureNames == null)
            {
                _textureNames = serializedObject.FindProperty("textureNames");
            }
            if (_blendList == null)
            {
                _blendList = serializedObject.FindProperty("overlayBlend");
            }

            if (_textureList == null || _textureNames == null || _blendList == null)
            {
                return;
            }

            Undo.RecordObjects(targets, "Add Texture Channel");

            int newIndex = _textureList.arraySize;
            _textureList.arraySize++;
            _textureNames.arraySize = _textureList.arraySize;
            _blendList.arraySize = _textureList.arraySize;

            SerializedProperty textureElement = _textureList.GetArrayElementAtIndex(newIndex);
            if (textureElement != null)
            {
                textureElement.objectReferenceValue = null;
            }

            SerializedProperty textureNameElement = _textureNames.GetArrayElementAtIndex(newIndex);
            if (textureNameElement != null)
            {
                textureNameElement.stringValue = string.Empty;
            }

            SerializedProperty blendElement = _blendList.GetArrayElementAtIndex(newIndex);
            if (blendElement != null)
            {
                blendElement.enumValueIndex = (int)OverlayDataAsset.OverlayBlend.Normal;
            }

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            _textureList = serializedObject.FindProperty("_textureList");
            _textureNames = serializedObject.FindProperty("textureNames");
            _blendList = serializedObject.FindProperty("overlayBlend");

            foreach (var selectedTarget in targets)
            {
                var overlay = selectedTarget as OverlayDataAsset;
                if (overlay == null)
                {
                    continue;
                }

                overlay.lastActionTime = Time.realtimeSinceStartup;
                overlay.doSave = true;
                EditorUtility.SetDirty(overlay);
            }

            Repaint();
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
                            if (od.textureNames == null) od.textureNames = new string[0];
                            if (od.overlayBlend == null) od.overlayBlend = new OverlayDataAsset.OverlayBlend[0];

                            // Resize texture/blend/name arrays to match channel count
                            if (channelCount > 0 && (od.textureList.Length != channelCount || od.textureNames.Length != channelCount || od.overlayBlend.Length != channelCount))
                            {
                                var oldTextureList = od.textureList;
                                var oldTextureNames = od.textureNames;
                                var oldBlendList = od.overlayBlend;

                                od.textureList = new Texture[channelCount];
                                od.textureNames = new string[channelCount];
                                od.overlayBlend = new OverlayDataAsset.OverlayBlend[channelCount];

                                for (int i = 0; i < channelCount; i++)
                                {
                                    od.textureList[i] = (i < oldTextureList.Length) ? oldTextureList[i] : null;
                                    od.textureNames[i] = (i < oldTextureNames.Length) ? oldTextureNames[i] : string.Empty;
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
