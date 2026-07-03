#if UNITY_EDITOR

using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMABoneBakingSkeletonTests
    {
        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void ResetAllRestoresAuthoredBaselineBeforeRelativeRigEffects()
        {
            GameObject rootObject = CreateSkeletonObject();
            try
            {
                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                int hipsHash = UMAUtils.StringToHash("Hips");
                Quaternion delta = Quaternion.Euler(0f, 10f, 0f);

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                skeleton.SetRotationRelative(hipsHash, delta, 1f);
                Quaternion firstRotation = skeleton.GetRotation(hipsHash);

                skeleton.ResetAll();
                skeleton.SetRotationRelative(hipsHash, delta, 1f);
                Quaternion secondRotation = skeleton.GetRotation(hipsHash);
                skeleton.EndSkeletonUpdate();

                Assert.Less(Quaternion.Angle(firstRotation, secondRotation), 0.0001f,
                    "Bone Baking skeleton reset must restore the authored pre-DNA baseline before relative DNA operations run again.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void CachedMatricesRefreshWhenParentPoseChangesInSameUpdate()
        {
            GameObject rootObject = CreateSkeletonObject();
            try
            {
                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                int hipsHash = UMAUtils.StringToHash("Hips");
                int headHash = UMAUtils.StringToHash("Head");

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                skeleton.GetLocalToWorldMatrix(headHash);
                skeleton.SetPosition(hipsHash, new Vector3(1f, 0f, 0f));
                Vector3 headPosition = skeleton.GetLocalToWorldMatrix(headHash).MultiplyPoint(Vector3.zero);
                skeleton.EndSkeletonUpdate();

                Assert.AreEqual(1f, headPosition.x, 0.0001f,
                    "Changing a parent bone must invalidate descendant cached matrices in the same skeleton update.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void PreservedBonesMarkedAfterResetAreUsedAsMergeTargets()
        {
            GameObject rootObject = CreateSkeletonObject();
            try
            {
                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                int hipsHash = UMAUtils.StringToHash("Hips");
                int headHash = UMAUtils.StringToHash("Head");

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                skeleton.SetAnimatedBone(skeleton.rootBoneHash);
                skeleton.SetAnimatedBone(hipsHash);
                int resolvedHash = skeleton.ResolvePreservedHash(headHash);
                skeleton.EndSkeletonUpdate();

                Assert.AreEqual(hipsHash, resolvedHash,
                    "Bone Baking must mark preserved bones after the final ResetAll so MergeSkeletons does not collapse every bone to the root.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void BoneScaleDNAUsesSkeletonCacheInsteadOfStaleLiveTransform()
        {
            GameObject rootObject = CreateSkeletonObject();
            GameObject avatarObject = new GameObject("UMA_BoneBakingDNAEffectTest");
            try
            {
                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                var umaData = avatarObject.AddComponent<UMAData>();
                umaData.skeleton = skeleton;
                int hipsHash = UMAUtils.StringToHash("Hips");

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                rootObject.transform.GetChild(0).localScale = new Vector3(3f, 3f, 3f);

                var effect = new DNAEffect_BoneScale
                {
                    BoneName = "Hips",
                    ScaleFactor = new Vector3(1f, 0f, 0f),
                    minMapping = 0f,
                    maxMapping = 1f,
                    curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
                };
                effect.Apply(umaData, null, 1f);
                Vector3 scale = skeleton.GetScale(hipsHash);
                skeleton.EndSkeletonUpdate();

                Assert.AreEqual(2f, scale.x, 0.0001f,
                    "Bone Baking DNA effects must read the reset skeleton cache, not stale live Transforms from the previous build.");
            }
            finally
            {
                Object.DestroyImmediate(avatarObject);
                Object.DestroyImmediate(rootObject);
            }
        }

        private static GameObject CreateSkeletonObject()
        {
            var rootObject = new GameObject("Global");
            var hipsObject = new GameObject("Hips");
            var headObject = new GameObject("Head");

            hipsObject.transform.SetParent(rootObject.transform, false);
            headObject.transform.SetParent(hipsObject.transform, false);
            headObject.transform.localPosition = new Vector3(0f, 1f, 0f);

            return rootObject;
        }
    }
}

#endif
