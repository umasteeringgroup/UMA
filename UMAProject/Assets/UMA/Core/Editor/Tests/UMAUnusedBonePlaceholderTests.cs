#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAUnusedBonePlaceholderTests
    {
        private GameObject root;
        private UMAData data;
        private SkinnedMeshRenderer renderer;
        private readonly List<SlotDataAsset> assets = new List<SlotDataAsset>();
        private bool previousParallel;

        [SetUp]
        public void SetUp()
        {
            previousParallel = SkinnedMeshCombinerMeshAPI.UseParallelBoneWeights;
            root = new GameObject("Global");
            data = root.AddComponent<UMAData>();
            data.umaRoot = root;
            data.umaRecipe = new UMAData.UMARecipe();
            data.skeleton = new UMASkeleton(root.transform);
            renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = root.transform;
        }

        [TearDown]
        public void TearDown()
        {
            SkinnedMeshCombinerMeshAPI.UseParallelBoneWeights = previousParallel;
            if (renderer != null && renderer.sharedMesh != null)
                UnityEngine.Object.DestroyImmediate(renderer.sharedMesh);
            foreach (SlotDataAsset asset in assets)
            {
                asset.meshData.submeshes[0].DisposeNativeTriangles(true);
                UnityEngine.Object.DestroyImmediate(asset);
            }
            assets.Clear();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void UnusedZeroHashesAreOmittedWithoutChangingSkinning(bool parallel, bool incremental)
        {
            SkinnedMeshCombinerMeshAPI.UseParallelBoneWeights = parallel;
            var sources = new[] { CreateSource(), CreateSource() };
            if (incremental)
            {
                using (var preparation = SkinnedMeshCombinerMeshAPI.BeginIncrementalCombinePreparation(
                    new SkinnedMeshCombinerMeshAPI.RendererBatch { Renderer = renderer, Sources = sources, AtlasResolution = 128 },
                    data, new Dictionary<string, float>(), false, false, Quaternion.identity))
                {
                    while (!preparation.IsComplete) preparation.Step();
                    using (var pending = preparation.TakeResult()) pending.FinalizeMesh();
                }
            }
            else
            {
                SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(renderer, sources, data, 0, 128,
                    new Dictionary<string, float>(), Quaternion.identity, false, false);
            }

            Mesh combined = renderer.sharedMesh;
            Assert.That(combined.vertexCount, Is.EqualTo(6));
            Assert.That(renderer.bones, Is.EqualTo(new[] { root.transform, root.transform }));
            Assert.That(combined.bindposes, Is.EqualTo(new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) }),
                "Different bind poses for the same real bone must remain distinct.");
            var weights = combined.GetAllBoneWeights().ToArray();
            Assert.That(weights.Select(w => w.boneIndex), Is.EqualTo(new[] { 0, 1, 1, 0, 0, 1, 1, 0 }));
            Assert.That(weights.Select(w => w.weight), Is.EqualTo(new[] { .75f, .25f, 1f, 1f, .75f, .25f, 1f, 1f }));
            Assert.That(combined.GetBonesPerVertex().ToArray(), Is.EqualTo(new byte[] { 2, 1, 1, 2, 1, 1 }));
            foreach (var source in sources)
            {
                Assert.That(source.meshData.boneNameHashes, Is.EqualTo(new[] { 0, UMAUtils.StringToHash("Global"), 0, UMAUtils.StringToHash("Global") }));
                Assert.That(source.meshData.ManagedBoneWeights.Select(w => w.boneIndex), Is.EqualTo(new[] { 1, 3, 3, 1 }),
                    "Combining must not rewrite the shared slot's bone indices.");
            }
        }

        [TestCase(false, 1f)]
        [TestCase(true, 1f)]
        [TestCase(false, 0f)]
        [TestCase(true, 0f)]
        public void ReferencedZeroHashKeepsItsRemapAndMissingBoneDiagnostic(bool parallel, float weight)
        {
            SkinnedMeshCombinerMeshAPI.UseParallelBoneWeights = parallel;
            var source = CreateSource();
            source.meshData.ManagedBoneWeights[1] = new BoneWeight1 { boneIndex = 0, weight = weight };
            using (var pending = SkinnedMeshCombinerMeshAPI.PrepareIncrementalCombine(
                new SkinnedMeshCombinerMeshAPI.RendererBatch { Renderer = renderer, Sources = new[] { source }, AtlasResolution = 128 },
                data, new Dictionary<string, float>(), false, false, Quaternion.identity))
            {
                pending.CompleteJobs();
                // Keep this entry for HashesToTransforms to diagnose at renderer finalization.
                // A zero-weight record still needs an in-range remap for Unity's bone-weight buffer.
                int missingBone = pending.BoneWeights[1].boneIndex;
                Assert.That(missingBone, Is.GreaterThanOrEqualTo(0).And.LessThan(pending.BonesList.Count));
                Assert.That(pending.BonesList[missingBone], Is.Zero);
                Assert.That(pending.BoneWeights[1].weight, Is.EqualTo(weight));
                Assert.That(pending.BonesList.Count(h => h == 0), Is.EqualTo(1),
                    "The other, unreferenced zero-hash entry must still be omitted.");
            }
        }

        private SkinnedMeshCombiner.CombineInstance CreateSource()
        {
            int rootHash = UMAUtils.StringToHash("Global");
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            assets.Add(asset);
            asset.name = "Unused bone placeholder test " + assets.Count;
            asset.meshData = new UMAMeshData
            {
                SlotName = asset.name,
                vertexCount = 3,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                subMeshCount = 1,
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) },
                boneNameHashes = new[] { 0, rootHash, 0, rootHash },
                bindPoses = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) },
                umaBones = Array.Empty<UMATransform>(),
                blendShapes = Array.Empty<UMABlendShape>(),
                ManagedBonesPerVertex = new byte[] { 2, 1, 1 },
                ManagedBoneWeights = new[]
                {
                    new BoneWeight1 { boneIndex = 1, weight = .75f },
                    new BoneWeight1 { boneIndex = 3, weight = .25f },
                    new BoneWeight1 { boneIndex = 3, weight = 1f },
                    new BoneWeight1 { boneIndex = 1, weight = 1f }
                }
            };
            return new SkinnedMeshCombiner.CombineInstance
            {
                meshData = asset.meshData, slotData = new SlotData(asset), targetSubmeshIndices = new[] { 0 }
            };
        }
    }
}
#endif
