#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public sealed class UMAResourceUsageMonitorRuntimeTests
    {
        [UnityTest]
        public IEnumerator SpawnedAvatarsReceivePolicyBeforeBuildAndDisablingUnhooksTheListener()
        {
            var monitorObject = new GameObject("Usage monitor test");
            var crowdObject = new GameObject("Usage monitor crowd");
            var avatarObject = new GameObject("Usage monitor avatar");
            monitorObject.SetActive(false);
            avatarObject.SetActive(false);
            var crowd = crowdObject.AddComponent<UMARandomAvatar>();
            crowd.enabled = false;
            crowd.RandomAvatarGenerated = new UMARandomAvatarEvent();
            var generator = monitorObject.AddComponent<UMAGenerator>();
            generator.enabled = false;
            var merge = ScriptableObject.CreateInstance<TextureMerge>();
            generator.textureMerge = merge;
            var monitor = monitorObject.AddComponent<UMAResourceUsageMonitor>();
            monitor.Generator = generator;
            monitor.LegacyCrowdGenerator = crowd;
            monitor.InitialReuse = UMAResourceUsageMonitor.ReusePolicy.On;
            monitor.LogCompletedRuns = false;
            var data = avatarObject.AddComponent<UMAData>();
            try
            {
                monitorObject.SetActive(true);
                crowd.RandomAvatarGenerated.Invoke(crowdObject, avatarObject);
                Assert.That(data.reuseGeneratedMeshes, Is.True);
                Assert.That(data.reuseGeneratedTextures, Is.True);
                // Asset loading/startup may not enqueue a build in the first two idle frames.
                yield return null;
                yield return null;
                yield return null;
                Assert.That(monitor.Current.Completed, Is.False);
                Assert.That(monitor.LastOn, Is.Null);
                data.CharacterUpdated.Invoke(data);
                yield return null;
                yield return null;
                yield return null;
                Assert.That(monitor.Current.Completed, Is.True);
                Assert.That(monitor.LastOn, Is.Not.Null);
                Assert.That(monitor.LastOn.CompletedAvatars, Is.EqualTo(1));
                var report = JsonUtility.FromJson<UMAResourceUsageMonitor.Report>(monitor.GetReport());
                Assert.That(report.LastOn.CompletedAvatars, Is.EqualTo(1));
                Assert.That(report.Current.GeneratorTimings, Is.Not.Null);
                double frozenWall = monitor.Current.WallMilliseconds;
                yield return null;
                monitor.RefreshCounters();
                Assert.That(monitor.Current.WallMilliseconds, Is.EqualTo(frozenWall));
                monitor.enabled = false;
                data.reuseGeneratedMeshes = data.reuseGeneratedTextures = false;
                crowd.RandomAvatarGenerated.Invoke(crowdObject, avatarObject);
                Assert.That(data.reuseGeneratedMeshes, Is.False);
                Assert.That(data.reuseGeneratedTextures, Is.False);
            }
            finally
            {
                Object.Destroy(monitorObject);
                Object.Destroy(crowdObject);
                Object.Destroy(avatarObject);
                Object.Destroy(merge);
            }
            yield return null;
        }
    }
}
#endif
