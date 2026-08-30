using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public sealed class PaintingEngine : IDisposable
    {
        // A half-texel conservative edge can miss the first exterior pixel when a UV boundary falls
        // close to a pixel center. One complete exterior ring is required for bilinear filtering;
        // 1.5 texels guarantees that ring for every subpixel edge placement. This is applied only to
        // true UV/slot boundaries via triangleBoundaryMask, never to shared triangle edges.
        internal const float TriangleBoundaryPaddingTexels = 1.5f;

        private readonly ComputeShader strokeShader;
        private readonly ComputeShader blurShader;
        private readonly ComputeShader normalShader;
        private readonly Material ribbonMaterial;
        private readonly MaterialPropertyBlock ribbonProperties = new MaterialPropertyBlock();
        private readonly StrokeHistory history = new StrokeHistory();
        private StrokeContext activeContext;
        private readonly List<ActiveTarget> activeTargets = new List<ActiveTarget>();
        private readonly HashSet<TextureLayerCompositor> activeCompositors =
            new HashSet<TextureLayerCompositor>();
        private readonly Dictionary<string, Texture2D> geometryMasks = new Dictionary<string, Texture2D>();
        private readonly Queue<string> geometryMaskOrder = new Queue<string>();
        private readonly Dictionary<int, Texture2D> ribbonCurveTextures = new Dictionary<int, Texture2D>();
        private Mesh directUVRibbonMesh;
        private readonly Dictionary<TextureSet, TexturePaintStrokeRecord> activeStrokeRecords =
            new Dictionary<TextureSet, TexturePaintStrokeRecord>();
        private readonly List<StrokeRecordBinding> activeStrokeBindings = new List<StrokeRecordBinding>();
        // Procedural paths clear their layer before being regenerated. Retain the previous raster
        // bounds so a smaller replacement can also recompose pixels that are no longer touched by
        // the new path. Keys include the history group because a target can be reassigned after a
        // layer is duplicated or converted.
        private readonly Dictionary<string, RectInt> proceduralRasterBounds =
            new Dictionary<string, RectInt>(StringComparer.Ordinal);
        private readonly Dictionary<ActiveTarget, RectInt> previousProceduralBounds =
            new Dictionary<ActiveTarget, RectInt>();
        private readonly Dictionary<ActiveTarget, RectInt> currentProceduralBounds =
            new Dictionary<ActiveTarget, RectInt>();
        private RenderTexture disabledStrokeCoverage;
        private bool strokeStarted;
        private bool captureActiveStrokeHistory = true;
        private long activeCoverageBytes;
        private TexturePaintBrushContextV2 activeBrushContext;
        private Texture2D activeStampTexture;
        private string activeHistoryName;
        private const int RasterBatchSize = 64;
        private const int RasterTileSize = 128;
        private int sampleBatchDepth;
        private bool flushingSampleBatch;
        private bool deferInteractiveRefresh;
        private readonly List<StrokeDispatchSample> queuedInteractiveSamples =
            new List<StrokeDispatchSample>();
        private readonly Dictionary<ActiveTarget, DirtyTargetState> deferredDirtyTargets =
            new Dictionary<ActiveTarget, DirtyTargetState>();
        private readonly Dictionary<TextureSet, PreviewDirtyState> deferredPreviewStates =
            new Dictionary<TextureSet, PreviewDirtyState>();
        private readonly HashSet<HistoryTileKey> capturedHistoryTiles =
            new HashSet<HistoryTileKey>();
        private readonly Dictionary<BatchGroupKey, List<StrokeDispatchSample>> batchGroups =
            new Dictionary<BatchGroupKey, List<StrokeDispatchSample>>();
        private readonly Stack<List<StrokeDispatchSample>> batchGroupPool =
            new Stack<List<StrokeDispatchSample>>();
        private readonly Dictionary<Vector2Int, BatchTile> batchTiles =
            new Dictionary<Vector2Int, BatchTile>();
        private readonly Stack<BatchTile> batchTilePool = new Stack<BatchTile>();
        private GPUBatchStamp[] batchStampScratch = new GPUBatchStamp[RasterBatchSize];
        private ComputeBuffer batchStampBuffer;

        private sealed class ActiveTarget
        {
            public TextureSet textures;
            public TexturePaintChannel channel;
            public EditableTextureTarget target;
            public Texture paintSource;
            public TexturePaintBrushSource sourceKind = TexturePaintBrushSource.Color;
            public Color color = Color.white;
            public float contribution = 1f;
            public bool isLayerMask;
            public float maskBaseValue = 1f;
            public readonly Dictionary<Vector2Int, CoverageTile> coverageTiles = new Dictionary<Vector2Int, CoverageTile>();
            public readonly Dictionary<int, Color> cpuStrokeBase = new Dictionary<int, Color>();
            public readonly Dictionary<int, float> cpuStrokeCoverage = new Dictionary<int, float>();
        }

        private sealed class CoverageTile
        {
            public RectInt rect;
            public RenderTexture strokeBase;
            public RenderTexture coverage;
        }

        private sealed class DirtyTargetState
        {
            public readonly Dictionary<Vector2Int, RectInt> tiles =
                new Dictionary<Vector2Int, RectInt>();
        }

        private sealed class PreviewDirtyState
        {
            public bool fullRefresh;
            public readonly Dictionary<Vector2Int, RectInt> tiles =
                new Dictionary<Vector2Int, RectInt>();
        }

        private sealed class BatchTile
        {
            public RectInt rect;
            public readonly List<int> sampleIndices = new List<int>(RasterBatchSize);
        }

        private readonly struct BatchGroupKey : IEquatable<BatchGroupKey>
        {
            private readonly int surfaceIndex;
            private readonly int uvIsland;
            private readonly string slotName;
            private readonly bool projected;

            public BatchGroupKey(StrokeDispatchSample dispatch, bool directUV)
            {
                surfaceIndex = dispatch.sample.surfaceIndex;
                projected = dispatch.projection.restrictToTriangle || directUV;
                uvIsland = projected ? -1 : dispatch.sample.uvIsland;
                slotName = projected ? string.Empty : dispatch.sample.slotName ?? string.Empty;
            }

            public bool Equals(BatchGroupKey other)
                => surfaceIndex == other.surfaceIndex && uvIsland == other.uvIsland &&
                    projected == other.projected && string.Equals(slotName, other.slotName,
                        StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is BatchGroupKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = surfaceIndex;
                    hash = hash * 397 ^ uvIsland;
                    hash = hash * 397 ^ (projected ? 1 : 0);
                    return hash * 397 ^ StringComparer.Ordinal.GetHashCode(slotName);
                }
            }
        }

        private readonly struct HistoryTileKey : IEquatable<HistoryTileKey>
        {
            private readonly EditableTextureTarget target;
            private readonly int x;
            private readonly int y;

            public HistoryTileKey(EditableTextureTarget target, int x, int y)
            {
                this.target = target;
                this.x = x;
                this.y = y;
            }

            public bool Equals(HistoryTileKey other)
                => ReferenceEquals(target, other.target) && x == other.x && y == other.y;

            public override bool Equals(object obj)
                => obj is HistoryTileKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = target != null ? RuntimeHelpers.GetHashCode(target) : 0;
                    hash = hash * 397 ^ x;
                    return hash * 397 ^ y;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GPUBatchStamp
        {
            public Vector2 center;
            public Vector4 uvToBrush;
            public float rotation;
            public float flow;
            public Vector4 color;
            public Vector2 footprintScale;
            public Vector2 sourceUVScale;
            public Vector2 sourceUVOffset;
            public Vector2 triangleUV0;
            public Vector2 triangleUV1;
            public Vector2 triangleUV2;
            public int triangleBoundaryMask;
            public int restrictToTriangle;
        }

        private sealed class StrokeRecordBinding
        {
            public TextureSet set;
            public TexturePaintLayer layer;
            public TexturePaintStrokeRecord record;
        }

        public StrokeHistory History => history;
        public bool IsPainting => strokeStarted;
        public string LastStrokeError { get; private set; }
        public event Action<TextureSet, TexturePaintChannel> TextureChanged;
        public TexturePaintPerformanceMetrics Performance { get; } = new TexturePaintPerformanceMetrics();
        public long CoverageMemoryBudgetBytes { get; set; } = 128L * 1024L * 1024L;
        public long ActiveCoverageMemoryBytes => activeCoverageBytes;

        public PaintingEngine(ComputeShader strokeShader, ComputeShader blurShader, ComputeShader normalShader,
            Shader ribbonShader = null)
        {
            this.strokeShader = strokeShader;
            this.blurShader = blurShader;
            this.normalShader = normalShader;
            ribbonShader ??= Shader.Find("Hidden/UMA/TexturePaint/RibbonProjection");
            if (ribbonShader != null)
                ribbonMaterial = new Material(ribbonShader)
                {
                    name = "Texture Paint Ribbon Projection",
                    hideFlags = HideFlags.HideAndDontSave
                };
        }

        /// <summary>
        /// Defers samples submitted by one editor input event so their raster work, composites,
        /// and preview updates can be coalesced without changing contact discovery or sample order.
        /// Calls may be nested; only the outermost matching EndSampleBatch flushes the queue.
        /// </summary>
        public void BeginSampleBatch()
        {
            sampleBatchDepth++;
        }

        public bool EndSampleBatch()
        {
            if (sampleBatchDepth <= 0) return false;
            sampleBatchDepth--;
            if (sampleBatchDepth > 0) return false;
            if (queuedInteractiveSamples.Count == 0) return false;

            bool changed = false;
            flushingSampleBatch = true;
            deferInteractiveRefresh = true;
            try
            {
                changed = ApplySamples(queuedInteractiveSamples);
                FlushDeferredRefresh();
            }
            finally
            {
                queuedInteractiveSamples.Clear();
                ClearDeferredRefreshState(false);
                deferInteractiveRefresh = false;
                flushingSampleBatch = false;
            }
            return changed;
        }

        public bool BeginStroke(StrokeContext context, TexturePaintSourceMode mode)
            => BeginStroke(context, mode, null);

        public bool BeginStroke(StrokeContext context, TexturePaintSourceMode mode, IReadOnlyList<TextureSet> textureSets)
        {
            EndStroke(false);
            capturedHistoryTiles.Clear();
            LastStrokeError = null;
            if (context?.textures == null || context.brush == null) return false;
            if (context.editLayerMask && context.tool == TexturePaintTool.NormalTouchup) return false;
            bool consumesPaintSource = context.tool == TexturePaintTool.Paint || context.tool == TexturePaintTool.Plugin;
            if (consumesPaintSource && context.channelSources.Count == 0 &&
                context.paintSource == TexturePaintBrushSource.Texture &&
                context.sourceTexture == null && context.sourceSprite == null) return false;
            if (consumesPaintSource && context.channelSources.Count == 0 &&
                context.paintSource == TexturePaintBrushSource.Overlay && context.sourceOverlay == null &&
                context.sourceOverlaysBySurfaceId.Count == 0) return false;
            TexturePaintStrokeDiagnostics diagnostics = Performance.StrokeDiagnostics;
            diagnostics.TryBegin(context.tool, context.channel, context.mirrorEnabled,
                context.directUV, textureSets?.Count ?? 1, Performance);
            using TexturePaintStrokeDiagnosticScope beginDiagnostics = diagnostics.Measure(
                TexturePaintStrokeDiagnosticPhase.BeginStroke);
            // A selected authored layer always owns its strokes. Older documents and the legacy
            // inspector can retain SourceTexture after a layer is created or selected; honoring
            // that stale value writes through to the base texture, so deleting the apparently
            // painted layer cannot remove the marks. Direct base painting remains available only
            // when there is no active Paint/Spline layer.
            mode = ResolveDestinationMode(mode, context.textures, textureSets);
            if (context.editLayerMask) mode = TexturePaintSourceMode.SourceOverlay;
            activeContext = context;
            activeHistoryName = "Texture Paint " + context.tool;
            activeStampTexture = context.brush.ResolvedStampTexture;
            if (textureSets != null && textureSets.Count > 0)
            {
                for (int i = 0; i < textureSets.Count; i++)
                    if (textureSets[i] != null) BuildActiveTargets(context, textureSets[i], mode);
            }
            else BuildActiveTargets(context, context.textures, mode);
            diagnostics.SetActiveTargets(activeTargets.Count);
            if (activeTargets.Count == 0)
                return FailStrokeStart("The stroke has no writable paint targets for the selected channel and source.");
            if (textureSets != null)
            {
                for (int i = 0; i < textureSets.Count; i++)
                {
                    TextureSet required = textureSets[i];
                    if (required == null || ContainsTextureSet(required)) continue;
                    string targetName = !string.IsNullOrEmpty(required.Name)
                        ? required.Name
                        : !string.IsNullOrEmpty(required.persistentId)
                            ? required.persistentId
                            : "unnamed surface";
                    if (!string.IsNullOrEmpty(required.persistentId) &&
                        !string.Equals(targetName, required.persistentId, StringComparison.Ordinal))
                        targetName += $" [{required.persistentId}]";
                    return FailStrokeStart($"Paint target '{targetName}' has no writable raster target. " +
                        "Its selected channel may be missing, locked, set to zero contribution, or use an invalid source.");
                }
            }
            captureActiveStrokeHistory = !context.derivedLayerRaster;
            if (context.replaceHistoryGroup && !string.IsNullOrEmpty(context.historyGroupKey))
                PrepareProceduralReplacement(context, textureSets);
            CreateStrokeRecords(mode);
            if (captureActiveStrokeHistory) history.BeginGroup(context.historyGroupKey);
            if (context.brushPlugin != null)
            {
                if (context.pluginHost == null)
                {
                    history.CancelPending(); activeTargets.Clear(); activeStampTexture = null;
                    activeContext = null;
                    activeHistoryName = null;
                    diagnostics.Abort("brush plugin host unavailable", Performance);
                    return false;
                }
                try
                {
                    activeBrushContext = context.pluginHost.BeginBrush(context.brushPlugin, context.textures.persistentId,
                        context.channel, context.brushPluginParameters, context.cancellationToken);
                }
                catch
                {
                    history.CancelPending(); activeTargets.Clear(); activeStampTexture = null;
                    activeContext = null;
                    activeHistoryName = null;
                    diagnostics.Abort("brush plugin initialization failed", Performance);
                    return false;
                }
            }
            for (int i = 0; i < activeTargets.Count; i++)
            {
                TextureLayerCompositor compositor = activeTargets[i].textures?.compositor;
                if (compositor != null && activeCompositors.Add(compositor))
                    compositor.BeginInteractiveEdit();
            }
            strokeStarted = true;
            return true;
        }

        internal static TexturePaintSourceMode ResolveDestinationMode(TexturePaintSourceMode requested,
            TextureSet primary, IReadOnlyList<TextureSet> textureSets = null)
        {
            if (requested != TexturePaintSourceMode.SourceTexture) return requested;
            if (HasWritableActiveLayer(primary)) return TexturePaintSourceMode.SourceOverlay;
            if (textureSets == null) return requested;
            for (int i = 0; i < textureSets.Count; i++)
                if (HasWritableActiveLayer(textureSets[i])) return TexturePaintSourceMode.SourceOverlay;
            return requested;
        }

        private static bool HasWritableActiveLayer(TextureSet set)
        {
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return false;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            return layer != null && (layer.kind == TexturePaintLayerKind.Paint || layer.IsSplineLayer);
        }

        public bool ApplySample(StrokeSample sample, float uvRadius)
            => ApplySample(sample, uvRadius, default);

        /// <summary>
        /// Fills one mesh polygon or its complete UV island into the current material channel or
        /// editable layer mask. This is a paint operation: it participates in GPU history and does
        /// not create a persistent structural mask object.
        /// </summary>
        public bool FillActiveGeometry(StrokeSample sample, bool wholeUVIsland)
        {
            if (!strokeStarted || activeContext == null || strokeShader == null ||
                !strokeShader.HasKernel("CSFillMasked")) return false;
            int kernel = strokeShader.FindKernel("CSFillMasked");
            if (!strokeShader.IsSupported(kernel)) return false;
            bool changed = false;
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget active = activeTargets[i];
                if (active.textures?.surface == null ||
                    active.textures.surface.index != sample.surfaceIndex) continue;
                int island = wholeUVIsland && active.textures.surface.triangleIslands != null &&
                    (uint)sample.triangleIndex < (uint)active.textures.surface.triangleIslands.Length
                        ? active.textures.surface.triangleIslands[sample.triangleIndex] : -1;
                List<TexturePaintGeometrySelector> selectors = new List<TexturePaintGeometrySelector>();
                if (activeContext.geometrySelection?.Selectors != null)
                    for (int selectorIndex = 0;
                        selectorIndex < activeContext.geometrySelection.Selectors.Count; selectorIndex++)
                        if (activeContext.geometrySelection.Selectors[selectorIndex] != null)
                            selectors.Add(activeContext.geometrySelection.Selectors[selectorIndex]);
                TexturePaintGeometrySelector selector = new TexturePaintGeometrySelector
                {
                    name = wholeUVIsland ? "UV Island Fill" : "Polygon Fill",
                    enabled = true,
                    kind = wholeUVIsland ? TexturePaintGeometrySelectorKind.UVIsland :
                        TexturePaintGeometrySelectorKind.Polygon,
                    surfaceIndex = sample.surfaceIndex
                };
                if (wholeUVIsland) selector.uvIslandIndices.Add(island);
                else selector.triangleIndices.Add(sample.triangleIndex);
                selectors.Add(selector);
                Texture2D geometry = TexturePaintGeometryMask.Build(active.textures.surface,
                    active.target.Width, active.target.Height, sample.slotName, -1,
                    new TexturePaintGeometrySelection(selectors));
                try
                {
                    RectInt rect = new RectInt(0, 0, active.target.Width, active.target.Height);
                    IncludeHistoryTiles(wholeUVIsland ? "Fill UV Island" : "Fill Polygon",
                        active.target, rect, Performance.StrokeDiagnostics);
                    strokeShader.SetInts("_TextureSize", active.target.Width, active.target.Height);
                    strokeShader.SetFloat("_Strength", activeContext.strength * active.contribution);
                    strokeShader.SetInt("_BlendMode", (int)activeContext.brush.blendMode);
                    strokeShader.SetInt("_MaskMode", active.isLayerMask ? 1 : 0);
                    strokeShader.SetFloat("_MaskEraseValue", active.maskBaseValue);
                    strokeShader.SetVector("_PaintColor", active.color);
                    strokeShader.SetInt("_PaintSourceKind", (int)active.sourceKind);
                    strokeShader.SetTexture(kernel, "_PaintSource",
                        active.paintSource != null ? active.paintSource : Texture2D.whiteTexture);
                    strokeShader.SetTexture(kernel, "_GeometryMask", geometry);
                    strokeShader.SetTexture(kernel, "_Source", active.target.Front);
                    strokeShader.SetTexture(kernel, "_Destination", active.target.Back);
                    strokeShader.Dispatch(kernel, Mathf.CeilToInt(active.target.Width / 16f),
                        Mathf.CeilToInt(active.target.Height / 16f), 1);
                    active.target.SwapAndSynchronize(rect);
                    CompositeChangedTarget(active, rect);
                    TextureChanged?.Invoke(active.textures,
                        active.isLayerMask ? TexturePaintChannel.Custom : active.channel);
                    changed = true;
                }
                finally { Destroy(geometry); }
            }
            if (!changed) return false;
            if (activeStrokeRecords.TryGetValue(activeContext.textures,
                out TexturePaintStrokeRecord record)) record.samples.Add(sample);
            HashSet<TextureSet> sets = new HashSet<TextureSet>();
            for (int i = 0; i < activeTargets.Count; i++)
                if (activeTargets[i].textures != null) sets.Add(activeTargets[i].textures);
            foreach (TextureSet set in sets) set.BindPreviewTextures(false);
            return true;
        }

        public bool ApplySample(StrokeSample sample, float uvRadius, BrushProjection projection)
        {
            if (!strokeStarted || activeTargets.Count == 0) return false;
            if (activeContext.cancellationToken.IsCancellationRequested) { EndStroke(false); return false; }
            if (sampleBatchDepth > 0 && !flushingSampleBatch)
            {
                queuedInteractiveSamples.Add(new StrokeDispatchSample(sample, uvRadius, projection));
                return true;
            }
            long previewStarted = Stopwatch.GetTimestamp();
            TexturePaintStrokeDiagnostics diagnostics = Performance.StrokeDiagnostics;
            TextureSet sampleTextures;
            float radius;
            Vector4 uvToBrush;
            float boundsRadius;
            using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.PaintPreparation))
            {
                sampleTextures = null;
                for (int i = 0; i < activeTargets.Count; i++)
                {
                    if (activeTargets[i].textures?.surface?.index != sample.surfaceIndex) continue;
                    sampleTextures = activeTargets[i].textures;
                    break;
                }
                if (sampleTextures == null) return false;
                diagnostics.RecordPaintOperation();
                if (activeContext.brushPlugin != null)
                {
                    var pluginSample = new TexturePaintBrushSampleV2
                    {
                        color = sample.hasColor ? sample.color : activeContext.color,
                        opacityMultiplier = 1f,
                        sizeMultiplier = 1f
                    };
                    activeContext.pluginHost.EvaluateBrush(activeContext.brushPlugin, activeBrushContext, sample, ref pluginSample);
                    if (pluginSample.skip) return false;
                    sample.color = pluginSample.color; sample.hasColor = true;
                    sample.flowMultiplier *= Mathf.Max(0f, pluginSample.opacityMultiplier);
                    float sizeMultiplier = Mathf.Max(0.0001f, pluginSample.sizeMultiplier);
                    uvRadius *= sizeMultiplier;
                    if (projection.valid)
                    {
                        projection.uvBoundsRadius *= sizeMultiplier;
                        projection.uvToBrush /= sizeMultiplier;
                    }
                    sample.rotation += pluginSample.rotationOffset;
                }
                int uvIsland = -1;
                ReconstructedSurface surface = sampleTextures.surface;
                if (sample.triangleIndex >= 0 && surface?.triangleIslands != null && sample.triangleIndex < surface.triangleIslands.Length)
                    uvIsland = surface.triangleIslands[sample.triangleIndex];
                if (activeContext.geometrySelection != null &&
                    !activeContext.geometrySelection.AllowsStructural(sample.surfaceIndex, sample.triangleIndex, uvIsland,
                    surface, sample.uv, sample.worldPosition)) return false;
                if (activeStrokeRecords.TryGetValue(sampleTextures, out TexturePaintStrokeRecord strokeRecord))
                    strokeRecord.samples.Add(sample);

                radius = Mathf.Max(0.00001f, projection.valid ? projection.uvBoundsRadius : uvRadius);
                if (activeContext.brush.shape == BrushPreset.Shape.Square) radius *= 1.41421356f;
                uvToBrush = projection.valid
                    ? projection.uvToBrush
                    : new Vector4(1f / radius, 0f, 0f, 1f / radius);
                boundsRadius = radius * FootprintRadiusScale(sample);
            }
            bool changed = false;
            bool maskChanged = false;
            RectInt changedRect = default;
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget active = activeTargets[i];
                if (active.textures != sampleTextures) continue;
                int filterHalo = activeContext.tool == TexturePaintTool.Blur || activeContext.tool == TexturePaintTool.NormalTouchup ? 2 : 1;
                RectInt rect = TexturePaintMath.BrushPixelRect(sample.uv, boundsRadius, active.target.Width, active.target.Height, filterHalo);
                if (projection.restrictToTriangle)
                    rect = Intersect(rect, TrianglePixelRect(projection, active.target.Width, active.target.Height,
                        Mathf.Max(filterHalo, Mathf.CeilToInt(TriangleBoundaryPaddingTexels))));
                if (rect.width == 0 || rect.height == 0) continue;
                diagnostics.RecordRasterTargetVisit();
                IncludeHistoryTiles(activeHistoryName, active.target, rect,
                    diagnostics);
                Texture2D geometryMask = RequiresGeometryMask(activeContext.geometrySelection,
                    projection.restrictToTriangle, activeContext.directUV)
                    ? GetGeometryMask(active, sample)
                    : null;
                using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.RasterSubmit))
                {
                    bool dispatched = SystemInfo.supportsComputeShaders && DispatchGPU(active, sample,
                        projection, uvToBrush, rect, geometryMask);
                    if (!dispatched)
                    {
                        Performance.cpuFallbacks++;
                        ApplyCPU(active, sample, projection, uvToBrush, rect, geometryMask);
                    }
                }
                RecordRasterChange(active, rect);
                changedRect = Union(changedRect, rect);
                maskChanged |= active.isLayerMask;
                changed = true;
            }
            if (!changed)
            {
                if (!deferInteractiveRefresh)
                    Performance.RecordPreview(ElapsedMilliseconds(previewStarted));
                return false;
            }
            if (!deferInteractiveRefresh)
            {
                using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.PreviewBinding))
                {
                    if (maskChanged) sampleTextures.BindPreviewTextures(false);
                    else sampleTextures.BindPreviewTextures(false, changedRect);
                }
            }
            if (!deferInteractiveRefresh)
                Performance.RecordPreview(ElapsedMilliseconds(previewStarted));
            return true;
        }

        public bool ApplySamples(IReadOnlyList<StrokeDispatchSample> samples)
        {
            if (samples == null || samples.Count == 0) return false;
            if (!strokeStarted || activeContext == null) return false;
            if (activeContext.cancellationToken.IsCancellationRequested)
            {
                EndStroke(false);
                return false;
            }
            if (!CanBatchCurrentTool() || !SystemInfo.supportsComputeShaders || samples.Count == 1)
            {
                bool changed = false;
                for (int i = 0; i < samples.Count; i++)
                    changed |= ApplySample(samples[i].sample, samples[i].uvRadius, samples[i].projection);
                return changed;
            }

            try
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    StrokeDispatchSample dispatch = samples[i];
                    StrokeSample sample = dispatch.sample;
                    if (dispatch.projection.restrictToTriangle && !AllowsBatchSample(sample))
                        continue;
                    // A restricted projector carries its polygon ownership in the stamp itself.
                    // Direct UV paint is likewise unrestricted by mesh islands. Other projected
                    // samples retain island/slot grouping because they share one geometry mask.
                    var key = new BatchGroupKey(dispatch, activeContext.directUV);
                    if (!batchGroups.TryGetValue(key,
                        out List<StrokeDispatchSample> group))
                    {
                        group = batchGroupPool.Count > 0
                            ? batchGroupPool.Pop()
                            : new List<StrokeDispatchSample>(RasterBatchSize);
                        batchGroups.Add(key, group);
                    }
                    group.Add(dispatch);
                }
                bool any = false;
                foreach (List<StrokeDispatchSample> group in batchGroups.Values)
                    for (int offset = 0; offset < group.Count; offset += RasterBatchSize)
                        any |= ApplyBatchGroup(group, offset,
                            Mathf.Min(RasterBatchSize, group.Count - offset));
                return any;
            }
            finally
            {
                foreach (List<StrokeDispatchSample> group in batchGroups.Values)
                {
                    group.Clear();
                    batchGroupPool.Push(group);
                }
                batchGroups.Clear();
            }
        }

        private bool AllowsBatchSample(StrokeSample sample)
        {
            TextureSet textures = null;
            for (int i = 0; i < activeTargets.Count; i++)
                if (activeTargets[i].textures?.surface?.index == sample.surfaceIndex)
                {
                    textures = activeTargets[i].textures;
                    break;
                }
            if (textures == null) return false;
            ReconstructedSurface surface = textures.surface;
            int uvIsland = sample.uvIsland;
            if (uvIsland < 0 && sample.triangleIndex >= 0 &&
                surface?.triangleIslands != null &&
                sample.triangleIndex < surface.triangleIslands.Length)
                uvIsland = surface.triangleIslands[sample.triangleIndex];
            return activeContext.geometrySelection == null ||
                activeContext.geometrySelection.AllowsStructural(sample.surfaceIndex,
                    sample.triangleIndex, uvIsland, surface, sample.uv,
                    sample.worldPosition);
        }

        /// <summary>
        /// Projects a continuous world-space ribbon by rasterizing each destination surface in UV
        /// space. The shader evaluates shared ribbon quads at every surface fragment, so topology,
        /// UV seams, slots, UDIM tiles, and dense polygons do not split the source into stamps.
        /// </summary>
        public bool ApplyRibbon(IReadOnlyList<TexturePaintRibbonSegment> segments,
            IReadOnlyList<StrokeSample> centerlineSamples, bool sourceAlongY, bool reverseSourceAxis,
            bool closed = false, bool directUV = false)
        {
            if (!strokeStarted || activeContext == null || ribbonMaterial == null ||
                !ribbonMaterial.shader.isSupported || segments == null || segments.Count == 0 ||
                activeContext.tool != TexturePaintTool.Paint) return false;
            directUV |= activeContext.directUV;
            long previewStarted = Stopwatch.GetTimestamp();

            TexturePaintRibbonSegment[] data = new TexturePaintRibbonSegment[segments.Count];
            float minimumAlong = float.PositiveInfinity;
            float maximumAlong = float.NegativeInfinity;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = segments[i];
                minimumAlong = Mathf.Min(minimumAlong, data[i].leftStartAlong.w, data[i].leftEndAlong.w);
                maximumAlong = Mathf.Max(maximumAlong, data[i].leftStartAlong.w, data[i].leftEndAlong.w);
            }
            using ComputeBuffer segmentBuffer = new ComputeBuffer(data.Length,
                Marshal.SizeOf<TexturePaintRibbonSegment>(), ComputeBufferType.Structured);
            segmentBuffer.SetData(data);

            foreach (KeyValuePair<TextureSet, TexturePaintStrokeRecord> pair in activeStrokeRecords)
            {
                bool added = false;
                if (centerlineSamples != null)
                    for (int i = 0; i < centerlineSamples.Count; i++)
                    {
                        StrokeSample sample = centerlineSamples[i];
                        if (pair.Key?.surface != null && sample.surfaceIndex != pair.Key.surface.index) continue;
                        pair.Value.samples.Add(sample);
                        added = true;
                    }
                if (!added && centerlineSamples != null && centerlineSamples.Count > 0)
                    pair.Value.samples.Add(centerlineSamples[0]);
            }

            bool changed = false;
            HashSet<TextureSet> changedSets = new HashSet<TextureSet>();
            for (int targetIndex = 0; targetIndex < activeTargets.Count; targetIndex++)
            {
                ActiveTarget active = activeTargets[targetIndex];
                Mesh mesh = directUV ? GetDirectUVRibbonMesh() : active.textures?.surface?.mesh;
                if (mesh == null || active.target?.Front == null || active.target.Back == null) continue;
                RectInt rect = new RectInt(0, 0, active.target.Width, active.target.Height);
                IncludeHistoryTiles("Texture Paint Ribbon", active.target, rect,
                    Performance.StrokeDiagnostics);
                active.target.CopyFrontToBack();

                StrokeSample unrestricted = new StrokeSample
                {
                    surfaceIndex = active.textures.surface.index,
                    uvIsland = -1,
                    slotName = string.Empty
                };
                // Ribbon projection already rasterizes the destination mesh in UV space. An
                // unrestricted CPU geometry mask merely repeats that coverage at full texture
                // resolution, so build one only when a structural selector actually clips it.
                Texture2D geometryMask = !directUV && HasGeometryRestrictions(activeContext.geometrySelection)
                    ? GetGeometryMask(active, unrestricted) : null;
                ribbonProperties.Clear();
                ribbonProperties.SetBuffer("_RibbonSegments", segmentBuffer);
                ribbonProperties.SetInt("_RibbonSegmentCount", data.Length);
                ribbonProperties.SetTexture("_DestinationTexture", active.target.Front);
                ribbonProperties.SetTexture("_PaintSource",
                    active.paintSource != null ? active.paintSource : Texture2D.whiteTexture);
                ribbonProperties.SetTexture("_BeginningSource",
                    activeContext.ribbonBeginningTexture != null
                        ? activeContext.ribbonBeginningTexture : Texture2D.whiteTexture);
                ribbonProperties.SetTexture("_EndSource",
                    activeContext.ribbonEndTexture != null
                        ? activeContext.ribbonEndTexture : Texture2D.whiteTexture);
                ribbonProperties.SetInt("_HasBeginningSource",
                    !closed && activeContext.ribbonBeginningTexture != null ? 1 : 0);
                ribbonProperties.SetInt("_HasEndSource",
                    !closed && activeContext.ribbonEndTexture != null ? 1 : 0);
                ribbonProperties.SetFloat("_RibbonMinimumAlong", minimumAlong);
                ribbonProperties.SetFloat("_RibbonMaximumAlong", maximumAlong);
                ribbonProperties.SetTexture("_GeometryMask",
                    geometryMask != null ? geometryMask : Texture2D.whiteTexture);
                ribbonProperties.SetColor("_PaintColor", active.color);
                ribbonProperties.SetFloat("_Strength", activeContext.strength * active.contribution);
                ribbonProperties.SetFloat("_BrushFlow", activeContext.brush.flow);
                ribbonProperties.SetFloat("_ProjectionDepth",
                    Mathf.Max(0.0001f, activeContext.brush.size * activeContext.projectionDepth));
                ribbonProperties.SetFloat("_NormalCosLimit",
                    Mathf.Cos(Mathf.Clamp(activeContext.normalAngleLimit, 0f, 180f) * Mathf.Deg2Rad));
                ribbonProperties.SetInt("_PaintBackfaces", activeContext.paintBackfaces ? 1 : 0);
                ribbonProperties.SetInt("_PressureAffectsFlow", activeContext.pressureAffectsFlow ? 1 : 0);
                ribbonProperties.SetInt("_PaintSourceKind", (int)active.sourceKind);
                ribbonProperties.SetInt("_BlendMode", (int)activeContext.brush.blendMode);
                ribbonProperties.SetInt("_VectorNormal", active.channel == TexturePaintChannel.Normal ? 1 : 0);
                ribbonProperties.SetInt("_SourceAlongY", sourceAlongY ? 1 : 0);
                ribbonProperties.SetInt("_ReverseSourceAxis", reverseSourceAxis ? 1 : 0);
                ribbonProperties.SetInt("_RibbonClosed", closed ? 1 : 0);
                ribbonProperties.SetInt("_EdgeFadeEnabled", activeContext.ribbonEdgeFadeEnabled ? 1 : 0);
                ribbonProperties.SetFloat("_EdgeFadeStart", Mathf.Clamp01(activeContext.ribbonEdgeFadeStart));
                ribbonProperties.SetFloat("_EdgeFadeSize", Mathf.Clamp01(activeContext.ribbonEdgeFadeSize));
                Matrix4x4 localToWorld = !directUV && active.textures.surface.gameObject != null
                    ? active.textures.surface.gameObject.transform.localToWorldMatrix
                    : Matrix4x4.identity;
                var effectPasses = new List<TexturePaintLayerEffectSettings>();
                TexturePaintLayerEffects ribbonEffects = activeContext.ribbonEffects;
                if (ribbonEffects != null)
                {
                    ribbonEffects.Normalize();
                    for (int effectIndex = 0; effectIndex < ribbonEffects.Stack.Count; effectIndex++)
                    {
                        TexturePaintLayerEffectSettings effect = ribbonEffects.Stack[effectIndex];
                        if (!TexturePaintLayerEffects.EnabledFor(effect, active.channel) ||
                            effect.kind == TexturePaintLayerEffectKind.EdgeFade ||
                            TexturePaintLayerEffects.IsCompositeOnlyEffect(effect.kind)) continue;
                        effectPasses.Add(effect);
                    }
                }
                // The first effect shares the paint pass. Outer effects and strokes occupy only
                // exterior pixels, while inner effects, bevels, and stitches are evaluated after
                // the paint contribution in the shader, so this preserves the first stack item's
                // semantics without repeating the O(texture-pixels * ribbon-segments) projection.
                // Additional stack items retain their own ordered effect-only passes.
                int renderPassCount = Mathf.Max(1, effectPasses.Count);
                for (int pass = 0; pass < renderPassCount; pass++)
                {
                    if (pass > 0) active.target.CopyFrontToBack(rect);
                    ribbonProperties.SetTexture("_DestinationTexture", active.target.Front);
                    ribbonProperties.SetInt("_RibbonPaintEnabled", pass == 0 ? 1 : 0);
                    DisableRibbonEffects(ribbonProperties);
                    if (pass < effectPasses.Count)
                        BindSingleRibbonEffect(ribbonProperties, effectPasses[pass],
                        active.channel);
                    using CommandBuffer command = new CommandBuffer
                    { name = pass == 0 ? "Project Texture Paint Ribbon" : "Project Ribbon Layer Effect" };
                    command.SetRenderTarget(active.target.Back);
                    int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                    for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                        command.DrawMesh(mesh, localToWorld, ribbonMaterial, subMesh, 0,
                            ribbonProperties);
                    Graphics.ExecuteCommandBuffer(command);
                    active.target.Swap();
                }
                active.target.CopyFrontToBack(rect);
                CompositeChangedTarget(active, rect);
                Performance.copiedPixels += (long)rect.width * rect.height * (renderPassCount + 1L);
                Performance.composedPixels += (long)rect.width * rect.height;
                TextureChanged?.Invoke(active.textures,
                    active.isLayerMask ? TexturePaintChannel.Custom : active.channel);
                changedSets.Add(active.textures);
                changed = true;
            }
            foreach (TextureSet set in changedSets) set.BindPreviewTextures(false);
            if (!deferInteractiveRefresh)
                Performance.RecordPreview(ElapsedMilliseconds(previewStarted));
            return changed;
        }

        private Mesh GetDirectUVRibbonMesh()
        {
            if (directUVRibbonMesh != null) return directUVRibbonMesh;
            directUVRibbonMesh = new Mesh
            {
                name = "Overlay Painter Direct UV Ribbon Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f)
                },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            directUVRibbonMesh.RecalculateBounds();
            return directUVRibbonMesh;
        }

        private void DisableRibbonEffects(MaterialPropertyBlock properties)
        {
            properties.SetInt("_StrokeEnabled", 0);
            properties.SetInt("_InnerShadowEnabled", 0);
            properties.SetInt("_OuterShadowEnabled", 0);
            properties.SetInt("_InnerGlowEnabled", 0);
            properties.SetInt("_OuterGlowEnabled", 0);
            properties.SetInt("_BevelEnabled", 0);
            properties.SetInt("_StitchEnabled", 0);
            properties.SetInt("_OuterRibbonEffectsEnabled", 0);
        }

        private void BindSingleRibbonEffect(MaterialPropertyBlock properties,
            TexturePaintLayerEffectSettings effect, TexturePaintChannel channel)
        {
            if (!TexturePaintLayerEffects.EnabledFor(effect, channel)) return;
            switch (effect.kind)
            {
                case TexturePaintLayerEffectKind.Stroke:
                    BindRibbonStrokeEffect(properties, effect, channel);
                    properties.SetInt("_OuterRibbonEffectsEnabled", 1);
                    break;
                case TexturePaintLayerEffectKind.InnerShadow:
                    BindRibbonDistanceEffect(properties, "InnerShadow", effect, channel);
                    break;
                case TexturePaintLayerEffectKind.OuterShadow:
                    BindRibbonDistanceEffect(properties, "OuterShadow", effect, channel);
                    properties.SetInt("_OuterRibbonEffectsEnabled", 1);
                    break;
                case TexturePaintLayerEffectKind.InnerGlow:
                    BindRibbonDistanceEffect(properties, "InnerGlow", effect, channel);
                    break;
                case TexturePaintLayerEffectKind.OuterGlow:
                    BindRibbonDistanceEffect(properties, "OuterGlow", effect, channel);
                    properties.SetInt("_OuterRibbonEffectsEnabled", 1);
                    break;
                case TexturePaintLayerEffectKind.BevelEdge:
                    properties.SetInt("_BevelEnabled", 1);
                    properties.SetInt("_BevelSide", (int)effect.ribbonSide);
                    properties.SetColor("_BevelLightColor", effect.color);
                    properties.SetColor("_BevelDarkColor", effect.secondaryColor);
                    properties.SetFloat("_BevelWidth", effect.width);
                    properties.SetFloat("_BevelSmoothness", effect.smoothness);
                    properties.SetFloat("_BevelLevel", effect.level);
                    properties.SetInt("_BevelLeftTone", (int)effect.ribbonLeftTone);
                    properties.SetInt("_BevelRightTone", (int)effect.ribbonRightTone);
                    properties.SetFloat("_BevelLeftOffset", effect.ribbonLeftOffset);
                    properties.SetFloat("_BevelRightOffset", effect.ribbonRightOffset);
                    break;
                case TexturePaintLayerEffectKind.ProceduralStitch:
                    properties.SetInt("_StitchEnabled", 1);
                    properties.SetInt("_StitchSide", (int)effect.ribbonSide);
                    properties.SetColor("_StitchColor", effect.color);
                    properties.SetInt("_StitchRows",
                        effect.stitchRows == TexturePaintRibbonStitchRows.Double ? 2 : 1);
                    properties.SetFloat("_StitchThreadSize", effect.stitchThreadSize);
                    properties.SetFloat("_StitchLength", effect.stitchLength);
                    properties.SetFloat("_StitchInset", effect.stitchInset);
                    properties.SetFloat("_StitchLevel", effect.level);
                    break;
            }
        }

        private static void BindRibbonStrokeEffect(MaterialPropertyBlock properties,
            TexturePaintLayerEffectSettings effect, TexturePaintChannel channel)
        {
            bool enabled = TexturePaintLayerEffects.EnabledFor(effect, channel);
            properties.SetInt("_StrokeEnabled", enabled ? 1 : 0);
            properties.SetColor("_StrokeColor", effect?.color ?? Color.clear);
            properties.SetVector("_StrokeParameters", new Vector4(
                effect?.width ?? 2f,
                effect?.offset.x ?? 0f,
                effect?.smoothness ?? 0.25f,
                effect?.level ?? 1f));
        }

        private void BindRibbonDistanceEffect(MaterialPropertyBlock properties, string prefix,
            TexturePaintLayerEffectSettings effect, TexturePaintChannel channel)
        {
            bool enabled = TexturePaintLayerEffects.EnabledFor(effect, channel);
            properties.SetInt("_" + prefix + "Enabled", enabled ? 1 : 0);
            properties.SetInt("_" + prefix + "Side",
                (int)(effect?.ribbonSide ?? TexturePaintRibbonSide.Both));
            properties.SetColor("_" + prefix + "Color", effect?.color ?? Color.clear);
            properties.SetFloat("_" + prefix + "Width", effect?.width ?? 8f);
            properties.SetFloat("_" + prefix + "Offset", effect?.offset.x ?? 0f);
            properties.SetFloat("_" + prefix + "Level", effect?.level ?? 1f);
            properties.SetTexture("_" + prefix + "Curve", GetRibbonCurveTexture(effect?.curve));
        }

        private Texture2D GetRibbonCurveTexture(AnimationCurve curve)
        {
            curve ??= TexturePaintLayerEffectSettings.DefaultCurve();
            int signature = RibbonCurveSignature(curve);
            if (ribbonCurveTextures.TryGetValue(signature, out Texture2D cached) && cached != null)
                return cached;
            Texture2D texture = new Texture2D(256, 1, TextureFormat.RFloat, false, true)
            {
                name = "Overlay Painter Ribbon Effect Curve",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(Mathf.Clamp01(curve.Evaluate(i / 255f)), 0f, 0f, 1f);
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            ribbonCurveTextures[signature] = texture;
            return texture;
        }

        private static int RibbonCurveSignature(AnimationCurve curve)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)curve.preWrapMode;
                hash = hash * 31 + (int)curve.postWrapMode;
                Keyframe[] keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    Keyframe key = keys[i];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + (int)key.weightedMode;
                }
                return hash;
            }
        }

        private bool ApplyBatchGroup(List<StrokeDispatchSample> samples, int offset, int count)
        {
            if (!strokeStarted || count <= 0) return false;
            StrokeSample first = samples[offset].sample;
            TextureSet sampleTextures = null;
            for (int i = 0; i < activeTargets.Count; i++)
                if (activeTargets[i].textures?.surface?.index == first.surfaceIndex) { sampleTextures = activeTargets[i].textures; break; }
            if (sampleTextures == null) return false;
            long previewStarted = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                StrokeSample sample = samples[offset + i].sample;
                if (activeStrokeRecords.TryGetValue(sampleTextures, out TexturePaintStrokeRecord record)) record.samples.Add(sample);
            }
            bool changed = false;
            bool maskChanged = false;
            RectInt previewRect = default;
            TexturePaintStrokeDiagnostics diagnostics = Performance.StrokeDiagnostics;
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget active = activeTargets[i];
                if (active.textures != sampleTextures) continue;
                ReleaseBatchTiles();
                try
                {
                    for (int sampleIndex = 0; sampleIndex < count; sampleIndex++)
                    {
                        StrokeDispatchSample dispatch = samples[offset + sampleIndex];
                        RectInt sampleRect = CalculateBatchSampleRect(dispatch,
                            active.target);
                        AddBatchTiles(sampleIndex, sampleRect, active.target.Width,
                            active.target.Height);
                    }
                    if (batchTiles.Count == 0) continue;
                    Texture2D geometryMask = RequiresGeometryMask(
                        activeContext.geometrySelection,
                        samples[offset].projection.restrictToTriangle,
                        activeContext.directUV)
                        ? GetGeometryMask(active, first)
                        : null;
                    foreach (BatchTile tile in batchTiles.Values)
                    {
                        diagnostics.RecordRasterTargetVisit();
                        IncludeHistoryTiles(activeHistoryName,
                            active.target, tile.rect, diagnostics);
                        int tileSampleCount = tile.sampleIndices.Count;
                        EnsureBatchStampBuffer(tileSampleCount);
                        for (int tileSampleIndex = 0;
                            tileSampleIndex < tileSampleCount; tileSampleIndex++)
                        {
                            int sampleIndex = tile.sampleIndices[tileSampleIndex];
                            batchStampScratch[tileSampleIndex] = CreateBatchStamp(
                                samples[offset + sampleIndex], active);
                        }
                        batchStampBuffer.SetData(batchStampScratch, 0, 0,
                            tileSampleCount);
                        bool batchDispatched;
                        using (diagnostics.Measure(
                            TexturePaintStrokeDiagnosticPhase.RasterSubmit))
                            batchDispatched = DispatchGPUBatch(active,
                                batchStampBuffer, tileSampleCount, tile.rect,
                                geometryMask);
                        if (!batchDispatched)
                            DispatchBatchTileSequentially(active, samples, offset,
                                tile, geometryMask, diagnostics);
                        RecordRasterChange(active, tile.rect);
                        previewRect = Union(previewRect, tile.rect);
                        maskChanged |= active.isLayerMask;
                        changed = true;
                    }
                }
                finally { ReleaseBatchTiles(); }
            }
            if (changed) diagnostics.RecordPaintOperation(count);
            if (changed)
            {
                if (!deferInteractiveRefresh)
                {
                    using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.PreviewBinding))
                    {
                        if (maskChanged) sampleTextures.BindPreviewTextures(false);
                        else sampleTextures.BindPreviewTextures(false, previewRect);
                    }
                }
            }
            if (!deferInteractiveRefresh)
                Performance.RecordPreview(ElapsedMilliseconds(previewStarted));
            return changed;
        }

        private RectInt CalculateBatchSampleRect(StrokeDispatchSample dispatch,
            EditableTextureTarget target)
        {
            float radius = Mathf.Max(0.00001f, dispatch.projection.valid
                ? dispatch.projection.uvBoundsRadius : dispatch.uvRadius);
            if (activeContext.brush.shape == BrushPreset.Shape.Square) radius *= 1.41421356f;
            radius *= FootprintRadiusScale(dispatch.sample);
            RectInt rect = TexturePaintMath.BrushPixelRect(dispatch.sample.uv, radius,
                target.Width, target.Height, 1);
            if (dispatch.projection.restrictToTriangle)
                rect = Intersect(rect, TrianglePixelRect(dispatch.projection, target.Width,
                    target.Height, Mathf.CeilToInt(TriangleBoundaryPaddingTexels)));
            return rect;
        }

        private void AddBatchTiles(int sampleIndex, RectInt rect, int width, int height)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            int minX = rect.xMin / RasterTileSize;
            int maxX = (rect.xMax - 1) / RasterTileSize;
            int minY = rect.yMin / RasterTileSize;
            int maxY = (rect.yMax - 1) / RasterTileSize;
            for (int tileY = minY; tileY <= maxY; tileY++)
            for (int tileX = minX; tileX <= maxX; tileX++)
            {
                RectInt tileBounds = Intersect(new RectInt(tileX * RasterTileSize,
                    tileY * RasterTileSize, RasterTileSize, RasterTileSize),
                    new RectInt(0, 0, width, height));
                RectInt intersection = Intersect(rect, tileBounds);
                if (intersection.width <= 0 || intersection.height <= 0) continue;
                Vector2Int key = new Vector2Int(tileX, tileY);
                if (!batchTiles.TryGetValue(key, out BatchTile tile))
                {
                    tile = batchTilePool.Count > 0
                        ? batchTilePool.Pop()
                        : new BatchTile();
                    batchTiles.Add(key, tile);
                }
                tile.rect = Union(tile.rect, intersection);
                tile.sampleIndices.Add(sampleIndex);
            }
        }

        private void ReleaseBatchTiles()
        {
            foreach (BatchTile tile in batchTiles.Values)
            {
                tile.rect = default;
                tile.sampleIndices.Clear();
                batchTilePool.Push(tile);
            }
            batchTiles.Clear();
        }

        private GPUBatchStamp CreateBatchStamp(StrokeDispatchSample dispatch, ActiveTarget active)
        {
            StrokeSample sample = dispatch.sample;
            float radius = Mathf.Max(0.00001f, dispatch.projection.valid
                ? dispatch.projection.uvBoundsRadius : dispatch.uvRadius);
            if (activeContext.brush.shape == BrushPreset.Shape.Square) radius *= 1.41421356f;
            Vector4 uvToBrush = dispatch.projection.valid
                ? dispatch.projection.uvToBrush
                : new Vector4(1f / radius, 0f, 0f, 1f / radius);
            return new GPUBatchStamp
            {
                center = sample.uv,
                uvToBrush = uvToBrush,
                rotation = (activeContext.brush.rotation + sample.rotation) * Mathf.Deg2Rad,
                flow = activeContext.brush.flow * (activeContext.pressureAffectsFlow
                    ? Mathf.Clamp01(sample.pressure) : 1f) *
                    Mathf.Max(0f, sample.flowMultiplier),
                color = TexturePaintChannelUtility.ConstrainColor(active.channel,
                    sample.hasColor ? sample.color : active.color),
                footprintScale = EffectiveScale(sample.footprintScale),
                sourceUVScale = EffectiveScale(sample.sourceUVScale),
                sourceUVOffset = sample.sourceUVOffset,
                triangleUV0 = dispatch.projection.triangleUV0,
                triangleUV1 = dispatch.projection.triangleUV1,
                triangleUV2 = dispatch.projection.triangleUV2,
                triangleBoundaryMask = dispatch.projection.triangleBoundaryMask,
                restrictToTriangle = dispatch.projection.restrictToTriangle ? 1 : 0
            };
        }

        private void EnsureBatchStampBuffer(int count)
        {
            int capacity = Mathf.Max(RasterBatchSize, count);
            if (batchStampScratch.Length < capacity)
                batchStampScratch = new GPUBatchStamp[capacity];
            if (batchStampBuffer != null && batchStampBuffer.count >= capacity) return;
            batchStampBuffer?.Dispose();
            batchStampBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<GPUBatchStamp>(),
                ComputeBufferType.Structured);
        }

        private void DispatchBatchTileSequentially(ActiveTarget active,
            List<StrokeDispatchSample> samples, int offset, BatchTile tile,
            Texture2D geometryMask, TexturePaintStrokeDiagnostics diagnostics)
        {
            for (int tileSampleIndex = 0; tileSampleIndex < tile.sampleIndices.Count;
                tileSampleIndex++)
            {
                StrokeDispatchSample dispatch = samples[offset +
                    tile.sampleIndices[tileSampleIndex]];
                RectInt rect = Intersect(CalculateBatchSampleRect(dispatch, active.target),
                    tile.rect);
                if (rect.width <= 0 || rect.height <= 0) continue;
                float radius = Mathf.Max(0.00001f, dispatch.projection.valid
                    ? dispatch.projection.uvBoundsRadius : dispatch.uvRadius);
                if (activeContext.brush.shape == BrushPreset.Shape.Square)
                    radius *= 1.41421356f;
                Vector4 uvToBrush = dispatch.projection.valid
                    ? dispatch.projection.uvToBrush
                    : new Vector4(1f / radius, 0f, 0f, 1f / radius);
                using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.RasterSubmit))
                {
                    bool dispatched = DispatchGPU(active, dispatch.sample,
                        dispatch.projection, uvToBrush, rect, geometryMask);
                    if (dispatched) continue;
                    Performance.cpuFallbacks++;
                    ApplyCPU(active, dispatch.sample, dispatch.projection, uvToBrush,
                        rect, geometryMask);
                }
            }
        }

        private bool DispatchGPUBatch(ActiveTarget active, ComputeBuffer buffer, int count, RectInt rect, Texture2D geometryMask)
        {
            if (strokeShader == null || !strokeShader.HasKernel("CSBatch")) return false;
            int kernel = strokeShader.FindKernel("CSBatch");
            if (!strokeShader.IsSupported(kernel)) return false;
            if (!activeContext.limitStrokeCoverage) EnsureDisabledStrokeCoverage();
            strokeShader.SetInts("_TextureSize", active.target.Width, active.target.Height);
            strokeShader.SetFloat("_Hardness", activeContext.brush.hardness);
            strokeShader.SetFloat("_Strength", activeContext.strength * active.contribution);
            strokeShader.SetFloat("_TriangleBoundaryPadding",
                TriangleBoundaryPaddingTexels);
            strokeShader.SetInt("_Shape", (int)activeContext.brush.shape);
            strokeShader.SetInt("_LimitStrokeCoverage", activeContext.limitStrokeCoverage ? 1 : 0);
            strokeShader.SetInt("_Operation", ToShaderOperation(activeContext.tool));
            strokeShader.SetInt("_BlendMode", (int)activeContext.brush.blendMode);
            strokeShader.SetInt("_PaintSourceKind", (int)active.sourceKind);
            strokeShader.SetInt("_VectorNormal", active.channel == TexturePaintChannel.Normal ? 1 : 0);
            strokeShader.SetInt("_MaskMode", active.isLayerMask ? 1 : 0);
            strokeShader.SetFloat("_MaskEraseValue", active.maskBaseValue);
            strokeShader.SetInt("_BatchCount", count);
            strokeShader.SetBuffer(kernel, "_BatchStamps", buffer);
            strokeShader.SetTexture(kernel, "_Source", active.target.Front);
            strokeShader.SetTexture(kernel, "_Destination", active.target.Back);
            strokeShader.SetTexture(kernel, "_PaintSource", active.paintSource != null ? active.paintSource : Texture2D.whiteTexture);
            strokeShader.SetTexture(kernel, "_Stamp", activeStampTexture != null ? activeStampTexture : Texture2D.whiteTexture);
            strokeShader.SetTexture(kernel, "_GeometryMask", geometryMask != null ? geometryMask : Texture2D.whiteTexture);
            if (activeContext.limitStrokeCoverage)
            {
                const int tileSize = 128;
                if (!CanAllocateCoverageTiles(active, rect, tileSize)) return false;
                int minX = rect.xMin / tileSize, maxX = (rect.xMax - 1) / tileSize;
                int minY = rect.yMin / tileSize, maxY = (rect.yMax - 1) / tileSize;
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    CoverageTile tile = EnsureGPUStrokeTile(active, new Vector2Int(x, y), tileSize);
                    RectInt intersection = Intersect(rect, tile.rect);
                    strokeShader.SetInts("_TileOffset", intersection.x, intersection.y);
                    strokeShader.SetInts("_DispatchSize", intersection.width, intersection.height);
                    strokeShader.SetInts("_CoverageOffset", tile.rect.x, tile.rect.y);
                    strokeShader.SetTexture(kernel, "_StrokeBase", tile.strokeBase);
                    strokeShader.SetTexture(kernel, "_StrokeCoverage", tile.coverage);
                    strokeShader.Dispatch(kernel, Mathf.CeilToInt(intersection.width / 16f), Mathf.CeilToInt(intersection.height / 16f), 1);
                    Performance.computeDispatches++;
                }
            }
            else
            {
                strokeShader.SetInts("_TileOffset", rect.x, rect.y);
                strokeShader.SetInts("_DispatchSize", rect.width, rect.height);
                strokeShader.SetInts("_CoverageOffset", 0, 0);
                strokeShader.SetTexture(kernel, "_StrokeBase", active.target.Front);
                strokeShader.SetTexture(kernel, "_StrokeCoverage", disabledStrokeCoverage);
                strokeShader.Dispatch(kernel, Mathf.CeilToInt(rect.width / 16f), Mathf.CeilToInt(rect.height / 16f), 1);
                Performance.computeDispatches++;
            }
            active.target.SwapAndSynchronize(rect);
            Performance.copiedPixels += (long)rect.width * rect.height;
            return true;
        }

        private bool CanBatchCurrentTool()
            => activeContext != null && (activeContext.tool == TexturePaintTool.Paint || activeContext.tool == TexturePaintTool.Erase ||
                activeContext.tool == TexturePaintTool.Dodge || activeContext.tool == TexturePaintTool.Burn);

        public void EndStroke(bool commit = true)
        {
            if (!strokeStarted)
            {
                EndInteractiveCompositing();
                ReleaseStrokeBuffers();
                queuedInteractiveSamples.Clear();
                ClearDeferredRefreshState(true);
                sampleBatchDepth = 0;
                deferInteractiveRefresh = false;
                flushingSampleBatch = false;
                capturedHistoryTiles.Clear();
                activeBrushContext = null;
                activeStampTexture = null;
                activeHistoryName = null;
                activeContext = null;
                activeTargets.Clear();
                previousProceduralBounds.Clear();
                currentProceduralBounds.Clear();
                captureActiveStrokeHistory = true;
                return;
            }
            TexturePaintStrokeDiagnostics diagnostics = Performance.StrokeDiagnostics;
            TexturePaintStrokeDiagnosticScope endDiagnostics = diagnostics.Measure(
                TexturePaintStrokeDiagnosticPhase.EndStroke);
            int storedSamples = 0;
            foreach (TexturePaintStrokeRecord record in activeStrokeRecords.Values)
                storedSamples += record?.samples.Count ?? 0;
            int activeCoverageTiles = 0;
            for (int i = 0; i < activeTargets.Count; i++)
                activeCoverageTiles += activeTargets[i].coverageTiles.Count;
            diagnostics.RecordEngineWorkingSet(storedSamples, history.PendingTileCount,
                activeCoverageTiles, activeCoverageBytes);
            activeContext?.pluginHost?.EndBrush(activeContext.brushPlugin, activeBrushContext, commit);
            if (captureActiveStrokeHistory)
            {
                using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.HistoryCapture))
                {
                    if (commit) history.Commit(); else history.CancelPending();
                }
            }
            FinalizeStrokeRecords(commit);
            ReleaseStrokeBuffers();
            HashSet<TextureSet> changedSets = new HashSet<TextureSet>();
            for (int i = 0; i < activeTargets.Count; i++)
                if (activeTargets[i].textures != null) changedSets.Add(activeTargets[i].textures);
            EndInteractiveCompositing();
            using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.Composite))
                RefreshClearedProceduralBounds(changedSets, commit);
            foreach (TextureSet set in changedSets)
            {
                // Interactive painting deliberately reuses the previous distance field so a 2K
                // JFA is not rebuilt for every stamp. A procedural path replacement first clears
                // its layer, however, so that reusable field can describe the empty layer while
                // the replacement ribbon is rasterized. Recompose after leaving interactive mode
                // to rebuild the field from the completed layer before packing the final preview.
                if (activeContext?.derivedLayerRaster != true &&
                    TextureLayerCompositor.HasDistanceEffects(set))
                    using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.PreviewBinding))
                        set.BindPreviewTextures();
            }
            activeBrushContext = null; activeStampTexture = null; activeHistoryName = null;
            activeContext = null; activeTargets.Clear(); strokeStarted = false;
            queuedInteractiveSamples.Clear();
            ClearDeferredRefreshState(true);
            sampleBatchDepth = 0;
            deferInteractiveRefresh = false;
            flushingSampleBatch = false;
            capturedHistoryTiles.Clear();
            captureActiveStrokeHistory = true;
            endDiagnostics.Dispose();
            diagnostics.Complete(commit, Performance);
        }

        private void EndInteractiveCompositing()
        {
            foreach (TextureLayerCompositor compositor in activeCompositors)
                compositor?.EndInteractiveEdit();
            activeCompositors.Clear();
        }

        public bool Undo() { bool changed = history.Undo(); if (changed) TextureChanged?.Invoke(null, TexturePaintChannel.Custom); return changed; }
        public bool Redo() { bool changed = history.Redo(); if (changed) TextureChanged?.Invoke(null, TexturePaintChannel.Custom); return changed; }

        /// <summary>
        /// Rewinds pixels authored by the currently open stroke while preserving its original GPU
        /// history captures. Used when the first provisional stamp must be replayed after its
        /// direction becomes known.
        /// </summary>
        public bool RewindActiveStroke(bool restartBrushPlugin = false)
        {
            if (!strokeStarted) return false;
            queuedInteractiveSamples.Clear();
            ClearDeferredRefreshState(false);
            TexturePaintBrushContextV2 replacementBrushContext = null;
            if (restartBrushPlugin && activeContext?.brushPlugin != null)
            {
                try
                {
                    replacementBrushContext = activeContext.pluginHost.BeginBrush(
                        activeContext.brushPlugin, activeContext.textures.persistentId,
                        activeContext.channel, activeContext.brushPluginParameters,
                        activeContext.cancellationToken);
                }
                catch { return false; }
            }
            history.RestorePendingBefore();
            ReleaseStrokeBuffers();
            foreach (TexturePaintStrokeRecord record in activeStrokeRecords.Values)
                record?.samples.Clear();
            if (replacementBrushContext != null)
            {
                activeContext.pluginHost.EndBrush(activeContext.brushPlugin,
                    activeBrushContext, false);
                activeBrushContext = replacementBrushContext;
            }
            HashSet<TextureSet> changedSets = new HashSet<TextureSet>();
            for (int i = 0; i < activeTargets.Count; i++)
                if (activeTargets[i].textures != null) changedSets.Add(activeTargets[i].textures);
            foreach (TextureSet set in changedSets) set.BindPreviewTextures();
            TextureChanged?.Invoke(null, TexturePaintChannel.Custom);
            return true;
        }

        public bool ClearProceduralResult(string historyGroupKey, TexturePaintLayer layer,
            IReadOnlyList<TextureSet> textureSets)
        {
            AdoptUnambiguousProceduralLayers(historyGroupKey, textureSets);
            bool changed = history.RevertLatest(historyGroupKey);
            if (textureSets != null) RemoveStrokeRecords(historyGroupKey, textureSets);
            HashSet<TexturePaintLayer> clearedLayers = new HashSet<TexturePaintLayer>();
            if (textureSets != null)
            {
                for (int setIndex = 0; setIndex < textureSets.Count; setIndex++)
                {
                    TextureSet set = textureSets[setIndex];
                    if (set == null) continue;
                    for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                    {
                        TexturePaintLayer candidate = set.layers[layerIndex];
                        if (!string.Equals(candidate?.proceduralGroupKey, historyGroupKey, StringComparison.Ordinal))
                            continue;
                        clearedLayers.Add(candidate);
                        foreach (EditableTextureTarget target in candidate.channels.Values)
                        {
                            target.Reset(null, Color.clear);
                            proceduralRasterBounds.Remove(ProceduralBoundsKey(historyGroupKey, target));
                        }
                        candidate.strokes.Clear();
                        changed |= candidate.channels.Count > 0;
                    }
                }
            }
            if (layer != null && !clearedLayers.Contains(layer))
            {
                foreach (EditableTextureTarget target in layer.channels.Values)
                {
                    target.Reset(null, Color.clear);
                    proceduralRasterBounds.Remove(ProceduralBoundsKey(historyGroupKey, target));
                }
                layer.strokes.Clear();
                changed = layer.channels.Count > 0;
            }
            if (!changed) return false;
            if (textureSets != null)
                for (int i = 0; i < textureSets.Count; i++) textureSets[i]?.BindPreviewTextures();
            TextureChanged?.Invoke(null, TexturePaintChannel.Custom);
            return true;
        }

        private static void AdoptUnambiguousProceduralLayers(string historyGroupKey,
            IReadOnlyList<TextureSet> textureSets)
        {
            if (string.IsNullOrEmpty(historyGroupKey) || textureSets == null) return;
            for (int setIndex = 0; setIndex < textureSets.Count; setIndex++)
            {
                TextureSet set = textureSets[setIndex];
                if (set == null) continue;
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                {
                    TexturePaintLayer candidate = set.layers[layerIndex];
                    if (candidate == null || !string.IsNullOrEmpty(candidate.proceduralGroupKey) ||
                        candidate.strokes.Count == 0) continue;
                    bool containsGroup = false;
                    bool containsOtherGroup = false;
                    for (int recordIndex = 0; recordIndex < candidate.strokes.Count; recordIndex++)
                    {
                        TexturePaintStrokeRecord record = candidate.strokes[recordIndex];
                        if (record == null) continue;
                        if (string.Equals(record.historyGroupKey, historyGroupKey, StringComparison.Ordinal))
                            containsGroup = true;
                        else containsOtherGroup = true;
                    }
                    if (containsGroup && !containsOtherGroup) candidate.proceduralGroupKey = historyGroupKey;
                }
            }
        }

        private void BuildActiveTargets(StrokeContext context, TextureSet textures, TexturePaintSourceMode mode)
        {
            if (context.editLayerMask)
            {
                if (textures == null || (uint)textures.activeLayerIndex >= (uint)textures.layers.Count) return;
                TexturePaintLayer layer = textures.layers[textures.activeLayerIndex];
                TexturePaintLayerMask mask = layer?.layerMask;
                if (mask?.target == null) return;
                var sourceSettings = new TexturePaintChannelSourceSettings
                {
                    source = context.paintSource,
                    sourceTexture = context.sourceTexture,
                    sourceSprite = context.sourceSprite,
                    sourceOverlay = context.ResolveSourceOverlay(textures),
                    color = context.color,
                    normalConvention = context.normalConvention,
                    invert = context.sourceInvert
                };
                TexturePaintChannel sampleChannel = context.paintSource == TexturePaintBrushSource.Overlay
                    ? context.maskSourceChannel : TexturePaintChannel.Albedo;
                Texture source = ResolveChannelPaintSource(textures, sampleChannel,
                    sourceSettings);
                float value = Mathf.Clamp01(context.maskValue);
                activeTargets.Add(new ActiveTarget
                {
                    textures = textures,
                    channel = sampleChannel,
                    target = mask.target,
                    paintSource = source,
                    sourceKind = context.paintSource,
                    color = new Color(value, value, value, 1f),
                    contribution = 1f,
                    isLayerMask = true,
                    maskBaseValue = mask.baseValue
                });
                return;
            }
            if (context.channelSources.Count > 0 && context.tool != TexturePaintTool.NormalTouchup)
            {
                foreach (KeyValuePair<TexturePaintChannel, TexturePaintChannelSourceSettings> pair in
                    context.channelSources)
                {
                    TexturePaintChannel channel = pair.Key;
                    TexturePaintChannelSourceSettings source = pair.Value;
                    Texture paintTexture = null;
                    bool sourceUsable = TryResolveChannelPaintSource(textures, channel, source,
                        out paintTexture);
                    if (mode != TexturePaintSourceMode.SourceTexture && textures != null &&
                        (uint)textures.activeLayerIndex < (uint)textures.layers.Count)
                    {
                        TexturePaintChannelSourceSettings memberSource = textures.layers[textures.activeLayerIndex]
                            .GetChannelSettings(channel, false)?.sourceSettings;
                        if (TryResolveChannelPaintSource(textures, channel, memberSource,
                            out Texture memberPaintTexture))
                        {
                            source = memberSource;
                            paintTexture = memberPaintTexture;
                            sourceUsable = true;
                        }
                        // A linked physical layer can retain legacy Texture-with-null metadata.
                        // That metadata must not erase the valid source selected for this stroke.
                    }
                    if (!sourceUsable || ContainsChannel(textures, channel)) continue;
                    EditableTextureTarget target = textures.GetPaintTarget(channel, mode);
                    if (target != null &&
                        (source.source == TexturePaintBrushSource.Color || paintTexture != null) &&
                        TryGetChannelContribution(textures, channel, mode, out float contribution))
                        activeTargets.Add(new ActiveTarget
                        {
                            textures = textures,
                            channel = channel,
                            target = target,
                            paintSource = paintTexture,
                            sourceKind = source.source,
                            color = TexturePaintChannelUtility.ConstrainColor(channel, source.color),
                            contribution = contribution
                        });
                }
            }
            OverlayDataAsset sourceOverlay = context.ResolveSourceOverlay(textures);
            if (context.channelSources.Count == 0 &&
                context.paintSource == TexturePaintBrushSource.Overlay && sourceOverlay != null &&
                context.tool != TexturePaintTool.NormalTouchup)
            {
                Texture[] overlayTextures = sourceOverlay.textureList;
                UMAMaterial mappingMaterial = sourceOverlay.material != null
                    ? sourceOverlay.material
                    : textures.umaMaterial;
                for (int i = 0; i < overlayTextures.Length; i++)
                {
                    TexturePaintChannel channel = context.channel;
                    if (mappingMaterial?.channels != null && i < mappingMaterial.channels.Length)
                    {
                        UMAMaterial.MaterialChannel umaChannel = mappingMaterial.channels[i];
#if UNITY_EDITOR
                        channel = TextureStore.ResolveChannel(umaChannel, mappingMaterial.material);
#else
                        channel = TextureStore.ResolveChannel(umaChannel.sourceTextureName + " " + umaChannel.materialPropertyName, umaChannel.channelType);
#endif
                    }
                    if (ContainsChannel(textures, channel)) continue;
                    EditableTextureTarget target = textures.GetPaintTarget(channel, mode);
                    Texture paintTexture = overlayTextures[i];
                    if (channel == TexturePaintChannel.Normal && paintTexture is Texture2D normalTexture)
                        paintTexture = TexturePaintSpriteSource.Resolve(normalTexture, null, channel,
                            context.normalConvention);
                    if (target != null && paintTexture != null && TryGetChannelContribution(textures, channel, mode, out float contribution))
                        activeTargets.Add(new ActiveTarget
                        {
                            textures = textures,
                            channel = channel,
                            target = target,
                            paintSource = paintTexture,
                            sourceKind = TexturePaintBrushSource.Overlay,
                            color = TexturePaintChannelUtility.ConstrainColor(channel, context.color),
                            contribution = contribution
                        });
                }
            }
            // Explicit per-channel sources define the complete authored target set. If none of
            // them can be resolved for this material, do not fall back to context.channel: that
            // value may be stale (commonly Albedo on a Normal-Control-only Path) and would create
            // an unintended layer channel. Normal Touchup remains a single-channel operation.
            bool allowSingleChannelFallback = context.channelSources.Count == 0 ||
                context.tool == TexturePaintTool.NormalTouchup;
            if (allowSingleChannelFallback && !ContainsTextureSet(textures))
            {
                EditableTextureTarget target = textures.GetPaintTarget(context.channel, mode);
                Texture resolvedSource = TexturePaintSpriteSource.Resolve(context.sourceTexture,
                    context.sourceSprite, context.channel, context.normalConvention);
                if (target != null && TryGetChannelContribution(textures, context.channel, mode, out float contribution))
                    activeTargets.Add(new ActiveTarget
                    {
                        textures = textures,
                        channel = context.channel,
                        target = target,
                        paintSource = resolvedSource,
                        sourceKind = context.paintSource,
                        color = TexturePaintChannelUtility.ConstrainColor(context.channel, context.color),
                        contribution = contribution
                    });
            }
        }

        private static bool TryResolveChannelPaintSource(TextureSet textures, TexturePaintChannel channel,
            TexturePaintChannelSourceSettings source, out Texture paintTexture)
        {
            paintTexture = null;
            if (source == null) return false;
            if (source.source == TexturePaintBrushSource.Color) return true;
            paintTexture = ResolveChannelPaintSource(textures, channel, source);
            return paintTexture != null;
        }

        private bool FailStrokeStart(string error)
        {
            LastStrokeError = error;
            activeTargets.Clear();
            activeStampTexture = null;
            activeHistoryName = null;
            activeContext = null;
            Performance.StrokeDiagnostics.Abort(error, Performance);
            return false;
        }

        private static Texture ResolveChannelPaintSource(TextureSet textures, TexturePaintChannel channel,
            TexturePaintChannelSourceSettings source)
        {
            if (source == null || source.source == TexturePaintBrushSource.Color) return null;
            if (source.source == TexturePaintBrushSource.Texture)
                return TexturePaintSpriteSource.Resolve(source.sourceTexture, source.sourceSprite,
                    channel, source.normalConvention, source.invert);
            if (source.source != TexturePaintBrushSource.Overlay || source.sourceOverlay == null ||
                textures == null) return null;
            var fillSettings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Overlay,
                sourceOverlay = source.sourceOverlay,
                normalConvention = source.normalConvention,
                invert = source.invert
            };
            return textures.ResolveFillSource(fillSettings, channel);
        }

        private static bool TryGetChannelContribution(TextureSet textures, TexturePaintChannel channel,
            TexturePaintSourceMode mode, out float contribution)
        {
            contribution = 1f;
            if (mode == TexturePaintSourceMode.SourceTexture || textures == null ||
                (uint)textures.activeLayerIndex >= (uint)textures.layers.Count) return true;
            TexturePaintLayer layer = textures.layers[textures.activeLayerIndex];
            TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel);
            if (settings.locked) return false;
            contribution = Mathf.Clamp01(settings.contribution);
            return contribution > 0f;
        }

        private void CreateStrokeRecords(TexturePaintSourceMode mode)
        {
            activeStrokeRecords.Clear();
            activeStrokeBindings.Clear();
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget target = activeTargets[i];
                if (target.textures == null || activeStrokeRecords.ContainsKey(target.textures)) continue;
                TexturePaintLayer owner = null;
                if (mode != TexturePaintSourceMode.SourceTexture)
                {
                    for (int layerIndex = 0; layerIndex < target.textures.layers.Count && owner == null; layerIndex++)
                    {
                        TexturePaintLayer candidate = target.textures.layers[layerIndex];
                        if (ReferenceEquals(candidate.layerMask?.target, target.target))
                        {
                            owner = candidate;
                            break;
                        }
                        foreach (EditableTextureTarget layerTarget in target.textures.layers[layerIndex].channels.Values)
                            if (ReferenceEquals(layerTarget, target.target)) { owner = target.textures.layers[layerIndex]; break; }
                    }
                }
                TexturePaintStrokeRecord record = new TexturePaintStrokeRecord
                {
                    createdUtc = DateTime.UtcNow.ToString("O"),
                    historyGroupKey = activeContext.historyGroupKey,
                    tool = activeContext.tool,
                    channel = activeContext.channel,
                    directUV = activeContext.directUV
                };
                if (owner != null) owner.strokes.Add(record); else target.textures.baseStrokes.Add(record);
                activeStrokeRecords[target.textures] = record;
                activeStrokeBindings.Add(new StrokeRecordBinding { set = target.textures, layer = owner, record = record });
            }
        }

        private void PrepareProceduralReplacement(StrokeContext context,
            IReadOnlyList<TextureSet> replacementSets)
        {
            previousProceduralBounds.Clear();
            currentProceduralBounds.Clear();
            if (context.derivedLayerRaster)
            {
                for (int i = 0; i < activeTargets.Count; i++)
                {
                    ActiveTarget active = activeTargets[i];
                    string key = ProceduralBoundsKey(context.historyGroupKey, active);
                    previousProceduralBounds[active] = proceduralRasterBounds.TryGetValue(key,
                        out RectInt previous)
                        ? previous
                        : new RectInt(0, 0, active.target.Width, active.target.Height);
                }
            }
            bool reverted = history.RevertLatest(context.historyGroupKey);
            bool replacementChanged = reverted;
            HashSet<TextureSet> changedSets = new HashSet<TextureSet>();
            if (replacementSets != null)
                for (int setIndex = 0; setIndex < replacementSets.Count; setIndex++)
                    if (replacementSets[setIndex] != null) changedSets.Add(replacementSets[setIndex]);
            else if (context.textures != null) changedSets.Add(context.textures);
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget active = activeTargets[i];
                if (active.textures != null) changedSets.Add(active.textures);
            }

            RemoveStrokeRecords(context.historyGroupKey, changedSets);
            if (context.replaceLayer != null)
            {
                context.replaceLayer.strokes.Clear();
                var activeReplacementTargets = new HashSet<EditableTextureTarget>();
                for (int i = 0; i < activeTargets.Count; i++)
                    activeReplacementTargets.Add(activeTargets[i].target);
                foreach (TextureSet changedSet in changedSets)
                {
                    bool clearedInactiveTarget = false;
                    for (int layerIndex = 0; layerIndex < changedSet.layers.Count; layerIndex++)
                    {
                        TexturePaintLayer candidate = changedSet.layers[layerIndex];
                        if (!IsProceduralReplacementLayer(candidate, context.replaceLayer,
                            context.historyGroupKey)) continue;
                        candidate.strokes.Clear();
                        foreach (EditableTextureTarget target in candidate.channels.Values)
                        {
                            if (target == null) continue;
                            target.Reset(null, Color.clear);
                            proceduralRasterBounds.Remove(ProceduralBoundsKey(
                                context.historyGroupKey, target));
                            replacementChanged = true;
                            if (!activeReplacementTargets.Contains(target)) clearedInactiveTarget = true;
                        }
                    }
                    // A removed, disabled, or now-unsupported channel has no ActiveTarget and
                    // therefore no incremental old-footprint refresh at EndStroke. Recompose that
                    // uncommon case now so its packed/preview texture cannot retain the old path.
                    if (context.derivedLayerRaster && clearedInactiveTarget)
                        changedSet.BindPreviewTextures();
                }
            }

            if (replacementChanged && !context.derivedLayerRaster)
            {
                foreach (TextureSet set in changedSets) set.BindPreviewTextures();
                TextureChanged?.Invoke(null, TexturePaintChannel.Custom);
            }
        }

        internal static bool IsProceduralReplacementLayer(TexturePaintLayer candidate,
            TexturePaintLayer primary, string historyGroupKey)
            => candidate != null && (ReferenceEquals(candidate, primary) ||
                !string.IsNullOrEmpty(historyGroupKey) &&
                string.Equals(candidate.proceduralGroupKey, historyGroupKey,
                    StringComparison.Ordinal));

        private static void RemoveStrokeRecords(string historyGroupKey, IEnumerable<TextureSet> sets)
        {
            foreach (TextureSet set in sets)
            {
                if (set == null) continue;
                set.baseStrokes.RemoveAll(record => record != null &&
                    string.Equals(record.historyGroupKey, historyGroupKey, StringComparison.Ordinal));
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                    set.layers[layerIndex].strokes.RemoveAll(record => record != null &&
                        string.Equals(record.historyGroupKey, historyGroupKey, StringComparison.Ordinal));
            }
        }

        private static TexturePaintLayer FindTargetOwner(TextureSet set, EditableTextureTarget target)
        {
            if (set == null || target == null) return null;
            for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
            {
                if (ReferenceEquals(set.layers[layerIndex].layerMask?.target, target))
                    return set.layers[layerIndex];
                foreach (EditableTextureTarget candidate in set.layers[layerIndex].channels.Values)
                    if (ReferenceEquals(candidate, target)) return set.layers[layerIndex];
            }
            return null;
        }

        private void FinalizeStrokeRecords(bool commit)
        {
            for (int i = 0; i < activeStrokeBindings.Count; i++)
            {
                StrokeRecordBinding binding = activeStrokeBindings[i];
                if (commit && binding.record.samples.Count > 0) continue;
                if (binding.layer != null) binding.layer.strokes.Remove(binding.record);
                else binding.set?.baseStrokes.Remove(binding.record);
            }
            activeStrokeRecords.Clear();
            activeStrokeBindings.Clear();
        }

        private bool ContainsTextureSet(TextureSet textures)
        {
            for (int i = 0; i < activeTargets.Count; i++) if (activeTargets[i].textures == textures) return true;
            return false;
        }

        private void IncludeHistoryTiles(string name, EditableTextureTarget target, RectInt rect,
            TexturePaintStrokeDiagnostics diagnostics)
        {
            if (!captureActiveStrokeHistory || target == null) return;
            rect = Intersect(rect, new RectInt(0, 0, target.Width, target.Height));
            if (rect.width <= 0 || rect.height <= 0) return;
            int tileSize = Mathf.Clamp(history.TileSize, 32, 512);
            int minX = rect.xMin / tileSize;
            int maxX = (rect.xMax - 1) / tileSize;
            int minY = rect.yMin / tileSize;
            int maxY = (rect.yMax - 1) / tileSize;
            for (int tileY = minY; tileY <= maxY; tileY++)
            for (int tileX = minX; tileX <= maxX; tileX++)
            {
                if (!capturedHistoryTiles.Add(new HistoryTileKey(target, tileX, tileY)))
                    continue;
                RectInt tileRect = Intersect(new RectInt(tileX * tileSize, tileY * tileSize,
                    tileSize, tileSize), new RectInt(0, 0, target.Width, target.Height));
                using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.HistoryCapture))
                    history.Include(name, target, tileRect);
                diagnostics.RecordHistoryInclude();
            }
        }

        private void RecordRasterChange(ActiveTarget active, RectInt rect)
        {
            if (active == null || rect.width <= 0 || rect.height <= 0) return;
            if (deferInteractiveRefresh)
            {
                if (!deferredDirtyTargets.TryGetValue(active, out DirtyTargetState state))
                    deferredDirtyTargets.Add(active, state = new DirtyTargetState());
                AddDirtyTiles(state.tiles, rect, active.target.Width, active.target.Height);
                return;
            }

            TexturePaintStrokeDiagnostics diagnostics = Performance.StrokeDiagnostics;
            using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.Composite))
                CompositeChangedTarget(active, rect);
            Performance.composedPixels += (long)rect.width * rect.height;
            TextureChanged?.Invoke(active.textures,
                active.isLayerMask ? TexturePaintChannel.Custom : active.channel);
        }

        private void FlushDeferredRefresh()
        {
            if (deferredDirtyTargets.Count == 0) return;
            long previewStarted = Stopwatch.GetTimestamp();
            TexturePaintStrokeDiagnostics diagnostics = Performance.StrokeDiagnostics;
            foreach (KeyValuePair<ActiveTarget, DirtyTargetState> pair in deferredDirtyTargets)
            {
                ActiveTarget active = pair.Key;
                DirtyTargetState dirty = pair.Value;
                if (dirty.tiles.Count == 0) continue;
                foreach (RectInt rect in dirty.tiles.Values)
                {
                    using (diagnostics.Measure(TexturePaintStrokeDiagnosticPhase.Composite))
                        CompositeChangedTarget(active, rect);
                    Performance.composedPixels += (long)rect.width * rect.height;
                }
                TextureChanged?.Invoke(active.textures,
                    active.isLayerMask ? TexturePaintChannel.Custom : active.channel);
                if (active.textures == null) continue;
                if (!deferredPreviewStates.TryGetValue(active.textures,
                    out PreviewDirtyState preview))
                    deferredPreviewStates.Add(active.textures,
                        preview = new PreviewDirtyState());
                if (active.isLayerMask) preview.fullRefresh = true;
                else
                    foreach (RectInt rect in dirty.tiles.Values)
                        AddDirtyTiles(preview.tiles, rect, active.target.Width,
                            active.target.Height);
            }
            foreach (KeyValuePair<TextureSet, PreviewDirtyState> pair in deferredPreviewStates)
            {
                if (!pair.Value.fullRefresh && pair.Value.tiles.Count == 0) continue;
                using TexturePaintStrokeDiagnosticScope scope = diagnostics.Measure(
                    TexturePaintStrokeDiagnosticPhase.PreviewBinding);
                if (pair.Value.fullRefresh) pair.Key.BindPreviewTextures(false);
                else
                    foreach (RectInt rect in pair.Value.tiles.Values)
                        pair.Key.BindPreviewTextures(false, rect);
            }
            Performance.RecordPreview(ElapsedMilliseconds(previewStarted));
        }

        private void ClearDeferredRefreshState(bool releaseTargets)
        {
            foreach (DirtyTargetState dirty in deferredDirtyTargets.Values)
                dirty.tiles.Clear();
            foreach (PreviewDirtyState preview in deferredPreviewStates.Values)
            {
                preview.fullRefresh = false;
                preview.tiles.Clear();
            }
            if (!releaseTargets) return;
            deferredDirtyTargets.Clear();
            deferredPreviewStates.Clear();
        }

        private static void AddDirtyTiles(Dictionary<Vector2Int, RectInt> tiles, RectInt rect,
            int width, int height)
        {
            rect = Intersect(rect, new RectInt(0, 0, width, height));
            if (rect.width <= 0 || rect.height <= 0) return;
            int minX = rect.xMin / RasterTileSize;
            int maxX = (rect.xMax - 1) / RasterTileSize;
            int minY = rect.yMin / RasterTileSize;
            int maxY = (rect.yMax - 1) / RasterTileSize;
            for (int tileY = minY; tileY <= maxY; tileY++)
            for (int tileX = minX; tileX <= maxX; tileX++)
            {
                RectInt bounds = Intersect(new RectInt(tileX * RasterTileSize,
                    tileY * RasterTileSize, RasterTileSize, RasterTileSize),
                    new RectInt(0, 0, width, height));
                RectInt intersection = Intersect(rect, bounds);
                if (intersection.width <= 0 || intersection.height <= 0) continue;
                Vector2Int key = new Vector2Int(tileX, tileY);
                tiles.TryGetValue(key, out RectInt accumulated);
                tiles[key] = Union(accumulated, intersection);
            }
        }

        private void CompositeChangedTarget(ActiveTarget active, RectInt rect)
        {
            if (active?.textures == null) return;
            if (activeContext?.derivedLayerRaster == true && rect.width > 0 && rect.height > 0)
            {
                currentProceduralBounds.TryGetValue(active, out RectInt accumulated);
                currentProceduralBounds[active] = Union(accumulated, rect);
            }
            if (active.isLayerMask)
            {
                foreach (TextureChannelTarget channel in active.textures.channels.Values)
                    if (channel != null)
                    {
                        int width = channel.composite != null ? channel.composite.width : channel.editable.Width;
                        int height = channel.composite != null ? channel.composite.height : channel.editable.Height;
                        active.textures.CompositeChannel(channel.channel, ScaleRect(rect,
                            active.target.Width, active.target.Height, width, height));
                    }
                return;
            }
            active.textures.CompositeChannel(active.channel, rect);
        }

        private void RefreshClearedProceduralBounds(HashSet<TextureSet> changedSets, bool commit)
        {
            if (activeContext?.derivedLayerRaster != true || previousProceduralBounds.Count == 0) return;
            var fullyRefreshed = new HashSet<TextureSet>();
            foreach (TextureSet set in changedSets)
            {
                if (!TextureLayerCompositor.HasDistanceEffects(set)) continue;
                set.BindPreviewTextures();
                fullyRefreshed.Add(set);
            }

            foreach (KeyValuePair<ActiveTarget, RectInt> pair in previousProceduralBounds)
            {
                ActiveTarget active = pair.Key;
                if (active?.textures == null || fullyRefreshed.Contains(active.textures)) continue;
                RectInt previous = pair.Value;
                if (previous.width <= 0 || previous.height <= 0) continue;
                // The new footprint was composited incrementally while painting. Recompose the old
                // footprint once more so the area exposed by a shrink reflects the cleared layer.
                CompositeTargetWithoutRecording(active, previous);
                active.textures.BindPreviewTextures(false, previous);
            }

            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget active = activeTargets[i];
                string key = ProceduralBoundsKey(activeContext.historyGroupKey, active);
                if (commit && currentProceduralBounds.TryGetValue(active, out RectInt current) &&
                    current.width > 0 && current.height > 0)
                    proceduralRasterBounds[key] = current;
                else proceduralRasterBounds.Remove(key);
            }
            previousProceduralBounds.Clear();
            currentProceduralBounds.Clear();
        }

        private static void CompositeTargetWithoutRecording(ActiveTarget active, RectInt rect)
        {
            if (active.isLayerMask)
            {
                foreach (TextureChannelTarget channel in active.textures.channels.Values)
                    if (channel != null)
                    {
                        int width = channel.composite != null ? channel.composite.width : channel.editable.Width;
                        int height = channel.composite != null ? channel.composite.height : channel.editable.Height;
                        active.textures.CompositeChannel(channel.channel, ScaleRect(rect,
                            active.target.Width, active.target.Height, width, height));
                    }
                return;
            }
            active.textures.CompositeChannel(active.channel, rect);
        }

        private static string ProceduralBoundsKey(string historyGroupKey, ActiveTarget active)
            => ProceduralBoundsKey(historyGroupKey, active?.target);

        private static string ProceduralBoundsKey(string historyGroupKey, EditableTextureTarget target)
        {
            EntityId targetId = target?.Front != null
                ? target.Front.GetEntityId() : EntityId.None;
            return (historyGroupKey ?? string.Empty) + "|" + targetId;
        }

        private static RectInt ScaleRect(RectInt rect, int sourceWidth, int sourceHeight,
            int destinationWidth, int destinationHeight)
        {
            if (rect.width <= 0 || rect.height <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
                return new RectInt(0, 0, destinationWidth, destinationHeight);
            int xMin = Mathf.FloorToInt(rect.xMin * destinationWidth / (float)sourceWidth);
            int yMin = Mathf.FloorToInt(rect.yMin * destinationHeight / (float)sourceHeight);
            int xMax = Mathf.CeilToInt(rect.xMax * destinationWidth / (float)sourceWidth);
            int yMax = Mathf.CeilToInt(rect.yMax * destinationHeight / (float)sourceHeight);
            xMin = Mathf.Clamp(xMin, 0, destinationWidth);
            yMin = Mathf.Clamp(yMin, 0, destinationHeight);
            xMax = Mathf.Clamp(xMax, xMin, destinationWidth);
            yMax = Mathf.Clamp(yMax, yMin, destinationHeight);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private bool ContainsChannel(TextureSet textures, TexturePaintChannel channel)
        {
            for (int i = 0; i < activeTargets.Count; i++)
                if (activeTargets[i].textures == textures && activeTargets[i].channel == channel) return true;
            return false;
        }

        private bool DispatchGPU(ActiveTarget active, StrokeSample sample, BrushProjection projection,
            Vector4 uvToBrush, RectInt rect,
            Texture2D geometryMask)
        {
            ComputeShader shader;
            string kernelName;
            switch (activeContext.tool)
            {
                case TexturePaintTool.Blur: shader = blurShader; kernelName = "CSBlur"; break;
                case TexturePaintTool.NormalTouchup: shader = normalShader; kernelName = "CSMain"; break;
                case TexturePaintTool.Smear:
                case TexturePaintTool.Clone:
                    shader = strokeShader; kernelName = "CSMain"; break;
                default:
                    shader = strokeShader; kernelName = "CSStamp"; break;
            }
            if (shader == null || !shader.HasKernel(kernelName)) return false;
            int kernel = shader.FindKernel(kernelName);
            if (!shader.IsSupported(kernel)) return false;
            if (!activeContext.limitStrokeCoverage) EnsureDisabledStrokeCoverage();
            shader.SetInts("_TextureSize", active.target.Width, active.target.Height);
            shader.SetVector("_BrushCenter", sample.uv);
            shader.SetVector("_UVToBrush", uvToBrush);
            shader.SetFloat("_Hardness", activeContext.brush.hardness);
            shader.SetFloat("_Flow", activeContext.brush.flow *
                (activeContext.pressureAffectsFlow ? Mathf.Clamp01(sample.pressure) : 1f) * Mathf.Max(0f, sample.flowMultiplier));
            shader.SetFloat("_Strength", activeContext.strength * active.contribution);
            shader.SetFloat("_Rotation", (activeContext.brush.rotation + sample.rotation) * Mathf.Deg2Rad);
            shader.SetInt("_Shape", (int)activeContext.brush.shape);
            Vector2 footprintScale = EffectiveScale(sample.footprintScale);
            shader.SetVector("_FootprintScale", new Vector4(footprintScale.x, footprintScale.y, 0f, 0f));
            SetTriangleRestriction(shader, projection);
            shader.SetInt("_LimitStrokeCoverage", activeContext.limitStrokeCoverage ? 1 : 0);
            shader.SetTexture(kernel, "_Source", active.target.Front);
            shader.SetTexture(kernel, "_Destination", active.target.Back);
            shader.SetTexture(kernel, "_Stamp", activeStampTexture != null ? activeStampTexture : Texture2D.whiteTexture);
            shader.SetTexture(kernel, "_PaintMask", Texture2D.whiteTexture);
            shader.SetTexture(kernel, "_GeometryMask", geometryMask != null ? geometryMask : Texture2D.whiteTexture);
            if (shader == strokeShader)
            {
                Vector2 sourceUVScale = EffectiveScale(sample.sourceUVScale);
                shader.SetVector("_SourceUVScale", new Vector4(sourceUVScale.x, sourceUVScale.y, 0f, 0f));
                shader.SetVector("_SourceUVOffset", new Vector4(sample.sourceUVOffset.x, sample.sourceUVOffset.y, 0f, 0f));
                shader.SetVector("_PreviousUV", sample.previousUV);
                shader.SetVector("_CloneSourceUV", activeContext.cloneSourceUV);
                shader.SetInt("_Operation", ToShaderOperation(activeContext.tool));
                shader.SetInt("_BlendMode", (int)activeContext.brush.blendMode);
                shader.SetVector("_PaintColor", TexturePaintChannelUtility.ConstrainColor(active.channel,
                    sample.hasColor ? sample.color : active.color));
                shader.SetInt("_PaintSourceKind", (int)active.sourceKind);
                shader.SetInt("_VectorNormal", active.channel == TexturePaintChannel.Normal ? 1 : 0);
                shader.SetInt("_MaskMode", active.isLayerMask ? 1 : 0);
                shader.SetFloat("_MaskEraseValue", active.maskBaseValue);
                shader.SetTexture(kernel, "_PaintSource", active.paintSource != null ? active.paintSource : Texture2D.whiteTexture);
            }
            if (shader == normalShader)
            {
                TangentSpaceMaps maps = active.textures.tangentSpaceMaps;
                if (maps == null) return false;
                shader.SetTexture(kernel, "_VertexNormalMap", maps.vertexNormals);
                shader.SetTexture(kernel, "_TangentMap", maps.tangents);
                shader.SetTexture(kernel, "_SeamLookup", maps.seamLookup);
                shader.SetFloat("_SeamBlend", 1f);
            }
            if (activeContext.limitStrokeCoverage)
            {
                const int tileSize = 128;
                if (!CanAllocateCoverageTiles(active, rect, tileSize))
                {
                    Performance.budgetFallbacks++;
                    return false;
                }
                int minX = rect.xMin / tileSize, maxX = (rect.xMax - 1) / tileSize;
                int minY = rect.yMin / tileSize, maxY = (rect.yMax - 1) / tileSize;
                for (int tileY = minY; tileY <= maxY; tileY++)
                for (int tileX = minX; tileX <= maxX; tileX++)
                {
                    CoverageTile tile = EnsureGPUStrokeTile(active, new Vector2Int(tileX, tileY), tileSize);
                    RectInt intersection = Intersect(rect, tile.rect);
                    if (intersection.width <= 0 || intersection.height <= 0) continue;
                    shader.SetInts("_TileOffset", intersection.x, intersection.y);
                    shader.SetInts("_DispatchSize", intersection.width, intersection.height);
                    shader.SetInts("_CoverageOffset", tile.rect.x, tile.rect.y);
                    shader.SetTexture(kernel, "_StrokeBase", tile.strokeBase);
                    shader.SetTexture(kernel, "_StrokeCoverage", tile.coverage);
                    shader.Dispatch(kernel, Mathf.CeilToInt(intersection.width / 16f), Mathf.CeilToInt(intersection.height / 16f), 1);
                    Performance.computeDispatches++;
                }
            }
            else
            {
                shader.SetInts("_TileOffset", rect.x, rect.y);
                shader.SetInts("_DispatchSize", rect.width, rect.height);
                shader.SetInts("_CoverageOffset", 0, 0);
                shader.SetTexture(kernel, "_StrokeBase", active.target.Front);
                shader.SetTexture(kernel, "_StrokeCoverage", disabledStrokeCoverage);
                shader.Dispatch(kernel, Mathf.CeilToInt(rect.width / 16f), Mathf.CeilToInt(rect.height / 16f), 1);
                Performance.computeDispatches++;
            }
            active.target.SwapAndSynchronize(rect);
            Performance.copiedPixels += (long)rect.width * rect.height;
            return true;
        }

        private void ApplyCPU(ActiveTarget active, StrokeSample sample, BrushProjection projection,
            Vector4 uvToBrush, RectInt rect,
            Texture2D geometryMask)
        {
            Texture2D source = Readback(active.target.Front);
            Color[] pixels = source.GetPixels();
            Color[] original = (Color[])pixels.Clone();
            int width = source.width, height = source.height;
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                Vector2 uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                if (projection.restrictToTriangle && !PointInsideTriangle(uv,
                    projection.triangleUV0, projection.triangleUV1, projection.triangleUV2,
                    new Vector2(TriangleBoundaryPaddingTexels / width,
                        TriangleBoundaryPaddingTexels / height), projection.triangleBoundaryMask)) continue;
                float falloff = EvaluateBrush(uv, sample.uv, uvToBrush, activeContext.brush,
                    activeStampTexture, sample);
                if (falloff <= 0f) continue;
                float mask = 1f;
                if (geometryMask != null) mask *= geometryMask.GetPixelBilinear(uv.x, uv.y).r;
                float coverageLimit = Mathf.Clamp01(falloff * mask * activeContext.strength * active.contribution);
                float pressure = activeContext.pressureAffectsFlow ? Mathf.Clamp01(sample.pressure) : 1f;
                float weight = Mathf.Clamp01(coverageLimit * activeContext.brush.flow * pressure * Mathf.Max(0f, sample.flowMultiplier));
                int index = y * width + x;
                Color result = ApplyCPUOperation(active, original, index, x, y, width, height, uv, sample,
                    uvToBrush, weight, coverageLimit);
                if (active.isLayerMask)
                {
                    float value = Mathf.Clamp01(result.grayscale);
                    result = new Color(value, value, value, 1f);
                }
                pixels[index] = result;
            }
            source.SetPixels(pixels); source.Apply(false, false);
            Graphics.CopyTexture(source, 0, 0, rect.x, rect.y, rect.width, rect.height,
                active.target.Back, 0, 0, rect.x, rect.y);
            active.target.SwapAndSynchronize(rect);
            Performance.copiedPixels += (long)rect.width * rect.height;
            Destroy(source);
        }

        private static void SetTriangleRestriction(ComputeShader shader, BrushProjection projection)
        {
            shader.SetInt("_RestrictToTriangle", projection.restrictToTriangle ? 1 : 0);
            shader.SetVector("_TriangleUV0", new Vector4(projection.triangleUV0.x, projection.triangleUV0.y, 0f, 0f));
            shader.SetVector("_TriangleUV1", new Vector4(projection.triangleUV1.x, projection.triangleUV1.y, 0f, 0f));
            shader.SetVector("_TriangleUV2", new Vector4(projection.triangleUV2.x, projection.triangleUV2.y, 0f, 0f));
            shader.SetInt("_TriangleBoundaryMask", projection.triangleBoundaryMask);
            shader.SetFloat("_TriangleBoundaryPadding", TriangleBoundaryPaddingTexels);
        }

        internal static bool PointInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c,
            Vector2 halfTexel = default, int boundaryMask = 7)
        {
            float area = Cross2D(b - a, c - a);
            if (Mathf.Abs(area) < 0.000000001f) return false;
            float orientation = area >= 0f ? 1f : -1f;
            return InsideOwnedEdge(point, a, b, halfTexel, orientation, (boundaryMask & 1) != 0) &&
                InsideOwnedEdge(point, b, c, halfTexel, orientation, (boundaryMask & 2) != 0) &&
                InsideOwnedEdge(point, c, a, halfTexel, orientation, (boundaryMask & 4) != 0);
        }

        private static bool InsideOwnedEdge(Vector2 point, Vector2 a, Vector2 b,
            Vector2 halfTexel, float orientation, bool boundary)
        {
            Vector2 edge = b - a;
            float edgeValue = orientation * Cross2D(edge, point - a);
            if (boundary)
            {
                float texelExtent = Mathf.Abs(edge.y) * halfTexel.x + Mathf.Abs(edge.x) * halfTexel.y;
                return edgeValue >= -texelExtent - 0.0000001f;
            }
            if (edgeValue > 0.0000001f) return true;
            if (edgeValue < -0.0000001f) return false;
            Vector2 ownedDirection = orientation > 0f ? edge : -edge;
            return ownedDirection.y > 0f ||
                Mathf.Abs(ownedDirection.y) <= 0.0000001f && ownedDirection.x < 0f;
        }

        private static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static RectInt TrianglePixelRect(BrushProjection projection, int width, int height, int halo)
        {
            float minU = Mathf.Min(projection.triangleUV0.x, Mathf.Min(projection.triangleUV1.x, projection.triangleUV2.x));
            float minV = Mathf.Min(projection.triangleUV0.y, Mathf.Min(projection.triangleUV1.y, projection.triangleUV2.y));
            float maxU = Mathf.Max(projection.triangleUV0.x, Mathf.Max(projection.triangleUV1.x, projection.triangleUV2.x));
            float maxV = Mathf.Max(projection.triangleUV0.y, Mathf.Max(projection.triangleUV1.y, projection.triangleUV2.y));
            int xMin = Mathf.Clamp(Mathf.FloorToInt(minU * width) - halo, 0, width);
            int yMin = Mathf.Clamp(Mathf.FloorToInt(minV * height) - halo, 0, height);
            int xMax = Mathf.Clamp(Mathf.CeilToInt(maxU * width) + halo, 0, width);
            int yMax = Mathf.Clamp(Mathf.CeilToInt(maxV * height) + halo, 0, height);
            return xMax > xMin && yMax > yMin ? new RectInt(xMin, yMin, xMax - xMin, yMax - yMin) : default;
        }

        private Color ApplyCPUOperation(ActiveTarget active, Color[] source, int index, int x, int y, int width, int height,
            Vector2 uv, StrokeSample sample, Vector4 uvToBrush, float weight, float coverageLimit)
        {
            Color current = source[index];
            switch (activeContext.tool)
            {
                case TexturePaintTool.Erase:
                    return ApplyCPUStrokeDeposit(active, index, current,
                        active.isLayerMask ? TextureSet.MaskColor(active.maskBaseValue) :
                            TextureSet.DefaultColor(active.channel), weight, coverageLimit);
                case TexturePaintTool.Dodge:
                    return ApplyCPUStrokeDeposit(active, index, current, new Color(1f, 1f, 1f, current.a), weight, coverageLimit);
                case TexturePaintTool.Burn:
                    return ApplyCPUStrokeDeposit(active, index, current, new Color(0f, 0f, 0f, current.a), weight, coverageLimit);
                case TexturePaintTool.Blur:
                    Color average = Color.clear; int count = 0;
                    for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                    { int sx = Mathf.Clamp(x + ox, 0, width - 1), sy = Mathf.Clamp(y + oy, 0, height - 1); average += source[sy * width + sx]; count++; }
                    return ApplyCPUStrokeDeposit(active, index, current, average / count, weight, coverageLimit);
                case TexturePaintTool.Smear:
                    Vector2 delta = (sample.uv - sample.previousUV) * new Vector2(width, height);
                    int smearX = Mathf.Clamp(Mathf.RoundToInt(x - delta.x), 0, width - 1);
                    int smearY = Mathf.Clamp(Mathf.RoundToInt(y - delta.y), 0, height - 1);
                    return ApplyCPUStrokeDeposit(active, index, current, source[smearY * width + smearX], weight, coverageLimit);
                case TexturePaintTool.Clone:
                    Vector2 cloneUV = activeContext.cloneSourceUV + (uv - sample.uv);
                    int cloneX = Mathf.Clamp(Mathf.FloorToInt(cloneUV.x * width), 0, width - 1);
                    int cloneY = Mathf.Clamp(Mathf.FloorToInt(cloneUV.y * height), 0, height - 1);
                    return ApplyCPUStrokeDeposit(active, index, current, source[cloneY * width + cloneX], weight, coverageLimit);
                case TexturePaintTool.NormalTouchup:
                    TangentSpaceMaps maps = active.textures.tangentSpaceMaps;
                    if (maps == null) return current;
                    Color nc = maps.vertexNormals.GetPixelBilinear(uv.x, uv.y);
                    Color tc = maps.tangents.GetPixelBilinear(uv.x, uv.y);
                    Color normalSource = activeContext.limitStrokeCoverage ? GetCPUStrokeBase(active, index, current) : current;
                    Vector3 tangentNormal = new Vector3(normalSource.r * 2f - 1f, normalSource.g * 2f - 1f, normalSource.b * 2f - 1f).normalized;
                    Vector3 vertexNormal = new Vector3(nc.r * 2f - 1f, nc.g * 2f - 1f, nc.b * 2f - 1f).normalized;
                    Vector4 tangent = new Vector4(tc.r * 2f - 1f, tc.g * 2f - 1f, tc.b * 2f - 1f, tc.a >= 0.5f ? 1f : -1f);
                    Vector3 bent = TexturePaintMath.BendNormalTowardVertexNormal(tangentNormal, vertexNormal, tangent,
                        activeContext.limitStrokeCoverage ? 1f : weight);
                    Color normalCandidate = new Color(bent.x * 0.5f + 0.5f, bent.y * 0.5f + 0.5f, bent.z * 0.5f + 0.5f, current.a);
                    return activeContext.limitStrokeCoverage
                        ? ApplyCPUStrokeDeposit(active, index, current, normalCandidate, weight, coverageLimit)
                        : normalCandidate;
                default:
                    Color paint = SamplePaintSource(active, uv, sample.uv, uvToBrush, activeContext.brush, sample);
                    float paintWeight = weight * paint.a;
                    float paintCoverageLimit = coverageLimit * paint.a;
                    if (!activeContext.limitStrokeCoverage)
                        return active.channel == TexturePaintChannel.Normal
                            ? BlendNormal(current, paint, paintWeight)
                            : Blend(current, paint, paintWeight, activeContext.brush.blendMode);
                    Color strokeBase = GetCPUStrokeBase(active, index, current);
                    Color paintCandidate = active.channel == TexturePaintChannel.Normal
                        ? BlendNormal(strokeBase, paint, 1f)
                        : Blend(strokeBase, paint, 1f, activeContext.brush.blendMode);
                    return ApplyCPUStrokeDeposit(active, index, current, paintCandidate, paintWeight,
                        paintCoverageLimit, active.channel != TexturePaintChannel.Normal);
            }
        }

        private Color ApplyCPUStrokeDeposit(ActiveTarget active, int index, Color current, Color candidate,
            float requestedCoverage, float maximumCoverage, bool straightAlphaPaint = false)
        {
            if (!activeContext.limitStrokeCoverage)
                return straightAlphaPaint
                    ? CompositeStraightAlpha(current, candidate, requestedCoverage)
                    : Color.Lerp(current, candidate, requestedCoverage);
            float accumulated = active.cpuStrokeCoverage.TryGetValue(index, out float used) ? used : 0f;
            float contribution = TexturePaintMath.ConsumeStrokeCoverage(requestedCoverage, maximumCoverage, ref accumulated);
            active.cpuStrokeCoverage[index] = accumulated;
            Color strokeBase = GetCPUStrokeBase(active, index, current);
            Color deposited = straightAlphaPaint
                ? DepositStraightAlpha(current, strokeBase, candidate, contribution)
                : current + contribution * (candidate - strokeBase);
            if (active.channel == TexturePaintChannel.Normal)
            {
                Vector3 normal = new Vector3(deposited.r * 2f - 1f, deposited.g * 2f - 1f, deposited.b * 2f - 1f).normalized;
                deposited.r = normal.x * 0.5f + 0.5f;
                deposited.g = normal.y * 0.5f + 0.5f;
                deposited.b = normal.z * 0.5f + 0.5f;
            }
            return deposited;
        }

        private static Color GetCPUStrokeBase(ActiveTarget active, int index, Color current)
        {
            if (active.cpuStrokeBase.TryGetValue(index, out Color value)) return value;
            active.cpuStrokeBase[index] = current;
            return current;
        }

        private Color SamplePaintSource(ActiveTarget active, Vector2 uv, Vector2 brushCenter, Vector4 uvToBrush,
            BrushPreset brush, StrokeSample sample)
        {
            if (active.sourceKind == TexturePaintBrushSource.Color)
                return TexturePaintChannelUtility.ConstrainColor(active.channel,
                    sample.hasColor ? sample.color : active.color);
            if (!(active.paintSource is Texture2D texture)) return active.color;
            Vector2 sampleUV = uv;
            if (active.sourceKind == TexturePaintBrushSource.Texture)
            {
                Vector2 delta = ToBrushSpace(uv - brushCenter, uvToBrush);
                float angle = -(brush.rotation + sample.rotation) * Mathf.Deg2Rad;
                delta = new Vector2(delta.x * Mathf.Cos(angle) - delta.y * Mathf.Sin(angle), delta.x * Mathf.Sin(angle) + delta.y * Mathf.Cos(angle));
                Vector2 footprintScale = EffectiveScale(sample.footprintScale);
                delta = new Vector2(delta.x / Mathf.Abs(footprintScale.x),
                    delta.y / Mathf.Abs(footprintScale.y));
                Vector2 localUV = delta * 0.5f + Vector2.one * 0.5f;
                Vector2 sourceScale = EffectiveScale(sample.sourceUVScale);
                sampleUV = Vector2.Scale(localUV, sourceScale) + sample.sourceUVOffset;
            }
            try { return texture.GetPixelBilinear(sampleUV.x, sampleUV.y); }
            catch (UnityException) { return active.color; }
        }

        private static Color Blend(Color destination, Color source, float weight, TexturePaintBlendMode mode)
        {
            Color result;
            switch (mode)
            {
                case TexturePaintBlendMode.Multiply: result = destination * source; break;
                case TexturePaintBlendMode.Add: result = destination + source; break;
                case TexturePaintBlendMode.Subtract: result = destination - source; break;
                case TexturePaintBlendMode.Screen: result = Color.white - (Color.white - destination) * (Color.white - source); break;
                default: result = source; break;
            }
            return CompositeStraightAlpha(destination, result, weight);
        }

        internal static Color CompositeStraightAlpha(Color destination, Color source, float effectiveSourceAlpha)
        {
            float sourceAlpha = Mathf.Clamp01(effectiveSourceAlpha);
            float destinationAlpha = Mathf.Clamp01(destination.a);
            float outputAlpha = TexturePaintMath.SourceOverAlpha(destinationAlpha, sourceAlpha);
            if (outputAlpha <= 0.0000001f) return Color.clear;
            Vector3 outputPremultiplied = new Vector3(source.r, source.g, source.b) * sourceAlpha +
                new Vector3(destination.r, destination.g, destination.b) *
                (destinationAlpha * (1f - sourceAlpha));
            Vector3 output = outputPremultiplied / outputAlpha;
            return new Color(output.x, output.y, output.z, outputAlpha);
        }

        internal static Color DepositStraightAlpha(Color current, Color strokeBase, Color candidate,
            float contribution)
        {
            contribution = Mathf.Clamp01(contribution);
            float outputAlpha = current.a + contribution * (1f - strokeBase.a);
            if (outputAlpha <= 0.0000001f) return Color.clear;
            Vector3 outputPremultiplied = new Vector3(current.r, current.g, current.b) * current.a +
                contribution * (new Vector3(candidate.r, candidate.g, candidate.b) -
                    new Vector3(strokeBase.r, strokeBase.g, strokeBase.b) * strokeBase.a);
            Vector3 output = outputPremultiplied / outputAlpha;
            return new Color(output.x, output.y, output.z, outputAlpha);
        }

        private static Color BlendNormal(Color destination, Color source, float weight)
        {
            Vector3 a = new Vector3(destination.r * 2f - 1f, destination.g * 2f - 1f, destination.b * 2f - 1f).normalized;
            Vector3 b = new Vector3(source.r * 2f - 1f, source.g * 2f - 1f, source.b * 2f - 1f).normalized;
            Vector3 normal = Vector3.Slerp(a, b, Mathf.Clamp01(weight)).normalized;
            return new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f,
                normal.z * 0.5f + 0.5f, TexturePaintMath.SourceOverAlpha(destination.a, weight));
        }

        private static float EvaluateBrush(Vector2 uv, Vector2 center, Vector4 uvToBrush, BrushPreset brush,
            Texture2D stampTexture, StrokeSample sample)
        {
            Vector2 delta = ToBrushSpace(uv - center, uvToBrush);
            float angle = -(brush.rotation + sample.rotation) * Mathf.Deg2Rad;
            delta = new Vector2(delta.x * Mathf.Cos(angle) - delta.y * Mathf.Sin(angle), delta.x * Mathf.Sin(angle) + delta.y * Mathf.Cos(angle));
            Vector2 footprintScale = EffectiveScale(sample.footprintScale);
            delta = new Vector2(delta.x / Mathf.Abs(footprintScale.x),
                delta.y / Mathf.Abs(footprintScale.y));
            float distance = brush.shape == BrushPreset.Shape.Square ? Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) : delta.magnitude;
            if (distance >= 1f) return 0f;
            float softStart = Mathf.Clamp01(brush.hardness);
            float falloff = distance <= softStart ? 1f : 1f - Mathf.InverseLerp(softStart, 1f, distance);
            if (brush.shape == BrushPreset.Shape.Stamp && stampTexture != null)
            {
                try { falloff *= stampTexture.GetPixelBilinear(delta.x * 0.5f + 0.5f, delta.y * 0.5f + 0.5f).a; }
                catch (UnityException) { }
            }
            return falloff;
        }

        private static Vector2 EffectiveScale(Vector2 scale)
            => new Vector2(Mathf.Abs(scale.x) <= 0.000001f ? 1f : scale.x,
                Mathf.Abs(scale.y) <= 0.000001f ? 1f : scale.y);

        private static float FootprintRadiusScale(StrokeSample sample)
        {
            Vector2 scale = EffectiveScale(sample.footprintScale);
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        private static Vector2 ToBrushSpace(Vector2 uvDelta, Vector4 uvToBrush)
            => new Vector2(uvDelta.x * uvToBrush.x + uvDelta.y * uvToBrush.y,
                uvDelta.x * uvToBrush.z + uvDelta.y * uvToBrush.w);

        private static RectInt Union(RectInt a, RectInt b)
        {
            if (a.width <= 0 || a.height <= 0) return b;
            if (b.width <= 0 || b.height <= 0) return a;
            int xMin = Mathf.Min(a.xMin, b.xMin), yMin = Mathf.Min(a.yMin, b.yMin);
            int xMax = Mathf.Max(a.xMax, b.xMax), yMax = Mathf.Max(a.yMax, b.yMax);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static int ToShaderOperation(TexturePaintTool tool)
        {
            switch (tool)
            {
                case TexturePaintTool.Erase: return 1;
                case TexturePaintTool.Smear: return 2;
                case TexturePaintTool.Clone: return 3;
                case TexturePaintTool.Dodge: return 4;
                case TexturePaintTool.Burn: return 5;
                default: return 0;
            }
        }

        private static Texture2D Readback(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = source;
            Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); result.Apply(false, false);
            RenderTexture.active = previous; return result;
        }

        private Texture2D GetGeometryMask(ActiveTarget active, StrokeSample sample)
        {
            ReconstructedSurface surface = active.textures?.surface;
            if (surface == null) return null;
            EntityId meshId = surface.mesh != null
                ? surface.mesh.GetEntityId() : EntityId.None;
            string surfaceId = !string.IsNullOrEmpty(active.textures?.persistentId)
                ? active.textures.persistentId : surface.index.ToString();
            string key = surfaceId + "|" + meshId + "|" + active.target.Width + "|" +
                active.target.Height + "|" + sample.uvIsland + "|" +
                (sample.slotName ?? string.Empty) + "|" +
                GeometrySelectionSignature(activeContext.geometrySelection);
            if (geometryMasks.TryGetValue(key, out Texture2D result)) return result;
            while (geometryMaskOrder.Count >= 8)
            {
                string oldest = geometryMaskOrder.Dequeue();
                if (!geometryMasks.TryGetValue(oldest, out Texture2D expired)) continue;
                geometryMasks.Remove(oldest);
                Destroy(expired);
            }
            result = TexturePaintGeometryMask.Build(surface, active.target.Width, active.target.Height,
                sample.slotName, sample.uvIsland, activeContext.geometrySelection);
            geometryMasks[key] = result;
            geometryMaskOrder.Enqueue(key);
            Performance.geometryMaskBuilds++;
            return result;
        }

        private static int GeometrySelectionSignature(TexturePaintGeometrySelection selection)
        {
            unchecked
            {
                int hash = 17;
                IReadOnlyList<TexturePaintGeometrySelector> selectors = selection?.Selectors;
                if (selectors == null) return hash;
                for (int i = 0; i < selectors.Count; i++)
                {
                    TexturePaintGeometrySelector selector = selectors[i];
                    if (selector == null) { hash = hash * 31; continue; }
                    hash = hash * 31 + (selector.enabled ? 1 : 0);
                    hash = hash * 31 + (int)selector.kind;
                    hash = hash * 31 + selector.surfaceIndex;
                    for (int triangle = 0; triangle < selector.triangleIndices.Count; triangle++)
                        hash = hash * 31 + selector.triangleIndices[triangle];
                    hash = hash * 31 + 486187739;
                    for (int island = 0; island < selector.uvIslandIndices.Count; island++)
                        hash = hash * 31 + selector.uvIslandIndices[island];
                }
                return hash;
            }
        }

        internal static bool RequiresGeometryMask(TexturePaintGeometrySelection selection,
            bool restrictToTriangle, bool directUV = false)
            => !directUV && !restrictToTriangle;

        private static bool HasGeometryRestrictions(TexturePaintGeometrySelection selection)
        {
            IReadOnlyList<TexturePaintGeometrySelector> selectors = selection?.Selectors;
            if (selectors == null) return false;
            for (int i = 0; i < selectors.Count; i++)
                if (selectors[i]?.enabled == true &&
                    selectors[i].kind != TexturePaintGeometrySelectorKind.None) return true;
            return false;
        }

        private static RenderTexture CreateStrokeBase(RenderTexture source, int width, int height)
        {
            RenderTextureDescriptor descriptor = source.descriptor;
            descriptor.depthBufferBits = 0;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.msaaSamples = 1;
            descriptor.width = width;
            descriptor.height = height;
            RenderTexture result = new RenderTexture(descriptor)
            {
                name = source.name + " Stroke Base",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            result.Create();
            return result;
        }

        private static RenderTexture CreateCoverageBuffer(int width, int height, string name)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.RFloat, 0)
            {
                enableRandomWrite = true,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            RenderTexture result = new RenderTexture(descriptor)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            result.Create();
            return result;
        }

        private static void Clear(RenderTexture texture, Color color)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, color);
            RenderTexture.active = previous;
        }

        private CoverageTile EnsureGPUStrokeTile(ActiveTarget active, Vector2Int coordinate, int tileSize)
        {
            if (active.coverageTiles.TryGetValue(coordinate, out CoverageTile existing)) return existing;
            RectInt rect = Intersect(new RectInt(coordinate.x * tileSize, coordinate.y * tileSize, tileSize, tileSize),
                new RectInt(0, 0, active.target.Width, active.target.Height));
            CoverageTile tile = new CoverageTile
            {
                rect = rect,
                strokeBase = CreateStrokeBase(active.target.Front, rect.width, rect.height),
                coverage = CreateCoverageBuffer(rect.width, rect.height, active.target.Front.name + " Coverage " + coordinate)
            };
            Graphics.CopyTexture(active.target.Front, 0, 0, rect.x, rect.y, rect.width, rect.height,
                tile.strokeBase, 0, 0, 0, 0);
            Clear(tile.coverage, Color.clear);
            active.coverageTiles[coordinate] = tile;
            activeCoverageBytes += EstimateCoverageTileBytes(tile);
            return tile;
        }

        private bool CanAllocateCoverageTiles(ActiveTarget active, RectInt rect, int tileSize)
        {
            long required = 0L;
            int minX = rect.xMin / tileSize, maxX = (rect.xMax - 1) / tileSize;
            int minY = rect.yMin / tileSize, maxY = (rect.yMax - 1) / tileSize;
            for (int tileY = minY; tileY <= maxY; tileY++)
            for (int tileX = minX; tileX <= maxX; tileX++)
            {
                Vector2Int coordinate = new Vector2Int(tileX, tileY);
                if (active.coverageTiles.ContainsKey(coordinate)) continue;
                RectInt tileRect = Intersect(new RectInt(tileX * tileSize, tileY * tileSize, tileSize, tileSize),
                    new RectInt(0, 0, active.target.Width, active.target.Height));
                uint baseBytes = GraphicsFormatUtility.GetBlockSize(active.target.Front.graphicsFormat);
                required += (long)tileRect.width * tileRect.height * (baseBytes + 4L);
            }
            return activeCoverageBytes + required <= Math.Max(1024L * 1024L, CoverageMemoryBudgetBytes);
        }

        private static long EstimateCoverageTileBytes(CoverageTile tile)
        {
            uint baseBytes = GraphicsFormatUtility.GetBlockSize(tile.strokeBase.graphicsFormat);
            return (long)tile.rect.width * tile.rect.height * (baseBytes + 4L);
        }

        private void EnsureDisabledStrokeCoverage()
        {
            if (disabledStrokeCoverage != null) return;
            disabledStrokeCoverage = CreateCoverageBuffer(1, 1, "Texture Paint Disabled Stroke Coverage");
            Clear(disabledStrokeCoverage, Color.clear);
        }

        private void ReleaseStrokeBuffers()
        {
            for (int i = 0; i < activeTargets.Count; i++)
            {
                ActiveTarget active = activeTargets[i];
                foreach (CoverageTile tile in active.coverageTiles.Values)
                {
                    DestroyRenderTexture(tile.strokeBase);
                    DestroyRenderTexture(tile.coverage);
                }
                active.coverageTiles.Clear();
                active.cpuStrokeBase.Clear();
                active.cpuStrokeCoverage.Clear();
            }
            activeCoverageBytes = 0L;
        }

        private void ReleaseGeometryMasks()
        {
            foreach (Texture2D mask in geometryMasks.Values) Destroy(mask);
            geometryMasks.Clear();
            geometryMaskOrder.Clear();
        }

        private static void DestroyRenderTexture(RenderTexture value)
        {
            if (value == null) return;
            value.Release();
            Destroy(value);
        }

        private static RectInt Intersect(RectInt a, RectInt b)
        {
            int xMin = Mathf.Max(a.xMin, b.xMin), yMin = Mathf.Max(a.yMin, b.yMin);
            int xMax = Mathf.Min(a.xMax, b.xMax), yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax > xMin && yMax > yMin ? new RectInt(xMin, yMin, xMax - xMin, yMax - yMin) : default;
        }

        private static double ElapsedMilliseconds(long started)
            => (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;

        private static void Destroy(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        public void Dispose()
        {
            EndStroke(false);
            batchStampBuffer?.Dispose();
            batchStampBuffer = null;
            DestroyRenderTexture(disabledStrokeCoverage);
            disabledStrokeCoverage = null;
            ReleaseGeometryMasks();
            foreach (Texture2D curve in ribbonCurveTextures.Values) Destroy(curve);
            ribbonCurveTextures.Clear();
            Destroy(directUVRibbonMesh);
            directUVRibbonMesh = null;
            proceduralRasterBounds.Clear();
            previousProceduralBounds.Clear();
            currentProceduralBounds.Clear();
            history.Dispose();
            Destroy(ribbonMaterial);
        }
    }
}
