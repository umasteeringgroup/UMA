#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    /// <summary>
    /// Captures a repeatable, low-overhead baseline while an UMA crowd scene is
    /// running. The report is intentionally independent of the future
    /// incremental combiner so the same capture can be used for comparisons.
    /// </summary>
    public sealed class UMAGenerationBaselineWindow : EditorWindow
    {
        private const string WindowTitle = "UMA Generation Baseline";
        private const string DefaultOutputDirectory = "ProfilerCaptures";
        private const string BlendshapeStressRace = "Human Female 3.0";

        [Serializable]
        private sealed class GeneratorCounterSnapshot
        {
            public long elapsedTicks;
            public long dnaChanged;
            public long textureChanged;
            public long slotsChanged;
            public long texturesProcessed;
            public long validationTicks;
            public long meshPreprocessTicks;
            public long begunEventsTicks;
            public long preApplyTicks;
            public long textureProcessingTicks;
            public long meshUpdatesTicks;
            public long skeletonUpdatesTicks;
            public long raceBlendshapesTicks;
            public long endEventsTicks;
            public long multiStepBudgetOverruns;
            public long multiStepWaitingForAsync;
            public long multiStepRestarts;
            public long multiStepCancellations;
            public long multiStepFailures;
            public long lastMultiStepLatencyTicks;
            public long maximumMultiStepLatencyTicks;
            public long multiStepDiscardedMeshTicks;

            public static GeneratorCounterSnapshot Capture(UMAGeneratorBuiltin generator)
            {
                if (generator == null)
                {
                    return new GeneratorCounterSnapshot();
                }

                return new GeneratorCounterSnapshot
                {
                    elapsedTicks = generator.ElapsedTicks,
                    dnaChanged = generator.DnaChanged,
                    textureChanged = generator.TextureChanged,
                    slotsChanged = generator.SlotsChanged,
                    texturesProcessed = generator.TexturesProcessed,
                    validationTicks = generator.validationTicks,
                    meshPreprocessTicks = generator.meshpreprocessTicks,
                    begunEventsTicks = generator.BegunEventsTicks,
                    preApplyTicks = generator.preapplyTicks,
                    textureProcessingTicks = generator.textureprocessingTicks,
                    meshUpdatesTicks = generator.meshUpdatesTicks,
                    skeletonUpdatesTicks = generator.skeletonUpdatesTicks,
                    raceBlendshapesTicks = generator.raceblendshapesTicks,
                    endEventsTicks = generator.endEventsTicks,
                    multiStepBudgetOverruns =
                        generator.multiStepBudgetOverrunCount,
                    multiStepWaitingForAsync =
                        generator.multiStepWaitingForAsyncCount,
                    multiStepRestarts =
                        generator.multiStepRestartCount,
                    multiStepCancellations =
                        generator.multiStepCancellationCount,
                    multiStepFailures =
                        generator.multiStepFailureCount,
                    lastMultiStepLatencyTicks =
                        generator.lastMultiStepGenerationLatencyTicks,
                    maximumMultiStepLatencyTicks =
                        generator.maximumMultiStepGenerationLatencyTicks,
                    multiStepDiscardedMeshTicks =
                        generator.multiStepDiscardedMeshTicks
                };
            }

            public static GeneratorCounterSnapshot Subtract(
                GeneratorCounterSnapshot end,
                GeneratorCounterSnapshot start)
            {
                return new GeneratorCounterSnapshot
                {
                    elapsedTicks = end.elapsedTicks - start.elapsedTicks,
                    dnaChanged = end.dnaChanged - start.dnaChanged,
                    textureChanged = end.textureChanged - start.textureChanged,
                    slotsChanged = end.slotsChanged - start.slotsChanged,
                    texturesProcessed = end.texturesProcessed - start.texturesProcessed,
                    validationTicks = end.validationTicks - start.validationTicks,
                    meshPreprocessTicks = end.meshPreprocessTicks - start.meshPreprocessTicks,
                    begunEventsTicks = end.begunEventsTicks - start.begunEventsTicks,
                    preApplyTicks = end.preApplyTicks - start.preApplyTicks,
                    textureProcessingTicks = end.textureProcessingTicks - start.textureProcessingTicks,
                    meshUpdatesTicks = end.meshUpdatesTicks - start.meshUpdatesTicks,
                    skeletonUpdatesTicks = end.skeletonUpdatesTicks - start.skeletonUpdatesTicks,
                    raceBlendshapesTicks = end.raceBlendshapesTicks - start.raceBlendshapesTicks,
                    endEventsTicks = end.endEventsTicks - start.endEventsTicks,
                    multiStepBudgetOverruns =
                        end.multiStepBudgetOverruns -
                        start.multiStepBudgetOverruns,
                    multiStepWaitingForAsync =
                        end.multiStepWaitingForAsync -
                        start.multiStepWaitingForAsync,
                    multiStepRestarts =
                        end.multiStepRestarts -
                        start.multiStepRestarts,
                    multiStepCancellations =
                        end.multiStepCancellations -
                        start.multiStepCancellations,
                    multiStepFailures =
                        end.multiStepFailures -
                        start.multiStepFailures,
                    lastMultiStepLatencyTicks =
                        end.lastMultiStepLatencyTicks,
                    maximumMultiStepLatencyTicks =
                        end.maximumMultiStepLatencyTicks,
                    multiStepDiscardedMeshTicks =
                        end.multiStepDiscardedMeshTicks -
                        start.multiStepDiscardedMeshTicks
                };
            }
        }

        [Serializable]
        private sealed class GeneratorConfiguration
        {
            public string generatorType;
            public string meshCombinerType;
            public int atlasResolution;
            public int iterationCount;
            public int interFrameDelay;
            public float maxMultiStepWorkMilliseconds;
            public bool processAllPending;
            public bool convertRenderTexture;
            public bool useAsyncConversion;
            public bool asyncMipRegen;
            public bool convertMipMaps;
            public bool multiThreadTextureConversion;
            public int maxQueuedConversionsPerFrame;
            public bool allowReadFromMesh;
            public bool alwaysRegenerateRenderers;

            public static GeneratorConfiguration Capture(UMAGeneratorBase generator)
            {
                var result = new GeneratorConfiguration
                {
                    generatorType = generator != null ? generator.GetType().FullName : string.Empty
                };

                if (generator == null)
                {
                    return result;
                }

                result.atlasResolution = generator.atlasResolution;
                result.convertRenderTexture = generator.convertRenderTexture;
                result.useAsyncConversion = generator.useAsyncConversion;
                result.asyncMipRegen = generator.asyncMipRegen;
                result.convertMipMaps = generator.convertMipMaps;
                result.multiThreadTextureConversion = generator.MultiThreadTextureConversion;
                result.maxQueuedConversionsPerFrame = generator.MaxQueuedConversionsPerFrame;
                result.alwaysRegenerateRenderers = generator.alwaysRegenerateRenderers;

                if (generator is UMAGeneratorBuiltin builtin)
                {
                    result.meshCombinerType = builtin.meshCombiner != null
                        ? builtin.meshCombiner.GetType().FullName
                        : string.Empty;
                    result.iterationCount = builtin.IterationCount;
                    result.interFrameDelay = builtin.InterFrameDelay;
                    result.maxMultiStepWorkMilliseconds = builtin.MaxMultiStepWorkMilliseconds;
                    result.processAllPending = builtin.processAllPending;
                    result.allowReadFromMesh = builtin.AllowReadFromMesh;
                }

                return result;
            }
        }

        [Serializable]
        private sealed class BaselineReport
        {
            public string reportVersion = "1";
            public string capturedUtc;
            public string unityVersion;
            public string operatingSystem;
            public string deviceModel;
            public string processor;
            public int processorCount;
            public int systemMemoryMb;
            public string graphicsDevice;
            public int graphicsMemoryMb;
            public string graphicsApi;
            public string scenePath;
            public int expectedAvatarCount;
            public int observedAvatarCount;
            public string blendshapeStressRace;
            public int dynamicCharacterAvatarCount;
            public int stressRaceAvatarCount;
            public int stressRaceBlendshapesEnabledCount;
            public int stressRaceAllFramesEnabledCount;
            public int generatedBlendshapeCount;
            public int generatedBlendshapeFrameCount;
            public bool blendshapeStressFixtureValid;
            public int startFrame;
            public int endFrame;
            public int sampledFrames;
            public double durationSeconds;
            public int peakGeneratorQueue;
            public float meanFrameMs;
            public float medianFrameMs;
            public float p95FrameMs;
            public float p99FrameMs;
            public float maximumFrameMs;
            public long startAllocatedMemoryBytes;
            public long endAllocatedMemoryBytes;
            public long peakAllocatedMemoryBytes;
            public long peakReservedMemoryBytes;
            public long peakMonoUsedMemoryBytes;
            public long peakGraphicsDriverMemoryBytes;
            public long startManagedMemoryBytes;
            public long endManagedMemoryBytes;
            public long peakManagedMemoryBytes;
            public float averageTextureProcessingMs;
            public float averageMeshUpdateMs;
            public float averageSkeletonUpdateMs;
            public long incrementalBlendshapePreparationTicks;
            public long incrementalAddBlendshapeFrameTicks;
            public long incrementalBlendshapeFramesPrepared;
            public long incrementalBlendshapeFramesApplied;
            public GeneratorConfiguration generator;
            public GeneratorCounterSnapshot generatorCounterDelta;
            public float[] frameMilliseconds;
        }

        [SerializeField] private UMAGeneratorBase generator;
        [SerializeField] private int expectedAvatarCount = 96;
        [SerializeField] private bool stopWhenQueueDrains = true;
        [SerializeField] private int idleFramesBeforeStop = 3;
        [SerializeField] private Vector2 scrollPosition;

        private readonly List<float> frameMilliseconds = new List<float>(512);
        private ProfilerRecorder mainThreadRecorder;
        private bool mainThreadRecorderStarted;
        private bool captureActive;
        private bool observedQueuedWork;
        private int consecutiveIdleFrames;
        private int lastSampledFrame = -1;
        private int startFrame;
        private double startTime;
        private int peakQueue;
        private long lastObservedGeneratorTicks;
        private long startAllocatedMemory;
        private long peakAllocatedMemory;
        private long peakReservedMemory;
        private long peakMonoUsedMemory;
        private long peakGraphicsDriverMemory;
        private long startManagedMemory;
        private long peakManagedMemory;
        private long startBlendshapePreparationTicks;
        private long startAddBlendshapeFrameTicks;
        private long startBlendshapeFramesPrepared;
        private long startBlendshapeFramesApplied;
        private GeneratorCounterSnapshot startCounters;
        private BaselineReport lastReport;
        private string lastSavedPath;

        [MenuItem("UMA/Testing/Generation Baseline...", priority = 2003)]
        public static void OpenWindow()
        {
            var window = GetWindow<UMAGenerationBaselineWindow>(WindowTitle);
            window.minSize = new Vector2(560f, 430f);
            window.TryFindGenerator();
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            TryFindGenerator();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            DisposeRecorder();
            captureActive = false;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Crowd Generation Baseline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Enter Play Mode, start capture immediately before creating the crowd, then generate the avatars. " +
                $"Blendshape baselines must use '{BlendshapeStressRace}' with Load BlendShapes enabled. " +
                "The capture can stop automatically after queued work has been observed and the generator remains idle.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(captureActive))
            {
                generator = (UMAGeneratorBase)EditorGUILayout.ObjectField(
                    "Generator",
                    generator,
                    typeof(UMAGeneratorBase),
                    true);
                expectedAvatarCount = Mathf.Max(1, EditorGUILayout.IntField("Expected UMA Count", expectedAvatarCount));
                stopWhenQueueDrains = EditorGUILayout.Toggle("Stop When Queue Drains", stopWhenQueueDrains);
                using (new EditorGUI.DisabledScope(!stopWhenQueueDrains))
                {
                    idleFramesBeforeStop = Mathf.Max(
                        1,
                        EditorGUILayout.IntField("Stable Idle Frames", idleFramesBeforeStop));
                }
            }

            EditorGUILayout.Space();
            DrawCaptureControls();
            EditorGUILayout.Space();
            DrawCaptureStatus();
            EditorGUILayout.Space();
            DrawLastReport();
        }

        private void DrawCaptureControls()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(
                       captureActive || !Application.isPlaying || generator == null))
            {
                if (GUILayout.Button("Start Capture", GUILayout.Height(30f)))
                {
                    StartCapture();
                }
            }

            using (new EditorGUI.DisabledScope(!captureActive))
            {
                if (GUILayout.Button("Stop and Save", GUILayout.Height(30f)))
                {
                    StopCapture(true);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to capture frame and generator timing.", MessageType.Warning);
            }
            else if (generator == null)
            {
                EditorGUILayout.HelpBox("Assign the active UMA generator.", MessageType.Warning);
            }
        }

        private void DrawCaptureStatus()
        {
            EditorGUILayout.LabelField("Capture Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", captureActive ? "Recording" : "Stopped");
            EditorGUILayout.LabelField("Sampled Frames", frameMilliseconds.Count.ToString());
            EditorGUILayout.LabelField("Peak Queue", peakQueue.ToString());
            if (captureActive && generator != null)
            {
                EditorGUILayout.LabelField("Current Queue", generator.QueueSize().ToString());
            }
            else if (Application.isPlaying)
            {
                DrawBlendshapeFixtureStatus();
            }
        }

        private void DrawLastReport()
        {
            EditorGUILayout.LabelField("Last Report", EditorStyles.boldLabel);
            if (lastReport == null)
            {
                EditorGUILayout.HelpBox("No baseline has been captured in this window session.", MessageType.None);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("Scene", lastReport.scenePath);
            EditorGUILayout.LabelField("Observed UMAs", lastReport.observedAvatarCount.ToString());
            EditorGUILayout.LabelField(
                "Blendshape Stress Fixture",
                lastReport.blendshapeStressFixtureValid ? "Valid" : "Invalid");
            EditorGUILayout.LabelField(
                "Generated Blendshapes",
                $"{lastReport.generatedBlendshapeCount} shapes / {lastReport.generatedBlendshapeFrameCount} frames");
            EditorGUILayout.LabelField("Duration", $"{lastReport.durationSeconds:F3} s");
            EditorGUILayout.LabelField("Frames", lastReport.sampledFrames.ToString());
            EditorGUILayout.LabelField("Mean", $"{lastReport.meanFrameMs:F3} ms");
            EditorGUILayout.LabelField("Median", $"{lastReport.medianFrameMs:F3} ms");
            EditorGUILayout.LabelField("P95", $"{lastReport.p95FrameMs:F3} ms");
            EditorGUILayout.LabelField("P99", $"{lastReport.p99FrameMs:F3} ms");
            EditorGUILayout.LabelField("Maximum", $"{lastReport.maximumFrameMs:F3} ms");
            EditorGUILayout.LabelField("Average Mesh Update", $"{lastReport.averageMeshUpdateMs:F3} ms");
            EditorGUILayout.LabelField("Peak Allocated", FormatBytes(lastReport.peakAllocatedMemoryBytes));
            EditorGUILayout.LabelField("Peak Graphics Driver", FormatBytes(lastReport.peakGraphicsDriverMemoryBytes));
            EditorGUILayout.LabelField("Saved To", string.IsNullOrEmpty(lastSavedPath) ? "Not saved" : lastSavedPath);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Report As..."))
            {
                SaveReportAs(lastReport);
            }
            if (GUILayout.Button("Copy Summary"))
            {
                EditorGUIUtility.systemCopyBuffer = BuildSummary(lastReport);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OnEditorUpdate()
        {
            if (!captureActive)
            {
                return;
            }

            if (!Application.isPlaying || generator == null)
            {
                StopCapture(true);
                return;
            }

            if (Time.frameCount == lastSampledFrame)
            {
                return;
            }
            lastSampledFrame = Time.frameCount;

            int queueSize = generator.QueueSize();
            peakQueue = Mathf.Max(peakQueue, queueSize);
            bool generatorAdvanced = false;
            if (generator is UMAGeneratorBuiltin builtin &&
                builtin.ElapsedTicks != lastObservedGeneratorTicks)
            {
                lastObservedGeneratorTicks = builtin.ElapsedTicks;
                generatorAdvanced = true;
            }

            if (queueSize > 0 || generatorAdvanced)
            {
                observedQueuedWork = true;
                consecutiveIdleFrames = 0;
            }
            else if (observedQueuedWork && generator.IsIdle())
            {
                consecutiveIdleFrames++;
            }
            else
            {
                consecutiveIdleFrames = 0;
            }

            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (mainThreadRecorderStarted && mainThreadRecorder.Valid && mainThreadRecorder.Count > 0)
            {
                frameMs = (float)(mainThreadRecorder.LastValue * 1e-6);
            }
            if (float.IsFinite(frameMs) && frameMs >= 0f)
            {
                frameMilliseconds.Add(frameMs);
            }

            CaptureMemoryPeaks();

            if (stopWhenQueueDrains &&
                observedQueuedWork &&
                consecutiveIdleFrames >= idleFramesBeforeStop)
            {
                StopCapture(true);
            }

            Repaint();
        }

        private void StartCapture()
        {
            if (!Application.isPlaying || generator == null)
            {
                return;
            }

            DynamicCharacterAvatar[] existingAvatars = FindDynamicCharacterAvatars();
            int invalidExistingStressAvatars = 0;
            for (int i = 0; i < existingAvatars.Length; i++)
            {
                if (IsStressRace(existingAvatars[i]) && !existingAvatars[i].loadBlendShapes)
                {
                    invalidExistingStressAvatars++;
                }
            }
            if (invalidExistingStressAvatars > 0)
            {
                EditorUtility.DisplayDialog(
                    "Invalid Blendshape Baseline",
                    $"{invalidExistingStressAvatars} '{BlendshapeStressRace}' DCA object(s) have " +
                    "Load BlendShapes disabled. Enable it before starting the baseline.",
                    "OK");
                return;
            }

            frameMilliseconds.Clear();
            captureActive = true;
            observedQueuedWork = generator.QueueSize() > 0;
            consecutiveIdleFrames = 0;
            lastSampledFrame = Time.frameCount;
            startFrame = Time.frameCount;
            startTime = EditorApplication.timeSinceStartup;
            peakQueue = generator.QueueSize();
            lastObservedGeneratorTicks = generator is UMAGeneratorBuiltin builtin
                ? builtin.ElapsedTicks
                : 0L;
            startAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            peakAllocatedMemory = startAllocatedMemory;
            peakReservedMemory = Profiler.GetTotalReservedMemoryLong();
            peakMonoUsedMemory = Profiler.GetMonoUsedSizeLong();
            peakGraphicsDriverMemory = Profiler.GetAllocatedMemoryForGraphicsDriver();
            startManagedMemory = GC.GetTotalMemory(false);
            peakManagedMemory = startManagedMemory;
            startCounters = GeneratorCounterSnapshot.Capture(generator as UMAGeneratorBuiltin);
            startBlendshapePreparationTicks =
                SkinnedMeshCombinerMeshAPI
                    .Ticks_BlendShapeFramePreparation;
            startAddBlendshapeFrameTicks =
                SkinnedMeshCombinerMeshAPI
                    .Ticks_AddBlendShapeFrame;
            startBlendshapeFramesPrepared =
                SkinnedMeshCombinerMeshAPI
                    .BlendShapeFramesPrepared;
            startBlendshapeFramesApplied =
                SkinnedMeshCombinerMeshAPI
                    .BlendShapeFramesApplied;
            lastReport = null;
            lastSavedPath = string.Empty;

            DisposeRecorder();
            try
            {
                mainThreadRecorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Internal,
                    "Main Thread",
                    1);
                mainThreadRecorderStarted = mainThreadRecorder.Valid;
            }
            catch (Exception exception)
            {
                mainThreadRecorderStarted = false;
                Debug.LogWarning(
                    $"[UMA] Main Thread profiler recorder was unavailable. " +
                    $"The baseline will use unscaled frame time. {exception.Message}");
            }
        }

        private void StopCapture(bool saveAutomatically)
        {
            if (!captureActive)
            {
                return;
            }

            captureActive = false;
            DisposeRecorder();
            CaptureMemoryPeaks();

            lastReport = BuildReport();
            if (saveAutomatically)
            {
                lastSavedPath = SaveReportAutomatically(lastReport);
            }

            if (lastReport.observedAvatarCount != expectedAvatarCount)
            {
                Debug.LogWarning(
                    $"[UMA] Baseline captured {lastReport.observedAvatarCount} UMAData objects; " +
                    $"the expected count was {expectedAvatarCount}. The report was still saved.");
            }
            if (!lastReport.blendshapeStressFixtureValid)
            {
                Debug.LogWarning(
                    $"[UMA] Blendshape stress baseline is invalid. Use '{BlendshapeStressRace}' and enable " +
                    "Load BlendShapes on every matching DynamicCharacterAvatar. The report was still saved for diagnostics.");
            }

            Debug.Log(BuildSummary(lastReport));
            Repaint();
        }

        private BaselineReport BuildReport()
        {
            var samples = frameMilliseconds.ToArray();
            var sorted = (float[])samples.Clone();
            Array.Sort(sorted);

            var endCounters = GeneratorCounterSnapshot.Capture(generator as UMAGeneratorBuiltin);
            GeneratorCounterSnapshot delta = GeneratorCounterSnapshot.Subtract(endCounters, startCounters);
            DynamicCharacterAvatar[] avatars = FindDynamicCharacterAvatars();
            int stressRaceAvatarCount = 0;
            int stressRaceBlendshapesEnabledCount = 0;
            int stressRaceAllFramesEnabledCount = 0;
            for (int i = 0; i < avatars.Length; i++)
            {
                DynamicCharacterAvatar avatar = avatars[i];
                if (!IsStressRace(avatar))
                {
                    continue;
                }

                stressRaceAvatarCount++;
                if (avatar.loadBlendShapes)
                {
                    stressRaceBlendshapesEnabledCount++;
                }
                if (avatar.loadBlendShapes && avatar.loadAllFrames)
                {
                    stressRaceAllFramesEnabledCount++;
                }
            }

            CountGeneratedBlendshapes(
                out int generatedBlendshapeCount,
                out int generatedBlendshapeFrameCount);

            var report = new BaselineReport
            {
                capturedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                deviceModel = SystemInfo.deviceModel,
                processor = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemoryMb = SystemInfo.systemMemorySize,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsMemoryMb = SystemInfo.graphicsMemorySize,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                scenePath = SceneManager.GetActiveScene().path,
                expectedAvatarCount = expectedAvatarCount,
                observedAvatarCount = UMAObjectUtility.FindObjectsByType<UMAData>(
                    FindObjectsInactive.Include).Length,
                blendshapeStressRace = BlendshapeStressRace,
                dynamicCharacterAvatarCount = avatars.Length,
                stressRaceAvatarCount = stressRaceAvatarCount,
                stressRaceBlendshapesEnabledCount = stressRaceBlendshapesEnabledCount,
                stressRaceAllFramesEnabledCount = stressRaceAllFramesEnabledCount,
                generatedBlendshapeCount = generatedBlendshapeCount,
                generatedBlendshapeFrameCount = generatedBlendshapeFrameCount,
                blendshapeStressFixtureValid =
                    stressRaceAvatarCount > 0 &&
                    stressRaceBlendshapesEnabledCount == stressRaceAvatarCount &&
                    generatedBlendshapeCount > 0,
                startFrame = startFrame,
                endFrame = Time.frameCount,
                sampledFrames = samples.Length,
                durationSeconds = Math.Max(0d, EditorApplication.timeSinceStartup - startTime),
                peakGeneratorQueue = peakQueue,
                meanFrameMs = CalculateMean(samples),
                medianFrameMs = Percentile(sorted, 0.50f),
                p95FrameMs = Percentile(sorted, 0.95f),
                p99FrameMs = Percentile(sorted, 0.99f),
                maximumFrameMs = sorted.Length > 0 ? sorted[sorted.Length - 1] : 0f,
                startAllocatedMemoryBytes = startAllocatedMemory,
                endAllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong(),
                peakAllocatedMemoryBytes = peakAllocatedMemory,
                peakReservedMemoryBytes = peakReservedMemory,
                peakMonoUsedMemoryBytes = peakMonoUsedMemory,
                peakGraphicsDriverMemoryBytes = peakGraphicsDriverMemory,
                startManagedMemoryBytes = startManagedMemory,
                endManagedMemoryBytes = GC.GetTotalMemory(false),
                peakManagedMemoryBytes = peakManagedMemory,
                averageTextureProcessingMs = CalculateAverageMilliseconds(
                    delta.textureProcessingTicks,
                    delta.textureChanged),
                averageMeshUpdateMs = CalculateAverageMilliseconds(
                    delta.meshUpdatesTicks,
                    delta.slotsChanged),
                averageSkeletonUpdateMs = CalculateAverageMilliseconds(
                    delta.skeletonUpdatesTicks,
                    delta.dnaChanged),
                incrementalBlendshapePreparationTicks =
                    SkinnedMeshCombinerMeshAPI
                        .Ticks_BlendShapeFramePreparation -
                    startBlendshapePreparationTicks,
                incrementalAddBlendshapeFrameTicks =
                    SkinnedMeshCombinerMeshAPI
                        .Ticks_AddBlendShapeFrame -
                    startAddBlendshapeFrameTicks,
                incrementalBlendshapeFramesPrepared =
                    SkinnedMeshCombinerMeshAPI
                        .BlendShapeFramesPrepared -
                    startBlendshapeFramesPrepared,
                incrementalBlendshapeFramesApplied =
                    SkinnedMeshCombinerMeshAPI
                        .BlendShapeFramesApplied -
                    startBlendshapeFramesApplied,
                generator = GeneratorConfiguration.Capture(generator),
                generatorCounterDelta = delta,
                frameMilliseconds = samples
            };
            return report;
        }

        private void CaptureMemoryPeaks()
        {
            peakAllocatedMemory = Math.Max(peakAllocatedMemory, Profiler.GetTotalAllocatedMemoryLong());
            peakReservedMemory = Math.Max(peakReservedMemory, Profiler.GetTotalReservedMemoryLong());
            peakMonoUsedMemory = Math.Max(peakMonoUsedMemory, Profiler.GetMonoUsedSizeLong());
            peakGraphicsDriverMemory = Math.Max(
                peakGraphicsDriverMemory,
                Profiler.GetAllocatedMemoryForGraphicsDriver());
            peakManagedMemory = Math.Max(peakManagedMemory, GC.GetTotalMemory(false));
        }

        private string SaveReportAutomatically(BaselineReport report)
        {
            string directory = Path.GetFullPath(DefaultOutputDirectory);
            Directory.CreateDirectory(directory);
            string sceneName = SceneManager.GetActiveScene().name;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                sceneName = "Untitled";
            }

            string fileName =
                $"UMA-Generation-Baseline-{SanitizeFileName(sceneName)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path;
        }

        private void SaveReportAs(BaselineReport report)
        {
            string defaultName =
                $"UMA-Generation-Baseline-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            string path = EditorUtility.SaveFilePanel(
                "Save UMA Generation Baseline",
                Path.GetFullPath(DefaultOutputDirectory),
                defaultName,
                "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            lastSavedPath = path;
        }

        private void TryFindGenerator()
        {
            if (generator != null)
            {
                return;
            }

            generator = Object.FindAnyObjectByType<UMAGeneratorBase>(
                FindObjectsInactive.Include);
        }

        private void DrawBlendshapeFixtureStatus()
        {
            DynamicCharacterAvatar[] avatars = FindDynamicCharacterAvatars();
            int stressRaceCount = 0;
            int enabledCount = 0;
            for (int i = 0; i < avatars.Length; i++)
            {
                if (!IsStressRace(avatars[i]))
                {
                    continue;
                }
                stressRaceCount++;
                if (avatars[i].loadBlendShapes)
                {
                    enabledCount++;
                }
            }

            string status = stressRaceCount == 0
                ? $"Waiting for a '{BlendshapeStressRace}' DCA"
                : $"{enabledCount}/{stressRaceCount} '{BlendshapeStressRace}' DCAs load blendshapes";
            EditorGUILayout.LabelField("Blendshape Fixture", status);
        }

        private static DynamicCharacterAvatar[] FindDynamicCharacterAvatars()
        {
            return UMAObjectUtility.FindObjectsByType<DynamicCharacterAvatar>(
                FindObjectsInactive.Include);
        }

        private static bool IsStressRace(DynamicCharacterAvatar avatar)
        {
            return avatar != null &&
                   avatar.activeRace != null &&
                   string.Equals(
                       avatar.activeRace.name,
                       BlendshapeStressRace,
                       StringComparison.Ordinal);
        }

        private static void CountGeneratedBlendshapes(
            out int blendshapeCount,
            out int blendshapeFrameCount)
        {
            blendshapeCount = 0;
            blendshapeFrameCount = 0;
            SkinnedMeshRenderer[] renderers = UMAObjectUtility.FindObjectsByType<SkinnedMeshRenderer>(
                FindObjectsInactive.Include);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Mesh mesh = renderers[rendererIndex].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                blendshapeCount += mesh.blendShapeCount;
                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    blendshapeFrameCount += mesh.GetBlendShapeFrameCount(shapeIndex);
                }
            }
        }

        private void DisposeRecorder()
        {
            if (mainThreadRecorderStarted || mainThreadRecorder.Valid)
            {
                mainThreadRecorder.Dispose();
            }
            mainThreadRecorder = default;
            mainThreadRecorderStarted = false;
        }

        private static float CalculateMean(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            double total = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                total += values[i];
            }
            return (float)(total / values.Length);
        }

        private static float Percentile(float[] sortedValues, float percentile)
        {
            if (sortedValues == null || sortedValues.Length == 0)
            {
                return 0f;
            }

            float index = Mathf.Clamp01(percentile) * (sortedValues.Length - 1);
            int lower = Mathf.FloorToInt(index);
            int upper = Mathf.CeilToInt(index);
            if (lower == upper)
            {
                return sortedValues[lower];
            }
            return Mathf.Lerp(sortedValues[lower], sortedValues[upper], index - lower);
        }

        private static float CalculateAverageMilliseconds(long ticks, long count)
        {
            if (ticks <= 0 || count <= 0)
            {
                return 0f;
            }
            return (float)(ticks * 1000d / System.Diagnostics.Stopwatch.Frequency / count);
        }

        private static string BuildSummary(BaselineReport report)
        {
            string combiner = report.generator != null
                ? report.generator.meshCombinerType
                : string.Empty;
            return
                $"[UMA] Generation baseline: {report.observedAvatarCount}/{report.expectedAvatarCount} UMAs, " +
                $"{report.sampledFrames} frames over {report.durationSeconds:F3}s, " +
                $"mean {report.meanFrameMs:F3}ms, p95 {report.p95FrameMs:F3}ms, " +
                $"p99 {report.p99FrameMs:F3}ms, max {report.maximumFrameMs:F3}ms, " +
                $"mesh update avg {report.averageMeshUpdateMs:F3}ms, combiner {combiner}.";
        }

        private static string FormatBytes(long value)
        {
            if (value < 1024)
            {
                return $"{value} B";
            }
            if (value < 1024L * 1024L)
            {
                return $"{value / 1024d:F2} KB";
            }
            return $"{value / (1024d * 1024d):F2} MB";
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '-');
            }
            return value;
        }
    }
}

#endif
