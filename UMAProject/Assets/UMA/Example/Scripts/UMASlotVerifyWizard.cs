using UnityEngine;
using System.Collections;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	public class UMASlotVerifyWizard : MonoBehaviour
	{
		GameObject RaceGO;
		SkinnedMeshRenderer RaceSMR;
		GameObject SlotGO;
		SkinnedMeshRenderer SlotSMR;
		public GameObject[] Pages;
		public int page;
		public Text resultText;
		private Object slotAsset;
		public Button ForceButton;
		private bool forcedSlotBones;

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
			SetBaseMesh("Assets/UMA/Content/UMA/Humanoid/FBX/Male/Male_Unified.fbx");
#endif
		}
		public void SelectFemaleClick()
		{
#if UNITY_EDITOR
			SetBaseMesh("Assets/UMA/Content/UMA/Humanoid/FBX/Female/Female_Unified.fbx");
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
					Destroy(RaceGO);
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
					Destroy(SlotGO);
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
				Destroy(SlotGO);
				SlotGO = Instantiate(slotAsset) as GameObject;
				SlotSMR = SlotGO.GetComponentInChildren<SkinnedMeshRenderer>();
				forcedSlotBones = false;
			}
			Destroy(RaceGO);
			SetPage(0);
		}

		public void SelectNewSlotMesh()
		{
			forcedSlotBones = false;
			Destroy(SlotGO);
			SetPage(1);
		}

		public void ForceSkeleton()
		{
			forcedSlotBones = true;
			SkeletonTools.ForceSkeleton(RaceSMR, SlotSMR);
			ForceButton.gameObject.SetActive(false);
		}
		#endregion

	}
}