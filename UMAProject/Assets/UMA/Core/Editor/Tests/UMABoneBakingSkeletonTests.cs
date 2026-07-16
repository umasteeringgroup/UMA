#if UNITY_EDITOR

using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMABoneBakingSkeletonTests
    {
        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("BoneBaking")]
        public void DefaultBoneBakingCombinerUsesDefaultCombinerPipeline()
        {
            var gameObject = new GameObject("UMA_DefaultBoneBakingCombinerTest");
            try
            {
                var combiner = gameObject.AddComponent<UMADefaultBoneBakingMeshCombiner>();

                Assert.IsInstanceOf<UMADefaultMeshCombiner>(combiner);
                Assert.IsInstanceOf<UMAMeshCombiner>(combiner);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("BoneBaking")]
        public void LegacyBoneBakingCombinerRemainsCompatibleWithDefaultBoneBaking()
        {
            var gameObject = new GameObject("UMA_BoneBakingCompatibilityCombinerTest");
            try
            {
                var combiner = gameObject.AddComponent<UMABoneBakingMeshCombiner>();

                Assert.IsInstanceOf<UMADefaultBoneBakingMeshCombiner>(combiner);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

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

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void CachedBakeMatrixReadDoesNotResetLiveAnimatedTransform()
        {
            GameObject rootObject = CreateSkeletonObject();
            try
            {
                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                Transform hips = rootObject.transform.GetChild(0);
                int hipsHash = UMAUtils.StringToHash("Hips");
                Quaternion animatedRotation = Quaternion.Euler(12f, 34f, 5f);
                hips.localRotation = animatedRotation;

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                skeleton.SetAnimatedBone(skeleton.rootBoneHash);
                skeleton.SetAnimatedBone(hipsHash);
                skeleton.GetLocalToWorldMatrix(hipsHash);
                skeleton.EndSkeletonUpdate();

                Assert.Less(Quaternion.Angle(animatedRotation, hips.localRotation), 0.0001f,
                    "Reading cached post-DNA matrices for mesh baking must not overwrite a live animated pose.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void RegisteredAnimatedBonesSurviveGeneratorShapeReset()
        {
            GameObject rootObject = CreateSkeletonObject();
            GameObject avatarObject = new GameObject("UMA_BoneBakingPreservationTest");
            try
            {
                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                var umaData = avatarObject.AddComponent<UMAData>();
                umaData.skeleton = skeleton;
                int hipsHash = UMAUtils.StringToHash("Hips");
                int headHash = UMAUtils.StringToHash("Head");

                umaData.ResetAnimatedBones();
                umaData.RegisterAnimatedBone(hipsHash);

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                umaData.RestoreRegisteredAnimatedBones();
                int resolvedHash = skeleton.ResolvePreservedHash(headHash);
                skeleton.EndSkeletonUpdate();

                Assert.AreEqual(hipsHash, resolvedHash,
                    "The generator shape pass must restore the combiner's preserved-bone set after ResetAll.");
            }
            finally
            {
                Object.DestroyImmediate(avatarObject);
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void RemovingBakedParentReparentsEveryPreservedChild()
        {
            var rootObject = new GameObject("Global");
            var helperObject = new GameObject("Helper");
            var firstChild = new GameObject("FirstAnimatedChild");
            var secondChild = new GameObject("SecondAnimatedChild");
            try
            {
                helperObject.transform.SetParent(rootObject.transform, false);
                firstChild.transform.SetParent(helperObject.transform, false);
                secondChild.transform.SetParent(helperObject.transform, false);

                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                skeleton.SetAnimatedBone(skeleton.rootBoneHash);
                skeleton.SetAnimatedBone(UMAUtils.StringToHash(firstChild.name));
                skeleton.SetAnimatedBone(UMAUtils.StringToHash(secondChild.name));
                skeleton.EndSkeletonUpdate();

                Assert.AreSame(rootObject.transform, firstChild.transform.parent);
                Assert.AreSame(rootObject.transform, secondChild.transform.parent,
                    "Reparenting while iterating must not skip the second preserved child.");
            }
            finally
            {
                if (secondChild != null) Object.DestroyImmediate(secondChild);
                if (firstChild != null) Object.DestroyImmediate(firstChild);
                if (helperObject != null) Object.DestroyImmediate(helperObject);
                if (rootObject != null) Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void AvatarTPoseUsesFlattenedHipsRotationAfterBakingHelperParent()
        {
            var rootObject = new GameObject("Global");
            var positionObject = new GameObject("Position");
            var hipsObject = new GameObject("Hips");
            try
            {
                positionObject.transform.SetParent(rootObject.transform, false);
                hipsObject.transform.SetParent(positionObject.transform, false);
                Quaternion authoredHipsRotation = Quaternion.Euler(6.335f, 0f, 0f);
                hipsObject.transform.localRotation = authoredHipsRotation;

                var skeleton = new UMAImprovedSkeleton(rootObject.transform);
                int hipsHash = UMAUtils.StringToHash(hipsObject.name);

                skeleton.BeginSkeletonUpdate();
                skeleton.ResetAll();
                skeleton.SetAnimatedBone(skeleton.rootBoneHash);
                skeleton.SetAnimatedBone(hipsHash);
                skeleton.EndSkeletonUpdate();

                Quaternion avatarRotation = skeleton.GetTPoseCorrectedRotation(hipsHash, authoredHipsRotation);

                Assert.AreSame(rootObject.transform, hipsObject.transform.parent);
                Assert.Less(Quaternion.Angle(authoredHipsRotation, hipsObject.transform.localRotation), 0.0001f,
                    "Flattening an identity helper parent must retain the authored Hips rotation.");
                Assert.Less(Quaternion.Angle(authoredHipsRotation, avatarRotation), 0.0001f,
                    "The Avatar skeleton description must receive the flattened Hips rotation, not identity.");
            }
            finally
            {
                if (hipsObject != null) Object.DestroyImmediate(hipsObject);
                if (positionObject != null) Object.DestroyImmediate(positionObject);
                if (rootObject != null) Object.DestroyImmediate(rootObject);
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
