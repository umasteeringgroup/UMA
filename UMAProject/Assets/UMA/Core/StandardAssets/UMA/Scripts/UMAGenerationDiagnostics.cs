using System;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UMA
{
    // Main-thread, cumulative counters. Scopes are structs; recording allocates no per-frame objects.
    public sealed class UMAGenerationDiagnostics
    {
        public enum Stage
        {
            SpawnBatch, Instantiate, SpawnCallbacksAndSetup, Randomization, AnimatorSetup, RecipeAndEnqueue,
            GeneratorWorkCall, BuildAllMeshHits, BuildMeshMissOrMixed, BuildPendingMesh, BuildNoMeshRequests,
            BuildElapsed, BetweenUpdatesAsync, BetweenUpdatesBudget, BetweenUpdatesIterationLimit,
            BetweenUpdatesInterFrameDelay, BetweenUpdatesOther,
            MeshLookupHit, MeshLookupMiss, MeshLookupPending, MeshLookupFailed, MeshBinding,
            TextureAllAtlasHits, TextureAtlasMissOrMixed, TexturePendingAtlas, TextureNoAtlasRequests, BuildInterrupted, Count
        }

        public enum YieldReason { Other, Async, Budget, IterationLimit, InterFrameDelay }

        [Serializable]
        public struct Measurement
        {
            public string Stage;
            public long Samples;
            public double TotalMilliseconds;
            public double AverageMilliseconds;
        }

        public struct Counter { public long Ticks, Samples; }
        private readonly Counter[] counters = new Counter[(int)Stage.Count];
        private static readonly string[] stageNames = Enum.GetNames(typeof(Stage));
        public long LastBatchStartTimestamp { get; private set; }
        private long previousWorkEnd;
        private bool previouslyPending;
        private YieldReason previousYield;
        private int buildDepth;
        private bool buildActive;
        private long buildStart, sliceStart, buildCpu;
        private long buildHits, buildMisses, buildPending, buildBypasses;
        private UMAGeneratedResourceCache.ResourceStatistics sliceCache;

        public readonly struct Scope : IDisposable
        {
            private readonly UMAGenerationDiagnostics owner;
            private readonly Stage stage;
            private readonly long start;
            internal Scope(UMAGenerationDiagnostics owner, Stage stage)
            {
                this.owner = owner; this.stage = stage; start = Stopwatch.GetTimestamp();
                if (stage == Stage.SpawnBatch) owner.LastBatchStartTimestamp = start;
            }
            public void Dispose() => owner.Record(stage, Stopwatch.GetTimestamp() - start);
        }

        public Scope Measure(Stage stage) => new Scope(this, stage);

        public readonly struct TextureScope : IDisposable
        {
            private readonly UMAGenerationDiagnostics owner;
            private readonly long start;
            private readonly UMAGeneratedResourceCache.ResourceStatistics before;
            public TextureScope(UMAGenerationDiagnostics owner)
            {
                this.owner = owner; start = Stopwatch.GetTimestamp();
                before = UMAGeneratedResourceCache.Shared.GetStatistics<UMACachedAtlas>();
            }
            public void Dispose()
            {
                if (owner == null) return;
                var delta = UMAGeneratedResourceCache.ResourceStatistics.Since(
                    UMAGeneratedResourceCache.Shared.GetStatistics<UMACachedAtlas>(), before);
                owner.Record(delta.Misses > 0 || delta.Hits > 0 && delta.Bypasses > 0 ? Stage.TextureAtlasMissOrMixed : delta.PendingJoins > 0 ? Stage.TexturePendingAtlas :
                    delta.Hits > 0 ? Stage.TextureAllAtlasHits : Stage.TextureNoAtlasRequests, Stopwatch.GetTimestamp() - start);
            }
        }

        public void Record(Stage stage, long ticks)
        {
            ref Counter counter = ref counters[(int)stage];
            counter.Ticks += Math.Max(0, ticks);
            counter.Samples++;
        }

        public Counter[] Baseline() => (Counter[])counters.Clone();

        public Measurement[] Since(Counter[] baseline, Measurement[] result = null)
        {
            if (result == null || result.Length != counters.Length) result = new Measurement[counters.Length];
            for (int i = 0; i < result.Length; i++)
            {
                long samples = Math.Max(0, counters[i].Samples - (baseline?[i].Samples ?? 0));
                double milliseconds = UMATime.StopwatchTicksToMilliseconds(
                    Math.Max(0, counters[i].Ticks - (baseline?[i].Ticks ?? 0)));
                result[i] = new Measurement { Stage = stageNames[i], Samples = samples,
                    TotalMilliseconds = milliseconds, AverageMilliseconds = samples == 0 ? 0 : milliseconds / samples };
            }
            return result;
        }

        public void BeginWork(long timestamp)
        {
            if (!previouslyPending || previousWorkEnd == 0) return;
            Stage stage = previousYield switch
            {
                YieldReason.Async => Stage.BetweenUpdatesAsync,
                YieldReason.Budget => Stage.BetweenUpdatesBudget,
                YieldReason.IterationLimit => Stage.BetweenUpdatesIterationLimit,
                YieldReason.InterFrameDelay => Stage.BetweenUpdatesInterFrameDelay,
                _ => Stage.BetweenUpdatesOther
            };
            Record(stage, timestamp - previousWorkEnd);
        }

        public void EndWork(long timestamp, bool pending, YieldReason reason)
        {
            previousWorkEnd = timestamp; previouslyPending = pending; previousYield = reason;
        }

        public void ClearPendingTracking()
        {
            previousWorkEnd = 0; previouslyPending = false;
            if (buildDepth != 0 || !buildActive) return;
            Record(Stage.BuildInterrupted, buildCpu);
            buildActive = false;
        }

        public void BeginBuildSlice()
        {
            if (buildDepth++ != 0) return;
            sliceStart = Stopwatch.GetTimestamp();
            sliceCache = UMAGeneratedResourceCache.Shared.GetStatistics<Mesh>();
            if (buildActive) return;
            buildActive = true; buildStart = sliceStart; buildCpu = buildHits = buildMisses = buildPending = buildBypasses = 0;
        }

        public void EndBuildSlice(bool stillActive)
        {
            if (--buildDepth != 0) return;
            long end = Stopwatch.GetTimestamp();
            var delta = UMAGeneratedResourceCache.ResourceStatistics.Since(
                UMAGeneratedResourceCache.Shared.GetStatistics<Mesh>(), sliceCache);
            buildCpu += end - sliceStart;
            buildHits += delta.Hits; buildMisses += delta.Misses; buildPending += delta.PendingJoins;
            buildBypasses += delta.Bypasses;
            if (stillActive) return;
            Record(buildMisses > 0 || buildHits > 0 && buildBypasses > 0 ? Stage.BuildMeshMissOrMixed : buildPending > 0 ? Stage.BuildPendingMesh :
                buildHits > 0 ? Stage.BuildAllMeshHits : Stage.BuildNoMeshRequests, buildCpu);
            Record(Stage.BuildElapsed, end - buildStart);
            buildActive = false;
        }
    }
}
