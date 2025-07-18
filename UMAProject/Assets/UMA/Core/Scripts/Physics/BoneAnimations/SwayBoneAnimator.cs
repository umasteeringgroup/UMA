using UMA;
using UnityEngine;
using UnityEngine.EventSystems;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public class SwayBoneAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/SwayBoneAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<SwayBoneAnimator>();
        }
#endif
        [Tooltip("The name of the root bone for this animator. This is the bone at the top of the chain that will be animated by this animator.")]
        public string AnchorBoneName;

        private SwayRootBone swayRootBone;
        [Range(0.0f, 1.0f)]
        [Tooltip("How much inertia each bone has - makes it more bouncy")]
        public float inertia = 0.75f;  // how much the force slows each second.

        [Range(1.0f, 2.0f)]
        [Tooltip("How far something can stretch - 1.0 = no stretch")]
        public float limit = 2.0f;

        [Range(1.0f, 4.0f)]
        [Tooltip("How much it can pull away during movement")]
        public float elasticity = 2.0f;
        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);
            // Find the anchor bone transform
            Transform anchorTransform = umaData.skeleton.GetBoneTransform(AnchorBoneName);
            if (!anchorTransform)
            {
                Debug.LogError($"Anchor bone '{AnchorBoneName}' not found in UMA skeleton.");
                return;
            }
            swayRootBone = anchorTransform.GetComponent<SwayRootBone>();
            if (swayRootBone == null)
            {
                swayRootBone = anchorTransform.gameObject.AddComponent<SwayRootBone>();

            }
            swayRootBone.Setup(elasticity, inertia, limit);
            swayRootBone.enabled = true;
            initialized = true;
        }

        public override void DoUpdate(UMAData umaData, float step)
        {
            if (!initialized)
            {
                return;
            }
            swayRootBone.UpdateRootBone(step);
        }
    }
}