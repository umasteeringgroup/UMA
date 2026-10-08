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

        [Test]
        public void RemovalPolicySurvivesRecoveryAndOldJournalsDefaultToPreservingEdits()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsTests, UMAContentKind.HairCards);
            Assert.That(BatchType.GetField("removeModified").GetValue(Invoke("ReadBatch", JsonUtility.ToJson(batch))), Is.False);
            BatchType.GetField("removeModified").SetValue(batch, true);
            BatchType.GetField("next").SetValue(batch, 1);
            object resumed = Invoke("ReadBatch", JsonUtility.ToJson(batch));
            Assert.That(BatchType.GetField("removeModified").GetValue(resumed), Is.True);
            Assert.That(BatchType.GetField("next").GetValue(resumed), Is.EqualTo(1));
            object legacy = Invoke("ReadBatch", JsonUtility.ToJson(Entries(batch)[0]));
            Assert.That(BatchType.GetField("removeModified").GetValue(legacy), Is.False);
        }

        [TestCase(false, "unchanged")]
        [TestCase(true, "unchanged")]
        [TestCase(false, "modified")]
        [TestCase(true, "modified")]
        [TestCase(false, "importer")]
        [TestCase(true, "importer")]
        [TestCase(false, "missing-meta")]
        [TestCase(true, "missing-meta")]
        [TestCase(false, "orphan-meta")]
        [TestCase(true, "orphan-meta")]
        public void SelectedPolicyRemovesOwnedFilesAndAlwaysPreservesUnownedFiles(bool removeModified, string state)
        {
            string parent = Path.GetFullPath("Library/UMA/RemovalPolicyTests") + Path.DirectorySeparatorChar;
            string directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string owned = Path.Combine(directory, "Owned.txt");
                string unowned = Path.Combine(directory, "User.txt");
                File.WriteAllText(owned, "original");
                File.WriteAllText(owned + ".meta", "original importer");
                File.WriteAllText(unowned, "user content");
                object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCards);
                object removal = Entries(batch)[0];
                var manifest = (UMAContentManifest)RemovalType.GetField("manifest").GetValue(removal);
                var record = manifest.assets[0];
                record.path = owned;
                record.sha256 = (string)Invoke("Hash", owned);
                record.metaSha256 = (string)Invoke("Hash", owned + ".meta");
                if (state == "modified") File.WriteAllText(owned, "modified");
                if (state == "importer") File.WriteAllText(owned + ".meta", "modified importer");
                if (state == "missing-meta") File.Delete(owned + ".meta");
                if (state == "orphan-meta") File.Delete(owned);
                Func<string, bool> delete = path =>
                {
                    Assert.That(path, Is.EqualTo(owned), "Only the manifest-owned path may be deleted.");
                    File.Delete(path);
                    File.Delete(path + ".meta");
                    return true;
                };
                Invoke("RemoveOwnedFiles", removal, (Action)(() => { }), removeModified, delete);
                bool shouldRemove = removeModified || state == "unchanged";
                Assert.That(File.Exists(owned), Is.EqualTo(!shouldRemove && state != "orphan-meta"));
                Assert.That(File.Exists(owned + ".meta"), Is.EqualTo(!shouldRemove && state != "missing-meta"));
                Assert.That(File.ReadAllText(unowned), Is.EqualTo("user content"));
                Assert.That(((List<string>)RemovalType.GetField("preserved").GetValue(removal)).Count,
                    Is.EqualTo(shouldRemove ? 0 : 1));
                // Recovery can repeat a completed file operation safely.
                Invoke("RemoveOwnedFiles", removal, (Action)(() => { }), removeModified, delete);
            }
            finally
            {
                if (Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
            }
        }

        [TestCase("empty")]
        [TestCase("user-file")]
        [TestCase("orphan-meta")]
        [TestCase("companion")]
        [TestCase("delete-failed")]
        public void EmptyFolderCleanupPrunesParentsAndPreservesContentAndCompanions(string state)
        {
            string parent = Path.GetFullPath("Library/UMA/RemovalPolicyTests") + Path.DirectorySeparatorChar;
            string fixture = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            string root = (fixture + "/Plugin").Replace('\\', '/');
            string leaf = root + "/Races/HumanFemale/FBX/TPoses";
            string retained = root + "/Retained/Nested";
            Directory.CreateDirectory(leaf);
            Directory.CreateDirectory(retained);
            foreach (string directory in Directory.GetDirectories(fixture, "*", SearchOption.AllDirectories))
                File.WriteAllText(directory + ".meta", "folder metadata");
            File.WriteAllText(fixture + "/Sibling.txt", "outside package");
            string content = retained + (state == "orphan-meta" ? "/MissingAsset.txt.meta" : "/User.txt");
            if (state == "user-file" || state == "orphan-meta") File.WriteAllText(content, "retain me");
            var removed = new List<string>();
            Func<string, bool> protect = path => state == "companion" && path == root + "/Retained";
            Func<string, bool> delete = path =>
            {
                Assert.That(path == root || path.StartsWith(root + "/", StringComparison.Ordinal), Is.True);
                Assert.That(Directory.EnumerateFileSystemEntries(path), Is.Empty,
                    "A parent must only be deleted after all its children and their metadata are gone.");
                if (state == "delete-failed") return false;
                Directory.Delete(path); // Deliberately leave its .meta to exercise batched cleanup.
                removed.Add(path);
                return true;
            };
            try
            {
                if (state == "delete-failed")
                {
                    Assert.Throws<IOException>(() => Invoke("RemoveEmptyFolders", root, protect, delete));
                    Assert.That(Directory.Exists(root), Is.True);
                    Assert.That(removed, Is.Empty);
                    return;
                }
                Invoke("RemoveEmptyFolders", root, protect, delete);
                Assert.That(Directory.Exists(root + "/Races"), Is.False);
                Assert.That(File.Exists(root + "/Races.meta"), Is.False);
                Assert.That(Directory.Exists(root), Is.EqualTo(state != "empty"));
                Assert.That(File.Exists(root + ".meta"), Is.EqualTo(state != "empty"));
                if (state == "empty") Assert.That(removed.Last(), Is.EqualTo(root));
                if (state == "user-file" || state == "orphan-meta")
                    Assert.That(File.ReadAllText(content), Is.EqualTo("retain me"));
                if (state == "companion")
                {
                    Assert.That(Directory.Exists(retained), Is.True);
                    Assert.That(File.Exists(retained + ".meta"), Is.True);
                }
                int count = removed.Count;
                Invoke("RemoveEmptyFolders", root, protect, delete);
                Assert.That(removed.Count, Is.EqualTo(count), "Recovery must be idempotent.");
                Assert.That(File.ReadAllText(fixture + "/Sibling.txt"), Is.EqualTo("outside package"));
            }
            finally
            {
                if (Path.GetFullPath(fixture).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(fixture, true);
            }
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
