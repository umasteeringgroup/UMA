using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;

public class UMADynamicBoneJiggle : MonoBehaviour 
{
	[Header("General Settings")]
	public string jiggleBoneName;
	public List<string> exceptions;
	[Range(0,1)]
	public float reduceEffect;
	
	[Header("Removable Bone Settings")]
	public bool deleteBoneWithSlot;
	public string slotToWatch;
	private string linkedRecipe;


	public void AddJiggle(UMAData umaData)
	{
		Transform rootBone = SkeletonTools.RecursiveFindBone(umaData.umaRoot.transform, jiggleBoneName);
		UMABoneCleaner cleaner = umaData.gameObject.GetComponent<UMABoneCleaner>();
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

			foreach (string exception in exceptions)
			{
				exclusionList.Add(SkeletonTools.RecursiveFindBone(umaData.gameObject.transform,exception));
			}

			jiggleBone.Exclusions = exclusionList;
			jiggleBone.inertia = reduceEffect;
			jiggleBone.SetupBoneChains();
#endif
		}

		if (deleteBoneWithSlot)
		{
			if(cleaner == null)
				cleaner = umaData.gameObject.AddComponent<UMABoneCleaner>();
			
			UMAJiggleBoneListing listing = new UMAJiggleBoneListing();
			listing.boneName = jiggleBoneName;
			listing.carrierSlot = slotToWatch;
			
			linkedRecipe = umaData.gameObject.GetComponent<DynamicCharacterAvatar>().GetWardrobeItemName(slotToWatch);
			
			listing.recipe = linkedRecipe;

			listing.exceptions = exclusionList;
			cleaner.RegisterJiggleBone(listing);
		}
	}
}