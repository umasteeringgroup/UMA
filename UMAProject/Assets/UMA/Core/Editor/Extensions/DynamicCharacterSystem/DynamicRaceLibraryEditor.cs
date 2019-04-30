using UnityEngine;
using UnityEditor;

namespace UMA.CharacterSystem.Editors
{
    [CustomEditor(typeof(DynamicRaceLibrary))]
    [CanEditMultipleObjects]
    public class DynamicRaceLibraryEditor : Editor
    {
        private SerializedObject m_Object;
        private DynamicRaceLibrary raceLibrary;
        string raceInBundleToFind = "";

        public void OnEnable()
        {
            m_Object = new SerializedObject(target);
            raceLibrary = m_Object.targetObject as DynamicRaceLibrary;

        }

        public override void OnInspectorGUI()
        {
            Editor.DrawPropertiesExcluding(serializedObject, new string[] { "dynamicallyAddFromResources", "resourcesFolderPath", "dynamicallyAddFromAssetBundles", "assetBundleNamesToSearch" });
            serializedObject.ApplyModifiedProperties();
            SerializedProperty dynamicallyAddFromResources = serializedObject.FindProperty("dynamicallyAddFromResources");
            SerializedProperty dynamicallyAddFromAssetBundles = serializedObject.FindProperty("dynamicallyAddFromAssetBundles");
			EditorGUI.BeginChangeCheck();
			dynamicallyAddFromResources.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(" Dynamically add from Global Library", "If true this library will dynamically add any assets you have checked on in the UMA Global Library or which you have put in a Resources folder"), dynamicallyAddFromResources.boolValue);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("resourcesFolderPath"), new GUIContent("Global Library Folder Filter", "Limit the Global Library search to the following folders (no starting slash and seperate multiple entries with a comma)"));
            dynamicallyAddFromAssetBundles.boolValue = EditorGUILayout.ToggleLeft(" Dynamically Add From Asset Bundles", dynamicallyAddFromAssetBundles.boolValue);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("assetBundleNamesToSearch"), new GUIContent("AssetBundles to Search"));
			if (EditorGUI.EndChangeCheck())
			{
				raceLibrary.ClearEditorAddedAssets();
				serializedObject.ApplyModifiedProperties();
			}
            if (Application.isPlaying && dynamicallyAddFromAssetBundles.boolValue)
            {
                EditorGUILayout.Space();
                raceInBundleToFind = EditorGUILayout.TextField(raceInBundleToFind);
                if (GUILayout.Button("Find Races's AssetBundle"))
                {
                    if (raceInBundleToFind != "")
                    {
                        Debug.Log("Trying");
                        raceLibrary.GetOriginatingAssetBundle(raceInBundleToFind);
                    }
                }
            }
        }
    }
}
