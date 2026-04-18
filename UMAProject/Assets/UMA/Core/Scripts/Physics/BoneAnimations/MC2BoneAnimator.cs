using UnityEngine;
using UMA;
using System.Collections.Generic;
#if MAGICACLOTH2
using MagicaCloth2;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
/* 
 * This script requires Magica Cloth 2 script to be installed in the project.
 * Magica Cloth 2 is a third-party asset available on the Unity Asset Store.
 * To use this script, ensure you have the Magica Cloth 2 package imported into your Unity project.
 * Also, the MC2 asmdef file should be added to the UMA_CORE asmdef file
 */

namespace UMA
{
    public class MC2BoneAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/MC2BoneAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<MC2BoneAnimator>();
        }
#endif
        [Header("General Settings")]
        [Tooltip("Add the root bone of each bone chain you want to animate. ")]
        public string[] AnimatedRootBoneNames;
        [SerializeField]
        [Tooltip("Add Magica CLoth2 preset file. If not set, the default settings will be used.")]
        private TextAsset presetFile;
        [Tooltip("Add the bones you want to exclude from the animated chains.")]
        public string[] BoneToExcludeNames;

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);
            initialized = true;

            if (AnimatedRootBoneNames == null || AnimatedRootBoneNames.Length == 0)
            {
                Debug.LogError("No animated root bone names specified. Please set AnimatedRootBoneNames in the inspector.");
                return;
            }


            for (int i = 0; i < AnimatedRootBoneNames.Length; i++)
            {
                string bone = AnimatedRootBoneNames[i];
                if (!string.IsNullOrEmpty(bone))
                {
                    Transform boneXform = umaData.skeleton.GetBoneTransform(bone);
                    AddMCBoneJiggle(umaData, boneXform);
                }
            }
        }

        public void AddMCBoneJiggle(UMAData umaData, Transform rootBone)
        {
            if (rootBone != null)
            {
#if MAGICACLOTH2
                // Check if rootBone already has MagicaCloth component and abort if it does
                if (rootBone.GetComponent<MagicaCloth>() != null) return;

                // Add MagicaCloth component to the root bone
                var cloth = rootBone.gameObject.AddComponent<MagicaCloth>();
                var sdata = cloth.SerializeData;
                var sdata2 = cloth.GetSerializeData2();

                // Setup bone cloth
                sdata.clothType = ClothProcess.ClothType.BoneCloth;
                sdata.rootBones.Add(rootBone.transform);
                if (presetFile != null)
                {
                    // If a preset file is provided, import the settings from it
                    sdata.ImportJson(presetFile.text);
                }

                // Exclude specified bones from animation by marking them as Fixed
                if (BoneToExcludeNames != null && BoneToExcludeNames.Length > 0)
                {

                    for (int i = 0; i < BoneToExcludeNames.Length; i++)
                    {
                        string boneName = BoneToExcludeNames[i];
                        if (!string.IsNullOrEmpty(boneName))
                        {
                            Transform boneToExclude = umaData.skeleton.GetBoneTransform(boneName);
                            if (boneToExclude != null)
                            {
                                sdata2.boneAttributeDict.Add(boneToExclude, VertexAttribute.Invalid);
                            }
                        }
                    }

                }

                // Build MagicaCloth2
                cloth.BuildAndRun();
#endif
            }
        }
    }
}