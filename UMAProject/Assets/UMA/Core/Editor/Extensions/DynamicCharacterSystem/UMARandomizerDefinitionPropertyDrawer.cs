using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{


    [CustomPropertyDrawer(typeof(UMARandomizer.RandomizerDefinition))]
    public class RandomizerDefinitionPropertyDrawer : PropertyDrawer
    {
        const float IconPreviewSize = 64f;

        private static class Tooltips
        {
            internal static GUIContent Icon = new GUIContent("", "Optional : Add a icon");

            internal static GUIContent Name = new GUIContent("", "Optional : Define a functional name");

            internal static GUIContent Note = new GUIContent("", "Optional : Add a note/reminder");
        }


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {

            position.height = IconPreviewSize;
            EditorGUI.BeginProperty(position, label, property);

            // Don't make child fields be indented
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // ****************************

            Rect imageRect = new Rect(position.x, position.y, IconPreviewSize, IconPreviewSize);
            Rect nameRect = new Rect(imageRect.xMax, imageRect.y, position.width - imageRect.width - 5f, IconPreviewSize / 3f);
            Rect noteRect = new Rect(imageRect.xMax, nameRect.yMax, position.width - imageRect.width - 5f, IconPreviewSize * 2 / 3f);

            SerializedProperty icon = property.FindPropertyRelative("Icon");
            SerializedProperty name = property.FindPropertyRelative("Name");
            SerializedProperty note = property.FindPropertyRelative("Note");


            EditorGUI.ObjectField(imageRect, icon, typeof(Sprite), GUIContent.none);
            name.stringValue = EditorGUI.DelayedTextField(nameRect, name.stringValue);
            note.stringValue = EditorGUI.TextArea(noteRect, note.stringValue, EditorStyles.textArea);
            property.serializedObject.ApplyModifiedProperties();

            EditorGUI.LabelField(imageRect, Tooltips.Icon);
            EditorGUI.LabelField(nameRect, Tooltips.Name);
            EditorGUI.LabelField(noteRect, Tooltips.Note);
            // ****************************

            // Set indent back to what it was
            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            //Since you need two spaces for your two elements
            return IconPreviewSize;
        }
    }
}
