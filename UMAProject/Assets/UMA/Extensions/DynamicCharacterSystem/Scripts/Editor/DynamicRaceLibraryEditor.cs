using UnityEngine;
using UnityEditor;

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
        dynamicallyAddFromResources.boolValue = EditorGUILayout.ToggleLeft("Dynamically Add From Resources", dynamicallyAddFromResources.boolValue);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("resourcesFolderPath"));
        dynamicallyAddFromAssetBundles.boolValue = EditorGUILayout.ToggleLeft("Dynamically Add From Asset Bundles", dynamicallyAddFromAssetBundles.boolValue);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("assetBundleNamesToSearch"));
        serializedObject.ApplyModifiedProperties();
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
