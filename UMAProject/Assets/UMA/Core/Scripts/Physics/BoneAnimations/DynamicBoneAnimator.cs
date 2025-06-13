using UnityEngine;
using UMA;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
/* 
 * This script requires DynamicBone script to be installed in the project.
 * DynamicBone is a third-party asset available on the Unity Asset Store.
 * To use this script, ensure you have the DynamicBone package imported into your Unity project.
 * As well, change the #if false below to #if true to enable the script.
 * If you do not have DynamicBone, you can use the SwayBoneAnimator scriptable object instead.
 */
namespace UMA
{
    public class DynamicBoneAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/DynamicBoneAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<DynamicBoneAnimator>();
        }
#endif
        [Header("General Settings")]
        [Tooltip("Add the root bone of each bone chain you want to animate. ")]
        public string[] AnimatedRootBoneNames;
        [Tooltip("List of bone names to exclude from the jiggle effect. These bones and their children will not be affected by the jiggle.")]
        public List<string> exceptions = new List<string>();
        [Range(0, 1)]
        public float reduceEffect;

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            if (AnimatedRootBoneNames == null || AnimatedRootBoneNames.Length == 0)
            {
                Debug.LogError("No animated root bone names specified. Please set AnimatedRootBoneNames in the inspector.");
                return;
            }
            base.Initialize(umaData, sd);
            for (int i = 0; i < AnimatedRootBoneNames.Length; i++)
            {
                string bone = AnimatedRootBoneNames[i];
                if (!string.IsNullOrEmpty(bone))
                {
                    Transform boneXform = umaData.skeleton.GetBoneTransform(bone);
                    AddBoneJiggle(umaData, boneXform);
                }
            }
        }

        public void AddBoneJiggle(UMAData umaData, Transform rootBone)
        {
            List<Transform> exclusionList = new List<Transform>();

            if (rootBone != null)
            {
#if false
			DynamicBone jiggleBone = rootBone.GetComponent<DynamicBone>();
			if(jiggleBone == null)
			{
				jiggleBone = rootBone.gameObject.AddComponent<DynamicBone>();
			}
			
			jiggleBone.m_Root = rootBone;
			

			
			foreach(string exception in exceptions)
			{
                string exclusionBone = exception.Trim();
                exclusionList.Add(umaData.skeleton.GetBoneTransform(exclusionBone));
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
        }
    }
}