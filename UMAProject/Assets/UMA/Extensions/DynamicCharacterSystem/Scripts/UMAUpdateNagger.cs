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
	public class UMAUpdateNagger : UnityEditor.AssetModificationProcessor
	{
		static bool naggerEnabled = true;
		static int wardrobeToUpdate = 0;
		static int dcaToUpdate = 0;

		static UMAUpdateNagger()
		{
			bool showNagger = false;
			wardrobeToUpdate = UMAWardrobeRecipe.TestForOldRecipes();
			dcaToUpdate = UMADynamicCharacterAvatarRecipe.TestForOldRecipes();
			if (wardrobeToUpdate > 0 || dcaToUpdate > 0)
				showNagger = true;
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
				UMANagWindow.wardrobeToUpdate = wardrobeToUpdate;
				UMANagWindow.dcaToUpdate = dcaToUpdate;
                UMANagWindow.ShowWindow();
				dcaToUpdate = 0;
				wardrobeToUpdate = 0;
			}
		}


	}
	public class UMANagWindow : EditorWindow
	{
		public static int wardrobeToUpdate = 0;
		public static int dcaToUpdate = 0;

		[UnityEditor.MenuItem("UMA/Utilities/Check Recipes Up To Date")]
		public static void TestAndShowWindow()
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode)
				return;

			wardrobeToUpdate = UMAWardrobeRecipe.TestForOldRecipes();
			dcaToUpdate = UMADynamicCharacterAvatarRecipe.TestForOldRecipes();

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
			if(wardrobeToUpdate > 0 || dcaToUpdate > 0)
			{
				GUILayout.Label("Please update your UMA Recipes", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox(wardrobeToUpdate+" Wardrobe Recipes need updating.", MessageType.Warning);
				EditorGUILayout.HelpBox(dcaToUpdate +" DynamicCharacterAvatar Recipes need updating.", MessageType.Warning);
				EditorGUILayout.HelpBox("Please backup your project first!", MessageType.Warning);
				if (GUILayout.Button("Update Recipes"))
				{
					if (wardrobeToUpdate > 0)
					{
						UMAWardrobeRecipe.ConvertOldWardrobeRecipes();
						didUpdate = true;
						wardrobeToUpdate = 0;
					}
					if (dcaToUpdate > 0)
					{
						UMADynamicCharacterAvatarRecipe.ConvertOldDCARecipes();
						didUpdate = true;
						dcaToUpdate = 0;
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
