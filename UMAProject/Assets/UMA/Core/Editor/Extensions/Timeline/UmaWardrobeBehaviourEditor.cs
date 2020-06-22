#if UNITY_2017_1_OR_NEWER
using UnityEngine;
using UnityEditor;
using UMA.Timeline;

namespace UMA.Editors
{
    [CustomPropertyDrawer(typeof(UmaWardrobeBehaviour))]
    public class UmaWardrobeBehaviourEditor : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int fieldCount = 25; // todo: get this dynamically
            return fieldCount * EditorGUIUtility.singleLineHeight * EditorGUIUtility.pixelsPerPoint;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty wardrobeOption = property.FindPropertyRelative("wardrobeOption");
            SerializedProperty recipesToAdd = property.FindPropertyRelative("recipesToAdd");
            SerializedProperty slotsToClear = property.FindPropertyRelative("slotsToClear");
            SerializedProperty rebuildImmediately = property.FindPropertyRelative("rebuildImmediately");

            Rect singleFieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(singleFieldRect, rebuildImmediately);

            singleFieldRect.y += EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(singleFieldRect, wardrobeOption);

            if (wardrobeOption.enumValueIndex == (int)UmaWardrobeBehaviour.WardrobeOptions.AddRecipes)
            {
                singleFieldRect.y += EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(singleFieldRect, recipesToAdd, true);
            }

            if (wardrobeOption.enumValueIndex == (int)UmaWardrobeBehaviour.WardrobeOptions.ClearSlots)
            {
                singleFieldRect.y += EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(singleFieldRect, slotsToClear, true);
            }

            if (wardrobeOption.enumValueIndex == (int)UmaWardrobeBehaviour.WardrobeOptions.ClearAllSlots)
            {
            }
        }
    }
}
#endif