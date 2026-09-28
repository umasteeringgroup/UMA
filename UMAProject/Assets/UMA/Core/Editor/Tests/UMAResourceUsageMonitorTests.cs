#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Tests
{
    public sealed class UMAResourceUsageMonitorTests
    {
        private readonly List<Object> objects = new List<Object>();
        private readonly List<UMAData> avatars = new List<UMAData>();

        private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }

        private UMAData Avatar(Mesh mesh, Texture texture)
        {
            var go = Keep(new GameObject("Memory census avatar"));
            go.SetActive(false);
            var data = go.AddComponent<UMAData>();
            var first = go.AddComponent<SkinnedMeshRenderer>();
            var second = Keep(new GameObject("Second pass renderer")).AddComponent<SkinnedMeshRenderer>();
            first.sharedMesh = second.sharedMesh = mesh;
            data.SetRenderers(new[] { first, second });
            data.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial { resultingAtlasList = new[] { texture, texture } });
            data.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial { resultingAtlasList = new[] { texture } });
            avatars.Add(data);
            return data;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var data in avatars)
                if (data != null) { data.SetRenderers(null); data.generatedMaterials.materials.Clear(); }
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            avatars.Clear();
            objects.Clear();
        }

        [Test]
        public void SharedObjectsCountOnceGloballyAndOncePerAvatarNotPerPass()
        {
            var mesh = Keep(new Mesh());
            var texture = Keep(new Texture2D(8, 8));
            var a = Avatar(mesh, texture);
            var b = Avatar(mesh, texture);
            int measurements = 0;
            var result = UMAGeneratedResourceMemory.Capture(new[] { a, b, a, null }, obj =>
            { measurements++; return obj is Mesh ? 100 : 200; });
            Assert.That(result.Avatars, Is.EqualTo(2));
            Assert.That(result.Renderers, Is.EqualTo(4));
            Assert.That(result.UniqueMeshes, Is.EqualTo(1));
            Assert.That(result.UniqueTextures, Is.EqualTo(1));
            Assert.That(result.MeshUses, Is.EqualTo(2));
            Assert.That(result.TextureUses, Is.EqualTo(2));
            Assert.That(result.TotalBytes, Is.EqualTo(300));
            Assert.That(result.AvoidedBytes, Is.EqualTo(300));
            Assert.That(result.SharedMeshUses, Is.EqualTo(1));
            Assert.That(result.SharedTextureUses, Is.EqualTo(1));
            Assert.That(measurements, Is.EqualTo(2));
        }

        [Test]
        public void PrivateObjectsAndUnavailableSizesAreReportedWithoutInventingSavings()
        {
            Avatar(Keep(new Mesh()), Keep(new Texture2D(8, 8)));
            Avatar(Keep(new Mesh()), Keep(new Texture2D(8, 8)));
            var privateMemory = UMAGeneratedResourceMemory.Capture(avatars, obj => 100);
            Assert.That(privateMemory.TotalBytes, Is.EqualTo(400));
            Assert.That(privateMemory.AvoidedBytes, Is.Zero);
            Assert.That(privateMemory.UniqueMeshes, Is.EqualTo(2));
            Assert.That(privateMemory.UniqueTextures, Is.EqualTo(2));
            var unavailable = UMAGeneratedResourceMemory.Capture(avatars, obj => 0);
            Assert.That(unavailable.UnavailableMemorySizes, Is.EqualTo(4));
        }

        [Test]
        public void TypedCacheCountersSeparateMeshAtlasAndMaterialAndExcludeRetains()
        {
            var cache = new UMAGeneratedResourceCache();
            var key = new UMAGeneratedResourceKey("test mesh", new byte[] { 1 });
            using (var first = cache.Acquire<Mesh>(key))
            using (var pending = cache.Acquire<Mesh>(key))
            {
                first.Publish(new Mesh());
                using (var hit = cache.Acquire<Mesh>(key))
                using (var retained = hit.Retain())
                using (var texture = cache.Acquire<UMACachedAtlas>(new UMAGeneratedResourceKey("test atlas", new byte[] { 1 })))
                using (var material = cache.Acquire<Material>(new UMAGeneratedResourceKey("test material", new byte[] { 1 })))
                {
                    var counters = cache.GetStatistics<Mesh>();
                    Assert.That(counters.Hits, Is.EqualTo(1));
                    Assert.That(counters.Misses, Is.EqualTo(1));
                    Assert.That(counters.PendingJoins, Is.EqualTo(1));
                    Assert.That(counters.Publications, Is.EqualTo(1));
                    Assert.That(counters.Requests, Is.EqualTo(3));
                    Assert.That(cache.GetStatistics<UMACachedAtlas>().Hits, Is.Zero);
                    Assert.That(cache.GetStatistics<UMACachedAtlas>().Misses, Is.EqualTo(1));
                    Assert.That(cache.GetStatistics<Material>().Misses, Is.EqualTo(1));
                }
            }
            Assert.That(cache.EntryCount, Is.Zero);
            Assert.That(cache.ReferenceCount, Is.Zero);
            Assert.That(cache.GetStatistics<Mesh>().Hits, Is.EqualTo(1), "Lifetime counts survive released resources.");
        }

        [Test]
        public void AtlasBypassesAreVisibleSeparatelyFromCacheMissesAndHits()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture())
            {
                var cache = UMAGeneratedResourceCache.Shared;
                var before = cache.GetStatistics<UMACachedAtlas>();
                var data = f.Avatar();
                data.AtlasUpdated = new UMATextureEvent();
                data.AtlasUpdated.AddListener((avatar, parameters) => { });
                f.Build(data);
                var after = cache.GetStatistics<UMACachedAtlas>();
                var delta = UMAGeneratedResourceCache.ResourceStatistics.Since(after, before);
                Assert.That(delta.Bypasses, Is.GreaterThan(0));
                Assert.That(delta.Requests, Is.Zero);
                Assert.That(delta.LastBypassReason, Does.Contain("AtlasUpdated"));
                Assert.That(UMAGeneratedResourceCache.ResourceStatistics.Since(after, after).LastBypassReason, Is.Null);
                Assert.That(data.generatedMaterials.materials[0].resultingAtlasList[0], Is.Not.Null);
            }
        }

        [Test]
        public void MonitorUsesStopwatchUnitsAndBaselinesWithoutResettingTheGenerator()
        {
            var go = Keep(new GameObject("Usage monitor timing"));
            go.SetActive(false);
            var generator = go.AddComponent<UMAGenerator>();
            var monitor = go.AddComponent<UMAResourceUsageMonitor>();
            monitor.Generator = generator;
            generator.ElapsedTicks = Stopwatch.Frequency;
            generator.meshUpdatesTicks = Stopwatch.Frequency;
            monitor.ResetCapture();
            generator.ElapsedTicks += Stopwatch.Frequency * 3;
            generator.meshUpdatesTicks += Stopwatch.Frequency;
            generator.meshpreprocessTicks += Stopwatch.Frequency / 2;
            generator.textureprocessingTicks += Stopwatch.Frequency;
            generator.atlasPreparationTicks += Stopwatch.Frequency / 4;
            generator.atlasLookupTicks += Stopwatch.Frequency / 8;
            generator.atlasGenerationTicks += Stopwatch.Frequency / 2;
            generator.atlasEarlyHits += 7;
            monitor.RefreshCounters();
            Assert.That(monitor.Current.GeneratorMilliseconds, Is.EqualTo(3000).Within(.01));
            Assert.That(monitor.Current.MeshMilliseconds, Is.EqualTo(1500).Within(.01));
            Assert.That(monitor.Current.TextureMilliseconds, Is.EqualTo(1000).Within(.01));
            Assert.That(monitor.Current.AtlasPreparationMilliseconds, Is.EqualTo(250).Within(.01));
            Assert.That(monitor.Current.AtlasLookupMilliseconds, Is.EqualTo(125).Within(.01));
            Assert.That(monitor.Current.AtlasGenerationMilliseconds, Is.EqualTo(500).Within(.01));
            Assert.That(monitor.Current.AtlasEarlyHits, Is.EqualTo(7));
            monitor.ResetCapture();
            monitor.RefreshCounters();
            Assert.That(monitor.Current.GeneratorMilliseconds, Is.Zero);
            Assert.That(monitor.Current.AtlasLookupMilliseconds, Is.Zero);
            Assert.That(monitor.Current.AtlasEarlyHits, Is.Zero);
            Assert.That(generator.ElapsedTicks, Is.EqualTo(Stopwatch.Frequency * 4));
            generator.ResetStatistics();
            monitor.RefreshCounters();
            Assert.That(monitor.Current.CountersReset, Is.True);
        }

        [Test]
        public void InspectorImportsWithComparisonAndRestartControls()
        {
            var go = Keep(new GameObject("Usage monitor inspector"));
            go.SetActive(false);
            var monitor = go.AddComponent<UMAResourceUsageMonitor>();
            var editor = Keep(UnityEditor.Editor.CreateEditor(monitor));
            Assert.That(editor, Is.TypeOf<UMAResourceUsageMonitorEditor>());
            var root = editor.CreateInspectorGUI();
            Assert.That(root.Q<Button>("restartOff"), Is.Not.Null);
            Assert.That(root.Q<Button>("restartOn"), Is.Not.Null);
            Assert.That(root.Q<Button>("save"), Is.Not.Null);
            Assert.That(root.Q<Button>("reveal"), Is.Not.Null);
            Assert.That(root.Q<Foldout>("spawnTimings").childCount, Is.GreaterThan(0));
            Assert.That(root.Q<Foldout>("schedulingTimings").childCount, Is.GreaterThan(0));
            Assert.That(root.Q("comparison").childCount, Is.EqualTo(12));
            Assert.That(root.Q<Label>("notes").text, Does.Contain("not GPU time"));
        }

        [Test]
        public void DiagnosticDeltasUseStopwatchUnitsAndReuseDestinationWithoutChangingBaseline()
        {
            var timings = new UMAGenerationDiagnostics();
            var stage = UMAGenerationDiagnostics.Stage.Instantiate;
            timings.Record(stage, Stopwatch.Frequency);
            var baseline = timings.Baseline();
            timings.Record(stage, Stopwatch.Frequency * 2);
            timings.Record(stage, Stopwatch.Frequency * 4);
            var result = timings.Since(baseline);
            Assert.That(result[(int)stage].Samples, Is.EqualTo(2));
            Assert.That(result[(int)stage].TotalMilliseconds, Is.EqualTo(6000).Within(.01));
            Assert.That(result[(int)stage].AverageMilliseconds, Is.EqualTo(3000).Within(.01));
            Assert.That(timings.Since(baseline, result), Is.SameAs(result));
            Assert.That(baseline[(int)stage].Ticks, Is.EqualTo(Stopwatch.Frequency));
        }

        [Test]
        public void SchedulerGapsAreOnlyRecordedWhilePendingAndUsePreviousYieldReason()
        {
            var timings = new UMAGenerationDiagnostics();
            timings.BeginWork(10);
            timings.EndWork(20, true, UMAGenerationDiagnostics.YieldReason.Async);
            timings.BeginWork(20 + Stopwatch.Frequency);
            timings.EndWork(30 + Stopwatch.Frequency, false, UMAGenerationDiagnostics.YieldReason.IterationLimit);
            timings.BeginWork(30 + Stopwatch.Frequency * 4);
            var result = timings.Since(null);
            Assert.That(result[(int)UMAGenerationDiagnostics.Stage.BetweenUpdatesAsync].TotalMilliseconds, Is.EqualTo(1000).Within(.01));
            Assert.That(result[(int)UMAGenerationDiagnostics.Stage.BetweenUpdatesIterationLimit].Samples, Is.Zero);
        }

        [Test]
        public void BuildSlicesAccumulateAcrossAsyncYieldsWithoutDoubleCountingNestedCalls()
        {
            var timings = new UMAGenerationDiagnostics();
            var key = new UMAGeneratedResourceKey("diagnostic build test", new byte[] { 93, 41, 17 });
            var cache = UMAGeneratedResourceCache.Shared;
            timings.BeginBuildSlice();
            timings.BeginBuildSlice();
            using (var miss = cache.Acquire<Mesh>(key))
            {
                miss.Publish(new Mesh());
                timings.EndBuildSlice(true);
                timings.EndBuildSlice(true);
                Assert.That(timings.Since(null)[(int)UMAGenerationDiagnostics.Stage.BuildMeshMissOrMixed].Samples, Is.Zero);
                timings.BeginBuildSlice();
                using (var hit = cache.Acquire<Mesh>(key)) timings.EndBuildSlice(false);
                var result = timings.Since(null);
                Assert.That(result[(int)UMAGenerationDiagnostics.Stage.BuildMeshMissOrMixed].Samples, Is.EqualTo(1));
                Assert.That(result[(int)UMAGenerationDiagnostics.Stage.BuildAllMeshHits].Samples, Is.Zero);
                timings.BeginBuildSlice();
                using (var hit = cache.Acquire<Mesh>(key)) timings.EndBuildSlice(false);
                Assert.That(timings.Since(null)[(int)UMAGenerationDiagnostics.Stage.BuildAllMeshHits].Samples, Is.EqualTo(1));
            }
        }

        [Test]
        public void JsonExportIncludesSettingsBreakdownsAndCanBeSavedToChosenPath()
        {
            var go = Keep(new GameObject("Timing export"));
            go.SetActive(false);
            var generator = go.AddComponent<UMAGenerator>();
            var monitor = go.AddComponent<UMAResourceUsageMonitor>();
            monitor.Generator = generator;
            generator.IterationCount = 8;
            monitor.ResetCapture();
            generator.GenerationTimings.Record(UMAGenerationDiagnostics.Stage.BuildAllMeshHits, Stopwatch.Frequency);
            generator.validationTicks += Stopwatch.Frequency / 4;
            monitor.RefreshCounters();
            var report = JsonUtility.FromJson<UMAResourceUsageMonitor.Report>(monitor.GetReport());
            Assert.That(report.SchemaVersion, Is.EqualTo(2));
            Assert.That(report.UnityVersion, Is.EqualTo(Application.unityVersion));
            Assert.That(report.Current.ConfigurationAtStart.IterationCount, Is.EqualTo(8));
            Assert.That(report.Current.ValidationMilliseconds, Is.EqualTo(250).Within(.01));
            Assert.That(report.Current.GeneratorTimings[(int)UMAGenerationDiagnostics.Stage.BuildAllMeshHits].TotalMilliseconds, Is.EqualTo(1000).Within(.01));
            Assert.That(report.MeasurementNotes, Does.Contain("NOT idle CPU"));
            string path = Path.Combine(Application.temporaryCachePath, "UMA-Timing-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.That(monitor.SaveReport(path), Is.EqualTo(path));
                Assert.That(monitor.LastSavedReportPath, Is.EqualTo(path));
                Assert.That(JsonUtility.FromJson<UMAResourceUsageMonitor.Report>(File.ReadAllText(path)).SchemaVersion, Is.EqualTo(2));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            monitor.ResetCapture();
            monitor.RefreshCounters();
            Assert.That(monitor.Current.GeneratorTimings[(int)UMAGenerationDiagnostics.Stage.BuildAllMeshHits].Samples, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealAtlasAndMeshBuildsExposeMemoryAndReuseCounts(bool reuse)
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture())
            {
                var cache = UMAGeneratedResourceCache.Shared;
                var meshesBefore = cache.GetStatistics<Mesh>();
                var atlasesBefore = cache.GetStatistics<UMACachedAtlas>();
                var a = f.Avatar(reuse); a.reuseGeneratedMeshes = reuse;
                var b = f.Avatar(reuse); b.reuseGeneratedMeshes = reuse;
                var lookupBaseline = UMAResourceReuse.MeshTimings.Baseline();
                f.Build(a); f.Meshes.Build(a);
                f.Build(b); f.Meshes.Build(b);
                var memory = UMAGeneratedResourceMemory.Capture(new[] { a, b });
                Assert.That(memory.UniqueMeshes, Is.EqualTo(reuse ? 1 : 2));
                Assert.That(memory.UniqueTextures, Is.EqualTo(reuse ? 1 : 2));
                Assert.That(memory.TotalBytes, Is.GreaterThan(0));
                var meshCounts = UMAGeneratedResourceCache.ResourceStatistics.Since(cache.GetStatistics<Mesh>(), meshesBefore);
                var textureCounts = UMAGeneratedResourceCache.ResourceStatistics.Since(cache.GetStatistics<UMACachedAtlas>(), atlasesBefore);
                Assert.That(meshCounts.Hits, Is.EqualTo(reuse ? 1 : 0));
                Assert.That(textureCounts.Hits, Is.EqualTo(reuse ? 1 : 0));
                Assert.That(memory.SharedMeshUses, Is.EqualTo(reuse ? 1 : 0));
                Assert.That(memory.SharedTextureUses, Is.EqualTo(reuse ? 1 : 0));
                var timings = UMAResourceReuse.MeshTimings.Since(lookupBaseline);
                Assert.That(timings[(int)UMAGenerationDiagnostics.Stage.MeshLookupHit].Samples, Is.EqualTo(reuse ? 1 : 0));
                Assert.That(timings[(int)UMAGenerationDiagnostics.Stage.MeshLookupMiss].Samples, Is.EqualTo(reuse ? 1 : 0));
                Assert.That(timings[(int)UMAGenerationDiagnostics.Stage.MeshBinding].Samples, Is.EqualTo(reuse ? 1 : 0));
            }
        }
    }
}
#endif
