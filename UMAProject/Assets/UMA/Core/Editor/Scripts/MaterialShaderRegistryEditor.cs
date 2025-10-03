using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMA.MaterialShaderRegistry))]
    public class MaterialShaderRegistryEditor : Editor
    {
        private SerializedProperty _entriesProp;
        private SerializedProperty _autoSyncProp;

        private GUIStyle _dropStyle;

        private static bool IsHiddenInternalName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("Hidden/Internal", System.StringComparison.OrdinalIgnoreCase);
        }

        private void OnEnable()
        {
            _entriesProp = serializedObject.FindProperty("_entries");
            _autoSyncProp = serializedObject.FindProperty("_autoSyncShaderFromMaterial");

            _dropStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 12
            };
        }

        public override void OnInspectorGUI()
        {
            var registry = (UMA.MaterialShaderRegistry)target;
            bool needSave = false;

            serializedObject.Update();

            DrawHeader(registry);
            DrawDropArea(ref needSave, registry);

            EditorGUILayout.Space(6);
            if (_autoSyncProp != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_autoSyncProp, new GUIContent("Auto Sync From Material"));
                if (EditorGUI.EndChangeCheck())
                {
                    needSave = true;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel);

            DrawToolbar(ref needSave, registry);

            EditorGUILayout.Space(4);
            if (_entriesProp != null)
            {
                for (int i = 0; i < _entriesProp.arraySize; i++)
                {
                    var entryProp = _entriesProp.GetArrayElementAtIndex(i);
                    DrawEntry(entryProp, i, ref needSave, registry);
                    EditorGUILayout.Space(2);
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                needSave = true;
            }

            if (needSave)
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }
        }

        private void DrawHeader(UMA.MaterialShaderRegistry registry)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Material Shader Registry", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Rebuild Index", GUILayout.Width(110)))
                {
                    Undo.RecordObject(registry, "Rebuild Index");
                    registry.BuildIndex();
                    EditorUtility.SetDirty(registry);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private void DrawToolbar(ref bool needSave, UMA.MaterialShaderRegistry registry)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Empty", GUILayout.Width(90)))
                {
                    Undo.RecordObject(registry, "Add Entry");
                    _entriesProp.arraySize++;
                    var newEntry = _entriesProp.GetArrayElementAtIndex(_entriesProp.arraySize - 1);
                    newEntry.FindPropertyRelative("material").objectReferenceValue = null;
                    newEntry.FindPropertyRelative("shader").objectReferenceValue = null;
                    newEntry.FindPropertyRelative("shaderName").stringValue = string.Empty;
                    needSave = true;
                }

                if (GUILayout.Button("Add From Selection", GUILayout.Width(150)))
                {
                    var mats = GetMaterialsFromObjects(Selection.objects);
                    if (mats.Count > 0)
                    {
                        Undo.RecordObject(registry, "Add Materials");
                        foreach (var m in mats)
                        {
                            registry.AddOrUpdate(m);
                        }
                        EditorUtility.SetDirty(registry);
                        AssetDatabase.SaveAssets();
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Resolve Missing Shaders", GUILayout.Width(180)))
                {
                    Undo.RecordObject(registry, "Resolve Missing Shaders");
                    bool changed = false;
                    var entries = _entriesProp;
                    for (int i = 0; i < entries.arraySize; i++)
                    {
                        var e = entries.GetArrayElementAtIndex(i);
                        var shaderProp = e.FindPropertyRelative("shader");
                        var nameProp = e.FindPropertyRelative("shaderName");

                        if (shaderProp.objectReferenceValue == null && !string.IsNullOrEmpty(nameProp.stringValue))
                        {
                            var found = Shader.Find(nameProp.stringValue);
                            if (found != null)
                            {
                                shaderProp.objectReferenceValue = found;
                                changed = true;
                            }
                        }
                    }
                    if (changed)
                    {
                        serializedObject.ApplyModifiedProperties();
                        registry.BuildIndex();
                        EditorUtility.SetDirty(registry);
                        AssetDatabase.SaveAssets();
                    }
                }

                if (GUILayout.Button("Sync From Material", GUILayout.Width(160)))
                {
                    Undo.RecordObject(registry, "Sync Shaders From Materials");
                    bool changed = false;
                    var entries = _entriesProp;
                    for (int i = 0; i < entries.arraySize; i++)
                    {
                        var e = entries.GetArrayElementAtIndex(i);
                        var mat = e.FindPropertyRelative("material").objectReferenceValue as Material;
                        var shaderProp = e.FindPropertyRelative("shader");
                        var nameProp = e.FindPropertyRelative("shaderName");

                        if (mat != null)
                        {
                            var s = mat.shader;
                            if (shaderProp.objectReferenceValue != s)
                            {
                                shaderProp.objectReferenceValue = s;
                                changed = true;
                            }
                            var sn = s != null ? s.name : string.Empty;
                            if (!IsHiddenInternalName(sn) && nameProp.stringValue != sn)
                            {
                                nameProp.stringValue = sn;
                                changed = true;
                            }
                        }
                    }
                    if (changed)
                    {
                        serializedObject.ApplyModifiedProperties();
                        registry.BuildIndex();
                        EditorUtility.SetDirty(registry);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
        }

        private void DrawDropArea(ref bool needSave, UMA.MaterialShaderRegistry registry)
        {
            EditorGUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none, _dropStyle);
            GUI.Label(rect, "Drag & Drop Materials here to add/update", _dropStyle);

            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition))
                return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    var mats = GetMaterialsFromObjects(DragAndDrop.objectReferences);
                    if (mats.Count > 0)
                    {
                        Undo.RecordObject(registry, "Add Materials");
                        foreach (var m in mats)
                        {
                            registry.AddOrUpdate(m);
                        }
                        EditorUtility.SetDirty(registry);
                        AssetDatabase.SaveAssets();
                        needSave = false; // already saved above
                        Repaint();
                    }

                    evt.Use();
                }
                else
                {
                    evt.Use();
                }
            }
        }

        private void DrawEntry(SerializedProperty entryProp, int index, ref bool needSave, UMA.MaterialShaderRegistry registry)
        {
            var matProp = entryProp.FindPropertyRelative("material");
            var shaderProp = entryProp.FindPropertyRelative("shader");
            var nameProp = entryProp.FindPropertyRelative("shaderName");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var title = matProp.objectReferenceValue != null
                        ? ((Material)matProp.objectReferenceValue).name
                        : $"Entry {index}";
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        var obj = matProp.objectReferenceValue != null ? matProp.objectReferenceValue : (Object)registry;
                        EditorGUIUtility.PingObject(obj);
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        Undo.RecordObject(registry, "Remove Entry");
                        // Remove via serialized property to avoid re-enumeration issues
                        _entriesProp.DeleteArrayElementAtIndex(index);
                        needSave = true;
                        return;
                    }
                }

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(matProp, new GUIContent("Material"));
                bool materialChanged = EditorGUI.EndChangeCheck();

                if (materialChanged)
                {
                    var mat = matProp.objectReferenceValue as Material;
                    // Best-effort: keep shader and name in sync on material change
                    shaderProp.objectReferenceValue = mat != null ? mat.shader : null;
                    var n = (mat != null && mat.shader != null) ? mat.shader.name : string.Empty;
                    if (!IsHiddenInternalName(n))
                    {
                        nameProp.stringValue = n;
                    }
                    needSave = true;
                }

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(shaderProp, new GUIContent("Shader"));
                if (EditorGUI.EndChangeCheck())
                {
                    // Keep shaderName synced when shader changes (skip hidden/internal)
                    var shader = shaderProp.objectReferenceValue as Shader;
                    if (shader != null && !IsHiddenInternalName(shader.name))
                    {
                        nameProp.stringValue = shader.name;
                    }
                    needSave = true;
                }

                // Delayed text field for shader name
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.DelayedTextField(new GUIContent("Shader Name"), nameProp.stringValue);
                if (EditorGUI.EndChangeCheck())
                {
                    nameProp.stringValue = newName;
                    needSave = true;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Resolve From Name", GUILayout.Width(150)))
                    {
                        var found = !string.IsNullOrEmpty(nameProp.stringValue) ? Shader.Find(nameProp.stringValue) : null;
                        if (found != null)
                        {
                            shaderProp.objectReferenceValue = found;
                            needSave = true;
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("Shader not found by name.", MessageType.Warning);
                        }
                    }

                    if (GUILayout.Button("Sync From Material", GUILayout.Width(150)))
                    {
                        var mat = matProp.objectReferenceValue as Material;
                        if (mat != null)
                        {
                            shaderProp.objectReferenceValue = mat.shader;
                            var n = mat.shader != null ? mat.shader.name : string.Empty;
                            if (!IsHiddenInternalName(n))
                            {
                                nameProp.stringValue = n;
                            }
                            needSave = true;
                        }
                    }
                }
            }
        }

        private static HashSet<Material> GetMaterialsFromObjects(Object[] objects)
        {
            var mats = new HashSet<Material>();
            foreach (var obj in objects)
            {
                if (obj is Material m)
                {
                    mats.Add(m);
                }
                else if (obj is GameObject go)
                {
                    // Collect materials from renderers on dropped GameObjects
                    var renderers = go.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in renderers)
                    {
                        foreach (var mat in r.sharedMaterials)
                        {
                            if (mat != null) mats.Add(mat);
                        }
                    }
                }
            }
            return mats;
        }
    }
}