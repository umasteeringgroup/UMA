using System;
using System.Collections.Generic;
using UMA;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    [CustomEditor(typeof(MeshModifier))]
    public class MeshModifierInspector : Editor
    {
        private SerializedProperty _modifiersProp;
        private GUIStyle _headerStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _boxedStyle;

        // Persist foldout states per asset instance id (not across domain reload, but stable during play/compiles)
        private static readonly Dictionary<int, List<bool>> FoldoutStates = new Dictionary<int, List<bool>>();

        private int _instanceId;

        private void OnEnable()
        {
            _instanceId = target != null ? target.GetInstanceID() : 0; 
            AcquireProperties();
            EnsureFoldoutList();
        }

        private void AcquireProperties()
        {
            if (serializedObject != null)
            {
                _modifiersProp = serializedObject.FindProperty("modifiers");
            }
        }

        private void EnsureStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            }
            if (_foldoutStyle == null)
            {
                _foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            }
            if (_boxedStyle == null)
            {
                _boxedStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 4, 6) };
            }
        }

        private void EnsureFoldoutList()
        {
            if (!FoldoutStates.ContainsKey(_instanceId))
            {
                FoldoutStates[_instanceId] = new List<bool>();
            }
            if (_modifiersProp != null)
            {
                var list = FoldoutStates[_instanceId];
                while (list.Count < _modifiersProp.arraySize) list.Add(false);
                if (list.Count > _modifiersProp.arraySize) list.RemoveRange(_modifiersProp.arraySize, list.Count - _modifiersProp.arraySize);
            }
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return; // Asset deleted or in transient state
            EnsureStyles();
            // Show compile/build status & protect modifications
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorGUILayout.HelpBox("Unity is compiling scripts... Editing disabled.", MessageType.Info);
                Repaint();
                return;
            }

            try
            {
                serializedObject.Update(); // May throw if in assembly reload edge case
            }
            catch
            {
                return; // bail out safely
            }

            // Reacquire property if lost (domain reload or layout rebuild)
            if (_modifiersProp == null)
            {
                AcquireProperties();
            }

            EditorGUILayout.LabelField("Mesh Modifier", _headerStyle);
            EditorGUILayout.Space(2);

            if (_modifiersProp == null)
            {
                EditorGUILayout.HelpBox("'modifiers' list not found on object.", MessageType.Error);
                return;
            }

            // Guard against null list in the underlying object (can happen after domain reload if list not serialized yet)
            var mm = target as MeshModifier;
            if (mm != null && mm.modifiers == null)
            {
                Undo.RecordObject(mm, "Initialize Modifiers List");
                mm.modifiers = new System.Collections.Generic.List<MeshModifier.Modifier>();
                EditorUtility.SetDirty(mm);
                serializedObject.Update();
            }

            EnsureFoldoutList();

            using (new EditorGUILayout.VerticalScope(_boxedStyle))
            {
                EditorGUILayout.LabelField("Modifiers", EditorStyles.boldLabel);

                if (_modifiersProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No modifiers defined.", MessageType.Info);
                }

                // Draw each modifier entry
                for (int i = 0; i < _modifiersProp.arraySize; i++)
                {
                    var foldouts = FoldoutStates[_instanceId];
                    if (i >= foldouts.Count) foldouts.Add(false);

                    var element = _modifiersProp.GetArrayElementAtIndex(i);
                    string slotName = element.FindPropertyRelative("SlotName")?.stringValue ?? "(Unnamed)";

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        foldouts[i] = EditorGUILayout.Foldout(foldouts[i], $"Modifier {i + 1}: {slotName}", true, _foldoutStyle);

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button(new GUIContent("x", "Remove this modifier"), GUILayout.Width(20)))
                        {
                            RemoveModifierAt(i);
                            break; // collection changed
                        }
                    }

                    if (!foldouts[i]) continue;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUI.indentLevel++;
                        var slotNameProp = element.FindPropertyRelative("SlotName");
                        var dnaNameProp = element.FindPropertyRelative("DNAName");
                        var scaleProp = element.FindPropertyRelative("Scale");
#if UNITY_EDITOR
                        var modName = element.FindPropertyRelative("ModifierName");
                        if (modName != null)
                        {
                            EditorGUILayout.PropertyField(modName, new GUIContent("Modifier Name"));
                        }
#endif
                        EditorGUILayout.PropertyField(slotNameProp, new GUIContent("Slot Name"));
                        EditorGUILayout.PropertyField(dnaNameProp, new GUIContent("DNA Name"));

                        if (scaleProp != null)
                        {
                            float newScale = EditorGUILayout.Slider(new GUIContent("Scale"), scaleProp.floatValue, 0f, 5f);
                            if (!Mathf.Approximately(newScale, scaleProp.floatValue))
                            {
                                scaleProp.floatValue = Mathf.Clamp(newScale, 0f, 100f);
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Modifier"))
                    {
                        AddModifier();
                    }
                    GUILayout.FlexibleSpace();
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                if (mm != null) EditorUtility.SetDirty(mm);
            }
        }

        private void AddModifier()
        {
            if (_modifiersProp == null) return;
            int newIndex = _modifiersProp.arraySize;
            _modifiersProp.InsertArrayElementAtIndex(newIndex);
            var newElement = _modifiersProp.GetArrayElementAtIndex(newIndex);
            // Clear string fields & set defaults
            var slotName = newElement.FindPropertyRelative("SlotName");
            if (slotName != null) slotName.stringValue = string.Empty;
            var dnaName = newElement.FindPropertyRelative("DNAName");
            if (dnaName != null) dnaName.stringValue = string.Empty;
            var scale = newElement.FindPropertyRelative("Scale");
            if (scale != null) scale.floatValue = 1f;
            EnsureFoldoutList();
            if (FoldoutStates.TryGetValue(_instanceId, out var list))
            {
                while (list.Count < _modifiersProp.arraySize) list.Add(false);
                if (list.Count > 0) list[list.Count - 1] = true; // auto expand new
            }
        }

        private void RemoveModifierAt(int index)
        {
            if (_modifiersProp == null || index < 0 || index >= _modifiersProp.arraySize) return;
            _modifiersProp.DeleteArrayElementAtIndex(index);
            EnsureFoldoutList();
        }
    }
}