#if UNITY_EDITOR

using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMABoneBakingSkeletonTests
    {
        [Test]
        [Category("UMA")]
        [Category("BlendShapes")]
        public void ForcedBakedBlendShapeValueOverridesRuntimeUpdates()
        {
            var gameObject = new GameObject("UMA_ForcedBlendShapeValueTest");
            try
            {
                var umaData = gameObject.AddComponent<UMAData>();
                umaData.blendShapeSettings.forceBakedBlendShapeValue = true;
                umaData.blendShapeSettings.forcedBakedBlendShapeValue = 0.5f;
                umaData.blendShapeSettings.blendShapes.Add(
                    "TestShape",
                    new BlendShapeData { isBaked = true, value = 0.5f });

                umaData.SetBlendShape("TestShape", 0.9f, false, true);

                Assert.AreEqual(0.5f, umaData.blendShapeSettings.blendShapes["TestShape"].value);
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
        [Category("MeshCombiner")]
        [Category("BoneBaking")]
        public void RigOnlyBoneBakingBuildPreservesExistingAtlasUVs()
        {
            var gameObject = new GameObject("UMA_BoneBakingRigOnlyUVTest");
            UMAMaterial umaMaterial = null;
            SlotDataAsset slotAsset = null;
            try
            {
                var umaData = gameObject.AddComponent<UMAData>();
                var combiner = gameObject.AddComponent<UMADefaultBoneBakingMeshCombiner>();
                umaData.isShapeDirty = true;
                umaData.isMeshDirty = false;
                combiner.Preprocess(umaData);

                Assert.IsTrue(umaData.isMeshDirty, "Bone baking must still rebuild geometry after a rig change.");
                FieldInfo preserveField = typeof(UMADefaultBoneBakingMeshCombiner).GetField(
                    "preserveUVsForRigOnlyBuild",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(preserveField);
                Assert.IsTrue((bool)preserveField.GetValue(combiner));

                var previousUVs = new[]
                {
                    new Vector2(0.21f, 0.32f),
                    new Vector2(0.58f, 0.77f)
                };
                var mesh = new MeshBuilder
                {
                    vertexCount = previousUVs.Length,
                    has_uv = true,
                    uv = (Vector2[])previousUVs.Clone()
                };

                MethodInfo captureMethod = typeof(UMADefaultBoneBakingMeshCombiner).GetMethod(
                    "CapturePreviousUVs",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo restoreMethod = typeof(UMADefaultBoneBakingMeshCombiner).GetMethod(
                    "RestorePreviousUVs",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(captureMethod);
                Assert.NotNull(restoreMethod);

                var snapshot = (Vector2[])captureMethod.Invoke(combiner, new object[] { mesh, null, 0 });
                mesh.uv[0] = Vector2.zero;
                mesh.uv[1] = Vector2.one;
                bool restored = (bool)restoreMethod.Invoke(null, new object[] { mesh, snapshot });

                Assert.IsTrue(restored);
                CollectionAssert.AreEqual(previousUVs, new[] { mesh.uv[0], mesh.uv[1] });

                // If no previous combined buffer is available, an unchanged atlas must use
                // the slot's cached normalized UV area rather than current atlas metadata.
                umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
                slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
                slotAsset.meshData = new UMAMeshData
                {
                    vertexCount = 2,
                    vertices = new Vector3[2]
                };
                var slot = new SlotData(slotAsset)
                {
                    UVArea = new Rect(0.2f, 0.3f, 0.4f, 0.5f)
                };
                var generatedMaterial = new UMAData.GeneratedMaterial
                {
                    umaMaterial = umaMaterial,
                    cropResolution = new Vector2(1024f, 1024f),
                    resolutionScale = Vector2.one
                };
                generatedMaterial.materialFragments.Add(new UMAData.MaterialFragment
                {
                    slotData = slot,
                    atlasRegion = new Rect(800f, 700f, 100f, 100f),
                    overlayList = new List<OverlayData>()
                });

                mesh.uv[0] = Vector2.zero;
                mesh.uv[1] = new Vector2(1f, 0.5f);
                FieldInfo atlasResolutionField = typeof(UMADefaultMeshCombiner).GetField(
                    "atlasResolution",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo recalculateMethod = typeof(UMADefaultBoneBakingMeshCombiner).GetMethod(
                    "RecalculateUV",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(MeshBuilder),
                        typeof(List<UMAData.GeneratedMaterial>),
                        typeof(bool)
                    },
                    null);
                Assert.NotNull(atlasResolutionField);
                Assert.NotNull(recalculateMethod);
                atlasResolutionField.SetValue(combiner, 1000);
                recalculateMethod.Invoke(
                    combiner,
                    new object[] { mesh, new List<UMAData.GeneratedMaterial> { generatedMaterial }, false });

                AssertVector2Equal(new Vector2(0.2f, 0.3f), mesh.uv[0]);
                AssertVector2Equal(new Vector2(0.6f, 0.55f), mesh.uv[1]);
                AssertRectEqual(new Rect(0.2f, 0.3f, 0.4f, 0.5f), slot.UVArea);

                // A real atlas update must still replace the cached area and remap UVs.
                mesh.uv[0] = Vector2.zero;
                mesh.uv[1] = new Vector2(1f, 0.5f);
                recalculateMethod.Invoke(
                    combiner,
                    new object[] { mesh, new List<UMAData.GeneratedMaterial> { generatedMaterial }, true });

                AssertVector2Equal(new Vector2(0.8f, 0.7f), mesh.uv[0]);
                AssertVector2Equal(new Vector2(0.9f, 0.75f), mesh.uv[1]);
                AssertRectEqual(new Rect(0.8f, 0.7f, 0.1f, 0.1f), slot.UVArea);
            }
            finally
            {
                if (slotAsset != null) Object.DestroyImmediate(slotAsset);
                if (umaMaterial != null) Object.DestroyImmediate(umaMaterial);
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

        [Test]
        [Category("UMA")]
        [Category("BoneBaking")]
        public void JobifiedSkinningMatchesManagedRetargeting()
        {
            bool previousUseJobs = SkinnedMeshCombinerRetargeting.UseJobifiedSkinning;
            int previousThreshold = SkinnedMeshCombinerRetargeting.JobifiedSkinningVertexThreshold;
            try
            {
                SkinnedMeshCombinerRetargeting.JobifiedSkinningVertexThreshold = 1;

                SkinnedMeshCombinerRetargeting.UseJobifiedSkinning = false;
                MeshBuilder managed = BuildRetargetedTestMesh();

                SkinnedMeshCombinerRetargeting.UseJobifiedSkinning = true;
                MeshBuilder jobified = BuildRetargetedTestMesh();

                AssertVector3ArraysEqual(managed.vertices, jobified.vertices, managed.vertexCount, "vertices");
                AssertVector3ArraysEqual(managed.normals, jobified.normals, managed.vertexCount, "normals");
                AssertVector4ArraysEqual(managed.tangents, jobified.tangents, managed.vertexCount, "tangents");
                CollectionAssert.AreEqual(managed.bonesPerVertexManaged, jobified.bonesPerVertexManaged);
                Assert.AreEqual(managed.boneWeightsManaged.Length, jobified.boneWeightsManaged.Length);
                for (int i = 0; i < managed.boneWeightsManaged.Length; i++)
                {
                    Assert.AreEqual(managed.boneWeightsManaged[i].boneIndex, jobified.boneWeightsManaged[i].boneIndex);
                    Assert.AreEqual(managed.boneWeightsManaged[i].weight, jobified.boneWeightsManaged[i].weight, 0.00001f);
                }

                Assert.AreEqual(managed.blendShapes.Count, jobified.blendShapes.Count);
                for (int shapeIndex = 0; shapeIndex < managed.blendShapes.Count; shapeIndex++)
                {
                    UMABlendShape managedShape = managed.blendShapes[shapeIndex];
                    UMABlendShape jobifiedShape = jobified.blendShapes[shapeIndex];
                    Assert.AreEqual(managedShape.shapeName, jobifiedShape.shapeName);
                    Assert.AreEqual(managedShape.frames.Length, jobifiedShape.frames.Length);
                    for (int frameIndex = 0; frameIndex < managedShape.frames.Length; frameIndex++)
                    {
                        UMABlendFrame managedFrame = managedShape.frames[frameIndex];
                        UMABlendFrame jobifiedFrame = jobifiedShape.frames[frameIndex];
                        AssertVector3ArraysEqual(managedFrame.deltaVertices, jobifiedFrame.deltaVertices, managed.vertexCount, "blendshape vertices");
                        AssertVector3ArraysEqual(managedFrame.deltaNormals, jobifiedFrame.deltaNormals, managed.vertexCount, "blendshape normals");
                        AssertVector3ArraysEqual(managedFrame.deltaTangents, jobifiedFrame.deltaTangents, managed.vertexCount, "blendshape tangents");
                    }
                }
            }
            finally
            {
                SkinnedMeshCombinerRetargeting.UseJobifiedSkinning = previousUseJobs;
                SkinnedMeshCombinerRetargeting.JobifiedSkinningVertexThreshold = previousThreshold;
            }
        }

        private static MeshBuilder BuildRetargetedTestMesh()
        {
            var sourceMesh = new UMAMeshData
            {
                SlotName = "JobifiedBoneBakingTest",
                vertexCount = 3,
                vertices = new[]
                {
                    new Vector3(-0.5f, 0.1f, 0.2f),
                    new Vector3(0.3f, 0.8f, -0.1f),
                    new Vector3(0.7f, -0.2f, 0.4f)
                },
                normals = new[] { Vector3.up, Vector3.forward, Vector3.right },
                tangents = new[]
                {
                    new Vector4(1f, 0f, 0f, 1f),
                    new Vector4(0f, 1f, 0f, -1f),
                    new Vector4(0f, 0f, 1f, 1f)
                },
                ManagedBonesPerVertex = new byte[] { 2, 2, 2 },
                ManagedBoneWeights = new[]
                {
                    new BoneWeight1 { boneIndex = 0, weight = 0.25f },
                    new BoneWeight1 { boneIndex = 1, weight = 0.75f },
                    new BoneWeight1 { boneIndex = 0, weight = 0.6f },
                    new BoneWeight1 { boneIndex = 1, weight = 0.4f },
                    new BoneWeight1 { boneIndex = 0, weight = 0.5f },
                    new BoneWeight1 { boneIndex = 1, weight = 0.5f }
                },
                subMeshCount = 1,
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) }
            };

            var frame = new UMABlendFrame(3)
            {
                frameWeight = 100f,
                deltaVertices = new[]
                {
                    new Vector3(0.01f, 0.02f, 0.03f),
                    new Vector3(-0.02f, 0.01f, 0.04f),
                    new Vector3(0.03f, -0.01f, 0.02f)
                },
                deltaNormals = new[]
                {
                    new Vector3(0.01f, 0f, 0f),
                    new Vector3(0f, 0.01f, 0f),
                    new Vector3(0f, 0f, 0.01f)
                },
                deltaTangents = new[]
                {
                    new Vector3(0f, 0.01f, 0f),
                    new Vector3(0f, 0f, 0.01f),
                    new Vector3(0.01f, 0f, 0f)
                }
            };
            sourceMesh.blendShapes = new[]
            {
                new UMABlendShape { shapeName = "TestShape", frames = new[] { frame } }
            };

            var source = new SkinnedMeshCombinerRetargeting.CombineInstance
            {
                meshData = sourceMesh,
                targetSubmeshIndices = new[] { 0 },
                targetBoneIndices = new[] { 0, 0 },
                resolvedBoneMatrixes = new[]
                {
                    Matrix4x4.TRS(new Vector3(0.2f, -0.1f, 0.05f), Quaternion.Euler(5f, 12f, -3f), Vector3.one),
                    Matrix4x4.TRS(new Vector3(-0.1f, 0.15f, 0.08f), Quaternion.Euler(-7f, 4f, 9f), Vector3.one)
                }
            };
            var settings = new BlendShapeSettings();
            settings.blendShapes.Add("TestShape", new BlendShapeData { value = 0f, isBaked = false });

            var target = new MeshBuilder();
            target.PrepareBones(1);
            SkinnedMeshCombinerRetargeting.CombineMeshes(
                target,
                new[] { source },
                new[] { Matrix4x4.TRS(new Vector3(0.05f, 0.02f, -0.04f), Quaternion.Euler(2f, -6f, 3f), Vector3.one) },
                settings,
                uniformTargetPoses: true);
            target.ReleaseBuffers();
            return target;
        }

        private static void AssertVector3ArraysEqual(Vector3[] expected, Vector3[] actual, int count, string label)
        {
            Assert.IsNotNull(expected, $"Managed {label} are missing.");
            Assert.IsNotNull(actual, $"Jobified {label} are missing.");
            for (int i = 0; i < count; i++)
                Assert.Less(Vector3.Distance(expected[i], actual[i]), 0.0001f, $"{label} differ at index {i}.");
        }

        private static void AssertVector4ArraysEqual(Vector4[] expected, Vector4[] actual, int count, string label)
        {
            Assert.IsNotNull(expected, $"Managed {label} are missing.");
            Assert.IsNotNull(actual, $"Jobified {label} are missing.");
            for (int i = 0; i < count; i++)
                Assert.Less(Vector4.Distance(expected[i], actual[i]), 0.0001f, $"{label} differ at index {i}.");
        }

        private static void AssertVector2Equal(Vector2 expected, Vector2 actual)
        {
            Assert.Less(Vector2.Distance(expected, actual), 0.000001f);
        }

        private static void AssertRectEqual(Rect expected, Rect actual)
        {
            AssertVector2Equal(expected.position, actual.position);
            AssertVector2Equal(expected.size, actual.size);
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
