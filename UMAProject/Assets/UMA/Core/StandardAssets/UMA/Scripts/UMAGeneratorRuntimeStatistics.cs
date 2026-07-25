using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA
{
    /// <summary>
    /// Captures generator and frame timing in a player build and exports the
    /// results as an Excel-friendly CSV file.
    /// </summary>
    [AddComponentMenu("UMA/Runtime/UMA Generator Runtime Statistics")]
    public sealed class UMAGeneratorRuntimeStatistics : MonoBehaviour
    {
        [Serializable]
        public readonly struct RuntimeCapture
        {
            public long FrameCount { get; }
            public double ElapsedSeconds { get; }
            public double TotalFrameMilliseconds { get; }
            public double AverageFrameMilliseconds =>
                FrameCount > 0 ? TotalFrameMilliseconds / FrameCount : 0d;
            public double MaximumFrameMilliseconds { get; }

            public RuntimeCapture(
                long frameCount,
                double elapsedSeconds,
                double totalFrameMilliseconds,
                double maximumFrameMilliseconds)
            {
                FrameCount = frameCount;
                ElapsedSeconds = elapsedSeconds;
                TotalFrameMilliseconds = totalFrameMilliseconds;
                MaximumFrameMilliseconds = maximumFrameMilliseconds;
            }
        }

        [Tooltip("Generator to measure. Leave empty to find the active scene generator at runtime.")]
        public UMAGeneratorBuiltin Generator;

        [Tooltip("Show Reset Timing and Save Timing CSV controls in a development or release player.")]
        public bool ShowRuntimeControls = true;

        [Tooltip("Automatically write one CSV after the generator has been busy and then becomes idle.")]
        public bool AutoSaveWhenGeneratorBecomesIdle;

        [Min(1)]
        [Tooltip("Idle frames to wait before auto-saving. Waiting at least one frame captures the frame time of the final generation update.")]
        public int AutoSaveIdleFrameDelay = 2;

        [Tooltip("Prefix used for CSV files written to Application.persistentDataPath.")]
        public string FileNamePrefix = "UMA-Generator-Statistics";

        [Tooltip("Screen-space position and size of the runtime timing controls.")]
        public Rect RuntimeControlsRect = new Rect(10f, 10f, 310f, 145f);

        public string LastSavedPath { get; private set; }
        public string LastStatus { get; private set; }

        private readonly Stopwatch captureStopwatch = new Stopwatch();
        private long capturedFrameCount;
        private double totalFrameMilliseconds;
        private double maximumFrameMilliseconds;
        private bool generatorWasBusy;
        private bool autoSaveCompleted;
        private int idleFramesAfterBusy;

        private void OnEnable()
        {
            RestartFrameCapture();
            ResolveGenerator();
        }

        private void Update()
        {
            RecordFrameTiming();
            ResolveGenerator();

            if (Generator == null)
            {
                return;
            }

            if (!Generator.IsIdle() || Generator.QueueSize() > 0)
            {
                generatorWasBusy = true;
                idleFramesAfterBusy = 0;
                return;
            }

            if (AutoSaveWhenGeneratorBecomesIdle &&
                generatorWasBusy &&
                !autoSaveCompleted)
            {
                idleFramesAfterBusy++;
                if (idleFramesAfterBusy < Mathf.Max(1, AutoSaveIdleFrameDelay))
                {
                    return;
                }
                autoSaveCompleted = true;
                SaveStatisticsCsv();
            }
        }

        private void OnGUI()
        {
            if (!ShowRuntimeControls)
            {
                return;
            }

            GUILayout.BeginArea(RuntimeControlsRect, "UMA Runtime Timing", GUI.skin.window);
            GUILayout.Label(
                Generator != null
                    ? $"Generator: {Generator.name}"
                    : "Waiting for the scene generator...");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Timing"))
            {
                ResetStatistics();
            }
            GUI.enabled = Generator != null;
            if (GUILayout.Button("Save Timing CSV"))
            {
                SaveStatisticsCsv();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            RuntimeCapture capture = GetRuntimeCapture();
            GUILayout.Label(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} frames | avg {1:F3} ms | max {2:F3} ms",
                    capture.FrameCount,
                    capture.AverageFrameMilliseconds,
                    capture.MaximumFrameMilliseconds));
            if (!string.IsNullOrEmpty(LastStatus))
            {
                GUILayout.Label(LastStatus);
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// Clears the active generator metrics and restarts frame timing.
        /// The generator queue and any active generation operation are not changed.
        /// </summary>
        public void ResetStatistics()
        {
            ResolveGenerator();
            Generator?.ResetStatistics();
            RestartFrameCapture();
            generatorWasBusy = Generator != null && !Generator.IsIdle();
            idleFramesAfterBusy = 0;
            autoSaveCompleted = false;
            LastStatus = "Timing reset.";
        }

        /// <summary>
        /// Writes the current runtime capture to Application.persistentDataPath.
        /// </summary>
        public string SaveStatisticsCsv()
        {
            ResolveGenerator();
            if (Generator == null)
            {
                LastStatus = "No active UMA generator was found.";
                UnityEngine.Debug.LogWarning(
                    "UMA runtime timing could not be saved because no active UMAGenerator was found.",
                    this);
                return null;
            }

            try
            {
                DateTime capturedUtc = DateTime.UtcNow;
                string csv = CreateCsv(
                    Generator,
                    GetRuntimeCapture(),
                    capturedUtc,
                    SceneManager.GetActiveScene().name);
                string fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}-{1}.csv",
                    SanitizeFileName(FileNamePrefix),
                    capturedUtc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
                string path = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllText(path, csv, new UTF8Encoding(true));

                LastSavedPath = path;
                LastStatus = $"Saved: {fileName}";
                UnityEngine.Debug.Log($"UMA runtime timing CSV saved to '{path}'.", this);
                return path;
            }
            catch (Exception exception)
            {
                LastStatus = $"CSV save failed: {exception.Message}";
                UnityEngine.Debug.LogException(exception, this);
                return null;
            }
        }

        public RuntimeCapture GetRuntimeCapture()
        {
            return new RuntimeCapture(
                capturedFrameCount,
                captureStopwatch.Elapsed.TotalSeconds,
                totalFrameMilliseconds,
                maximumFrameMilliseconds);
        }

        private void ResolveGenerator()
        {
            if (Generator != null)
            {
                return;
            }

            UMAAssetIndexer indexer = UMAAssetIndexer.bareInstance;
            if (indexer != null && indexer.bareGenerator != null)
            {
                Generator = indexer.bareGenerator;
                return;
            }

            Generator = FindFirstObjectByType<UMAGenerator>(
                FindObjectsInactive.Exclude);
        }

        private void RecordFrameTiming()
        {
            double frameMilliseconds = Time.unscaledDeltaTime * 1000d;
            capturedFrameCount++;
            totalFrameMilliseconds += frameMilliseconds;
            if (frameMilliseconds > maximumFrameMilliseconds)
            {
                maximumFrameMilliseconds = frameMilliseconds;
            }
        }

        private void RestartFrameCapture()
        {
            capturedFrameCount = 0;
            totalFrameMilliseconds = 0d;
            maximumFrameMilliseconds = 0d;
            captureStopwatch.Restart();
        }

        private static string SanitizeFileName(string value)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "UMA-Generator-Statistics"
                : value.Trim();
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidCharacters.Length; i++)
            {
                result = result.Replace(invalidCharacters[i], '_');
            }
            return result;
        }

        /// <summary>
        /// Creates a rectangular CSV suitable for importing into Excel. Each
        /// generator phase and incremental atomic step is emitted as a row.
        /// </summary>
        public static string CreateCsv(
            UMAGeneratorBuiltin generator,
            RuntimeCapture capture,
            DateTime capturedUtc,
            string sceneName)
        {
            if (generator == null)
            {
                throw new ArgumentNullException(nameof(generator));
            }

            var rows = new List<StatisticsRow>();
            AddGeneratorRows(rows, generator);
            AddAtomicStepRows(rows, generator);
            AddCounterRows(rows, generator);

            var builder = new StringBuilder(8192);
            AppendHeader(builder);
            for (int i = 0; i < rows.Count; i++)
            {
                AppendRow(
                    builder,
                    generator,
                    capture,
                    capturedUtc,
                    sceneName,
                    rows[i]);
            }
            return builder.ToString();
        }

        private static void AddGeneratorRows(
            List<StatisticsRow> rows,
            UMAGeneratorBuiltin generator)
        {
            AddTimingRow(rows, "Generator Phase", "Generator Work",
                0, generator.ElapsedTicks, 0d, 0d);
            AddTimingRow(rows, "Generator Phase", "Validation",
                0, generator.validationTicks, 0d, 0d);
            AddTimingRow(rows, "Generator Phase", "Mesh Preprocess",
                0, generator.meshpreprocessTicks, 0d, 0d);
            AddTimingRow(rows, "Generator Phase", "Character Begun Events",
                0, generator.BegunEventsTicks, 0d, 0d);
            AddTimingRow(rows, "Generator Phase", "Pre Apply",
                0, generator.preapplyTicks, 0d, 0d);
            AddTimingRow(rows, "Generator Phase", "Texture Processing",
                generator.TextureChanged,
                generator.textureprocessingTicks,
                generator.averageTextureProcessingTime,
                0d);
            AddTimingRow(rows, "Generator Phase", "Mesh Updates",
                generator.SlotsChanged,
                generator.meshUpdatesTicks,
                generator.averageMeshUpdatesTime,
                0d);
            AddTimingRow(rows, "Generator Phase", "Skeleton Updates",
                generator.DnaChanged,
                generator.skeletonUpdatesTicks,
                generator.averageSkeletonUpdatesTime,
                0d);
            AddTimingRow(rows, "Generator Phase", "Race Blendshapes",
                0, generator.raceblendshapesTicks, 0d, 0d);
            AddTimingRow(rows, "Generator Phase", "End Events",
                0, generator.endEventsTicks, 0d, 0d);
            AddTimingRow(rows, "Incremental Summary", "Last Generation Latency",
                1, generator.lastMultiStepGenerationLatencyTicks,
                UMATime.StopwatchTicksToMilliseconds(
                    generator.lastMultiStepGenerationLatencyTicks),
                UMATime.StopwatchTicksToMilliseconds(
                    generator.lastMultiStepGenerationLatencyTicks));
            AddTimingRow(rows, "Incremental Summary", "Maximum Generation Latency",
                1, generator.maximumMultiStepGenerationLatencyTicks,
                UMATime.StopwatchTicksToMilliseconds(
                    generator.maximumMultiStepGenerationLatencyTicks),
                UMATime.StopwatchTicksToMilliseconds(
                    generator.maximumMultiStepGenerationLatencyTicks));
            AddTimingRow(rows, "Incremental Summary", "Discarded Mesh Work",
                0, generator.multiStepDiscardedMeshTicks, 0d, 0d);
        }

        private static void AddAtomicStepRows(
            List<StatisticsRow> rows,
            UMAGeneratorBuiltin generator)
        {
            var stepStatistics =
                new List<UMAGeneratorBuiltin.MultiStepAtomicStepStatistic>();
            var overrunStatistics =
                new List<UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>();
            generator.GetMultiStepAtomicStepStatistics(stepStatistics);
            generator.GetMultiStepBudgetOverrunStatistics(overrunStatistics);

            var overrunsByStep =
                new Dictionary<string, UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>(
                    StringComparer.Ordinal);
            for (int i = 0; i < overrunStatistics.Count; i++)
            {
                overrunsByStep[overrunStatistics[i].StepName] =
                    overrunStatistics[i];
            }

            for (int i = 0; i < stepStatistics.Count; i++)
            {
                UMAGeneratorBuiltin.MultiStepAtomicStepStatistic statistic =
                    stepStatistics[i];
                overrunsByStep.TryGetValue(
                    statistic.StepName,
                    out UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic overrun);
                rows.Add(new StatisticsRow
                {
                    MetricType = "Incremental Step",
                    MetricName = statistic.StepName,
                    Count = statistic.Count,
                    TotalMilliseconds = statistic.TotalMilliseconds,
                    AverageMilliseconds = statistic.AverageMilliseconds,
                    MaximumMilliseconds = statistic.MaximumMilliseconds,
                    BudgetOverrunCount = overrun.Count,
                    MaximumOverrunMilliseconds =
                        overrun.MaximumOverrunMilliseconds
                });
            }

            foreach (KeyValuePair<string, UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>
                     entry in overrunsByStep)
            {
                bool hasTimingRow = stepStatistics.Exists(
                    statistic => statistic.StepName == entry.Key);
                if (hasTimingRow)
                {
                    continue;
                }

                UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic overrun =
                    entry.Value;
                rows.Add(new StatisticsRow
                {
                    MetricType = "Incremental Step",
                    MetricName = overrun.StepName,
                    Count = overrun.Count,
                    MaximumMilliseconds = overrun.MaximumStepMilliseconds,
                    BudgetOverrunCount = overrun.Count,
                    MaximumOverrunMilliseconds =
                        overrun.MaximumOverrunMilliseconds
                });
            }
        }

        private static void AddCounterRows(
            List<StatisticsRow> rows,
            UMAGeneratorBuiltin generator)
        {
            AddCounterRow(rows, "Pending UMAs", generator.pendingUmas);
            AddCounterRow(rows, "Generated UMAs",
                generator.umaDatasGenerated != null
                    ? generator.umaDatasGenerated.Count
                    : 0);
            AddCounterRow(rows, "Shape Dirty", generator.DnaChanged);
            AddCounterRow(rows, "Texture Dirty", generator.TextureChanged);
            AddCounterRow(rows, "Mesh Dirty", generator.SlotsChanged);
            AddCounterRow(rows, "Textures Processed", generator.TexturesProcessed);
            AddCounterRow(rows, "Budget Overruns",
                generator.multiStepBudgetOverrunCount);
            AddCounterRow(rows, "Async Waits",
                generator.multiStepWaitingForAsyncCount);
            AddCounterRow(rows, "Restarts", generator.multiStepRestartCount);
            AddCounterRow(rows, "Cancellations",
                generator.multiStepCancellationCount);
            AddCounterRow(rows, "Failures", generator.multiStepFailureCount);
            AddCounterRow(rows, "Render Texture Copies Enqueued",
                RenderTexToCPU.copiesEnqueued);
            AddCounterRow(rows, "Render Texture Copies Dequeued",
                RenderTexToCPU.copiesDequeued);
            AddCounterRow(rows, "Render Texture Queue Failures",
                RenderTexToCPU.unableToQueue);
            AddCounterRow(rows, "Render Texture Missed Uploads",
                RenderTexToCPU.misseduploads);
            AddCounterRow(rows, "Render Texture Upload Errors",
                RenderTexToCPU.errorUploads);
            AddCounterRow(rows, "Textures Uploaded",
                RenderTexToCPU.texturesUploaded);
        }

        private static void AddTimingRow(
            List<StatisticsRow> rows,
            string metricType,
            string metricName,
            long count,
            long stopwatchTicks,
            double averageMilliseconds,
            double maximumMilliseconds)
        {
            rows.Add(new StatisticsRow
            {
                MetricType = metricType,
                MetricName = metricName,
                Count = count,
                TotalMilliseconds =
                    UMATime.StopwatchTicksToMilliseconds(stopwatchTicks),
                AverageMilliseconds = averageMilliseconds,
                MaximumMilliseconds = maximumMilliseconds
            });
        }

        private static void AddCounterRow(
            List<StatisticsRow> rows,
            string name,
            long value)
        {
            rows.Add(new StatisticsRow
            {
                MetricType = "Counter",
                MetricName = name,
                Count = value
            });
        }

        private static void AppendHeader(StringBuilder builder)
        {
            builder.AppendLine(
                "Captured UTC,Scene,Application Version,Unity Version,Platform," +
                "Device Model,Operating System,Processor,CPU Cores,System Memory MB," +
                "Graphics Device,Graphics API,Graphics Memory MB,Generator,Mesh Combiner," +
                "Atlas Resolution,Max Queued Conversions,Iteration Count,Inter Frame Delay," +
                "Incremental Budget MS,Capture Seconds,Frames Sampled,Average Frame MS," +
                "Maximum Frame MS,Metric Type,Metric Name,Count,Total MS,Average MS," +
                "Maximum MS,Budget Overrun Count,Maximum Overrun MS,Notes");
        }

        private static void AppendRow(
            StringBuilder builder,
            UMAGeneratorBuiltin generator,
            RuntimeCapture capture,
            DateTime capturedUtc,
            string sceneName,
            StatisticsRow row)
        {
            AppendCsvValue(builder,
                capturedUtc.ToString("O", CultureInfo.InvariantCulture));
            AppendCsvValue(builder, sceneName);
            AppendCsvValue(builder, Application.version);
            AppendCsvValue(builder, Application.unityVersion);
            AppendCsvValue(builder, Application.platform.ToString());
            AppendCsvValue(builder, SystemInfo.deviceModel);
            AppendCsvValue(builder, SystemInfo.operatingSystem);
            AppendCsvValue(builder, SystemInfo.processorType);
            AppendCsvValue(builder, SystemInfo.processorCount);
            AppendCsvValue(builder, SystemInfo.systemMemorySize);
            AppendCsvValue(builder, SystemInfo.graphicsDeviceName);
            AppendCsvValue(builder, SystemInfo.graphicsDeviceType.ToString());
            AppendCsvValue(builder, SystemInfo.graphicsMemorySize);
            AppendCsvValue(builder, generator.name);
            AppendCsvValue(builder,
                generator.meshCombiner != null
                    ? generator.meshCombiner.GetType().Name
                    : string.Empty);
            AppendCsvValue(builder, generator.atlasResolution);
            AppendCsvValue(builder, generator.MaxQueuedConversionsPerFrame);
            AppendCsvValue(builder, generator.IterationCount);
            AppendCsvValue(builder, generator.InterFrameDelay);
            AppendCsvValue(builder, generator.MaxMultiStepWorkMilliseconds);
            AppendCsvValue(builder, capture.ElapsedSeconds);
            AppendCsvValue(builder, capture.FrameCount);
            AppendCsvValue(builder, capture.AverageFrameMilliseconds);
            AppendCsvValue(builder, capture.MaximumFrameMilliseconds);
            AppendCsvValue(builder, row.MetricType);
            AppendCsvValue(builder, row.MetricName);
            AppendCsvValue(builder, row.Count);
            AppendCsvValue(builder, row.TotalMilliseconds);
            AppendCsvValue(builder, row.AverageMilliseconds);
            AppendCsvValue(builder, row.MaximumMilliseconds);
            AppendCsvValue(builder, row.BudgetOverrunCount);
            AppendCsvValue(builder, row.MaximumOverrunMilliseconds);
            AppendCsvValue(builder, row.Notes, true);
        }

        private static void AppendCsvValue(
            StringBuilder builder,
            object value,
            bool endOfLine = false)
        {
            string text;
            if (value == null)
            {
                text = string.Empty;
            }
            else if (value is IFormattable formattable)
            {
                text = formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            else
            {
                text = value.ToString();
            }

            bool needsQuotes =
                text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (needsQuotes)
            {
                builder.Append('"');
                builder.Append(text.Replace("\"", "\"\""));
                builder.Append('"');
            }
            else
            {
                builder.Append(text);
            }

            if (endOfLine)
            {
                builder.AppendLine();
            }
            else
            {
                builder.Append(',');
            }
        }

        private sealed class StatisticsRow
        {
            public string MetricType;
            public string MetricName;
            public long Count;
            public double TotalMilliseconds;
            public double AverageMilliseconds;
            public double MaximumMilliseconds;
            public long BudgetOverrunCount;
            public double MaximumOverrunMilliseconds;
            public string Notes;
        }
    }
}
