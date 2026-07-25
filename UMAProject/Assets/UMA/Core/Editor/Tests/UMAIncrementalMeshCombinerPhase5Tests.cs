#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    public sealed class UMAIncrementalMeshCombinerPhase5Tests
    {
        private static readonly int RootHash =
            UMAUtils.StringToHash("root");

        [SetUp]
        public void SetUp()
        {
            SkinnedMeshCombiner.StaticInitializeOnLoad();
            SkinnedMeshCombinerMeshAPI.ResetTimings();
        }

        [TearDown]
        public void TearDown()
        {
            UMAMeshData.CleanupGlobalBuffers();
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshCombiner")]
        public void PersistentPendingCombineBuildsEquivalentDetachedBaseMesh()
        {
            GameObject root = null;
            SlotDataAsset asset = null;
            Mesh sentinelMesh = null;
            Mesh outputMesh = null;
            SkinnedMeshCombinerMeshAPI.PendingCombine pending = null;
            try
            {
                root = new GameObject("root");
                asset = CreateSlotAsset("IncrementalPersistentSlot");
                var managedSlot = new SlotData(asset);
                var incrementalSlot = new SlotData(asset);
                var managedSource = CreateSource(
                    asset.meshData,
                    managedSlot);
                var incrementalSource = CreateSource(
                    asset.meshData,
                    incrementalSlot);
                var recipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { incrementalSlot }
                };
                var settings = new BlendShapeSettings
                {
                    ignoreBlendShapes = true
                };

                var expectedMeshData = new UMAMeshData();
                SkinnedMeshCombiner.CombineMeshes(
                    expectedMeshData,
                    new[] { managedSource },
                    settings,
                    recipe,
                    0);

                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                sentinelMesh = new Mesh { name = "Live sentinel mesh" };
                renderer.sharedMesh = sentinelMesh;
                renderer.rootBone = root.transform;

                var data = root.AddComponent<UMAData>();
                data.umaRoot = root;
                data.skeleton = new UMASkeleton(root.transform);
                data.umaRecipe = recipe;
                data.blendShapeSettings = settings;
                data.force32bit = false;

                pending =
                    SkinnedMeshCombinerMeshAPI.PrepareIncrementalCombine(
                        new SkinnedMeshCombinerMeshAPI.RendererBatch
                        {
                            Renderer = renderer,
                            Sources = new[] { incrementalSource },
                            CurrentRendererIndex = 0,
                            AtlasResolution = 512,
                            SkipSkeletonUpdate = false
                        },
                        data,
                        new Dictionary<string, float>(),
                        false,
                        false,
                        Quaternion.identity);

                Assert.AreEqual(
                    Allocator.Persistent,
                    pending.NativeAllocator);
                pending.CompleteJobs();

                outputMesh = new Mesh { name = "Detached output mesh" };
                pending.ApplyPreparedBaseMesh(outputMesh);

                Assert.AreSame(
                    sentinelMesh,
                    renderer.sharedMesh,
                    "Applying the prepared base mesh must not mutate the live renderer.");
                UMAMeshBaselineSnapshot expected =
                    UMAMeshBaselineSnapshot.Capture(
                        "Managed base mesh",
                        expectedMeshData,
                        IndexFormat.UInt16);
                UMAMeshBaselineSnapshot actual =
                    UMAMeshBaselineSnapshot.Capture(
                        "Incremental detached base mesh",
                        outputMesh);
                UMAMeshBaselineSnapshot.AssertEquivalent(expected, actual);
            }
            finally
            {
                pending?.Dispose();
                DisposeSlotTriangles(asset);
                if (outputMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(outputMesh);
                }
                if (sentinelMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(sentinelMesh);
                }
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshCombiner")]
        public void IncrementalCombinerSynchronousContractCommitsEquivalentMesh()
        {
            GameObject root = null;
            GameObject oldRendererObject = null;
            SlotDataAsset asset = null;
            RaceData race = null;
            UMAMaterial umaMaterial = null;
            Material material = null;
            Mesh oldMesh = null;
            Mesh committedMesh = null;
            try
            {
                root = new GameObject("root");
                asset = CreateSlotAsset("IncrementalComponentSlot");
                asset.meshData.blendShapes = CreateBlendShapes();
                race = ScriptableObject.CreateInstance<RaceData>();
                race.name = "IncrementalComponentRace";
                race.useNewDNA = false;
                var slot = new SlotData(asset);
                slot.skinnedMeshRenderer = 17;
                slot.submeshIndex = 18;
                slot.vertexOffset = 19;
                slot.UVArea = new Rect(0.2f, 0.3f, 0.4f, 0.5f);
                var recipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { slot }
                };
                recipe.SetRace(race);

                var expectedMeshData = new UMAMeshData();
                SkinnedMeshCombiner.CombineMeshes(
                    expectedMeshData,
                    new[] { CreateSource(asset.meshData, new SlotData(asset)) },
                    new BlendShapeSettings
                    {
                        ignoreBlendShapes = false,
                        loadAllFrames = true,
                        loadNormals = true,
                        loadTangents = true
                    },
                    recipe,
                    0);

                oldRendererObject = new GameObject("Old live renderer");
                oldRendererObject.transform.SetParent(root.transform, false);
                var oldRenderer =
                    oldRendererObject.AddComponent<SkinnedMeshRenderer>();
                oldMesh = new Mesh { name = "Old live mesh" };
                oldRenderer.sharedMesh = oldMesh;
                oldRenderer.rootBone = root.transform;

                var data = root.AddComponent<UMAData>();
                data.umaRoot = root;
                data.skeleton = new UMASkeleton(root.transform);
                data.umaRecipe = recipe;
                data.blendShapeSettings = new BlendShapeSettings
                {
                    ignoreBlendShapes = false,
                    loadAllFrames = true,
                    loadNormals = true,
                    loadTangents = true
                };
                data.markNotReadable = false;
                data.SetRenderers(new[] { oldRenderer });
                data.SetRendererAssets(new UMARendererAsset[] { null });

                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.NotNull(shader);
                material = new Material(shader);
                umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
                umaMaterial.materialType = UMAMaterial.MaterialType.NoAtlas;
                umaMaterial.material = material;
                var generatedMaterial = new UMAData.GeneratedMaterial
                {
                    umaMaterial = umaMaterial,
                    material = material,
                    rendererAsset = null,
                    cropResolution = new Vector2(512f, 512f),
                    resolutionScale = Vector2.one
                };
                generatedMaterial.materialIndex = 20;
                generatedMaterial.materialFragments.Add(
                    new UMAData.MaterialFragment
                    {
                        slotData = slot,
                        atlasRegion = new Rect(0f, 0f, 512f, 512f),
                        overlayList = new List<OverlayData>()
                    });
                data.generatedMaterials.materials.Add(generatedMaterial);
                data.generatedMaterials.rendererAssets.Add(null);

                var combiner =
                    root.AddComponent<UMAIncrementalMeshCombiner>();
                combiner.UpdateUMAMesh(false, data, 512);

                Assert.AreEqual(1, data.RendererCount);
                SkinnedMeshRenderer committedRenderer = data.GetRenderer(0);
                Assert.NotNull(committedRenderer);
                Assert.AreNotSame(oldRenderer, committedRenderer);
                Assert.IsTrue(oldRenderer == null);
                committedMesh = committedRenderer.sharedMesh;
                Assert.NotNull(committedMesh);
                Assert.AreEqual(2, committedMesh.blendShapeCount);
                Assert.AreEqual(
                    2,
                    committedMesh.GetBlendShapeFrameCount(0));
                Assert.AreSame(
                    committedRenderer,
                    generatedMaterial.skinnedMeshRenderer);
                Assert.AreEqual(0, generatedMaterial.materialIndex);
                Assert.AreEqual(0, slot.skinnedMeshRenderer);
                Assert.AreEqual(0, slot.submeshIndex);
                Assert.AreEqual(0, slot.vertexOffset);
                Assert.AreEqual(
                    new Rect(0.2f, 0.3f, 0.4f, 0.5f),
                    slot.UVArea,
                    "A non-atlas update must preserve the previous UV area.");

                UMAMeshBaselineSnapshot expected =
                    UMAMeshBaselineSnapshot.Capture(
                        "Managed committed mesh",
                        expectedMeshData,
                        IndexFormat.UInt16);
                UMAMeshBaselineSnapshot actual =
                    UMAMeshBaselineSnapshot.Capture(
                        "Incremental committed mesh",
                        committedMesh);
                UMAMeshBaselineSnapshot.AssertEquivalent(expected, actual);
            }
            finally
            {
                DisposeSlotTriangles(asset);
                if (committedMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(committedMesh);
                }
                if (oldMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldMesh);
                }
                if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
                if (umaMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(umaMaterial);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                if (oldRendererObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldRendererObject);
                }
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshCombiner")]
        [Category("IncrementalBlendShapes")]
        public void BlendshapeLoaderAddsExactlyOneFramePerStepAndMatchesBaseline()
        {
            GameObject root = null;
            SlotDataAsset asset = null;
            Mesh outputMesh = null;
            SkinnedMeshCombinerMeshAPI.PendingCombine pending = null;
            SkinnedMeshCombinerMeshAPI.IncrementalBlendShapeLoader
                loader = null;
            try
            {
                root = new GameObject("root");
                asset = CreateSlotAsset("IncrementalBlendshapeSlot");
                asset.meshData.blendShapes = CreateBlendShapes();
                var managedSlot = new SlotData(asset);
                var incrementalSlot = new SlotData(asset);
                var recipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { incrementalSlot }
                };
                var settings = new BlendShapeSettings
                {
                    ignoreBlendShapes = false,
                    loadAllFrames = true,
                    loadNormals = true,
                    loadTangents = true
                };

                var expectedMeshData = new UMAMeshData();
                SkinnedMeshCombiner.CombineMeshes(
                    expectedMeshData,
                    new[]
                    {
                        CreateSource(asset.meshData, managedSlot)
                    },
                    settings,
                    recipe,
                    0);

                var renderer =
                    root.AddComponent<SkinnedMeshRenderer>();
                outputMesh =
                    new Mesh { name = "Incremental shape output" };
                renderer.sharedMesh = outputMesh;
                renderer.rootBone = root.transform;
                var data = root.AddComponent<UMAData>();
                data.umaRoot = root;
                data.skeleton = new UMASkeleton(root.transform);
                data.umaRecipe = recipe;
                data.blendShapeSettings = settings;

                pending =
                    SkinnedMeshCombinerMeshAPI
                        .PrepareIncrementalCombine(
                            new SkinnedMeshCombinerMeshAPI
                                .RendererBatch
                            {
                                Renderer = renderer,
                                Sources = new[]
                                {
                                    CreateSource(
                                        asset.meshData,
                                        incrementalSlot)
                                },
                                CurrentRendererIndex = 0,
                                AtlasResolution = 512,
                                SkipSkeletonUpdate = false
                            },
                            data,
                            new Dictionary<string, float>(),
                            false,
                            false,
                            Quaternion.identity);
                loader =
                    pending.CreateIncrementalBlendShapeLoader();
                Assert.AreEqual(3, loader.TotalFrameCount);
                Assert.IsFalse(loader.IsInitialized);
                loader.CompletePreparation();
                Assert.IsTrue(loader.IsInitialized);

                pending.CompleteJobs();
                pending.ApplyPreparedBaseMesh(outputMesh);
                Assert.AreEqual(0, outputMesh.blendShapeCount);

                int stepCount = 0;
                while (!loader.IsComplete)
                {
                    loader.CompletePreparation();
                    int framesBefore = loader.AppliedFrameCount;
                    UMAMeshCombineStepResult result =
                        loader.Step(outputMesh);
                    Assert.AreEqual(
                        framesBefore + 1,
                        loader.AppliedFrameCount,
                        "One loader step must call AddBlendShapeFrame at most once.");
                    Assert.That(
                        result.Status,
                        Is.EqualTo(UMAMeshCombineStatus.InProgress)
                            .Or.EqualTo(
                                UMAMeshCombineStatus.Completed));
                    stepCount++;
                }

                Assert.AreEqual(3, stepCount);
                Assert.AreEqual(3, loader.AppliedFrameCount);
                Assert.AreEqual(
                    3L,
                    SkinnedMeshCombinerMeshAPI
                        .BlendShapeFramesPrepared);
                Assert.AreEqual(
                    3L,
                    SkinnedMeshCombinerMeshAPI
                        .BlendShapeFramesApplied);

                pending
                    .FinalizePreparedRendererWithoutBlendShapes(
                        outputMesh);
                UMAMeshBaselineSnapshot expected =
                    UMAMeshBaselineSnapshot.Capture(
                        "Managed blendshape output",
                        expectedMeshData,
                        IndexFormat.UInt16);
                UMAMeshBaselineSnapshot actual =
                    UMAMeshBaselineSnapshot.Capture(
                        "Incremental blendshape output",
                        outputMesh);
                UMAMeshBaselineSnapshot.AssertEquivalent(expected, actual);
            }
            finally
            {
                loader?.Dispose();
                pending?.Dispose();
                DisposeSlotTriangles(asset);
                if (outputMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(outputMesh);
                }
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshCombiner")]
        [Category("AtomicCommit")]
        public void CancellationRestoresMetadataAndKeepsPreviousRendererLive()
        {
            GameObject root = null;
            GameObject oldRendererObject = null;
            SlotDataAsset asset = null;
            RaceData race = null;
            UMAMaterial umaMaterial = null;
            Material material = null;
            Material originalSecondPass = null;
            Mesh oldMesh = null;
            IUMAMeshCombineOperation operation = null;
            try
            {
                root = new GameObject("root");
                asset = CreateSlotAsset("IncrementalCancellationSlot");
                race = ScriptableObject.CreateInstance<RaceData>();
                race.name = "IncrementalCancellationRace";
                race.useNewDNA = false;
                var slot = new SlotData(asset)
                {
                    skinnedMeshRenderer = 31,
                    submeshIndex = 32,
                    vertexOffset = 33,
                    uvAreaUpdateFrame = 34,
                    UVArea = new Rect(0.11f, 0.22f, 0.33f, 0.44f)
                };
                var recipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { slot }
                };
                recipe.SetRace(race);

                oldRendererObject = new GameObject("Previous renderer");
                oldRendererObject.transform.SetParent(root.transform, false);
                var oldRenderer =
                    oldRendererObject.AddComponent<SkinnedMeshRenderer>();
                oldMesh = new Mesh { name = "Previous mesh" };
                oldRenderer.sharedMesh = oldMesh;
                oldRenderer.rootBone = root.transform;

                var data = root.AddComponent<UMAData>();
                data.umaRoot = root;
                data.skeleton = new UMASkeleton(root.transform);
                data.umaRecipe = recipe;
                data.blendShapeSettings = new BlendShapeSettings
                {
                    ignoreBlendShapes = true
                };
                data.markNotReadable = false;
                data.SetRenderers(new[] { oldRenderer });
                data.SetRendererAssets(new UMARendererAsset[] { null });

                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.NotNull(shader);
                material = new Material(shader);
                originalSecondPass = new Material(shader);
                umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
                umaMaterial.materialType = UMAMaterial.MaterialType.Atlas;
                umaMaterial.material = material;
                var generatedMaterial = new UMAData.GeneratedMaterial
                {
                    umaMaterial = umaMaterial,
                    material = material,
                    secondPassMaterial = originalSecondPass,
                    rendererAsset = null,
                    materialIndex = 35,
                    skinnedMeshRenderer = oldRenderer,
                    cropResolution = new Vector2(512f, 512f),
                    resolutionScale = Vector2.one
                };
                generatedMaterial.materialFragments.Add(
                    new UMAData.MaterialFragment
                    {
                        slotData = slot,
                        atlasRegion = new Rect(128f, 64f, 256f, 128f),
                        overlayList = new List<OverlayData>()
                    });
                data.generatedMaterials.materials.Add(generatedMaterial);
                data.generatedMaterials.rendererAssets.Add(null);

                var combiner =
                    root.AddComponent<UMAIncrementalMeshCombiner>();
                operation =
                    combiner.BeginUpdateUMAMesh(true, data, 512);

                var diagnostics =
                    operation as IUMAMeshCombineOperationDiagnostics;
                Assert.NotNull(diagnostics);
                var observedPreparationSteps =
                    new List<string>();
                int preparationAttempts = 0;
                while (diagnostics.AtomicStepName !=
                       "Create BlendShape Loader")
                {
                    Assert.Less(
                        preparationAttempts++,
                        10000,
                        "Incremental renderer preparation did not reach blendshape-loader creation.");
                    observedPreparationSteps.Add(
                        diagnostics.AtomicStepName);
                    UMAMeshCombineStepResult result =
                        operation.Step(
                            UMAMeshCombineTimeSlice.Unlimited);
                    Assert.AreNotEqual(
                        UMAMeshCombineStatus.Failed,
                        result.Status,
                        result.Error?.ToString());
                    AssertOriginalMetadata();
                    if (result.Status ==
                        UMAMeshCombineStatus.WaitingForAsync)
                    {
                        Thread.Yield();
                    }
                }
                Assert.AreEqual(
                    "Create BlendShape Loader",
                    diagnostics.AtomicStepName);
                CollectionAssert.Contains(
                    observedPreparationSteps,
                    "Build Plan: Validate Inputs");
                CollectionAssert.Contains(
                    observedPreparationSteps,
                    "Build Plan: Renderer 0");
                CollectionAssert.Contains(
                    observedPreparationSteps,
                    "Prepare Renderer: Analyze");
                CollectionAssert.Contains(
                    observedPreparationSteps,
                    "Prepare Renderer: Allocate MeshData");
                CollectionAssert.Contains(
                    observedPreparationSteps,
                    "Prepare Renderer: Source 0");
                var preparationTimingNames = new List<string>();
                while (diagnostics.TryDequeueCompletedTiming(
                           out UMAMeshCombineStepTiming timing))
                {
                    preparationTimingNames.Add(timing.StepName);
                    Assert.GreaterOrEqual(timing.StopwatchTicks, 0L);
                }
                CollectionAssert.Contains(
                    preparationTimingNames,
                    "Prepare: Source Analysis");
                CollectionAssert.Contains(
                    preparationTimingNames,
                    "Prepare: MeshData Allocation");
                CollectionAssert.Contains(
                    preparationTimingNames,
                    "Prepare: Bone/Modifier Jobs");
                CollectionAssert.Contains(
                    preparationTimingNames,
                    "Prepare: Other Setup and Allocation");
                CollectionAssert.Contains(
                    preparationTimingNames,
                    "Prepare: Capture Slot Metadata");

                operation.Cancel();
                UMAMeshCombineStatus cancellationStatus;
                int cancellationAttempts = 0;
                do
                {
                    Assert.Less(
                        cancellationAttempts++,
                        10000,
                        "Cancellation did not reach a safe terminal boundary.");
                    cancellationStatus = operation.Step(
                        UMAMeshCombineTimeSlice.Unlimited).Status;
                    if (cancellationStatus ==
                        UMAMeshCombineStatus.WaitingForAsync)
                    {
                        Thread.Yield();
                    }
                }
                while (cancellationStatus ==
                       UMAMeshCombineStatus.WaitingForAsync);
                Assert.AreEqual(
                    UMAMeshCombineStatus.Cancelled,
                    cancellationStatus);
                operation.Dispose();
                operation = null;

                AssertOriginalMetadata();
                Assert.AreSame(oldRenderer, data.GetRenderer(0));
                Assert.AreSame(oldMesh, oldRenderer.sharedMesh);
                Assert.IsTrue(oldRenderer.enabled);

                // Run a second transaction to its final commit boundary, then
                // invalidate an input that material application requires. The
                // detached build must fail without replacing the old renderer
                // or publishing staged slot/material metadata.
                operation =
                    combiner.BeginUpdateUMAMesh(true, data, 512);
                int attempts = 0;
                var secondRunSteps = new List<string>();
                while (operation.StageName != "Commit")
                {
                    Assert.Less(
                        attempts++,
                        10000,
                        "The failure-injection operation did not reach commit.");
                    var secondDiagnostics =
                        operation as
                            IUMAMeshCombineOperationDiagnostics;
                    if (secondDiagnostics != null)
                    {
                        secondRunSteps.Add(
                            secondDiagnostics.AtomicStepName);
                    }
                    UMAMeshCombineStepResult stepResult =
                        operation.Step(
                            UMAMeshCombineTimeSlice.Unlimited);
                    if (stepResult.Status ==
                        UMAMeshCombineStatus.WaitingForAsync)
                    {
                        Thread.Yield();
                    }
                    else if (stepResult.Status ==
                             UMAMeshCombineStatus.Failed)
                    {
                        throw stepResult.Error;
                    }
                }
                CollectionAssert.Contains(
                    secondRunSteps,
                    "Apply Base Mesh: Prepare Output");
                CollectionAssert.Contains(
                    secondRunSteps,
                    "Apply Base Mesh: Writable MeshData");
                CollectionAssert.Contains(
                    secondRunSteps,
                    "Apply Base Mesh: Skinning");

                generatedMaterial.umaMaterial = null;
                UMAMeshCombineStepResult failedCommit =
                    operation.Step(
                        UMAMeshCombineTimeSlice.Unlimited);
                generatedMaterial.umaMaterial = umaMaterial;
                Assert.AreEqual(
                    UMAMeshCombineStatus.Failed,
                    failedCommit.Status);
                Assert.NotNull(failedCommit.Error);
                operation.Dispose();
                operation = null;

                AssertOriginalMetadata();
                Assert.AreSame(oldRenderer, data.GetRenderer(0));
                Assert.AreSame(oldMesh, oldRenderer.sharedMesh);
                Assert.IsTrue(oldRenderer.enabled);

                void AssertOriginalMetadata()
                {
                    Assert.AreEqual(31, slot.skinnedMeshRenderer);
                    Assert.AreEqual(32, slot.submeshIndex);
                    Assert.AreEqual(33, slot.vertexOffset);
                    Assert.AreEqual(34, slot.uvAreaUpdateFrame);
                    Assert.AreEqual(
                        new Rect(0.11f, 0.22f, 0.33f, 0.44f),
                        slot.UVArea);
                    Assert.AreEqual(35, generatedMaterial.materialIndex);
                    Assert.AreSame(
                        oldRenderer,
                        generatedMaterial.skinnedMeshRenderer);
                    Assert.AreSame(
                        originalSecondPass,
                        generatedMaterial.secondPassMaterial);
                }
            }
            finally
            {
                operation?.Dispose();
                DisposeSlotTriangles(asset);
                if (oldMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldMesh);
                }
                if (originalSecondPass != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        originalSecondPass);
                }
                if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
                if (umaMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(umaMaterial);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                if (oldRendererObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        oldRendererObject);
                }
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("IncrementalMeshCombiner")]
        [Category("FeatureParity")]
        public void MultipleRenderersAtlasSecondPassClothAndEmptyRendererCommitCorrectly()
        {
            GameObject root = null;
            GameObject global = null;
            SlotDataAsset firstAsset = null;
            SlotDataAsset secondAsset = null;
            RaceData race = null;
            UMARendererAsset firstRendererAsset = null;
            UMARendererAsset secondRendererAsset = null;
            UMARendererAsset emptyRendererAsset = null;
            UMAMaterial firstUmaMaterial = null;
            UMAMaterial secondUmaMaterial = null;
            Material firstMaterial = null;
            Material secondMaterial = null;
            Material secondPassTemplate = null;
            UMAData.GeneratedMaterial firstGenerated = null;
            try
            {
                root = new GameObject("Multi-renderer UMA");
                global = new GameObject("Global");
                global.transform.SetParent(root.transform, false);
                firstAsset = CreateSlotAsset("IncrementalAtlasSlot");
                secondAsset = CreateSlotAsset("IncrementalNoAtlasSlot");
                RetargetSingleBone(firstAsset.meshData, "Global");
                RetargetSingleBone(secondAsset.meshData, "Global");
                for (int i = 0;
                     i < secondAsset.meshData.vertices.Length;
                     i++)
                {
                    secondAsset.meshData.vertices[i] +=
                        new Vector3(2f, 0f, 0f);
                }
                firstAsset.meshData.blendShapes = CreateBlendShapes();
                firstAsset.meshData.clothSkinningSerialized =
                    Repeat(new Vector2(0.25f, 0.75f), 4);

                race = ScriptableObject.CreateInstance<RaceData>();
                race.name = "IncrementalFeatureRace";
                race.useNewDNA = false;
                var firstSlot = new SlotData(firstAsset);
                var secondSlot = new SlotData(secondAsset);
                var recipe = new UMAData.UMARecipe
                {
                    slotDataList =
                        new[] { firstSlot, secondSlot }
                };
                recipe.SetRace(race);

                firstRendererAsset =
                    ScriptableObject.CreateInstance<UMARendererAsset>();
                secondRendererAsset =
                    ScriptableObject.CreateInstance<UMARendererAsset>();
                emptyRendererAsset =
                    ScriptableObject.CreateInstance<UMARendererAsset>();

                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.NotNull(shader);
                firstMaterial = new Material(shader);
                secondMaterial = new Material(shader);
                secondPassTemplate = new Material(shader);
                firstUmaMaterial =
                    ScriptableObject.CreateInstance<UMAMaterial>();
                firstUmaMaterial.materialType =
                    UMAMaterial.MaterialType.Atlas;
                firstUmaMaterial.material = firstMaterial;
                var firstUmaMaterialObject =
                    new UnityEditor.SerializedObject(firstUmaMaterial);
                firstUmaMaterialObject.FindProperty("_secondPass")
                    .objectReferenceValue = secondPassTemplate;
                firstUmaMaterialObject.ApplyModifiedPropertiesWithoutUndo();
                secondUmaMaterial =
                    ScriptableObject.CreateInstance<UMAMaterial>();
                secondUmaMaterial.materialType =
                    UMAMaterial.MaterialType.NoAtlas;
                secondUmaMaterial.material = secondMaterial;

                firstGenerated = CreateGeneratedMaterial(
                    firstSlot,
                    firstRendererAsset,
                    firstUmaMaterial,
                    firstMaterial,
                    new Rect(128f, 64f, 256f, 128f));
                var secondGenerated = CreateGeneratedMaterial(
                    secondSlot,
                    secondRendererAsset,
                    secondUmaMaterial,
                    secondMaterial,
                    new Rect(0f, 0f, 512f, 512f));

                var data = root.AddComponent<UMAData>();
                data.umaRoot = root;
                data.skeleton = new UMASkeleton(global.transform);
                data.umaRecipe = recipe;
                data.blendShapeSettings = new BlendShapeSettings
                {
                    ignoreBlendShapes = false,
                    loadAllFrames = true,
                    loadNormals = true,
                    loadTangents = true
                };
                data.markNotReadable = false;
                data.generatedMaterials.materials.Add(firstGenerated);
                data.generatedMaterials.materials.Add(secondGenerated);
                data.generatedMaterials.rendererAssets.Add(
                    firstRendererAsset);
                data.generatedMaterials.rendererAssets.Add(
                    secondRendererAsset);
                data.generatedMaterials.rendererAssets.Add(
                    emptyRendererAsset);

                var combiner =
                    root.AddComponent<UMAIncrementalMeshCombiner>();
                combiner.UpdateUMAMesh(true, data, 512);

                Assert.AreEqual(3, data.RendererCount);
                SkinnedMeshRenderer firstRenderer =
                    data.GetRenderer(0);
                SkinnedMeshRenderer secondRenderer =
                    data.GetRenderer(1);
                SkinnedMeshRenderer emptyRenderer =
                    data.GetRenderer(2);
                Assert.NotNull(firstRenderer);
                Assert.NotNull(secondRenderer);
                Assert.NotNull(emptyRenderer);
                Assert.AreEqual(4, firstRenderer.sharedMesh.vertexCount);
                Assert.AreEqual(4, secondRenderer.sharedMesh.vertexCount);
                Assert.AreEqual(0, emptyRenderer.sharedMesh.vertexCount);
                Assert.AreEqual(2, firstRenderer.sharedMesh.subMeshCount);
                Assert.AreEqual(2, firstRenderer.sharedMaterials.Length);
                Assert.AreEqual(1, secondRenderer.sharedMesh.subMeshCount);
                Assert.AreEqual(1, secondRenderer.sharedMaterials.Length);
                CollectionAssert.AreEqual(
                    firstRenderer.sharedMesh.GetIndices(0),
                    firstRenderer.sharedMesh.GetIndices(1));
                Assert.AreEqual(2, firstRenderer.sharedMesh.blendShapeCount);
                Assert.AreEqual(0, secondRenderer.sharedMesh.blendShapeCount);

                Vector2[] atlasUV = firstRenderer.sharedMesh.uv;
                Assert.That(atlasUV[0].x, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(atlasUV[0].y, Is.EqualTo(0.125f).Within(1e-6f));
                Assert.That(atlasUV[3].x, Is.EqualTo(0.75f).Within(1e-6f));
                Assert.That(atlasUV[3].y, Is.EqualTo(0.375f).Within(1e-6f));
                Assert.AreEqual(
                    new Rect(0.25f, 0.125f, 0.5f, 0.25f),
                    firstSlot.UVArea);
                Assert.AreEqual(0, firstSlot.skinnedMeshRenderer);
                Assert.AreEqual(1, secondSlot.skinnedMeshRenderer);
                Assert.AreSame(
                    firstRenderer,
                    firstGenerated.skinnedMeshRenderer);
                Assert.AreSame(
                    secondRenderer,
                    secondGenerated.skinnedMeshRenderer);

                Cloth cloth = firstRenderer.GetComponent<Cloth>();
                Assert.NotNull(cloth);
                Assert.AreEqual(4, cloth.coefficients.Length);
                Assert.IsNull(secondRenderer.GetComponent<Cloth>());
                Assert.AreEqual(
                    Vector3.zero,
                    emptyRenderer.localBounds.size);
                Assert.AreEqual(0, emptyRenderer.sharedMaterials.Length);
            }
            finally
            {
                if (root != null)
                {
                    SkinnedMeshRenderer[] renderers =
                        root.GetComponentsInChildren<SkinnedMeshRenderer>(
                            true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (renderers[i].sharedMesh != null)
                        {
                            UnityEngine.Object.DestroyImmediate(
                                renderers[i].sharedMesh);
                        }
                    }
                }
                DisposeSlotTriangles(firstAsset);
                DisposeSlotTriangles(secondAsset);
                if (firstGenerated?.secondPassMaterial != null &&
                    firstGenerated.secondPassMaterial != firstMaterial &&
                    firstGenerated.secondPassMaterial != secondPassTemplate)
                {
                    UnityEngine.Object.DestroyImmediate(
                        firstGenerated.secondPassMaterial);
                }
                if (secondPassTemplate != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        secondPassTemplate);
                }
                if (firstMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstMaterial);
                }
                if (secondMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondMaterial);
                }
                if (firstUmaMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstUmaMaterial);
                }
                if (secondUmaMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondUmaMaterial);
                }
                if (firstRendererAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstRendererAsset);
                }
                if (secondRendererAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondRendererAsset);
                }
                if (emptyRendererAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(emptyRendererAsset);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
                if (firstAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstAsset);
                }
                if (secondAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondAsset);
                }
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static UMAData.GeneratedMaterial CreateGeneratedMaterial(
            SlotData slot,
            UMARendererAsset rendererAsset,
            UMAMaterial umaMaterial,
            Material material,
            Rect atlasRegion)
        {
            var generated = new UMAData.GeneratedMaterial
            {
                umaMaterial = umaMaterial,
                material = material,
                rendererAsset = rendererAsset,
                cropResolution = new Vector2(512f, 512f),
                resolutionScale = Vector2.one
            };
            generated.materialFragments.Add(
                new UMAData.MaterialFragment
                {
                    slotData = slot,
                    atlasRegion = atlasRegion,
                    overlayList = new List<OverlayData>()
                });
            return generated;
        }

        private static void RetargetSingleBone(
            UMAMeshData meshData,
            string boneName)
        {
            int boneHash = UMAUtils.StringToHash(boneName);
            meshData.boneNameHashes[0] = boneHash;
            meshData.umaBones[0].hash = boneHash;
            meshData.umaBones[0].name = boneName;
            meshData.rootBoneHash = boneHash;
            meshData.RootBoneName = boneName;
        }

        private static SlotDataAsset CreateSlotAsset(string name)
        {
            var asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            asset.name = name;
            asset.subMeshIndex = 0;
            asset.meshData = new UMAMeshData
            {
                SlotName = name,
                vertexCount = 4,
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 1f, 0f)
                },
                normals = Repeat(Vector3.forward, 4),
                tangents = Repeat(
                    new Vector4(1f, 0f, 0f, 1f),
                    4),
                colors32 = Repeat(
                    new Color32(200, 180, 160, 255),
                    4),
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f)
                },
                uv2 = new[]
                {
                    new Vector2(0.1f, 0.1f),
                    new Vector2(0.9f, 0.1f),
                    new Vector2(0.1f, 0.9f),
                    new Vector2(0.9f, 0.9f)
                },
                subMeshCount = 1,
                submeshes = new[]
                {
                    new SubMeshTriangles(
                        new[] { 0, 1, 2, 2, 1, 3 })
                },
                bindPoses = new[] { Matrix4x4.identity },
                boneNameHashes = new[] { RootHash },
                umaBones = new[]
                {
                    new UMATransform
                    {
                        hash = RootHash,
                        name = "root"
                    }
                },
                ManagedBonesPerVertex = new byte[] { 1, 1, 1, 1 },
                ManagedBoneWeights = new[]
                {
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f }
                }
            };
            return asset;
        }

        private static UMABlendShape[] CreateBlendShapes()
        {
            return new[]
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
            };
        }

        private static UMABlendFrame CreateBlendFrame(
            float weight,
            float delta,
            bool includeNormals,
            bool includeTangents)
        {
            return new UMABlendFrame
            {
                frameWeight = weight,
                deltaVertices = Repeat(
                    new Vector3(0f, delta, 0f),
                    4),
                deltaNormals = includeNormals
                    ? Repeat(
                        new Vector3(0f, 0f, delta * 0.1f),
                        4)
                    : null,
                deltaTangents = includeTangents
                    ? Repeat(
                        new Vector3(delta * 0.1f, 0f, 0f),
                        4)
                    : null
            };
        }

        private static SkinnedMeshCombiner.CombineInstance CreateSource(
            UMAMeshData meshData,
            SlotData slot)
        {
            return new SkinnedMeshCombiner.CombineInstance
            {
                meshData = meshData,
                slotData = slot,
                targetSubmeshIndices = new[] { 0 }
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

        private static void DisposeSlotTriangles(SlotDataAsset asset)
        {
            if (asset?.meshData?.submeshes == null)
            {
                return;
            }
            for (int i = 0; i < asset.meshData.submeshes.Length; i++)
            {
                asset.meshData.submeshes[i]?.DisposeNativeTriangles(true);
            }
        }
    }
}

#endif
