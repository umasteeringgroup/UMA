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
        }

        public RunSnapshot Current { get; private set; } = new RunSnapshot();
        public RunSnapshot LastOff { get; private set; }
        public RunSnapshot LastOn { get; private set; }
        public string Status { get; private set; } = "Enter Play mode to measure a crowd.";
        public bool IsRestarting => restart != null;
        public bool HasCrowd => LegacyCrowdGenerator != null ||
            CrowdGenerator != null && CrowdGenerator.mode == UMARandomAvatarV2.Mode.Generate;

        private readonly HashSet<UMAData> avatars = new HashSet<UMAData>();
        private readonly HashSet<UMAData> completedAvatars = new HashSet<UMAData>();
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

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ResolveSources();
            activePolicy = InitialReuse;
            if (LegacyCrowdGenerator != null)
            {
                LegacyCrowdGenerator.RandomAvatarGenerated ??= new UMARandomAvatarEvent();
                legacyEvent = LegacyCrowdGenerator.RandomAvatarGenerated;
                legacyEvent.AddListener(RegisterSpawnedAvatar);
            }
            if (CrowdGenerator != null && CrowdGenerator.mode == UMARandomAvatarV2.Mode.Generate)
            {
                CrowdGenerator.Generation.RandomAvatarGenerated ??= new UMARandomAvatarEvent();
                crowdEvent = CrowdGenerator.Generation.RandomAvatarGenerated;
                crowdEvent.AddListener(RegisterSpawnedAvatar);
            }
            ResetCapture();
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
            if (HasCrowd) return;
            foreach (var candidate in FindObjectsByType<UMARandomAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { LegacyCrowdGenerator = candidate; return; }
            foreach (var candidate in FindObjectsByType<UMARandomAvatarV2>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene && candidate.mode == UMARandomAvatarV2.Mode.Generate)
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
            if (avatars.Add(data)) data.OnCharacterUpdated += AvatarCompleted;
            Current.SpawnedAvatars = avatars.Count;
            BeginTiming();
        }

        private void AvatarCompleted(UMAData data) => completedAvatars.Add(data);

        private void BeginTiming()
        {
            if (started) return;
            startTimestamp = Stopwatch.GetTimestamp();
            started = true;
            Status = "Generating...";
        }

        public void ResetCapture()
        {
            ResolveSources();
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
            Current = new RunSnapshot
            {
                Mode = activePolicy.ToString(), CacheEntriesAtStart = cache.EntryCount,
                SpawnedAvatars = avatars.Count, CapturedUtc = DateTime.UtcNow.ToString("O")
            };
            completedAvatars.Clear();
            started = false;
            idleFrames = 0;
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
            RefreshCounters();
            bool busy = Generator != null && (!Generator.IsIdle() || Generator.QueueSize() > 0) || PendingTextureWork > 0;
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
            Current.QueueSize = Generator != null ? Generator.QueueSize() : 0;
            Current.PendingTextureWork = PendingTextureWork;
            if (Current.Completed) return;
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
        }

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
                if (LegacyCrowdGenerator != null) LegacyCrowdGenerator.DestroyGeneratedCharacters();
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
            completedAvatars.Clear();
        }

        public string GetReport() => JsonUtility.ToJson(Current, true);

        public void LogReport()
        {
            Debug.Log("UMA resource usage (generator/cache counters are session-wide; memory is scoped to this crowd):\n" + GetReport(), this);
        }

        public string SaveReport()
        {
            string path = Path.Combine(Application.persistentDataPath,
                "UMA-Resource-Usage-" + Current.Mode + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(path, GetReport());
            Debug.Log("UMA resource report saved to " + path, this);
            return path;
        }
    }
}
