#if UNITY_2021_3_OR_NEWER
#define UMA_MESHAPI_2021
#endif
#if UNITY_WEBGL
#undef UMA_UNSAFE
#else
#define UMA_UNSAFE
#endif
using System;
using System.Buffers;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    public static partial class SkinnedMeshCombinerMeshAPI
    {
#if UMA_MESHAPI_2021
        /// <summary>
        /// Resumable ownership container for incremental renderer preparation.
        /// Unity MeshData allocation remains on the main thread, while the
        /// bulk modifier, bounds, index, UV, and normal/tangent work is
        /// scheduled as jobs and completed in later frames.
        /// </summary>
        public sealed class IncrementalCombinePreparation : IDisposable
        {
            private enum PreparationStage
            {
                Analyze,
                AllocateMeshData,
                AllocateSkinning,
                PrepareSources,
                BakeBlendShapes,
                ScheduleModifiers,
                ScheduleBounds,
                AllocateIndexValidation,
                ScheduleSourceIndices,
                ValidateIndexLayout,
                ScheduleUVAndNormals,
                TransferOwnership,
                Completed,
                Failed
            }

            private readonly RendererBatch batch;
            private readonly UMAData umaData;
            private readonly Dictionary<string, float> bakedBlendshapes;
            private readonly bool markDynamic;
            private readonly bool markNotReadable;
            private readonly Quaternion boundsRotation;
            private readonly Allocator nativeAllocator;
            private readonly SkinnedMeshCombiner.CombineInstance[] sources;

            private PreparationStage stage = PreparationStage.Analyze;
            private Exception error;
            private bool disposed;
            private bool ownershipTransferred;
            private bool writableMeshDataAllocated;
            private long totalPreparationTicks;

            private int[] subMeshTriangleLength;
            private int[] subIndexStart;
            private int[] subWrite;
            private int[] sourceVertexOffsets;
            private int subMeshCount;
            private int vertexCount;
            private int boneWeightCount;
            private int bindPoseCount;
            private int transformHierarchyCount;
            private int totalIndexCount;
            private int lodLevel;
            private MeshComponents flags;
            private bool ignoreBlendShapes;
            private bool loadAllBlendShapeFrames;
            private bool hasNormals;
            private bool hasTangents;
            private bool hasUV;
            private bool hasUV2;
            private bool hasUV3;
            private bool hasUV4;
            private bool hasColors32;
            private bool hasBlendShapes;
            private bool hasCloth;
            private Dictionary<string, BlendShapeVertexData> blendShapeNames;

            private Mesh.MeshDataArray writableMeshData;
            private NativeArray<Vector3> positions;
            private NativeArray<NormTan> normalsTangents;
            private NativeArray<ColUV01> colorsUV;
            private NativeArray<UV23> additionalUV;
            private NativeArray<int> indices32;
            private NativeArray<ushort> indices16;
            private IndexFormat indexFormat;

            private Dictionary<int, BoneIndexEntry> bonesCollection;
            private List<Matrix4x4> bindPoses;
            private List<int> bonesList;
            private NativeArray<BoneWeight1> nativeBoneWeights;
            private NativeArray<byte> nativeBonesPerVertex;
            private NativeArray<int> boneWeightRemap;
            private int sourceCursor;
            private int vertexOffset;
            private int boneWeightOffset;

            private NativeArray<UVTransform> uvTransforms;
            private NativeArray<VertexDeltaInput> vertexDeltaInputs;
            private NativeArray<VertexDeltaRecord> vertexDeltaRecords;
            private NativeList<VertexDeltaRecord> vertexDeltas;
            private NativeArray<int> modifierValidation;
            private NativeArray<byte> modifiedSourceFlags;
            private NativeArray<ModifiedSourceRange> modifiedSources;
            private NativeArray<ModifiedSourceTriangle>
                modifiedSourceTriangles;
            private NativeArray<Vector3> modifiedNormalSums;
            private NativeArray<Vector3> modifiedTangentSums;
            private NativeArray<Vector3> modifiedBitangentSums;
            private NativeArray<BoundsResult> boundsPartials;
            private NativeArray<BoundsResult> boundsResult;
            private NativeArray<int> indexValidation;
            private List<NativeArray<byte>> triangleMasks;
            private int indexValidationCount;
            private JobHandle scheduledJobs;
            private JobHandle vertexDeltaHandle;
            private JobHandle boundsHandle;
            private JobHandle meshStreamHandle;
            private bool jobsScheduled;
            private PendingCombine result;

            internal IncrementalCombinePreparation(
                RendererBatch batch,
                UMAData umaData,
                Dictionary<string, float> bakedBlendshapes,
                bool markDynamic,
                bool markNotReadable,
                Quaternion boundsRotation)
            {
                this.batch = batch;
                this.umaData = umaData;
                this.bakedBlendshapes = bakedBlendshapes;
                this.markDynamic = markDynamic;
                this.markNotReadable = markNotReadable;
                this.boundsRotation = boundsRotation;
                nativeAllocator = Allocator.Persistent;
                sources = batch.Sources;
            }

            public bool IsComplete =>
                stage == PreparationStage.Completed;

            public bool HasPendingJobs =>
                jobsScheduled && !scheduledJobs.IsCompleted;

            public Exception Error => error;

            public string AtomicStepName
            {
                get
                {
                    switch (stage)
                    {
                        case PreparationStage.Analyze:
                            return "Prepare Renderer: Analyze";
                        case PreparationStage.AllocateMeshData:
                            return "Prepare Renderer: Allocate MeshData";
                        case PreparationStage.AllocateSkinning:
                            return "Prepare Renderer: Allocate Skinning";
                        case PreparationStage.PrepareSources:
                            return $"Prepare Renderer: Source {sourceCursor}";
                        case PreparationStage.BakeBlendShapes:
                            return $"Prepare Renderer: Bake Shapes {sourceCursor}";
                        case PreparationStage.ScheduleModifiers:
                            return "Prepare Renderer: Modifier Jobs";
                        case PreparationStage.ScheduleBounds:
                            return "Prepare Renderer: Bounds Jobs";
                        case PreparationStage.AllocateIndexValidation:
                            return "Prepare Renderer: Index Allocation";
                        case PreparationStage.ScheduleSourceIndices:
                            return $"Prepare Renderer: Indices {sourceCursor}";
                        case PreparationStage.ValidateIndexLayout:
                            return "Prepare Renderer: Validate Indices";
                        case PreparationStage.ScheduleUVAndNormals:
                            return "Prepare Renderer: UV/Normal Jobs";
                        case PreparationStage.TransferOwnership:
                            return "Prepare Renderer: Transfer Ownership";
                        case PreparationStage.Completed:
                            return "Prepare Renderer: Complete";
                        case PreparationStage.Failed:
                            return "Prepare Renderer: Failed";
                        default:
                            return "Prepare Renderer";
                    }
                }
            }

            public bool Step()
            {
                if (disposed)
                    throw new ObjectDisposedException(
                        nameof(IncrementalCombinePreparation));
                if (stage == PreparationStage.Failed)
                    throw error ?? new InvalidOperationException(
                        "Incremental renderer preparation failed.");
                if (stage == PreparationStage.Completed)
                    return true;

                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    switch (stage)
                    {
                        case PreparationStage.Analyze:
                            Analyze();
                            break;
                        case PreparationStage.AllocateMeshData:
                            AllocateMeshData();
                            break;
                        case PreparationStage.AllocateSkinning:
                            AllocateSkinning();
                            break;
                        case PreparationStage.PrepareSources:
                            PrepareOneSource();
                            break;
                        case PreparationStage.BakeBlendShapes:
                            BakeOneSource();
                            break;
                        case PreparationStage.ScheduleModifiers:
                            ScheduleModifiers();
                            break;
                        case PreparationStage.ScheduleBounds:
                            ScheduleBounds();
                            break;
                        case PreparationStage.AllocateIndexValidation:
                            AllocateIndexValidation();
                            break;
                        case PreparationStage.ScheduleSourceIndices:
                            ScheduleOneSourceIndices();
                            break;
                        case PreparationStage.ValidateIndexLayout:
                            ValidateIndexLayout();
                            break;
                        case PreparationStage.ScheduleUVAndNormals:
                            ScheduleUVAndNormals();
                            break;
                        case PreparationStage.TransferOwnership:
                            TransferOwnership();
                            break;
                    }
                }
                catch (Exception exception)
                {
                    error = exception;
                    stage = PreparationStage.Failed;
                    LogJobFailure(
                        batch,
                        AtomicStepName,
                        exception);
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    totalPreparationTicks += stopwatch.ElapsedTicks;
                }
                return stage == PreparationStage.Completed;
            }

            public PendingCombine TakeResult()
            {
                if (!IsComplete || result == null)
                    throw new InvalidOperationException(
                        "Renderer preparation has not completed.");
                PendingCombine value = result;
                result = null;
                return value;
            }

            private void Analyze()
            {
                if (umaData?.umaRecipe == null)
                    throw new InvalidOperationException(
                        "MeshData combine requires an initialized UMA recipe.");
                if (!IsFinite(boundsRotation) ||
                    !IsFinite(BoundsInflationFraction))
                    throw new InvalidOperationException(
                        "Mesh bounds rotation or inflation contains a non-finite value.");
                ValidateSources(sources);
                subMeshCount = FindTargetSubMeshCount(sources);
                if (subMeshCount <= 0)
                    throw new InvalidOperationException(
                        "The combine sources do not target any output submesh.");

                subMeshTriangleLength =
                    ArrayPool<int>.Shared.Rent(subMeshCount);
                lodLevel = Mathf.Max(0, umaData.currentLODLevel);
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                AnalyzeSources(
                    sources,
                    subMeshTriangleLength,
                    lodLevel,
                    ref vertexCount,
                    ref boneWeightCount,
                    ref bindPoseCount,
                    ref transformHierarchyCount,
                    ref flags);
                stopwatch.Stop();
                Ticks_AnalyzeSources += stopwatch.ElapsedTicks;

                ignoreBlendShapes =
                    umaData.blendShapeSettings != null &&
                    umaData.blendShapeSettings.ignoreBlendShapes;
                loadAllBlendShapeFrames =
                    umaData.blendShapeSettings == null ||
                    umaData.blendShapeSettings.loadAllFrames;
                bool loadNormals =
                    umaData.blendShapeSettings == null ||
                    umaData.blendShapeSettings.loadNormals;
                bool loadTangents =
                    umaData.blendShapeSettings == null ||
                    umaData.blendShapeSettings.loadTangents;
                stopwatch.Restart();
                if (!ignoreBlendShapes)
                {
                    AnalyzeBlendShapeSources(
                        sources,
                        bakedBlendshapes,
                        loadAllBlendShapeFrames,
                        loadNormals,
                        loadTangents,
                        ref flags,
                        out blendShapeNames,
                        umaData.umaRecipe);
                }
                stopwatch.Stop();
                Ticks_AnalyzeBlendshapes += stopwatch.ElapsedTicks;

                hasNormals =
                    (flags & MeshComponents.has_normals) != 0;
                hasTangents =
                    (flags & MeshComponents.has_tangents) != 0;
                hasUV = (flags & MeshComponents.has_uv) != 0;
                hasUV2 = (flags & MeshComponents.has_uv2) != 0;
                hasUV3 = (flags & MeshComponents.has_uv3) != 0;
                hasUV4 = (flags & MeshComponents.has_uv4) != 0;
                hasColors32 =
                    (flags & MeshComponents.has_colors32) != 0;
                hasBlendShapes =
                    !ignoreBlendShapes &&
                    (flags & MeshComponents.has_blendShapes) != 0;
                hasCloth =
                    (flags & MeshComponents.has_clothSkinning) != 0;

                subIndexStart =
                    ArrayPool<int>.Shared.Rent(subMeshCount);
                for (int i = 0, run = 0; i < subMeshCount; i++)
                {
                    subIndexStart[i] = run;
                    run = checked(
                        run + subMeshTriangleLength[i]);
                    totalIndexCount = run;
                }
                stage = PreparationStage.AllocateMeshData;
            }

            private void AllocateMeshData()
            {
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                writableMeshData =
                    Mesh.AllocateWritableMeshData(1);
                writableMeshDataAllocated = true;
                Mesh.MeshData meshData = writableMeshData[0];
                meshData.SetVertexBufferParams(
                    vertexCount,
                    BuildVertexLayout(
                        hasNormals,
                        hasTangents,
                        hasUV,
                        hasUV2,
                        hasUV3,
                        hasUV4,
                        hasColors32));
                indexFormat =
                    umaData.force32bit || vertexCount > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16;
                meshData.SetIndexBufferParams(
                    totalIndexCount,
                    indexFormat);
                meshData.subMeshCount = subMeshCount;
                positions = meshData.GetVertexData<Vector3>(0);
                int stream = 1;
                if (hasNormals || hasTangents)
                    normalsTangents =
                        meshData.GetVertexData<NormTan>(stream++);
                if (hasColors32 || hasUV || hasUV2)
                    colorsUV =
                        meshData.GetVertexData<ColUV01>(stream++);
                if (hasUV3 || hasUV4)
                    additionalUV =
                        meshData.GetVertexData<UV23>(stream++);
                if (indexFormat == IndexFormat.UInt16)
                    indices16 = meshData.GetIndexData<ushort>();
                else
                    indices32 = meshData.GetIndexData<int>();
                stopwatch.Stop();
                Ticks_AllocateMeshData += stopwatch.ElapsedTicks;
                stage = PreparationStage.AllocateSkinning;
            }

            private void AllocateSkinning()
            {
                bonesCollection =
                    new Dictionary<int, BoneIndexEntry>(
                        Math.Max(64, bindPoseCount));
                bindPoses =
                    new List<Matrix4x4>(bindPoseCount);
                bonesList =
                    new List<int>(transformHierarchyCount);
                nativeBoneWeights =
                    new NativeArray<BoneWeight1>(
                        boneWeightCount,
                        nativeAllocator);
                nativeBonesPerVertex =
                    new NativeArray<byte>(
                        Math.Max(1, vertexCount),
                        nativeAllocator);
                subWrite =
                    ArrayPool<int>.Shared.Rent(subMeshCount);
                sourceVertexOffsets =
                    ArrayPool<int>.Shared.Rent(sources.Length);
                if (UseParallelBoneWeights)
                {
                    boneWeightRemap =
                        new NativeArray<int>(
                            boneWeightCount,
                            nativeAllocator,
                            NativeArrayOptions.UninitializedMemory);
                }
                sourceCursor = 0;
                stage = sources.Length > 0
                    ? PreparationStage.PrepareSources
                    : PreparationStage.BakeBlendShapes;
            }

            private void PrepareOneSource()
            {
                SkinnedMeshCombiner.CombineInstance instance =
                    sources[sourceCursor];
                UMAMeshData source = instance.meshData;
                int sourceCount = source.vertexCount;
                sourceVertexOffsets[sourceCursor] =
                    vertexOffset;

                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                if (UseParallelBoneWeights)
                {
                    int[] bones = source.boneNameHashes;
                    Matrix4x4[] sourceBindPoses =
                        source.bindPoses;
                    int[] map =
                        ArrayPool<int>.Shared.Rent(bones.Length);
                    try
                    {
                        for (int i = 0; i < bones.Length; i++)
                        {
                            map[i] = TranslateBoneIndex(
                                i,
                                bones,
                                sourceBindPoses,
                                bonesCollection,
                                bindPoses,
                                bonesList);
                        }
                        NativeArray<byte>.Copy(
                            source.ManagedBonesPerVertex,
                            0,
                            nativeBonesPerVertex,
                            vertexOffset,
                            source.ManagedBonesPerVertex.Length);
                        NativeArray<BoneWeight1>.Copy(
                            source.ManagedBoneWeights,
                            0,
                            nativeBoneWeights,
                            boneWeightOffset,
                            source.ManagedBoneWeights.Length);
                        for (int i = 0;
                             i < source.ManagedBoneWeights.Length;
                             i++)
                        {
                            boneWeightRemap[boneWeightOffset + i] =
                                map[source.ManagedBoneWeights[i]
                                    .boneIndex];
                        }
                    }
                    finally
                    {
                        ArrayPool<int>.Shared.Return(map, false);
                    }
                }
                else
                {
                    BuildBoneWeights(
                        source,
                        nativeBoneWeights,
                        nativeBonesPerVertex,
                        vertexOffset,
                        boneWeightOffset,
                        bonesCollection,
                        bindPoses,
                        bonesList);
                }
                stopwatch.Stop();
                Ticks_BuildBoneWeights += stopwatch.ElapsedTicks;

                stopwatch.Restart();
#if UMA_UNSAFE
                float expand =
                    instance.slotData != null &&
                    instance.slotData.expandAlongNormal != 0
                        ? instance.slotData.expandAlongNormal /
                          1000000f
                        : 0f;
                FastCopyPositionsUnsafe(
                    positions,
                    vertexOffset,
                    source.vertices,
                    source.normals,
                    sourceCount,
                    expand);
#else
                if (instance.slotData != null &&
                    instance.slotData.expandAlongNormal != 0 &&
                    source.normals != null &&
                    source.normals.Length == sourceCount)
                {
                    float expand =
                        instance.slotData.expandAlongNormal /
                        1000000f;
                    for (int i = 0; i < sourceCount; i++)
                    {
                        positions[vertexOffset + i] =
                            source.vertices[i] +
                            source.normals[i] * expand;
                    }
                }
                else
                {
                    NativeArray<Vector3>.Copy(
                        source.vertices,
                        0,
                        positions,
                        vertexOffset,
                        sourceCount);
                }
#endif
                stopwatch.Stop();
                Ticks_CopyPositionsAndBounds +=
                    stopwatch.ElapsedTicks;

                if (hasNormals || hasTangents)
                {
                    stopwatch.Restart();
#if UMA_UNSAFE
                    PackNormTanUnsafe(
                        normalsTangents,
                        vertexOffset,
                        source.normals,
                        source.tangents,
                        sourceCount,
                        hasNormals,
                        hasTangents);
#else
                    for (int i = 0; i < sourceCount; i++)
                    {
                        NormTan value = default;
                        value.normal =
                            hasNormals &&
                            source.normals != null &&
                            source.normals.Length == sourceCount
                                ? source.normals[i]
                                : Vector3.zero;
                        value.tangent =
                            hasTangents &&
                            source.tangents != null &&
                            source.tangents.Length == sourceCount
                                ? source.tangents[i]
                                : BuildFallbackTangent(
                                    value.normal,
                                    1f);
                        normalsTangents[vertexOffset + i] =
                            value;
                    }
#endif
                    stopwatch.Stop();
                    Ticks_PackNormalsTangents +=
                        stopwatch.ElapsedTicks;
                }
                if (hasColors32 || hasUV || hasUV2)
                {
                    stopwatch.Restart();
#if UMA_UNSAFE
                    PackColUV01Unsafe(
                        colorsUV,
                        vertexOffset,
                        source.colors32,
                        source.uv,
                        source.uv2,
                        sourceCount,
                        hasColors32,
                        hasUV,
                        hasUV2);
#else
                    for (int i = 0; i < sourceCount; i++)
                    {
                        ColUV01 value = default;
                        value.color =
                            hasColors32 &&
                            source.colors32 != null &&
                            source.colors32.Length == sourceCount
                                ? source.colors32[i]
                                : (Color32)Color.white;
                        value.uv0 =
                            hasUV &&
                            source.uv != null &&
                            source.uv.Length >= sourceCount
                                ? source.uv[i]
                                : Vector2.zero;
                        value.uv1 =
                            hasUV2 &&
                            source.uv2 != null &&
                            source.uv2.Length >= sourceCount
                                ? source.uv2[i]
                                : Vector2.zero;
                        colorsUV[vertexOffset + i] = value;
                    }
#endif
                    stopwatch.Stop();
                    Ticks_PackColUV01 += stopwatch.ElapsedTicks;
                }
                if (hasUV3 || hasUV4)
                {
                    stopwatch.Restart();
#if UMA_UNSAFE
                    PackUV23Unsafe(
                        additionalUV,
                        vertexOffset,
                        source.uv3,
                        source.uv4,
                        sourceCount,
                        hasUV3,
                        hasUV4);
#else
                    for (int i = 0; i < sourceCount; i++)
                    {
                        UV23 value = default;
                        value.uv2 =
                            hasUV3 &&
                            source.uv3 != null &&
                            source.uv3.Length >= sourceCount
                                ? source.uv3[i]
                                : Vector2.zero;
                        value.uv3 =
                            hasUV4 &&
                            source.uv4 != null &&
                            source.uv4.Length >= sourceCount
                                ? source.uv4[i]
                                : Vector2.zero;
                        additionalUV[vertexOffset + i] = value;
                    }
#endif
                    stopwatch.Stop();
                    Ticks_PackUV23 += stopwatch.ElapsedTicks;
                }

                vertexOffset += sourceCount;
                boneWeightOffset +=
                    source.ManagedBoneWeights.Length;
                sourceCursor++;
                if (sourceCursor >= sources.Length)
                {
                    sourceCursor = 0;
                    stage = PreparationStage.BakeBlendShapes;
                }
            }

            private void BakeOneSource()
            {
                if (ignoreBlendShapes ||
                    bakedBlendshapes == null ||
                    bakedBlendshapes.Count == 0 ||
                    sourceCursor >= sources.Length)
                {
                    stage = PreparationStage.ScheduleModifiers;
                    return;
                }

                UMAMeshData source =
                    sources[sourceCursor].meshData;
                int offset = sourceVertexOffsets[sourceCursor];
                List<UMABlendShape> shapes =
                    SkinnedMeshCombiner.GetBlendshapeSources(
                        source,
                        umaData.umaRecipe);
                if (shapes != null)
                {
                    foreach (UMABlendShape shape
                             in shapes)
                    {
                        if (!bakedBlendshapes.TryGetValue(
                                shape.shapeName,
                                out float weight) ||
                            Mathf.Approximately(weight, 0f))
                        {
                            continue;
                        }
                        BakeShapeIntoBuffers(
                            shape,
                            weight,
                            positions,
                            normalsTangents,
                            offset,
                            source.vertexCount,
                            hasNormals,
                            hasTangents);
                    }
                }
                sourceCursor++;
                if (sourceCursor >= sources.Length)
                {
                    sourceCursor = 0;
                    stage = PreparationStage.ScheduleModifiers;
                }
            }

            private void ScheduleModifiers()
            {
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                if (UseParallelBoneWeights &&
                    boneWeightRemap.IsCreated &&
                    nativeBoneWeights.Length > 0)
                {
                    JobHandle handle =
                        new RemapAllBoneWeightsJob
                        {
                            Weights = nativeBoneWeights,
                            RemappedIndex = boneWeightRemap
                        }.Schedule(
                            nativeBoneWeights.Length,
                            256);
                    CombineScheduledJob(handle);
                }

                if (UseParallelMeshModifiers)
                {
                    vertexDeltaInputs =
                        SnapshotVertexDeltaInputsWithAllocator(
                            sources,
                            sourceVertexOffsets,
                            nativeAllocator);
                    if (vertexDeltaInputs.IsCreated &&
                        vertexDeltaInputs.Length > 0)
                    {
                        vertexDeltaRecords =
                            new NativeArray<VertexDeltaRecord>(
                                vertexDeltaInputs.Length,
                                nativeAllocator,
                                NativeArrayOptions.UninitializedMemory);
                        vertexDeltas =
                            new NativeList<VertexDeltaRecord>(
                                vertexDeltaInputs.Length,
                                nativeAllocator);
                        modifierValidation =
                            new NativeArray<int>(
                                1,
                                nativeAllocator,
                                NativeArrayOptions.ClearMemory);
                        modifiedSourceFlags =
                            new NativeArray<byte>(
                                sources.Length,
                                nativeAllocator,
                                NativeArrayOptions.ClearMemory);
                        JobHandle build =
                            new BuildVertexDeltaRecordsJob
                            {
                                Inputs = vertexDeltaInputs,
                                Records = vertexDeltaRecords
                            }.Schedule(
                                vertexDeltaInputs.Length,
                                128);
                        JobHandle compact =
                            new CompactVertexDeltaRecordsJob
                            {
                                Records = vertexDeltaRecords,
                                Compacted = vertexDeltas,
                                ModifiedSources =
                                    modifiedSourceFlags,
                                Validation =
                                    modifierValidation
                            }.Schedule(build);
                        vertexDeltaHandle =
                            new ApplyVertexDeltasJob
                            {
                                Vertices = positions,
                                Deltas = vertexDeltas
                                    .AsDeferredJobArray()
                            }.Schedule(
                                vertexDeltas,
                                128,
                                compact);
                        CombineScheduledJob(vertexDeltaHandle);
                    }
                }
                else
                {
                    vertexDeltaRecords =
                        BuildVertexDeltaRecordsWithAllocator(
                            sources,
                            sourceVertexOffsets,
                            nativeAllocator);
                    if (vertexDeltaRecords.IsCreated &&
                        vertexDeltaRecords.Length > 0)
                    {
                        modifiedSourceFlags =
                            new NativeArray<byte>(
                                sources.Length,
                                nativeAllocator,
                                NativeArrayOptions.ClearMemory);
                        for (int i = 0;
                             i < vertexDeltaRecords.Length;
                             i++)
                        {
                            VertexDeltaRecord delta =
                                vertexDeltaRecords[i];
                            positions[delta.vertexIndex] +=
                                delta.delta;
                            modifiedSourceFlags[
                                delta.sourceIndex] = 1;
                        }
                    }
                }

                bool hasModifierWork =
                    (vertexDeltaInputs.IsCreated &&
                     vertexDeltaInputs.Length > 0) ||
                    (vertexDeltaRecords.IsCreated &&
                     vertexDeltaRecords.Length > 0);
                if (hasModifierWork && hasNormals)
                {
                    BuildModifiedSourceTopologyWithAllocator(
                        sources,
                        sourceVertexOffsets,
                        lodLevel,
                        (hasTangents || hasNormals) && hasUV,
                        nativeAllocator,
                        out modifiedSources,
                        out modifiedSourceTriangles);
                    modifiedNormalSums =
                        new NativeArray<Vector3>(
                            Math.Max(1, vertexCount),
                            nativeAllocator,
                            NativeArrayOptions.ClearMemory);
                    if ((hasTangents || hasNormals) && hasUV)
                    {
                        modifiedTangentSums =
                            new NativeArray<Vector3>(
                                Math.Max(1, vertexCount),
                                nativeAllocator,
                                NativeArrayOptions.ClearMemory);
                        modifiedBitangentSums =
                            new NativeArray<Vector3>(
                                Math.Max(1, vertexCount),
                                nativeAllocator,
                                NativeArrayOptions.ClearMemory);
                    }
                }
                stopwatch.Stop();
                Ticks_ModifierPreparationAndSchedule +=
                    stopwatch.ElapsedTicks;
                stage = PreparationStage.ScheduleBounds;
            }

            private void ScheduleBounds()
            {
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                int batchCount = Math.Max(
                    1,
                    (vertexCount +
                     BOUNDS_VERTICES_PER_BATCH - 1) /
                    BOUNDS_VERTICES_PER_BATCH);
                boundsPartials =
                    new NativeArray<BoundsResult>(
                        batchCount,
                        nativeAllocator,
                        NativeArrayOptions.UninitializedMemory);
                boundsResult =
                    new NativeArray<BoundsResult>(
                        1,
                        nativeAllocator,
                        NativeArrayOptions.UninitializedMemory);
                JobHandle partials =
                    new CalculateBoundsPartialsJob
                    {
                        Vertices = positions,
                        Result = boundsPartials,
                        VerticesPerBatch =
                            BOUNDS_VERTICES_PER_BATCH
                    }.Schedule(
                        batchCount,
                        1,
                        vertexDeltaHandle);
                boundsHandle =
                    new ReduceBoundsJob
                    {
                        Partials = boundsPartials,
                        Result = boundsResult
                    }.Schedule(partials);
                CombineScheduledJob(boundsHandle);
                stopwatch.Stop();
                Ticks_BoundsJobsSchedule +=
                    stopwatch.ElapsedTicks;
                stage =
                    PreparationStage.AllocateIndexValidation;
            }

            private void AllocateIndexValidation()
            {
                int jobCapacity = 0;
                for (int i = 0; i < sources.Length; i++)
                {
                    jobCapacity = checked(
                        jobCapacity +
                        sources[i].meshData.subMeshCount);
                }
                int validationCapacity = checked(
                    (totalIndexCount +
                     INDEX_COPY_BATCH_SIZE - 1) /
                    INDEX_COPY_BATCH_SIZE +
                    jobCapacity);
                indexValidation =
                    new NativeArray<int>(
                        Math.Max(1, validationCapacity),
                        nativeAllocator,
                        NativeArrayOptions.ClearMemory);
                Array.Clear(subWrite, 0, subMeshCount);
                indexValidationCount = 0;
                sourceCursor = 0;
                stage = sources.Length > 0
                    ? PreparationStage.ScheduleSourceIndices
                    : PreparationStage.ValidateIndexLayout;
            }

            private void ScheduleOneSourceIndices()
            {
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                SkinnedMeshCombiner.CombineInstance instance =
                    sources[sourceCursor];
                UMAMeshData source = instance.meshData;
                int add = sourceVertexOffsets[sourceCursor];
                for (int subMesh = 0;
                     subMesh < source.subMeshCount;
                     subMesh++)
                {
                    int destination =
                        instance.targetSubmeshIndices[subMesh];
                    if (destination < 0)
                        continue;
                    NativeArray<int> sourceTriangles =
                        GetTrianglesForLOD(
                            source.submeshes[subMesh],
                            lodLevel);
                    if (!sourceTriangles.IsCreated)
                        continue;
                    int triangleLength =
                        sourceTriangles.Length;
                    if (triangleLength % 3 != 0)
                        throw new InvalidOperationException(
                            $"Source {sourceCursor}, submesh {subMesh} has {triangleLength} indices; triangle index counts must be divisible by three.");
                    int destinationStart =
                        subIndexStart[destination] +
                        subWrite[destination];
                    bool hasMask =
                        ShouldApplyTriangleMask(lodLevel) &&
                        instance.triangleMask != null &&
                        subMesh < instance.triangleMask.Length &&
                        instance.triangleMask[subMesh] != null &&
                        instance.triangleMask[subMesh].Length > 0;
                    if (!hasMask)
                    {
                        int writeCount = triangleLength;
                        ValidateIndexDestinationRange(
                            destinationStart,
                            writeCount,
                            subIndexStart[destination],
                            subIndexStart[destination] +
                            subMeshTriangleLength[destination],
                            sourceCursor,
                            subMesh);
                        if (writeCount == 0)
                            continue;
                        int copyBatchCount =
                            (writeCount +
                             INDEX_COPY_BATCH_SIZE - 1) /
                            INDEX_COPY_BATCH_SIZE;
                        int validationStart =
                            indexValidationCount;
                        indexValidationCount += copyBatchCount;
                        JobHandle handle =
                            indexFormat == IndexFormat.UInt16
                                ? new CopyIndicesJobU16
                                {
                                    Src = sourceTriangles,
                                    Dst = indices16,
                                    DstStart = destinationStart,
                                    Count = writeCount,
                                    Add = add,
                                    SourceVertexCount =
                                        source.vertexCount,
                                    Validation = indexValidation,
                                    ValidationStart =
                                        validationStart
                                }.Schedule(copyBatchCount, 1)
                                : new CopyIndicesJobInt
                                {
                                    Src = sourceTriangles,
                                    Dst = indices32,
                                    DstStart = destinationStart,
                                    Count = writeCount,
                                    Add = add,
                                    SourceVertexCount =
                                        source.vertexCount,
                                    Validation = indexValidation,
                                    ValidationStart =
                                        validationStart
                                }.Schedule(copyBatchCount, 1);
                        CombineScheduledJob(handle);
                        subWrite[destination] += writeCount;
                    }
                    else
                    {
                        var mask =
                            instance.triangleMask[subMesh];
                        int triangleCount =
                            triangleLength / 3;
                        int removed = 0;
                        int masked = Math.Min(
                            mask.Length,
                            triangleCount);
                        for (int i = 0; i < masked; i++)
                        {
                            if (mask[i])
                                removed++;
                        }
                        int writeCount =
                            (triangleCount - removed) * 3;
                        if (writeCount == 0)
                            continue;
                        ValidateIndexDestinationRange(
                            destinationStart,
                            writeCount,
                            subIndexStart[destination],
                            subIndexStart[destination] +
                            subMeshTriangleLength[destination],
                            sourceCursor,
                            subMesh);
                        NativeArray<byte> nativeMask =
                            BitArrayToNative(
                                mask,
                                triangleCount,
                                nativeAllocator);
                        if (triangleMasks == null)
                        {
                            triangleMasks =
                                new List<NativeArray<byte>>();
                        }
                        triangleMasks.Add(nativeMask);
                        JobHandle handle =
                            indexFormat == IndexFormat.UInt16
                                ? new MaskedCopyIndicesJobU16
                                {
                                    Src = sourceTriangles,
                                    Mask = nativeMask,
                                    Dst = indices16,
                                    DstStart = destinationStart,
                                    Add = add,
                                    SourceVertexCount =
                                        source.vertexCount,
                                    Validation = indexValidation,
                                    ValidationIndex =
                                        indexValidationCount++
                                }.Schedule()
                                : new MaskedCopyIndicesJobInt
                                {
                                    Src = sourceTriangles,
                                    Mask = nativeMask,
                                    Dst = indices32,
                                    DstStart = destinationStart,
                                    Add = add,
                                    SourceVertexCount =
                                        source.vertexCount,
                                    Validation = indexValidation,
                                    ValidationIndex =
                                        indexValidationCount++
                                }.Schedule();
                        CombineScheduledJob(handle);
                        subWrite[destination] += writeCount;
                    }
                }
                stopwatch.Stop();
                Ticks_IndexJobsSchedule +=
                    stopwatch.ElapsedTicks;
                sourceCursor++;
                if (sourceCursor >= sources.Length)
                {
                    stage =
                        PreparationStage.ValidateIndexLayout;
                }
            }

            private void ValidateIndexLayout()
            {
                for (int i = 0; i < subMeshCount; i++)
                {
                    if (subWrite[i] !=
                        subMeshTriangleLength[i])
                    {
                        throw new InvalidOperationException(
                            $"Output submesh {i} was allocated for {subMeshTriangleLength[i]} indices but prepared {subWrite[i]}.");
                    }
                }
                meshStreamHandle = boundsHandle;
                stage =
                    PreparationStage.ScheduleUVAndNormals;
            }

            private void ScheduleUVAndNormals()
            {
                if (hasUV)
                {
                    var stopwatch =
                        System.Diagnostics.Stopwatch.StartNew();
                    uvTransforms = BuildUVTransformsForUMA(
                        colorsUV,
                        umaData,
                        batch.AtlasResolution,
                        batch.CurrentRendererIndex,
                        sources,
                        sourceVertexOffsets,
                        batch.RendererAsset,
                        batch.HasRendererAssetOverride,
                        nativeAllocator);
                    if (uvTransforms.IsCreated &&
                        uvTransforms.Length > 0)
                    {
                        JobHandle handle =
                            new ApplyUVTransformsJob
                            {
                                Vertices = colorsUV,
                                Transforms = uvTransforms
                            }.Schedule(
                                colorsUV.Length,
                                128,
                                boundsHandle);
                        CombineScheduledJob(handle);
                        meshStreamHandle = handle;
                    }
                    stopwatch.Stop();
                    Ticks_UVRemap += stopwatch.ElapsedTicks;
                }

                if (modifiedSources.IsCreated &&
                    modifiedSourceFlags.IsCreated)
                {
                    JobHandle handle =
                        new RecalculateModifiedSourcesJob
                        {
                            Sources = modifiedSources,
                            Triangles =
                                modifiedSourceTriangles,
                            ModifiedSources =
                                modifiedSourceFlags,
                            Positions = positions,
                            ColorsUV = colorsUV,
                            NormalsTangents =
                                normalsTangents,
                            NormalSums = modifiedNormalSums,
                            TangentSums =
                                modifiedTangentSums,
                            BitangentSums =
                                modifiedBitangentSums
                        }.Schedule(
                            modifiedSources.Length,
                            1,
                            meshStreamHandle);
                    CombineScheduledJob(handle);
                }
                stage =
                    PreparationStage.TransferOwnership;
            }

            private void TransferOwnership()
            {
                result = new PendingCombine
                {
                    Batch = batch,
                    UmaData = umaData,
                    BakedBlendshapes = bakedBlendshapes,
                    BoundsRotation = boundsRotation,
                    MeshDataArray = writableMeshData,
                    MarkDynamic = markDynamic,
                    MarkNotReadable = markNotReadable,
                    HasBlendShapes = hasBlendShapes,
                    HasCloth = hasCloth,
                    BlendShapeNames = blendShapeNames,
                    SourceVertexOffsets = sourceVertexOffsets,
                    SubIndexStart = subIndexStart,
                    SubWrite = subWrite,
                    SubMeshCount = subMeshCount,
                    VertexCount = vertexCount,
                    BindPoses = bindPoses,
                    BonesList = bonesList,
                    BoneWeights = nativeBoneWeights,
                    BonesPerVertex = nativeBonesPerVertex,
                    BoneWeightRemap = boneWeightRemap,
                    UVTransforms = uvTransforms,
                    VertexDeltaInputs = vertexDeltaInputs,
                    VertexDeltaRecords = vertexDeltaRecords,
                    VertexDeltas = vertexDeltas,
                    ModifierValidation = modifierValidation,
                    ModifiedSourceFlags =
                        modifiedSourceFlags,
                    ModifiedSources = modifiedSources,
                    ModifiedSourceTriangles =
                        modifiedSourceTriangles,
                    ModifiedNormalSums =
                        modifiedNormalSums,
                    ModifiedTangentSums =
                        modifiedTangentSums,
                    ModifiedBitangentSums =
                        modifiedBitangentSums,
                    BoundsPartials = boundsPartials,
                    BoundsResult = boundsResult,
                    IndexValidation = indexValidation,
                    TriangleMasks = triangleMasks,
                    IndexValidationCount =
                        indexValidationCount,
                    Positions = positions,
                    NormalsTangents = normalsTangents,
                    ColorsUV = colorsUV,
                    HasNormals = hasNormals,
                    HasTangents = hasTangents,
                    HasUV = hasUV,
                    LoadAllBlendShapeFrames =
                        loadAllBlendShapeFrames,
                    Jobs = scheduledJobs,
                    JobsScheduled = jobsScheduled,
                    NativeAllocator = nativeAllocator
                };

                sourceVertexOffsets = null;
                subIndexStart = null;
                subWrite = null;
                nativeBoneWeights = default;
                nativeBonesPerVertex = default;
                boneWeightRemap = default;
                uvTransforms = default;
                vertexDeltaInputs = default;
                vertexDeltaRecords = default;
                vertexDeltas = default;
                modifierValidation = default;
                modifiedSourceFlags = default;
                modifiedSources = default;
                modifiedSourceTriangles = default;
                modifiedNormalSums = default;
                modifiedTangentSums = default;
                modifiedBitangentSums = default;
                boundsPartials = default;
                boundsResult = default;
                indexValidation = default;
                triangleMasks = null;
                ownershipTransferred = true;
                writableMeshDataAllocated = false;
                Ticks_CombineInternalTotal +=
                    totalPreparationTicks;
                stage = PreparationStage.Completed;
            }

            private void CombineScheduledJob(JobHandle handle)
            {
                scheduledJobs =
                    JobHandle.CombineDependencies(
                        scheduledJobs,
                        handle);
                jobsScheduled = true;
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                if (!ownershipTransferred)
                {
                    Exception cleanupException = null;
                    if (jobsScheduled)
                    {
                        try
                        {
                            scheduledJobs.Complete();
                        }
                        catch (Exception exception)
                        {
                            RecordCleanupException(
                                ref cleanupException,
                                exception);
                        }
                    }
                    TryDisposeNativeArray(
                        ref boundsResult,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref boundsPartials,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref indexValidation,
                        ref cleanupException);
                    if (triangleMasks != null)
                    {
                        for (int i = 0;
                             i < triangleMasks.Count;
                             i++)
                        {
                            NativeArray<byte> mask =
                                triangleMasks[i];
                            TryDisposeNativeArray(
                                ref mask,
                                ref cleanupException);
                        }
                    }
                    TryDisposeNativeArray(
                        ref uvTransforms,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref vertexDeltaInputs,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref vertexDeltaRecords,
                        ref cleanupException);
                    TryDisposeNativeList(
                        ref vertexDeltas,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifierValidation,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifiedSourceFlags,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifiedSources,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifiedSourceTriangles,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifiedNormalSums,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifiedTangentSums,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref modifiedBitangentSums,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref boneWeightRemap,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref nativeBonesPerVertex,
                        ref cleanupException);
                    TryDisposeNativeArray(
                        ref nativeBoneWeights,
                        ref cleanupException);
                    if (writableMeshDataAllocated)
                    {
                        try
                        {
                            writableMeshData.Dispose();
                        }
                        catch (Exception exception)
                        {
                            RecordCleanupException(
                                ref cleanupException,
                                exception);
                        }
                    }
                    if (cleanupException != null)
                    {
                        LogJobFailure(
                            batch,
                            "incremental preparation cleanup",
                            cleanupException);
                    }
                }

                if (subMeshTriangleLength != null)
                {
                    ArrayPool<int>.Shared.Return(
                        subMeshTriangleLength,
                        false);
                    subMeshTriangleLength = null;
                }
                if (subIndexStart != null)
                {
                    ArrayPool<int>.Shared.Return(
                        subIndexStart,
                        false);
                    subIndexStart = null;
                }
                if (subWrite != null)
                {
                    ArrayPool<int>.Shared.Return(
                        subWrite,
                        false);
                    subWrite = null;
                }
                if (sourceVertexOffsets != null)
                {
                    ArrayPool<int>.Shared.Return(
                        sourceVertexOffsets,
                        false);
                    sourceVertexOffsets = null;
                }
            }
        }

        public static IncrementalCombinePreparation
            BeginIncrementalCombinePreparation(
                RendererBatch batch,
                UMAData umaData,
                Dictionary<string, float> bakedBlendshapes,
                bool markDynamic,
                bool markNotReadable,
                Quaternion boundsRotation)
        {
            return new IncrementalCombinePreparation(
                batch,
                umaData,
                bakedBlendshapes,
                markDynamic,
                markNotReadable,
                boundsRotation);
        }
#endif
    }
}
