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

        private void OnEnable()
        {
            _modifiersProp = serializedObject.FindProperty("modifiers");
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            _foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Mesh Modifier", _headerStyle);
            EditorGUILayout.Space(2);

            if (_modifiersProp == null)
            {
                EditorGUILayout.HelpBox("Modifiers list not found.", MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Modifiers", EditorStyles.boldLabel);

                if (_modifiersProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No modifiers defined.", MessageType.Info);
                }

                for (int i = 0; i < _modifiersProp.arraySize; i++)
                {
                    var element = _modifiersProp.GetArrayElementAtIndex(i);

                    // Pull out slot name first to avoid escaping quotes inside interpolation.
                    string slotName = element.FindPropertyRelative("SlotName").stringValue;

                    element.isExpanded = EditorGUILayout.Foldout(
                        element.isExpanded,
                        $"Modifier {i + 1}: {slotName}",
                        true,
                        _foldoutStyle);

                    if (!element.isExpanded) continue;

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
                        EditorGUILayout.Slider(scaleProp, 0f, 5f, new GUIContent("Scale"));

                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Modifier"))
                {
                    int newIndex = _modifiersProp.arraySize;
                    _modifiersProp.InsertArrayElementAtIndex(newIndex);
                    var newElement = _modifiersProp.GetArrayElementAtIndex(newIndex);
                    InitializeNewModifier(newElement);
                }

                if (_modifiersProp.arraySize > 0)
                {
                    if (GUILayout.Button("Remove Last"))
                    {
                        _modifiersProp.DeleteArrayElementAtIndex(_modifiersProp.arraySize - 1);
                    }
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }
        }

        private void InitializeNewModifier(SerializedProperty modifierProp)
        {
            modifierProp.FindPropertyRelative("SlotName")?.SetString(string.Empty);
            modifierProp.FindPropertyRelative("DNAName")?.SetString(string.Empty);
            var scale = modifierProp.FindPropertyRelative("Scale");
            if (scale != null) scale.floatValue = 1f;
        }
    }

}