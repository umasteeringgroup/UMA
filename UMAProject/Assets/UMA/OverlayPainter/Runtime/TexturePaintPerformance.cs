using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintStrokeDiagnosticPhase
    {
        BeginStroke,
        InputEvent,
        InputRaycast,
        SampleProjection,
        ContactDiscovery,
        ContactPreparation,
        PaintPreparation,
        HistoryCapture,
        RasterSubmit,
        Composite,
        PreviewBinding,
        EndStroke,
        Count
    }

    public readonly struct TexturePaintStrokeDiagnosticScope : IDisposable
    {
        private readonly TexturePaintStrokeDiagnostics owner;
        private readonly TexturePaintStrokeDiagnosticPhase phase;
        private readonly long started;
        private readonly long allocatedBefore;
        private readonly bool inputEvent;
        private readonly ProfilerMarker marker;

        internal TexturePaintStrokeDiagnosticScope(TexturePaintStrokeDiagnostics owner,
            TexturePaintStrokeDiagnosticPhase phase, bool inputEvent)
        {
            this.owner = owner;
            this.phase = phase;
            this.inputEvent = inputEvent;
            started = Stopwatch.GetTimestamp();
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            marker = owner.GetMarker(phase);
            marker.Begin();
        }

        public void Dispose()
        {
            if (owner == null) return;
            marker.End();
            owner.RecordPhase(phase, Stopwatch.GetTimestamp() - started, inputEvent,
                Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore));
        }
    }

    /// <summary>
    /// Opt-in, one-stroke diagnostics. It is disabled by default and retains no per-stroke data
    /// unless Capture Next Stroke has been requested. This keeps the instrumentation easy to turn
    /// off while still exposing the complete CPU submission path in the Unity Profiler.
    /// </summary>
    public sealed class TexturePaintStrokeDiagnostics
    {
        private const int PhaseCount = (int)TexturePaintStrokeDiagnosticPhase.Count;
        private readonly long[] phaseTicks = new long[PhaseCount];
        private readonly int[] phaseCalls = new int[PhaseCount];
        private readonly long[] phaseMaximumTicks = new long[PhaseCount];
        private readonly long[] phaseAllocatedBytes = new long[PhaseCount];
        private readonly ProfilerMarker[] phaseMarkers = new ProfilerMarker[PhaseCount];
        private readonly List<double> inputEventMilliseconds = new List<double>(256);
        private bool enabled;
        private bool captureRequested;
        private long strokeStarted;
        private TexturePaintTool tool;
        private TexturePaintChannel channel;
        private bool mirrored;
        private bool directUV;
        private int requestedTextureSets;
        private int activeTargets;
        private long startCopiedPixels;
        private long startComposedPixels;
        private int startComputeDispatches;
        private int startCpuFallbacks;
        private int startBudgetFallbacks;
        private int startGeometryMaskBuilds;
        private long startAllocatedBytes;
        private int rawInputEvents;
        private int resampledCenters;
        private int ordinaryFootprints;
        private int mirroredFootprints;
        private int raycasts;
        private int raycastSurfaces;
        private int colliderQueries;
        private int raycastHits;
        private int spatialQueries;
        private int spatialGridBuilds;
        private long spatialCells;
        private long candidateReferences;
        private long uniqueCandidates;
        private long acceptedContacts;
        private int paintOperations;
        private int rasterTargetVisits;
        private int historyIncludeCalls;
        private int completedCenterSamples;
        private int previousContactEntries;
        private int storedStrokeSamples;
        private int historyTiles;
        private int coverageTiles;
        private long coverageBytes;

        public TexturePaintStrokeDiagnostics()
        {
            for (int i = 0; i < phaseMarkers.Length; i++)
                phaseMarkers[i] = new ProfilerMarker("OverlayPainter.Stroke." +
                    ((TexturePaintStrokeDiagnosticPhase)i));
        }

        public bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                if (!enabled)
                {
                    captureRequested = false;
                    IsCapturing = false;
                }
            }
        }

        public bool CaptureRequested => captureRequested;
        public bool IsCapturing { get; private set; }
        public string LastSummary { get; private set; }
        public string LastReport { get; private set; }

        public void RequestNextStroke()
        {
            enabled = true;
            captureRequested = true;
        }

        public void CancelRequestedCapture() => captureRequested = false;

        public void ClearReport()
        {
            LastSummary = null;
            LastReport = null;
        }

        internal bool TryBegin(TexturePaintTool paintTool, TexturePaintChannel paintChannel,
            bool mirrorEnabled, bool textureSpace, int textureSetCount,
            TexturePaintPerformanceMetrics metrics)
        {
            if (!enabled || !captureRequested || IsCapturing) return false;
            captureRequested = false;
            IsCapturing = true;
            Array.Clear(phaseTicks, 0, phaseTicks.Length);
            Array.Clear(phaseCalls, 0, phaseCalls.Length);
            Array.Clear(phaseMaximumTicks, 0, phaseMaximumTicks.Length);
            Array.Clear(phaseAllocatedBytes, 0, phaseAllocatedBytes.Length);
            inputEventMilliseconds.Clear();
            tool = paintTool;
            channel = paintChannel;
            mirrored = mirrorEnabled;
            directUV = textureSpace;
            requestedTextureSets = textureSetCount;
            activeTargets = 0;
            rawInputEvents = resampledCenters = ordinaryFootprints = mirroredFootprints = 0;
            raycasts = raycastSurfaces = colliderQueries = raycastHits = 0;
            spatialQueries = spatialGridBuilds = 0;
            spatialCells = candidateReferences = uniqueCandidates = acceptedContacts = 0L;
            paintOperations = rasterTargetVisits = historyIncludeCalls = 0;
            completedCenterSamples = previousContactEntries = storedStrokeSamples = 0;
            historyTiles = coverageTiles = 0;
            coverageBytes = 0L;
            startCopiedPixels = metrics?.copiedPixels ?? 0L;
            startComposedPixels = metrics?.composedPixels ?? 0L;
            startComputeDispatches = metrics?.computeDispatches ?? 0;
            startCpuFallbacks = metrics?.cpuFallbacks ?? 0;
            startBudgetFallbacks = metrics?.budgetFallbacks ?? 0;
            startGeometryMaskBuilds = metrics?.geometryMaskBuilds ?? 0;
            startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            strokeStarted = Stopwatch.GetTimestamp();
            return true;
        }

        public TexturePaintStrokeDiagnosticScope Measure(TexturePaintStrokeDiagnosticPhase phase)
            => IsCapturing && (int)phase >= 0 && phase < TexturePaintStrokeDiagnosticPhase.Count
                ? new TexturePaintStrokeDiagnosticScope(this, phase, false)
                : default;

        public TexturePaintStrokeDiagnosticScope MeasureInputEvent()
        {
            if (!IsCapturing) return default;
            rawInputEvents++;
            return new TexturePaintStrokeDiagnosticScope(this,
                TexturePaintStrokeDiagnosticPhase.InputEvent, true);
        }

        public void RecordResampledCenter()
        {
            if (IsCapturing) resampledCenters++;
        }

        public void RecordFootprintQuery(bool mirror)
        {
            if (!IsCapturing) return;
            if (mirror) mirroredFootprints++;
            else ordinaryFootprints++;
        }

        public void RecordRaycast(int surfacesConsidered, int queriedColliders, int hits)
        {
            if (!IsCapturing) return;
            raycasts++;
            raycastSurfaces += Mathf.Max(0, surfacesConsidered);
            colliderQueries += Mathf.Max(0, queriedColliders);
            raycastHits += Mathf.Max(0, hits);
        }

        public void RecordSpatialQuery(bool builtGrid, int visitedCells, int references,
            int candidates, int contacts)
        {
            if (!IsCapturing) return;
            spatialQueries++;
            if (builtGrid) spatialGridBuilds++;
            spatialCells += Math.Max(0, visitedCells);
            candidateReferences += Math.Max(0, references);
            uniqueCandidates += Math.Max(0, candidates);
            acceptedContacts += Math.Max(0, contacts);
        }

        public void RecordPaintOperation(int count = 1)
        {
            if (IsCapturing) paintOperations += Mathf.Max(0, count);
        }

        public void RecordRasterTargetVisit()
        {
            if (IsCapturing) rasterTargetVisits++;
        }

        public void RecordHistoryInclude()
        {
            if (IsCapturing) historyIncludeCalls++;
        }

        internal void SetActiveTargets(int count)
        {
            if (IsCapturing) activeTargets = Mathf.Max(activeTargets, count);
        }

        public void RecordWorkingSet(int completedSamples, int contactEntries)
        {
            if (!IsCapturing) return;
            completedCenterSamples = Mathf.Max(completedCenterSamples, completedSamples);
            previousContactEntries = Mathf.Max(previousContactEntries, contactEntries);
        }

        internal void RecordEngineWorkingSet(int strokeSamples, int pendingHistoryTiles,
            int activeCoverageTiles, long activeCoverageBytes)
        {
            if (!IsCapturing) return;
            storedStrokeSamples = Mathf.Max(storedStrokeSamples, strokeSamples);
            historyTiles = Mathf.Max(historyTiles, pendingHistoryTiles);
            coverageTiles = Mathf.Max(coverageTiles, activeCoverageTiles);
            coverageBytes = Math.Max(coverageBytes, activeCoverageBytes);
        }

        internal ProfilerMarker GetMarker(TexturePaintStrokeDiagnosticPhase phase)
            => phaseMarkers[Mathf.Clamp((int)phase, 0, phaseMarkers.Length - 1)];

        internal void RecordPhase(TexturePaintStrokeDiagnosticPhase phase, long ticks,
            bool inputEvent, long allocatedBytes)
        {
            if (!IsCapturing || ticks < 0L) return;
            int index = (int)phase;
            phaseTicks[index] += ticks;
            phaseCalls[index]++;
            phaseAllocatedBytes[index] += Math.Max(0L, allocatedBytes);
            if (ticks > phaseMaximumTicks[index]) phaseMaximumTicks[index] = ticks;
            if (inputEvent) inputEventMilliseconds.Add(ToMilliseconds(ticks));
        }

        internal void Abort(string reason, TexturePaintPerformanceMetrics metrics)
            => Complete(false, metrics, string.IsNullOrEmpty(reason) ? "aborted" : reason);

        internal void Complete(bool committed, TexturePaintPerformanceMetrics metrics,
            string outcome = null)
        {
            if (!IsCapturing) return;
            long elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - strokeStarted);
            int computeDispatches = Math.Max(0, (metrics?.computeDispatches ?? 0) - startComputeDispatches);
            int cpuFallbacks = Math.Max(0, (metrics?.cpuFallbacks ?? 0) - startCpuFallbacks);
            int budgetFallbacks = Math.Max(0, (metrics?.budgetFallbacks ?? 0) - startBudgetFallbacks);
            int geometryMasks = Math.Max(0, (metrics?.geometryMaskBuilds ?? 0) - startGeometryMaskBuilds);
            long copiedPixels = Math.Max(0L, (metrics?.copiedPixels ?? 0L) - startCopiedPixels);
            long composedPixels = Math.Max(0L, (metrics?.composedPixels ?? 0L) - startComposedPixels);
            long allocatedBytes = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes);
            double duration = ToMilliseconds(elapsedTicks);
            double early = SegmentP95(0, inputEventMilliseconds.Count / 3);
            double middle = SegmentP95(inputEventMilliseconds.Count / 3,
                inputEventMilliseconds.Count / 3);
            double late = SegmentP95(inputEventMilliseconds.Count * 2 / 3,
                inputEventMilliseconds.Count - inputEventMilliseconds.Count * 2 / 3);
            string resolvedOutcome = outcome ?? (committed ? "committed" : "cancelled");
            double growth = early > 0.000001d ? late / early : 0d;
            LastSummary = $"{tool} {duration:0.0} ms · input p95 {early:0.00}/{middle:0.00}/{late:0.00} ms " +
                $"· contacts {acceptedContacts} · paint ops {paintOperations} · dispatches {computeDispatches}";

            var report = new StringBuilder(2048);
            report.AppendLine("OVERLAY PAINTER STROKE DIAGNOSTICS");
            report.Append("Outcome: ").AppendLine(resolvedOutcome);
            report.Append("Tool/channel: ").Append(tool).Append(" / ").AppendLine(channel.ToString());
            report.Append("Domain: ").Append(directUV ? "2D texture" : "3D surface")
                .Append(" · mirror: ").AppendLine(mirrored ? "on" : "off");
            report.Append("Duration: ").Append(duration.ToString("0.00")).AppendLine(" ms");
            report.Append("Texture sets / active raster targets: ").Append(requestedTextureSets)
                .Append(" / ").AppendLine(activeTargets.ToString());
            report.Append("Input events / resampled centers: ").Append(rawInputEvents).Append(" / ")
                .AppendLine(resampledCenters.ToString());
            report.Append("Normal / mirrored footprint queries: ").Append(ordinaryFootprints).Append(" / ")
                .AppendLine(mirroredFootprints.ToString());
            report.Append("Input-event p95 early / middle / late: ").Append(early.ToString("0.00"))
                .Append(" / ").Append(middle.ToString("0.00")).Append(" / ")
                .Append(late.ToString("0.00")).AppendLine(" ms");
            report.Append("Late / early latency ratio: ")
                .AppendLine(growth > 0d ? growth.ToString("0.00") + "x" : "n/a");
            report.Append("Raycasts / considered surfaces / collider queries / hits: ").Append(raycasts)
                .Append(" / ").Append(raycastSurfaces).Append(" / ").Append(colliderQueries)
                .Append(" / ").AppendLine(raycastHits.ToString());
            report.Append("Spatial queries / grid builds / cells: ").Append(spatialQueries).Append(" / ")
                .Append(spatialGridBuilds).Append(" / ").AppendLine(spatialCells.ToString());
            report.Append("Triangle references / unique candidates / accepted contacts: ")
                .Append(candidateReferences).Append(" / ").Append(uniqueCandidates).Append(" / ")
                .AppendLine(acceptedContacts.ToString());
            report.Append("Paint operations / raster-target visits: ").Append(paintOperations).Append(" / ")
                .AppendLine(rasterTargetVisits.ToString());
            report.Append("Compute dispatches / CPU fallbacks / budget fallbacks: ").Append(computeDispatches)
                .Append(" / ").Append(cpuFallbacks).Append(" / ").AppendLine(budgetFallbacks.ToString());
            report.Append("Copied / composed pixels: ").Append(copiedPixels).Append(" / ")
                .AppendLine(composedPixels.ToString());
            report.Append("Managed allocations on capture thread: ").Append(allocatedBytes)
                .AppendLine(" bytes");
            report.Append("History include calls / captured tiles: ").Append(historyIncludeCalls)
                .Append(" / ").AppendLine(historyTiles.ToString());
            report.Append("Coverage tiles / bytes: ").Append(coverageTiles).Append(" / ")
                .AppendLine(coverageBytes.ToString());
            report.Append("Completed centers / contact-state entries / stored raster samples: ")
                .Append(completedCenterSamples).Append(" / ").Append(previousContactEntries).Append(" / ")
                .AppendLine(storedStrokeSamples.ToString());
            report.Append("Geometry masks built: ").AppendLine(geometryMasks.ToString());
            report.AppendLine("CPU phase totals (total / calls / maximum / nested allocated bytes):");
            for (int i = 0; i < PhaseCount; i++)
            {
                if (phaseCalls[i] == 0) continue;
                report.Append("  ").Append((TexturePaintStrokeDiagnosticPhase)i).Append(": ")
                    .Append(ToMilliseconds(phaseTicks[i]).ToString("0.00")).Append(" ms / ")
                    .Append(phaseCalls[i]).Append(" / ")
                    .Append(ToMilliseconds(phaseMaximumTicks[i]).ToString("0.00")).Append(" ms / ")
                    .AppendLine(phaseAllocatedBytes[i].ToString());
            }
            report.AppendLine("CPU phase values measure main-thread submission; nested phase totals can overlap. " +
                "Use the Unity GPU Profiler with the OverlayPainter.Stroke markers to confirm GPU execution " +
                "or queue pressure.");
            LastReport = report.ToString();
            IsCapturing = false;
        }

        private double SegmentP95(int start, int count)
        {
            if (count <= 0 || start < 0 || start >= inputEventMilliseconds.Count) return 0d;
            count = Math.Min(count, inputEventMilliseconds.Count - start);
            double[] values = new double[count];
            inputEventMilliseconds.CopyTo(start, values, 0, count);
            Array.Sort(values);
            return values[Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.95f) - 1,
                0, values.Length - 1)];
        }

        private static double ToMilliseconds(long ticks)
            => ticks * 1000d / Stopwatch.Frequency;
    }

    public readonly struct TexturePaintOperationContext
    {
        public readonly CancellationToken cancellationToken;
        public readonly IProgress<float> progress;

        public TexturePaintOperationContext(CancellationToken cancellationToken, IProgress<float> progress = null)
        {
            this.cancellationToken = cancellationToken;
            this.progress = progress;
        }

        public void ThrowIfCancellationRequested() => cancellationToken.ThrowIfCancellationRequested();
        public void Report(float value) => progress?.Report(Mathf.Clamp01(value));
    }

    public sealed class TexturePaintPerformanceMetrics
    {
        private readonly Queue<double> previewMilliseconds = new Queue<double>();
        private const int WindowSize = 256;
        public long copiedPixels;
        public long composedPixels;
        public int computeDispatches;
        public int cpuFallbacks;
        public int budgetFallbacks;
        public int geometryMaskBuilds;
        public double LastPreviewMilliseconds { get; private set; }
        public double MaximumPreviewMilliseconds { get; private set; }
        public TexturePaintStrokeDiagnostics StrokeDiagnostics { get; } =
            new TexturePaintStrokeDiagnostics();

        public void RecordPreview(double milliseconds)
        {
            LastPreviewMilliseconds = milliseconds;
            MaximumPreviewMilliseconds = Math.Max(MaximumPreviewMilliseconds, milliseconds);
            previewMilliseconds.Enqueue(milliseconds);
            while (previewMilliseconds.Count > WindowSize) previewMilliseconds.Dequeue();
        }

        public double PreviewP95Milliseconds
        {
            get
            {
                if (previewMilliseconds.Count == 0) return 0d;
                double[] values = previewMilliseconds.ToArray();
                Array.Sort(values);
                return values[Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.95f) - 1, 0, values.Length - 1)];
            }
        }

        public void Reset()
        {
            previewMilliseconds.Clear(); copiedPixels = composedPixels = 0L;
            computeDispatches = cpuFallbacks = budgetFallbacks = geometryMaskBuilds = 0;
            LastPreviewMilliseconds = MaximumPreviewMilliseconds = 0d;
        }
    }

    public readonly struct TexturePaintResourceSnapshot
    {
        public readonly int renderTextures;
        public readonly int textures;

        public TexturePaintResourceSnapshot(int renderTextures, int textures)
        {
            this.renderTextures = renderTextures;
            this.textures = textures;
        }

        public int RenderTextureDelta(TexturePaintResourceSnapshot before) => renderTextures - before.renderTextures;
        public int TextureDelta(TexturePaintResourceSnapshot before) => textures - before.textures;
    }

    public static class TexturePaintResourceDiagnostics
    {
        public static TexturePaintResourceSnapshot Capture()
        {
            int renderTextures = 0, textures = 0;
            RenderTexture[] allRenderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>();
            for (int i = 0; i < allRenderTextures.Length; i++)
                if (IsOwned(allRenderTextures[i])) renderTextures++;
            Texture2D[] allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < allTextures.Length; i++)
                if (IsOwned(allTextures[i])) textures++;
            return new TexturePaintResourceSnapshot(renderTextures, textures);
        }

        private static bool IsOwned(UnityEngine.Object value)
        {
            if (value == null || (value.hideFlags & HideFlags.HideAndDontSave) == 0) return false;
            string name = value.name ?? string.Empty;
            return name.Contains("Texture Paint") || name.Contains("Stroke") || name.Contains("Coverage") ||
                name.Contains("Vertex Normal Map") || name.Contains("Vertex Tangent Map") || name.Contains("UV Seam Lookup");
        }
    }
}
