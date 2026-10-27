using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UMATestProject
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class UMATimingOverlay : MonoBehaviour
    {
        public UMAGeneratorBuiltin Generator;
        public UMARandomAvatar Crowd;
        public UMARandomAvatarV2 CrowdV2;

        public bool IsTiming { get; private set; }
        public bool IsRestarting { get; private set; }
        public bool HasCompletedRun { get; private set; }
        public string Status { get; private set; } = "Ready. Start Timing removes the existing UMAs and rebuilds the crowd.";
        public double ElapsedMilliseconds => stopwatch.Elapsed.TotalMilliseconds;
        public int CompletedCount => completed.Count;
        public int SpawnedCount => tracked.Count;

        private static readonly string[] CombinerNames =
        {
            "Jobified", "Incremental", "Default", "Default Bone Baking", "Bone Baking (Compatibility)"
        };
        private static readonly Type[] CombinerTypes =
        {
            typeof(UMAJobifiedMeshCombiner), typeof(UMAIncrementalMeshCombiner), typeof(UMADefaultMeshCombiner),
            typeof(UMADefaultBoneBakingMeshCombiner), typeof(UMABoneBakingMeshCombiner)
        };

        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly HashSet<UMAData> tracked = new HashSet<UMAData>();
        private readonly HashSet<UMAData> completed = new HashSet<UMAData>();
        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<UMAGeneratorBuiltin.MultiStepAtomicStepStatistic> steps = new List<UMAGeneratorBuiltin.MultiStepAtomicStepStatistic>();
        private readonly List<UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic> overruns = new List<UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>();
        private readonly List<Label[]> stepRows = new List<Label[]>();
        private readonly List<Label[]> overrunRows = new List<Label[]>();
        private UIDocument document;
        private Button startButton;
        private Label statusLabel, wallLabel, workLabel, builtLabel;
        private Label fpsLabel, frameTimeLabel, combinerLabel;
        private VisualElement combinerOptions;
        private int selectedCombinerIndex, fpsFrames;
        private double fpsSampleStart;
        private string runCombinerName;
        private GeneratorQualityProfile combinerProfile;
        private UMAGenerator combinerOwner;
        private VisualElement stepTable, overrunTable;
        private UMARandomAvatarEvent spawnEvent;
        private Coroutine restart;
        private UMAGeneratorBuiltin suspendedGenerator;
        private int idleFrames;
        private double nextRefresh;
        private PanelSettings ownedPanel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForTestScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                OnSceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/UMA/SRP/Samples/Scenes/U3-Generating Random Characters.unity") return;
            foreach (var overlay in UMAObjectUtility.FindObjectsByType<UMATimingOverlay>(FindObjectsInactive.Include))
                if (overlay.gameObject.scene == scene) return;
            var go = new GameObject("UMA Timing Overlay");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<UMATimingOverlay>();
        }

        private void OnEnable()
        {
            HasCompletedRun = false;
            completed.Clear();
            stopwatch.Reset();
            Status = "Ready. Start Timing removes the existing UMAs and rebuilds the crowd.";
            document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                ownedPanel = ScriptableObject.CreateInstance<PanelSettings>();
                ownedPanel.name = "UMA Timing Panel";
                ownedPanel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ownedPanel.referenceResolution = new Vector2Int(1600, 900);
                ownedPanel.match = 0.5f;
                ownedPanel.sortingOrder = 1000;
                ownedPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UMATimingTheme");
                document.panelSettings = ownedPanel;
            }
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UMATiming");
            document.sortingOrder = 1000;
            var root = document.rootVisualElement;
            root.Clear();
            if (document.visualTreeAsset != null) document.visualTreeAsset.CloneTree(root);
            root.pickingMode = PickingMode.Ignore;
            startButton = root.Q<Button>("startTiming");
            if (startButton == null)
            {
                Debug.LogError("UMA Timing UI could not load Resources/UMATiming.uxml.", this);
                enabled = false;
                return;
            }
            statusLabel = root.Q<Label>("status");
            wallLabel = root.Q<Label>("wallTime");
            workLabel = root.Q<Label>("workTime");
            builtLabel = root.Q<Label>("builtCount");
            fpsLabel = root.Q<Label>("fps");
            frameTimeLabel = root.Q<Label>("frameTime");
            combinerLabel = root.Q<Label>("combinerStatus");
            combinerOptions = root.Q("combinerOptions");
            fpsFrames = 0;
            fpsSampleStart = Time.realtimeSinceStartupAsDouble;
            runCombinerName = null;
            stepTable = root.Q("atomicSteps");
            overrunTable = root.Q("overrunSteps");
            startButton.clicked += StartTiming;
            ResolveSources();
            BuildCombinerOptions();
            BuildMetricRows(root);
            RefreshDisplay();
        }

        private void OnDisable()
        {
            if (startButton != null) startButton.clicked -= StartTiming;
            if (restart != null) StopCoroutine(restart);
            RestoreGenerator();
            UnbindAvatars();
            stopwatch.Stop();
            restart = null;
            IsRestarting = IsTiming = false;
            GeneratorQualityRuntime.Remove(combinerOwner, this);
            combinerOwner = null;
            if (combinerProfile != null) Destroy(combinerProfile);
            if (ownedPanel != null)
            {
                document.panelSettings = null;
                Destroy(ownedPanel);
            }
        }

        private bool ResolveSources()
        {
            if (Generator == null) Generator = UMAAssetIndexer.bareInstance?.bareGenerator;
            if (Generator == null)
                foreach (var candidate in UMAObjectUtility.FindObjectsByType<UMAGeneratorBuiltin>(FindObjectsInactive.Exclude))
                    if (candidate.gameObject.scene == gameObject.scene) { Generator = candidate; break; }
            if (CrowdV2 != null && CrowdV2.UnifiedController != null) Crowd = CrowdV2.UnifiedController;
            if (Crowd == null)
                foreach (var candidate in UMAObjectUtility.FindObjectsByType<UMARandomAvatar>(FindObjectsInactive.Exclude))
                    if (candidate.gameObject.scene == gameObject.scene && candidate.isActiveAndEnabled && candidate.mode == UMARandomAvatar.Mode.Generate)
                    { Crowd = candidate; break; }
            if (Crowd == null && CrowdV2 == null)
                foreach (var candidate in UMAObjectUtility.FindObjectsByType<UMARandomAvatarV2>(FindObjectsInactive.Exclude))
                    if (candidate.gameObject.scene == gameObject.scene && candidate.isActiveAndEnabled && candidate.mode == UMARandomAvatarV2.Mode.Generate)
                    { CrowdV2 = candidate; Crowd = candidate.UnifiedController; break; }
            return Generator != null && Generator.isActiveAndEnabled &&
                (Crowd != null && Crowd.isActiveAndEnabled && Crowd.mode == UMARandomAvatar.Mode.Generate && Crowd.prefab != null ||
                 Crowd == null && CrowdV2 != null && CrowdV2.isActiveAndEnabled && CrowdV2.mode == UMARandomAvatarV2.Mode.Generate && CrowdV2.Generation.Prefab != null);
        }

        public void StartTiming()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsRestarting) return;
            if (!ResolveSources())
            {
                Status = "An enabled UMA generator and a Generate-mode crowd with a character prefab are required.";
                RefreshDisplay();
                return;
            }
            IsRestarting = true;
            IsTiming = false;
            HasCompletedRun = false;
            restart = StartCoroutine(RestartRoutine());
        }

        private void BuildCombinerOptions()
        {
            selectedCombinerIndex = Generator != null && Generator.meshCombiner != null
                ? Array.IndexOf(CombinerTypes, Generator.meshCombiner.GetType()) : 0;
            if (selectedCombinerIndex < 0) selectedCombinerIndex = 0;
            combinerOptions.Clear();
            for (int i = 0; i < CombinerNames.Length; i++)
            {
                int index = i;
                var button = new Button(() =>
                {
                    selectedCombinerIndex = index;
                    RefreshCombinerDisplay();
                }) { name = "combiner" + i, text = CombinerNames[i], tooltip = CombinerTypes[i].Name };
                button.AddToClassList("combiner-option");
                combinerOptions.Add(button);
            }
            RefreshCombinerDisplay();
        }

        private void RefreshCombinerDisplay()
        {
            for (int i = 0; i < combinerOptions.childCount; i++)
                combinerOptions[i].EnableInClassList("combiner-option--selected", i == selectedCombinerIndex);
            combinerOptions.SetEnabled(!IsRestarting);
            string current = runCombinerName;
            if (current == null && Generator != null && Generator.meshCombiner != null)
            {
                int index = Array.IndexOf(CombinerTypes, Generator.meshCombiner.GetType());
                current = index >= 0 ? CombinerNames[index] : Generator.meshCombiner.GetType().Name;
            }
            combinerLabel.text = (runCombinerName == null ? "Current: " : "Measured run: ") + (current ?? "None") +
                ". Start Timing applies " + CombinerNames[selectedCombinerIndex] + ".";
        }

        // Called only after outstanding work and old characters have been cleaned up.
        private void ApplySelectedCombiner()
        {
            UMAMeshCombiner selected;
            switch (selectedCombinerIndex)
            {
                case 1: selected = GetOrCreateCombiner<UMAIncrementalMeshCombiner>(); break;
                case 2: selected = GetOrCreateCombiner<UMADefaultMeshCombiner>(); break;
                case 3: selected = GetOrCreateCombiner<UMADefaultBoneBakingMeshCombiner>(); break;
                case 4: selected = GetOrCreateCombiner<UMABoneBakingMeshCombiner>(); break;
                default: selected = GetOrCreateCombiner<UMAJobifiedMeshCombiner>(); break;
            }
            var owner = Generator as UMAGenerator;
            if (combinerOwner != owner) GeneratorQualityRuntime.Remove(combinerOwner, this);
            combinerOwner = owner;
            if (owner != null)
            {
                // A transient layer keeps scene quality overrides from resetting the choice.
                // Every other generator setting continues to inherit from the scene.
                if (combinerProfile == null)
                {
                    combinerProfile = ScriptableObject.CreateInstance<GeneratorQualityProfile>();
                    combinerProfile.hideFlags = HideFlags.HideAndDontSave;
                }
                combinerProfile.meshCombiner = new GeneratorQualityValue<UMAMeshCombiner>(selected) { overrideValue = true };
                GeneratorQualityRuntime.SetProfile(owner, this, combinerProfile, int.MaxValue);
            }
            else Generator.meshCombiner = selected;
            runCombinerName = CombinerNames[selectedCombinerIndex];
        }

        private T GetOrCreateCombiner<T>() where T : UMAMeshCombiner
        {
            if (Generator.meshCombiner != null && Generator.meshCombiner.GetType() == typeof(T))
                return (T)Generator.meshCombiner;
            foreach (var candidate in UMAObjectUtility.FindObjectsByType<T>(FindObjectsInactive.Exclude))
                if (candidate.GetType() == typeof(T) &&
                    (candidate.gameObject.scene == gameObject.scene || candidate.transform.IsChildOf(Generator.transform)))
                    return candidate;
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(Generator.transform, false);
            return go.AddComponent<T>();
        }

        private IEnumerator RestartRoutine()
        {
            UnbindAvatars();
            completed.Clear();
            stopwatch.Reset();
            Status = "Removing existing UMAs and finishing cleanup...";
            RefreshDisplay();
            suspendedGenerator = Generator;
            Generator.enabled = false;
            try
            {
                var objects = new HashSet<GameObject>();
                foreach (var data in UMAObjectUtility.FindObjectsByType<UMAData>(FindObjectsInactive.Include))
                {
                    if (data.gameObject.scene != gameObject.scene) continue;
                    var avatar = data.GetComponentInParent<DynamicCharacterAvatar>(true);
                    objects.Add(avatar != null ? avatar.gameObject : data.gameObject);
                }
                foreach (var avatar in UMAObjectUtility.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Include))
                    if (avatar.gameObject.scene == gameObject.scene) objects.Add(avatar.gameObject);
                if (Crowd != null) Crowd.DestroyGeneratedCharacters();
                else CrowdV2.DestroyGeneratedCharacters();
                foreach (var go in objects) if (go != null) Destroy(go);
                yield return null;
                yield return null;
                RenderTexToCPU.CleanupPendingCopies();
                RestoreGenerator();
                ApplySelectedCombiner();
                Generator.umaDatasGenerated.RemoveAll(data => data == null);
                Generator.ResetStatistics();
                ResetTextureCounters();
                foreach (var monitor in UMAObjectUtility.FindObjectsByType<UMAResourceUsageMonitor>(FindObjectsInactive.Exclude))
                    if (monitor.isActiveAndEnabled && monitor.gameObject.scene == gameObject.scene) monitor.ResetCapture();
                spawnEvent = Crowd != null
                    ? Crowd.RandomAvatarGenerated ??= new UMARandomAvatarEvent()
                    : CrowdV2.Generation.RandomAvatarGenerated ??= new UMARandomAvatarEvent();
                spawnEvent.AddListener(OnSpawned);
                idleFrames = 0;
                IsTiming = true;
                stopwatch.Restart();
                Status = "Building the fresh crowd...";
                try
                {
                    if (Crowd != null) Crowd.GenerateCharacters(true);
                    else CrowdV2.GenerateCharacters(true);
                }
                catch (Exception exception)
                {
                    IsTiming = false;
                    stopwatch.Stop();
                    Status = "Crowd build failed: " + exception.Message;
                    Debug.LogException(exception, this);
                }
            }
            finally
            {
                RestoreGenerator();
                IsRestarting = false;
                restart = null;
                RefreshDisplay();
            }
        }

        private void RestoreGenerator()
        {
            if (suspendedGenerator != null) suspendedGenerator.enabled = true;
            suspendedGenerator = null;
        }

        private void OnSpawned(GameObject source, GameObject character)
        {
            var data = character != null ? character.GetComponent<UMAData>() : null;
            if (data != null && tracked.Add(data)) data.OnCharacterUpdated += OnBuilt;
        }

        private void OnBuilt(UMAData data) { if (data != null && tracked.Contains(data)) completed.Add(data); }

        private void UnbindAvatars()
        {
            spawnEvent?.RemoveListener(OnSpawned);
            spawnEvent = null;
            foreach (var data in tracked) if (data != null) data.OnCharacterUpdated -= OnBuilt;
            tracked.Clear();
        }

        private void Update()
        {
            UpdateFrameRate();
            if (IsTiming && !IsRestarting)
            {
                bool busy = Crowd != null && Crowd.IsGenerating || Generator != null && !Generator.IsIdle() ||
                    RenderTexToCPU.PendingCopies() > 0 || RenderTexToCPU.renderTexturesToCPU.Count > 0;
                idleFrames = busy ? 0 : idleFrames + 1;
                if (idleFrames >= 3 && (completed.Count == tracked.Count || Generator == null || Generator.multiStepFailureCount > 0))
                {
                    stopwatch.Stop();
                    IsTiming = false;
                    HasCompletedRun = true;
                    Status = tracked.Count == 0 ? "No avatars were generated. Check the crowd configuration." :
                        completed.Count < tracked.Count || Generator == null || Generator.multiStepFailureCount > 0
                        ? $"Finished with incomplete builds: {completed.Count}/{tracked.Count}. Check the Console."
                        : $"Complete. {completed.Count} avatars built. Start Timing runs the test again.";
                    RefreshDisplay();
                }
                else if (idleFrames >= 3)
                    Status = $"Waiting for avatar builds: {completed.Count}/{tracked.Count}. Start Timing can restart a stalled run.";
            }
            if (!HasCompletedRun && Time.unscaledTimeAsDouble >= nextRefresh)
            {
                nextRefresh = Time.unscaledTimeAsDouble + 0.2;
                RefreshDisplay();
            }
        }

        private void UpdateFrameRate()
        {
            fpsFrames++;
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - fpsSampleStart;
            if (elapsed < 0.5) return;
            fpsLabel.text = (fpsFrames / elapsed).ToString("F1", CultureInfo.InvariantCulture) + " FPS";
            frameTimeLabel.text = (elapsed * 1000 / fpsFrames).ToString("F2", CultureInfo.InvariantCulture) + " ms / frame";
            fpsFrames = 0;
            fpsSampleStart = now;
        }

        private static string Ms(double value) => value.ToString("F3", CultureInfo.InvariantCulture) + " ms";
        private static string Ticks(long value) => Ms(UMATime.StopwatchTicksToMilliseconds(value));
        private static string Atomic(string name, float value) => string.IsNullOrEmpty(name) ? "None" : name + " · " + Ms(value);

        private void BuildMetricRows(VisualElement root)
        {
            refreshers.Clear();
            stepRows.Clear();
            overrunRows.Clear();
            void Row(string section, string caption, Func<string> value)
            {
                var row = new VisualElement(); row.AddToClassList("metric-row");
                var label = new Label(caption); label.AddToClassList("metric-name"); row.Add(label);
                var output = new Label("—"); output.AddToClassList("metric-value"); row.Add(output);
                root.Q(section).Add(row);
                refreshers.Add(() => output.text = Generator != null ? value() : "—");
            }
            Row("phaseMetrics", "Generator Work Time", () => Ticks(Generator.ElapsedTicks));
            Row("phaseMetrics", "Validation Time", () => Ticks(Generator.validationTicks));
            Row("phaseMetrics", "Mesh Processing Time", () => Ticks(Generator.meshpreprocessTicks));
            Row("phaseMetrics", "Begun Events Time", () => Ticks(Generator.BegunEventsTicks));
            Row("phaseMetrics", "Pre Apply Time", () => Ticks(Generator.preapplyTicks));
            Row("phaseMetrics", "Texture Processing Time", () => Ticks(Generator.textureprocessingTicks));
            Row("phaseMetrics", "Successful Mesh Work Time", () => Ticks(Generator.meshUpdatesTicks));
            Row("phaseMetrics", "Skeleton Updates Time", () => Ticks(Generator.skeletonUpdatesTicks));
            Row("phaseMetrics", "Race Blendshapes Time", () => Ticks(Generator.raceblendshapesTicks));
            Row("phaseMetrics", "End Events Time", () => Ticks(Generator.endEventsTicks));
            Row("phaseMetrics", "Atlas Preparation Time", () => Ticks(Generator.atlasPreparationTicks));
            Row("phaseMetrics", "Atlas Lookup Time", () => Ticks(Generator.atlasLookupTicks));
            Row("phaseMetrics", "Atlas Generation Time", () => Ticks(Generator.atlasGenerationTicks));
            Row("averageMetrics", "Average Mesh Time", () => Ms(Generator.averageMeshUpdatesTime));
            Row("averageMetrics", "Average Texture Time", () => Ms(Generator.averageTextureProcessingTime));
            Row("averageMetrics", "Average Skeleton Time", () => Ms(Generator.averageSkeletonUpdatesTime));
            Row("compilerMetrics", "Active Stage", () => string.IsNullOrEmpty(Generator.ActiveMultiStepStage) ? "Idle" : Generator.ActiveMultiStepStage);
            Row("compilerMetrics", "Active Progress", () => Generator.ActiveMultiStepProgress.ToString("P1"));
            Row("compilerMetrics", "Last Atomic Step", () => Atomic(Generator.lastMultiStepAtomicStepName, Generator.lastMultiStepAtomicStepMilliseconds));
            Row("compilerMetrics", "Longest Atomic Step", () => Atomic(Generator.maximumMultiStepAtomicStepName, Generator.maximumMultiStepAtomicStepMilliseconds));
            Row("compilerMetrics", "Last Generation Latency", () => Ticks(Generator.lastMultiStepGenerationLatencyTicks));
            Row("compilerMetrics", "Maximum Generation Latency", () => Ticks(Generator.maximumMultiStepGenerationLatencyTicks));
            Row("compilerMetrics", "Discarded Mesh Work", () => Ticks(Generator.multiStepDiscardedMeshTicks));
            Row("compilerMetrics", "Budget Overruns", () => Generator.multiStepBudgetOverrunCount.ToString());
            Row("compilerMetrics", "Last Budget Overrun", () => Generator.multiStepBudgetOverrunCount == 0 ? "None" : Atomic(Generator.lastMultiStepBudgetOverrunStepName, Generator.lastMultiStepBudgetOverrunStepMilliseconds) + " (+" + Ms(Generator.lastMultiStepBudgetOverrunAmountMilliseconds) + ")");
            Row("compilerMetrics", "Async Waits", () => Generator.multiStepWaitingForAsyncCount.ToString());
            Row("compilerMetrics", "Restarts", () => Generator.multiStepRestartCount.ToString());
            Row("compilerMetrics", "Cancellations", () => Generator.multiStepCancellationCount.ToString());
            Row("compilerMetrics", "Failures", () => Generator.multiStepFailureCount.ToString());
            Row("compilerMetrics", "Source Validation Cache Hits", () => SkinnedMeshCombinerMeshAPI.SourceValidationCacheHits.ToString());
            Row("compilerMetrics", "Source Validation Cache Misses", () => SkinnedMeshCombinerMeshAPI.SourceValidationCacheMisses.ToString());
            Row("compilerMetrics", "Source Validation Cache Bypasses", () => SkinnedMeshCombinerMeshAPI.SourceValidationCacheBypasses.ToString());
            Row("queueMetrics", "Pending UMAs", () => Generator.pendingUmas.ToString());
            Row("queueMetrics", "Current Queue Size", () => Generator.QueueSize().ToString());
            Row("queueMetrics", "Shape Dirty", () => Generator.DnaChanged.ToString());
            Row("queueMetrics", "Texture Dirty", () => Generator.TextureChanged.ToString());
            Row("queueMetrics", "Mesh Dirty", () => Generator.SlotsChanged.ToString());
            Row("queueMetrics", "Atlas Early Hits", () => Generator.atlasEarlyHits.ToString());
            Row("textureMetrics", "Textures Processed", () => Generator.TexturesProcessed.ToString());
            Row("textureMetrics", "Copies Enqueued", () => RenderTexToCPU.copiesEnqueued.ToString());
            Row("textureMetrics", "Copies Dequeued", () => RenderTexToCPU.copiesDequeued.ToString());
            Row("textureMetrics", "Unable to Queue", () => RenderTexToCPU.unableToQueue.ToString());
            Row("textureMetrics", "Missed Uploads", () => RenderTexToCPU.misseduploads.ToString());
            Row("textureMetrics", "Error Uploads", () => RenderTexToCPU.errorUploads.ToString());
            Row("textureMetrics", "Textures Uploaded", () => RenderTexToCPU.texturesUploaded.ToString());
            Row("cleanupMetrics", "UMAData Cleanup", () => RenderTexToCPU.renderTexturesCleanedUMAData.ToString());
            Row("cleanupMetrics", "Applied Cleanup", () => RenderTexToCPU.renderTexturesCleanedApplied.ToString());
            Row("cleanupMetrics", "Not Applied Cleanup", () => RenderTexToCPU.renderTexturesCleanedMissed.ToString());
            Row("cleanupMetrics", "Total Cleanup", () => (RenderTexToCPU.renderTexturesCleanedUMAData + RenderTexToCPU.renderTexturesCleanedApplied + RenderTexToCPU.renderTexturesCleanedMissed).ToString());
            Row("boneMetrics", "Cleanup Runs", () => Generator.BoneCleanupRuns.ToString());
            Row("boneMetrics", "Bones Removed", () => Generator.BoneCleanupRemoved.ToString());
            Row("boneMetrics", "Last Cleanup Time", () => Ms(Generator.BoneCleanupLastMilliseconds));
            Row("boneMetrics", "Total Cleanup Time", () => Ms(Generator.BoneCleanupTotalMilliseconds));
        }

        private void RefreshDisplay()
        {
            if (startButton == null) return;
            statusLabel.text = Status;
            startButton.SetEnabled(!IsRestarting);
            RefreshCombinerDisplay();
            wallLabel.text = stopwatch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + " s";
            workLabel.text = Generator != null ? Ticks(Generator.ElapsedTicks) : "—";
            builtLabel.text = $"{completed.Count} / {tracked.Count}";
            foreach (var refresh in refreshers) refresh();
            if (Generator == null) return;
            Generator.GetMultiStepAtomicStepStatistics(steps);
            EnsureTable(stepTable, stepRows, steps.Count, new[] { "Step", "Count", "Avg", "Max", "Total" });
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i]; var row = stepRows[i];
                row[0].text = step.StepName; row[1].text = step.Count.ToString();
                row[2].text = step.AverageMilliseconds.ToString("F3"); row[3].text = step.MaximumMilliseconds.ToString("F3"); row[4].text = step.TotalMilliseconds.ToString("F3");
            }
            Generator.GetMultiStepBudgetOverrunStatistics(overruns);
            EnsureTable(overrunTable, overrunRows, overruns.Count, new[] { "Step", "Count", "Max", "Max over" });
            for (int i = 0; i < overruns.Count; i++)
            {
                var step = overruns[i]; var row = overrunRows[i];
                row[0].text = step.StepName; row[1].text = step.Count.ToString();
                row[2].text = step.MaximumStepMilliseconds.ToString("F3"); row[3].text = step.MaximumOverrunMilliseconds.ToString("F3");
            }
        }

        private static void EnsureTable(VisualElement table, List<Label[]> rows, int count, string[] headings)
        {
            if (rows.Count == count && table.childCount > 0) return;
            table.Clear(); rows.Clear();
            if (count == 0) { var empty = new Label("No samples recorded."); empty.AddToClassList("empty"); table.Add(empty); return; }
            for (int i = -1; i < count; i++)
            {
                var row = new VisualElement(); row.AddToClassList("metric-row");
                if (i == -1) row.AddToClassList("table-header");
                var cells = new Label[headings.Length];
                for (int j = 0; j < cells.Length; j++)
                {
                    cells[j] = new Label(i == -1 ? headings[j] : "");
                    cells[j].AddToClassList(j == 0 ? "step-name" : j == 1 ? "step-count" : "step-number");
                    row.Add(cells[j]);
                }
                table.Add(row); if (i >= 0) rows.Add(cells);
            }
        }

        private static void ResetTextureCounters()
        {
            RenderTexToCPU.copiesEnqueued = RenderTexToCPU.copiesDequeued = RenderTexToCPU.unableToQueue = 0;
            RenderTexToCPU.misseduploads = RenderTexToCPU.errorUploads = RenderTexToCPU.texturesUploaded = 0;
            RenderTexToCPU.renderTexturesCleanedUMAData = RenderTexToCPU.renderTexturesCleanedApplied = RenderTexToCPU.renderTexturesCleanedMissed = 0;
        }
    }
}
