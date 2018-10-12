using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace UMA.Editors
{ 
    [CustomEditor(typeof(ColorDnaAsset))]
    public class ColorDnaAssetEditor : Editor
    {
        private SerializedProperty _dnaTypeHash;
        private ReorderableList _list;
        private bool _isEditingDnaHash = false;

        private void OnEnable()
        {
            _dnaTypeHash = serializedObject.FindProperty("dnaTypeHash");
            _list = new ReorderableList(serializedObject, serializedObject.FindProperty("colorSets"), true, true, true, true);
            _list.drawElementCallback = DrawElement;
            _list.elementHeight = 6f + ((EditorGUIUtility.singleLineHeight + 2f) * 5f);
            _list.drawHeaderCallback = (Rect rect) => { EditorGUI.LabelField(rect, "Color Sets"); };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_isEditingDnaHash)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.PropertyField(_dnaTypeHash);
                if (GUILayout.Button("Save", GUILayout.MaxWidth(80)))
                    _isEditingDnaHash = false;

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("Dna Type Hash", _dnaTypeHash.intValue.ToString());
                if (GUILayout.Button("Edit", GUILayout.MaxWidth(80)))
                    _isEditingDnaHash = true;

                EditorGUILayout.EndHorizontal();
            }

            _list.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = _list.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += 4;
            EditorGUI.PrefixLabel(new Rect(rect.x, rect.y, 60, EditorGUIUtility.singleLineHeight), new GUIContent("Dna Entry Name"));
            EditorGUI.PropertyField(new Rect(rect.x + 140, rect.y, 180, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("dnaEntryName"), GUIContent.none);

            rect.y += (2 + EditorGUIUtility.singleLineHeight);
            EditorGUI.PrefixLabel(new Rect(rect.x, rect.y, 60, EditorGUIUtility.singleLineHeight), new GUIContent("Overlay Entry Name"));
            EditorGUI.PropertyField(new Rect(rect.x + 140, rect.y, 180, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("overlayEntryName"), GUIContent.none);

            rect.y += (2 + EditorGUIUtility.singleLineHeight);
            EditorGUI.PrefixLabel(new Rect(rect.x, rect.y, 60, EditorGUIUtility.singleLineHeight), new GUIContent("Color Channel"));
            EditorGUI.PropertyField(new Rect(rect.x + 140, rect.y, 30, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("colorChannel"), GUIContent.none);

            rect.y += (2 + EditorGUIUtility.singleLineHeight);
            EditorGUI.PrefixLabel(new Rect(rect.x, rect.y, 60, EditorGUIUtility.singleLineHeight), new GUIContent("Min Color"));
            EditorGUI.ColorField(new Rect(rect.x + 140, rect.y, 60, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("minColor").colorValue);

            rect.y += (2 + EditorGUIUtility.singleLineHeight);
            EditorGUI.PrefixLabel(new Rect(rect.x, rect.y, 60, EditorGUIUtility.singleLineHeight), new GUIContent("Max Color"));
            EditorGUI.ColorField(new Rect(rect.x + 140, rect.y, 60, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("maxColor").colorValue);
        }
    }
}
