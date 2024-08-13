using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAColorScheme))]
    public class ColorSchemeAssetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            UMAColorScheme colorScheme = target as UMAColorScheme;
            int previousChannelCount = colorScheme.ColorData.channelCount;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Description"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UserObject"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ColorData"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }

            if (previousChannelCount != colorScheme.ColorData.channelCount)
            {
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
                Repaint();
            }
            if (GUILayout.Button("Save Now"))
            {
                Save();
            }

        }

        public void Save()
        {
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            serializedObject.Update();
        }

        [MenuItem("Assets/Create/UMA/Core/ColorScheme")]
        public static void CreateRaceMenuItem()
        {
            CustomAssetUtility.CreateAsset<UMAColorScheme>();
        }
    }
}
