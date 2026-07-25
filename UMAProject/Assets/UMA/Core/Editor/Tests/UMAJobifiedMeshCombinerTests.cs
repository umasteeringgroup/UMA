#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    public sealed class UMAJobifiedMeshCombinerTests
    {
        [Serializable]
        private sealed class DerivedVertexDeltaAdjustment : VertexDeltaAdjustment { }

        [Serializable]
        private sealed class DerivedVertexDeltaAdjustmentCollection : VertexDeltaAdjustmentCollection { }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void AdditiveVertexDeltaStackUsesJobifiedModifierPath()
        {
            var slot = new SlotData();
            var adjustments = new VertexDeltaAdjustmentCollection();
            adjustments.vertexAdjustments.Add(new VertexDeltaAdjustment
            {
                vertexIndex = 3,
                weight = 0.5f,
                delta = new UnityEngine.Vector3(1f, 2f, 3f)
            });
            slot.meshModifiers.Add(new MeshModifier.Modifier
            {
                Scale = 0.75f,
                adjustments = adjustments
            });

            Assert.IsTrue(SkinnedMeshCombinerMeshAPI.SupportsJobifiedMeshModifiers(slot));
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void OrderDependentModifierStackRetainsManagedFallback()
        {
            var slot = new SlotData();
            var adjustments = new VertexResetAdjustmentCollection();
            adjustments.vertexAdjustments.Add(new VertexResetAdjustment { vertexIndex = 0, weight = 1f });
            slot.meshModifiers.Add(new MeshModifier.Modifier
            {
                Scale = 1f,
                adjustments = adjustments
            });

            Assert.IsFalse(SkinnedMeshCombinerMeshAPI.SupportsJobifiedMeshModifiers(slot));
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void DerivedDeltaAdjustmentRetainsManagedFallback()
        {
            var slot = new SlotData();
            var adjustments = new VertexDeltaAdjustmentCollection();
            adjustments.vertexAdjustments.Add(new DerivedVertexDeltaAdjustment { vertexIndex = 0, weight = 1f });
            slot.meshModifiers.Add(new MeshModifier.Modifier { Scale = 1f, adjustments = adjustments });

            Assert.IsFalse(SkinnedMeshCombinerMeshAPI.SupportsJobifiedMeshModifiers(slot),
                "A derived adjustment can override managed behavior and must not be treated as a plain additive delta.");
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void DerivedDeltaCollectionRetainsManagedFallback()
        {
            var slot = new SlotData();
            var adjustments = new DerivedVertexDeltaAdjustmentCollection();
            adjustments.vertexAdjustments.Add(new VertexDeltaAdjustment { vertexIndex = 0, weight = 1f });
            slot.meshModifiers.Add(new MeshModifier.Modifier { Scale = 1f, adjustments = adjustments });

            Assert.IsFalse(SkinnedMeshCombinerMeshAPI.SupportsJobifiedMeshModifiers(slot),
                "A derived collection can override ordering or apply semantics and must remain on the managed path.");
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void EmptyCustomCollectionCannotHideAlongsideValidDeltas()
        {
            var slot = new SlotData();
            var deltas = new VertexDeltaAdjustmentCollection();
            deltas.vertexAdjustments.Add(new VertexDeltaAdjustment { vertexIndex = 0, weight = 1f });
            slot.meshModifiers.Add(new MeshModifier.Modifier { Scale = 1f, adjustments = deltas });
            slot.meshModifiers.Add(new MeshModifier.Modifier
            {
                Scale = 1f,
                adjustments = new DerivedVertexDeltaAdjustmentCollection()
            });

            Assert.IsFalse(SkinnedMeshCombinerMeshAPI.SupportsJobifiedMeshModifiers(slot),
                "A custom collection may implement procedural behavior even when its serialized adjustment list is empty.");
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void CombineInstanceDefaultsToManagedModifierBehavior()
        {
            var source = new SkinnedMeshCombiner.CombineInstance();
            Assert.IsFalse(source.applyMeshModifiersInJobs,
                "Legacy, default, and bone-baking combiners must not opt into the jobified modifier path implicitly.");
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void InterleavedVertexStructsMatchDeclaredMeshStreams()
        {
            var combinerType = typeof(SkinnedMeshCombinerMeshAPI);
            Assert.AreEqual(28, Marshal.SizeOf(combinerType.GetNestedType("NormTan", BindingFlags.NonPublic)));
            Assert.AreEqual(20, Marshal.SizeOf(combinerType.GetNestedType("ColUV01", BindingFlags.NonPublic)));
            Assert.AreEqual(16, Marshal.SizeOf(combinerType.GetNestedType("UV23", BindingFlags.NonPublic)));

            var layoutMethod = combinerType.GetMethod("BuildVertexLayout", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(layoutMethod);
            var layout = (VertexAttributeDescriptor[])layoutMethod.Invoke(null, new object[] { true, true, true, true, true, true, true });

            Assert.AreEqual(8, layout.Length);
            AssertDescriptor(layout[0], VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            AssertDescriptor(layout[1], VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1);
            AssertDescriptor(layout[2], VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1);
            AssertDescriptor(layout[3], VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 2);
            AssertDescriptor(layout[4], VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2);
            AssertDescriptor(layout[5], VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 2);
            AssertDescriptor(layout[6], VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 3);
            AssertDescriptor(layout[7], VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, 3);
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void SourceValidationRejectsMismatchedVertexChannelsBeforeNativeAllocation()
        {
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                asset.meshData = new UMAMeshData
                {
                    vertexCount = 3,
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    normals = new[] { Vector3.forward, Vector3.forward }
                };
                var sources = new[]
                {
                    new SkinnedMeshCombiner.CombineInstance
                    {
                        meshData = asset.meshData,
                        slotData = new SlotData(asset)
                    }
                };
                var validateMethod = typeof(SkinnedMeshCombinerMeshAPI).GetMethod("ValidateSources", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(validateMethod);

                var exception = Assert.Throws<TargetInvocationException>(() => validateMethod.Invoke(null, new object[] { sources }));
                StringAssert.Contains("normals entries", exception.InnerException?.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void IndexRangeValidationCannotSpillIntoAnotherSubmesh()
        {
            var validateMethod = typeof(SkinnedMeshCombinerMeshAPI).GetMethod("ValidateIndexDestinationRange", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(validateMethod);

            Assert.DoesNotThrow(() => validateMethod.Invoke(null, new object[] { 3, 3, 3, 6, 0, 0 }));
            var exception = Assert.Throws<TargetInvocationException>(() => validateMethod.Invoke(null, new object[] { 5, 3, 3, 6, 0, 0 }));
            Assert.IsInstanceOf<InvalidOperationException>(exception.InnerException);
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void FallbackTangentIsNormalizedAndOrthogonal()
        {
            var tangentMethod = typeof(SkinnedMeshCombinerMeshAPI).GetMethod("BuildFallbackTangent", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(tangentMethod);
            var tangent = (Vector4)tangentMethod.Invoke(null, new object[] { Vector3.up, 0f });
            var tangentDirection = new Vector3(tangent.x, tangent.y, tangent.z);

            Assert.That(tangentDirection.magnitude, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(Vector3.Dot(Vector3.up, tangentDirection), Is.EqualTo(0f).Within(1e-6f));
            Assert.AreEqual(1f, tangent.w);
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void CancelledModifierDeltasDoNotAllocateNativeWork()
        {
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                asset.meshData = new UMAMeshData { vertexCount = 1, vertices = new[] { Vector3.zero } };
                var slot = new SlotData(asset);
                var adjustments = new VertexDeltaAdjustmentCollection();
                adjustments.vertexAdjustments.Add(new VertexDeltaAdjustment { vertexIndex = 0, weight = 1f, delta = Vector3.right });
                adjustments.vertexAdjustments.Add(new VertexDeltaAdjustment { vertexIndex = 0, weight = 1f, delta = Vector3.left });
                slot.meshModifiers.Add(new MeshModifier.Modifier { Scale = 1f, adjustments = adjustments });
                var source = new SkinnedMeshCombiner.CombineInstance
                {
                    meshData = asset.meshData,
                    slotData = slot,
                    applyMeshModifiersInJobs = true
                };

                var method = typeof(SkinnedMeshCombinerMeshAPI).GetMethod("BuildVertexDeltaRecords", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(method);
                object result = null;
                try
                {
                    result = method.Invoke(null, new object[] { new[] { source }, new[] { 0 } });
                    var isCreated = (bool)result.GetType().GetProperty("IsCreated").GetValue(result);
                    Assert.IsFalse(isCreated);
                }
                finally
                {
                    if (result != null && (bool)result.GetType().GetProperty("IsCreated").GetValue(result))
                        result.GetType().GetMethod("Dispose", Type.EmptyTypes).Invoke(result, null);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void ExplicitlyEmptyInternalLodDoesNotFallBackToBaseTriangles()
        {
            var submesh = new SubMeshTriangles(new[] { 0, 1, 2 });
            submesh.SetLodRanges(new List<UMALodRange>
            {
                new UMALodRange(0, 3),
                new UMALodRange(3, 0)
            });

            try
            {
                var method = typeof(SkinnedMeshCombinerMeshAPI).GetMethod("GetTrianglesForLOD", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(method);
                var triangles = (NativeArray<int>)method.Invoke(null, new object[] { submesh, 1 });
                Assert.AreEqual(0, triangles.Length);
            }
            finally
            {
                submesh.DisposeNativeTriangles(true);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void MeshDataCombinePreservesEveryVertexAndIndexChannel()
        {
            var gameObject = new GameObject("MeshData channel contract test");
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            Mesh outputMesh = null;
            try
            {
                asset.name = "ChannelContractSlot";
                asset.subMeshIndex = 0;
                asset.meshData = new UMAMeshData
                {
                    SlotName = asset.slotName,
                    vertexCount = 3,
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                    tangents = new[]
                    {
                        new Vector4(1f, 0f, 0f, -1f),
                        new Vector4(1f, 0f, 0f, -1f),
                        new Vector4(1f, 0f, 0f, -1f)
                    },
                    colors32 = new[]
                    {
                        new Color32(10, 20, 30, 40),
                        new Color32(50, 60, 70, 80),
                        new Color32(90, 100, 110, 120)
                    },
                    uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) },
                    uv2 = new[] { new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f), new Vector2(0.5f, 0.6f) },
                    uv3 = new[] { new Vector2(0.2f, 0.3f), new Vector2(0.4f, 0.5f), new Vector2(0.6f, 0.7f) },
                    uv4 = new[] { new Vector2(0.3f, 0.4f), new Vector2(0.5f, 0.6f), new Vector2(0.7f, 0.8f) },
                    subMeshCount = 1,
                    submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) },
                    bindPoses = new[] { Matrix4x4.identity },
                    boneNameHashes = new[] { 123 },
                    umaBones = new[] { new UMATransform { hash = 123, name = "root" } },
                    ManagedBonesPerVertex = new byte[] { 1, 1, 1 },
                    ManagedBoneWeights = new[]
                    {
                        new BoneWeight1 { boneIndex = 0, weight = 1f },
                        new BoneWeight1 { boneIndex = 0, weight = 1f },
                        new BoneWeight1 { boneIndex = 0, weight = 1f }
                    }
                };

                var slot = new SlotData(asset);
                var source = new SkinnedMeshCombiner.CombineInstance
                {
                    meshData = asset.meshData,
                    slotData = slot,
                    targetSubmeshIndices = new[] { 0 }
                };
                var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                outputMesh = new Mesh { name = "Reused MeshData destination" };
                outputMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                outputMesh.triangles = new[] { 0, 1, 2 };
                outputMesh.AddBlendShapeFrame("StaleShape", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
                renderer.sharedMesh = outputMesh;
                var umaData = gameObject.AddComponent<UMAData>();
                umaData.umaRecipe = new UMAData.UMARecipe();
                umaData.force32bit = true;

                SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                    renderer,
                    new[] { source },
                    umaData,
                    0,
                    1024,
                    new Dictionary<string, float>(),
                    Quaternion.identity,
                    false,
                    false);

                outputMesh = renderer.sharedMesh;
                Assert.NotNull(outputMesh);
                Assert.AreEqual(3, outputMesh.vertexCount);
                Assert.AreEqual(0, outputMesh.blendShapeCount);
                CollectionAssert.AreEqual(asset.meshData.vertices, outputMesh.vertices);
                CollectionAssert.AreEqual(asset.meshData.normals, outputMesh.normals);
                CollectionAssert.AreEqual(asset.meshData.tangents, outputMesh.tangents);
                CollectionAssert.AreEqual(asset.meshData.colors32, outputMesh.colors32);
                CollectionAssert.AreEqual(asset.meshData.uv, outputMesh.uv);
                CollectionAssert.AreEqual(asset.meshData.uv2, outputMesh.uv2);
                CollectionAssert.AreEqual(asset.meshData.uv3, outputMesh.uv3);
                CollectionAssert.AreEqual(asset.meshData.uv4, outputMesh.uv4);
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, outputMesh.GetIndices(0));
                Assert.AreEqual(IndexFormat.UInt32, outputMesh.indexFormat);
                var bonesPerVertex = outputMesh.GetBonesPerVertex();
                var boneWeights = outputMesh.GetAllBoneWeights();
                try
                {
                    Assert.AreEqual(3, bonesPerVertex.Length);
                Assert.AreEqual(3, boneWeights.Length);
                }
                finally
                {
                    if (bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
                    if (boneWeights.IsCreated) boneWeights.Dispose();
                }
                Assert.That(outputMesh.GetSubMesh(0).bounds.size.x, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(outputMesh.GetSubMesh(0).bounds.size.y, Is.EqualTo(1f).Within(1e-6f));

                // A production UMA can make the prior result non-readable. The next MeshData
                // rebuild must replace its buffers without reading or pre-mutating that mesh.
                outputMesh.UploadMeshData(true);
                Assert.IsFalse(outputMesh.isReadable);
                SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                    renderer,
                    new[] { source },
                    umaData,
                    0,
                    1024,
                    new Dictionary<string, float>(),
                    Quaternion.identity,
                    false,
                    false);
                Assert.AreSame(outputMesh, renderer.sharedMesh);
                Assert.AreEqual(3, renderer.sharedMesh.vertexCount);
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, renderer.sharedMesh.GetIndices(0));
            }
            finally
            {
                asset.meshData?.submeshes?[0]?.DisposeNativeTriangles(true);
                if (outputMesh != null) UnityEngine.Object.DestroyImmediate(outputMesh);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(3)]
        [TestCase(4000)]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void AtlasUVRemapWaitsForBoundsJobAndCompletes(int vertexCount)
        {
            var gameObject = new GameObject("Atlas UV dependency test");
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            var umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
            Mesh outputMesh = null;
            bool previousParallelUV = SkinnedMeshCombinerMeshAPI.UseParallelUVRemap;
            try
            {
                var vertices = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var uvs = new Vector2[vertexCount];
                var bonesPerVertex = new byte[vertexCount];
                var boneWeights = new BoneWeight1[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    vertices[i] = new Vector3(i % 100, i / 100, 0f);
                    normals[i] = Vector3.forward;
                    uvs[i] = new Vector2(0.25f, 0.75f);
                    bonesPerVertex[i] = 1;
                    boneWeights[i] = new BoneWeight1 { boneIndex = 0, weight = 1f };
                }

                asset.name = "ParallelAtlasSlot";
                asset.subMeshIndex = 0;
                asset.meshData = new UMAMeshData
                {
                    SlotName = asset.slotName,
                    vertexCount = vertexCount,
                    vertices = vertices,
                    normals = normals,
                    uv = uvs,
                    subMeshCount = 1,
                    submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) },
                    bindPoses = new[] { Matrix4x4.identity },
                    boneNameHashes = new[] { 123 },
                    umaBones = new[] { new UMATransform { hash = 123, name = "root" } },
                    ManagedBonesPerVertex = bonesPerVertex,
                    ManagedBoneWeights = boneWeights
                };

                var slot = new SlotData(asset);
                var source = new SkinnedMeshCombiner.CombineInstance
                {
                    meshData = asset.meshData,
                    slotData = slot,
                    targetSubmeshIndices = new[] { 0 }
                };
                var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                var umaData = gameObject.AddComponent<UMAData>();
                umaData.umaRecipe = new UMAData.UMARecipe();

                umaMaterial.materialType = UMAMaterial.MaterialType.Atlas;
                var generatedMaterial = new UMAData.GeneratedMaterial
                {
                    umaMaterial = umaMaterial,
                    rendererAsset = null,
                    cropResolution = new Vector2(1024f, 1024f),
                    resolutionScale = Vector2.one
                };
                generatedMaterial.materialFragments.Add(new UMAData.MaterialFragment
                {
                    slotData = slot,
                    atlasRegion = new Rect(256f, 128f, 512f, 256f),
                    overlayList = new List<OverlayData>()
                });
                umaData.generatedMaterials.materials.Add(generatedMaterial);

                SkinnedMeshCombinerMeshAPI.UseParallelUVRemap = true;
                Assert.DoesNotThrow(() => SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                    renderer,
                    new[] { source },
                    umaData,
                    0,
                    1024,
                    new Dictionary<string, float>(),
                    Quaternion.identity,
                    false,
                    false));

                outputMesh = renderer.sharedMesh;
                Assert.NotNull(outputMesh);
                Assert.AreEqual(vertexCount, outputMesh.vertexCount);
                var outputUVs = outputMesh.uv;
                Assert.That(outputUVs[0].x, Is.EqualTo(0.375f).Within(1e-6f));
                Assert.That(outputUVs[0].y, Is.EqualTo(0.3125f).Within(1e-6f));
                Assert.That(outputUVs[vertexCount - 1].x, Is.EqualTo(0.375f).Within(1e-6f));
                Assert.That(outputUVs[vertexCount - 1].y, Is.EqualTo(0.3125f).Within(1e-6f));
            }
            finally
            {
                SkinnedMeshCombinerMeshAPI.UseParallelUVRemap = previousParallelUV;
                asset.meshData?.submeshes?[0]?.DisposeNativeTriangles(true);
                if (outputMesh != null) UnityEngine.Object.DestroyImmediate(outputMesh);
                UnityEngine.Object.DestroyImmediate(umaMaterial);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void LargeModifierStackIsCompactedAndRecalculatesSurfaceFrames()
        {
            var gameObject =
                new GameObject("root");
            var asset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            Mesh outputMesh = null;
            bool previousParallelModifiers =
                SkinnedMeshCombinerMeshAPI
                    .UseParallelMeshModifiers;
            try
            {
                int rootHash =
                    UMAUtils.StringToHash("root");
                asset.name = "LargeModifierStackSlot";
                asset.subMeshIndex = 0;
                asset.meshData = new UMAMeshData
                {
                    SlotName = asset.slotName,
                    vertexCount = 4,
                    vertices = new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(1f, 0f, 0f),
                        new Vector3(0f, 1f, 0f),
                        new Vector3(1f, 1f, 0f)
                    },
                    normals = new[]
                    {
                        Vector3.forward,
                        Vector3.forward,
                        Vector3.forward,
                        Vector3.forward
                    },
                    tangents = new[]
                    {
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f)
                    },
                    uv = new[]
                    {
                        new Vector2(0f, 0f),
                        new Vector2(1f, 0f),
                        new Vector2(0f, 1f),
                        new Vector2(1f, 1f)
                    },
                    subMeshCount = 1,
                    submeshes = new[]
                    {
                        new SubMeshTriangles(
                            new[] { 0, 1, 2, 2, 1, 3 })
                    },
                    bindPoses = new[] { Matrix4x4.identity },
                    boneNameHashes = new[] { rootHash },
                    umaBones = new[]
                    {
                        new UMATransform
                        {
                            hash = rootHash,
                            name = "root"
                        }
                    },
                    ManagedBonesPerVertex =
                        new byte[] { 1, 1, 1, 1 },
                    ManagedBoneWeights = new[]
                    {
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        },
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        },
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        },
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        }
                    }
                };

                var slot = new SlotData(asset);
                const int modifierCount = 64;
                const int adjustmentsPerModifier = 64;
                float deltaPerAdjustment =
                    0.5f /
                    (modifierCount *
                     adjustmentsPerModifier);
                for (int modifierIndex = 0;
                     modifierIndex < modifierCount;
                     modifierIndex++)
                {
                    var adjustments =
                        new VertexDeltaAdjustmentCollection();
                    for (int adjustmentIndex = 0;
                         adjustmentIndex <
                         adjustmentsPerModifier;
                         adjustmentIndex++)
                    {
                        adjustments.vertexAdjustments.Add(
                            new VertexDeltaAdjustment
                            {
                                vertexIndex = 0,
                                weight = 1f,
                                delta =
                                    new Vector3(
                                        0f,
                                        0f,
                                        deltaPerAdjustment)
                            });
                    }
                    slot.meshModifiers.Add(
                        new MeshModifier.Modifier
                        {
                            Scale = 1f,
                            adjustments = adjustments
                        });
                }

                var source =
                    new SkinnedMeshCombiner.CombineInstance
                    {
                        meshData = asset.meshData,
                        slotData = slot,
                        targetSubmeshIndices =
                            new[] { 0 },
                        applyMeshModifiersInJobs = true
                    };
                var renderer =
                    gameObject
                        .AddComponent<SkinnedMeshRenderer>();
                renderer.rootBone = gameObject.transform;
                var umaData =
                    gameObject.AddComponent<UMAData>();
                umaData.umaRoot = gameObject;
                umaData.umaRecipe =
                    new UMAData.UMARecipe
                    {
                        slotDataList = new[] { slot }
                    };
                umaData.skeleton =
                    new UMASkeleton(gameObject.transform);

                SkinnedMeshCombinerMeshAPI
                    .UseParallelMeshModifiers = true;
                SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                    renderer,
                    new[] { source },
                    umaData,
                    0,
                    1024,
                    new Dictionary<string, float>(),
                    Quaternion.identity,
                    false,
                    false);

                outputMesh = renderer.sharedMesh;
                Assert.NotNull(outputMesh);
                Vector3[] outputVertices =
                    outputMesh.vertices;
                Assert.That(
                    outputVertices[0].z,
                    Is.EqualTo(0.5f).Within(1e-5f));

                Vector3 expectedNormal =
                    new Vector3(0.5f, 0.5f, 1f)
                        .normalized;
                Vector3[] outputNormals =
                    outputMesh.normals;
                Assert.That(
                    Vector3.Distance(
                        outputNormals[0],
                        expectedNormal),
                    Is.LessThan(1e-5f));
                Vector4 outputTangent =
                    outputMesh.tangents[0];
                Vector3 tangentDirection =
                    new Vector3(
                        outputTangent.x,
                        outputTangent.y,
                        outputTangent.z);
                Assert.That(
                    tangentDirection.magnitude,
                    Is.EqualTo(1f).Within(1e-5f));
                Assert.That(
                    Mathf.Abs(
                        Vector3.Dot(
                            outputNormals[0],
                            tangentDirection)),
                    Is.LessThan(1e-5f));
            }
            finally
            {
                SkinnedMeshCombinerMeshAPI
                    .UseParallelMeshModifiers =
                    previousParallelModifiers;
                asset.meshData?.submeshes?[0]
                    ?.DisposeNativeTriangles(true);
                if (outputMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        outputMesh);
                }
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(
                    gameObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void PendingLargeModifierJobsDisposeAllNativeResources()
        {
            var gameObject = new GameObject("root");
            var asset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            Mesh outputMesh = null;
            SkinnedMeshCombinerMeshAPI.PendingCombine pending =
                null;
            try
            {
                int rootHash =
                    UMAUtils.StringToHash("root");
                asset.name = "PendingModifierCleanupSlot";
                asset.subMeshIndex = 0;
                asset.meshData = new UMAMeshData
                {
                    SlotName = asset.slotName,
                    vertexCount = 3,
                    vertices = new[]
                    {
                        Vector3.zero,
                        Vector3.right,
                        Vector3.up
                    },
                    normals = new[]
                    {
                        Vector3.forward,
                        Vector3.forward,
                        Vector3.forward
                    },
                    tangents = new[]
                    {
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f)
                    },
                    uv = new[]
                    {
                        Vector2.zero,
                        Vector2.right,
                        Vector2.up
                    },
                    subMeshCount = 1,
                    submeshes = new[]
                    {
                        new SubMeshTriangles(
                            new[] { 0, 1, 2 })
                    },
                    bindPoses = new[] { Matrix4x4.identity },
                    boneNameHashes = new[] { rootHash },
                    umaBones = new[]
                    {
                        new UMATransform
                        {
                            hash = rootHash,
                            name = "root"
                        }
                    },
                    ManagedBonesPerVertex =
                        new byte[] { 1, 1, 1 },
                    ManagedBoneWeights = new[]
                    {
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        },
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        },
                        new BoneWeight1
                        {
                            boneIndex = 0,
                            weight = 1f
                        }
                    }
                };

                var slot = new SlotData(asset);
                var adjustments =
                    new VertexDeltaAdjustmentCollection();
                for (int i = 0; i < 4096; i++)
                {
                    adjustments.vertexAdjustments.Add(
                        new VertexDeltaAdjustment
                        {
                            vertexIndex = i % 3,
                            weight = 1f,
                            delta =
                                new Vector3(
                                    0f,
                                    0f,
                                    0.00001f)
                        });
                }
                slot.meshModifiers.Add(
                    new MeshModifier.Modifier
                    {
                        Scale = 1f,
                        adjustments = adjustments
                    });
                var renderer =
                    gameObject
                        .AddComponent<SkinnedMeshRenderer>();
                outputMesh =
                    new Mesh
                    {
                        name =
                            "Pending modifier cleanup mesh"
                    };
                renderer.sharedMesh = outputMesh;
                renderer.rootBone = gameObject.transform;
                var data = gameObject.AddComponent<UMAData>();
                data.umaRoot = gameObject;
                data.umaRecipe =
                    new UMAData.UMARecipe
                    {
                        slotDataList = new[] { slot }
                    };
                data.skeleton =
                    new UMASkeleton(gameObject.transform);

                pending =
                    SkinnedMeshCombinerMeshAPI
                        .PrepareIncrementalCombine(
                            new SkinnedMeshCombinerMeshAPI
                                .RendererBatch
                            {
                                Renderer = renderer,
                                Sources = new[]
                                {
                                    new SkinnedMeshCombiner
                                        .CombineInstance
                                    {
                                        meshData =
                                            asset.meshData,
                                        slotData = slot,
                                        targetSubmeshIndices =
                                            new[] { 0 },
                                        applyMeshModifiersInJobs =
                                            true
                                    }
                                },
                                CurrentRendererIndex = 0,
                                AtlasResolution = 512
                            },
                            data,
                            new Dictionary<string, float>(),
                            false,
                            false,
                            Quaternion.identity);

                Assert.DoesNotThrow(() => pending.Dispose());
                pending = null;
            }
            finally
            {
                pending?.Dispose();
                asset.meshData?.submeshes?[0]
                    ?.DisposeNativeTriangles(true);
                if (outputMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        outputMesh);
                }
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(
                    gameObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        public void EmptyRendererCleanupRemovesPreviousMeshState()
        {
            var gameObject = new GameObject("Jobified combiner cleanup test");
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.AddBlendShapeFrame(
                    "PreviousShape",
                    100f,
                    new[] { Vector3.forward, Vector3.zero, Vector3.zero },
                    new Vector3[3],
                    new Vector3[3]);

                var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterials = new Material[1];
                renderer.bones = new[] { gameObject.transform };

                var combiner = gameObject.AddComponent<UMAJobifiedMeshCombiner>();
                var renderersField = typeof(UMAJobifiedMeshCombiner).GetField("renderers", BindingFlags.Instance | BindingFlags.NonPublic);
                var clearMethod = typeof(UMAJobifiedMeshCombiner).GetMethod("ClearRendererState", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(renderersField);
                Assert.NotNull(clearMethod);
                renderersField.SetValue(combiner, new[] { renderer });

                clearMethod.Invoke(combiner, new object[] { 0 });

                Assert.AreEqual(0, renderer.sharedMesh.vertexCount);
                Assert.AreEqual(0, renderer.sharedMesh.blendShapeCount);
                Assert.AreEqual(0, renderer.sharedMaterials.Length);
                Assert.AreEqual(0, renderer.bones.Length);
                Assert.AreEqual(Vector3.zero, renderer.localBounds.size);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void AssertDescriptor(VertexAttributeDescriptor descriptor, VertexAttribute attribute, VertexAttributeFormat format, int dimension, int stream)
        {
            Assert.AreEqual(attribute, descriptor.attribute);
            Assert.AreEqual(format, descriptor.format);
            Assert.AreEqual(dimension, descriptor.dimension);
            Assert.AreEqual(stream, descriptor.stream);
        }
    }
}

#endif
