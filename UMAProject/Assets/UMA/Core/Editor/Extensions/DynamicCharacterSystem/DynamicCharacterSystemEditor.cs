using UnityEngine;
using UnityEditor;

namespace UMA.CharacterSystem.Editors
{
	[CustomEditor(typeof(DynamicCharacterSystem))]
	[CanEditMultipleObjects]
	public class DynamicCharacterSystemEditor : Editor
	{
		private SerializedObject m_Object;
		private DynamicCharacterSystem dCharacterSystem;
		string recipeInBundleToFind = "";

		public void OnEnable()
		{
			m_Object = new SerializedObject(target);
			dCharacterSystem = m_Object.targetObject as DynamicCharacterSystem;
		}

		public override void OnInspectorGUI(){
			Editor.DrawPropertiesExcluding (serializedObject, new string[] { "dynamicallyAddFromResources", "resourcesCharactersFolder", "resourcesRecipesFolder", "dynamicallyAddFromAssetBundles", "assetBundlesForCharactersToSearch", "assetBundlesForRecipesToSearch", "addAllRecipesFromDownloadedBundles" });
			serializedObject.ApplyModifiedProperties ();
			SerializedProperty dynamicallyAddFromResources = serializedObject.FindProperty("dynamicallyAddFromResources");
			SerializedProperty dynamicallyAddFromAssetBundles = serializedObject.FindProperty("dynamicallyAddFromAssetBundles");
			SerializedProperty addAllRecipesFromDownloadedBundles = serializedObject.FindProperty("addAllRecipesFromDownloadedBundles");
			dynamicallyAddFromResources.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(" Dynamically add from Global Library", "If true this library will dynamically add any assets you have checked on in the UMA Global Library or which you have put in a Resources folder"), dynamicallyAddFromResources.boolValue);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("resourcesCharactersFolder"), new GUIContent("Global Library Characters Folder Filter", "Limit the Global Library search to the following folders (no starting slash and seperate multiple entries with a comma)"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("resourcesRecipesFolder"), new GUIContent("Global Library Recipes Folder Filter", "Limit the Global Library search to the following folders (no starting slash and seperate multiple entries with a comma)"));
			dynamicallyAddFromAssetBundles.boolValue = EditorGUILayout.ToggleLeft(" Dynamically add from AssetBundles", dynamicallyAddFromAssetBundles.boolValue);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("assetBundlesForCharactersToSearch"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("assetBundlesForRecipesToSearch"));
			addAllRecipesFromDownloadedBundles.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(" Add all recipes from downloaded bundles", "If true will automatically scan and add all Recipes from any downloaded bundles."), addAllRecipesFromDownloadedBundles.boolValue);
			serializedObject.ApplyModifiedProperties();
			if (Application.isPlaying && dynamicallyAddFromAssetBundles.boolValue) {
				EditorGUILayout.Space ();
				recipeInBundleToFind = EditorGUILayout.TextField (recipeInBundleToFind);
				if (GUILayout.Button ("Find Recipes's AssetBundle")) {
					if (recipeInBundleToFind != "")
                    {
                        dCharacterSystem.GetOriginatingAssetBundle (recipeInBundleToFind);
                    }
                }
			}
		}
	}
}
