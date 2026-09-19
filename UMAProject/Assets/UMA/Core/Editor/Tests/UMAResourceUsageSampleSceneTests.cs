#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public sealed class UMAResourceUsageSampleSceneTests
    {
        private SceneSetup[] previousScenes;
        private bool previousOptionsEnabled;
        private EnterPlayModeOptions previousOptions;

        [UnityTest]
        [Category("UMASampleIntegration")]
        public IEnumerator LimitedRandomCrowdReplaysItsPoolAndSharesMeshes()
        {
            const string path = "Assets/UMA/SRP/Samples/Scenes/U3-Generating Random Characters.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) Assert.Ignore("Random crowd sample is not installed.");
            previousScenes = EditorSceneManager.GetSceneManagerSetup();
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            var installedSettings = AssetDatabase.LoadAssetAtPath<UMASettings>("Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset");
            Assert.That(installedSettings, Is.Not.Null);
            Debug.Log("Validating limited crowd: " + installedSettings.UMAVersion + "; Unity " + Application.unityVersion + "; " + Application.dataPath);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var monitor = UnityEngine.Object.FindAnyObjectByType<UMAResourceUsageMonitor>();
            var crowd = UnityEngine.Object.FindAnyObjectByType<UMARandomAvatar>();
            bool fullCrowd = Environment.GetEnvironmentVariable("UMA_REUSE_PERFORMANCE_FULL_CROWD") == "1";
            int size = fullCrowd ? 9 : 3;
            int setups = fullCrowd ? 8 : 2;
            monitor.InitialReuse = UMAResourceUsageMonitor.ReusePolicy.Off;
            crowd.GenerateGrid = true;
            crowd.GridXSize = crowd.GridZSize = size;
            crowd.MaximumUniqueCharacters = setups;
            monitor.LogCompletedRuns = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            yield return WaitFor(() => monitor.LastOff != null, monitor);
            var definitions = Definitions();
            Assert.That(definitions.Length, Is.EqualTo(size * size));
            Assert.That(definitions.Distinct().Count(), Is.EqualTo(setups));
            Assert.That(crowd.UniqueCharacterSetupCount, Is.EqualTo(setups));
            Debug.Log("Pooled crowd OFF:\n" + monitor.GetReport());
            monitor.RestartCrowd(true);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOn != null, monitor);
            CollectionAssert.AreEqual(definitions, Definitions(), "OFF/ON must rebuild the same pool and choose the same instances.");
            Assert.That(monitor.LastOn.CompletedAvatars, Is.EqualTo(size * size));
            Assert.That(monitor.LastOn.MeshCache.Hits + monitor.LastOn.MeshCache.PendingJoins, Is.GreaterThan(0), monitor.GetReport());
            Assert.That(monitor.LastOn.Memory.UniqueMeshes, Is.LessThan(monitor.LastOff.Memory.UniqueMeshes));
            Assert.That(monitor.LastOn.TextureCache.Bypasses, Is.Zero, monitor.GetReport());
            Assert.That(monitor.LastOn.TextureCache.Hits + monitor.LastOn.TextureCache.PendingJoins,
                Is.GreaterThan(0), "Empty AtlasUpdated events must not prevent pooled characters sharing textures.\n" + monitor.GetReport());
            Assert.That(monitor.LastOn.Memory.UniqueTextures, Is.LessThan(monitor.LastOff.Memory.UniqueTextures));
            Assert.That(monitor.LastOn.AtlasEarlyHits, Is.GreaterThan(0));
            Debug.Log($"Limited random crowd verified ({size * size} avatars, {setups} setups):\n" + monitor.GetReport());
            var previousOff = monitor.LastOff;
            monitor.RestartCrowd(false);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOff != previousOff, monitor);
            CollectionAssert.AreEqual(definitions, Definitions());
            Assert.That(monitor.LastOff.CacheEntriesAtStart, Is.Zero, "The setup pool must not retain generated resources.");
            Debug.Log("Pooled crowd OFF again:\n" + monitor.GetReport());
        }

        [UnityTest]
        [Category("UMASampleIntegration")]
        public IEnumerator RandomCrowdSampleReplaysTheSameAvatarsWithReuseOffOnAndOff()
        {
            const string path = "Assets/UMA/SRP/Samples/Scenes/U3-Generating Random Characters.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                Assert.Ignore("The random crowd sample is not installed in this validation project.");
            previousScenes = EditorSceneManager.GetSceneManagerSetup();
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            var installedSettings = AssetDatabase.LoadAssetAtPath<UMASettings>("Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset");
            Assert.That(installedSettings, Is.Not.Null, "Validate against the current installed UMA settings, not a fallback-only test copy.");
            Assert.That(UMASettings.GetOrCreateSettings().UMAVersion, Is.EqualTo(installedSettings.UMAVersion));
            Debug.Log("Validating current UMA source: " + installedSettings.UMAVersion + "; Unity " + Application.unityVersion + "; project " + Application.dataPath);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var monitor = UnityEngine.Object.FindAnyObjectByType<UMAResourceUsageMonitor>();
            var crowd = UnityEngine.Object.FindAnyObjectByType<UMARandomAvatar>();
            Assert.That(monitor, Is.Not.Null, "The shipped scene must contain the monitor.");
            Assert.That(monitor.gameObject.name, Is.EqualTo("GeneratorParms"));
            Assert.That(monitor.InitialReuse, Is.EqualTo(UMAResourceUsageMonitor.ReusePolicy.Off));
            Assert.That(crowd, Is.Not.Null);
            // Reduce only the loaded test scene; never save these test overrides to disk.
            crowd.GenerateGrid = true;
            int gridSize = Environment.GetEnvironmentVariable("UMA_REUSE_PERFORMANCE_FULL_CROWD") == "1" ? 9 : 2;
            int avatarCount = gridSize * gridSize;
            crowd.GridXSize = crowd.GridZSize = gridSize;
            monitor.LogCompletedRuns = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            yield return WaitFor(() => monitor.LastOff != null, monitor);
            Assert.That(monitor.LastOff.SpawnedAvatars, Is.EqualTo(avatarCount));
            Assert.That(monitor.LastOff.CompletedAvatars, Is.EqualTo(avatarCount), monitor.Status);
            Assert.That(monitor.LastOff.Memory.UniqueMeshes, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.Memory.UniqueTextures, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.MeshMilliseconds, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.TextureMilliseconds, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.MeshCache.Requests, Is.Zero);
            Assert.That(monitor.LastOff.TextureCache.Requests, Is.Zero);
            var originalAvatars = Avatars();
            var originalDefinitions = Definitions();
            monitor.RestartCrowd(true);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOn != null, monitor);
            Assert.That(originalAvatars.All(a => a == null), Is.True, "Old avatars must be destroyed before the new run.");
            CollectionAssert.AreEqual(originalDefinitions, Definitions(), "OFF and ON must build the same random appearances.");
            Assert.That(Avatars().All(a => a.reuseGeneratedMeshes && a.reuseGeneratedTextures), Is.True);
            Assert.That(monitor.LastOn.MeshCache.Requests, Is.GreaterThan(0), monitor.GetReport() + "\n" + monitor.Generator.meshCombiner.GetType().Name);
            Assert.That(monitor.LastOn.TextureCache.Attempts, Is.GreaterThan(0), monitor.GetReport());
            if (monitor.LastOn.TextureCache.Bypasses > 0)
                Assert.That(monitor.LastOn.TextureCache.LastBypassReason, Is.Not.Null.And.Not.Empty);
            Assert.That(monitor.LastOn.Memory.Avatars, Is.EqualTo(avatarCount));
            Assert.That(monitor.LastOn.CountersReset, Is.False);
            var coldOn = monitor.LastOn;
            long lookupTicksBefore = LookupCounter("MeshLookupTicks");
            long lookupCountBefore = LookupCounter("MeshLookupCount");
            long detailedBefore = LookupCounter("MeshDetailedKeyCount");
            long rejectedBefore = LookupCounter("MeshFirstStageRejectCount");
            long admittedBefore = LookupCounter("MeshAdmissionCount");
            monitor.RestartCrowd(true);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOn != coldOn, monitor);
            CollectionAssert.AreEqual(originalDefinitions, Definitions());
            Assert.That(monitor.LastOn.CacheEntriesAtStart, Is.Zero);
            long warmLookups = LookupCounter("MeshLookupCount") - lookupCountBefore;
            double warmLookupMs = UMATime.StopwatchTicksToMilliseconds(LookupCounter("MeshLookupTicks") - lookupTicksBefore);
            long detailedKeys = LookupCounter("MeshDetailedKeyCount") - detailedBefore;
            long firstStageRejects = LookupCounter("MeshFirstStageRejectCount") - rejectedBefore;
            long admissions = LookupCounter("MeshAdmissionCount") - admittedBefore;
            Assert.That(warmLookups, Is.GreaterThan(0));
            var firstOff = monitor.LastOff;
            monitor.RestartCrowd(false);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOff != firstOff, monitor);
            CollectionAssert.AreEqual(originalDefinitions, Definitions());
            Assert.That(Avatars().All(a => !a.reuseGeneratedMeshes && !a.reuseGeneratedTextures), Is.True);
            Assert.That(monitor.LastOff.CacheEntriesAtStart, Is.Zero, "No cache entries should survive the previous crowd.");
            Assert.That(monitor.LastOff.Memory.Avatars, Is.EqualTo(avatarCount));
            Assert.That(monitor.LastOff.CountersReset, Is.False);
            Debug.Log("Resource monitor sample OFF/ON cold/ON warm/OFF verified. Last ON report:\n" + JsonUtility.ToJson(monitor.LastOn, true));
            Debug.Log($"Warm crowd mesh lookup: {warmLookupMs / warmLookups:F4} ms/request; {warmLookupMs / avatarCount:F4} ms/avatar including publication recheck ({warmLookups} lookups, {warmLookupMs:F2} ms total).");
            Debug.Log($"First-stage rejects: {firstStageRejects}/{admissions}; detailed keys created: {detailedKeys}. Timing includes dictionary lookup and candidate verification.");
            Debug.Log($"Reuse performance ({avatarCount} avatars): OFF cold/warm mesh {firstOff.MeshMilliseconds:F2}/{monitor.LastOff.MeshMilliseconds:F2} ms; " +
                $"ON cold/warm mesh {coldOn.MeshMilliseconds:F2}/{monitor.LastOn.MeshMilliseconds:F2} ms; OFF cold/warm ready {firstOff.WallMilliseconds:F2}/{monitor.LastOff.WallMilliseconds:F2} ms; " +
                $"ON cold/warm ready {coldOn.WallMilliseconds:F2}/{monitor.LastOn.WallMilliseconds:F2} ms.");
            Assert.That(warmLookupMs / avatarCount, Is.LessThan(.5), "All warm lookups per avatar must average below 0.5 ms on the actual sample inputs.");
            Assert.That(monitor.LastOn.MeshMilliseconds,
                Is.LessThan(Math.Max(firstOff.MeshMilliseconds, monitor.LastOff.MeshMilliseconds) * 3 + avatarCount * 10),
                "Reuse misses must not introduce per-avatar serialization stalls.");
        }

        private static long LookupCounter(string name) => (long)typeof(UMAResourceReuse)
            .GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);

        [UnityTearDown]
        public IEnumerator RestoreScene()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            if (previousScenes == null) yield break;
            EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
            EditorSettings.enterPlayModeOptions = previousOptions;
            if (previousScenes.Any(scene => scene.isLoaded && scene.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            previousScenes = null;
        }

        private static DynamicCharacterAvatar[] Avatars() =>
            UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(a => a.name).ToArray();
        private static string[] Definitions() => Avatars().Select(a => a.GetAvatarDefinitionString(false, false)).ToArray();

        private static IEnumerator WaitFor(Func<bool> condition, UMAResourceUsageMonitor monitor)
        {
            double deadline = EditorApplication.timeSinceStartup + 120;
            while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, monitor.Status + "\n" + monitor.GetReport());
        }
    }
}
#endif
