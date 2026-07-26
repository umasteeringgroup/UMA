#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Phase-one output fixtures. These lock down parity between the managed
    /// combine result used by UMADefaultMeshCombiner and the current MeshData
    /// jobified path before the incremental combiner is introduced.
    /// </summary>
    public sealed class UMAIncrementalMeshCombinerBaselineTests
    {
        private static readonly int RootHash = UMAUtils.StringToHash("root");
        private static readonly int ChildHash = UMAUtils.StringToHash("child");

        [SetUp]
        public void SetUp()
        {
            SkinnedMeshCombiner.StaticInitializeOnLoad();
        }

        [TearDown]
        public void TearDown()
        {
            UMAMeshData.CleanupGlobalBuffers();
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshBaseline")]
        public void ManagedAndMeshDataPathsMatchAllChannelsBonesSubmeshesAndBlendshapeFrames()
        {
            var gameObject = new GameObject("root");
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            Mesh outputMesh = null;
            try
            {
                asset.name = "IncrementalBaselineFullChannels";
                asset.subMeshIndex = 0;
                asset.meshData = CreateFullChannelMeshData(asset.name);

                var managedSlot = new SlotData(asset);
                var jobifiedSlot = new SlotData(asset);
                var managedSource = CreateSource(asset.meshData, managedSlot, new[] { 0, 1 });
                var jobifiedSource = CreateSource(asset.meshData, jobifiedSlot, new[] { 0, 1 });
                var recipe = new UMAData.UMARecipe();
                var blendShapeSettings = new BlendShapeSettings
                {
                    ignoreBlendShapes = false,
                    loadAllFrames = true,
                    loadNormals = true,
                    loadTangents = true
                };

                var managedTarget = new UMAMeshData();
                SkinnedMeshCombiner.CombineMeshes(
                    managedTarget,
                    new[] { managedSource },
                    blendShapeSettings,
                    recipe,
                    0);

                var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                var childBone = new GameObject("child").transform;
                childBone.SetParent(gameObject.transform, false);
                renderer.bones = new[] { gameObject.transform, childBone };
                renderer.rootBone = gameObject.transform;
                var umaData = gameObject.AddComponent<UMAData>();
                umaData.umaRoot = gameObject;
                umaData.skeleton = new UMASkeleton(gameObject.transform);
                umaData.umaRecipe = recipe;
                umaData.blendShapeSettings = blendShapeSettings;
                umaData.force32bit = true;

                SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                    renderer,
                    new[] { jobifiedSource },
                    umaData,
                    0,
                    1024,
                    new Dictionary<string, float>(),
                    Quaternion.identity,
                    false,
                    false);

                outputMesh = renderer.sharedMesh;
                Assert.NotNull(outputMesh);

                UMAMeshBaselineSnapshot expected = UMAMeshBaselineSnapshot.Capture(
                    "Managed/default baseline",
                    managedTarget,
                    IndexFormat.UInt32);
                UMAMeshBaselineSnapshot actual = UMAMeshBaselineSnapshot.Capture(
                    "MeshData/jobified baseline",
                    outputMesh);

                UMAMeshBaselineSnapshot.AssertEquivalent(expected, actual);
                TestContext.Progress.WriteLine($"Managed snapshot: {expected.ComputeSha256()}");
                TestContext.Progress.WriteLine($"MeshData snapshot: {actual.ComputeSha256()}");
            }
            finally
            {
                DisposeTriangles(asset.meshData);
                if (outputMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(outputMesh);
                }
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshBaseline")]
        public void ManagedAndMeshDataPathsMatchMultipleSlotsAndTriangleMasks()
        {
            var gameObject = new GameObject("root");
            var firstAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            var secondAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            Mesh outputMesh = null;
            try
            {
                firstAsset.name = "IncrementalBaselineMaskedSlot";
                firstAsset.subMeshIndex = 0;
                firstAsset.meshData = CreateTrianglePairMeshData(firstAsset.name, Vector3.zero, true);

                secondAsset.name = "IncrementalBaselineSecondSlot";
                secondAsset.subMeshIndex = 0;
                secondAsset.meshData = CreateTrianglePairMeshData(secondAsset.name, new Vector3(2f, 0f, 0f), true);

                var managedSources = new[]
                {
                    CreateSource(firstAsset.meshData, new SlotData(firstAsset), new[] { 0 }, CreateFirstTriangleMask()),
                    CreateSource(secondAsset.meshData, new SlotData(secondAsset), new[] { 0 })
                };
                var jobifiedSources = new[]
                {
                    CreateSource(firstAsset.meshData, new SlotData(firstAsset), new[] { 0 }, CreateFirstTriangleMask()),
                    CreateSource(secondAsset.meshData, new SlotData(secondAsset), new[] { 0 })
                };

                var recipe = new UMAData.UMARecipe();
                var settings = new BlendShapeSettings { ignoreBlendShapes = false };
                var managedTarget = new UMAMeshData();
                SkinnedMeshCombiner.CombineMeshes(managedTarget, managedSources, settings, recipe, 0);

                var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = new[] { gameObject.transform };
                renderer.rootBone = gameObject.transform;
                var umaData = gameObject.AddComponent<UMAData>();
                umaData.umaRoot = gameObject;
                umaData.skeleton = new UMASkeleton(gameObject.transform);
                umaData.umaRecipe = recipe;
                umaData.blendShapeSettings = settings;
                umaData.force32bit = false;

                SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                    renderer,
                    jobifiedSources,
                    umaData,
                    0,
                    512,
                    new Dictionary<string, float>(),
                    Quaternion.identity,
                    false,
                    false);

                outputMesh = renderer.sharedMesh;
                Assert.NotNull(outputMesh);

                UMAMeshBaselineSnapshot expected = UMAMeshBaselineSnapshot.Capture(
                    "Managed masked baseline",
                    managedTarget,
                    IndexFormat.UInt16);
                UMAMeshBaselineSnapshot actual = UMAMeshBaselineSnapshot.Capture(
                    "MeshData masked baseline",
                    outputMesh);

                UMAMeshBaselineSnapshot.AssertEquivalent(expected, actual);
                CollectionAssert.AreEqual(
                    new[] { 0, 2, 3, 4, 5, 6, 4, 6, 7 },
                    actual.subMeshes[0].indices,
                    "The masked triangle must be absent and the second slot indices must include its vertex offset.");
            }
            finally
            {
                DisposeTriangles(firstAsset.meshData);
                DisposeTriangles(secondAsset.meshData);
                if (outputMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(outputMesh);
                }
                UnityEngine.Object.DestroyImmediate(firstAsset);
                UnityEngine.Object.DestroyImmediate(secondAsset);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static SkinnedMeshCombiner.CombineInstance CreateSource(
            UMAMeshData meshData,
            SlotData slotData,
            int[] targetSubmeshes,
            BitArray[] triangleMask = null)
        {
            return new SkinnedMeshCombiner.CombineInstance
            {
                meshData = meshData,
                slotData = slotData,
                targetSubmeshIndices = targetSubmeshes,
                triangleMask = triangleMask
            };
        }

        private static UMAMeshData CreateFullChannelMeshData(string slotName)
        {
            return new UMAMeshData
            {
                SlotName = slotName,
                vertexCount = 4,
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 1f, 0f)
                },
                normals = Repeat(Vector3.forward, 4),
                tangents = Repeat(new Vector4(1f, 0f, 0f, -1f), 4),
                colors32 = new[]
                {
                    new Color32(10, 20, 30, 40),
                    new Color32(50, 60, 70, 80),
                    new Color32(90, 100, 110, 120),
                    new Color32(130, 140, 150, 160)
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f)
                },
                uv2 = OffsetUVs(0.1f),
                uv3 = OffsetUVs(0.2f),
                uv4 = OffsetUVs(0.3f),
                subMeshCount = 2,
                submeshes = new[]
                {
                    new SubMeshTriangles(new[] { 0, 1, 2 }),
                    new SubMeshTriangles(new[] { 2, 1, 3 })
                },
                bindPoses = new[]
                {
                    Matrix4x4.identity,
                    Matrix4x4.Translate(new Vector3(0f, -1f, 0f))
                },
                boneNameHashes = new[] { RootHash, ChildHash },
                umaBones = new[]
                {
                    new UMATransform { hash = RootHash, name = "root" },
                    new UMATransform { hash = ChildHash, name = "child", parent = RootHash }
                },
                ManagedBonesPerVertex = new byte[] { 1, 2, 1, 2 },
                ManagedBoneWeights = new[]
                {
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 0.75f },
                    new BoneWeight1 { boneIndex = 1, weight = 0.25f },
                    new BoneWeight1 { boneIndex = 1, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 0.2f },
                    new BoneWeight1 { boneIndex = 1, weight = 0.8f }
                },
                blendShapes = new[]
                {
                    new UMABlendShape
                    {
                        shapeName = "Smile",
                        frames = new[]
                        {
                            CreateBlendFrame(25f, 0.01f, true, true),
                            CreateBlendFrame(100f, 0.04f, true, true)
                        }
                    },
                    new UMABlendShape
                    {
                        shapeName = "Blink",
                        frames = new[]
                        {
                            CreateBlendFrame(100f, -0.02f, false, false)
                        }
                    }
                }
            };
        }

        private static UMAMeshData CreateTrianglePairMeshData(string slotName, Vector3 offset, bool includeOptionalChannels)
        {
            var vertices = new[]
            {
                offset + new Vector3(0f, 0f, 0f),
                offset + new Vector3(1f, 0f, 0f),
                offset + new Vector3(0f, 1f, 0f),
                offset + new Vector3(1f, 1f, 0f)
            };

            return new UMAMeshData
            {
                SlotName = slotName,
                vertexCount = vertices.Length,
                vertices = vertices,
                normals = includeOptionalChannels ? Repeat(Vector3.forward, vertices.Length) : null,
                tangents = includeOptionalChannels ? Repeat(new Vector4(1f, 0f, 0f, 1f), vertices.Length) : null,
                colors32 = includeOptionalChannels ? Repeat(new Color32(200, 180, 160, 255), vertices.Length) : null,
                uv = includeOptionalChannels ? OffsetUVs(0f) : null,
                uv2 = includeOptionalChannels ? OffsetUVs(0.1f) : null,
                uv3 = includeOptionalChannels ? OffsetUVs(0.2f) : null,
                uv4 = includeOptionalChannels ? OffsetUVs(0.3f) : null,
                subMeshCount = 1,
                submeshes = new[]
                {
                    new SubMeshTriangles(new[] { 0, 1, 2, 0, 2, 3 })
                },
                bindPoses = new[] { Matrix4x4.identity },
                boneNameHashes = new[] { RootHash },
                umaBones = new[] { new UMATransform { hash = RootHash, name = "root" } },
                ManagedBonesPerVertex = new byte[] { 1, 1, 1, 1 },
                ManagedBoneWeights = new[]
                {
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f }
                }
            };
        }

        private static BitArray[] CreateFirstTriangleMask()
        {
            return new[]
            {
                new BitArray(new[] { true, false })
            };
        }

        private static UMABlendFrame CreateBlendFrame(float weight, float delta, bool normals, bool tangents)
        {
            return new UMABlendFrame
            {
                frameWeight = weight,
                deltaVertices = new[]
                {
                    new Vector3(delta, 0f, 0f),
                    new Vector3(0f, delta, 0f),
                    new Vector3(0f, 0f, delta),
                    new Vector3(delta, delta, 0f)
                },
                deltaNormals = normals
                    ? Repeat(new Vector3(0f, 0f, delta), 4)
                    : Array.Empty<Vector3>(),
                deltaTangents = tangents
                    ? Repeat(new Vector3(delta, 0f, 0f), 4)
                    : Array.Empty<Vector3>()
            };
        }

        private static Vector2[] OffsetUVs(float offset)
        {
            return new[]
            {
                new Vector2(offset, offset),
                new Vector2(1f + offset, offset),
                new Vector2(offset, 1f + offset),
                new Vector2(1f + offset, 1f + offset)
            };
        }

        private static T[] Repeat<T>(T value, int count)
        {
            var result = new T[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = value;
            }
            return result;
        }

        private static void DisposeTriangles(UMAMeshData meshData)
        {
            if (meshData?.submeshes == null)
            {
                return;
            }
            for (int i = 0; i < meshData.submeshes.Length; i++)
            {
                meshData.submeshes[i]?.DisposeNativeTriangles(true);
            }
        }
    }
}

#endif
