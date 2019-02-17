using UnityEngine;
using System.Collections;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA.Examples
{
	public class UMASlotVerifyWizard : MonoBehaviour
	{
		GameObject RaceGO = null;
		SkinnedMeshRenderer RaceSMR = null;
		GameObject SlotGO;
		SkinnedMeshRenderer SlotSMR;
		public GameObject[] Pages;
		public int page;
		public Text resultText;
		private Object slotAsset = null;
		public Button ForceButton;
		private bool forcedSlotBones;
		string slotAssetPath;

		private void NextPage()
		{
			SetPage(page + 1);
		}

		private void SetPage(int newPage)
		{
			Pages[page].SetActive(false);
			page = newPage;
			Pages[page].SetActive(true);
		}

		#region Page 1

		public void SelectMaleClick()
		{
#if UNITY_EDITOR
			string[] assets = AssetDatabase.FindAssets("male_unified t:Model");
			string path="";
			foreach (string guid in assets)
			{
				string thePath = AssetDatabase.GUIDToAssetPath(guid);
				if (thePath.ToLower().Contains("female"))
					continue;
				path = thePath;
				break;
			}
			if (string.IsNullOrEmpty(path))
			{
				return;
			}

			SetBaseMesh(path);
#endif
		}
		public void SelectFemaleClick()
		{
#if UNITY_EDITOR
			string[] assets = AssetDatabase.FindAssets("female_unified t:Model");
			if (assets.Length < 1) return;
			string thePath = AssetDatabase.GUIDToAssetPath(assets[0]);
			SetBaseMesh(thePath);
#endif
		}
		public void BrowseBaseMeshClick()
		{
#if UNITY_EDITOR
			var assetPath = EditorUtility.OpenFilePanel("Select Asset", "Assets", "fbx");
			if (string.IsNullOrEmpty(assetPath)) return;
			SetBaseMesh(assetPath);
#endif
		}
#if UNITY_EDITOR
		private void SetBaseMesh(string assetPath)
		{
			var curDir = System.IO.Directory.GetCurrentDirectory().Replace('\\', '/');
			if (assetPath.StartsWith(curDir, System.StringComparison.InvariantCultureIgnoreCase))
			{
				assetPath = assetPath.Remove(0, curDir.Length + 1);
			}
			var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (asset is GameObject)
			{
				RaceGO = Instantiate(asset) as GameObject;
				RaceSMR = RaceGO.GetComponentInChildren<SkinnedMeshRenderer>();
				if (RaceSMR != null)
				{
					if (SlotSMR != null)
					{
						PerformValidation();
					}
					else
					{
						NextPage();
					}
				}
				else
				{
					UMAUtils.DestroySceneObject(RaceGO);
				}
			}
		}
#endif
		#endregion

		#region Page 2

		public void BrowseSlotMeshClick()
		{
#if UNITY_EDITOR
			var assetPath = EditorUtility.OpenFilePanel("Select Asset", "Assets", "fbx");
			if (string.IsNullOrEmpty(assetPath)) return;
			SetSlotMesh(assetPath);
#endif
		}

#if UNITY_EDITOR
		private void SetSlotMesh(string assetPath)
		{
			var curDir = System.IO.Directory.GetCurrentDirectory().Replace('\\', '/');
			if (assetPath.StartsWith(curDir, System.StringComparison.InvariantCultureIgnoreCase))
			{
				assetPath = assetPath.Remove(0, curDir.Length+1);
			}
			slotAssetPath = assetPath;
			slotAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (slotAsset is GameObject)
			{
				SlotGO = Instantiate(slotAsset) as GameObject;
				SlotSMR = SlotGO.GetComponentInChildren<SkinnedMeshRenderer>();
				if (SlotSMR != null)
				{
					PerformValidation();
				}
				else
				{
					UMAUtils.DestroySceneObject(SlotGO);
				}
			}
		}

		private void PerformValidation()
		{
			string ValidateDescription;
			var validateResult = SkeletonTools.ValidateSlot(RaceSMR, SlotSMR, out ValidateDescription);
			resultText.text = ValidateDescription;
			switch (validateResult)
			{
				case SkeletonTools.ValidateResult.InvalidScale:
					Selection.activeObject = slotAsset;
					ForceButton.gameObject.SetActive(false);
					break;
				case SkeletonTools.ValidateResult.Ok:
					ForceButton.gameObject.SetActive(false);
					break;
				case SkeletonTools.ValidateResult.SkeletonProblem:
					ForceButton.gameObject.SetActive(true);
					break;
			}
			SetPage(2);
		}
#endif
		#endregion

		#region Page 3
		public void SelectNewBaseMesh()
		{
			if (forcedSlotBones)
			{
				UMAUtils.DestroySceneObject(SlotGO);
				SlotGO = Instantiate(slotAsset) as GameObject;
				SlotSMR = SlotGO.GetComponentInChildren<SkinnedMeshRenderer>();
				forcedSlotBones = false;
			}
			UMAUtils.DestroySceneObject(RaceGO);
			SetPage(0);
		}

		public void SelectNewSlotMesh()
		{
			forcedSlotBones = false;
			UMAUtils.DestroySceneObject(SlotGO);
			SetPage(1);
		}

		public void ForceSkeleton()
		{
			forcedSlotBones = true;
			SkeletonTools.ForceSkeleton(RaceSMR, SlotSMR);
			ForceButton.gameObject.SetActive(false);
#if UNITY_EDITOR			
			if (slotAssetPath.EndsWith(".fbx", System.StringComparison.InvariantCultureIgnoreCase))
			{
#if UNITY_2018_3_OR_NEWER
				PrefabUtility.SaveAsPrefabAsset(SlotGO, AssetDatabase.GenerateUniqueAssetPath(slotAssetPath.Substring(0, slotAssetPath.Length - 4) + ".prefab"));
#else
				PrefabUtility.CreatePrefab(AssetDatabase.GenerateUniqueAssetPath(slotAssetPath.Substring(0, slotAssetPath.Length-4)+".prefab"), SlotGO);
#endif
			}
#endif
		}
		#endregion
	}
}