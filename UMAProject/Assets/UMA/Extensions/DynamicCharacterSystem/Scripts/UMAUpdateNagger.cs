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
		static bool naggerEnabled = true;

		static UMAUpdateNagger()
		{
			if (!Application.isPlaying && naggerEnabled)
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
				if (!Application.isPlaying)
				{
					var wardrobeToUpdate = UMAWardrobeRecipe.TestForOldRecipes();
					var dcaToUpdate = UMADynamicCharacterAvatarRecipe.TestForOldRecipes();
					if (wardrobeToUpdate > 0 || dcaToUpdate > 0)
					{
						UMANagWindow.wardrobeToUpdate = wardrobeToUpdate;
						UMANagWindow.dcaToUpdate = dcaToUpdate;
						UMANagWindow.ShowWindow();
					}
				}
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
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;

			wardrobeToUpdate = UMAWardrobeRecipe.TestForOldRecipes();
			dcaToUpdate = UMADynamicCharacterAvatarRecipe.TestForOldRecipes();

			EditorWindow.GetWindowWithRect<UMANagWindow>(new Rect(0f, 0f, 400f, 300f), true, "UMA Updater");
		}

		public static void ShowWindow()
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;

			EditorWindow.GetWindowWithRect<UMANagWindow>(new Rect(0f, 0f, 400f, 300f), true, "UMA Updater");
		}

		public void OnGUI()
		{
			bool didUpdate = false;
			if(wardrobeToUpdate > 0 || dcaToUpdate > 0)
			{
				GUILayout.Label("Please update your UMA Recipes", EditorStyles.boldLabel);
				if(wardrobeToUpdate > 0)
					EditorGUILayout.HelpBox(wardrobeToUpdate+" Wardrobe Recipes need updating.", MessageType.Warning);
				if(dcaToUpdate > 0)
					EditorGUILayout.HelpBox(dcaToUpdate +" DynamicCharacterAvatar Recipes need updating.", MessageType.Warning);
				EditorGUILayout.HelpBox("Please backup your project first.", MessageType.Info);
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
						postUpdateMessage += "You will need to update your UMAResourcesIndex and, ";
					}
					postUpdateMessage += "If you are using AssetBundles, you may need to rebuild your assetBundles ";
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
				/*EditorGUILayout.HelpBox("Just for testing. Click the button below so that the 'Update Recipes' button shows again.", MessageType.Warning);
				if (GUILayout.Button("ResetPrefs"))
				{
					EditorPrefs.SetBool(Application.dataPath + ":UMADCARecipesUpdated", false);
					EditorPrefs.SetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated", false);
				}*/
			}
		}
	}
}
#endif
