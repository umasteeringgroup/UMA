using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UMA
{
    [AddComponentMenu("UMA/Runtime/UMA Resource Usage Monitor")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class UMAResourceUsageMonitor : MonoBehaviour
    {
        public enum ReusePolicy { AvatarSettings, Off, On }

        [Tooltip("Generator whose cumulative main-thread timings are measured. Auto-detected if empty.")]
        public UMAGeneratorBuiltin Generator;
        public UMARandomAvatar LegacyCrowdGenerator;
        public UMARandomAvatarV2 CrowdGenerator;
        [Tooltip("Optional root for the memory census. Empty measures registered crowd avatars, or scene UMAData when no crowd controller is assigned.")]
        public Transform AvatarRoot;
        [Tooltip("Applied to newly spawned crowd avatars BEFORE their first build. Existing avatars are unchanged until restarted.")]
        public ReusePolicy InitialReuse = ReusePolicy.AvatarSettings;
        [Min(0.25f)] public float MemorySampleInterval = 1f;
        [Tooltip("Memory profiling allocates and takes time. Leave off for timing comparisons; memory is sampled when generation finishes.")]
        public bool SampleMemoryDuringGeneration;
        public bool LogCompletedRuns = true;

        [Serializable]
        public sealed class RunSnapshot
        {
            public string Mode;
            public string CapturedUtc;
            public bool Completed;
            public bool CountersReset;
            public int SpawnedAvatars;
            public int CompletedAvatars;
            public int CacheEntriesAtStart;
            public int QueueSize;
            public int PendingTextureWork;
            public double WallMilliseconds;
            public double GeneratorMilliseconds;
            public double MeshMilliseconds;
            public double TextureMilliseconds;
            public double AtlasPreparationMilliseconds;
            public double AtlasLookupMilliseconds;
            public double AtlasGenerationMilliseconds;
            public long AtlasEarlyHits;
            public long MeshUpdates;
            public long TextureUpdates;
            public UMAGeneratedResourceCache.ResourceStatistics MeshCache;
            public UMAGeneratedResourceCache.ResourceStatistics TextureCache;
            public UMAGeneratedResourceMemory.Snapshot Memory;
            public double MemorySampleMilliseconds;
            public string[] ReuseNotes;
            public Configuration ConfigurationAtStart, ConfigurationAtEnd;
            public double ValidationMilliseconds, MeshPreprocessMilliseconds, BegunEventsMilliseconds;
            public double DnaPreApplyMilliseconds, SkeletonMilliseconds, RaceBlendshapesMilliseconds, EndEventsMilliseconds;
            public double OutsideGeneratorMilliseconds;
            public UMAGenerationDiagnostics.Measurement[] SpawnTimings, GeneratorTimings, MeshLookupTimings;
            public long AsyncYieldCount, BudgetOverrunCount, RestartCount, CancellationCount, FailureCount;
            public int ObservedFrameIntervals, FramesOver33Milliseconds, FramesOver100Milliseconds, PeakQueue;
            public double AverageFrameMilliseconds, MaximumFrameMilliseconds;
            public int[] GarbageCollections;
            public double FirstAvatarReadyMilliseconds, LastAvatarReadyMilliseconds;
            public double AverageSpawnToReadyMilliseconds, MaximumSpawnToReadyMilliseconds;
            public int NPCShortcutInstances;
            public double NPCShortcutMilliseconds;
        }

        [Serializable]
        public sealed class Configuration
        {
            public string Scene, CrowdController, CharacterPrefab, GeneratorType, MeshCombiner;
            public int IterationCount, InterFrameDelay, AtlasResolution, MaximumUniqueCharacters, GridX, GridZ;
            public float MultiStepBudgetMilliseconds;
            public float SpawnBudgetMilliseconds;
            public bool UseNPCBuilds;
            public bool ProcessAllPending, ConvertRenderTextures, AsyncConversion, SampleMemoryDuringGeneration;
            public int QualityLevel, VSyncCount, TargetFrameRate;
        }

        [Serializable]
        public sealed class Report
        {
            public int SchemaVersion = 2;
            public string ExportedUtc, UnityVersion, UmaVersion, Platform, OperatingSystem, Processor, GraphicsDevice;
            public bool IsEditor, DevelopmentBuild;
            public int ProcessorCount, SystemMemoryMB, GraphicsMemoryMB;
            public string MeasurementNotes;
            public RunSnapshot Current, LastOff, LastOn;
        }

        public RunSnapshot Current { get; private set; } = new RunSnapshot();
        public RunSnapshot LastOff { get; private set; }
        public RunSnapshot LastOn { get; private set; }
        public string Status { get; private set; } = "Enter Play mode to measure a crowd.";
        public bool IsRestarting => restart != null;
        public bool HasCrowd => LegacyCrowdGenerator != null && LegacyCrowdGenerator.mode == UMARandomAvatar.Mode.Generate ||
            CrowdGenerator != null && CrowdGenerator.mode == UMARandomAvatarV2.Mode.Generate;

        private readonly HashSet<UMAData> avatars = new HashSet<UMAData>();
        private readonly HashSet<UMAData> completedAvatars = new HashSet<UMAData>();
        private readonly Dictionary<UMAData, (int count, double milliseconds)> npcBaselines = new Dictionary<UMAData, (int, double)>();
        private readonly List<UMAData> memoryAvatars = new List<UMAData>();
        private UMAGeneratedResourceCache cache;
        private UMAGeneratedResourceCache.ResourceStatistics meshBaseline, textureBaseline;
        private long workBaseline, meshTimeBaseline, textureTimeBaseline, meshCountBaseline, textureCountBaseline;
        private long atlasPreparationBaseline, atlasLookupBaseline, atlasGenerationBaseline, atlasEarlyHitsBaseline;
        private long startTimestamp;
        private double nextSample;
        private int idleFrames;
        private bool started;
        private ReusePolicy activePolicy;
        private UMARandomAvatarEvent legacyEvent, crowdEvent;
        private Coroutine restart;
        private UMAGeneratorBuiltin suspendedGenerator;
        private bool restoreGeneratorEnabled;
        private UMAGenerationDiagnostics.Counter[] spawnTimingBaseline, generatorTimingBaseline, lookupTimingBaseline;
        private long validationBaseline, preprocessBaseline, begunBaseline, preapplyBaseline, skeletonBaseline, raceBaseline, endEventsBaseline;
        private long asyncYieldBaseline, overrunBaseline, restartBaseline, cancellationBaseline, failureBaseline;
        private long captureResetTimestamp, previousFrameTimestamp, frameTicks;
        private int previousFrame;
        private int[] gcBaseline;
        private readonly Dictionary<UMAData, long> spawnTimestamps = new Dictionary<UMAData, long>();
        private long firstReadyTimestamp, lastReadyTimestamp, spawnToReadyTicks, maximumSpawnToReadyTicks;
        public string LastSavedReportPath { get; private set; }

        public const string TimingNotes =
            "All times are milliseconds. Stage CPU timers measure elapsed main-thread calls, not GPU time or total worker CPU. " +
            "NPCShortcutMilliseconds measures completed-template instantiation and callbacks outside the generator timer; compare generator plus NPC work. " +
            "SpawnBatch includes Instantiate, callbacks/setup, randomization, animator setup and recipe/enqueue (do not add parent and children). " +
            "Build outcome buckets measure whole build-attempt main-thread work, including textures, rig and callbacks, grouped by observed mesh-cache requests; " +
            "miss/mixed takes precedence, then pending joins, then hits. No requests includes reuse OFF, bypasses and non-mesh builds. " +
            "Texture buckets similarly group complete ProcessTexture calls. Mesh lookup timings cover signatures and cache admission; binding is separate. " +
            "BetweenUpdates buckets measure elapsed gaps while work remains, labelled by the previous yield reason; these gaps include other scene work, rendering, Editor and async execution, NOT idle CPU or job execution duration. " +
            "GeneratorWorkCall encloses the existing GeneratorMilliseconds timer and also includes scheduler/GC/bookkeeping. BuildElapsed overlaps both CPU and between-update gaps. " +
            "OutsideGeneratorMilliseconds is wall minus GeneratorMilliseconds, NOT a diagnosed wait. Frame intervals include setup and all scene work. " +
            "Spawn-to-ready starts at the spawn event (after Instantiate) and includes queue residence. Queue peak is sampled. " +
            "Generator timings cover the assigned generator; mesh lookup/cache counters are process-wide; spawn/memory are crowd-scoped. " +
            "Counters are capture deltas. Memory is generated mesh/atlas object memory, not total application memory. Completed timing snapshots are frozen; live memory can be resampled.";

        private UMAGenerationDiagnostics SpawnDiagnostics => LegacyCrowdGenerator != null ? LegacyCrowdGenerator.GenerationTimings : CrowdGenerator?.Generation.Timings;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ResolveSources();
            activePolicy = InitialReuse;
            BindSpawnEvents();
            ResetCapture();
        }

        private void BindSpawnEvents()
        {
            UMARandomAvatarEvent nextLegacy = null;
            if (LegacyCrowdGenerator != null)
                nextLegacy = LegacyCrowdGenerator.RandomAvatarGenerated ??= new UMARandomAvatarEvent();
            UMARandomAvatarEvent nextCrowd = null;
            if (CrowdGenerator != null && CrowdGenerator.mode == UMARandomAvatarV2.Mode.Generate)
                nextCrowd = CrowdGenerator.Generation.RandomAvatarGenerated ??= new UMARandomAvatarEvent();
            if (!ReferenceEquals(legacyEvent, nextLegacy))
            {
                legacyEvent?.RemoveListener(RegisterSpawnedAvatar);
                legacyEvent = nextLegacy;
                legacyEvent?.AddListener(RegisterSpawnedAvatar);
            }
            if (!ReferenceEquals(crowdEvent, nextCrowd))
            {
                crowdEvent?.RemoveListener(RegisterSpawnedAvatar);
                crowdEvent = nextCrowd;
                crowdEvent?.AddListener(RegisterSpawnedAvatar);
            }
        }

        private void OnDisable()
        {
            if (restart != null) StopCoroutine(restart);
            RestoreGenerator();
            restart = null;
            legacyEvent?.RemoveListener(RegisterSpawnedAvatar);
            crowdEvent?.RemoveListener(RegisterSpawnedAvatar);
            legacyEvent = crowdEvent = null;
            UnregisterAvatars();
        }

        private void ResolveSources()
        {
            if (Generator == null)
            {
                var indexer = UMAAssetIndexer.bareInstance;
                if (indexer != null) Generator = indexer.bareGenerator;
                if (Generator == null)
                    foreach (var candidate in FindObjectsByType<UMAGeneratorBuiltin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (candidate.gameObject.scene == gameObject.scene) { Generator = candidate; break; }
            }
            if (LegacyCrowdGenerator != null && LegacyCrowdGenerator.mode != UMARandomAvatar.Mode.Generate) LegacyCrowdGenerator = null;
            if (CrowdGenerator != null && CrowdGenerator.UnifiedController != null)
            {
                LegacyCrowdGenerator = CrowdGenerator.UnifiedController.mode == UMARandomAvatar.Mode.Generate ? CrowdGenerator.UnifiedController : null;
                CrowdGenerator = null;
            }
            if (HasCrowd) return;
            foreach (var candidate in FindObjectsByType<UMARandomAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene && candidate.isActiveAndEnabled && candidate.mode == UMARandomAvatar.Mode.Generate) { LegacyCrowdGenerator = candidate; return; }
            foreach (var candidate in FindObjectsByType<UMARandomAvatarV2>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene && candidate.isActiveAndEnabled && candidate.mode == UMARandomAvatarV2.Mode.Generate)
                { CrowdGenerator = candidate; return; }
        }

        private void RegisterSpawnedAvatar(GameObject source, GameObject character)
        {
            if (character == null || !character.TryGetComponent<UMAData>(out var data)) return;
            if (activePolicy != ReusePolicy.AvatarSettings)
            {
                data.reuseGeneratedMeshes = activePolicy == ReusePolicy.On;
                data.reuseGeneratedTextures = activePolicy == ReusePolicy.On;
            }
            if (avatars.Add(data))
            {
                data.OnCharacterUpdated += AvatarCompleted;
                spawnTimestamps[data] = Stopwatch.GetTimestamp();
            }
            Current.SpawnedAvatars = avatars.Count;
            BeginTiming();
        }

        private void AvatarCompleted(UMAData data)
        {
            if (!completedAvatars.Add(data)) return;
            lastReadyTimestamp = Stopwatch.GetTimestamp();
            if (firstReadyTimestamp == 0) firstReadyTimestamp = lastReadyTimestamp;
            if (spawnTimestamps.TryGetValue(data, out long spawned))
            {
                long ticks = Math.Max(0, lastReadyTimestamp - spawned);
                spawnToReadyTicks += ticks;
                maximumSpawnToReadyTicks = Math.Max(maximumSpawnToReadyTicks, ticks);
            }
        }

        private void BeginTiming()
        {
            if (started) return;
            startTimestamp = Stopwatch.GetTimestamp();
            IncludeBatchStart();
            previousFrameTimestamp = startTimestamp;
            previousFrame = Time.frameCount;
            started = true;
            Status = "Generating...";
        }

        public void ResetCapture()
        {
            ResolveSources();
            BindSpawnEvents();
            cache = UMAGeneratedResourceCache.Shared;
            meshBaseline = cache.GetStatistics<Mesh>();
            textureBaseline = cache.GetStatistics<UMACachedAtlas>();
            workBaseline = Generator != null ? Generator.ElapsedTicks : 0;
            meshTimeBaseline = MeshTicks;
            textureTimeBaseline = Generator != null ? Generator.textureprocessingTicks : 0;
            atlasPreparationBaseline = Generator != null ? Generator.atlasPreparationTicks : 0;
            atlasLookupBaseline = Generator != null ? Generator.atlasLookupTicks : 0;
            atlasGenerationBaseline = Generator != null ? Generator.atlasGenerationTicks : 0;
            atlasEarlyHitsBaseline = Generator != null ? Generator.atlasEarlyHits : 0;
            meshCountBaseline = Generator != null ? Generator.SlotsChanged : 0;
            textureCountBaseline = Generator != null ? Generator.TextureChanged : 0;
            captureResetTimestamp = Stopwatch.GetTimestamp();
            npcBaselines.Clear();
            foreach (var data in avatars)
                if (data is UMA.CharacterSystem.DynamicCharacterAvatar npc && npc != null)
                    npcBaselines[data] = (npc.NPCShortcutHits, npc.NPCShortcutMilliseconds);
            spawnTimingBaseline = SpawnDiagnostics?.Baseline();
            generatorTimingBaseline = Generator?.GenerationTimings.Baseline();
            lookupTimingBaseline = UMAResourceReuse.MeshTimings.Baseline();
            validationBaseline = Generator != null ? Generator.validationTicks : 0;
            preprocessBaseline = Generator != null ? Generator.meshpreprocessTicks : 0;
            begunBaseline = Generator != null ? Generator.BegunEventsTicks : 0;
            preapplyBaseline = Generator != null ? Generator.preapplyTicks : 0;
            skeletonBaseline = Generator != null ? Generator.skeletonUpdatesTicks : 0;
            raceBaseline = Generator != null ? Generator.raceblendshapesTicks : 0;
            endEventsBaseline = Generator != null ? Generator.endEventsTicks : 0;
            asyncYieldBaseline = Generator != null ? Generator.multiStepWaitingForAsyncCount : 0;
            overrunBaseline = Generator != null ? Generator.multiStepBudgetOverrunCount : 0;
            restartBaseline = Generator != null ? Generator.multiStepRestartCount : 0;
            cancellationBaseline = Generator != null ? Generator.multiStepCancellationCount : 0;
            failureBaseline = Generator != null ? Generator.multiStepFailureCount : 0;
            gcBaseline = new int[GC.MaxGeneration + 1];
            for (int i = 0; i < gcBaseline.Length; i++) gcBaseline[i] = GC.CollectionCount(i);
            Current = new RunSnapshot
            {
                Mode = activePolicy.ToString(), CacheEntriesAtStart = cache.EntryCount,
                SpawnedAvatars = avatars.Count, CapturedUtc = DateTime.UtcNow.ToString("O"),
                ConfigurationAtStart = CaptureConfiguration(), GarbageCollections = new int[gcBaseline.Length]
            };
            completedAvatars.Clear();
            started = false;
            idleFrames = 0;
            previousFrameTimestamp = frameTicks = firstReadyTimestamp = lastReadyTimestamp = spawnToReadyTicks = maximumSpawnToReadyTicks = 0;
            spawnTimestamps.Clear();
            nextSample = Time.realtimeSinceStartupAsDouble + Mathf.Max(.25f, MemorySampleInterval);
            Status = "Waiting for generation. Reset does not destroy avatars or clear the cache.";
        }

        private long MeshTicks => Generator != null
            ? Generator.meshpreprocessTicks + Generator.meshUpdatesTicks + Generator.multiStepDiscardedMeshTicks : 0;

        private static int PendingTextureWork => RenderTexToCPU.renderTexturesToCPU.Count + RenderTexToCPU.QueuedCopies.Count;

        private void Update()
        {
            if (IsRestarting) return;
            if (Generator == null && Time.realtimeSinceStartupAsDouble >= nextSample) ResolveSources();
            if (started && !Current.Completed && previousFrame != Time.frameCount)
            {
                long now = Stopwatch.GetTimestamp();
                long ticks = Math.Max(0, now - previousFrameTimestamp);
                double ms = UMATime.StopwatchTicksToMilliseconds(ticks);
                frameTicks += ticks;
                Current.ObservedFrameIntervals++;
                Current.AverageFrameMilliseconds = UMATime.StopwatchTicksToMilliseconds(frameTicks) / Current.ObservedFrameIntervals;
                Current.MaximumFrameMilliseconds = Math.Max(Current.MaximumFrameMilliseconds, ms);
                if (ms > 33.333) Current.FramesOver33Milliseconds++;
                if (ms > 100) Current.FramesOver100Milliseconds++;
                previousFrameTimestamp = now; previousFrame = Time.frameCount;
            }
            RefreshCounters();
            bool busy = LegacyCrowdGenerator != null && LegacyCrowdGenerator.IsGenerating ||
                Generator != null && (!Generator.IsIdle() || Generator.QueueSize() > 0) || PendingTextureWork > 0;
            if (busy)
            {
                BeginTiming();
                idleFrames = 0;
            }
            else if (started && !Current.Completed && Current.SpawnedAvatars > completedAvatars.Count)
            {
                idleFrames = 0;
                Status = $"Waiting for avatar builds: {completedAvatars.Count}/{Current.SpawnedAvatars} complete. Check the Console if this stops advancing.";
            }
            else if (started && !Current.Completed && Generator != null && ++idleFrames >= 2)
            {
                RefreshMemory();
                Current.Completed = true;
                Current.ConfigurationAtEnd = CaptureConfiguration();
                Current.CompletedAvatars = completedAvatars.Count;
                Status = Current.CountersReset ? "Counters changed during capture; restart for a valid comparison." : "Run complete.";
                if (Current.SpawnedAvatars > Current.CompletedAvatars)
                    Status += $" Only {Current.CompletedAvatars}/{Current.SpawnedAvatars} avatars reported a completed build; check the Console.";
                var saved = JsonUtility.FromJson<RunSnapshot>(JsonUtility.ToJson(Current));
                if (activePolicy == ReusePolicy.Off) LastOff = saved;
                if (activePolicy == ReusePolicy.On) LastOn = saved;
                if (LogCompletedRuns) LogReport();
            }
            if (Time.realtimeSinceStartupAsDouble >= nextSample && (!busy || SampleMemoryDuringGeneration)) RefreshMemory();
        }

        public void RefreshCounters()
        {
            if (Current != null && !Current.Completed)
            {
                Current.NPCShortcutInstances = 0; Current.NPCShortcutMilliseconds = 0;
                foreach (var avatar in avatars)
                    if (avatar is UMA.CharacterSystem.DynamicCharacterAvatar npc && npc != null)
                    {
                        npcBaselines.TryGetValue(avatar, out var baseline);
                        Current.NPCShortcutInstances += Math.Max(0, npc.NPCShortcutHits - baseline.count);
                        Current.NPCShortcutMilliseconds += Math.Max(0, npc.NPCShortcutMilliseconds - baseline.milliseconds);
                    }
            }
            Current.QueueSize = Generator != null ? Generator.QueueSize() : 0;
            Current.PendingTextureWork = PendingTextureWork;
            if (Current.Completed) return;
            IncludeBatchStart();
            if (started) Current.WallMilliseconds = UMATime.StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
            if (Generator != null)
            {
                Current.CountersReset |= Generator.ElapsedTicks < workBaseline || MeshTicks < meshTimeBaseline ||
                    Generator.textureprocessingTicks < textureTimeBaseline;
                Current.GeneratorMilliseconds = UMATime.StopwatchTicksToMilliseconds(Math.Max(0, Generator.ElapsedTicks - workBaseline));
                Current.MeshMilliseconds = UMATime.StopwatchTicksToMilliseconds(Math.Max(0, MeshTicks - meshTimeBaseline));
                Current.TextureMilliseconds = UMATime.StopwatchTicksToMilliseconds(Math.Max(0, Generator.textureprocessingTicks - textureTimeBaseline));
                Current.CountersReset |= Generator.atlasPreparationTicks < atlasPreparationBaseline ||
                    Generator.atlasLookupTicks < atlasLookupBaseline || Generator.atlasGenerationTicks < atlasGenerationBaseline ||
                    Generator.atlasEarlyHits < atlasEarlyHitsBaseline;
                Current.AtlasPreparationMilliseconds = UMATime.StopwatchTicksToMilliseconds(Math.Max(0, Generator.atlasPreparationTicks - atlasPreparationBaseline));
                Current.AtlasLookupMilliseconds = UMATime.StopwatchTicksToMilliseconds(Math.Max(0, Generator.atlasLookupTicks - atlasLookupBaseline));
                Current.AtlasGenerationMilliseconds = UMATime.StopwatchTicksToMilliseconds(Math.Max(0, Generator.atlasGenerationTicks - atlasGenerationBaseline));
                Current.AtlasEarlyHits = Math.Max(0, Generator.atlasEarlyHits - atlasEarlyHitsBaseline);
                Current.MeshUpdates = Math.Max(0, Generator.SlotsChanged - meshCountBaseline);
                Current.TextureUpdates = Math.Max(0, Generator.TextureChanged - textureCountBaseline);
            }
            Current.CountersReset |= cache != UMAGeneratedResourceCache.Shared;
            Current.MeshCache = UMAGeneratedResourceCache.ResourceStatistics.Since(UMAGeneratedResourceCache.Shared.GetStatistics<Mesh>(), meshBaseline);
            Current.TextureCache = UMAGeneratedResourceCache.ResourceStatistics.Since(UMAGeneratedResourceCache.Shared.GetStatistics<UMACachedAtlas>(), textureBaseline);
            Current.CompletedAvatars = completedAvatars.Count;
            Current.PeakQueue = Math.Max(Current.PeakQueue, Current.QueueSize);
            Current.SpawnTimings = SpawnDiagnostics?.Since(spawnTimingBaseline, Current.SpawnTimings);
            Current.GeneratorTimings = Generator?.GenerationTimings.Since(generatorTimingBaseline, Current.GeneratorTimings);
            Current.MeshLookupTimings = UMAResourceReuse.MeshTimings.Since(lookupTimingBaseline, Current.MeshLookupTimings);
            Current.OutsideGeneratorMilliseconds = Math.Max(0, Current.WallMilliseconds - Current.GeneratorMilliseconds);
            if (Generator != null)
            {
                Current.ValidationMilliseconds = DeltaMs(Generator.validationTicks, validationBaseline);
                Current.MeshPreprocessMilliseconds = DeltaMs(Generator.meshpreprocessTicks, preprocessBaseline);
                Current.BegunEventsMilliseconds = DeltaMs(Generator.BegunEventsTicks, begunBaseline);
                Current.DnaPreApplyMilliseconds = DeltaMs(Generator.preapplyTicks, preapplyBaseline);
                Current.SkeletonMilliseconds = DeltaMs(Generator.skeletonUpdatesTicks, skeletonBaseline);
                Current.RaceBlendshapesMilliseconds = DeltaMs(Generator.raceblendshapesTicks, raceBaseline);
                Current.EndEventsMilliseconds = DeltaMs(Generator.endEventsTicks, endEventsBaseline);
                Current.AsyncYieldCount = Math.Max(0, Generator.multiStepWaitingForAsyncCount - asyncYieldBaseline);
                Current.BudgetOverrunCount = Math.Max(0, Generator.multiStepBudgetOverrunCount - overrunBaseline);
                Current.RestartCount = Math.Max(0, Generator.multiStepRestartCount - restartBaseline);
                Current.CancellationCount = Math.Max(0, Generator.multiStepCancellationCount - cancellationBaseline);
                Current.FailureCount = Math.Max(0, Generator.multiStepFailureCount - failureBaseline);
            }
            if (gcBaseline != null)
                for (int i = 0; i < gcBaseline.Length; i++) Current.GarbageCollections[i] = Math.Max(0, GC.CollectionCount(i) - gcBaseline[i]);
            Current.FirstAvatarReadyMilliseconds = firstReadyTimestamp == 0 ? 0 : DeltaMs(firstReadyTimestamp, startTimestamp);
            Current.LastAvatarReadyMilliseconds = lastReadyTimestamp == 0 ? 0 : DeltaMs(lastReadyTimestamp, startTimestamp);
            Current.AverageSpawnToReadyMilliseconds = completedAvatars.Count == 0 ? 0 : UMATime.StopwatchTicksToMilliseconds(spawnToReadyTicks) / completedAvatars.Count;
            Current.MaximumSpawnToReadyMilliseconds = UMATime.StopwatchTicksToMilliseconds(maximumSpawnToReadyTicks);
        }

        private static double DeltaMs(long value, long baseline) => UMATime.StopwatchTicksToMilliseconds(Math.Max(0, value - baseline));

        private void IncludeBatchStart()
        {
            long batchStart = SpawnDiagnostics?.LastBatchStartTimestamp ?? 0;
            if (batchStart >= captureResetTimestamp && batchStart > 0 && batchStart < startTimestamp)
                startTimestamp = batchStart;
        }

        private Configuration CaptureConfiguration() => new Configuration
        {
            Scene = gameObject.scene.name, CrowdController = LegacyCrowdGenerator != null ? nameof(UMARandomAvatar) : CrowdGenerator != null ? nameof(UMARandomAvatarV2) : "None",
            CharacterPrefab = LegacyCrowdGenerator != null ? LegacyCrowdGenerator.prefab?.name : CrowdGenerator?.Generation.Prefab?.name,
            MaximumUniqueCharacters = LegacyCrowdGenerator != null ? LegacyCrowdGenerator.MaximumUniqueCharacters : 0,
            SpawnBudgetMilliseconds = LegacyCrowdGenerator != null ? LegacyCrowdGenerator.SpawnBudgetMilliseconds : 0,
            UseNPCBuilds = LegacyCrowdGenerator != null && (LegacyCrowdGenerator.UseNPCBuilds ||
                LegacyCrowdGenerator.prefab != null && LegacyCrowdGenerator.prefab.TryGetComponent<UMA.CharacterSystem.DynamicCharacterAvatar>(out var npc) && npc.useNPCBuilds),
            GridX = LegacyCrowdGenerator != null ? LegacyCrowdGenerator.GridXSize : CrowdGenerator?.Generation.GridXSize ?? 0,
            GridZ = LegacyCrowdGenerator != null ? LegacyCrowdGenerator.GridZSize : CrowdGenerator?.Generation.GridZSize ?? 0,
            GeneratorType = Generator?.GetType().FullName, MeshCombiner = Generator?.meshCombiner?.GetType().FullName,
            IterationCount = Generator != null ? Generator.IterationCount : 0, InterFrameDelay = Generator != null ? Generator.InterFrameDelay : 0,
            MultiStepBudgetMilliseconds = Generator != null ? Generator.MaxMultiStepWorkMilliseconds : 0,
            ProcessAllPending = Generator != null && Generator.processAllPending, AtlasResolution = Generator != null ? Generator.atlasResolution : 0,
            ConvertRenderTextures = Generator != null && Generator.convertRenderTexture, AsyncConversion = Generator != null && Generator.useAsyncConversion,
            SampleMemoryDuringGeneration = SampleMemoryDuringGeneration, QualityLevel = QualitySettings.GetQualityLevel(),
            VSyncCount = QualitySettings.vSyncCount, TargetFrameRate = Application.targetFrameRate
        };

        public void RefreshMemory()
        {
            long stamp = Stopwatch.GetTimestamp();
            memoryAvatars.Clear();
            if (AvatarRoot != null) AvatarRoot.GetComponentsInChildren(true, memoryAvatars);
            else if (HasCrowd) memoryAvatars.AddRange(avatars);
            else
                foreach (var data in FindObjectsByType<UMAData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (data.gameObject.scene == gameObject.scene) memoryAvatars.Add(data);
            Current.Memory = UMAGeneratedResourceMemory.Capture(memoryAvatars);
            var notes = new Dictionary<string, int>();
            foreach (var data in memoryAvatars)
                if (data != null && !string.IsNullOrEmpty(data.resourceReuseStatus))
                {
                    notes.TryGetValue(data.resourceReuseStatus, out int count);
                    notes[data.resourceReuseStatus] = count + 1;
                }
            Current.ReuseNotes = new string[notes.Count];
            int noteIndex = 0;
            foreach (var entry in notes) Current.ReuseNotes[noteIndex++] = $"{entry.Value} avatars: {entry.Key}";
            memoryAvatars.Clear();
            Current.MemorySampleMilliseconds = UMATime.StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - stamp);
            nextSample = Time.realtimeSinceStartupAsDouble + Mathf.Max(.25f, MemorySampleInterval);
        }

        public void RestartCrowd(bool enableReuse)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsRestarting) return;
            ResolveSources();
            if (!HasCrowd) { Status = "Assign a random crowd controller to restart a comparison run."; return; }
            restart = StartCoroutine(RestartRoutine(enableReuse));
        }

        private IEnumerator RestartRoutine(bool enableReuse)
        {
            Status = "Waiting for generation and texture readbacks to finish...";
            while (Generator != null && (!Generator.IsIdle() || Generator.QueueSize() > 0) || PendingTextureWork > 0)
                yield return null;
            suspendedGenerator = Generator;
            restoreGeneratorEnabled = Generator != null && Generator.enabled;
            if (Generator != null) Generator.enabled = false;
            try
            {
                Status = "Releasing the previous crowd...";
                UnregisterAvatars();
                if (LegacyCrowdGenerator != null)
                {
                    LegacyCrowdGenerator.DestroyGeneratedCharacters();
                    LegacyCrowdGenerator.ClearCharacterSetupPool();
                }
                else CrowdGenerator.DestroyGeneratedCharacters();
                yield return null;
                yield return null;
                // Enabling the generator clears old scheduler state. Do it before new avatars enqueue builds.
                RestoreGenerator();
                activePolicy = enableReuse ? ReusePolicy.On : ReusePolicy.Off;
                ResetCapture();
                BeginTiming();
                if (LegacyCrowdGenerator != null) LegacyCrowdGenerator.GenerateCharacters(true);
                else CrowdGenerator.GenerateCharacters(true);
            }
            finally
            {
                RestoreGenerator();
                restart = null;
            }
        }

        private void RestoreGenerator()
        {
            if (suspendedGenerator != null) suspendedGenerator.enabled = restoreGeneratorEnabled;
            suspendedGenerator = null;
        }

        private void UnregisterAvatars()
        {
            foreach (var data in avatars) if (data != null) data.OnCharacterUpdated -= AvatarCompleted;
            avatars.Clear();
            npcBaselines.Clear();
            completedAvatars.Clear();
            spawnTimestamps.Clear();
        }

        public string GetReport()
        {
            RefreshCounters();
            var report = new Report
            {
                ExportedUtc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(), IsEditor = Application.isEditor, DevelopmentBuild = Debug.isDebugBuild,
                OperatingSystem = SystemInfo.operatingSystem, Processor = SystemInfo.processorType, ProcessorCount = SystemInfo.processorCount,
                SystemMemoryMB = SystemInfo.systemMemorySize, GraphicsDevice = SystemInfo.graphicsDeviceName, GraphicsMemoryMB = SystemInfo.graphicsMemorySize,
                MeasurementNotes = TimingNotes, Current = Current, LastOff = LastOff, LastOn = LastOn
            };
#if UNITY_EDITOR
            report.UmaVersion = UMASettings.GetSettings().UMAVersion;
#endif
            return JsonUtility.ToJson(report, true);
        }

        public void LogReport()
        {
            Debug.Log("UMA resource usage (generator/cache counters are session-wide; memory is scoped to this crowd):\n" + GetReport(), this);
        }

        public string SaveReport()
        {
            string path = Path.Combine(Application.persistentDataPath,
                "UMA-Resource-Usage-" + Current.Mode + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json");
            return SaveReport(path);
        }

        public string SaveReport(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a JSON report path.", nameof(path));
            File.WriteAllText(path, GetReport());
            LastSavedReportPath = path;
            Debug.Log("UMA resource report saved to " + path, this);
            return path;
        }
    }
}
