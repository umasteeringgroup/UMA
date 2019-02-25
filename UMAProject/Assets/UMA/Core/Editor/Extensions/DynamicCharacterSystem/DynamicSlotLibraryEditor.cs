using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
    [CustomEditor(typeof(DynamicSlotLibrary))]
    [CanEditMultipleObjects]
    public class DynamicSlotLibraryEditor : SlotLibraryEditor
    {
        //extra fields for Dynamic version;
        private DynamicSlotLibrary dSlotLibrary;
        public SerializedProperty dynamicallyAddFromResources;
        public SerializedProperty resourcesFolderPath;
        public SerializedProperty dynamicallyAddFromAssetBundles;
        public SerializedProperty assetBundleNamesToSearch;
        string slotInBundleToFind = "";

        public new void OnEnable()
        {
            dSlotLibrary = base.serializedObject.targetObject as DynamicSlotLibrary;
            base.OnEnable();
            //extra for dVersion
            dynamicallyAddFromResources = serializedObject.FindProperty("dynamicallyAddFromResources");
            resourcesFolderPath = serializedObject.FindProperty("resourcesFolderPath");
            dynamicallyAddFromAssetBundles = serializedObject.FindProperty("dynamicallyAddFromAssetBundles");
            assetBundleNamesToSearch = serializedObject.FindProperty("assetBundleNamesToSearch");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            //add our extra DynamicOverlayLibrary bits
            dynamicallyAddFromResources.boolValue = GUILayout.Toggle(dynamicallyAddFromResources.boolValue ? true : false, new GUIContent(" Dynamically add from Global Library", "If true this library will dynamically add any assets you have checked on in the UMA Global Library or which you have put in a Resources folder"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Global Library Folder Filter", "Limit the Global Library search to the following folders (no starting slash and seperate multiple entries with a comma)"), GUILayout.Width(135));
            resourcesFolderPath.stringValue = GUILayout.TextField(resourcesFolderPath.stringValue);
            GUILayout.EndHorizontal();
            dynamicallyAddFromAssetBundles.boolValue = GUILayout.Toggle(dynamicallyAddFromAssetBundles.boolValue ? true : false, " Dynamically add from AssetBundles");
            GUILayout.BeginHorizontal();
            GUILayout.Label("AssetBundles to Search", GUILayout.Width(135));
            assetBundleNamesToSearch.stringValue = GUILayout.TextField(assetBundleNamesToSearch.stringValue);
            GUILayout.EndHorizontal();
            if (Application.isPlaying && dynamicallyAddFromAssetBundles.boolValue)
            {
                EditorGUILayout.Space();
                slotInBundleToFind = EditorGUILayout.TextField(slotInBundleToFind);
                if (GUILayout.Button("Find Slot's AssetBundle"))
                {
                    if (slotInBundleToFind != "")
                        dSlotLibrary.GetOriginatingAssetBundle(slotInBundleToFind);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}