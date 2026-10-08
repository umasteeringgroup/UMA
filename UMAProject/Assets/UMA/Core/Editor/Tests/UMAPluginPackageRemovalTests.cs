#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginPackageRemovalTests
    {
        private static readonly Type BatchType = typeof(UMAPluginPackageRemoval).GetNestedType("RemovalBatch", BindingFlags.NonPublic);
        private static readonly Type RemovalType = typeof(UMAPluginPackageRemoval).GetNestedType("Removal", BindingFlags.NonPublic);

        [TestCase(UMAContentKind.Uma2)]
        [TestCase(UMAContentKind.OverlayPainter)]
        [TestCase(UMAContentKind.HairCards)]
        [TestCase(UMAContentKind.Dismemberment)]
        public void RemoveAllOrdersInstalledCompanionsBeforeTheirParent(UMAContentKind parent)
        {
            if (!UMAContentPackageArchiveValidator.TryReadInstalledManifest(parent, out _, out _))
                Assert.Ignore("Optional plugin source has not been adopted.");
            object[] args = { parent, null, null };
            Assert.That((bool)Invoke("TryCreateBatch", args), Is.True, args[2] as string);
            var order = Entries(args[1]).Cast<object>().Select(Kind).ToArray();
            Assert.That(order.Last(), Is.EqualTo(parent));
            Assert.That(order.Distinct().Count(), Is.EqualTo(order.Length));
            foreach (var companion in UMAContentCatalog.Companions(parent))
                if (File.Exists(UMAContentCatalog.ManifestPath(companion)))
                    Assert.That(Array.IndexOf(order, companion), Is.InRange(0, order.Length - 2));
            Assert.That(UMAPluginPackageRemoval.CanRemoveAll(parent, out string error), Is.True, error);
        }

        [Test]
        public void InterruptedBatchResumesAfterCompletedCompanionsAndRetainsPreservedFiles()
        {
            var order = UMAContentCatalog.Companions(UMAContentKind.HairCards).Concat(new[] { UMAContentKind.HairCards }).ToArray();
            object batch = CreateBatch(UMAContentKind.HairCards, order);
            var first = Entries(batch)[0];
            ((List<string>)RemovalType.GetField("preserved").GetValue(first)).Add("Modified example retained");
            var removed = new List<UMAContentKind>();
            string checkpoint = null;
            Action<int> remove = index =>
            {
                if (index == 1) throw new IOException("Interrupted before removing Tests");
                removed.Add(Kind(Entries(batch)[index]));
            };
            Assert.Throws<IOException>(() => Invoke("ProcessBatch", batch, remove,
                (Action)(() => checkpoint = JsonUtility.ToJson(batch))));
            Assert.That(removed, Is.EqualTo(order.Take(1)));
            object resumed = JsonUtility.FromJson(checkpoint, BatchType);
            Assert.That(((List<string>)RemovalType.GetField("preserved").GetValue(Entries(resumed)[0])),
                Does.Contain("Modified example retained"));
            Invoke("ProcessBatch", resumed, (Action<int>)(index => removed.Add(Kind(Entries(resumed)[index]))), (Action)(() => { }));
            Assert.That(removed, Is.EqualTo(order), "Completed companions must not be removed again.");
            Assert.That(BatchType.GetField("next").GetValue(resumed), Is.EqualTo(order.Length));
        }

        [Test]
        public void BatchAcceptsAbsentCompanionsAndSingleCompanionRemoval()
        {
            foreach (var kind in new[] { UMAContentKind.HairCards, UMAContentKind.HairCardsTests })
            {
                object batch = CreateBatch(kind, kind);
                var removed = new List<int>();
                Invoke("ProcessBatch", batch, (Action<int>)removed.Add, (Action)(() => { }));
                Assert.That(removed, Is.EqualTo(new[] { 0 }));
            }
        }

        [Test]
        public void LegacySinglePackageJournalAndCompletedBatchRemainRecoverable()
        {
            object original = CreateBatch(UMAContentKind.HairCardsTests, UMAContentKind.HairCardsTests);
            object legacy = Invoke("ReadBatch", JsonUtility.ToJson(Entries(original)[0]));
            int removed = 0;
            Invoke("ProcessBatch", legacy, (Action<int>)(_ => removed++), (Action)(() => { }));
            Assert.That(removed, Is.EqualTo(1));
            object completed = Invoke("ReadBatch", JsonUtility.ToJson(legacy));
            Invoke("ProcessBatch", completed, (Action<int>)(_ => removed++), (Action)(() => Assert.Fail("Completed journal must not checkpoint again")));
            Assert.That(removed, Is.EqualTo(1));
        }

        [TestCase("parent-first")]
        [TestCase("unrelated-plugin")]
        [TestCase("duplicate-child")]
        [TestCase("invalid-cursor")]
        [TestCase("invalid-manifest")]
        public void InvalidBatchIsRejectedBeforeAnyPackageIsRemoved(string problem)
        {
            UMAContentKind[] order = problem switch
            {
                "parent-first" => new[] { UMAContentKind.HairCards, UMAContentKind.HairCardsTests },
                "unrelated-plugin" => new[] { UMAContentKind.OverlayPainterTests, UMAContentKind.HairCards },
                "duplicate-child" => new[] { UMAContentKind.HairCardsTests, UMAContentKind.HairCardsTests, UMAContentKind.HairCards },
                _ => new[] { UMAContentKind.HairCardsTests, UMAContentKind.HairCards }
            };
            object batch = CreateBatch(UMAContentKind.HairCards, order);
            if (problem == "invalid-cursor") BatchType.GetField("next").SetValue(batch, -1);
            if (problem == "invalid-manifest") RemovalType.GetField("manifest").SetValue(Entries(batch)[0], null);
            int calls = 0;
            Assert.Throws<InvalidDataException>(() => Invoke("ProcessBatch", batch, (Action<int>)(_ => calls++), (Action)(() => { })));
            Assert.That(calls, Is.Zero);
        }

        private static object CreateBatch(UMAContentKind parent, params UMAContentKind[] order)
        {
            object batch = Activator.CreateInstance(BatchType, true);
            BatchType.GetField("kind").SetValue(batch, parent);
            string version = UMAPackageVersionUtility.Normalize(UMASettings.GetSettings().UMAVersion, out _);
            var parsed = new Version(version.Split('-')[0].Split('+')[0]);
            foreach (var kind in order)
            {
                string path = UMAContentCatalog.Root(kind) + "/RemovalFixture.txt";
                string manifestPath = UMAContentCatalog.ManifestPath(kind);
                var manifest = new UMAContentManifest
                {
                    formatVersion = 2, requiredPluginApiVersion = UMAPluginApi.Version,
                    contentId = UMAContentCatalog.Id(kind), contentVersion = version,
                    installRoot = UMAContentCatalog.Root(kind), minimumCoreVersion = version,
                    maximumCoreVersionExclusive = parsed.Major + "." + (parsed.Minor + 1) + ".0",
                    dependencies = UMAContentCatalog.Dependencies(kind), requiredPaths = new[] { path },
                    ownedPaths = new[] { path, manifestPath },
                    assets = new[] { new UMAContentManifestAsset
                    {
                        path = path, guid = new string('a', 32), bytes = 1,
                        sha256 = new string('b', 64), metaBytes = 1, metaSha256 = new string('c', 64)
                    } }
                };
                object removal = Activator.CreateInstance(RemovalType, true);
                RemovalType.GetField("kind").SetValue(removal, kind);
                RemovalType.GetField("manifest").SetValue(removal, manifest);
                Entries(batch).Add(removal);
            }
            return batch;
        }

        private static IList Entries(object batch) => (IList)BatchType.GetField("removals").GetValue(batch);
        private static UMAContentKind Kind(object removal) => (UMAContentKind)RemovalType.GetField("kind").GetValue(removal);
        private static object Invoke(string method, params object[] args)
        {
            try { return typeof(UMAPluginPackageRemoval).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
    }
}
#endif
