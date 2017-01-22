#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using UMA;

namespace UMAEditor
{
	/// <summary>
	/// Use this to nag users to perform certain actions when the latest UMA updates require it
	/// </summary>
	[InitializeOnLoad]
	public class UMAUpdateNagger
	{
		static bool naggerEnabled = false;

		static UMAUpdateNagger()
		{
			bool showNagger = false;
			if (!EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated") || !EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated"))
			{
				if (!EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated"))
				{
					//Debug.Log("[UMAUpdateNagger] UMADCARecipesUpdated was false");
					showNagger = true;
				}
				if (!EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated"))
				{
					//Debug.Log("[UMAUpdateNagger] UMADCARecipesUpdated was false");
					showNagger = true;
				}
			}
			else
			{
				var wardrobeToUpdate = UMAWardrobeRecipe.TestForOldRecipes();
				var dcsToUpdate = UMADynamicCharacterAvatarRecipe.TestForOldRecipes();
				if (wardrobeToUpdate > 0 || dcsToUpdate > 0)
					showNagger = true;
			}
			if (showNagger && naggerEnabled)
			{
				EditorApplication.delayCall += ShowAfterCompile;
           }
		}

		static void ShowAfterCompile()
		{
			EditorApplication.delayCall -= ShowAfterCompile;
			if (EditorApplication.isCompiling)
			{
				EditorApplication.delayCall += ShowAfterCompile;
			}
			else
			{
				UMANagWindow.ShowWindow();
			}
		}


	}
	public class UMANagWindow : EditorWindow
	{
		static int wardrobeToUpdate = 0;
		static int dcsToUpdate = 0;

		[UnityEditor.MenuItem("UMA/Utilities/Check Recipes Up To Date")]
		public static void TestAndShowWindow()
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode)
				return;

			wardrobeToUpdate = UMAWardrobeRecipe.TestForOldRecipes();
			dcsToUpdate = UMADynamicCharacterAvatarRecipe.TestForOldRecipes();

			EditorWindow.GetWindowWithRect<UMANagWindow>(new Rect(0f, 0f, 400f, 300f), true, "UMA Updater");
		}

		public static void ShowWindow()
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode)
				return;

			EditorWindow.GetWindowWithRect<UMANagWindow>(new Rect(0f, 0f, 400f, 300f), true, "UMA Updater");
		}

		public void OnGUI()
		{
			bool didUpdate = false;
			if (!EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated") || !EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated"))
			{
				GUILayout.Label("Please update your UMA Recipes", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("We have updated UMA and some of your recipes need updating. Please click the button below to update your recipes", MessageType.Warning);
				EditorGUILayout.HelpBox("Please backup your project first!", MessageType.Warning);
				if (GUILayout.Button("Update Recipes"))
				{
					if (!EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated"))
					{
						UMADynamicCharacterAvatarRecipe.ConvertOldDCARecipes();
						didUpdate = true;
					}
					if (!EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated"))
					{
						UMAWardrobeRecipe.ConvertOldWardrobeRecipes();
						didUpdate = true;
					}
				}
				if (didUpdate)
				{
					string postUpdateMessage = "All Recipes Updated! ";
					if (UMAResourcesIndex.Instance != null)
					{
						UMAResourcesIndex.Instance.ClearIndex();
						UMAResourcesIndex.Instance.IndexAllResources();
						Debug.Log("Updated UmaResourcesIndex with new assets");
					}
					else
					{
						postUpdateMessage += "You MUST update your UMAResourcesIndex and, ";
					}
					postUpdateMessage += "If you are using AssetBundles, you MUST rebuild your assetBundles ";
					postUpdateMessage += "in order for everything to work properly.";
					EditorUtility.DisplayDialog("Recipes updated!", postUpdateMessage, "OK");
					EditorWindow.GetWindow<UMANagWindow>().Close();
					wardrobeToUpdate = 0;
					dcsToUpdate = 0;
				}
				EditorGUILayout.Space();
				EditorGUILayout.Space();
				EditorGUILayout.Space();
			}
			else if(wardrobeToUpdate > 0 || dcsToUpdate > 0)
			{
				GUILayout.Label("Please update your UMA Recipes", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox(wardrobeToUpdate+" Wardrobe Recipes need updating.", MessageType.Warning);
				EditorGUILayout.HelpBox(dcsToUpdate +" DynamicCharacterAvatar Recipes need updating.", MessageType.Warning);
				EditorGUILayout.HelpBox("Please backup your project first!", MessageType.Warning);
				if (GUILayout.Button("Update Recipes"))
				{
					if (wardrobeToUpdate > 0)
					{
						UMADynamicCharacterAvatarRecipe.ConvertOldDCARecipes();
						didUpdate = true;
					}
					if (dcsToUpdate > 0)
					{
						UMAWardrobeRecipe.ConvertOldWardrobeRecipes();
						didUpdate = true;
					}
				}
				if (didUpdate)
				{
					string postUpdateMessage = "All Recipes Updated! ";
					if (UMAResourcesIndex.Instance != null)
					{
						UMAResourcesIndex.Instance.ClearIndex();
						UMAResourcesIndex.Instance.IndexAllResources();
						Debug.Log("Updated UmaResourcesIndex with new assets");
					}
					else
					{
						postUpdateMessage += "You MUST update your UMAResourcesIndex and, ";
					}
					postUpdateMessage += "If you are using AssetBundles, you MUST rebuild your assetBundles ";
					postUpdateMessage += "in order for everything to work properly.";
					EditorUtility.DisplayDialog("Recipes updated!", postUpdateMessage, "OK");
					EditorWindow.GetWindow<UMANagWindow>().Close();
				}
				EditorGUILayout.Space();
				EditorGUILayout.Space();
				EditorGUILayout.Space();
			}
			else
			{
				EditorGUILayout.HelpBox("No Recipes needed to be updated.", MessageType.Info);
				EditorGUILayout.HelpBox("Just for testing. Click the button below so that the 'Update Recipes' button shows again.", MessageType.Warning);
				if (GUILayout.Button("ResetPrefs"))
				{
					EditorPrefs.SetBool(Application.dataPath + ":UMADCARecipesUpdated", false);
					EditorPrefs.SetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated", false);
				}
			}
		}
	}
}
#endif
