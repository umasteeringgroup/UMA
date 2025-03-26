using System.Collections.Generic;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;

namespace UMA
{
	public class UMADynamicBoneJiggle : MonoBehaviour
	{
		[Header("General Settings")]
		public string jiggleBoneName;
		public string[] AdditionalBones;
		public List<string> exceptions;
		[Range(0, 1)]
		public float reduceEffect;

		[Header("Removable Bone Settings")]
		public bool deleteBoneWithSlot;
		public string slotToWatch;
		private string linkedRecipe;
		public void AddJiggle(UMAData umaData)
		{
			UMABoneCleaner cleaner = umaData.gameObject.GetComponent<UMABoneCleaner>();
			Transform rootBone = SkeletonTools.RecursiveFindBone(umaData.umaRoot.transform, jiggleBoneName);
			AddBoneJiggle(umaData, rootBone, cleaner);
			if (AdditionalBones != null)
			{
				for (int i = 0; i < AdditionalBones.Length; i++)
				{
					string s = AdditionalBones[i];
					if (!string.IsNullOrEmpty(s))
					{
						rootBone = SkeletonTools.RecursiveFindBone(umaData.umaRoot.transform, s);
						AddBoneJiggle(umaData, rootBone, cleaner);
					}
				}
			}
		}

		public void AddBoneJiggle(UMAData umaData, Transform rootBone, UMABoneCleaner cleaner)
		{
			List<Transform> exclusionList = new List<Transform>();

			if (rootBone != null)
			{
#if DYNAMIC_BONE
			DynamicBone jiggleBone = rootBone.GetComponent<DynamicBone>();
			if(jiggleBone == null)
			{
				jiggleBone = rootBone.gameObject.AddComponent<DynamicBone>();
			}
			
			jiggleBone.m_Root = rootBone;
			

			
			foreach(string exception in exceptions)
			{
				exclusionList.Add(umaData.gameObject.transform.FindDeepChild(exception));
			}
			
			jiggleBone.m_Exclusions = exclusionList;
			jiggleBone.m_Inert = reduceEffect;
			jiggleBone.UpdateParameters();
#else
				SwayRootBone jiggleBone = rootBone.GetComponent<SwayRootBone>();
				if (jiggleBone == null)
				{
					jiggleBone = rootBone.gameObject.AddComponent<SwayRootBone>();
				}

				for (int i = 0; i < exceptions.Count; i++)
				{
					string exception = exceptions[i];
					exclusionList.Add(SkeletonTools.RecursiveFindBone(umaData.gameObject.transform, exception));
				}

				jiggleBone.Exclusions = exclusionList;
				jiggleBone.inertia = reduceEffect;
				jiggleBone.SetupBoneChains();
#endif
			}

			if (deleteBoneWithSlot)
			{
				if (cleaner == null)
				{
					cleaner = umaData.gameObject.AddComponent<UMABoneCleaner>();
				}

				UMAJiggleBoneListing listing = new UMAJiggleBoneListing();
				listing.boneName = jiggleBoneName;
				listing.carrierSlot = slotToWatch;

				linkedRecipe = umaData.gameObject.GetComponent<DynamicCharacterAvatar>().GetWardrobeItemName(slotToWatch);

				listing.recipe = linkedRecipe;

				listing.exceptions.AddRange(exclusionList);
				cleaner.RegisterJiggleBone(listing);
			}
		}
	}
}