using UMA;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public class UnitySpringJointAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/UnitySpringJointAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<UnitySpringJointAnimator>();
        }
#endif
        [Tooltip("The name of the root bone for this animator. This is the bone at the top of the chain that will be animated by this animator.")]
        public string AnchorBoneName;
        private Transform AnchorBone;

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);
            // Find Anchor Bone
            AnchorBone = umaData.skeleton.GetBoneTransform(AnchorBoneName);
            if (AnchorBone == null)
            {
                Debug.LogError($"Anchor bone '{AnchorBoneName}' not found in UMAData skeleton.");
                return;
            }
           // SetupSwingBones(SwingBoneNames);
            initialized = true;
        }


    }
}
