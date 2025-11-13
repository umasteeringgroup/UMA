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
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
#if UMA_UNSAFE
using UnityEditor;
#endif

namespace UMA
{
    /// <summary>
    /// Unity 2021.3+ MeshData API based combiner.
    /// </summary>
    public static partial class SkinnedMeshCombinerMeshAPI
    {
        public struct RendererBatch
        {
            public SkinnedMeshRenderer Renderer;
            public SkinnedMeshCombiner.CombineInstance[] Sources;
            public int CurrentRendererIndex;
            public int AtlasResolution;
        }
#if UMA_MESHAPI_2021
        public static bool UseParallelBoneWeights = true;
        public static bool UseParallelUVRemap = true;
        private const int UV_PARALLEL_MIN_VERTS = 4000;
        public static Quaternion FixupRotation = Quaternion.Euler(0f, 270f, 90f);
        public static float BoundsInflationFraction = 0.01f;
#endif
        // Timings
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

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NormTan { public Vector3 normal; public Vector4 tangent; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ColUV01 { public Color32 color; public Vector2 uv0; public Vector2 uv1; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct UV23 { public Vector2 uv2; public Vector2 uv3; }

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
                float xMin = t.xMin; float yMin = t.yMin; float xScale = t.xScale; float yScale = t.yScale;
                for (int v = start; v < end; v++)
                {
                    var c = Vertices[v];
                    c.uv0.x = xMin + c.uv0.x * xScale;
                    c.uv0.y = yMin + c.uv0.y * yScale;
                    Vertices[v] = c;
                }
            }
        }
        private struct UVTransform { public int start; public int count; public float xMin; public float yMin; public float xScale; public float yScale; }
        private static readonly List<UVTransform> _uvTransforms = new List<UVTransform>(128);
        private static readonly HashSet<SlotData> _uvProcessedSlots = new HashSet<SlotData>();

        // NEW: Bake blendshape deltas directly into base buffers (for baked shapes)
        private static void BakeShapeIntoBuffers(UMABlendShape shape, float weightInput, NativeArray<Vector3> vPos, NativeArray<NormTan> vNT, int vertexOffset, bool hasNormals, bool hasTangents)
        {
            if (shape == null || shape.frames == null || shape.frames.Length == 0) return;

#if UNITY_EDITOR
            if (Debug.isDebugBuild)
            {
                Debug.Log($"Baking shape {shape.shapeName} at weight {weightInput}");
            }
#endif

            float weight = (weightInput <= 1f) ? weightInput * 100f : weightInput; // allow 0..1 or 0..100
            if (weight <= 0f || Mathf.Approximately(weight, 0f)) return; // Early out: nothing to bake

            // Find the first frame whose declared frameWeight >= requested weight
            int frameIndex;
            for (frameIndex = 0; frameIndex < shape.frames.Length; frameIndex++)
            {
                if (shape.frames[frameIndex].frameWeight >= weight)
                    break;
            }

            float curFactor;
            float prevFactor = 0f;
            bool lerp = false;

            if (frameIndex >= shape.frames.Length)
            {
                // Beyond last frame: clamp to last but scale proportionally
                frameIndex = shape.frames.Length - 1;
                float fw = shape.frames[frameIndex].frameWeight;
                curFactor = (fw > 0f) ? (weight / fw) : 1f;
            }
            else if (frameIndex > 0)
            {
                // Interpolate between previous and current
                lerp = true;
                float prevW = shape.frames[frameIndex - 1].frameWeight;
                float curW = shape.frames[frameIndex].frameWeight;
                float span = curW - prevW;
                float t = (span > Mathf.Epsilon) ? (weight - prevW) / span : 1f;
                t = Mathf.Clamp01(t);
                curFactor = t;
                prevFactor = 1f - t;
            }
            else
            {
                // We are before or at the first frame
                float fw = shape.frames[frameIndex].frameWeight;
                if (fw <= 0f)
                {
                    // First frame weight is 0; baking weight > 0 cannot be represented => treat as no deformation rather than full.
                    return;
                }
                curFactor = Mathf.Clamp01(weight / fw);
            }

            int prevIndex = (frameIndex > 0) ? frameIndex - 1 : frameIndex;
            var cur = shape.frames[frameIndex];
            var prev = shape.frames[prevIndex];

            var dvCur = cur.deltaVertices;
            if (dvCur == null) return;

            var dvPrev = prev.deltaVertices;
            var dnCur = cur.deltaNormals;  var dnPrev = prev.deltaNormals;
            var dtCur = cur.deltaTangents; var dtPrev = prev.deltaTangents;

            int len = dvCur.Length;
            bool bakeNormals  = hasNormals  && vNT.IsCreated && dnCur != null && dnCur.Length == len;
            bool bakeTangents = hasTangents && vNT.IsCreated && dtCur != null && dtCur.Length == len;

            for (int i = 0; i < len; i++)
            {
                Vector3 add;
                if (lerp && dvPrev != null && dvPrev.Length == len)
                {
                    // Proper interpolation between two absolute frames
                    add = dvPrev[i] + (dvCur[i] - dvPrev[i]) * curFactor;
                }
                else
                {
                    add = dvCur[i] * curFactor;
                }

                if (add.sqrMagnitude > 0f)
                {
                    var p = vPos[vertexOffset + i];
                    p += add;
                    vPos[vertexOffset + i] = p;
                }

                if (bakeNormals)
                {
                    Vector3 nAdd;
                    if (lerp && dnPrev != null && dnPrev.Length == len)
                        nAdd = dnPrev[i] + (dnCur[i] - dnPrev[i]) * curFactor;
                    else
                        nAdd = dnCur[i] * curFactor;

                    if (nAdd.sqrMagnitude > 0f)
                    {
                        var nt = vNT[vertexOffset + i];
                        nt.normal += nAdd;
                        vNT[vertexOffset + i] = nt;
                    }
                }

                if (bakeTangents)
                {
                    Vector3 tAdd;
                    if (lerp && dtPrev != null && dtPrev.Length == len)
                        tAdd = dtPrev[i] + (dtCur[i] - dtPrev[i]) * curFactor;
                    else
                        tAdd = dtCur[i] * curFactor;

                    if (tAdd.sqrMagnitude > 0f)
                    {
                        var nt = vNT[vertexOffset + i];
                        var t = nt.tangent;
                        t.x += tAdd.x;
                        t.y += tAdd.y;
                        t.z += tAdd.z;
                        nt.tangent = t;
                        vNT[vertexOffset + i] = nt;
                    }
                }
            }
        }
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
            CombineInternal(new RendererBatch { Renderer = renderer, Sources = sources, CurrentRendererIndex = currentRendererIndex, AtlasResolution = atlasResolution }, umaData, bakedBlendshapes ?? new Dictionary<string, float>(), markDynamic, markNotReadable, umaData.umaRecipe.raceData.FixupRotations ? FixupRotation : Quaternion.identity, out var coeffs);
            return coeffs;
#endif
        }

        public static ClothSkinningCoefficient[] CombineIntoRenderer(
            SkinnedMeshRenderer renderer,
            SkinnedMeshCombiner.CombineInstance[] sources,
            UMAData umaData,
            int currentRendererIndex,
            int atlasResolution,
            Dictionary<string, float> bakedBlendshapes,
            Quaternion boundsRotation,
            bool markDynamic = false,
            bool markNotReadable = false)
        {
#if !UMA_MESHAPI_2021
            throw new NotSupportedException("Requires Unity 2021.3+ MeshData API.");
#else
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (sources == null || sources.Length == 0) throw new ArgumentException("sources empty", nameof(sources));
            if (umaData == null) throw new ArgumentNullException(nameof(umaData));
            CombineInternal(new RendererBatch { Renderer = renderer, Sources = sources, CurrentRendererIndex = currentRendererIndex, AtlasResolution = atlasResolution }, umaData, bakedBlendshapes ?? new Dictionary<string, float>(), markDynamic, markNotReadable, boundsRotation, out var coeffs);
            return coeffs;
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
                    Quaternion.identity,
                    out var clothCoeffs);

                results[i] = clothCoeffs;
            }
            return results;
#endif
        }

        /// <summary>
        /// Overload for batches where a single rotation is applied to all bounds (pass identity to skip).
        /// </summary>
        public static ClothSkinningCoefficient[][] CombineIntoRenderers(
            RendererBatch[] batches,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            Quaternion boundsRotation,
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
                    boundsRotation,
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
            Quaternion boundsRotation,
            out ClothSkinningCoefficient[] clothCoeffs)
        {
            clothCoeffs = null;
            int[] subMeshTriangleLength = null;
            int[] subIndexStart = null;
            int[] subWrite = null;
            int[] sourceVertexOffsets = null;
            try
            {
                var totalSW = System.Diagnostics.Stopwatch.StartNew();
                var sources = batch.Sources;
                int vertexCount = 0, boneWeightCount = 0, bindPoseCount = 0, transformHierarchyCount = 0;
                int subMeshCount = FindTargetSubMeshCount(sources);
                subMeshTriangleLength = ArrayPool<int>.Shared.Rent(subMeshCount);
                MeshComponents flags = MeshComponents.none;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                AnalyzeSources(sources, subMeshTriangleLength, ref vertexCount, ref boneWeightCount, ref bindPoseCount, ref transformHierarchyCount, ref flags);
                sw.Stop(); Ticks_AnalyzeSources += sw.ElapsedTicks;
                Dictionary<string, BlendShapeVertexData> blendShapeNames;
                sw.Restart();
                AnalyzeBlendShapeSources(sources, bakedBlendshapes, ref flags, out blendShapeNames, umaData.umaRecipe);
                sw.Stop(); Ticks_AnalyzeBlendshapes += sw.ElapsedTicks;
                bool hasNormals = (flags & MeshComponents.has_normals) != 0;
                bool hasTangents = (flags & MeshComponents.has_tangents) != 0;
                bool hasUV = (flags & MeshComponents.has_uv) != 0;
                bool hasUV2 = (flags & MeshComponents.has_uv2) != 0;
                bool hasUV3 = (flags & MeshComponents.has_uv3) != 0;
                bool hasUV4 = (flags & MeshComponents.has_uv4) != 0;
                bool hasColors32 = (flags & MeshComponents.has_colors32) != 0;
                bool hasBlendShapes = (flags & MeshComponents.has_blendShapes) != 0;
                bool hasCloth = (flags & MeshComponents.has_clothSkinning) != 0;
                subIndexStart = ArrayPool<int>.Shared.Rent(subMeshCount);
                int totalIndexCount = 0; for (int i = 0, run = 0; i < subMeshCount; i++) { subIndexStart[i] = run; run += subMeshTriangleLength[i]; totalIndexCount = run; }
                sw.Restart();
                var mda = Mesh.AllocateWritableMeshData(1); var md = mda[0];
                md.SetVertexBufferParams(vertexCount, BuildVertexLayout(hasNormals, hasTangents, hasUV, hasUV2, hasUV3, hasUV4, hasColors32));
                var indexFormat = (vertexCount <= 65535) ? IndexFormat.UInt16 : IndexFormat.UInt32;
                md.SetIndexBufferParams(totalIndexCount, indexFormat); md.subMeshCount = subMeshCount; sw.Stop(); Ticks_AllocateMeshData += sw.ElapsedTicks;
                var vPos = md.GetVertexData<Vector3>(0);
                NativeArray<NormTan> vNT = default; NativeArray<ColUV01> vC01 = default; NativeArray<UV23> vUV23 = default;
                int stream = 1; if (hasNormals || hasTangents) vNT = md.GetVertexData<NormTan>(stream++); if (hasColors32 || hasUV || hasUV2) vC01 = md.GetVertexData<ColUV01>(stream++); if (hasUV3 || hasUV4) vUV23 = md.GetVertexData<UV23>(stream++);
                NativeArray<int> ibInt = default; NativeArray<ushort> ibU16 = default; if (indexFormat == IndexFormat.UInt16) ibU16 = md.GetIndexData<ushort>(); else ibInt = md.GetIndexData<int>();
                sw.Restart(); int boneCount = 0; var mergedUmaTransforms = new UMATransform[transformHierarchyCount]; for (int i = 0; i < sources.Length; i++) MergeSortedTransforms(mergedUmaTransforms, ref boneCount, sources[i].meshData.umaBones, sources[i].slotData.asset.slotName); sw.Stop(); Ticks_MergeTransforms += sw.ElapsedTicks;
                sw.Restart(); if (umaData?.skeleton != null) { umaData.skeleton.BeginSkeletonUpdate(); for (int i = 0; i < boneCount; i++) umaData.skeleton.EnsureBone(mergedUmaTransforms[i]); umaData.skeleton.EnsureBoneHierarchy(); umaData.skeleton.EndSkeletonUpdate(); } sw.Stop(); Ticks_EnsureSkeleton += sw.ElapsedTicks;
                var bonesCollection = new Dictionary<int, BoneIndexEntry>(Math.Max(64, bindPoseCount)); var bindPoses = new List<Matrix4x4>(bindPoseCount); var bonesList = new List<int>(transformHierarchyCount);
                var nativeBoneWeights = new NativeArray<BoneWeight1>(boneWeightCount, Allocator.TempJob); var nativeBonesPerVertex = new NativeArray<byte>(Math.Max(1, vertexCount), Allocator.TempJob);
                int vertexOffset = 0; int boneWeightOffset = 0; var boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity); var boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                subWrite = ArrayPool<int>.Shared.Rent(subMeshCount); sourceVertexOffsets = ArrayPool<int>.Shared.Rent(sources.Length);
                var indexJobs = new List<JobHandle>(Math.Max(32, subMeshCount * sources.Length)); NativeArray<int> bwRemap = default; if (UseParallelBoneWeights) bwRemap = new NativeArray<int>(boneWeightCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for (int s = 0; s < sources.Length; s++)
                {
                    var ci = sources[s]; var src = ci.meshData; int srcCount = src.vertexCount; sourceVertexOffsets[s] = vertexOffset;
                    sw.Restart();
                    if (UseParallelBoneWeights)
                    {
                        var bones = src.boneNameHashes; var bindPosesSrc = src.bindPoses; var pool = ArrayPool<int>.Shared; var map = pool.Rent(bones.Length);
                        try
                        {
                            for (int iMap = 0; iMap < bones.Length; iMap++) map[iMap] = TranslateBoneIndex(iMap, bones, bindPosesSrc, bonesCollection, bindPoses, bonesList);
                            NativeArray<byte>.Copy(src.ManagedBonesPerVertex, 0, nativeBonesPerVertex, vertexOffset, src.ManagedBonesPerVertex.Length);
                            NativeArray<BoneWeight1>.Copy(src.ManagedBoneWeights, 0, nativeBoneWeights, boneWeightOffset, src.ManagedBoneWeights.Length);
                            var srcWeights = src.ManagedBoneWeights; for (int w = 0; w < srcWeights.Length; w++) bwRemap[boneWeightOffset + w] = map[srcWeights[w].boneIndex];
                        }
                        finally { pool.Return(map, false); }
                    }
                    else
                    {
                        BuildBoneWeights(src, nativeBoneWeights, nativeBonesPerVertex, vertexOffset, boneWeightOffset, bonesCollection, bindPoses, bonesList);
                    }
                    sw.Stop(); Ticks_BuildBoneWeights += sw.ElapsedTicks;
#if UMA_UNSAFE
                    float expand = (ci.slotData != null && ci.slotData.expandAlongNormal > 0) ? ci.slotData.expandAlongNormal / 1000000f : 0f;
                    FastCopyPositionsAndBoundsUnsafe(vPos, vertexOffset, src.vertices, src.normals, srcCount, expand, ref boundsMin, ref boundsMax);
#else
                    if (ci.slotData != null && ci.slotData.expandAlongNormal > 0 && src.normals != null && src.normals.Length == srcCount)
                    { float expand = ci.slotData.expandAlongNormal / 1000000f; for (int i = 0; i < srcCount; i++) vPos[vertexOffset + i] = src.vertices[i] + (src.normals[i] * expand); }
                    else { NativeArray<Vector3>.Copy(src.vertices, 0, vPos, vertexOffset, srcCount); }
                    for (int i = 0; i < srcCount; i++) { var v = vPos[vertexOffset + i]; if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x; if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y; if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z; }
#endif
                    if (hasNormals || hasTangents)
                    {
#if UMA_UNSAFE
                        PackNormTanUnsafe(vNT, vertexOffset, src.normals, src.tangents, srcCount, hasNormals, hasTangents);
#else
                        for (int i = 0; i < srcCount; i++) { var nt = default(NormTan); nt.normal = (hasNormals && src.normals != null && src.normals.Length == srcCount) ? src.normals[i] : Vector3.zero; nt.tangent = (hasTangents && src.tangents != null && src.tangents.Length == srcCount) ? src.tangents[i] : new Vector4(0, 0, 0, 1); vNT[vertexOffset + i] = nt; }
#endif
                    }
                    if (hasColors32 || hasUV || hasUV2)
                    {
#if UMA_UNSAFE
                        PackColUV01Unsafe(vC01, vertexOffset, src.colors32, src.uv, src.uv2, srcCount, hasColors32, hasUV, hasUV2);
#else
                        for (int i = 0; i < srcCount; i++) { var c01 = default(ColUV01); c01.color = (hasColors32 && src.colors32 != null && src.colors32.Length == srcCount) ? src.colors32[i] : (Color32)Color.white; c01.uv0 = (hasUV && src.uv != null && src.uv.Length >= srcCount) ? src.uv[i] : Vector2.zero; c01.uv1 = (hasUV2 && src.uv2 != null && src.uv2.Length >= srcCount) ? src.uv2[i] : Vector2.zero; vC01[vertexOffset + i] = c01; }
#endif
                    }
                    if (hasUV3 || hasUV4)
                    {
#if UMA_UNSAFE
                        PackUV23Unsafe(vUV23, vertexOffset, src.uv3, src.uv4, srcCount, hasUV3, hasUV4);
#else
                        for (int i = 0; i < srcCount; i++) { var uv23 = default(UV23); uv23.uv2 = (hasUV3 && src.uv3 != null && src.uv3.Length >= srcCount) ? src.uv3[i] : Vector2.zero; uv23.uv3 = (hasUV4 && src.uv4 != null && src.uv4.Length >= srcCount) ? src.uv4[i] : Vector2.zero; vUV23[vertexOffset + i] = uv23; }
#endif
                    }
                    ci.slotData.vertexOffset = vertexOffset; ci.slotData.skinnedMeshRenderer = batch.CurrentRendererIndex;
                    vertexOffset += srcCount; boneWeightOffset += src.ManagedBoneWeights.Length;
                }
                if (bakedBlendshapes != null && bakedBlendshapes.Count > 0)
                {
                    for (int s = 0; s < sources.Length; s++)
                    {
                        var src = sources[s].meshData; int vo = sourceVertexOffsets[s]; var shapes = SkinnedMeshCombiner.GetBlendshapeSources(src, umaData.umaRecipe); if (shapes == null) continue;
                        foreach (var shape in shapes)
                        {
                            if (!bakedBlendshapes.TryGetValue(shape.shapeName, out float w) || Mathf.Approximately(w, 0f)) continue;
                            BakeShapeIntoBuffers(shape, w, vPos, vNT, vo, hasNormals, hasTangents);
                        }
                    }
                }
                Array.Clear(subWrite, 0, subMeshCount); sw.Restart();
                for (int s = 0; s < sources.Length; s++)
                {
                    var ci = sources[s]; var src = ci.meshData;
                    for (int sm = 0; sm < src.subMeshCount; sm++)
                    {
                        int dstSub = ci.targetSubmeshIndices[sm]; if (dstSub < 0) continue;

                        SubMeshTriangles smt = src.submeshes[sm];
                        var srcTris = smt.GetTriangles();
                        int triLen = srcTris.Length;
                        int dstStart = subIndexStart[dstSub] + subWrite[dstSub];
                        var triCopy = new NativeArray<int>(triLen, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                        NativeArray<int>.Copy(srcTris, triCopy, triLen);

                        bool hasMask = (ci.triangleMask != null && sm < ci.triangleMask.Length && ci.triangleMask[sm] != null && ci.triangleMask[sm].Length > 0);
                        if (!hasMask)
                        {
                            if (indexFormat == IndexFormat.UInt16) indexJobs.Add(new CopyIndicesJobU16 { Src = triCopy, Dst = ibU16, DstStart = dstStart, Add = (ushort)ci.slotData.vertexOffset }.Schedule());
                            else indexJobs.Add(new CopyIndicesJobInt { Src = triCopy, Dst = ibInt, DstStart = dstStart, Add = ci.slotData.vertexOffset }.Schedule());
                            subWrite[dstSub] += triLen;
                        }
                        else
                        {
                            // mask length in triangles, true=remove
                            int triCount = triLen / 3;
                            int removedTris = UMAUtils.GetCardinality(ci.triangleMask[sm]);
                            int kept = Mathf.Max(0, triCount - removedTris) * 3;
                            var maskNative = BitArrayToNative(ci.triangleMask[sm], Allocator.TempJob);
                            if (indexFormat == IndexFormat.UInt16) indexJobs.Add(new MaskedCopyIndicesJobU16 { Src = triCopy, Mask = maskNative, Dst = ibU16, DstStart = dstStart, Add = (ushort)ci.slotData.vertexOffset }.Schedule());
                            else indexJobs.Add(new MaskedCopyIndicesJobInt { Src = triCopy, Mask = maskNative, Dst = ibInt, DstStart = dstStart, Add = ci.slotData.vertexOffset }.Schedule());
                            subWrite[dstSub] += kept;
                        }
                    }
                }
                sw.Stop(); Ticks_IndexJobsSchedule += sw.ElapsedTicks;
                if (indexJobs.Count > 0) { sw.Restart(); var handles = new NativeArray<JobHandle>(indexJobs.Count, Allocator.Temp); for (int i = 0; i < indexJobs.Count; i++) handles[i] = indexJobs[i]; JobHandle.CompleteAll(handles); handles.Dispose(); indexJobs.Clear(); sw.Stop(); Ticks_IndexJobsComplete += sw.ElapsedTicks; }
                if (hasUV) { sw.Restart(); RecalculateUVForUMA(vC01, umaData, batch.AtlasResolution, batch.CurrentRendererIndex); sw.Stop(); Ticks_UVRemap += sw.ElapsedTicks; }
                // Use what we actually wrote (subWrite) to describe submeshes to avoid overruns
                sw.Restart(); for (int i = 0; i < subMeshCount; i++) { md.SetSubMesh(i, new SubMeshDescriptor { topology = MeshTopology.Triangles, indexStart = subIndexStart[i], indexCount = subWrite[i], baseVertex = 0, vertexCount = vertexCount }, MeshUpdateFlags.Default); } sw.Stop(); Ticks_SetSubmeshes += sw.ElapsedTicks;
                sw.Restart(); var mesh = batch.Renderer.sharedMesh ?? new Mesh(); mesh.indexFormat = indexFormat; Mesh.ApplyAndDisposeWritableMeshData(mda, new[] { mesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices); sw.Stop(); Ticks_ApplyMeshData += sw.ElapsedTicks;
                if (hasBlendShapes && blendShapeNames != null && blendShapeNames.Count > 0)
                {
                    try { AddBlendShapesDirect(mesh, sources, bakedBlendshapes, blendShapeNames, umaData.umaRecipe, sourceVertexOffsets, vertexCount); }
                    catch (Exception ex) { Debug.LogError($"[UMA] Error adding blendshapes: {ex.Message}\n{ex.StackTrace}"); }
                }
                if (UseParallelBoneWeights && bwRemap.IsCreated)
                {
                    var job = new RemapAllBoneWeightsJob { Weights = nativeBoneWeights, RemappedIndex = bwRemap }.Schedule(nativeBoneWeights.Length, 256); job.Complete(); bwRemap.Dispose();
                }
                sw.Restart(); mesh.bindposes = bindPoses.ToArray(); mesh.SetBoneWeights(nativeBonesPerVertex, nativeBoneWeights); sw.Stop(); Ticks_SetBindposesAndWeights += sw.ElapsedTicks;
                sw.Restart(); batch.Renderer.sharedMesh = mesh; if (string.IsNullOrEmpty(mesh.name)) mesh.name = "UMAMesh (MeshAPI)"; if (umaData?.skeleton != null) { batch.Renderer.bones = umaData.skeleton.HashesToTransforms(bonesList.ToArray()); if (batch.Renderer.rootBone == null) batch.Renderer.rootBone = umaData.GetGlobalTransform(); } sw.Stop(); Ticks_AssignBones += sw.ElapsedTicks;
                if (!float.IsInfinity(boundsMin.x) && !float.IsInfinity(boundsMax.x))
                {
                    Vector3 rawSize = boundsMax - boundsMin; rawSize.x = Mathf.Max(rawSize.x, 1e-5f); rawSize.y = Mathf.Max(rawSize.y, 1e-5f); rawSize.z = Mathf.Max(rawSize.z, 1e-5f);
                    Vector3 size = rawSize * (1f + Mathf.Max(0f, BoundsInflationFraction)); Vector3 center = (boundsMin + boundsMax) * 0.5f; var b = RotateBoundsAABB(new Bounds(center, size), boundsRotation); mesh.bounds = b; batch.Renderer.localBounds = b;
                }
                else { mesh.RecalculateBounds(); var b = RotateBoundsAABB(mesh.bounds, boundsRotation); mesh.bounds = b; batch.Renderer.localBounds = b; }
                clothCoeffs = hasCloth ? BuildClothCoefficients(sources) : null; nativeBonesPerVertex.Dispose(); nativeBoneWeights.Dispose(); totalSW.Stop(); Ticks_CombineInternalTotal += totalSW.ElapsedTicks;
            }
            finally
            {
                if (subMeshTriangleLength != null) ArrayPool<int>.Shared.Return(subMeshTriangleLength, false);
                if (subIndexStart != null) ArrayPool<int>.Shared.Return(subIndexStart, false);
                if (subWrite != null) ArrayPool<int>.Shared.Return(subWrite, false);
                if (sourceVertexOffsets != null) ArrayPool<int>.Shared.Return(sourceVertexOffsets, false);
            }
        }
#endif

        private static void AddBlendShapesDirect(
            Mesh mesh,
            SkinnedMeshCombiner.CombineInstance[] sources,
            Dictionary<string, float> baked,
            Dictionary<string, BlendShapeVertexData> meta,
            UMAData.UMARecipe recipe,
            int[] sourceVertexOffsets,
            int vertexCount)
        {
            if (meta == null || vertexCount <= 0 || mesh == null) return;

            foreach (var kv in meta)
            {
                string shapeName = kv.Key;
                var info = kv.Value;
                if (info == null || info.frameCount == 0) continue;

                for (int f = 0; f < info.frameCount; f++)
                {
                    var pool = ArrayPool<Vector3>.Shared;
                    Vector3[] dv;
                    // Always rent full-size vertex arrays for vertices.
#if UMA_UNSAFE
                    dv = pool.Rent(vertexCount);
                    Array.Clear(dv, 0, vertexCount);

                    // Only rent normal/tangent buffers if required. Keep original references so we only return pooled arrays.
                    Vector3[] dn = null;
                    Vector3[] dnPooled = null;
                    if (info.hasNormals)
                    {
                        dnPooled = pool.Rent(vertexCount);
                        dn = dnPooled;
                        Array.Clear(dn, 0, vertexCount);
                    }

                    Vector3[] dt = null;
                    Vector3[] dtPooled = null;
                    if (info.hasTangents)
                    {
                        dtPooled = pool.Rent(vertexCount);
                        dt = dtPooled;
                        Array.Clear(dt, 0, vertexCount);
                    }

#else
                    //==================
                    dv = new Vector3[vertexCount];

                    Vector3[] dn = null;
                    Vector3[] dt = null;

                    if (info.hasNormals)
                    {
                        dn = new Vector3[vertexCount];
                    }
                    if (info.hasTangents)
                    {
                        dt = new Vector3[vertexCount];
                    }                    
#endif

                    try
                    {
                        // Accumulate per source
                        for (int s = 0; s < sources.Length; s++)
                        {
                            var src = sources[s];
                            var srcShapes = SkinnedMeshCombiner.GetBlendshapeSources(src.meshData, recipe);
                            if (srcShapes == null || srcShapes.Count == 0) continue;

                            for (int i = 0; i < srcShapes.Count; i++)
                            {
                                var ubs = srcShapes[i];
                                if (ubs.shapeName != shapeName) continue;

                                int vo = sourceVertexOffsets[s];
                                int vc = src.meshData.vertexCount;

                                if (vo < 0 || vo >= vertexCount) continue;
                                if (vc <= 0) continue;
                                if (vo + vc > vertexCount) vc = vertexCount - vo;
                                if (vc <= 0) continue;

                                int frameIdx = Mathf.Clamp(f, 0, ubs.frames.Length - 1);
                                var fr = ubs.frames[frameIdx];
                                if (fr == null) continue;

                                // Copy vertices (required)
                                if (fr.deltaVertices != null && fr.deltaVertices.Length >= vc)
                                {
                                    Array.Copy(fr.deltaVertices, 0, dv, vo, vc);
                                }
                                else
                                {
#if UNITY_EDITOR
                                    Debug.LogWarning($"[UMA] BlendShape '{shapeName}' frame {frameIdx} source vertex delta size mismatch (have {fr.deltaVertices?.Length ?? 0} need {vc}). Skipping that source section.");
#endif
                                }

                                // Copy normals if requested & length matches
                                if (info.hasNormals && dn != null && fr.deltaNormals != null && fr.deltaNormals.Length >= vc)
                                {
                                    Array.Copy(fr.deltaNormals, 0, dn, vo, vc);
                                }

                                // Copy tangents if requested & length matches
                                if (info.hasTangents && dt != null && fr.deltaTangents != null && fr.deltaTangents.Length >= vc)
                                {
                                    Array.Copy(fr.deltaTangents, 0, dt, vo, vc);
                                }
                            }
                        }

                        float w = (info.frameWeights != null && f < info.frameWeights.Length) ? info.frameWeights[f] : 100f;

                        // IMPORTANT: Pass null (not empty arrays) for normals / tangents if not present.
                        // Unity requires (array == null) OR (array.Length == mesh.vertexCount).
                        if (!info.hasNormals) dn = null;
                        else if (dn != null && dn.Length != vertexCount) // Should not happen with our allocation, but guard just in case.
                        {
                            var fixedDn = new Vector3[vertexCount];
                            Array.Copy(dn, 0, fixedDn, 0, Math.Min(dn.Length, vertexCount));
                            dn = fixedDn; // do not return fixedDn (not pooled)
                        }

                        if (!info.hasTangents) dt = null;
                        else if (dt != null && dt.Length != vertexCount)
                        {
                            var fixedDt = new Vector3[vertexCount];
                            Array.Copy(dt, 0, fixedDt, 0, Math.Min(dt.Length, vertexCount));
                            dt = fixedDt;
                        }
#if !UMA_UNSAFE
                        // Final defensive validation
                        if (dv.Length != vertexCount)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning($"[UMA] Resizing blendshape vertex array for '{shapeName}' frame {f} (had {dv.Length}, need {vertexCount}).");
#endif
                            var resized = new Vector3[vertexCount];
                            Array.Copy(dv, resized, Math.Min(dv.Length, vertexCount));
                            dv = resized;
                        }
#endif
#if UMA_UNSAFE && UNITY_6000_0_OR_NEWER
                        ReadOnlySpan<Vector3> verts = new ReadOnlySpan<Vector3>(dv, 0, vertexCount);
                        ReadOnlySpan<Vector3> norms = null;
                        ReadOnlySpan<Vector3> tangs = null;
                        if (dn != null)
                        {
                            norms = new ReadOnlySpan<Vector3>(dn, 0, vertexCount);
                        }
                        if (dt != null)
                        {
                            tangs = new ReadOnlySpan<Vector3>(dt, 0, vertexCount);
                        }
                        mesh.AddBlendShapeFrame(shapeName, w, verts, norms, tangs);
#else
                        mesh.AddBlendShapeFrame(shapeName, w, dv, dn, dt);
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[UMA] Failed adding blendshape '{shapeName}' frame {f}: {ex.Message}");
                    }
                    finally
                    {
#if UMA_UNSAFE
                        // Return only the pooled arrays (original references)
                        pool.Return(dv, false);
                        if (dnPooled != null) pool.Return(dnPooled, false);
                        if (dtPooled != null) pool.Return(dtPooled, false);
#else
                        dn = null;
                        dt = null;
                        dv = null;
#endif
                    }
                }
            }
        }

        #region Jobs / Helpers
#if UMA_UNSAFE
        private static unsafe void FastCopyPositionsAndBoundsUnsafe(NativeArray<Vector3> dst, int dstStart, Vector3[] srcVertices, Vector3[] srcNormals, int count, float expandAlongNormal, ref Vector3 boundsMin, ref Vector3 boundsMax)
        {
            var dstPtr = (Vector3*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<Vector3>());
            if (expandAlongNormal > 0f && srcNormals != null && srcNormals.Length >= count)
            {
                fixed (Vector3* sV = srcVertices)
                fixed (Vector3* sN = srcNormals)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 v = sV[i] + sN[i] * expandAlongNormal; dstPtr[i] = v;
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
                    long bytes = (long)count * UnsafeUtility.SizeOf<Vector3>(); UnsafeUtility.MemCpy(dstPtr, sV, bytes);
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
        private static unsafe void PackNormTanUnsafe(NativeArray<NormTan> dst, int dstStart, Vector3[] normals, Vector4[] tangents, int count, bool hasNormals, bool hasTangents)
        {
            var dstPtr = (NormTan*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<NormTan>());
            bool nValid = hasNormals && normals != null && normals.Length >= count; bool tValid = hasTangents && tangents != null && tangents.Length >= count; Vector3 zeroN = default; Vector4 defT = new Vector4(0, 0, 0, 1);
            if (nValid && tValid)
            {
                fixed (Vector3* nP = normals) fixed (Vector4* tP = tangents) { for (int i = 0; i < count; i++) { dstPtr[i].normal = nP[i]; dstPtr[i].tangent = tP[i]; } }
            }
            else if (nValid)
            {
                fixed (Vector3* nP = normals) { for (int i = 0; i < count; i++) { dstPtr[i].normal = nP[i]; dstPtr[i].tangent = defT; } }
            }
            else if (tValid)
            {
                fixed (Vector4* tP = tangents) { for (int i = 0; i < count; i++) { dstPtr[i].normal = zeroN; dstPtr[i].tangent = tP[i]; } }
            }
            else { for (int i = 0; i < count; i++) { dstPtr[i].normal = zeroN; dstPtr[i].tangent = defT; } }
        }
        private static unsafe void PackColUV01Unsafe(NativeArray<ColUV01> dst, int dstStart, Color32[] colors, Vector2[] uv0, Vector2[] uv1, int count, bool hasColors32, bool hasUV0, bool hasUV1)
        {
            var dstPtr = (ColUV01*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<ColUV01>());
            bool cValid = hasColors32 && colors != null && colors.Length >= count; bool u0Valid = hasUV0 && uv0 != null && uv0.Length >= count; bool u1Valid = hasUV1 && uv1 != null && uv1.Length >= count; Color32 white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < count; i++)
            {
                dstPtr[i].color = cValid ? colors[i] : white;
                dstPtr[i].uv0 = u0Valid ? uv0[i] : default;
                dstPtr[i].uv1 = u1Valid ? uv1[i] : default;
            }
        }
        private static unsafe void PackUV23Unsafe(NativeArray<UV23> dst, int dstStart, Vector2[] uv2, Vector2[] uv3, int count, bool hasUV2, bool hasUV3)
        {
            var dstPtr = (UV23*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<UV23>());
            bool u2Valid = hasUV2 && uv2 != null && uv2.Length >= count; bool u3Valid = hasUV3 && uv3 != null && uv3.Length >= count; for (int i = 0; i < count; i++) { dstPtr[i].uv2 = u2Valid ? uv2[i] : default; dstPtr[i].uv3 = u3Valid ? uv3[i] : default; }
        }
        [BurstCompile] private struct CopyIndicesJobInt : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src; [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<int> Dst; public int DstStart; public int Add; public void Execute() { for (int i = 0; i < Src.Length; i++) Dst[DstStart + i] = Src[i] + Add; } }
        [BurstCompile] private struct CopyIndicesJobU16 : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src; [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<ushort> Dst; public int DstStart; public ushort Add; public void Execute() { for (int i = 0; i < Src.Length; i++) Dst[DstStart + i] = (ushort)(Src[i] + Add); } }
        [BurstCompile] private struct MaskedCopyIndicesJobInt : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src; [ReadOnly, DeallocateOnJobCompletion] public NativeArray<byte> Mask; [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<int> Dst; public int DstStart; public int Add; public void Execute() { int dst = DstStart; for (int t = 0; t < Mask.Length; t++) { if (Mask[t] != 0) continue; int i3 = t * 3; Dst[dst++] = Src[i3] + Add; Dst[dst++] = Src[i3 + 1] + Add; Dst[dst++] = Src[i3 + 2] + Add; } } }
        [BurstCompile] private struct MaskedCopyIndicesJobU16 : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src; [ReadOnly, DeallocateOnJobCompletion] public NativeArray<byte> Mask; [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<ushort> Dst; public int DstStart; public ushort Add; public void Execute() { int dst = DstStart; for (int t = 0; t < Mask.Length; t++) { if (Mask[t] != 0) continue; int i3 = t * 3; Dst[dst++] = (ushort)(Src[i3] + Add); Dst[dst++] = (ushort)(Src[i3 + 1] + Add); Dst[dst++] = (ushort)(Src[i3 + 2] + Add); } } }

#else
        [BurstCompile] private struct CopyIndicesJobInt : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src;  [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<int> Dst; public int DstStart; public int Add; public void Execute() { for (int i = 0; i < Src.Length; i++) Dst[DstStart + i] = Src[i] + Add; } }
        [BurstCompile] private struct CopyIndicesJobU16 : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src;  [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<ushort> Dst; public int DstStart; public ushort Add; public void Execute() { for (int i = 0; i < Src.Length; i++) Dst[DstStart + i] = (ushort)(Src[i] + Add); } }
        [BurstCompile] private struct MaskedCopyIndicesJobInt : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src; [ReadOnly, DeallocateOnJobCompletion] public NativeArray<byte> Mask; [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<int> Dst; public int DstStart; public int Add; public void Execute() { int dst = DstStart; for (int t = 0; t < Mask.Length; t++) { if (Mask[t] != 0) continue; int i3 = t * 3; Dst[dst++] = Src[i3] + Add; Dst[dst++] = Src[i3 + 1] + Add; Dst[dst++] = Src[i3 + 2] + Add; } } }
        [BurstCompile] private struct MaskedCopyIndicesJobU16 : IJob { [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Src; [ReadOnly, DeallocateOnJobCompletion] public NativeArray<byte> Mask; [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<ushort> Dst; public int DstStart; public ushort Add; public void Execute() { int dst = DstStart; for (int t = 0; t < Mask.Length; t++) { if (Mask[t] != 0) continue; int i3 = t * 3; Dst[dst++] = (ushort)(Src[i3] + Add); Dst[dst++] = (ushort)(Src[i3 + 1] + Add); Dst[dst++] = (ushort)(Src[i3 + 2] + Add); } } }

#endif
        private static NativeArray<byte> BitArrayToNative(BitArray ba, Allocator allocator) { var arr = new NativeArray<byte>(ba.Count, allocator, NativeArrayOptions.UninitializedMemory); for (int i = 0; i < ba.Count; i++) arr[i] = ba[i] ? (byte)1 : (byte)0; return arr; }
        #endregion

        #region UMA helpers
        [Flags] private enum MeshComponents { none = 0, has_normals = 1, has_tangents = 2, has_colors32 = 4, has_uv = 8, has_uv2 = 16, has_uv3 = 32, has_uv4 = 64, has_blendShapes = 128, has_clothSkinning = 256 }
        private class BlendShapeVertexData { public bool hasNormals; public bool hasTangents; public int frameCount; public float[] frameWeights; public int index; }
        private static void AnalyzeSources(SkinnedMeshCombiner.CombineInstance[] sources, int[] subMeshTriangleLength, ref int vertexCount, ref int boneWeightCount, ref int bindPoseCount, ref int transformHierarchyCount, ref MeshComponents meshComponents)
        {
            Array.Fill(subMeshTriangleLength, 0);
            for (int j = 0; j < sources.Length; j++)
            {
                var src = sources[j]; boneWeightCount += src.meshData.ManagedBoneWeights.Length; vertexCount += src.meshData.vertices.Length; bindPoseCount += src.meshData.bindPoses.Length; transformHierarchyCount += src.meshData.umaBones.Length;
                if (src.meshData.normals?.Length > 0) meshComponents |= MeshComponents.has_normals;
                if (src.meshData.tangents?.Length > 0) meshComponents |= MeshComponents.has_tangents;
                if (src.meshData.uv?.Length > 0) meshComponents |= MeshComponents.has_uv;
                if (src.meshData.uv2?.Length > 0) meshComponents |= MeshComponents.has_uv2;
                if (src.meshData.uv3?.Length > 0) meshComponents |= MeshComponents.has_uv3;
                if (src.meshData.uv4?.Length > 0) meshComponents |= MeshComponents.has_uv4;
                if (src.meshData.colors32?.Length > 0) meshComponents |= MeshComponents.has_colors32;
                if (src.meshData.clothSkinningSerialized?.Length > 0) meshComponents |= MeshComponents.has_clothSkinning;
                for (int i = 0; i < src.meshData.subMeshCount; i++)
                {
                    int indexLen = src.meshData.submeshes[i].GetTriangleCount();
                    int dest = src.targetSubmeshIndices[i]; if (dest < 0) continue;
                    // If there is a mask, its length is in triangles with true=remove. Compute kept index length accordingly.
                    if (src.triangleMask != null && i < src.triangleMask.Length && src.triangleMask[i] != null && src.triangleMask[i].Length > 0)
                    {
                        int triCount = indexLen / 3;
                        int removedTris = UMAUtils.GetCardinality(src.triangleMask[i]);
                        int keptTris = Mathf.Clamp(triCount - removedTris, 0, triCount);
                        subMeshTriangleLength[dest] += keptTris * 3;
                    }
                    else
                    {
                        subMeshTriangleLength[dest] += indexLen;
                    }
                }
            }
        }
        private static void AnalyzeBlendShapeSources(SkinnedMeshCombiner.CombineInstance[] sources, Dictionary<string, float> bakedBlendshapes, ref MeshComponents meshComponents, out Dictionary<string, BlendShapeVertexData> blendShapeNames, UMAData.UMARecipe recipe)
        {
            blendShapeNames = new Dictionary<string, BlendShapeVertexData>(); int bakedCount = 0;
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k]; var sourceShapes = SkinnedMeshCombiner.GetBlendshapeSources(src.meshData, recipe); if (sourceShapes.Count == 0) continue;
                for (int j = 0; j < sourceShapes.Count; j++)
                {
                    var ubs = sourceShapes[j]; string shapeName = ubs.shapeName; if (bakedBlendshapes.ContainsKey(shapeName)) { bakedCount++; continue; }
                    if (!blendShapeNames.TryGetValue(shapeName, out var meta)) { meta = new BlendShapeVertexData(); blendShapeNames.Add(shapeName, meta); }
                    meta.hasNormals |= ubs.frames[0].HasNormals(); meta.hasTangents |= ubs.frames[0].HasTangents();
                    if (ubs.frames.Length > meta.frameCount) { meta.frameCount = ubs.frames.Length; meta.frameWeights = new float[meta.frameCount]; for (int i = 0; i < meta.frameCount; i++) meta.frameWeights[i] = ubs.frames[i].frameWeight; }
                }
            }
            if (blendShapeNames.Count > 0 || bakedCount > 0) meshComponents |= MeshComponents.has_blendShapes;
        }
        private static void BuildBoneWeights(UMAMeshData data, NativeArray<BoneWeight1> dest, NativeArray<byte> destBonesPerVertex, int destIndex, int destBoneWeightIndex, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
        {
            var bones = data.boneNameHashes; var bindPoses = data.bindPoses; var pool = ArrayPool<int>.Shared; var boneMapping = pool.Rent(bones.Length);
            try
            {
                for (int i = 0; i < bones.Length; i++) boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
                NativeArray<byte>.Copy(data.ManagedBonesPerVertex, 0, destBonesPerVertex, destIndex, data.ManagedBonesPerVertex.Length);
                NativeArray<BoneWeight1>.Copy(data.ManagedBoneWeights, 0, dest, destBoneWeightIndex, data.ManagedBoneWeights.Length);
                for (int i = 0; i < data.ManagedBoneWeights.Length; i++) { var bw = dest[destBoneWeightIndex + i]; bw.boneIndex = boneMapping[bw.boneIndex]; dest[destBoneWeightIndex + i] = bw; }
            }
            finally { pool.Return(boneMapping, false); }
        }
        private static ClothSkinningCoefficient[] BuildClothCoefficients(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var clothDict = new Dictionary<Vector3, int>(1024); var result = new List<ClothSkinningCoefficient>(1024);
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k]; int count = src.meshData.vertexCount;
                if (src.meshData.clothSkinningSerialized != null && src.meshData.clothSkinningSerialized.Length > 0)
                {
                    var local = new Dictionary<Vector3, int>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var v = src.meshData.vertices[i]; if (local.ContainsKey(v)) continue; local.Add(v, local.Count);
                        if (!clothDict.TryGetValue(v, out var global)) { var coeff = new ClothSkinningCoefficient(); ConvertData(ref src.meshData.clothSkinningSerialized[local[v]], ref coeff); clothDict.Add(v, result.Count); result.Add(coeff); }
                        else { var coeff = result[clothDict[v]]; ConvertData(ref src.meshData.clothSkinningSerialized[local[v]], ref coeff); result[clothDict[v]] = coeff; }
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++) { var v = src.meshData.vertices[i]; if (!clothDict.ContainsKey(v)) { clothDict.Add(v, result.Count); result.Add(new ClothSkinningCoefficient { maxDistance = 0, collisionSphereDistance = float.MaxValue }); } }
                }
            }
            return result.Count > 0 ? result.ToArray() : null;
        }
        private class BoneIndexEntry { public int index; public List<int> indices; public int Count => index >= 0 ? 1 : indices.Count; public int this[int i] { get { if (index >= 0) { if (i == 0) return index; throw new ArgumentOutOfRangeException(); } return indices[i]; } } public void AddIndex(int idx) { if (index >= 0) { indices = new List<int>(8); indices.Add(index); index = -1; } indices.Add(idx); } }
        private static bool CompareSkinningMatrices(Matrix4x4 m1, ref Matrix4x4 m2) =>
            Mathf.Abs(m1.m00 - m2.m00) <= 0.0001f && Mathf.Abs(m1.m01 - m2.m01) <= 0.0001f && Mathf.Abs(m1.m02 - m2.m02) <= 0.0001f && Mathf.Abs(m1.m03 - m2.m03) <= 0.0001f &&
            Mathf.Abs(m1.m10 - m2.m10) <= 0.0001f && Mathf.Abs(m1.m11 - m2.m11) <= 0.0001f && Mathf.Abs(m1.m12 - m2.m12) <= 0.0001f && Mathf.Abs(m1.m13 - m2.m13) <= 0.0001f &&
            Mathf.Abs(m1.m20 - m2.m20) <= 0.0001f && Mathf.Abs(m1.m21 - m2.m21) <= 0.0001f && Mathf.Abs(m1.m22 - m2.m22) <= 0.0001f && Mathf.Abs(m1.m23 - m2.m23) <= 0.0001f;
        private static int TranslateBoneIndex(int index, int[] bonesHashes, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
        {
            int boneHash = bonesHashes[index]; if (bonesCollection.TryGetValue(boneHash, out var entry)) { for (int i = 0; i < entry.Count; i++) { int res = entry[i]; if (CompareSkinningMatrices(bindPosesList[res], ref bindPoses[index])) return res; } int idx = bindPosesList.Count; entry.AddIndex(idx); bindPosesList.Add(bindPoses[index]); bonesList.Add(boneHash); return idx; } else { int idx = bindPosesList.Count; bonesCollection.Add(boneHash, new BoneIndexEntry { index = idx }); bindPosesList.Add(bindPoses[index]); bonesList.Add(boneHash); return idx; }
        }
        [BurstCompile] private struct RemapAllBoneWeightsJob : IJobParallelFor { [NativeDisableParallelForRestriction] public NativeArray<BoneWeight1> Weights; [ReadOnly] public NativeArray<int> RemappedIndex; public void Execute(int i) { var bw = Weights[i]; bw.boneIndex = RemappedIndex[i]; Weights[i] = bw; } }
        private static VertexAttributeDescriptor[] BuildVertexLayout(bool hasNormals, bool hasTangents, bool hasUV, bool hasUV2, bool hasUV3, bool hasUV4, bool hasColors32)
        {
            var list = new List<VertexAttributeDescriptor>(8) { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0) }; int stream = 1;
            if (hasNormals || hasTangents) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream)); stream++; }
            if (hasColors32 || hasUV || hasUV2) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, stream)); stream++; }
            if (hasUV3 || hasUV4) { list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, stream)); }
            return list.ToArray();
        }
        private struct FragmentChoice { public SlotData slot; public Rect atlasRegion; public bool isRectShared; public bool slotUseAtlasOverlay; public List<OverlayData> overlayList; public Vector2 resolutionScale; public Vector2 cropResolution; public bool prefersCropping; }
        private static void RecalculateUVForUMA(NativeArray<ColUV01> vC01, UMAData umaData, int atlasResolution, int currentRendererIndex)
        {
            if (!vC01.IsCreated || vC01.Length == 0 || umaData?.generatedMaterials == null) return; var targetRendererAsset = umaData.GetRendererAsset(currentRendererIndex); var materials = umaData.generatedMaterials.materials; var processedSlots = new HashSet<SlotData>();
            for (int mi = 0; mi < materials.Count; mi++)
            {
                var gm = materials[mi]; if (gm == null || gm.rendererAsset != targetRendererAsset) continue; if (gm.umaMaterial == null || !gm.umaMaterial.IsGeneratedTextures) continue; var fragments = gm.materialFragments;
                for (int f = 0; f < fragments.Count; f++)
                {
                    var fragment = fragments[f]; var slot = fragment.slotData; if (slot?.asset?.meshData == null) continue; if (processedSlots.Contains(slot)) continue; int vertexCount = slot.asset.meshData.vertexCount; int start = slot.vertexOffset; if (start < 0 || start + vertexCount > vC01.Length) { vertexCount = Mathf.Clamp(vertexCount, 0, vC01.Length - Math.Max(0, start)); if (vertexCount <= 0) continue; }
                    // Declare atlas mapping variables first so cropping adjustments can modify them
                    var rect = fragment.atlasRegion;
                    float xMin = rect.xMin / atlasResolution; float xMax = rect.xMax / atlasResolution; float yMin = rect.yMin / atlasResolution; float yMax = rect.yMax / atlasResolution;
                    float xRange = xMax - xMin; float yRange = yMax - yMin;
                    if (fragment.isRectShared && slot.useAtlasOverlay)
                    {
                        OverlayData foundRect = null; for (int i = 0; i < fragment.overlayList.Count; i++) { var ov = fragment.overlayList[i]; if (slot.slotName != null && ov.overlayName != null && ov.overlayName.Contains(slot.slotName)) { foundRect = ov; break; } }
                        if (foundRect != null && foundRect.rect != Rect.zero)
                        {
                            var size = foundRect.rect.size * gm.resolutionScale;
                            var offX = foundRect.rect.x * gm.resolutionScale.x;
                            var offY = foundRect.rect.y * gm.resolutionScale.x;
                            xMin += offX / gm.cropResolution.x; xRange = size.x / gm.cropResolution.x;
                            yMin += offY / gm.cropResolution.y; yRange = size.y / gm.cropResolution.y;
                        }
                    }
                    for (int i = 0; i < vertexCount; i++) { int vi = start + i; var c = vC01[vi]; c.uv0.x = xMin + xRange * c.uv0.x; c.uv0.y = yMin + yRange * c.uv0.y; vC01[vi] = c; }
                    processedSlots.Add(slot);
                }
            }
        }
        public static void ConvertData(ref Vector2 source, ref ClothSkinningCoefficient dest) { dest.collisionSphereDistance = source.x; dest.maxDistance = source.y; }
        private static int FindTargetSubMeshCount(SkinnedMeshCombiner.CombineInstance[] sources) { int highest = -1; for (int i = 0; i < sources.Length; i++) { var s = sources[i]; for (int j = 0; j < s.targetSubmeshIndices.Length; j++) { int t = s.targetSubmeshIndices[j]; if (t > highest) highest = t; } } return highest + 1; }
        private static void MergeSortedTransforms(UMATransform[] mergedTransforms, ref int len1, UMATransform[] umaTransforms, string slotName)
        {
            int newBones = 0, pos1 = 0, pos2 = 0, len2 = umaTransforms.Length; while (pos1 < len1 && pos2 < len2) { long diff = (long)mergedTransforms[pos1].hash - umaTransforms[pos2].hash; if (diff == 0) { pos1++; pos2++; } else if (diff < 0) pos1++; else { pos2++; newBones++; } }
            newBones += len2 - pos2; pos1 = len1 - 1; pos2 = len2 - 1; len1 += newBones; int dest = len1 - 1;
            while (pos1 >= 0 && pos2 >= 0)
            {
                long diff = (long)mergedTransforms[pos1].hash - umaTransforms[pos2].hash;
                if (diff == 0) { mergedTransforms[dest--] = mergedTransforms[pos1--]; pos2--; }
                else if (diff > 0) mergedTransforms[dest--] = mergedTransforms[pos1--];
                else mergedTransforms[dest--] = umaTransforms[pos2--];
            }
            while (pos2 >= 0) { if (pos2 < umaTransforms.Length && dest >= 0 && dest < mergedTransforms.Length) mergedTransforms[dest--] = umaTransforms[pos2--]; else break; }
        }
        public static Bounds RotateBoundsAABBFixUp(Bounds b) => RotateBoundsAABB(b, FixupRotation);
        private static Bounds RotateBoundsAABB(Bounds b, Quaternion rot)
        {
            if (rot == Quaternion.identity) return b; Vector3 rc = rot * b.center; Vector3 e = b.extents; Matrix4x4 m = Matrix4x4.Rotate(rot); Vector3 newExtents = new Vector3(Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z, Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z, Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z); return new Bounds(rc, newExtents * 2f);
        }
        #endregion
    }
}
#if UMA_MESHAPI_2021
namespace UMA { partial class SkinnedMeshCombinerMeshAPI { } }
#endif