#if UNITY_2021_3_OR_NEWER
#define UMA_MESHAPI_2021
#endif
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

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
            /// <summary>
            /// Optional immutable renderer-asset selection used while a
            /// detached incremental renderer is prepared before UMAData's live
            /// renderer metadata is swapped.
            /// </summary>
            public UMARendererAsset RendererAsset;
            public bool HasRendererAssetOverride;
            /// <summary>
            /// Requests one union skeleton update for the complete renderer batch. This is used
            /// by UMAJobifiedMeshCombiner; compatibility overloads leave it false and retain
            /// per-renderer skeleton behavior.
            /// </summary>
            public bool SkipSkeletonUpdate;
        }
#if UMA_MESHAPI_2021
        public static bool UseParallelBoneWeights = true;
        public static bool UseParallelUVRemap = true;
        public static bool UseParallelRendererBatches = true;
        public static bool UseParallelMeshModifiers = true;
#if false // Set to true temporarily when tracing the MeshData job pipeline.
        /// <summary>
        /// Emits renderer/stage lifecycle messages for the MeshData job pipeline. Failures are
        /// always logged; this flag controls the successful progress and cleanup messages.
        /// </summary>
        public static bool EnableJobDiagnostics = true;
#endif
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
        public static long Ticks_BlendShapeFramePreparation;
        public static long Ticks_AddBlendShapeFrame;
        public static long BlendShapeFramesPrepared;
        public static long BlendShapeFramesApplied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void StaticInitializeOnLoad()
        {
            ResetTimings();
#if UMA_MESHAPI_2021
            UseParallelBoneWeights = true;
            UseParallelUVRemap = true;
            UseParallelRendererBatches = true;
            UseParallelMeshModifiers = true;
#if false // Keep diagnostics opt-in; see the matching block near EnableJobDiagnostics.
            EnableJobDiagnostics = true;
#endif
            FixupRotation = Quaternion.Euler(0f, 270f, 90f);
            BoundsInflationFraction = 0.01f;
#endif
        }

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
            Ticks_BlendShapeFramePreparation = 0;
            Ticks_AddBlendShapeFrame = 0;
            BlendShapeFramesPrepared = 0;
            BlendShapeFramesApplied = 0;
        }

#if UMA_MESHAPI_2021
#if false // Set to true temporarily when tracing the MeshData job pipeline.
        private const string JobDiagnosticPrefix = "[UMA.JobifiedMeshCombiner]";

        private static string DescribeBatch(RendererBatch batch)
        {
            string rendererName = batch.Renderer != null ? batch.Renderer.name : "<missing renderer>";
            int sourceCount = batch.Sources != null ? batch.Sources.Length : 0;
            return $"Renderer='{rendererName}', RendererIndex={batch.CurrentRendererIndex}, Sources={sourceCount}";
        }

        private static void LogJobDiagnostic(RendererBatch batch, string stage, string details = null)
        {
            if (!EnableJobDiagnostics) return;
            string suffix = string.IsNullOrEmpty(details) ? string.Empty : $" {details}";
            Debug.Log($"{JobDiagnosticPrefix} {stage}. {DescribeBatch(batch)}.{suffix}");
        }

        private static void LogJobFailure(RendererBatch batch, string stage, Exception exception)
        {
            Debug.LogError($"{JobDiagnosticPrefix} FAILED during {stage}. {DescribeBatch(batch)}. " +
                $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
        }

        private static void LogJobFailure(string stage, Exception exception)
        {
            Debug.LogError($"{JobDiagnosticPrefix} FAILED during {stage}. " +
                $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
        }
#else
        // Keep the instrumentation call sites intact, but compile all diagnostics out by default.
        private static void LogJobDiagnostic(RendererBatch batch, string stage, string details = null) { }
        private static void LogJobFailure(RendererBatch batch, string stage, Exception exception) { }
        private static void LogJobFailure(string stage, Exception exception) { }
#endif
#endif

        /// <summary>
        /// Returns true when a slot's modifier stack is entirely additive vertex deltas and can
        /// therefore be safely accumulated and applied in parallel. Custom or order-dependent
        /// adjustment types deliberately use the established managed modifier path.
        /// </summary>
        public static bool SupportsJobifiedMeshModifiers(SlotData slotData)
        {
            var modifiers = slotData?.meshModifiers;
            if (modifiers == null || modifiers.Count == 0) return false;

            bool hasAdjustment = false;
            int vertexCount = slotData.asset?.meshData?.vertexCount ?? -1;
            for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
            {
                var modifier = modifiers[modifierIndex];
                if (modifier == null || modifier.Scale == 0f) continue;
                var collection = modifier.adjustments;
                if (collection == null) continue;
                if (collection.GetType() != typeof(VertexDeltaAdjustmentCollection)) return false;
                var adjustments = collection.vertexAdjustments;
                if (adjustments == null) continue;
                for (int adjustmentIndex = 0; adjustmentIndex < adjustments.Count; adjustmentIndex++)
                {
                    if (adjustments[adjustmentIndex]?.GetType() != typeof(VertexDeltaAdjustment)) return false;
                    var adjustment = (VertexDeltaAdjustment)adjustments[adjustmentIndex];
                    if (vertexCount >= 0 && (uint)adjustment.vertexIndex >= (uint)vertexCount) return false;
                    hasAdjustment = true;
                }
            }
            return hasAdjustment;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct NormTan { public Vector3 normal; public Vector4 tangent; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct ColUV01 { public Color32 color; public Vector2 uv0; public Vector2 uv1; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct UV23 { public Vector2 uv2; public Vector2 uv3; }

#if UMA_MESHAPI_2021
        [BurstCompile]
        private struct ApplyUVTransformsJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<ColUV01> Vertices;
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
        internal struct UVTransform { public int start; public int count; public float xMin; public float yMin; public float xScale; public float yScale; }

        internal struct VertexDeltaRecord { public int vertexIndex; public Vector3 delta; }

        [BurstCompile]
        private struct ApplyVertexDeltasJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Vector3> Vertices;
            [ReadOnly] public NativeArray<VertexDeltaRecord> Deltas;

            public void Execute(int i)
            {
                var delta = Deltas[i];
                Vertices[delta.vertexIndex] += delta.delta;
            }
        }

        // NEW: Bake blendshape deltas directly into base buffers (for baked shapes)
        private static void BakeShapeIntoBuffers(UMABlendShape shape, float weightInput, NativeArray<Vector3> vPos, NativeArray<NormTan> vNT, int vertexOffset, int sourceVertexCount, bool hasNormals, bool hasTangents)
        {
            if (shape == null || shape.frames == null || shape.frames.Length == 0 || sourceVertexCount <= 0) return;
            if (vertexOffset < 0 || vertexOffset > vPos.Length - sourceVertexCount)
                throw new InvalidOperationException($"Blendshape '{shape.shapeName}' has an invalid output vertex range.");

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
            if (cur == null || prev == null) return;

            var dvCur = cur.deltaVertices;
            if (dvCur == null || dvCur.Length != sourceVertexCount)
                throw new InvalidOperationException($"Blendshape '{shape.shapeName}' frame {frameIndex} has {dvCur?.Length ?? 0} vertex deltas for a {sourceVertexCount}-vertex source.");

            var dvPrev = prev.deltaVertices;
            var dnCur = cur.deltaNormals;  var dnPrev = prev.deltaNormals;
            var dtCur = cur.deltaTangents; var dtPrev = prev.deltaTangents;

            int len = sourceVertexCount;
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

            for (int i = 0; i < batches.Length; i++)
            {
                if (batches[i].Renderer == null) throw new ArgumentNullException($"Renderer at {i}");
                if (batches[i].Sources == null || batches[i].Sources.Length == 0) throw new ArgumentException($"sources empty at {i}");
            }
            return CombineBatchInternal(batches, umaData, bakedBlendshapes ?? new Dictionary<string, float>(), markDynamic, markNotReadable, Quaternion.identity);
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

            for (int i = 0; i < batches.Length; i++)
            {
                if (batches[i].Renderer == null) throw new ArgumentNullException($"Renderer at {i}");
                if (batches[i].Sources == null || batches[i].Sources.Length == 0) throw new ArgumentException($"sources empty at {i}");
            }
            return CombineBatchInternal(batches, umaData, bakedBlendshapes ?? new Dictionary<string, float>(), markDynamic, markNotReadable, boundsRotation);
#endif
        }

#if UMA_MESHAPI_2021
        private static ClothSkinningCoefficient[][] CombineBatchInternal(
            RendererBatch[] batches,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic,
            bool markNotReadable,
            Quaternion boundsRotation)
        {
            ValidateBatchDestinations(batches);
            if (!UseParallelRendererBatches)
            {
                for (int i = 0; i < batches.Length; i++)
                    ValidateSources(batches[i].Sources);
                EnsureBatchSkeleton(batches, umaData);
                var sequentialResults = new ClothSkinningCoefficient[batches.Length][];
                for (int i = 0; i < batches.Length; i++)
                {
                    CombineInternal(batches[i], umaData, bakedBlendshapes, markDynamic, markNotReadable, boundsRotation, out sequentialResults[i]);
                }
                return sequentialResults;
            }

            var pending = new PendingCombine[batches.Length];
            var results = new ClothSkinningCoefficient[batches.Length][];
            JobHandle allJobs = default;
            bool anyJobs = false;
            try
            {
                // Preparing each renderer is a main-thread MeshData operation. Its native jobs
                // are deliberately not completed here, allowing work from every renderer to run
                // concurrently while the remaining batches are prepared.
                for (int i = 0; i < batches.Length; i++)
                {
                    LogJobDiagnostic(batches[i], "Preparing writable mesh and scheduling jobs");
                    pending[i] = PrepareCombine(
                        batches[i],
                        umaData,
                        bakedBlendshapes,
                        markDynamic,
                        markNotReadable,
                        boundsRotation,
                        Allocator.TempJob);
                    LogJobDiagnostic(batches[i], "Preparation completed",
                        $"JobsScheduled={pending[i].JobsScheduled}, Vertices={pending[i].VertexCount}, Submeshes={pending[i].SubMeshCount}.");
                    if (pending[i].JobsScheduled)
                    {
                        allJobs = JobHandle.CombineDependencies(allJobs, pending[i].Jobs);
                        anyJobs = true;
                    }
                }

                EnsureBatchSkeleton(batches, umaData);

                var sw = System.Diagnostics.Stopwatch.StartNew();
#if false // Set to true temporarily when tracing the MeshData job pipeline.
                if (EnableJobDiagnostics)
                    Debug.Log($"{JobDiagnosticPrefix} Completing combined job fence for {batches.Length} renderer batch(es). AnyJobs={anyJobs}.");
#endif
                if (anyJobs) allJobs.Complete();
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i].MarkJobsCompleted();
                    LogJobDiagnostic(batches[i], "All scheduled jobs completed");
                }
                sw.Stop(); Ticks_IndexJobsComplete += sw.ElapsedTicks;

                // Validate and finish all native output buffers before mutating any renderer
                // mesh. A bad later batch therefore cannot leave earlier renderers rebuilt.
                var outputMeshes = new Mesh[pending.Length];
                for (int i = 0; i < pending.Length; i++)
                {
                    LogJobDiagnostic(batches[i], "Validating completed native output");
                    outputMeshes[i] = pending[i].PrepareOutputMesh();
                }
                for (int i = 0; i < pending.Length; i++)
                {
                    LogJobDiagnostic(batches[i], "Applying writable MeshData");
                    results[i] = pending[i].ApplyPreparedMesh(outputMeshes[i]);
                    LogJobDiagnostic(batches[i], "Renderer mesh finalized");
                }
                return results;
            }
            catch (Exception exception)
            {
                LogJobFailure("renderer batch combine", exception);
                throw;
            }
            finally
            {
                for (int i = 0; i < pending.Length; i++)
                {
                    try { pending[i]?.Dispose(); }
                    catch (Exception disposeException) { LogJobFailure(batches[i], "batch cleanup", disposeException); }
                }
            }
        }

        private static void ValidateBatchDestinations(RendererBatch[] batches)
        {
            var renderers = new HashSet<SkinnedMeshRenderer>();
            var meshes = new HashSet<Mesh>();
            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                var renderer = batches[batchIndex].Renderer;
                if (!renderers.Add(renderer))
                    throw new InvalidOperationException($"Renderer '{renderer.name}' appears more than once in one mesh-combine batch.");

                var mesh = renderer.sharedMesh;
                if (mesh != null && !meshes.Add(mesh))
                    throw new InvalidOperationException($"Renderer '{renderer.name}' shares its destination mesh with another renderer in the same combine batch.");
            }
        }

        private static void EnsureBatchSkeleton(RendererBatch[] batches, UMAData umaData)
        {
            if (umaData?.skeleton == null) return;
            bool requested = false;
            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                if (batches[batchIndex].SkipSkeletonUpdate)
                {
                    requested = true;
                    break;
                }
            }
            if (!requested) return;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int transformCapacity = 0;
            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                if (!batches[batchIndex].SkipSkeletonUpdate) continue;
                var sources = batches[batchIndex].Sources;
                for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                    transformCapacity = checked(transformCapacity + sources[sourceIndex].meshData.umaBones.Length);
            }

            var transformPool = ArrayPool<UMATransform>.Shared;
            var mergedTransforms = transformPool.Rent(Math.Max(1, transformCapacity));
            bool updateStarted = false;
            try
            {
                int mergedCount = 0;
                var mergeStopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
                {
                    if (!batches[batchIndex].SkipSkeletonUpdate) continue;
                    var sources = batches[batchIndex].Sources;
                    for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                    {
                        var source = sources[sourceIndex];
                        MergeSortedTransforms(
                            mergedTransforms,
                            ref mergedCount,
                            source.meshData.umaBones,
                            source.slotData.asset.slotName);
                    }
                }
                mergeStopwatch.Stop();
                Ticks_MergeTransforms += mergeStopwatch.ElapsedTicks;

                umaData.skeleton.BeginSkeletonUpdate();
                updateStarted = true;
                for (int boneIndex = 0; boneIndex < mergedCount; boneIndex++)
                    umaData.skeleton.EnsureBone(mergedTransforms[boneIndex]);
                umaData.skeleton.EnsureBoneHierarchy();
            }
            finally
            {
                try
                {
                    if (updateStarted) umaData.skeleton.EndSkeletonUpdate();
                }
                finally
                {
                    transformPool.Return(mergedTransforms, true);
                    stopwatch.Stop();
                    Ticks_EnsureSkeleton += stopwatch.ElapsedTicks;
                }
            }
        }

        public sealed class PendingCombine : IDisposable
        {
            public RendererBatch Batch;
            public UMAData UmaData;
            public Dictionary<string, float> BakedBlendshapes;
            public Quaternion BoundsRotation;
            public Mesh.MeshDataArray MeshDataArray;
            public bool MarkDynamic;
            public bool MarkNotReadable;
            public bool HasBlendShapes;
            public bool HasCloth;
            internal Dictionary<string, BlendShapeVertexData> BlendShapeNames;
            public int[] SourceVertexOffsets;
            public int[] SubIndexStart;
            public int[] SubWrite;
            public int SubMeshCount;
            public int VertexCount;
            public List<Matrix4x4> BindPoses;
            public List<int> BonesList;
            public NativeArray<BoneWeight1> BoneWeights;
            public NativeArray<byte> BonesPerVertex;
            public NativeArray<int> BoneWeightRemap;
            internal NativeArray<UVTransform> UVTransforms;
            internal NativeArray<VertexDeltaRecord> VertexDeltas;
            internal NativeArray<BoundsResult> BoundsResult;
            public NativeArray<int> IndexValidation;
            public List<NativeArray<byte>> TriangleMasks;
            public NativeArray<Vector3> Positions;
            internal NativeArray<NormTan> NormalsTangents;
            internal NativeArray<ColUV01> ColorsUV;
            public int IndexValidationCount;
            public bool HasNormals;
            public bool HasTangents;
            public bool HasUV;
            public bool LoadAllBlendShapeFrames;
            public ClothSkinningCoefficient[] PreparedCloth;
            public Bounds PreparedBounds;
            public JobHandle Jobs;
            public bool JobsScheduled;
            public Allocator NativeAllocator;
            private bool jobsCompleted;
            private bool meshDataApplied;
            private bool rendererFinalized;
            private bool disposed;

            /// <summary>
            /// True when every scheduled native job has finished. Reading this
            /// property never completes or waits for a job.
            /// </summary>
            public bool IsCompleted => !JobsScheduled || jobsCompleted || Jobs.IsCompleted;

            public void CompleteJobs()
            {
                if (jobsCompleted) return;
                LogJobDiagnostic(Batch, "Waiting for renderer jobs", $"JobsScheduled={JobsScheduled}.");
                try
                {
                    if (JobsScheduled) Jobs.Complete();
                    LogJobDiagnostic(Batch, "Renderer jobs completed");
                }
                catch (Exception exception)
                {
                    LogJobFailure(Batch, "job completion", exception);
                    throw;
                }
                finally
                {
                    // A managed job exception is reported by Complete after the work has
                    // terminated. Do not attempt to complete the same handle again while
                    // unwinding; native resources can now be released deterministically.
                    jobsCompleted = true;
                }
            }

            public void MarkJobsCompleted()
            {
                jobsCompleted = true;
            }

            private void ValidateCompletedIndexJobs()
            {
                if (!IndexValidation.IsCreated) return;
                for (int i = 0; i < IndexValidationCount; i++)
                {
                    if (IndexValidation[i] != 0)
                    {
                        string rendererName = Batch.Renderer != null ? Batch.Renderer.name : "<missing renderer>";
                        throw new InvalidOperationException($"Cannot combine renderer '{rendererName}': a source submesh contains an index outside its source vertex range.");
                    }
                }
            }

            private void RecalculateModifiedSourceNormalsAndTangents()
            {
                if (!HasNormals || !NormalsTangents.IsCreated || !Positions.IsCreated) return;
                int lodLevel = UmaData != null ? Mathf.Max(0, UmaData.currentLODLevel) : 0;
                RecalculateModifiedSources(
                    Batch.Sources,
                    SourceVertexOffsets,
                    VertexDeltas,
                    Positions,
                    NormalsTangents,
                    ColorsUV,
                    // The interleaved normal stream deliberately carries a tangent attribute
                    // as well. Keep that physical output tangent orthogonal even when no source
                    // supplied authored tangents and it began as a generated fallback.
                    HasTangents || HasNormals,
                    HasUV,
                    lodLevel);
            }

            public Mesh PrepareOutputMesh()
            {
                CompleteJobs();
                ValidateCompletedIndexJobs();
                if (!BoundsResult.IsCreated || BoundsResult[0].IsValid == 0)
                    throw new InvalidOperationException("The combined mesh bounds job did not produce a result.");
                if (BoundsResult[0].IsValid == 2)
                    throw new InvalidOperationException("The combined mesh contains a non-finite vertex position.");
                RecalculateModifiedSourceNormalsAndTangents();
                if (HasCloth)
                    PreparedCloth = BuildClothCoefficients(Batch.Sources, Positions, SourceVertexOffsets);

                Vector3 rawSize = BoundsResult[0].Max - BoundsResult[0].Min;
                rawSize.x = Mathf.Max(rawSize.x, 1e-5f);
                rawSize.y = Mathf.Max(rawSize.y, 1e-5f);
                rawSize.z = Mathf.Max(rawSize.z, 1e-5f);
                Vector3 size = rawSize * (1f + Mathf.Max(0f, BoundsInflationFraction));
                Vector3 center = (BoundsResult[0].Min + BoundsResult[0].Max) * 0.5f;
                if (!IsFinite(size) || !IsFinite(center))
                    throw new InvalidOperationException("The combined mesh bounds overflowed while applying bounds inflation.");
                PreparedBounds = RotateBoundsAABB(new Bounds(center, size), BoundsRotation);
                if (!IsFinite(PreparedBounds.center) || !IsFinite(PreparedBounds.size))
                    throw new InvalidOperationException("The combined mesh bounds became non-finite after rotation.");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var meshData = MeshDataArray[0];
                var submeshBounds = new Bounds(
                    (BoundsResult[0].Min + BoundsResult[0].Max) * 0.5f,
                    BoundsResult[0].Max - BoundsResult[0].Min);
                for (int i = 0; i < SubMeshCount; i++)
                {
                    meshData.SetSubMesh(i, new SubMeshDescriptor
                    {
                        topology = MeshTopology.Triangles,
                        indexStart = SubIndexStart[i],
                        indexCount = SubWrite[i],
                        baseVertex = 0,
                        firstVertex = 0,
                        vertexCount = VertexCount,
                        // DontRecalculateBounds is intentional for performance. A conservative
                        // whole-mesh bound is valid for every submesh and avoids leaving the
                        // descriptor bounds at their zero-valued default.
                        bounds = submeshBounds
                    }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
                stopwatch.Stop(); Ticks_SetSubmeshes += stopwatch.ElapsedTicks;
                return Batch.Renderer.sharedMesh;
            }

            public ClothSkinningCoefficient[] FinalizeMesh()
            {
                return ApplyPreparedMesh(PrepareOutputMesh());
            }

            public ClothSkinningCoefficient[] ApplyPreparedMesh(Mesh mesh)
            {
                bool createdMesh = mesh == null;
                if (createdMesh) mesh = new Mesh();
                try
                {
                    if (MarkDynamic) mesh.MarkDynamic();
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        Mesh.ApplyAndDisposeWritableMeshData(MeshDataArray, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    }
                    finally
                    {
                        // The API contract consumes the MeshDataArray. This prevents a second
                        // disposal if Unity reports an apply error after consuming it.
                        meshDataApplied = true;
                    }
                    stopwatch.Stop(); Ticks_ApplyMeshData += stopwatch.ElapsedTicks;
                    return FinalizeAppliedMesh(mesh);
                }
                catch
                {
                    if (createdMesh && Batch.Renderer.sharedMesh != mesh)
                        UMAUtils.DestroySceneObject(mesh);
                    throw;
                }
            }

            /// <summary>
            /// Applies completed writable data to a detached mesh without
            /// assigning it to the destination renderer or loading blendshape
            /// frames. The caller must first observe <see cref="IsCompleted"/>.
            /// </summary>
            public ClothSkinningCoefficient[] ApplyPreparedBaseMesh(Mesh mesh)
            {
                if (!IsCompleted)
                {
                    throw new InvalidOperationException(
                        "Cannot apply an incremental mesh while its native jobs are still running.");
                }

                PrepareOutputMesh();
                bool createdMesh = mesh == null;
                if (createdMesh) mesh = new Mesh();
                try
                {
                    if (MarkDynamic) mesh.MarkDynamic();
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        Mesh.ApplyAndDisposeWritableMeshData(
                            MeshDataArray,
                            mesh,
                            MeshUpdateFlags.DontRecalculateBounds |
                            MeshUpdateFlags.DontValidateIndices);
                    }
                    finally
                    {
                        meshDataApplied = true;
                    }
                    stopwatch.Stop();
                    Ticks_ApplyMeshData += stopwatch.ElapsedTicks;
                    FinalizeBaseMesh(mesh);
                    return PreparedCloth;
                }
                catch
                {
                    if (createdMesh)
                        UMAUtils.DestroySceneObject(mesh);
                    throw;
                }
            }

            public ClothSkinningCoefficient[] FinalizeAppliedMesh(Mesh mesh)
            {
                FinalizeBaseMesh(mesh);
                return FinalizePreparedRenderer(mesh);
            }

            /// <summary>
            /// Finalizes a detached base mesh previously produced by
            /// <see cref="ApplyPreparedBaseMesh"/>. This remains a separate
            /// bounded unit so writable MeshData application and renderer
            /// finalization do not have to occur in one generator step.
            /// </summary>
            public ClothSkinningCoefficient[] FinalizePreparedRenderer(
                Mesh mesh)
            {
                return FinalizePreparedRendererCore(mesh, true);
            }

            /// <summary>
            /// Finalizes renderer bindings after an incremental loader has
            /// already added every blendshape frame to the detached mesh.
            /// </summary>
            public ClothSkinningCoefficient[]
                FinalizePreparedRendererWithoutBlendShapes(Mesh mesh)
            {
                return FinalizePreparedRendererCore(mesh, false);
            }

            public IncrementalBlendShapeLoader
                CreateIncrementalBlendShapeLoader()
            {
                return new IncrementalBlendShapeLoader(
                    Batch.Sources,
                    BlendShapeNames,
                    UmaData.umaRecipe,
                    SourceVertexOffsets,
                    VertexCount,
                    LoadAllBlendShapeFrames);
            }

            private ClothSkinningCoefficient[] FinalizePreparedRendererCore(
                Mesh mesh,
                bool addBlendShapes)
            {
                if (!meshDataApplied)
                {
                    throw new InvalidOperationException(
                        "The detached base mesh must be applied before renderer finalization.");
                }
                if (rendererFinalized)
                {
                    return PreparedCloth;
                }

                // MeshData does not replace blendshape data. Reused renderer meshes must be
                // explicitly reset or old shapes/frames survive into the newly combined mesh.
                if (addBlendShapes &&
                    HasBlendShapes &&
                    BlendShapeNames != null &&
                    BlendShapeNames.Count > 0)
                    AddBlendShapesDirect(mesh, Batch.Sources, BakedBlendshapes, BlendShapeNames, UmaData.umaRecipe, SourceVertexOffsets, VertexCount, LoadAllBlendShapeFrames);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                Batch.Renderer.sharedMesh = mesh;
                if (UmaData?.skeleton != null)
                {
                    Batch.Renderer.bones = UmaData.skeleton.HashesToTransforms(BonesList.ToArray());
                    if (Batch.Renderer.rootBone == null) Batch.Renderer.rootBone = UmaData.GetGlobalTransform();
                }
                else
                {
                    Batch.Renderer.bones = Array.Empty<Transform>();
                }
                stopwatch.Stop(); Ticks_AssignBones += stopwatch.ElapsedTicks;

                Batch.Renderer.localBounds = PreparedBounds;

                if (MarkNotReadable) mesh.UploadMeshData(true);
                rendererFinalized = true;
                return PreparedCloth;
            }

            private void FinalizeBaseMesh(Mesh mesh)
            {
                mesh.ClearBlendShapes();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                mesh.bindposes = BindPoses.ToArray();
                mesh.SetBoneWeights(BonesPerVertex, BoneWeights);
                stopwatch.Stop();
                Ticks_SetBindposesAndWeights += stopwatch.ElapsedTicks;
                if (string.IsNullOrEmpty(mesh.name))
                    mesh.name = "UMAMesh (MeshAPI)";
                mesh.bounds = PreparedBounds;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                LogJobDiagnostic(Batch, "Beginning native resource cleanup",
                    $"JobsCompleted={jobsCompleted}, MeshDataApplied={meshDataApplied}.");
                Exception cleanupException = null;
                try { CompleteJobs(); }
                catch (Exception exception) { RecordCleanupException(ref cleanupException, exception); }

                try
                {
                    TryDisposeNativeArray(ref BoundsResult, ref cleanupException);
                    TryDisposeNativeArray(ref IndexValidation, ref cleanupException);
                    if (TriangleMasks != null)
                    {
                        for (int i = 0; i < TriangleMasks.Count; i++)
                        {
                            var mask = TriangleMasks[i];
                            TryDisposeNativeArray(ref mask, ref cleanupException);
                            TriangleMasks[i] = mask;
                        }
                        TriangleMasks.Clear();
                    }
                    TryDisposeNativeArray(ref UVTransforms, ref cleanupException);
                    TryDisposeNativeArray(ref VertexDeltas, ref cleanupException);
                    TryDisposeNativeArray(ref BoneWeightRemap, ref cleanupException);
                    TryDisposeNativeArray(ref BonesPerVertex, ref cleanupException);
                    TryDisposeNativeArray(ref BoneWeights, ref cleanupException);
                    if (!meshDataApplied)
                    {
                        try { MeshDataArray.Dispose(); }
                        catch (Exception exception) { RecordCleanupException(ref cleanupException, exception); }
                    }
                }
                finally
                {
                    if (SourceVertexOffsets != null) ArrayPool<int>.Shared.Return(SourceVertexOffsets, false);
                    if (SubIndexStart != null) ArrayPool<int>.Shared.Return(SubIndexStart, false);
                    if (SubWrite != null) ArrayPool<int>.Shared.Return(SubWrite, false);
                    SourceVertexOffsets = null;
                    SubIndexStart = null;
                    SubWrite = null;
                }

                if (cleanupException != null)
                {
                    LogJobFailure(Batch, "native resource cleanup", cleanupException);
                    throw new InvalidOperationException("A mesh-combine job or native resource cleanup failed.", cleanupException);
                }
                LogJobDiagnostic(Batch, "Native resource cleanup completed");
            }
        }

        private static void RecordCleanupException(ref Exception existing, Exception next)
        {
            existing = existing == null ? next : new AggregateException(existing, next);
        }

        private static void TryDisposeNativeArray<T>(ref NativeArray<T> array, ref Exception cleanupException)
            where T : struct
        {
            if (!array.IsCreated) return;
            try { array.Dispose(); }
            catch (Exception exception) { RecordCleanupException(ref cleanupException, exception); }
            finally { array = default; }
        }

        private static void CombineInternal(
            RendererBatch batch,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic,
            bool markNotReadable,
            Quaternion boundsRotation,
            out ClothSkinningCoefficient[] clothCoeffs)
        {
            LogJobDiagnostic(batch, "Beginning renderer combine");
            try
            {
                using (var pending = PrepareCombine(
                    batch,
                    umaData,
                    bakedBlendshapes,
                    markDynamic,
                    markNotReadable,
                    boundsRotation,
                    Allocator.TempJob))
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    pending.CompleteJobs();
                    stopwatch.Stop(); Ticks_IndexJobsComplete += stopwatch.ElapsedTicks;
                    clothCoeffs = pending.FinalizeMesh();
                }
                LogJobDiagnostic(batch, "Renderer combine completed");
            }
            catch (Exception exception)
            {
                LogJobFailure(batch, "renderer combine", exception);
                throw;
            }
        }

        public static PendingCombine PrepareIncrementalCombine(
            RendererBatch batch,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic,
            bool markNotReadable,
            Quaternion boundsRotation)
        {
            return PrepareCombine(
                batch,
                umaData,
                bakedBlendshapes,
                markDynamic,
                markNotReadable,
                boundsRotation,
                Allocator.Persistent);
        }

        private static PendingCombine PrepareCombine(
            RendererBatch batch,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic,
            bool markNotReadable,
            Quaternion boundsRotation,
            Allocator nativeAllocator)
        {
            int[] subMeshTriangleLength = null;
            int[] subIndexStart = null;
            int[] subWrite = null;
            int[] sourceVertexOffsets = null;
            NativeArray<BoneWeight1> nativeBoneWeights = default;
            NativeArray<byte> nativeBonesPerVertex = default;
            NativeArray<int> bwRemap = default;
            NativeArray<UVTransform> uvTransforms = default;
            NativeArray<VertexDeltaRecord> vertexDeltas = default;
            NativeArray<BoundsResult> boundsResult = default;
            NativeArray<int> indexValidation = default;
            List<NativeArray<byte>> triangleMasks = null;
            JobHandle scheduledJobs = default;
            bool jobsScheduled = false;
            bool ownershipTransferred = false;
            Mesh.MeshDataArray writableMeshData = default;
            bool writableMeshDataAllocated = false;
            string preparationStage = "initialization";
            try
            {
                var totalSW = System.Diagnostics.Stopwatch.StartNew();
                var sources = batch.Sources;
                preparationStage = "source validation";
                if (umaData?.umaRecipe == null)
                    throw new InvalidOperationException("MeshData combine requires an initialized UMA recipe.");
                if (!IsFinite(boundsRotation) || !IsFinite(BoundsInflationFraction))
                    throw new InvalidOperationException("Mesh bounds rotation or inflation contains a non-finite value.");
                ValidateSources(sources);
                int vertexCount = 0, boneWeightCount = 0, bindPoseCount = 0, transformHierarchyCount = 0;
                int subMeshCount = FindTargetSubMeshCount(sources);
                if (subMeshCount <= 0)
                    throw new InvalidOperationException("The combine sources do not target any output submesh.");
                subMeshTriangleLength = ArrayPool<int>.Shared.Rent(subMeshCount);
                MeshComponents flags = MeshComponents.none;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                preparationStage = "source analysis";
                int lodLevel = 0;
                if (umaData != null)
                {
                    lodLevel = Mathf.Max(0, umaData.currentLODLevel);
                }
                AnalyzeSources(sources, subMeshTriangleLength, lodLevel, ref vertexCount, ref boneWeightCount, ref bindPoseCount, ref transformHierarchyCount, ref flags);
                sw.Stop(); Ticks_AnalyzeSources += sw.ElapsedTicks;
                Dictionary<string, BlendShapeVertexData> blendShapeNames;
                bool ignoreBlendShapes = umaData != null && umaData.blendShapeSettings != null && umaData.blendShapeSettings.ignoreBlendShapes;
                bool loadAllBlendShapeFrames = umaData?.blendShapeSettings == null || umaData.blendShapeSettings.loadAllFrames;
                bool loadBlendShapeNormals = umaData?.blendShapeSettings == null || umaData.blendShapeSettings.loadNormals;
                bool loadBlendShapeTangents = umaData?.blendShapeSettings == null || umaData.blendShapeSettings.loadTangents;
                sw.Restart();
                preparationStage = "blendshape analysis";
                if (!ignoreBlendShapes)
                {
                    AnalyzeBlendShapeSources(sources, bakedBlendshapes, loadAllBlendShapeFrames, loadBlendShapeNormals, loadBlendShapeTangents, ref flags, out blendShapeNames, umaData.umaRecipe);
                }
                else
                {
                    blendShapeNames = null;
                }
                sw.Stop(); Ticks_AnalyzeBlendshapes += sw.ElapsedTicks;
                bool hasNormals = (flags & MeshComponents.has_normals) != 0;
                bool hasTangents = (flags & MeshComponents.has_tangents) != 0;
                bool hasUV = (flags & MeshComponents.has_uv) != 0;
                bool hasUV2 = (flags & MeshComponents.has_uv2) != 0;
                bool hasUV3 = (flags & MeshComponents.has_uv3) != 0;
                bool hasUV4 = (flags & MeshComponents.has_uv4) != 0;
                bool hasColors32 = (flags & MeshComponents.has_colors32) != 0;
                bool hasBlendShapes = !ignoreBlendShapes && (flags & MeshComponents.has_blendShapes) != 0;
                bool hasCloth = (flags & MeshComponents.has_clothSkinning) != 0;
                subIndexStart = ArrayPool<int>.Shared.Rent(subMeshCount);
                int totalIndexCount = 0;
                for (int i = 0, run = 0; i < subMeshCount; i++)
                {
                    subIndexStart[i] = run;
                    run = checked(run + subMeshTriangleLength[i]);
                    totalIndexCount = run;
                }
                sw.Restart();
                preparationStage = "writable MeshData allocation";
                writableMeshData = Mesh.AllocateWritableMeshData(1);
                writableMeshDataAllocated = true;
                var md = writableMeshData[0];
                md.SetVertexBufferParams(vertexCount, BuildVertexLayout(hasNormals, hasTangents, hasUV, hasUV2, hasUV3, hasUV4, hasColors32));
                // UInt16 can only represent indices up to 65535. Use UInt32 if vertex count exceeds that.
                var indexFormat = (umaData != null && umaData.force32bit) || vertexCount > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
                md.SetIndexBufferParams(totalIndexCount, indexFormat); md.subMeshCount = subMeshCount; sw.Stop(); Ticks_AllocateMeshData += sw.ElapsedTicks;
                var vPos = md.GetVertexData<Vector3>(0);
                NativeArray<NormTan> vNT = default; NativeArray<ColUV01> vC01 = default; NativeArray<UV23> vUV23 = default;
                int stream = 1; if (hasNormals || hasTangents) vNT = md.GetVertexData<NormTan>(stream++); if (hasColors32 || hasUV || hasUV2) vC01 = md.GetVertexData<ColUV01>(stream++); if (hasUV3 || hasUV4) vUV23 = md.GetVertexData<UV23>(stream++);
                NativeArray<int> ibInt = default; NativeArray<ushort> ibU16 = default; if (indexFormat == IndexFormat.UInt16) ibU16 = md.GetIndexData<ushort>(); else ibInt = md.GetIndexData<int>();
                preparationStage = "skeleton and vertex stream preparation";
                if (!batch.SkipSkeletonUpdate && umaData?.skeleton != null)
                {
                    sw.Restart();
                    int boneCount = 0;
                    var transformPool = ArrayPool<UMATransform>.Shared;
                    var mergedUmaTransforms = transformPool.Rent(Math.Max(1, transformHierarchyCount));
                    try
                    {
                        for (int i = 0; i < sources.Length; i++)
                            MergeSortedTransforms(mergedUmaTransforms, ref boneCount, sources[i].meshData.umaBones, sources[i].slotData.asset.slotName);
                        sw.Stop(); Ticks_MergeTransforms += sw.ElapsedTicks;

                        sw.Restart();
                        bool updateStarted = false;
                        try
                        {
                            umaData.skeleton.BeginSkeletonUpdate();
                            updateStarted = true;
                            for (int i = 0; i < boneCount; i++) umaData.skeleton.EnsureBone(mergedUmaTransforms[i]);
                            umaData.skeleton.EnsureBoneHierarchy();
                        }
                        finally
                        {
                            if (updateStarted) umaData.skeleton.EndSkeletonUpdate();
                        }
                        sw.Stop(); Ticks_EnsureSkeleton += sw.ElapsedTicks;
                    }
                    finally
                    {
                        transformPool.Return(mergedUmaTransforms, true);
                    }
                }
                var bonesCollection = new Dictionary<int, BoneIndexEntry>(Math.Max(64, bindPoseCount)); var bindPoses = new List<Matrix4x4>(bindPoseCount); var bonesList = new List<int>(transformHierarchyCount);
                nativeBoneWeights = new NativeArray<BoneWeight1>(boneWeightCount, nativeAllocator); nativeBonesPerVertex = new NativeArray<byte>(Math.Max(1, vertexCount), nativeAllocator);
                int vertexOffset = 0; int boneWeightOffset = 0;
                subWrite = ArrayPool<int>.Shared.Rent(subMeshCount); sourceVertexOffsets = ArrayPool<int>.Shared.Rent(sources.Length);
                if (UseParallelBoneWeights) bwRemap = new NativeArray<int>(boneWeightCount, nativeAllocator, NativeArrayOptions.UninitializedMemory);
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
                    float expand = (ci.slotData != null && ci.slotData.expandAlongNormal != 0) ? ci.slotData.expandAlongNormal / 1000000f : 0f;
                    FastCopyPositionsUnsafe(vPos, vertexOffset, src.vertices, src.normals, srcCount, expand);
#else
                    if (ci.slotData != null && ci.slotData.expandAlongNormal != 0 && src.normals != null && src.normals.Length == srcCount)
                    { float expand = ci.slotData.expandAlongNormal / 1000000f; for (int i = 0; i < srcCount; i++) vPos[vertexOffset + i] = src.vertices[i] + (src.normals[i] * expand); }
                    else { NativeArray<Vector3>.Copy(src.vertices, 0, vPos, vertexOffset, srcCount); }
#endif
                    if (hasNormals || hasTangents)
                    {
#if UMA_UNSAFE
                        PackNormTanUnsafe(vNT, vertexOffset, src.normals, src.tangents, srcCount, hasNormals, hasTangents);
#else
                        for (int i = 0; i < srcCount; i++) { var nt = default(NormTan); nt.normal = (hasNormals && src.normals != null && src.normals.Length == srcCount) ? src.normals[i] : Vector3.zero; nt.tangent = (hasTangents && src.tangents != null && src.tangents.Length == srcCount) ? src.tangents[i] : BuildFallbackTangent(nt.normal, 1f); vNT[vertexOffset + i] = nt; }
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
                if (!ignoreBlendShapes && bakedBlendshapes != null && bakedBlendshapes.Count > 0)
                {
                    for (int s = 0; s < sources.Length; s++)
                    {
                        var src = sources[s].meshData; int vo = sourceVertexOffsets[s]; var shapes = SkinnedMeshCombiner.GetBlendshapeSources(src, umaData.umaRecipe); if (shapes == null) continue;
                        foreach (var shape in shapes)
                        {
                            if (!bakedBlendshapes.TryGetValue(shape.shapeName, out float w) || Mathf.Approximately(w, 0f)) continue;
                            BakeShapeIntoBuffers(shape, w, vPos, vNT, vo, src.vertexCount, hasNormals, hasTangents);
                        }
                    }
                }

                preparationStage = "bone, modifier, and bounds job scheduling";
                // Schedule all independent native work before the single completion point.
                if (UseParallelBoneWeights && bwRemap.IsCreated && nativeBoneWeights.Length > 0)
                {
                    var boneHandle = new RemapAllBoneWeightsJob
                    {
                        Weights = nativeBoneWeights,
                        RemappedIndex = bwRemap
                    }.Schedule(nativeBoneWeights.Length, 256);
                    scheduledJobs = JobHandle.CombineDependencies(scheduledJobs, boneHandle);
                    jobsScheduled = true;
                }

                JobHandle vertexDeltaHandle = default;
                vertexDeltas = BuildVertexDeltaRecordsWithAllocator(
                    sources,
                    sourceVertexOffsets,
                    nativeAllocator);
                if (vertexDeltas.IsCreated && vertexDeltas.Length > 0)
                {
                    if (UseParallelMeshModifiers)
                    {
                        vertexDeltaHandle = new ApplyVertexDeltasJob { Vertices = vPos, Deltas = vertexDeltas }.Schedule(vertexDeltas.Length, 128);
                        // Record this handle immediately, rather than relying only on its later
                        // transitive bounds dependency. If bounds scheduling fails, preparation
                        // cleanup must still wait before disposing the delta array or MeshData.
                        scheduledJobs = JobHandle.CombineDependencies(scheduledJobs, vertexDeltaHandle);
                        jobsScheduled = true;
                    }
                    else
                    {
                        for (int deltaIndex = 0; deltaIndex < vertexDeltas.Length; deltaIndex++)
                        {
                            var delta = vertexDeltas[deltaIndex];
                            vPos[delta.vertexIndex] += delta.delta;
                        }
                    }
                }

                boundsResult = new NativeArray<BoundsResult>(1, nativeAllocator, NativeArrayOptions.UninitializedMemory);
                var boundsHandle = new CalculateBoundsJob { Vertices = vPos, Result = boundsResult }.Schedule(vertexDeltaHandle);
                scheduledJobs = JobHandle.CombineDependencies(scheduledJobs, boundsHandle);
                jobsScheduled = true;

                preparationStage = "index job scheduling";
                // Each source/submesh writes to a precomputed, exclusive range in the output
                // index buffer. Source triangle arrays remain owned by UMAMeshData.
                int indexJobCapacity = 0;
                for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                    indexJobCapacity = checked(indexJobCapacity + sources[sourceIndex].meshData.subMeshCount);
                indexValidation = new NativeArray<int>(Math.Max(1, indexJobCapacity), nativeAllocator, NativeArrayOptions.ClearMemory);
                int indexValidationCount = 0;
                Array.Clear(subWrite, 0, subMeshCount);
                sw.Restart();
                for (int s = 0; s < sources.Length; s++)
                {
                    var ci = sources[s];
                    var src = ci.meshData;
                    // SlotData can be referenced by more than one combine instance. Use the
                    // immutable per-source offset captured during preparation, rather than the
                    // mutable editor/runtime convenience field on SlotData.
                    int add = sourceVertexOffsets[s];
                    int lod = 0;
                    if (umaData != null)
                    {
                        lod = Mathf.Max(0, umaData.currentLODLevel);
                    }

                    for (int sm = 0; sm < src.subMeshCount; sm++)
                    {
                        int dstSub = ci.targetSubmeshIndices[sm];
                        if (dstSub < 0) continue;

                        NativeArray<int> srcTris = GetTrianglesForLOD(src.submeshes[sm], lod);
                        if (!srcTris.IsCreated)
                            continue; // Empty submeshes are valid and contribute no indices.
                        int triLen = srcTris.Length;
                        if ((triLen % 3) != 0)
                            throw new InvalidOperationException($"Source {s}, submesh {sm} has {triLen} indices; triangle index counts must be divisible by three.");
                        int dstStart = subIndexStart[dstSub] + subWrite[dstSub];

                        bool hasMask = ShouldApplyTriangleMask(lod) &&
                            ci.triangleMask != null && sm < ci.triangleMask.Length &&
                            ci.triangleMask[sm] != null && ci.triangleMask[sm].Length > 0;
                        if (!hasMask)
                        {
                            int writeCount = triLen;
                            ValidateIndexDestinationRange(dstStart, writeCount, subIndexStart[dstSub], subIndexStart[dstSub] + subMeshTriangleLength[dstSub], s, sm);
                            JobHandle indexHandle;
                            if (indexFormat == IndexFormat.UInt16)
                            {
                                indexHandle = new CopyIndicesJobU16 { Src = srcTris, Dst = ibU16, DstStart = dstStart, Count = writeCount, Add = add, SourceVertexCount = src.vertexCount, Validation = indexValidation, ValidationIndex = indexValidationCount++ }.Schedule();
                            }
                            else
                            {
                                indexHandle = new CopyIndicesJobInt { Src = srcTris, Dst = ibInt, DstStart = dstStart, Count = writeCount, Add = add, SourceVertexCount = src.vertexCount, Validation = indexValidation, ValidationIndex = indexValidationCount++ }.Schedule();
                            }
                            scheduledJobs = JobHandle.CombineDependencies(scheduledJobs, indexHandle);
                            jobsScheduled = true;
                            subWrite[dstSub] += writeCount;
                        }
                        else
                        {
                            var mask = ci.triangleMask[sm];
                            int triCount = triLen / 3;
                            int removed = 0;
                            int maskedTriangles = Math.Min(mask.Length, triCount);
                            for (int t = 0; t < maskedTriangles; t++)
                                if (mask[t]) removed++;
                            int writeCount = (triCount - removed) * 3;
                            if (writeCount == 0) continue;
                            ValidateIndexDestinationRange(dstStart, writeCount, subIndexStart[dstSub], subIndexStart[dstSub] + subMeshTriangleLength[dstSub], s, sm);
                            var nativeMask = BitArrayToNative(mask, triCount, nativeAllocator);
                            try
                            {
                                if (triangleMasks == null) triangleMasks = new List<NativeArray<byte>>();
                                triangleMasks.Add(nativeMask);
                            }
                            catch
                            {
                                nativeMask.Dispose();
                                throw;
                            }
                            JobHandle indexHandle;
                            if (indexFormat == IndexFormat.UInt16)
                            {
                                indexHandle = new MaskedCopyIndicesJobU16 { Src = srcTris, Mask = nativeMask, Dst = ibU16, DstStart = dstStart, Add = add, SourceVertexCount = src.vertexCount, Validation = indexValidation, ValidationIndex = indexValidationCount++ }.Schedule();
                            }
                            else
                            {
                                indexHandle = new MaskedCopyIndicesJobInt { Src = srcTris, Mask = nativeMask, Dst = ibInt, DstStart = dstStart, Add = add, SourceVertexCount = src.vertexCount, Validation = indexValidation, ValidationIndex = indexValidationCount++ }.Schedule();
                            }
                            scheduledJobs = JobHandle.CombineDependencies(scheduledJobs, indexHandle);
                            jobsScheduled = true;
                            subWrite[dstSub] += writeCount;
                        }
                    }
                }
                sw.Stop();
                Ticks_IndexJobsSchedule += sw.ElapsedTicks;

                // Analysis and scheduling use the same exact NativeArray slices. Any mismatch
                // indicates source data changed during the combine and must not be hidden.
                for (int i = 0; i < subMeshCount; i++)
                {
                    if (subWrite[i] != subMeshTriangleLength[i])
                        throw new InvalidOperationException($"Output submesh {i} was allocated for {subMeshTriangleLength[i]} indices but prepared {subWrite[i]}.");
                }

                if (hasUV)
                {
                    preparationStage = "UV transform scheduling";
                    sw.Restart();
                    uvTransforms = BuildUVTransformsForUMA(
                        vC01,
                        umaData,
                        batch.AtlasResolution,
                        batch.CurrentRendererIndex,
                        sources,
                        sourceVertexOffsets,
                        batch.RendererAsset,
                        batch.HasRendererAssetOverride,
                        nativeAllocator);
                    if (uvTransforms.IsCreated && uvTransforms.Length > 0)
                    {
                        // Incremental operations use persistent native
                        // ownership specifically so they can yield instead of
                        // completing the bounds chain on this call. Always
                        // schedule their UV work, even for a small mesh.
                        bool persistentIncrementalWork =
                            nativeAllocator == Allocator.Persistent;
                        if (persistentIncrementalWork ||
                            (UseParallelUVRemap &&
                             vC01.Length >= UV_PARALLEL_MIN_VERTS))
                        {
                            // Unity aliases the AtomicSafetyHandle used by all streams returned
                            // from one writable MeshData. Although positions and UVs are physically
                            // separate streams, the UV writer must therefore depend on the bounds
                            // reader (which itself depends on the vertex-delta writer).
                            var uvHandle = new ApplyUVTransformsJob { Vertices = vC01, Transforms = uvTransforms }.Schedule(uvTransforms.Length, 1, boundsHandle);
                            scheduledJobs = JobHandle.CombineDependencies(scheduledJobs, uvHandle);
                            jobsScheduled = true;
                        }
                        else
                        {
                            // Main-thread access is governed by the same aliased safety handle.
                            // Complete the MeshData position chain before touching the UV stream.
                            boundsHandle.Complete();
                            ApplyUVTransforms(vC01, uvTransforms);
                        }
                    }
                    sw.Stop(); Ticks_UVRemap += sw.ElapsedTicks;
                }

                totalSW.Stop(); Ticks_CombineInternalTotal += totalSW.ElapsedTicks;
                preparationStage = "native resource ownership transfer";
                var pending = new PendingCombine
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
                    BoneWeightRemap = bwRemap,
                    UVTransforms = uvTransforms,
                    VertexDeltas = vertexDeltas,
                    BoundsResult = boundsResult,
                    IndexValidation = indexValidation,
                    TriangleMasks = triangleMasks,
                    IndexValidationCount = indexValidationCount,
                    Positions = vPos,
                    NormalsTangents = vNT,
                    ColorsUV = vC01,
                    HasNormals = hasNormals,
                    HasTangents = hasTangents,
                    HasUV = hasUV,
                    LoadAllBlendShapeFrames = loadAllBlendShapeFrames,
                    Jobs = scheduledJobs,
                    JobsScheduled = jobsScheduled,
                    NativeAllocator = nativeAllocator
                };

                sourceVertexOffsets = null;
                subIndexStart = null;
                subWrite = null;
                nativeBoneWeights = default;
                nativeBonesPerVertex = default;
                bwRemap = default;
                uvTransforms = default;
                vertexDeltas = default;
                boundsResult = default;
                indexValidation = default;
                triangleMasks = null;
                ownershipTransferred = true;
                writableMeshDataAllocated = false;
                LogJobDiagnostic(batch, "Native preparation completed",
                    $"Vertices={vertexCount}, Indices={totalIndexCount}, Submeshes={subMeshCount}, JobsScheduled={jobsScheduled}.");
                return pending;
            }
            catch (Exception exception)
            {
                LogJobFailure(batch, preparationStage, exception);
                throw;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    LogJobDiagnostic(batch, "Cleaning up failed preparation", $"LastStage='{preparationStage}', JobsScheduled={jobsScheduled}.");
                    Exception cleanupException = null;
                    if (jobsScheduled)
                    {
                        try { scheduledJobs.Complete(); }
                        catch (Exception jobException) { RecordCleanupException(ref cleanupException, jobException); }
                    }
                    TryDisposeNativeArray(ref boundsResult, ref cleanupException);
                    TryDisposeNativeArray(ref indexValidation, ref cleanupException);
                    if (triangleMasks != null)
                    {
                        for (int i = 0; i < triangleMasks.Count; i++)
                        {
                            var mask = triangleMasks[i];
                            TryDisposeNativeArray(ref mask, ref cleanupException);
                            triangleMasks[i] = mask;
                        }
                    }
                    TryDisposeNativeArray(ref uvTransforms, ref cleanupException);
                    TryDisposeNativeArray(ref vertexDeltas, ref cleanupException);
                    TryDisposeNativeArray(ref bwRemap, ref cleanupException);
                    TryDisposeNativeArray(ref nativeBonesPerVertex, ref cleanupException);
                    TryDisposeNativeArray(ref nativeBoneWeights, ref cleanupException);
                    if (writableMeshDataAllocated)
                    {
                        try { writableMeshData.Dispose(); }
                        catch (Exception exception) { RecordCleanupException(ref cleanupException, exception); }
                    }
                    if (cleanupException != null) LogJobFailure(batch, "failed-preparation cleanup", cleanupException);
                    else LogJobDiagnostic(batch, "Failed preparation cleanup completed", $"LastStage='{preparationStage}'.");
                }
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
            int vertexCount,
            bool loadAllFrames)
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
                    Vector3[] dv = null;
                    Vector3[] dn = null;
                    Vector3[] dt = null;
#if UMA_UNSAFE && UNITY_6000_0_OR_NEWER
                    Vector3[] dnPooled = null;
                    Vector3[] dtPooled = null;
#endif
                    // ArrayPool may return a larger array. Pooling is therefore only safe with
                    // Unity 6's span overload, where we can pass the exact vertex-count slice.
                    try
                    {
#if UMA_UNSAFE && UNITY_6000_0_OR_NEWER
                        dv = pool.Rent(vertexCount);
                        Array.Clear(dv, 0, vertexCount);

                        // Only rent normal/tangent buffers if required. Keep original references so we only return pooled arrays.
                        if (info.hasNormals)
                        {
                            dnPooled = pool.Rent(vertexCount);
                            dn = dnPooled;
                            Array.Clear(dn, 0, vertexCount);
                        }

                        if (info.hasTangents)
                        {
                            dtPooled = pool.Rent(vertexCount);
                            dt = dtPooled;
                            Array.Clear(dt, 0, vertexCount);
                        }

#else
                        dv = new Vector3[vertexCount];

                        if (info.hasNormals)
                            dn = new Vector3[vertexCount];
                        if (info.hasTangents)
                            dt = new Vector3[vertexCount];
#endif
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

                                if (vo < 0 || vc <= 0 || vo > vertexCount - vc)
                                    throw new InvalidOperationException($"Blendshape '{shapeName}' source {s} has an invalid output vertex range.");

                                if (ubs?.frames == null || (loadAllFrames && f >= ubs.frames.Length))
                                    throw new InvalidOperationException($"Blendshape '{shapeName}' source {s} is missing output frame {f}.");
                                int frameIdx = loadAllFrames ? f : ubs.frames.Length - 1;
                                var fr = ubs.frames[frameIdx];
                                if (fr == null)
                                    throw new InvalidOperationException($"Blendshape '{shapeName}' source {s} has a null frame at index {frameIdx}.");

                                // Copy vertices (required)
                                if (fr.deltaVertices != null && fr.deltaVertices.Length == vc)
                                {
                                    Array.Copy(fr.deltaVertices, 0, dv, vo, vc);
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Blendshape '{shapeName}' frame {frameIdx} source vertex delta size mismatch (have {fr.deltaVertices?.Length ?? 0}, need {vc}).");
                                }

                                // Copy normals if requested & length matches
                                if (info.hasNormals && dn != null && fr.deltaNormals != null && fr.deltaNormals.Length == vc)
                                {
                                    Array.Copy(fr.deltaNormals, 0, dn, vo, vc);
                                }

                                // Copy tangents if requested & length matches
                                if (info.hasTangents && dt != null && fr.deltaTangents != null && fr.deltaTangents.Length == vc)
                                {
                                    Array.Copy(fr.deltaTangents, 0, dt, vo, vc);
                                }
                            }
                        }

                        float w = (info.frameWeights != null && f < info.frameWeights.Length) ? info.frameWeights[f] : 100f;

                        // IMPORTANT: Pass null (not empty arrays) for normals / tangents if not present.
                        // Unity requires (array == null) OR (array.Length == mesh.vertexCount).
                        if (!info.hasNormals) dn = null;
                        if (!info.hasTangents) dt = null;
#if UMA_UNSAFE && UNITY_6000_0_OR_NEWER
                        ReadOnlySpan<Vector3> verts = new ReadOnlySpan<Vector3>(dv, 0, vertexCount);
                        ReadOnlySpan<Vector3> norms = default;
                        ReadOnlySpan<Vector3> tangs = default;
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
                        throw new InvalidOperationException($"Failed adding blendshape '{shapeName}' frame {f}.", ex);
                    }
                    finally
                    {
#if UMA_UNSAFE && UNITY_6000_0_OR_NEWER
                        // Return only the pooled arrays (original references)
                        if (dv != null) pool.Return(dv, false);
                        if (dnPooled != null) pool.Return(dnPooled, false);
                        if (dtPooled != null) pool.Return(dtPooled, false);
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Retains one reusable set of full-output delta buffers and prepares
        /// one blendshape frame at a time on a worker thread. Unity's
        /// AddBlendShapeFrame call remains a main-thread atomic unit.
        /// </summary>
        public sealed class IncrementalBlendShapeLoader : IDisposable
        {
            private static readonly ProfilerMarker PrepareFrameMarker =
                new ProfilerMarker(
                    "UMA.IncrementalMesh.BlendShape.PrepareFrame");
            private static readonly ProfilerMarker AddFrameMarker =
                new ProfilerMarker(
                    "UMA.IncrementalMesh.BlendShape.AddFrame");

            private sealed class ShapePlan
            {
                public string Name;
                public bool HasNormals;
                public bool HasTangents;
                public float[] FrameWeights;
                public SourcePlan[] Sources;
            }

            private sealed class SourcePlan
            {
                public UMABlendShape Shape;
                public int VertexOffset;
                public int VertexCount;
            }

            private readonly ShapePlan[] shapes;
            private readonly int vertexCount;
            private readonly bool loadAllFrames;
            private readonly Vector3[] deltaVertices;
            private readonly Vector3[] deltaNormals;
            private readonly Vector3[] deltaTangents;
            private readonly CancellationTokenSource cancellation =
                new CancellationTokenSource();
            private Task preparationTask;
            private int shapeIndex;
            private int frameIndex;
            private bool disposed;

            internal IncrementalBlendShapeLoader(
                SkinnedMeshCombiner.CombineInstance[] sources,
                Dictionary<string, BlendShapeVertexData> metadata,
                UMAData.UMARecipe recipe,
                int[] sourceVertexOffsets,
                int vertexCount,
                bool loadAllFrames)
            {
                this.vertexCount = vertexCount;
                this.loadAllFrames = loadAllFrames;
                shapes = BuildShapePlans(
                    sources,
                    metadata,
                    recipe,
                    sourceVertexOffsets);

                bool needsNormals = false;
                bool needsTangents = false;
                for (int i = 0; i < shapes.Length; i++)
                {
                    needsNormals |= shapes[i].HasNormals;
                    needsTangents |= shapes[i].HasTangents;
                }

                deltaVertices = vertexCount > 0
                    ? new Vector3[vertexCount]
                    : Array.Empty<Vector3>();
                deltaNormals = needsNormals
                    ? new Vector3[vertexCount]
                    : null;
                deltaTangents = needsTangents
                    ? new Vector3[vertexCount]
                    : null;

                if (shapes.Length > 0)
                {
                    ScheduleCurrentFrame();
                }
            }

            public bool IsComplete =>
                shapeIndex >= shapes.Length;

            public bool HasPendingPreparation =>
                preparationTask != null &&
                !preparationTask.IsCompleted;

            public int AppliedFrameCount { get; private set; }

            public int TotalFrameCount
            {
                get
                {
                    int total = 0;
                    for (int i = 0; i < shapes.Length; i++)
                    {
                        total += shapes[i].FrameWeights.Length;
                    }
                    return total;
                }
            }

            public string CurrentShapeName =>
                IsComplete ? string.Empty : shapes[shapeIndex].Name;

            public int CurrentFrameIndex => frameIndex;

            public UMAMeshCombineStepResult Step(Mesh mesh)
            {
                ThrowIfDisposed();
                if (IsComplete)
                {
                    return UMAMeshCombineStepResult.Completed();
                }
                if (mesh == null)
                {
                    throw new ArgumentNullException(nameof(mesh));
                }
                if (preparationTask == null)
                {
                    ScheduleCurrentFrame();
                }
                if (!preparationTask.IsCompleted)
                {
                    return UMAMeshCombineStepResult.WaitingForAsync();
                }

                CompletePreparation();
                ShapePlan shape = shapes[shapeIndex];
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using (AddFrameMarker.Auto())
                    {
                        mesh.AddBlendShapeFrame(
                            shape.Name,
                            shape.FrameWeights[frameIndex],
                            deltaVertices,
                            shape.HasNormals ? deltaNormals : null,
                            shape.HasTangents ? deltaTangents : null);
                    }
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed adding blendshape '{shape.Name}' frame {frameIndex}.",
                        exception);
                }
                finally
                {
                    stopwatch.Stop();
                    Interlocked.Add(
                        ref Ticks_AddBlendShapeFrame,
                        stopwatch.ElapsedTicks);
                }
                Interlocked.Increment(
                    ref BlendShapeFramesApplied);
                AppliedFrameCount++;

                AdvanceCursor();
                if (IsComplete)
                {
                    preparationTask = null;
                    return UMAMeshCombineStepResult.Completed();
                }

                ScheduleCurrentFrame();
                return UMAMeshCombineStepResult.InProgress();
            }

            /// <summary>
            /// Completes only the currently scheduled frame preparation. This
            /// is used by the inherited synchronous combiner API.
            /// </summary>
            public void CompletePreparation()
            {
                ThrowIfDisposed();
                Task task = preparationTask;
                if (task == null)
                {
                    return;
                }
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed preparing blendshape '{CurrentShapeName}' frame {frameIndex}.",
                        exception);
                }
            }

            private void ScheduleCurrentFrame()
            {
                ShapePlan shape = shapes[shapeIndex];
                int scheduledFrame = frameIndex;
                CancellationToken token = cancellation.Token;
                preparationTask = Task.Run(
                    () => PrepareFrame(
                        shape,
                        scheduledFrame,
                        token),
                    token);
            }

            private void PrepareFrame(
                ShapePlan shape,
                int outputFrameIndex,
                CancellationToken token)
            {
                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using (PrepareFrameMarker.Auto())
                    {
                        Array.Clear(
                            deltaVertices,
                            0,
                            deltaVertices.Length);
                        if (shape.HasNormals)
                        {
                            Array.Clear(
                                deltaNormals,
                                0,
                                deltaNormals.Length);
                        }
                        if (shape.HasTangents)
                        {
                            Array.Clear(
                                deltaTangents,
                                0,
                                deltaTangents.Length);
                        }

                        for (int sourceIndex = 0;
                             sourceIndex < shape.Sources.Length;
                             sourceIndex++)
                        {
                            token.ThrowIfCancellationRequested();
                            SourcePlan source = shape.Sources[sourceIndex];
                            int sourceFrameIndex = loadAllFrames
                                ? outputFrameIndex
                                : source.Shape.frames.Length - 1;
                            if ((uint)sourceFrameIndex >=
                                (uint)source.Shape.frames.Length)
                            {
                                throw new InvalidOperationException(
                                    $"Blendshape '{shape.Name}' source {sourceIndex} is missing frame {sourceFrameIndex}.");
                            }

                            UMABlendFrame frame =
                                source.Shape.frames[sourceFrameIndex];
                            if (frame?.deltaVertices == null ||
                                frame.deltaVertices.Length !=
                                source.VertexCount)
                            {
                                throw new InvalidOperationException(
                                    $"Blendshape '{shape.Name}' frame {sourceFrameIndex} has an invalid vertex-delta count.");
                            }

                            Array.Copy(
                                frame.deltaVertices,
                                0,
                                deltaVertices,
                                source.VertexOffset,
                                source.VertexCount);
                            if (shape.HasNormals &&
                                frame.deltaNormals != null &&
                                frame.deltaNormals.Length ==
                                source.VertexCount)
                            {
                                Array.Copy(
                                    frame.deltaNormals,
                                    0,
                                    deltaNormals,
                                    source.VertexOffset,
                                    source.VertexCount);
                            }
                            if (shape.HasTangents &&
                                frame.deltaTangents != null &&
                                frame.deltaTangents.Length ==
                                source.VertexCount)
                            {
                                Array.Copy(
                                    frame.deltaTangents,
                                    0,
                                    deltaTangents,
                                    source.VertexOffset,
                                    source.VertexCount);
                            }
                        }
                    }
                    Interlocked.Increment(
                        ref BlendShapeFramesPrepared);
                }
                finally
                {
                    stopwatch.Stop();
                    Interlocked.Add(
                        ref Ticks_BlendShapeFramePreparation,
                        stopwatch.ElapsedTicks);
                }
            }

            private void AdvanceCursor()
            {
                frameIndex++;
                if (frameIndex >=
                    shapes[shapeIndex].FrameWeights.Length)
                {
                    shapeIndex++;
                    frameIndex = 0;
                }
            }

            private static ShapePlan[] BuildShapePlans(
                SkinnedMeshCombiner.CombineInstance[] sources,
                Dictionary<string, BlendShapeVertexData> metadata,
                UMAData.UMARecipe recipe,
                int[] sourceVertexOffsets)
            {
                if (metadata == null ||
                    metadata.Count == 0)
                {
                    return Array.Empty<ShapePlan>();
                }

                var result =
                    new List<ShapePlan>(metadata.Count);
                foreach (KeyValuePair<string, BlendShapeVertexData>
                         entry in metadata)
                {
                    BlendShapeVertexData info = entry.Value;
                    if (info == null ||
                        info.frameCount <= 0)
                    {
                        continue;
                    }

                    var sourcePlans = new List<SourcePlan>();
                    for (int sourceIndex = 0;
                         sourceIndex < sources.Length;
                         sourceIndex++)
                    {
                        SkinnedMeshCombiner.CombineInstance source =
                            sources[sourceIndex];
                        List<UMABlendShape> sourceShapes =
                            SkinnedMeshCombiner.GetBlendshapeSources(
                                source.meshData,
                                recipe);
                        if (sourceShapes == null)
                        {
                            continue;
                        }
                        for (int sourceShapeIndex = 0;
                             sourceShapeIndex < sourceShapes.Count;
                             sourceShapeIndex++)
                        {
                            UMABlendShape sourceShape =
                                sourceShapes[sourceShapeIndex];
                            if (sourceShape != null &&
                                sourceShape.shapeName == entry.Key)
                            {
                                sourcePlans.Add(new SourcePlan
                                {
                                    Shape = sourceShape,
                                    VertexOffset =
                                        sourceVertexOffsets[sourceIndex],
                                    VertexCount =
                                        source.meshData.vertexCount
                                });
                                break;
                            }
                        }
                    }

                    result.Add(new ShapePlan
                    {
                        Name = entry.Key,
                        HasNormals = info.hasNormals,
                        HasTangents = info.hasTangents,
                        FrameWeights =
                            (float[])info.frameWeights.Clone(),
                        Sources = sourcePlans.ToArray()
                    });
                }
                return result.ToArray();
            }

            private void ThrowIfDisposed()
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(IncrementalBlendShapeLoader));
                }
            }

            /// <summary>
            /// Requests cancellation without waiting for the worker task.
            /// The owning operation can poll
            /// <see cref="HasPendingPreparation"/> and dispose after the task
            /// reaches a terminal state.
            /// </summary>
            public void CancelPreparation()
            {
                if (!disposed)
                {
                    cancellation.Cancel();
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                cancellation.Cancel();
                Task task = preparationTask;
                if (task != null && !task.IsCompleted)
                {
                    try
                    {
                        task.GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        // The operation reports preparation failures from
                        // Step. Cleanup must remain idempotent.
                    }
                }
                cancellation.Dispose();
                preparationTask = null;
                disposed = true;
            }
        }

        #region Jobs / Helpers
#if UMA_UNSAFE
        private static unsafe void FastCopyPositionsUnsafe(NativeArray<Vector3> dst, int dstStart, Vector3[] srcVertices, Vector3[] srcNormals, int count, float expandAlongNormal)
        {
            var dstPtr = (Vector3*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<Vector3>());
            if (expandAlongNormal != 0f && srcNormals != null && srcNormals.Length >= count)
            {
                fixed (Vector3* sV = srcVertices)
                fixed (Vector3* sN = srcNormals)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i] = sV[i] + sN[i] * expandAlongNormal;
                    }
                }
            }
            else
            {
                fixed (Vector3* sV = srcVertices)
                {
                    long bytes = (long)count * UnsafeUtility.SizeOf<Vector3>(); UnsafeUtility.MemCpy(dstPtr, sV, bytes);
                }
            }
        }
        private static unsafe void PackNormTanUnsafe(NativeArray<NormTan> dst, int dstStart, Vector3[] normals, Vector4[] tangents, int count, bool hasNormals, bool hasTangents)
        {
            var dstPtr = (NormTan*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<NormTan>());
            bool nValid = hasNormals && normals != null && normals.Length >= count; bool tValid = hasTangents && tangents != null && tangents.Length >= count; Vector3 zeroN = default; Vector4 defT = new Vector4(1, 0, 0, 1);
            if (nValid && tValid)
            {
                fixed (Vector3* nP = normals) fixed (Vector4* tP = tangents) { for (int i = 0; i < count; i++) { dstPtr[i].normal = nP[i]; dstPtr[i].tangent = tP[i]; } }
            }
            else if (nValid)
            {
                fixed (Vector3* nP = normals) { for (int i = 0; i < count; i++) { dstPtr[i].normal = nP[i]; dstPtr[i].tangent = BuildFallbackTangent(nP[i], 1f); } }
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
#else
#endif
        private static Vector4 BuildFallbackTangent(Vector3 normal, float handedness)
        {
            handedness = handedness < 0f ? -1f : 1f;
            if (normal.sqrMagnitude <= 1e-20f) return new Vector4(1f, 0f, 0f, handedness);
            normal.Normalize();
            Vector3 reference = Mathf.Abs(normal.y) < 0.999f ? Vector3.up : Vector3.right;
            Vector3 tangent = Vector3.Cross(reference, normal);
            tangent.Normalize();
            return new Vector4(tangent.x, tangent.y, tangent.z, handedness);
        }

        [BurstCompile]
        private struct CopyIndicesJobInt : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<int> Dst;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Validation;
            public int DstStart;
            public int Count;
            public int Add;
            public int SourceVertexCount;
            public int ValidationIndex;

            public void Execute()
            {
                int invalid = 0;
                for (int i = 0; i < Count; i++)
                {
                    int sourceIndex = Src[i];
                    if ((uint)sourceIndex >= (uint)SourceVertexCount)
                    {
                        invalid = 1;
                        sourceIndex = 0;
                    }
                    Dst[DstStart + i] = sourceIndex + Add;
                }
                Validation[ValidationIndex] = invalid;
            }
        }

        [BurstCompile]
        private struct CopyIndicesJobU16 : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<ushort> Dst;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Validation;
            public int DstStart;
            public int Count;
            public int Add;
            public int SourceVertexCount;
            public int ValidationIndex;

            public void Execute()
            {
                int invalid = 0;
                for (int i = 0; i < Count; i++)
                {
                    int sourceIndex = Src[i];
                    int outputIndex = sourceIndex + Add;
                    if ((uint)sourceIndex >= (uint)SourceVertexCount || (uint)outputIndex > ushort.MaxValue)
                    {
                        invalid = 1;
                        outputIndex = 0;
                    }
                    Dst[DstStart + i] = (ushort)outputIndex;
                }
                Validation[ValidationIndex] = invalid;
            }
        }

        [BurstCompile]
        private struct MaskedCopyIndicesJobInt : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [ReadOnly] public NativeArray<byte> Mask;
            [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<int> Dst;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Validation;
            public int DstStart;
            public int Add;
            public int SourceVertexCount;
            public int ValidationIndex;

            public void Execute()
            {
                int dst = DstStart;
                int invalid = 0;
                for (int triangle = 0; triangle < Mask.Length; triangle++)
                {
                    if (Mask[triangle] != 0) continue;
                    int index = triangle * 3;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int sourceIndex = Src[index + corner];
                        if ((uint)sourceIndex >= (uint)SourceVertexCount)
                        {
                            invalid = 1;
                            sourceIndex = 0;
                        }
                        Dst[dst++] = sourceIndex + Add;
                    }
                }
                Validation[ValidationIndex] = invalid;
            }
        }

        [BurstCompile]
        private struct MaskedCopyIndicesJobU16 : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [ReadOnly] public NativeArray<byte> Mask;
            [NativeDisableContainerSafetyRestriction, WriteOnly] public NativeArray<ushort> Dst;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Validation;
            public int DstStart;
            public int Add;
            public int SourceVertexCount;
            public int ValidationIndex;

            public void Execute()
            {
                int dst = DstStart;
                int invalid = 0;
                for (int triangle = 0; triangle < Mask.Length; triangle++)
                {
                    if (Mask[triangle] != 0) continue;
                    int index = triangle * 3;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int sourceIndex = Src[index + corner];
                        int outputIndex = sourceIndex + Add;
                        if ((uint)sourceIndex >= (uint)SourceVertexCount || (uint)outputIndex > ushort.MaxValue)
                        {
                            invalid = 1;
                            outputIndex = 0;
                        }
                        Dst[dst++] = (ushort)outputIndex;
                    }
                }
                Validation[ValidationIndex] = invalid;
            }
        }

        private static NativeArray<byte> BitArrayToNative(BitArray ba, int triangleCount, Allocator allocator)
        {
            var result = new NativeArray<byte>(triangleCount, allocator, NativeArrayOptions.ClearMemory);
            try
            {
                int count = Math.Min(ba.Count, triangleCount);
                for (int i = 0; i < count; i++) result[i] = ba[i] ? (byte)1 : (byte)0;
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        #endregion

        #region UMA helpers
        [Flags] private enum MeshComponents { none = 0, has_normals = 1, has_tangents = 2, has_colors32 = 4, has_uv = 8, has_uv2 = 16, has_uv3 = 32, has_uv4 = 64, has_blendShapes = 128, has_clothSkinning = 256 }
        internal class BlendShapeVertexData { public bool hasNormals; public bool hasTangents; public int frameCount; public float[] frameWeights; }
        private static void ValidateSources(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            if (sources == null || sources.Length == 0)
                throw new ArgumentException("At least one combine source is required.", nameof(sources));

            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (source == null)
                    throw new ArgumentException($"Combine source {sourceIndex} is null.", nameof(sources));
                if (source.meshData == null)
                    throw new ArgumentException($"Combine source {sourceIndex} has no mesh data.", nameof(sources));
            }

            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                var meshData = source.meshData;
                string sourceName = source.slotData?.slotName ?? $"source {sourceIndex}";
                int vertexCount = meshData.vertexCount;

                if (source.slotData?.asset == null)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has no SlotDataAsset.");
                if (source.slotData.asset.meshData == null)
                    throw new InvalidOperationException($"Combine source '{sourceName}' SlotDataAsset has no source mesh data.");
                if (source.applyMeshModifiersInJobs && !SupportsJobifiedMeshModifiers(source.slotData))
                    throw new InvalidOperationException($"Combine source '{sourceName}' opted into jobified modifiers with an unsupported or mutable adjustment stack.");
                if (source.applyMeshModifiersInJobs && source.slotData.asset.meshData.vertexCount != vertexCount)
                    throw new InvalidOperationException($"Combine source '{sourceName}' changed topology after its jobified mesh modifiers were authored.");
                if (meshData.vertices == null || vertexCount <= 0 || meshData.vertices.Length != vertexCount)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has invalid vertex data.");
                ValidateOptionalVertexChannel(meshData.normals, vertexCount, sourceName, "normals");
                ValidateOptionalVertexChannel(meshData.tangents, vertexCount, sourceName, "tangents");
                ValidateOptionalVertexChannel(meshData.colors32, vertexCount, sourceName, "colors");
                ValidateOptionalVertexChannel(meshData.uv, vertexCount, sourceName, "UV0");
                ValidateOptionalVertexChannel(meshData.uv2, vertexCount, sourceName, "UV1");
                ValidateOptionalVertexChannel(meshData.uv3, vertexCount, sourceName, "UV2");
                ValidateOptionalVertexChannel(meshData.uv4, vertexCount, sourceName, "UV3");
                if (meshData.submeshes == null || meshData.subMeshCount <= 0 || meshData.submeshes.Length < meshData.subMeshCount)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has invalid submesh data.");
                if (source.targetSubmeshIndices == null || source.targetSubmeshIndices.Length < meshData.subMeshCount)
                    throw new InvalidOperationException($"Combine source '{sourceName}' does not map every source submesh.");

                for (int submesh = 0; submesh < meshData.subMeshCount; submesh++)
                {
                    if (meshData.submeshes[submesh] == null)
                        throw new InvalidOperationException($"Combine source '{sourceName}' has a null submesh at index {submesh}.");
                    int target = source.targetSubmeshIndices[submesh];
                    if (target < -1)
                        throw new InvalidOperationException($"Combine source '{sourceName}' maps submesh {submesh} to invalid output submesh {target}.");
                }

                if (meshData.ManagedBonesPerVertex == null || meshData.ManagedBonesPerVertex.Length != vertexCount)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has {meshData.ManagedBonesPerVertex?.Length ?? 0} bones-per-vertex entries for {vertexCount} vertices.");
                if (meshData.ManagedBoneWeights == null)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has no bone weight data.");
                if (meshData.boneNameHashes == null || meshData.bindPoses == null || meshData.boneNameHashes.Length != meshData.bindPoses.Length)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has inconsistent bone hashes and bind poses.");
                for (int bindPose = 0; bindPose < meshData.bindPoses.Length; bindPose++)
                {
                    if (!IsFinite(meshData.bindPoses[bindPose]))
                        throw new InvalidOperationException($"Combine source '{sourceName}' bind pose {bindPose} contains a non-finite value.");
                }
                if (meshData.umaBones == null)
                    throw new InvalidOperationException($"Combine source '{sourceName}' has no UMA bone hierarchy data.");
                int previousBoneHash = int.MinValue;
                for (int bone = 0; bone < meshData.umaBones.Length; bone++)
                {
                    var transform = meshData.umaBones[bone];
                    if (transform == null)
                        throw new InvalidOperationException($"Combine source '{sourceName}' has a null UMA bone transform at index {bone}.");
                    if (bone > 0 && transform.hash < previousBoneHash)
                        throw new InvalidOperationException($"Combine source '{sourceName}' UMA bone hierarchy is not sorted by hash at index {bone}.");
                    if (!IsFinite(transform.position) || !IsFinite(transform.rotation) || !IsFinite(transform.scale))
                        throw new InvalidOperationException($"Combine source '{sourceName}' UMA bone transform {bone} contains a non-finite value.");
                    previousBoneHash = transform.hash;
                }

                long expectedWeightCount = 0;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                    expectedWeightCount += meshData.ManagedBonesPerVertex[vertex];
                if (expectedWeightCount != meshData.ManagedBoneWeights.Length)
                    throw new InvalidOperationException($"Combine source '{sourceName}' declares {expectedWeightCount} bone weights but stores {meshData.ManagedBoneWeights.Length}.");
                for (int weight = 0; weight < meshData.ManagedBoneWeights.Length; weight++)
                {
                    int boneIndex = meshData.ManagedBoneWeights[weight].boneIndex;
                    if ((uint)boneIndex >= (uint)meshData.boneNameHashes.Length)
                        throw new InvalidOperationException($"Combine source '{sourceName}' bone weight {weight} references invalid bone {boneIndex}.");
                    float value = meshData.ManagedBoneWeights[weight].weight;
                    if (!IsFinite(value) || value < 0f)
                        throw new InvalidOperationException($"Combine source '{sourceName}' bone weight {weight} has invalid value {value}.");
                }
            }
        }

        private static void ValidateOptionalVertexChannel<T>(T[] channel, int vertexCount, string sourceName, string channelName)
        {
            if (channel != null && channel.Length != 0 && channel.Length != vertexCount)
                throw new InvalidOperationException($"Combine source '{sourceName}' has {channel.Length} {channelName} entries for {vertexCount} vertices.");
        }

        private static NativeArray<int> GetTrianglesForLOD(SubMeshTriangles submesh, int lodLevel)
        {
            // GetTriangles already returns the base buffer when no LOD ranges exist and clamps
            // an out-of-range LOD to the final authored level. Do not turn an explicitly empty
            // LOD into LOD0 geometry.
            return submesh.GetTriangles(Mathf.Max(0, lodLevel));
        }

        private static bool ShouldApplyTriangleMask(int lodLevel)
        {
#if UNITY_6000_2_OR_NEWER
            // MeshHideAsset authors/generates a mask for the active internal LOD.
            return true;
#else
            // Older supported Unity versions only provide base-LOD hide masks.
            return lodLevel == 0;
#endif
        }

        private static void ValidateIndexDestinationRange(int start, int count, int minimum, int maximum, int sourceIndex, int submeshIndex)
        {
            if (minimum < 0 || maximum < minimum || start < minimum || count < 0 || start > maximum - count)
                throw new InvalidOperationException($"Source {sourceIndex}, submesh {submeshIndex} would write [{start}, {start + count}) outside its assigned range [{minimum}, {maximum}).");
        }

        private static void AnalyzeSources(SkinnedMeshCombiner.CombineInstance[] sources, int[] subMeshTriangleLength, int lodLevel, ref int vertexCount, ref int boneWeightCount, ref int bindPoseCount, ref int transformHierarchyCount, ref MeshComponents meshComponents)
        {
            Array.Fill(subMeshTriangleLength, 0);
            for (int j = 0; j < sources.Length; j++)
            {
                var src = sources[j];
                boneWeightCount = checked(boneWeightCount + src.meshData.ManagedBoneWeights.Length);
                vertexCount = checked(vertexCount + src.meshData.vertices.Length);
                bindPoseCount = checked(bindPoseCount + src.meshData.bindPoses.Length);
                transformHierarchyCount = checked(transformHierarchyCount + src.meshData.umaBones.Length);
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
                    NativeArray<int> triangles = GetTrianglesForLOD(src.meshData.submeshes[i], lodLevel);
                    int indexLen = triangles.IsCreated ? triangles.Length : 0;
                    if ((indexLen % 3) != 0)
                        throw new InvalidOperationException($"Source {j}, submesh {i} has {indexLen} indices; triangle index counts must be divisible by three.");
                    int dest = src.targetSubmeshIndices[i]; if (dest < 0) continue;
                    // If there is a mask, its length is in triangles with true=remove. Compute kept index length accordingly.
                    if (ShouldApplyTriangleMask(lodLevel) && src.triangleMask != null && i < src.triangleMask.Length && src.triangleMask[i] != null && src.triangleMask[i].Length > 0)
                    {
                        int triCount = indexLen / 3;
                        int removedTris = 0;
                        int maskCount = Math.Min(triCount, src.triangleMask[i].Length);
                        for (int triangle = 0; triangle < maskCount; triangle++)
                            if (src.triangleMask[i][triangle]) removedTris++;
                        int keptTris = Mathf.Clamp(triCount - removedTris, 0, triCount);
                        subMeshTriangleLength[dest] = checked(subMeshTriangleLength[dest] + checked(keptTris * 3));
                    }
                    else
                    {
                        subMeshTriangleLength[dest] = checked(subMeshTriangleLength[dest] + indexLen);
                    }
                }
            }
        }
        private static void AnalyzeBlendShapeSources(
            SkinnedMeshCombiner.CombineInstance[] sources,
            Dictionary<string, float> bakedBlendshapes,
            bool loadAllFrames,
            bool loadNormals,
            bool loadTangents,
            ref MeshComponents meshComponents,
            out Dictionary<string, BlendShapeVertexData> blendShapeNames,
            UMAData.UMARecipe recipe)
        {
            blendShapeNames = new Dictionary<string, BlendShapeVertexData>(); int bakedCount = 0;
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k];
                var sourceShapes = SkinnedMeshCombiner.GetBlendshapeSources(src.meshData, recipe);
                if (sourceShapes == null || sourceShapes.Count == 0) continue;
                string sourceName = src.slotData?.slotName ?? $"source {k}";
                for (int j = 0; j < sourceShapes.Count; j++)
                {
                    var shape = sourceShapes[j];
                    ValidateBlendShape(shape, src.meshData.vertexCount, sourceName);
                    string shapeName = shape.shapeName;
                    for (int previousShape = 0; previousShape < j; previousShape++)
                    {
                        if (sourceShapes[previousShape] != null && sourceShapes[previousShape].shapeName == shapeName)
                            throw new InvalidOperationException($"Combine source '{sourceName}' contains duplicate blendshape '{shapeName}'.");
                    }
                    if (bakedBlendshapes.TryGetValue(shapeName, out float bakedWeight))
                    {
                        if (!IsFinite(bakedWeight))
                            throw new InvalidOperationException($"Baked blendshape '{shapeName}' has non-finite weight {bakedWeight}.");
                        bakedCount++;
                        continue;
                    }

                    if (!blendShapeNames.TryGetValue(shapeName, out var meta))
                    {
                        int outputFrameCount = loadAllFrames ? shape.frames.Length : 1;
                        meta = new BlendShapeVertexData
                        {
                            frameCount = outputFrameCount,
                            frameWeights = new float[outputFrameCount]
                        };
                        if (loadAllFrames)
                        {
                            for (int frame = 0; frame < shape.frames.Length; frame++)
                                meta.frameWeights[frame] = shape.frames[frame].frameWeight;
                        }
                        else
                        {
                            meta.frameWeights[0] = shape.frames[shape.frames.Length - 1].frameWeight;
                        }
                        blendShapeNames.Add(shapeName, meta);
                    }
                    else
                    {
                        int sourceOutputFrameCount = loadAllFrames ? shape.frames.Length : 1;
                        if (meta.frameCount != sourceOutputFrameCount)
                            throw new InvalidOperationException($"Blendshape '{shapeName}' has inconsistent frame counts across combined sources ({meta.frameCount} and {sourceOutputFrameCount}).");
                        for (int frame = 0; frame < sourceOutputFrameCount; frame++)
                        {
                            int sourceFrame = loadAllFrames ? frame : shape.frames.Length - 1;
                            if (!Mathf.Approximately(meta.frameWeights[frame], shape.frames[sourceFrame].frameWeight))
                                throw new InvalidOperationException($"Blendshape '{shapeName}' frame {frame} has inconsistent weights across combined sources.");
                        }
                    }

                    int firstFrame = loadAllFrames ? 0 : shape.frames.Length - 1;
                    for (int frame = firstFrame; frame < shape.frames.Length; frame++)
                    {
                        meta.hasNormals |= loadNormals && shape.frames[frame].HasNormals();
                        meta.hasTangents |= loadTangents && shape.frames[frame].HasTangents();
                    }
                }
            }
            if (blendShapeNames.Count > 0 || bakedCount > 0) meshComponents |= MeshComponents.has_blendShapes;
        }

        private static void ValidateBlendShape(UMABlendShape shape, int vertexCount, string sourceName)
        {
            if (shape == null || string.IsNullOrEmpty(shape.shapeName))
                throw new InvalidOperationException($"Combine source '{sourceName}' contains a null or unnamed blendshape.");
            if (shape.frames == null || shape.frames.Length == 0)
                throw new InvalidOperationException($"Blendshape '{shape.shapeName}' on '{sourceName}' has no frames.");

            float previousWeight = float.NegativeInfinity;
            for (int frameIndex = 0; frameIndex < shape.frames.Length; frameIndex++)
            {
                var frame = shape.frames[frameIndex];
                if (frame == null)
                    throw new InvalidOperationException($"Blendshape '{shape.shapeName}' on '{sourceName}' has a null frame at index {frameIndex}.");
                if (float.IsNaN(frame.frameWeight) || float.IsInfinity(frame.frameWeight) || frame.frameWeight <= previousWeight)
                    throw new InvalidOperationException($"Blendshape '{shape.shapeName}' on '{sourceName}' has a non-finite or non-increasing weight at frame {frameIndex}.");
                previousWeight = frame.frameWeight;
                if (frame.deltaVertices == null || frame.deltaVertices.Length != vertexCount)
                    throw new InvalidOperationException($"Blendshape '{shape.shapeName}' frame {frameIndex} on '{sourceName}' has {frame.deltaVertices?.Length ?? 0} vertex deltas for {vertexCount} vertices.");
                ValidateOptionalVertexChannel(frame.deltaNormals, vertexCount, sourceName, $"'{shape.shapeName}' frame {frameIndex} normal deltas");
                ValidateOptionalVertexChannel(frame.deltaTangents, vertexCount, sourceName, $"'{shape.shapeName}' frame {frameIndex} tangent deltas");
            }
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
        private static void RecalculateModifiedSources(
            SkinnedMeshCombiner.CombineInstance[] sources,
            int[] sourceVertexOffsets,
            NativeArray<VertexDeltaRecord> vertexDeltas,
            NativeArray<Vector3> positions,
            NativeArray<NormTan> normalsTangents,
            NativeArray<ColUV01> colorsUV,
            bool hasTangents,
            bool hasUV,
            int lodLevel)
        {
            if (!vertexDeltas.IsCreated || vertexDeltas.Length == 0) return;
            var vectorPool = ArrayPool<Vector3>.Shared;
            int deltaIndex = 0;
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (!source.applyMeshModifiersInJobs) continue;

                int vertexCount = source.meshData.vertexCount;
                int vertexOffset = sourceVertexOffsets[sourceIndex];
                int vertexEnd = vertexOffset + vertexCount;
                while (deltaIndex < vertexDeltas.Length && vertexDeltas[deltaIndex].vertexIndex < vertexOffset)
                    deltaIndex++;
                if (deltaIndex >= vertexDeltas.Length || vertexDeltas[deltaIndex].vertexIndex >= vertexEnd)
                    continue;
                var normalSums = vectorPool.Rent(vertexCount);
                Vector3[] tangentSums = null;
                Vector3[] bitangentSums = null;
                bool calculateTangents = hasTangents && hasUV && colorsUV.IsCreated;
                if (calculateTangents)
                {
                    tangentSums = vectorPool.Rent(vertexCount);
                    bitangentSums = vectorPool.Rent(vertexCount);
                }

                try
                {
                    Array.Clear(normalSums, 0, vertexCount);
                    if (calculateTangents)
                    {
                        Array.Clear(tangentSums, 0, vertexCount);
                        Array.Clear(bitangentSums, 0, vertexCount);
                    }

                    for (int submeshIndex = 0; submeshIndex < source.meshData.subMeshCount; submeshIndex++)
                    {
                        NativeArray<int> triangles = GetTrianglesForLOD(source.meshData.submeshes[submeshIndex], lodLevel);
                        if (!triangles.IsCreated) continue;

                        var mask = ShouldApplyTriangleMask(lodLevel) && source.triangleMask != null && submeshIndex < source.triangleMask.Length
                            ? source.triangleMask[submeshIndex]
                            : null;
                        int triangleCount = triangles.Length / 3;
                        for (int triangle = 0; triangle < triangleCount; triangle++)
                        {
                            if (mask != null && triangle < mask.Length && mask[triangle]) continue;
                            int triangleStart = triangle * 3;
                            int i0 = triangles[triangleStart];
                            int i1 = triangles[triangleStart + 1];
                            int i2 = triangles[triangleStart + 2];
                            if ((uint)i0 >= (uint)vertexCount || (uint)i1 >= (uint)vertexCount || (uint)i2 >= (uint)vertexCount)
                                continue;

                            Vector3 p0 = positions[vertexOffset + i0];
                            Vector3 p1 = positions[vertexOffset + i1];
                            Vector3 p2 = positions[vertexOffset + i2];
                            Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
                            if (faceNormal.sqrMagnitude <= 1e-20f) continue;
                            normalSums[i0] += faceNormal;
                            normalSums[i1] += faceNormal;
                            normalSums[i2] += faceNormal;

                            if (!calculateTangents) continue;
                            Vector2 uv0 = colorsUV[vertexOffset + i0].uv0;
                            Vector2 uv1 = colorsUV[vertexOffset + i1].uv0;
                            Vector2 uv2 = colorsUV[vertexOffset + i2].uv0;
                            Vector3 edge1 = p1 - p0;
                            Vector3 edge2 = p2 - p0;
                            Vector2 uvEdge1 = uv1 - uv0;
                            Vector2 uvEdge2 = uv2 - uv0;
                            float determinant = uvEdge1.x * uvEdge2.y - uvEdge1.y * uvEdge2.x;
                            if (Mathf.Abs(determinant) <= 1e-12f) continue;
                            float reciprocal = 1f / determinant;
                            Vector3 tangent = (edge1 * uvEdge2.y - edge2 * uvEdge1.y) * reciprocal;
                            Vector3 bitangent = (edge2 * uvEdge1.x - edge1 * uvEdge2.x) * reciprocal;
                            tangentSums[i0] += tangent;
                            tangentSums[i1] += tangent;
                            tangentSums[i2] += tangent;
                            bitangentSums[i0] += bitangent;
                            bitangentSums[i1] += bitangent;
                            bitangentSums[i2] += bitangent;
                        }
                    }

                    for (int vertex = 0; vertex < vertexCount; vertex++)
                    {
                        Vector3 normal = normalSums[vertex];
                        if (normal.sqrMagnitude <= 1e-20f) continue;
                        normal.Normalize();
                        int outputIndex = vertexOffset + vertex;
                        var normalTangent = normalsTangents[outputIndex];
                        normalTangent.normal = normal;

                        if (hasTangents)
                        {
                            Vector3 tangent = calculateTangents ? tangentSums[vertex] : Vector3.zero;
                            if (tangent.sqrMagnitude <= 1e-20f)
                                tangent = (Vector3)normalTangent.tangent;
                            tangent -= normal * Vector3.Dot(normal, tangent);
                            if (tangent.sqrMagnitude > 1e-20f)
                            {
                                tangent.Normalize();
                                float handedness = calculateTangents && bitangentSums[vertex].sqrMagnitude > 1e-20f
                                    ? (Vector3.Dot(Vector3.Cross(normal, tangent), bitangentSums[vertex]) < 0f ? -1f : 1f)
                                    : (normalTangent.tangent.w < 0f ? -1f : 1f);
                                normalTangent.tangent = new Vector4(tangent.x, tangent.y, tangent.z, handedness);
                            }
                            else
                            {
                                normalTangent.tangent = BuildFallbackTangent(normal, normalTangent.tangent.w);
                            }
                        }
                        normalsTangents[outputIndex] = normalTangent;
                    }
                }
                finally
                {
                    vectorPool.Return(normalSums, false);
                    if (tangentSums != null) vectorPool.Return(tangentSums, false);
                    if (bitangentSums != null) vectorPool.Return(bitangentSums, false);
                }
            }
        }

        private static ClothSkinningCoefficient[] BuildClothCoefficients(
            SkinnedMeshCombiner.CombineInstance[] sources,
            NativeArray<Vector3> finalPositions,
            int[] sourceVertexOffsets)
        {
            var clothDict = new Dictionary<Vector3, int>(1024); var result = new List<ClothSkinningCoefficient>(1024);
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k]; int count = src.meshData.vertexCount;
                var serialized = src.meshData.clothSkinningSerialized;
                Dictionary<Vector3, int> local = null;
                if (serialized != null && serialized.Length > 0)
                {
                    local = new Dictionary<Vector3, int>(count);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 sourcePosition = src.meshData.vertices[i];
                        if (!local.ContainsKey(sourcePosition)) local.Add(sourcePosition, local.Count);
                    }
                    if (serialized.Length < local.Count)
                        throw new InvalidOperationException($"Slot '{src.slotData?.slotName}' has {serialized.Length} cloth coefficients for {local.Count} unique source vertices.");
                }

                for (int i = 0; i < count; i++)
                {
                    Vector3 finalPosition = finalPositions[sourceVertexOffsets[k] + i];
                    var coefficient = new ClothSkinningCoefficient { maxDistance = 0f, collisionSphereDistance = float.MaxValue };
                    if (local != null && local.TryGetValue(src.meshData.vertices[i], out int serializedIndex) && (uint)serializedIndex < (uint)serialized.Length)
                        ConvertData(ref serialized[serializedIndex], ref coefficient);

                    if (!clothDict.TryGetValue(finalPosition, out int globalIndex))
                    {
                        clothDict.Add(finalPosition, result.Count);
                        result.Add(coefficient);
                    }
                    else
                    {
                        result[globalIndex] = coefficient;
                    }
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
        internal struct BoundsResult { public Vector3 Min; public Vector3 Max; public byte IsValid; }
        [BurstCompile]
        private struct CalculateBoundsJob : IJob
        {
            [ReadOnly] public NativeArray<Vector3> Vertices;
            [WriteOnly] public NativeArray<BoundsResult> Result;

            public void Execute()
            {
                if (Vertices.Length == 0)
                {
                    Result[0] = default;
                    return;
                }

                Vector3 min = Vertices[0];
                Vector3 max = Vertices[0];
                if (!IsFinite(min))
                {
                    Result[0] = new BoundsResult { IsValid = 2 };
                    return;
                }
                for (int i = 1; i < Vertices.Length; i++)
                {
                    Vector3 v = Vertices[i];
                    if (!IsFinite(v))
                    {
                        Result[0] = new BoundsResult { IsValid = 2 };
                        return;
                    }
                    min = Vector3.Min(min, v);
                    max = Vector3.Max(max, v);
                }
                Result[0] = new BoundsResult { Min = min, Max = max, IsValid = 1 };
            }
        }
        private static readonly VertexAttributeDescriptor[][] VertexLayoutCache = new VertexAttributeDescriptor[128][];
        private sealed class VertexDeltaRecordComparer : IComparer<VertexDeltaRecord>
        {
            public static readonly VertexDeltaRecordComparer Instance = new VertexDeltaRecordComparer();
            public int Compare(VertexDeltaRecord left, VertexDeltaRecord right) => left.vertexIndex.CompareTo(right.vertexIndex);
        }

        private static VertexAttributeDescriptor[] BuildVertexLayout(bool hasNormals, bool hasTangents, bool hasUV, bool hasUV2, bool hasUV3, bool hasUV4, bool hasColors32)
        {
            int key = (hasNormals ? 1 : 0) | (hasTangents ? 2 : 0) | (hasUV ? 4 : 0) |
                (hasUV2 ? 8 : 0) | (hasUV3 ? 16 : 0) | (hasUV4 ? 32 : 0) | (hasColors32 ? 64 : 0);
            var cached = VertexLayoutCache[key];
            if (cached != null) return cached;
            var list = new List<VertexAttributeDescriptor>(8) { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0) }; int stream = 1;
            if (hasNormals || hasTangents) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream)); stream++; }
            if (hasColors32 || hasUV || hasUV2) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, stream)); stream++; }
            if (hasUV3 || hasUV4) { list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, stream)); }
            cached = list.ToArray();
            VertexLayoutCache[key] = cached;
            return cached;
        }
        private static NativeArray<VertexDeltaRecord> BuildVertexDeltaRecords(
            SkinnedMeshCombiner.CombineInstance[] sources,
            int[] sourceVertexOffsets)
        {
            return BuildVertexDeltaRecordsWithAllocator(
                sources,
                sourceVertexOffsets,
                Allocator.TempJob);
        }

        private static NativeArray<VertexDeltaRecord>
            BuildVertexDeltaRecordsWithAllocator(
            SkinnedMeshCombiner.CombineInstance[] sources,
            int[] sourceVertexOffsets,
            Allocator allocator)
        {
            int recordCapacity = 0;
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (!source.applyMeshModifiersInJobs) continue;
                var modifiers = source.slotData?.meshModifiers;
                if (modifiers == null) continue;
                for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
                {
                    var modifier = modifiers[modifierIndex];
                    if (modifier == null || modifier.Scale == 0f) continue;
                    var adjustments = modifier.adjustments?.vertexAdjustments;
                    if (adjustments != null)
                        recordCapacity = checked(recordCapacity + adjustments.Count);
                }
            }
            if (recordCapacity == 0) return default;

            var recordPool = ArrayPool<VertexDeltaRecord>.Shared;
            var records = recordPool.Rent(recordCapacity);
            int recordCount = 0;
            try
            {
                for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    var source = sources[sourceIndex];
                    if (!source.applyMeshModifiersInJobs) continue;
                    var modifiers = source.slotData?.meshModifiers;
                    if (modifiers == null) continue;
                    int vertexCount = source.meshData.vertexCount;
                    int vertexOffset = sourceVertexOffsets[sourceIndex];

                    for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
                    {
                        var modifier = modifiers[modifierIndex];
                        if (modifier == null || modifier.Scale == 0f) continue;
                        if (!IsFinite(modifier.Scale))
                            throw new InvalidOperationException($"Slot '{source.slotData?.slotName}' has a mesh modifier with a non-finite scale.");
                        var adjustments = modifier.adjustments?.vertexAdjustments;
                        if (adjustments == null) continue;
                        for (int adjustmentIndex = 0; adjustmentIndex < adjustments.Count; adjustmentIndex++)
                        {
                            var adjustment = adjustments[adjustmentIndex] as VertexDeltaAdjustment;
                            if (adjustment == null || (uint)adjustment.vertexIndex >= (uint)vertexCount) continue;
                            if (!IsFinite(adjustment.weight) || !IsFinite(adjustment.delta))
                                throw new InvalidOperationException($"Slot '{source.slotData?.slotName}' has a non-finite vertex delta at index {adjustment.vertexIndex}.");
                            Vector3 delta = adjustment.delta * (modifier.Scale * adjustment.weight);
                            if (!IsFinite(delta))
                                throw new InvalidOperationException($"Slot '{source.slotData?.slotName}' vertex delta {adjustment.vertexIndex} overflowed while applying modifier scale and weight.");
                            if (delta.sqrMagnitude <= 0f) continue;
                            if (recordCount >= recordCapacity)
                                throw new InvalidOperationException($"Slot '{source.slotData?.slotName}' mesh modifier stack changed while it was being prepared.");
                            records[recordCount++] = new VertexDeltaRecord { vertexIndex = vertexOffset + adjustment.vertexIndex, delta = delta };
                        }
                    }
                }

                if (recordCount == 0) return default;
                Array.Sort(records, 0, recordCount, VertexDeltaRecordComparer.Instance);

                int compactCount = 0;
                var accumulated = records[0];
                for (int i = 1; i < recordCount; i++)
                {
                    var current = records[i];
                    if (current.vertexIndex == accumulated.vertexIndex)
                    {
                        accumulated.delta += current.delta;
                        if (!IsFinite(accumulated.delta))
                            throw new InvalidOperationException($"Accumulated vertex delta {accumulated.vertexIndex} overflowed.");
                        continue;
                    }
                    if (accumulated.delta.sqrMagnitude > 0f)
                        records[compactCount++] = accumulated;
                    accumulated = current;
                }
                if (accumulated.delta.sqrMagnitude > 0f)
                    records[compactCount++] = accumulated;
                if (compactCount == 0) return default;

                var result = new NativeArray<VertexDeltaRecord>(compactCount, allocator, NativeArrayOptions.UninitializedMemory);
                try
                {
                    for (int recordIndex = 0; recordIndex < compactCount; recordIndex++)
                        result[recordIndex] = records[recordIndex];
                    return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }
            finally
            {
                recordPool.Return(records, false);
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        private static bool IsFinite(Matrix4x4 value)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (!IsFinite(value[row, column])) return false;
            return true;
        }

        private struct SourceVertexRange
        {
            public int start;
            public int count;
        }

        private static NativeArray<UVTransform> BuildUVTransformsForUMA(
            NativeArray<ColUV01> vC01,
            UMAData umaData,
            int atlasResolution,
            int currentRendererIndex,
            SkinnedMeshCombiner.CombineInstance[] sources,
            int[] sourceVertexOffsets,
            UMARendererAsset rendererAssetOverride,
            bool hasRendererAssetOverride,
            Allocator allocator)
        {
            if (!vC01.IsCreated || vC01.Length == 0 || umaData?.generatedMaterials == null)
                return default;
            if (atlasResolution <= 0)
                throw new ArgumentOutOfRangeException(nameof(atlasResolution), atlasResolution, "Atlas resolution must be positive when UVs are present.");

            var targetRendererAsset = hasRendererAssetOverride
                ? rendererAssetOverride
                : umaData.GetRendererAsset(currentRendererIndex);
            var materials = umaData.generatedMaterials.materials;
            var transforms = new List<UVTransform>(Math.Min(128, materials.Count * 4));
            var sourceRanges = new Dictionary<SlotData, Queue<SourceVertexRange>>();
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var slot = sources[sourceIndex].slotData;
                if (slot == null) continue;
                if (!sourceRanges.TryGetValue(slot, out var ranges))
                {
                    ranges = new Queue<SourceVertexRange>();
                    sourceRanges.Add(slot, ranges);
                }
                ranges.Enqueue(new SourceVertexRange
                {
                    start = sourceVertexOffsets[sourceIndex],
                    count = sources[sourceIndex].meshData.vertexCount
                });
            }

            for (int mi = 0; mi < materials.Count; mi++)
            {
                var gm = materials[mi];
                if (gm == null || gm.rendererAsset != targetRendererAsset) continue;
                var fragments = gm.materialFragments;
                for (int f = 0; f < fragments.Count; f++)
                {
                    var fragment = fragments[f]; var slot = fragment.slotData;
                    if (UMAMeshData.IsNullOrEmptyMeshData(slot?.asset?.meshData)) continue;
                    if (!sourceRanges.TryGetValue(slot, out var ranges) || ranges.Count == 0) continue;
                    var range = ranges.Dequeue();
                    // Every material fragment creates one combine source. Consume the range
                    // even for non-atlased materials so duplicate SlotData references remain
                    // aligned with their own fragment rather than sharing a mutable offset.
                    if (gm.umaMaterial == null || !gm.umaMaterial.IsGeneratedTextures) continue;
                    // Declare atlas mapping variables first so cropping adjustments can modify them
                    var rect = fragment.atlasRegion;
                    float xMin = rect.xMin / atlasResolution; float xMax = rect.xMax / atlasResolution; float yMin = rect.yMin / atlasResolution; float yMax = rect.yMax / atlasResolution;
                    float xRange = xMax - xMin; float yRange = yMax - yMin;
                    if (fragment.isRectShared && slot.useAtlasOverlay)
                    {
                        OverlayData foundRect = null; for (int i = 0; i < fragment.overlayList.Count; i++) { var ov = fragment.overlayList[i]; if (slot.slotName != null && ov.overlayName != null && ov.overlayName.Contains(slot.slotName)) { foundRect = ov; break; } }
                        if (foundRect != null && foundRect.rect != Rect.zero)
                        {
                            if (Mathf.Abs(gm.cropResolution.x) <= Mathf.Epsilon || Mathf.Abs(gm.cropResolution.y) <= Mathf.Epsilon)
                                throw new InvalidOperationException($"Slot '{slot.slotName}' uses a shared atlas rect with an invalid crop resolution {gm.cropResolution}.");
                            var size = foundRect.rect.size * gm.resolutionScale;
                            var offX = foundRect.rect.x * gm.resolutionScale.x;
                            var offY = foundRect.rect.y * gm.resolutionScale.y;
                            xMin += offX / gm.cropResolution.x; xRange = size.x / gm.cropResolution.x;
                            yMin += offY / gm.cropResolution.y; yRange = size.y / gm.cropResolution.y;
                        }
                    }
                    if (range.start < 0 || range.count <= 0 || range.start > vC01.Length - range.count)
                        throw new InvalidOperationException($"Slot '{slot.slotName}' has an invalid source vertex range [{range.start}, {range.start + range.count}).");
                    if (!IsFinite(xMin) || !IsFinite(yMin) || !IsFinite(xRange) || !IsFinite(yRange))
                        throw new InvalidOperationException($"Slot '{slot.slotName}' produced a non-finite atlas UV transform.");
                    transforms.Add(new UVTransform { start = range.start, count = range.count, xMin = xMin, yMin = yMin, xScale = xRange, yScale = yRange });
                }
            }

            if (transforms.Count == 0) return default;
            transforms.Sort((left, right) => left.start.CompareTo(right.start));
            for (int i = 1; i < transforms.Count; i++)
            {
                var previous = transforms[i - 1];
                var current = transforms[i];
                if (current.start < previous.start + previous.count)
                    throw new InvalidOperationException($"Atlas UV transform ranges overlap at vertex {current.start}.");
            }
            var result = new NativeArray<UVTransform>(transforms.Count, allocator, NativeArrayOptions.UninitializedMemory);
            try
            {
                for (int i = 0; i < transforms.Count; i++) result[i] = transforms[i];
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static void ApplyUVTransforms(NativeArray<ColUV01> vertices, NativeArray<UVTransform> transforms)
        {
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                var transform = transforms[transformIndex];
                int end = transform.start + transform.count;
                for (int vertexIndex = transform.start; vertexIndex < end; vertexIndex++)
                {
                    var vertex = vertices[vertexIndex];
                    vertex.uv0.x = transform.xMin + vertex.uv0.x * transform.xScale;
                    vertex.uv0.y = transform.yMin + vertex.uv0.y * transform.yScale;
                    vertices[vertexIndex] = vertex;
                }
            }
        }
        public static void ConvertData(ref Vector2 source, ref ClothSkinningCoefficient dest) { dest.collisionSphereDistance = source.x; dest.maxDistance = source.y; }
        private static int FindTargetSubMeshCount(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            int highest = -1;
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                int sourceSubmeshCount = source.meshData.subMeshCount;
                for (int submesh = 0; submesh < sourceSubmeshCount; submesh++)
                {
                    int target = source.targetSubmeshIndices[submesh];
                    if (target > highest) highest = target;
                }
            }
            if (highest == int.MaxValue)
                throw new InvalidOperationException("An output submesh index cannot be Int32.MaxValue.");
            return highest + 1;
        }
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
