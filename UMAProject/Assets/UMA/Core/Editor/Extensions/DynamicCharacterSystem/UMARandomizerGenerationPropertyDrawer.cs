using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomPropertyDrawer(typeof(UMARandomAvatarV2.CharacterGeneration))]
    public class RandomizerGenerationPropertyDrawer : PropertyDrawer
    {
        bool eventFoldout;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Don't make child fields be indented
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // ****************************
            var lwidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 120f;

            SerializedProperty Prefab = property.FindPropertyRelative("Prefab");
            SerializedProperty ParentObject = property.FindPropertyRelative("ParentObject");
            SerializedProperty ShowPlaceholder = property.FindPropertyRelative("ShowPlaceholder");
            SerializedProperty GenerateGrid = property.FindPropertyRelative("GenerateGrid");
            SerializedProperty GridXSize = property.FindPropertyRelative("GridXSize");
            SerializedProperty GridZSize = property.FindPropertyRelative("GridZSize");
            SerializedProperty GridDistance = property.FindPropertyRelative("GridDistance");
            SerializedProperty GridRandOffset = property.FindPropertyRelative("GridRandomOffset");
            SerializedProperty RandRotation = property.FindPropertyRelative("RandomRotation");
            SerializedProperty NameBase = property.FindPropertyRelative("NameBase");
            SerializedProperty RandomAvatarGenerated = property.FindPropertyRelative("RandomAvatarGenerated");

            EditorGUILayout.PropertyField(Prefab);
            EditorGUILayout.PropertyField(ParentObject);

            GUIHelper.BeginHorizontalPadded(0f, new Color(1f, 1f, 1f, 0f));
            ShowPlaceholder.boolValue = EditorGUILayout.ToggleLeft("Show Place Holder", ShowPlaceholder.boolValue);
            RandRotation.boolValue = EditorGUILayout.ToggleLeft("Apply Random Rotation", RandRotation.boolValue);
            GUIHelper.EndHorizontalPadded(0f);

            GUIHelper.BeginVerticalPadded(0f, GUIHelper.Colors.Grey);
            GenerateGrid.boolValue = EditorGUILayout.ToggleLeft("Generate Grid", GenerateGrid.boolValue);

            if (GenerateGrid.boolValue)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(GridXSize);
                EditorGUILayout.PropertyField(GridZSize);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(GridDistance);
                EditorGUILayout.PropertyField(GridRandOffset);
                EditorGUILayout.EndHorizontal();
            }
            GUIHelper.EndVerticalPadded(0f);

            EditorGUILayout.PropertyField(NameBase);

            EventsGUI(RandomAvatarGenerated);

            // ****************************
            property.serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.labelWidth = lwidth;
            // Set indent back to what it was
            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }

        private void EventsGUI(SerializedProperty RandomAvatarGenerated)
        {
            GUIHelper.BeginVerticalPadded(0f, GUIHelper.Colors.Grey);
            Rect eventsRect = EditorGUILayout.GetControlRect();
            eventsRect.xMin += 12f;
            eventFoldout = EditorGUI.Foldout(eventsRect, eventFoldout, "Events", true);

            if (eventFoldout)
                EditorGUILayout.PropertyField(RandomAvatarGenerated);

            GUIHelper.EndVerticalPadded(0f);
        }

        public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
        {
            return base.GetPropertyHeight(prop, label) - EditorGUIUtility.singleLineHeight;
        }
    }
}
