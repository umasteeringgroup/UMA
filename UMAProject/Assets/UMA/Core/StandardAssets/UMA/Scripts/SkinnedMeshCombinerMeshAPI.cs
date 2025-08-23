#if UNITY_2021_3_OR_NEWER
#define UMA_MESHAPI_2021
#endif
#define UMA_DEBUG_UV_VALIDATE

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
#if UMA_UNSAFE
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace UMA
{
    /// <summary>
    /// Unity 2021.3+ MeshData API based combiner.
    /// - Writes directly to MeshData buffers to avoid large intermediates.
    /// - Uses 16-bit index buffers when possible.
    /// - Burst/Jobs for index copy (masked and unmasked).
    /// - Preserves cloth and blendshapes (unbaked added directly to Mesh).
    /// - UMA atlas UV remap operates on MeshData UV buffer.
    /// </summary>
    public static class SkinnedMeshCombinerMeshAPI
    {
        public struct RendererBatch
        {
            public SkinnedMeshRenderer Renderer;
            public SkinnedMeshCombiner.CombineInstance[] Sources;
            public int CurrentRendererIndex;
            public int AtlasResolution;
        }
        // Toggle to enable the parallel bone weight remap path.
        // Can be flipped at runtime before combining.
#if UMA_MESHAPI_2021
        // Add near other static flags (top of class)
        public static bool UseParallelBoneWeights = true;
        public static bool UseParallelUVRemap = true;
        private const int UV_PARALLEL_MIN_VERTS = 4000;
#endif
        // Timings (Stopwatch ticks). Call ResetTimings() to clear.
        public static long Ticks_CombineInternalTotal;
        public static long Ticks_AnalyzeSources;
        public static long Ticks_AnalyzeBlendshapes;
        public static long Ticks_AllocateMeshData;
        public static long Ticks_MergeTransforms;
        public static long Ticks_EnsureSkeleton;
        public static long Ticks_BuildBoneWeights;
        public static long Ticks_CopyPositionsAndBounds;
        public static long Ticks_PackNormalsTangents;
        public static long Ticks_PackColUV01;
        public static long Ticks_PackUV23;
        public static long Ticks_IndexJobsSchedule;
        public static long Ticks_IndexJobsComplete;
        public static long Ticks_UVRemap;
        public static long Ticks_SetSubmeshes;
        public static long Ticks_ApplyMeshData;
        public static long Ticks_SetBindposesAndWeights;
        public static long Ticks_AssignBones;
        public static long Ticks_BuildCloth;
#if UMA_PARALLEL_BONEWEIGHTS_VALIDATE || UMA_DEBUG_BONEWEIGHTS_VALIDATE
        private const int BoneWeightValidateSampleCount = 16;
#endif
        public static void ResetTimings()
        {
            Ticks_CombineInternalTotal = 0;
            Ticks_AnalyzeSources = 0;
            Ticks_AnalyzeBlendshapes = 0;
            Ticks_AllocateMeshData = 0;
            Ticks_MergeTransforms = 0;
            Ticks_EnsureSkeleton = 0;
            Ticks_BuildBoneWeights = 0;
            Ticks_CopyPositionsAndBounds = 0;
            Ticks_PackNormalsTangents = 0;
            Ticks_PackColUV01 = 0;
            Ticks_PackUV23 = 0;
            Ticks_IndexJobsSchedule = 0;
            Ticks_IndexJobsComplete = 0;
            Ticks_UVRemap = 0;
            Ticks_SetSubmeshes = 0;
            Ticks_ApplyMeshData = 0;
            Ticks_SetBindposesAndWeights = 0;
            Ticks_AssignBones = 0;
            Ticks_BuildCloth = 0;
        }

        // Safe interleaved structs (multi-stream layout)
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NormTan { public Vector3 normal; public Vector4 tangent; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ColUV01 { public Color32 color; public Vector2 uv0; public Vector2 uv1; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct UV23 { public Vector2 uv2; public Vector2 uv3; }
        // Add inside class near other private structs (after NormTan/ColUV01/UV23 definitions).
#if UMA_MESHAPI_2021
        [BurstCompile]
        private struct ApplyUVTransformsJob : IJobParallelFor
        {
            public NativeArray<ColUV01> Vertices;
            [ReadOnly] public NativeArray<UVTransform> Transforms;

            public void Execute(int i)
            {
                var t = Transforms[i];
                int start = t.start;
                int end = start + t.count;
                float xMin = t.xMin;
                float yMin = t.yMin;
                float xScale = t.xScale;
                float yScale = t.yScale;

                for (int v = start; v < end; v++)
                {
                    var c = Vertices[v];
                    c.uv0.x = xMin + c.uv0.x * xScale;
                    c.uv0.y = yMin + c.uv0.y * yScale;
                    Vertices[v] = c;
                }
            }
        }

        private struct UVTransform
        {
            public int start;
            public int count;
            public float xMin;
            public float yMin;
            public float xScale;
            public float yScale;
        }

        // Reusable static (cleared each invocation) to cut GC.
        private static readonly List<UVTransform> _uvTransforms = new List<UVTransform>(128);
        private static readonly HashSet<SlotData> _uvProcessedSlots = new HashSet<SlotData>();
#endif
        public static ClothSkinningCoefficient[] CombineIntoRenderer(
            SkinnedMeshRenderer renderer,
            SkinnedMeshCombiner.CombineInstance[] sources,
            UMAData umaData,
            int currentRendererIndex,
            int atlasResolution,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic = false,
            bool markNotReadable = false)
        {
#if !UMA_MESHAPI_2021
            throw new NotSupportedException("Requires Unity 2021.3+ MeshData API.");
#else
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (sources == null || sources.Length == 0) throw new ArgumentException("sources empty", nameof(sources));
            if (umaData == null) throw new ArgumentNullException(nameof(umaData));

            CombineInternal(
                new RendererBatch
                {
                    Renderer = renderer,
                    Sources = sources,
                    CurrentRendererIndex = currentRendererIndex,
                    AtlasResolution = atlasResolution
                },
                umaData,
                bakedBlendshapes ?? new Dictionary<string, float>(),
                markDynamic,
                markNotReadable,
                out var clothCoeffs);

            return clothCoeffs;
#endif
        }

        public static ClothSkinningCoefficient[][] CombineIntoRenderers(
            RendererBatch[] batches,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic = false,
            bool markNotReadable = false)
        {
#if !UMA_MESHAPI_2021
            throw new NotSupportedException("Requires Unity 2021.3+ MeshData API.");
#else
            if (batches == null || batches.Length == 0) throw new ArgumentException("batches empty", nameof(batches));
            if (umaData == null) throw new ArgumentNullException(nameof(umaData));

            var results = new ClothSkinningCoefficient[batches.Length][];
            for (int i = 0; i < batches.Length; i++)
            {
                if (batches[i].Renderer == null) throw new ArgumentNullException($"Renderer at {i}");
                if (batches[i].Sources == null || batches[i].Sources.Length == 0) throw new ArgumentException($"sources empty at {i}");

                CombineInternal(
                    batches[i],
                    umaData,
                    bakedBlendshapes ?? new Dictionary<string, float>(),
                    markDynamic,
                    markNotReadable,
                    out var clothCoeffs);

                results[i] = clothCoeffs;
            }
            return results;
#endif
        }


#if UMA_MESHAPI_2021
        private static void CombineInternal(
            RendererBatch batch,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic,
            bool markNotReadable,
            out ClothSkinningCoefficient[] clothCoeffs)
        {
            var totalSW = System.Diagnostics.Stopwatch.StartNew();

            var sources = batch.Sources;

            // Analyze
            int vertexCount = 0;
            int boneWeightCount = 0;
            int bindPoseCount = 0;
            int transformHierarchyCount = 0;
            int subMeshCount = FindTargetSubMeshCount(sources);
            int[] subMeshTriangleLength = new int[subMeshCount];

            MeshComponents flags = MeshComponents.none;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            AnalyzeSources(sources, subMeshTriangleLength, ref vertexCount, ref boneWeightCount, ref bindPoseCount, ref transformHierarchyCount, ref flags);
            sw.Stop();
            Ticks_AnalyzeSources += sw.ElapsedTicks;

            // Blendshape analysis (unbaked only)
            Dictionary<string, BlendShapeVertexData> blendShapeNames;
            sw.Restart();
            AnalyzeBlendShapeSources(sources, bakedBlendshapes, ref flags, out blendShapeNames, umaData.umaRecipe);
            sw.Stop();
            Ticks_AnalyzeBlendshapes += sw.ElapsedTicks;

            bool hasNormals = (flags & MeshComponents.has_normals) != 0;
            bool hasTangents = (flags & MeshComponents.has_tangents) != 0;
            bool hasUV = (flags & MeshComponents.has_uv) != 0;
            bool hasUV2 = (flags & MeshComponents.has_uv2) != 0;
            bool hasUV3 = (flags & MeshComponents.has_uv3) != 0;
            bool hasUV4 = (flags & MeshComponents.has_uv4) != 0;
            bool hasColors32 = (flags & MeshComponents.has_colors32) != 0;
            bool hasBlendShapes = (flags & MeshComponents.has_blendShapes) != 0;
            bool hasCloth = (flags & MeshComponents.has_clothSkinning) != 0;

            // Prefix sums for submeshes and total index count
            int totalIndexCount = 0;
            var subIndexStart = new int[subMeshCount];
            for (int i = 0, run = 0; i < subMeshCount; i++)
            {
                subIndexStart[i] = run;
                run += subMeshTriangleLength[i];
                totalIndexCount = run;
            }

            // Allocate MeshData
            sw.Restart();
            var mda = Mesh.AllocateWritableMeshData(1);
            var md = mda[0];

            var vDescs = BuildVertexLayout(hasNormals, hasTangents, hasUV, hasUV2, hasUV3, hasUV4, hasColors32);
            md.SetVertexBufferParams(vertexCount, vDescs);

            var indexFormat = (vertexCount <= 65535) ? IndexFormat.UInt16 : IndexFormat.UInt32;
            md.SetIndexBufferParams(totalIndexCount, indexFormat);

            md.subMeshCount = subMeshCount;
            sw.Stop();
            Ticks_AllocateMeshData += sw.ElapsedTicks;

            // Grab vertex streams
            var vPos = md.GetVertexData<Vector3>(0);

            NativeArray<NormTan> vNT = default;
            NativeArray<ColUV01> vC01 = default;
            NativeArray<UV23> vUV23 = default;

            int stream = 1;
            if (hasNormals || hasTangents) vNT = md.GetVertexData<NormTan>(stream++);
            if (hasColors32 || hasUV || hasUV2) vC01 = md.GetVertexData<ColUV01>(stream++);
            if (hasUV3 || hasUV4) vUV23 = md.GetVertexData<UV23>(stream++);

            // Index buffer
            NativeArray<int> ibInt = default;
            NativeArray<ushort> ibU16 = default;
            if (indexFormat == IndexFormat.UInt16) ibU16 = md.GetIndexData<ushort>();
            else ibInt = md.GetIndexData<int>();

            // UMA transforms merged
            sw.Restart();
            int boneCount = 0;
            var mergedUmaTransforms = new UMATransform[transformHierarchyCount];
            for (int i = 0; i < sources.Length; i++)
                MergeSortedTransforms(mergedUmaTransforms, ref boneCount, sources[i].meshData.umaBones);
            sw.Stop();
            Ticks_MergeTransforms += sw.ElapsedTicks;

            // Ensure skeleton
            sw.Restart();
            if (umaData != null && umaData.skeleton != null)
            {
                umaData.skeleton.BeginSkeletonUpdate();
                for (int i = 0; i < boneCount; i++) umaData.skeleton.EnsureBone(mergedUmaTransforms[i]);
                umaData.skeleton.EnsureBoneHierarchy();
                umaData.skeleton.EndSkeletonUpdate();
            }
            sw.Stop();
            Ticks_EnsureSkeleton += sw.ElapsedTicks;

            // Bones and weights collection
            var bonesCollection = new Dictionary<int, BoneIndexEntry>(Math.Max(64, bindPoseCount));
            var bindPoses = new List<Matrix4x4>(bindPoseCount);
            var bonesList = new List<int>(transformHierarchyCount);

            // Must be TempJob (or Persistent) because nativeBoneWeights is touched by scheduled jobs.
            var nativeBoneWeights = new NativeArray<BoneWeight1>(boneWeightCount, Allocator.TempJob);
            var nativeBonesPerVertex = new NativeArray<byte>(Math.Max(1, vertexCount), Allocator.TempJob);

            // Track offsets and bounds
            int vertexOffset = 0;
            int boneWeightOffset = 0;
            var boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            // Index write cursors per submesh
            var subWrite = new int[subMeshCount];

            // Collect vertex offsets per source (for blendshape pass later)
            var sourceVertexOffsets = new int[sources.Length];

            // Collect scheduled index jobs
            var indexJobs = new List<JobHandle>(Math.Max(32, subMeshCount * sources.Length));
            var boneWeightJobs = UseParallelBoneWeights ? new List<JobHandle>(sources.Length) : null;

            // Parallel path accumulates remap targets then does a single job after all copies.
            NativeArray<int> bwRemap = default;
            if (UseParallelBoneWeights)
                bwRemap = new NativeArray<int>(boneWeightCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            // Copy vertex attributes directly into MeshData buffers
            for (int s = 0; s < sources.Length; s++)
            {
                var ci = sources[s];
                var src = ci.meshData;
                int srcCount = src.vertexCount;
                sourceVertexOffsets[s] = vertexOffset;

                // Bone weights: remap and copy into global buffers
                sw.Restart();
                if (UseParallelBoneWeights)
                {
                    // Build mapping (same logic as synchronous path) but do NOT modify weights yet.
                    var bones = src.boneNameHashes;
                    var bindPosesSrc = src.bindPoses;
                    var pool = ArrayPool<int>.Shared;
                    var boneMapping = pool.Rent(bones.Length);
                    try
                    {
                        for (int iMap = 0; iMap < bones.Length; iMap++)
                            boneMapping[iMap] = TranslateBoneIndex(iMap, bones, bindPosesSrc, bonesCollection, bindPoses, bonesList);

                        // Copy raw bones-per-vertex & weights (original indices)
                        NativeArray<byte>.Copy(src.ManagedBonesPerVertex, 0, nativeBonesPerVertex, vertexOffset, src.ManagedBonesPerVertex.Length);
                        NativeArray<BoneWeight1>.Copy(src.ManagedBoneWeights, 0, nativeBoneWeights, boneWeightOffset, src.ManagedBoneWeights.Length);

                        // Fill remap table for this block
                        var srcWeights = src.ManagedBoneWeights;
                        for (int w = 0; w < srcWeights.Length; w++)
                            bwRemap[boneWeightOffset + w] = boneMapping[srcWeights[w].boneIndex];
                    }
                    finally
                    {
                        pool.Return(boneMapping, clearArray: false);
                    }
                }
                else
                {
                    BuildBoneWeights(src, nativeBoneWeights, nativeBonesPerVertex, vertexOffset, boneWeightOffset, bonesCollection, bindPoses, bonesList);
                }
                sw.Stop();
                Ticks_BuildBoneWeights += sw.ElapsedTicks;



                // Positions (+bounds) and optional expandAlongNormal
                sw.Restart();
#if UMA_UNSAFE
                {
                    float expand = 0f;
                    if (ci.slotData != null && ci.slotData.expandAlongNormal > 0)
                        expand = ci.slotData.expandAlongNormal / 1000000f;

                    FastCopyPositionsAndBoundsUnsafe(
                        vPos, vertexOffset,
                        src.vertices, src.normals,
                        srcCount, expand,
                        ref boundsMin, ref boundsMax);
                }
#else
                if (ci.slotData != null && ci.slotData.expandAlongNormal > 0 && src.normals != null && src.normals.Length == srcCount)
                {
                    float expand = ci.slotData.expandAlongNormal / 1000000f;
                    for (int i = 0; i < srcCount; i++)
                        vPos[vertexOffset + i] = src.vertices[i] + (src.normals[i] * expand);
                }
                else
                {
                    NativeArray<Vector3>.Copy(src.vertices, 0, vPos, vertexOffset, srcCount);
                }
                // update bounds
                for (int i = 0; i < srcCount; i++)
                {
                    var v = vPos[vertexOffset + i];
                    if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x;
                    if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y;
                    if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z;
                }
#endif
                sw.Stop();
                Ticks_CopyPositionsAndBounds += sw.ElapsedTicks;

                // Normals/Tangents
                if (hasNormals || hasTangents)
                {
                    sw.Restart();
#if UMA_UNSAFE
                    PackNormTanUnsafe(vNT, vertexOffset, src.normals, src.tangents, srcCount, hasNormals, hasTangents);
#else
                    for (int i = 0; i < srcCount; i++)
                    {
                        var nt = default(NormTan);
                        nt.normal = (hasNormals && src.normals != null && src.normals.Length == srcCount) ? src.normals[i] : Vector3.zero;
                        nt.tangent = (hasTangents && src.tangents != null && src.tangents.Length == srcCount) ? src.tangents[i] : new Vector4(0, 0, 0, 1);
                        vNT[vertexOffset + i] = nt;
                    }
#endif
                    sw.Stop();
                    Ticks_PackNormalsTangents += sw.ElapsedTicks;
                }

                // Colors, UV0, UV1
                if (hasColors32 || hasUV || hasUV2)
                {
                    sw.Restart();
#if UMA_UNSAFE
                    PackColUV01Unsafe(vC01, vertexOffset, src.colors32, src.uv, src.uv2, srcCount, hasColors32, hasUV, hasUV2);
#else
                    for (int i = 0; i < srcCount; i++)
                    {
                        var c01 = default(ColUV01);
                        c01.color = (hasColors32 && src.colors32 != null && src.colors32.Length == srcCount) ? src.colors32[i] : (Color32)Color.white;
                        c01.uv0 = (hasUV && src.uv != null && src.uv.Length >= srcCount) ? src.uv[i] : Vector2.zero;
                        c01.uv1 = (hasUV2 && src.uv2 != null && src.uv2.Length >= srcCount) ? src.uv2[i] : Vector2.zero;
                        vC01[vertexOffset + i] = c01;
                    }
#endif
                    sw.Stop();
                    Ticks_PackColUV01 += sw.ElapsedTicks;
                }

                // UV2, UV3 (TexCoord2, TexCoord3)
                if (hasUV3 || hasUV4)
                {
                    sw.Restart();
#if UMA_UNSAFE
                    PackUV23Unsafe(vUV23, vertexOffset, src.uv3, src.uv4, srcCount, hasUV3, hasUV4);
#else
                    for (int i = 0; i < srcCount; i++)
                    {
                        var uv23 = default(UV23);
                        uv23.uv2 = (hasUV3 && src.uv3 != null && src.uv3.Length >= srcCount) ? src.uv3[i] : Vector2.zero;
                        uv23.uv3 = (hasUV4 && src.uv4 != null && src.uv4.Length >= srcCount) ? src.uv4[i] : Vector2.zero;
                        vUV23[vertexOffset + i] = uv23;
                    }
#endif
                    sw.Stop();
                    Ticks_PackUV23 += sw.ElapsedTicks;
                }

                ci.slotData.vertexOffset = vertexOffset;
                ci.slotData.skinnedMeshRenderer = batch.CurrentRendererIndex;

                vertexOffset += srcCount;
                boneWeightOffset += src.ManagedBoneWeights.Length;
            }

            // SECOND PASS: schedule index copy jobs
            Array.Clear(subWrite, 0, subWrite.Length);

            sw.Restart();
            for (int s = 0; s < sources.Length; s++)
            {
                var ci = sources[s];
                var src = ci.meshData;

                for (int sm = 0; sm < src.subMeshCount; sm++)
                {
                    int dstSub = ci.targetSubmeshIndices[sm];
                    if (dstSub < 0) continue;

                    var srcTris = src.submeshes[sm].GetTriangles();
                    int triLen = srcTris.Length;
                    int dstStart = subIndexStart[dstSub] + subWrite[dstSub];

                    if (ci.triangleMask != null && sm < ci.triangleMask.Length)
                    {
                        int maskedRemoved = UMAUtils.GetCardinality(ci.triangleMask[sm]);
                        int kept = triLen - (maskedRemoved * 3);

                        if (maskedRemoved == 0)
                        {
                            if (indexFormat == IndexFormat.UInt16)
                            {
                                var job = new CopyIndicesJobU16
                                {
                                    Src = srcTris,
                                    Dst = ibU16,
                                    DstStart = dstStart,
                                    Add = (ushort)ci.slotData.vertexOffset
                                }.Schedule();
                                indexJobs.Add(job);
                            }
                            else
                            {
                                var job = new CopyIndicesJobInt
                                {
                                    Src = srcTris,
                                    Dst = ibInt,
                                    DstStart = dstStart,
                                    Add = ci.slotData.vertexOffset
                                }.Schedule();
                                indexJobs.Add(job);
                            }
                            subWrite[dstSub] += triLen;
                        }
                        else if (kept > 0)
                        {
                            if (indexFormat == IndexFormat.UInt16)
                            {
                                var job = new MaskedCopyIndicesJobU16
                                {
                                    Src = srcTris,
                                    Mask = BitArrayToNative(ci.triangleMask[sm], Allocator.TempJob),
                                    Dst = ibU16,
                                    DstStart = dstStart,
                                    Add = (ushort)ci.slotData.vertexOffset
                                }.Schedule();
                                indexJobs.Add(job);
                            }
                            else
                            {
                                var job = new MaskedCopyIndicesJobInt
                                {
                                    Src = srcTris,
                                    Mask = BitArrayToNative(ci.triangleMask[sm], Allocator.TempJob),
                                    Dst = ibInt,
                                    DstStart = dstStart,
                                    Add = ci.slotData.vertexOffset
                                }.Schedule();
                                indexJobs.Add(job);
                            }
                            subWrite[dstSub] += kept;
                        }
                        // else: all masked, nothing to write
                    }
                    else
                    {
                        if (indexFormat == IndexFormat.UInt16)
                        {
                            var job = new CopyIndicesJobU16
                            {
                                Src = srcTris,
                                Dst = ibU16,
                                DstStart = dstStart,
                                Add = (ushort)ci.slotData.vertexOffset
                            }.Schedule();
                            indexJobs.Add(job);
                        }
                        else
                        {
                            var job = new CopyIndicesJobInt
                            {
                                Src = srcTris,
                                Dst = ibInt,
                                DstStart = dstStart,
                                Add = ci.slotData.vertexOffset
                            }.Schedule();
                            indexJobs.Add(job);
                        }
                        subWrite[dstSub] += triLen;
                    }
                }
            }
            sw.Stop();
            Ticks_IndexJobsSchedule += sw.ElapsedTicks;

            // Complete all scheduled index jobs once
            if (indexJobs.Count > 0)
            {
                sw.Restart();
                var handles = new NativeArray<JobHandle>(indexJobs.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < indexJobs.Count; i++) handles[i] = indexJobs[i];
                JobHandle.CompleteAll(handles);
                handles.Dispose();
                indexJobs.Clear();
                sw.Stop();
                Ticks_IndexJobsComplete += sw.ElapsedTicks;
            }



            // UMA atlas UV remap — in place on MeshData UV buffer
            if (hasUV)
            {
                sw.Restart();
                RecalculateUVForUMA(vC01, umaData, batch.AtlasResolution, batch.CurrentRendererIndex);
                sw.Stop();
                Ticks_UVRemap += sw.ElapsedTicks;
            }

            // Submesh descriptors
            sw.Restart();
            for (int i = 0; i < subMeshCount; i++)
            {
                var smd = new SubMeshDescriptor
                {
                    topology = MeshTopology.Triangles,
                    indexStart = subIndexStart[i],
                    indexCount = subMeshTriangleLength[i],
                    baseVertex = 0,
                    vertexCount = vertexCount
                };
                md.SetSubMesh(i, smd, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            }
            sw.Stop();
            Ticks_SetSubmeshes += sw.ElapsedTicks;

            // Create Mesh and apply
            sw.Restart();
            var mesh = batch.Renderer.sharedMesh ?? new Mesh();
            mesh.indexFormat = indexFormat;
            Mesh.ApplyAndDisposeWritableMeshData(mda, new[] { mesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            sw.Stop();
            Ticks_ApplyMeshData += sw.ElapsedTicks;


            // Complete bone weight remap jobs (parallel path)
            if (UseParallelBoneWeights && bwRemap.IsCreated)
            {
                var swBW = System.Diagnostics.Stopwatch.StartNew();
                var job = new RemapAllBoneWeightsJob
                {
                    Weights = nativeBoneWeights,
                    RemappedIndex = bwRemap
                }.Schedule(nativeBoneWeights.Length, 256);
                job.Complete();
                swBW.Stop();
                // Consume existing timing bucket (optional): Ticks_BuildBoneWeights += swBW.ElapsedTicks;
                bwRemap.Dispose();
            }

#if UNITY_EDITOR && (UMA_PARALLEL_BONEWEIGHTS_VALIDATE || UMA_DEBUG_BONEWEIGHTS_VALIDATE)
            if (UseParallelBoneWeights)
            {
                // Lightweight validation: sample first few weights of each source to ensure boneIndex < bindPoses.Count
                int samples = 0;
                for (int s = 0; s < sources.Length && samples < BoneWeightValidateSampleCount; s++)
                {
                    var srcValidate = sources[s].meshData;
                    int len = Math.Min(srcValidate.ManagedBoneWeights.Length, 4);
                    for (int i = 0; i < len && samples < BoneWeightValidateSampleCount; i++, samples++)
                    {
                        var bw = nativeBoneWeights[boneWeightOffset - srcValidate.ManagedBoneWeights.Length + i];
                        if (bw.boneIndex < 0 || bw.boneIndex >= bindPoses.Count)
                        {
                            Debug.LogWarning($"[UMA] BoneWeight remap validation failed (boneIndex {bw.boneIndex} out of range 0..{bindPoses.Count - 1}) in source {s}.");
                            break;
                        }
                    }
                }
            }
#endif
            // Bindposes and weights
            sw.Restart();
            mesh.bindposes = bindPoses.ToArray();
            mesh.SetBoneWeights(nativeBonesPerVertex, nativeBoneWeights);
            sw.Stop();
            Ticks_SetBindposesAndWeights += sw.ElapsedTicks;

            // Assign to renderer and bones
            sw.Restart();
            batch.Renderer.sharedMesh = mesh;
            if (string.IsNullOrEmpty(mesh.name)) mesh.name = "UMAMesh (MeshAPI)";

            if (umaData != null && umaData.skeleton != null)
            {
                batch.Renderer.bones = umaData.skeleton.HashesToTransforms(bonesList.ToArray());
                if (batch.Renderer.rootBone == null)
                    batch.Renderer.rootBone = umaData.GetGlobalTransform();
            }
            sw.Stop();
            Ticks_AssignBones += sw.ElapsedTicks;

            // Cloth
            sw.Restart();
            clothCoeffs = hasCloth ? BuildClothCoefficients(sources) : null;
            sw.Stop();
            Ticks_BuildCloth += sw.ElapsedTicks;

            nativeBonesPerVertex.Dispose();
            nativeBoneWeights.Dispose();
            if (bwRemap.IsCreated) bwRemap.Dispose(); // safety (should already be disposed)

            totalSW.Stop();
            Ticks_CombineInternalTotal += totalSW.ElapsedTicks;
        }
#endif


        #region Jobs and helpers

#if UMA_UNSAFE
        private static unsafe void FastCopyPositionsAndBoundsUnsafe(
            NativeArray<Vector3> dst, int dstStart,
            Vector3[] srcVertices, Vector3[] srcNormals,
            int count, float expandAlongNormal,
            ref Vector3 boundsMin, ref Vector3 boundsMax)
        {
            var dstPtr = (Vector3*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<Vector3>());

            if (expandAlongNormal > 0f && srcNormals != null && srcNormals.Length >= count)
            {
                fixed (Vector3* sV = srcVertices)
                fixed (Vector3* sN = srcNormals)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 v = sV[i] + sN[i] * expandAlongNormal;
                        dstPtr[i] = v;

                        if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x;
                        if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y;
                        if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z;
                    }
                }
            }
            else
            {
                fixed (Vector3* sV = srcVertices)
                {
                    long bytes = (long)count * UnsafeUtility.SizeOf<Vector3>();
                    UnsafeUtility.MemCpy(dstPtr, sV, bytes);

                    for (int i = 0; i < count; i++)
                    {
                        Vector3 v = sV[i];
                        if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x;
                        if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y;
                        if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z;
                    }
                }
            }
        }

        private static unsafe void PackNormTanUnsafe(
            NativeArray<SkinnedMeshCombinerMeshAPI.NormTan> dst, int dstStart,
            Vector3[] normals, Vector4[] tangents, int count,
            bool hasNormals, bool hasTangents)
        {
            var dstPtr = (NormTan*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<NormTan>());
            bool nValid = hasNormals && normals != null && normals.Length >= count;
            bool tValid = hasTangents && tangents != null && tangents.Length >= count;

            Vector3 zeroN = default;
            Vector4 defT = new Vector4(0, 0, 0, 1);

            if (nValid && tValid)
            {
                fixed (Vector3* nP = normals)
                fixed (Vector4* tP = tangents)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].normal = nP[i];
                        dstPtr[i].tangent = tP[i];
                    }
                }
            }
            else if (nValid)
            {
                fixed (Vector3* nP = normals)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].normal = nP[i];
                        dstPtr[i].tangent = defT;
                    }
                }
            }
            else if (tValid)
            {
                fixed (Vector4* tP = tangents)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].normal = zeroN;
                        dstPtr[i].tangent = tP[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    dstPtr[i].normal = zeroN;
                    dstPtr[i].tangent = defT;
                }
            }
        }

        private static unsafe void PackColUV01Unsafe(
            NativeArray<SkinnedMeshCombinerMeshAPI.ColUV01> dst, int dstStart,
            Color32[] colors, Vector2[] uv0, Vector2[] uv1, int count,
            bool hasColors32, bool hasUV0, bool hasUV1)
        {
            var dstPtr = (ColUV01*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<ColUV01>());
            bool cValid = hasColors32 && colors != null && colors.Length >= count;
            bool u0Valid = hasUV0 && uv0 != null && uv0.Length >= count;
            bool u1Valid = hasUV1 && uv1 != null && uv1.Length >= count;

            Color32 white = new Color32(255, 255, 255, 255);

            if (cValid && u0Valid && u1Valid)
            {
                fixed (Color32* cP = colors)
                fixed (Vector2* u0P = uv0)
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else if (cValid && u0Valid)
            {
                fixed (Color32* cP = colors)
                fixed (Vector2* u0P = uv0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = default;
                    }
                }
            }
            else if (cValid && u1Valid)
            {
                fixed (Color32* cP = colors)
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = default;
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else if (u0Valid && u1Valid)
            {
                fixed (Vector2* u0P = uv0)
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = white;
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else if (cValid)
            {
                fixed (Color32* cP = colors)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = default;
                        dstPtr[i].uv1 = default;
                    }
                }
            }
            else if (u0Valid)
            {
                fixed (Vector2* u0P = uv0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = white;
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = default;
                    }
                }
            }
            else if (u1Valid)
            {
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = white;
                        dstPtr[i].uv0 = default;
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    dstPtr[i].color = white;
                    dstPtr[i].uv0 = default;
                    dstPtr[i].uv1 = default;
                }
            }
        }

        private static unsafe void PackUV23Unsafe(
            NativeArray<SkinnedMeshCombinerMeshAPI.UV23> dst, int dstStart,
            Vector2[] uv2, Vector2[] uv3, int count,
            bool hasUV2, bool hasUV3)
        {
            var dstPtr = (UV23*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<UV23>());
            bool u2Valid = hasUV2 && uv2 != null && uv2.Length >= count;
            bool u3Valid = hasUV3 && uv3 != null && uv3.Length >= count;

            if (u2Valid && u3Valid)
            {
                fixed (Vector2* u2P = uv2)
                fixed (Vector2* u3P = uv3)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].uv2 = u2P[i];
                        dstPtr[i].uv3 = u3P[i];
                    }
                }
            }
            else if (u2Valid)
            {
                fixed (Vector2* u2P = uv2)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].uv2 = u2P[i];
                        dstPtr[i].uv3 = default;
                    }
                }
            }
            else if (u3Valid)
            {
                fixed (Vector2* u3P = uv3)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].uv2 = default;
                        dstPtr[i].uv3 = u3P[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    dstPtr[i].uv2 = default;
                    dstPtr[i].uv3 = default;
                }
            }
        }
#endif

        [BurstCompile]
        private struct CopyIndicesJobInt : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Dst;
            public int DstStart;
            public int Add;

            public void Execute()
            {
                for (int i = 0; i < Src.Length; i++)
                    Dst[DstStart + i] = Src[i] + Add;
            }
        }

        [BurstCompile]
        private struct CopyIndicesJobU16 : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [NativeDisableContainerSafetyRestriction] public NativeArray<ushort> Dst;
            public int DstStart;
            public ushort Add;

            public void Execute()
            {
                for (int i = 0; i < Src.Length; i++)
                    Dst[DstStart + i] = (ushort)(Src[i] + Add);
            }
        }

        [BurstCompile]
        private struct MaskedCopyIndicesJobInt : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<byte> Mask;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Dst;
            public int DstStart;
            public int Add;

            public void Execute()
            {
                int dst = DstStart;
                int triCount = Mask.Length;
                for (int t = 0; t < triCount; t++)
                {
                    if (Mask[t] != 0) continue;
                    int i3 = t * 3;
                    Dst[dst++] = Src[i3 + 0] + Add;
                    Dst[dst++] = Src[i3 + 1] + Add;
                    Dst[dst++] = Src[i3 + 2] + Add;
                }
            }
        }

        [BurstCompile]
        private struct MaskedCopyIndicesJobU16 : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<byte> Mask;
            [NativeDisableContainerSafetyRestriction] public NativeArray<ushort> Dst;
            public int DstStart;
            public ushort Add;

            public void Execute()
            {
                int dst = DstStart;
                int triCount = Mask.Length;
                for (int t = 0; t < triCount; t++)
                {
                    if (Mask[t] != 0) continue;
                    int i3 = t * 3;
                    Dst[dst++] = (ushort)(Src[i3 + 0] + Add);
                    Dst[dst++] = (ushort)(Src[i3 + 1] + Add);
                    Dst[dst++] = (ushort)(Src[i3 + 2] + Add);
                }
            }
        }

        private static NativeArray<byte> BitArrayToNative(BitArray ba, Allocator allocator)
        {
            var arr = new NativeArray<byte>(ba.Count, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < ba.Count; i++)
                arr[i] = ba[i] ? (byte)1 : (byte)0;
            return arr;
        }

        private static void AddBlendShapesDirect(
            Mesh mesh,
            SkinnedMeshCombiner.CombineInstance[] sources,
            Dictionary<string, float> baked,
            Dictionary<string, BlendShapeVertexData> meta,
            UMAData.UMARecipe recipe,
            int[] sourceVertexOffsets,
            int vertexCount)
        {
            // Aggregate per shape per frame into temp arrays, then add to Mesh, discard immediately.
            foreach (var kv in meta)
            {
                string shapeName = kv.Key;
                var info = kv.Value;

                for (int f = 0; f < info.frameCount; f++)
                {
                    var dv = new Vector3[vertexCount];
                    Vector3[] dn = info.hasNormals ? new Vector3[vertexCount] : Array.Empty<Vector3>();
                    Vector3[] dt = info.hasTangents ? new Vector3[vertexCount] : Array.Empty<Vector3>();

                    for (int s = 0; s < sources.Length; s++)
                    {
                        var srcShapes = SkinnedMeshCombiner.GetBlendshapeSources(sources[s].meshData, recipe);
                        if (srcShapes == null || srcShapes.Count == 0) continue;

                        for (int i = 0; i < srcShapes.Count; i++)
                        {
                            var ubs = srcShapes[i];
                            if (ubs.shapeName != shapeName) continue;

                            int vo = sourceVertexOffsets[s];
                            int vc = sources[s].meshData.vertexCount;

                            int frameIdx = Mathf.Clamp(f, 0, ubs.frames.Length - 1);
                            var fr = ubs.frames[frameIdx];

                            Array.Copy(fr.deltaVertices, 0, dv, vo, vc);

                            if (info.hasNormals && fr.deltaNormals != null && fr.deltaNormals.Length == vc)
                                Array.Copy(fr.deltaNormals, 0, dn, vo, vc);

                            if (info.hasTangents && fr.deltaTangents != null && fr.deltaTangents.Length == vc)
                                Array.Copy(fr.deltaTangents, 0, dt, vo, vc);
                        }
                    }

                    float w = (info.frameWeights != null && f < info.frameWeights.Length) ? info.frameWeights[f] : 100f;
                    mesh.AddBlendShapeFrame(shapeName, w, dv, dn, dt);
                }
            }
        }

        #endregion

        #region UMA helpers and retained logic

        [Flags]
        private enum MeshComponents
        {
            none = 0,
            has_normals = 1,
            has_tangents = 2,
            has_colors32 = 4,
            has_uv = 8,
            has_uv2 = 16,
            has_uv3 = 32,
            has_uv4 = 64,
            has_blendShapes = 128,
            has_clothSkinning = 256,
        }

        private class BlendShapeVertexData
        {
            public bool hasNormals = false;
            public bool hasTangents = false;
            public int frameCount = 0;
            public float[] frameWeights;
            public int index;
        }

        private static void AnalyzeSources(
            SkinnedMeshCombiner.CombineInstance[] sources,
            int[] subMeshTriangleLength,
            ref int vertexCount,
            ref int boneWeightCount,
            ref int bindPoseCount,
            ref int transformHierarchyCount,
            ref MeshComponents meshComponents)
        {
            Array.Fill(subMeshTriangleLength, 0);

            for (int j = 0; j < sources.Length; j++)
            {
                var src = sources[j];

                boneWeightCount += src.meshData.ManagedBoneWeights.Length;
                vertexCount += src.meshData.vertices.Length;
                bindPoseCount += src.meshData.bindPoses.Length;
                transformHierarchyCount += src.meshData.umaBones.Length;

                if (src.meshData.normals != null && src.meshData.normals.Length != 0) meshComponents |= MeshComponents.has_normals;
                if (src.meshData.tangents != null && src.meshData.tangents.Length != 0) meshComponents |= MeshComponents.has_tangents;
                if (src.meshData.uv != null && src.meshData.uv.Length != 0) meshComponents |= MeshComponents.has_uv;
                if (src.meshData.uv2 != null && src.meshData.uv2.Length != 0) meshComponents |= MeshComponents.has_uv2;
                if (src.meshData.uv3 != null && src.meshData.uv3.Length != 0) meshComponents |= MeshComponents.has_uv3;
                if (src.meshData.uv4 != null && src.meshData.uv4.Length != 0) meshComponents |= MeshComponents.has_uv4;
                if (src.meshData.colors32 != null && src.meshData.colors32.Length != 0) meshComponents |= MeshComponents.has_colors32;
                if (src.meshData.clothSkinningSerialized != null && src.meshData.clothSkinningSerialized.Length != 0) meshComponents |= MeshComponents.has_clothSkinning;

                for (int i = 0; i < src.meshData.subMeshCount; i++)
                {
                    int dest = src.targetSubmeshIndices[i];
                    if (dest < 0) continue;

                    int subLen = src.meshData.submeshes[i].GetTriangles().Length;
                    int triLen = (src.triangleMask == null) ? subLen : (subLen - (UMAUtils.GetCardinality(src.triangleMask[i]) * 3));
                    subMeshTriangleLength[dest] += triLen;
                }
            }
        }

        private static void AnalyzeBlendShapeSources(
            SkinnedMeshCombiner.CombineInstance[] sources,
            Dictionary<string, float> bakedBlendshapes,
            ref MeshComponents meshComponents,
            out Dictionary<string, BlendShapeVertexData> blendShapeNames,
            UMAData.UMARecipe recipe)
        {
            blendShapeNames = new Dictionary<string, BlendShapeVertexData>();

            int bakedCount = 0;
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k];
                var sourceShapes = SkinnedMeshCombiner.GetBlendshapeSources(src.meshData, recipe);
                if (sourceShapes.Count == 0) continue;

                for (int j = 0; j < sourceShapes.Count; j++)
                {
                    var ubs = sourceShapes[j];
                    string shapeName = ubs.shapeName;

                    if (bakedBlendshapes.ContainsKey(shapeName))
                    {
                        bakedCount++;
                        continue;
                    }

                    if (!blendShapeNames.ContainsKey(shapeName))
                        blendShapeNames.Add(shapeName, new BlendShapeVertexData());

                    var meta = blendShapeNames[shapeName];
                    meta.hasNormals |= ubs.frames[0].HasNormals();
                    meta.hasTangents |= ubs.frames[0].HasTangents();

                    if (ubs.frames.Length > meta.frameCount)
                    {
                        meta.frameCount = ubs.frames.Length;
                        meta.frameWeights = new float[meta.frameCount];
                        for (int i = 0; i < meta.frameCount; i++)
                            meta.frameWeights[i] = ubs.frames[i].frameWeight;
                    }
                }
            }

            if (blendShapeNames.Count > 0 || bakedCount > 0)
                meshComponents |= MeshComponents.has_blendShapes;
        }
        private static void BuildBoneWeights(
    UMAMeshData data,
    NativeArray<BoneWeight1> dest,
    NativeArray<byte> destBonesPerVertex,
    int destIndex,
    int destBoneWeightIndex,
    Dictionary<int, BoneIndexEntry> bonesCollection,
    List<Matrix4x4> bindPosesList,
    List<int> bonesList)
        {
            var bones = data.boneNameHashes;
            var bindPoses = data.bindPoses;

            var pool = ArrayPool<int>.Shared;
            var boneMapping = pool.Rent(bones.Length);
            try
            {
                for (int i = 0; i < bones.Length; i++)
                    boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);

                NativeArray<byte>.Copy(data.ManagedBonesPerVertex, 0, destBonesPerVertex, destIndex, data.ManagedBonesPerVertex.Length);
                NativeArray<BoneWeight1>.Copy(data.ManagedBoneWeights, 0, dest, destBoneWeightIndex, data.ManagedBoneWeights.Length);

                for (int i = 0; i < data.ManagedBoneWeights.Length; i++)
                {
                    var bw = dest[destBoneWeightIndex + i];
                    bw.boneIndex = boneMapping[bw.boneIndex];
                    dest[destBoneWeightIndex + i] = bw;
                }
            }
            finally
            {
                pool.Return(boneMapping, clearArray: false);
            }
        }

        private static ClothSkinningCoefficient[] BuildClothCoefficients(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var clothDict = new Dictionary<Vector3, int>(1024);
            var result = new List<ClothSkinningCoefficient>(1024);

            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k];
                int count = src.meshData.vertexCount;

                if (src.meshData.clothSkinningSerialized != null && src.meshData.clothSkinningSerialized.Length > 0)
                {
                    var local = new Dictionary<Vector3, int>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var v = src.meshData.vertices[i];
                        if (!local.ContainsKey(v))
                        {
                            local.Add(v, local.Count);
                            if (!clothDict.TryGetValue(v, out var globalIndex))
                            {
                                var coeff = new ClothSkinningCoefficient();
                                ConvertData(ref src.meshData.clothSkinningSerialized[local[v]], ref coeff);
                                clothDict.Add(v, result.Count);
                                result.Add(coeff);
                            }
                            else
                            {
                                var coeff = result[clothDict[v]];
                                ConvertData(ref src.meshData.clothSkinningSerialized[local[v]], ref coeff);
                                result[clothDict[v]] = coeff;
                            }
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        var v = src.meshData.vertices[i];
                        if (!clothDict.ContainsKey(v))
                        {
                            clothDict.Add(v, result.Count);
                            result.Add(new ClothSkinningCoefficient { maxDistance = 0, collisionSphereDistance = float.MaxValue });
                        }
                    }
                }
            }

            return (result.Count > 0) ? result.ToArray() : null;
        }

        private class BoneIndexEntry
        {
            public int index;
            public List<int> indices;
            public int Count => index >= 0 ? 1 : indices.Count;
            public int this[int idx]
            {
                get
                {
                    if (index >= 0)
                    {
                        if (idx == 0) return index;
                        throw new ArgumentOutOfRangeException();
                    }
                    return indices[idx];
                }
            }
            internal void AddIndex(int idx)
            {
                if (index >= 0)
                {
                    indices = new List<int>(10);
                    indices.Add(index);
                    index = -1;
                }
                indices.Add(idx);
            }
        }

        private static bool CompareSkinningMatrices(Matrix4x4 m1, ref Matrix4x4 m2)
        {
            if (Mathf.Abs(m1.m00 - m2.m00) > 0.0001) return false;
            if (Mathf.Abs(m1.m01 - m2.m01) > 0.0001) return false;
            if (Mathf.Abs(m1.m02 - m2.m02) > 0.0001) return false;
            if (Mathf.Abs(m1.m03 - m2.m03) > 0.0001) return false;
            if (Mathf.Abs(m1.m10 - m2.m10) > 0.0001) return false;
            if (Mathf.Abs(m1.m11 - m2.m11) > 0.0001) return false;
            if (Mathf.Abs(m1.m12 - m2.m12) > 0.0001) return false;
            if (Mathf.Abs(m1.m13 - m2.m13) > 0.0001) return false;
            if (Mathf.Abs(m1.m20 - m2.m20) > 0.0001) return false;
            if (Mathf.Abs(m1.m21 - m2.m21) > 0.0001) return false;
            if (Mathf.Abs(m1.m22 - m2.m22) > 0.0001) return false;
            if (Mathf.Abs(m1.m23 - m2.m23) > 0.0001) return false;
            return true;
        }

        private static int TranslateBoneIndex(
            int index,
            int[] bonesHashes,
            Matrix4x4[] bindPoses,
            Dictionary<int, BoneIndexEntry> bonesCollection,
            List<Matrix4x4> bindPosesList,
            List<int> bonesList)
        {
            int boneHash = bonesHashes[index];
            if (bonesCollection.TryGetValue(boneHash, out var entry))
            {
                for (int i = 0; i < entry.Count; i++)
                {
                    var res = entry[i];
                    if (CompareSkinningMatrices(bindPosesList[res], ref bindPoses[index]))
                        return res;
                }
                var idx = bindPosesList.Count;
                entry.AddIndex(idx);
                bindPosesList.Add(bindPoses[index]);
                bonesList.Add(boneHash);
                return idx;
            }
            else
            {
                var idx = bindPosesList.Count;
                bonesCollection.Add(boneHash, new BoneIndexEntry() { index = idx });
                bindPosesList.Add(bindPoses[index]);
                bonesList.Add(boneHash);
                return idx;
            }
        }

        [BurstCompile]
        private struct RemapAllBoneWeightsJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<BoneWeight1> Weights;          // Combined destination weights
            [ReadOnly] public NativeArray<int> RemappedIndex; // Per-weight final bone index
            public void Execute(int i)
            {
                var bw = Weights[i];
                bw.boneIndex = RemappedIndex[i];
                Weights[i] = bw;
            }
        }

        private static JobHandle BuildBoneWeightsParallel(
            UMAMeshData data,
            NativeArray<BoneWeight1> dest,
            NativeArray<byte> destBonesPerVertex,
            int destVertexStart,
            int destBoneWeightStart,
            Dictionary<int, BoneIndexEntry> bonesCollection,
            List<Matrix4x4> bindPosesList,
            List<int> bonesList,
            out bool scheduled)
        {
            scheduled = false;
            return default;
        }


        private static VertexAttributeDescriptor[] BuildVertexLayout(
            bool hasNormals, bool hasTangents, bool hasUV, bool hasUV2, bool hasUV3, bool hasUV4, bool hasColors32)
        {
            var list = new List<VertexAttributeDescriptor>(8)
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0)
    };
            int stream = 1;
            if (hasNormals || hasTangents) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream)); stream++; }
            if (hasColors32 || hasUV || hasUV2) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, stream)); stream++; }
            if (hasUV3 || hasUV4) { list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, stream)); }
            return list.ToArray();
        }
        private static void WorkingRecalculateUVForUMA(NativeArray<ColUV01> vC01, UMAData umaData, int atlasResolution, int currentRendererIndex)
        {
            if (!vC01.IsCreated || vC01.Length == 0 || umaData == null || umaData.generatedMaterials == null)
                return;

            var targetRendererAsset = umaData.GetRendererAsset(currentRendererIndex);
           // if (targetRendererAsset == null)
           //     return;

            try
            {
                // Per-slot best fragment (cropping candidate preferred)
                var bestFragmentPerSlot = new Dictionary<SlotData, FragmentChoice>(64);
                var materials = umaData.generatedMaterials.materials;

                for (int mi = 0; mi < materials.Count; mi++)
                {
                    var gm = materials[mi];
                    if (gm == null) continue;
                    if (gm.rendererAsset != targetRendererAsset) continue;
                    if (gm.umaMaterial == null || !gm.umaMaterial.IsGeneratedTextures) continue;

                    var frags = gm.materialFragments;
                    for (int fi = 0; fi < frags.Count; fi++)
                    {
                        var frag = frags[fi];
                        if (frag == null) continue;

                        var slot = frag.slotData;
                        if (slot == null || slot.asset == null || slot.asset.meshData == null) continue;
                        if (slot.skinnedMeshRenderer != currentRendererIndex) continue;

                        bool isCroppingCandidate = frag.isRectShared && slot.useAtlasOverlay;

                        if (!bestFragmentPerSlot.TryGetValue(slot, out var existing))
                        {
                            bestFragmentPerSlot.Add(slot, new FragmentChoice
                            {
                                slot = slot,
                                atlasRegion = frag.atlasRegion,
                                isRectShared = frag.isRectShared,
                                slotUseAtlasOverlay = slot.useAtlasOverlay,
                                overlayList = frag.overlayList,                 // assumed List<OverlayData>
                                resolutionScale = gm.resolutionScale,           // assumed Vector2
                                cropResolution = gm.cropResolution,             // assumed Vector2
                                prefersCropping = isCroppingCandidate
                            });
                        }
                        else if (isCroppingCandidate && !existing.prefersCropping)
                        {
                            existing.atlasRegion = frag.atlasRegion;
                            existing.isRectShared = frag.isRectShared;
                            existing.slotUseAtlasOverlay = slot.useAtlasOverlay;
                            existing.overlayList = frag.overlayList;
                            existing.resolutionScale = gm.resolutionScale;
                            existing.cropResolution = gm.cropResolution;
                            existing.prefersCropping = true;
                            bestFragmentPerSlot[slot] = existing;
                        }
                    }
                }

                foreach (var kvp in bestFragmentPerSlot)
                {
                    var slot = kvp.Key;
                    var choice = kvp.Value;

                    int vertexCount = slot.asset.meshData.vertexCount;
                    int start = slot.vertexOffset;

                    if (start < 0 || start + vertexCount > vC01.Length)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"RecalculateUVForUMA: Slot '{slot.asset.name}' vertex range out of bounds (start {start}, count {vertexCount}, len {vC01.Length}). Skipping.");
#endif
                        continue;
                    }

                    var atlasRect = choice.atlasRegion;

                    float atlasXMin = atlasRect.xMin / atlasResolution;
                    float atlasXMax = atlasRect.xMax / atlasResolution;
                    float atlasYMin = atlasRect.yMin / atlasResolution;
                    float atlasYMax = atlasRect.yMax / atlasResolution;

                    if (choice.isRectShared && choice.slotUseAtlasOverlay)
                    {
                        OverlayData foundRect = null;
                        // overlayList can be null if fragment did not populate overlays (defensive)
                        var overlays = choice.overlayList;
                        if (overlays != null)
                        {
                            for (int i = 0; i < overlays.Count; i++)
                            {
                                var ov = overlays[i];
                                if (slot.slotName != null && ov.overlayName != null && ov.overlayName.Contains(slot.slotName))
                                {
                                    foundRect = ov;
                                    break;
                                }
                            }
                        }
                        if (foundRect != null && foundRect.rect != Rect.zero)
                        {
                            var size = foundRect.rect.size * choice.resolutionScale;
                            var offsetX = foundRect.rect.x * choice.resolutionScale.x;
                            var offsetY = foundRect.rect.y * choice.resolutionScale.x; // preserve original behavior

                            atlasXMin += (offsetX / choice.cropResolution.x);
                            float atlasXRange = size.x / choice.cropResolution.x;
                            atlasXMax = atlasXMin + atlasXRange;

                            atlasYMin += (offsetY / choice.cropResolution.y);
                            float atlasYRange = size.y / choice.cropResolution.y;
                            atlasYMax = atlasYMin + atlasYRange;
                        }
                    }

                    float rangeX = atlasXMax - atlasXMin;
                    float rangeY = atlasYMax - atlasYMin;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        int vi = start + i;
                        var c01 = vC01[vi];
                        c01.uv0.x = atlasXMin + rangeX * c01.uv0.x;
                        c01.uv0.y = atlasYMin + rangeY * c01.uv0.y;
                        vC01[vi] = c01;
                    }
                }

#if UNITY_EDITOR && UMA_DEBUG_UV_VALIDATE
                foreach (var kvp in bestFragmentPerSlot)
                {
                    var slot = kvp.Key;
                    int vertexCount = slot.asset.meshData.vertexCount;
                    int start = slot.vertexOffset;
                    bool outOfRange = false;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        var uv = vC01[start + i].uv0;
                        if (uv.x < -0.001f || uv.x > 1.001f || uv.y < -0.001f || uv.y > 1.001f)
                        {
                            outOfRange = true;
                            break;
                        }
                    }
                    if (outOfRange)
                        Debug.LogWarning($"RecalculateUVForUMA: UVs for slot '{slot.asset.name}' outside [0,1].");
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private struct FragmentChoice
        {
            public SlotData slot;
            public Rect atlasRegion;
            public bool isRectShared;
            public bool slotUseAtlasOverlay;
            public List<OverlayData> overlayList;
            public Vector2 resolutionScale;
            public Vector2 cropResolution;
            public bool prefersCropping;
        }

#if UMA_MESHAPI_2021
        private static void RecalculateUVForUMA_Optimized(
            NativeArray<ColUV01> vC01,
            UMAData umaData,
            int atlasResolution,
            int currentRendererIndex)
        {
            if (!vC01.IsCreated || vC01.Length == 0 || umaData == null || umaData.generatedMaterials == null)
                return;

            var targetRendererAsset = umaData.GetRendererAsset(currentRendererIndex);
            //if (targetRendererAsset == null) return;

            try
            {
                _uvTransforms.Clear();
                _uvProcessedSlots.Clear();
                float invAtlas = 1f / atlasResolution;

                var materials = umaData.generatedMaterials.materials;
                for (int mIdx = 0; mIdx < materials.Count; mIdx++)
                {
                    var gm = materials[mIdx];
                    if (gm == null) continue;
                    if (gm.rendererAsset != targetRendererAsset) continue;
                    if (gm.umaMaterial == null || !gm.umaMaterial.IsGeneratedTextures) continue;

                    var fragments = gm.materialFragments;
                    for (int f = 0; f < fragments.Count; f++)
                    {
                        var frag = fragments[f];
                        if (frag == null) continue;

                        var slot = frag.slotData;
                        if (slot == null || slot.asset == null || slot.asset.meshData == null)
                            continue;

                        if (_uvProcessedSlots.Contains(slot)) continue; // de-dupe per slot

                        int start = slot.vertexOffset;
                        int count = slot.asset.meshData.vertexCount;
                        if (start < 0 || count <= 0 || (start + count) > vC01.Length)
                            continue;

                        // Base atlas rect
                        var rect = frag.atlasRegion;
                        float xMin = rect.xMin * invAtlas;
                        float xMax = rect.xMax * invAtlas;
                        float yMin = rect.yMin * invAtlas;
                        float yMax = rect.yMax * invAtlas;

                        float xRange = xMax - xMin;
                        float yRange = yMax - yMin;

                        // Shared rect cropping adjustment (same logic as legacy)
                        if (frag.isRectShared && slot.useAtlasOverlay)
                        {
                            OverlayData foundRect = null;
                            var overlays = frag.overlayList;
                            if (overlays != null)
                            {
                                for (int o = 0; o < overlays.Count; o++)
                                {
                                    var ov = overlays[o];
                                    if (slot.slotName != null &&
                                        ov.overlayName != null &&
                                        ov.overlayName.Contains(slot.slotName))
                                    {
                                        foundRect = ov;
                                        break;
                                    }
                                }
                            }
                            if (foundRect != null && foundRect.rect != Rect.zero)
                            {
                                var size = foundRect.rect.size * gm.resolutionScale;
                                var offsetX = foundRect.rect.x * gm.resolutionScale.x;
                                var offsetY = foundRect.rect.y * gm.resolutionScale.x;

                                xMin += (offsetX / gm.cropResolution.x);
                                xRange = size.x / gm.cropResolution.x;

                                yMin += (offsetY / gm.cropResolution.y);
                                yRange = size.y / gm.cropResolution.y;
                            }
                        }

                        _uvTransforms.Add(new UVTransform
                        {
                            start = start,
                            count = count,
                            xMin = xMin,
                            yMin = yMin,
                            xScale = xRange,
                            yScale = yRange
                        });

                        _uvProcessedSlots.Add(slot);
                    }
                }

                int transformCount = _uvTransforms.Count;
                if (transformCount == 0) return;

                // Small meshes: do synchronous to avoid job overhead
                if (!UseParallelUVRemap || vC01.Length < UV_PARALLEL_MIN_VERTS || transformCount == 1)
                {
                    for (int i = 0; i < transformCount; i++)
                    {
                        var t = _uvTransforms[i];
                        int end = t.start + t.count;
                        float xMin = t.xMin, yMin = t.yMin, xScale = t.xScale, yScale = t.yScale;
                        for (int v = t.start; v < end; v++)
                        {
                            var c = vC01[v];
                            c.uv0.x = xMin + c.uv0.x * xScale;
                            c.uv0.y = yMin + c.uv0.y * yScale;
                            vC01[v] = c;
                        }
                    }
                }
                else
                {
                    // Parallel job path
                    var naTransforms = new NativeArray<UVTransform>(transformCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < transformCount; i++)
                        naTransforms[i] = _uvTransforms[i];

                    var job = new ApplyUVTransformsJob
                    {
                        Vertices = vC01,
                        Transforms = naTransforms
                    }.Schedule(transformCount, 1); // 1 per slot; inner loop is big chunk

                    job.Complete();
                    naTransforms.Dispose();
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogException(ex);
#endif
                // Fallback if something unexpected
                RecalculateUVForUMA(vC01, umaData, atlasResolution, currentRendererIndex);
            }
            finally
            {
                _uvTransforms.Clear();
                _uvProcessedSlots.Clear();
            }
        }
#endif
        private static void RecalculateUVForUMA(NativeArray<ColUV01> vC01, UMAData umaData, int atlasResolution, int currentRendererIndex)
        {
            if (!vC01.IsCreated || vC01.Length == 0 || umaData == null || umaData.generatedMaterials == null)
                return;

            var targetRendererAsset = umaData.GetRendererAsset(currentRendererIndex);

            try
            {
                var materials = umaData.generatedMaterials.materials;
                // Track slots already remapped (avoid reprocessing when a slot appears in multiple fragments)
                var processedSlots = new HashSet<SlotData>();

                for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
                {
                    var generatedMaterial = materials[materialIndex];
                    if (generatedMaterial == null) continue;

                    if (generatedMaterial.rendererAsset != targetRendererAsset)
                        continue;

                    if (generatedMaterial.umaMaterial == null || !generatedMaterial.umaMaterial.IsGeneratedTextures)
                        continue;

                    var fragments = generatedMaterial.materialFragments;
                    for (int m = 0; m < fragments.Count; m++)
                    {
                        var fragment = fragments[m];
                        if (fragment == null) continue;
                        var slot = fragment.slotData;
                        if (slot == null || slot.asset == null || slot.asset.meshData == null)
                            continue;

                        // De-duplicate by slot reference
                        if (processedSlots.Contains(slot))
                            continue;

                        int declaredRenderer = slot.skinnedMeshRenderer;
                        // Accept if it matches the current renderer OR (fallback) vertex range is inside array (slot renderer index might not be set yet for some pipelines).
                        bool rendererMatches = (declaredRenderer == currentRendererIndex);
                        int vertexCount = slot.asset.meshData.vertexCount;
                        int start = slot.vertexOffset;

                        if (!rendererMatches)
                        {
                            if (start < 0 || start >= vC01.Length)
                                continue; // Definitely not ours
#if UNITY_EDITOR
#if UMA_DEBUG_UV
                            Debug.LogWarning($"RecalculateUVForUMA: slot '{slot.asset.name}' had skinnedMeshRenderer={declaredRenderer} expected {currentRendererIndex} but falls inside range; remapping anyway.");
#endif
#endif
                        }

                        // Bounds defensive clamp
                        if (start < 0 || start + vertexCount > vC01.Length)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning($"RecalculateUVForUMA: vertex range out of bounds (start {start} count {vertexCount} len {vC01.Length}) for slot {slot.asset.name}. Clamping.");
#endif
                            vertexCount = Mathf.Max(0, Mathf.Min(vertexCount, vC01.Length - Math.Max(0, start)));
                        }
                        if (vertexCount <= 0)
                            continue;

                        // Atlas rect from fragment
                        var tempAtlasRect = fragment.atlasRegion;
                        float atlasXMin = tempAtlasRect.xMin / atlasResolution;
                        float atlasXMax = tempAtlasRect.xMax / atlasResolution;
                        float atlasYMin = tempAtlasRect.yMin / atlasResolution;
                        float atlasYMax = tempAtlasRect.yMax / atlasResolution;
                        float atlasXRange = atlasXMax - atlasXMin;
                        float atlasYRange = atlasYMax - atlasYMin;

                        // Shared rect adjustment (same as previous logic)
                        if (fragment.isRectShared && slot.useAtlasOverlay)
                        {
                            OverlayData foundRect = null;
                            for (int i = 0; i < fragment.overlayList.Count; i++)
                            {
                                var ov = fragment.overlayList[i];
                                if (slot.slotName != null &&
                                    ov.overlayName != null &&
                                    ov.overlayName.Contains(slot.slotName))
                                {
                                    foundRect = ov;
                                    break;
                                }
                            }
                            if (foundRect != null && foundRect.rect != Rect.zero)
                            {
                                var size = foundRect.rect.size * generatedMaterial.resolutionScale;
                                var offsetX = foundRect.rect.x * generatedMaterial.resolutionScale.x;
                                var offsetY = foundRect.rect.y * generatedMaterial.resolutionScale.x;

                                atlasXMin += (offsetX / generatedMaterial.cropResolution.x);
                                atlasXRange = size.x / generatedMaterial.cropResolution.x;

                                atlasYMin += (offsetY / generatedMaterial.cropResolution.y);
                                atlasYRange = size.y / generatedMaterial.cropResolution.y;
                            }
                        }

                        // Remap UVs
                        for (int i = 0; i < vertexCount; i++)
                        {
                            int vi = start + i;
                            var c01 = vC01[vi];
                            c01.uv0.x = atlasXMin + atlasXRange * c01.uv0.x;
                            c01.uv0.y = atlasYMin + atlasYRange * c01.uv0.y;
                            vC01[vi] = c01;
                        }

                        processedSlots.Add(slot);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void OldRecalculateUVForUMA(NativeArray<ColUV01> vC01, UMAData umaData, int atlasResolution, int currentRendererIndex)
        {
            try
            {
                int idx = 0;
                for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
                {
                    var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
                    if (generatedMaterial.rendererAsset != umaData.GetRendererAsset(currentRendererIndex))
                    {
                        for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                        {
                            var fragment = generatedMaterial.materialFragments[i];
                            idx += fragment.slotData.asset.meshData.vertices.Length;
                        }
                        continue;
                    }

                    if (!generatedMaterial.umaMaterial.IsGeneratedTextures)
                    {
                        for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                        {
                            var fragment = generatedMaterial.materialFragments[i];
                            idx += fragment.slotData.asset.meshData.vertices.Length;
                        }
                        continue;
                    }

                    for (int m = 0; m < generatedMaterial.materialFragments.Count; m++)
                    {
                        var fragment = generatedMaterial.materialFragments[m];
                        var tempAtlasRect = fragment.atlasRegion;
                        int vertexCount = fragment.slotData.asset.meshData.vertices.Length;

                        float atlasXMin = tempAtlasRect.xMin / atlasResolution;
                        float atlasXMax = tempAtlasRect.xMax / atlasResolution;
                        float atlasXRange = atlasXMax - atlasXMin;
                        float atlasYMin = tempAtlasRect.yMin / atlasResolution;
                        float atlasYMax = tempAtlasRect.yMax / atlasResolution;
                        float atlasYRange = atlasYMax - atlasYMin;

                        if (fragment.isRectShared && fragment.slotData.useAtlasOverlay)
                        {
                            OverlayData foundRect = null;
                            for (int i = 0; i < fragment.overlayList.Count; i++)
                            {
                                var ov = fragment.overlayList[i];
                                if (fragment.slotData.slotName != null && ov.overlayName != null && ov.overlayName.Contains(fragment.slotData.slotName))
                                {
                                    foundRect = ov; break;
                                }
                            }
                            if (foundRect != null && foundRect.rect != Rect.zero)
                            {
                                var size = foundRect.rect.size * generatedMaterial.resolutionScale;
                                var offsetX = foundRect.rect.x * generatedMaterial.resolutionScale.x;
                                var offsetY = foundRect.rect.y * generatedMaterial.resolutionScale.x;

                                atlasXMin += (offsetX / generatedMaterial.cropResolution.x);
                                atlasXRange = size.x / generatedMaterial.cropResolution.x;

                                atlasYMin += (offsetY / generatedMaterial.cropResolution.y);
                                atlasYRange = size.y / generatedMaterial.cropResolution.y;
                            }
                        }

                        for (int i = 0; i < vertexCount; i++)
                        {
                            var c01 = vC01[idx];
                            c01.uv0.x = atlasXMin + atlasXRange * c01.uv0.x;
                            c01.uv0.y = atlasYMin + atlasYRange * c01.uv0.y;
                            vC01[idx] = c01;
                            idx++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }


        public static void ConvertData(ref Vector2 source, ref ClothSkinningCoefficient dest)
        {
            dest.collisionSphereDistance = source.x;
            dest.maxDistance = source.y;
        }

        private static int FindTargetSubMeshCount(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            int highestTargetIndex = -1;
            for (int i = 0; i < sources.Length; i++)
            {
                var s = sources[i];
                for (int j = 0; j < s.targetSubmeshIndices.Length; j++)
                {
                    int t = s.targetSubmeshIndices[j];
                    if (highestTargetIndex < t) highestTargetIndex = t;
                }
            }
            return highestTargetIndex + 1;
        }

        private static void MergeSortedTransforms(UMATransform[] mergedTransforms, ref int len1, UMATransform[] umaTransforms)
        {
            int newBones = 0;
            int pos1 = 0;
            int pos2 = 0;
            int len2 = umaTransforms.Length;

            while (pos1 < len1 && pos2 < len2)
            {
                long i = ((long)mergedTransforms[pos1].hash) - ((long)umaTransforms[pos2].hash);
                if (i == 0)
                {
                    pos1++;
                    pos2++;
                }
                else if (i < 0)
                {
                    pos1++;
                }
                else
                {
                    pos2++;
                    newBones++;
                }
            }
            newBones += len2 - pos2;
            pos1 = len1 - 1;
            pos2 = len2 - 1;

            len1 += newBones;

            int dest = len1 - 1;

            while (pos1 >= 0 && pos2 >= 0)
            {
                long i = ((long)mergedTransforms[pos1].hash) - ((long)umaTransforms[pos2].hash);
                if (i == 0)
                {
                    mergedTransforms[dest] = mergedTransforms[pos1];
                    pos1--;
                    pos2--;
                }
                else if (i > 0)
                {
                    mergedTransforms[dest] = mergedTransforms[pos1];
                    pos1--;
                }
                else
                {
                    mergedTransforms[dest] = umaTransforms[pos2];
                    pos2--;
                }
                dest--;
            }
            while (pos2 >= 0)
            {
                mergedTransforms[dest] = umaTransforms[pos2];
                pos2--;
                dest--;
            }
        }
        #endregion
    }
}