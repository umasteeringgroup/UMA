#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UMA.Editors.PackageSupport;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginReinstallTests
    {
        private string root;
        private string asset;
        private UMAContentManifest baseline;

        [SetUp]
        public void SetUp()
        {
            root = "Library/UMA/ReinstallTests/" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root);
            asset = root + "/Content.txt";
            File.WriteAllText(asset, "original content");
            File.WriteAllText(asset + ".meta", "original importer");
            baseline = Manifest(Record(asset));
        }

        [TearDown]
        public void TearDown()
        {
            string parent = Path.GetFullPath("Library/UMA/ReinstallTests") + Path.DirectorySeparatorChar;
            if (root != null && Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void IdenticalReinstallIgnoresEmptyFolderTreesAndTheirMetadata()
        {
            Directory.CreateDirectory(root + "/Empty/Nested");
            File.WriteAllText(root + "/Empty.meta", "folder importer");
            File.WriteAllText(root + "/Empty/Nested.meta", "nested folder importer");
            Assert.That(Conflicts(baseline), Is.Empty);
            Assert.That(Directory.Exists(root + "/Empty/Nested"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RebuiltContentMatchingLocalEditsIsNotAConflict(bool importer)
        {
            File.WriteAllText(asset + (importer ? ".meta" : ""), "rebuilt content");
            Assert.That(Conflicts(Manifest(Record(asset))), Is.Empty);
            Assert.That(Conflicts(baseline), Has.Count.EqualTo(1), "The installed baseline must still detect local changes.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EditsAfterBuildingStillRequireConflictProtection(bool importer)
        {
            File.WriteAllText(asset, "built revision");
            var incoming = Manifest(Record(asset));
            File.WriteAllText(asset + (importer ? ".meta" : ""), "unsaved to package");
            Assert.That(Conflicts(incoming), Is.Not.Empty);
        }

        [Test]
        public void NewlyPackagedFileAlreadyPresentIsNotAConflict()
        {
            string added = root + "/Added.txt";
            File.WriteAllText(added, "new packaged content");
            File.WriteAllText(added + ".meta", "new importer");
            var incoming = Manifest(Record(asset), Record(added));
            Assert.That(Conflicts(incoming), Is.Empty);
            File.WriteAllText(added, "divergent local content");
            Assert.That(Conflicts(incoming), Is.Not.Empty);
        }

        [Test]
        public void UntrackedContentAndOrphanMetadataAreNotHiddenAsEmptyFolders()
        {
            Directory.CreateDirectory(root + "/UserFiles");
            File.WriteAllText(root + "/UserFiles/Custom.txt", "user content");
            File.WriteAllText(root + "/Orphan.meta", "orphan importer");
            var conflicts = Conflicts(baseline);
            Assert.That(conflicts, Does.Contain(root + "/UserFiles/Custom.txt (added/untracked)"));
            Assert.That(conflicts, Does.Contain(root + "/Orphan.meta (added/untracked)"));
        }

        [Test]
        public void DeletedManagedFilesRemainConflicts()
        {
            File.Delete(asset);
            Assert.That(Conflicts(baseline), Does.Contain(asset + " (deleted)"));
        }

        [Test]
        public void ModifiedManagedFolderMetadataRemainsAConflict()
        {
            Directory.CreateDirectory(root + "/Managed");
            File.WriteAllText(root + "/Managed.meta", "original folder");
            baseline = Manifest(Record(asset), Record(root + "/Managed"));
            File.WriteAllText(root + "/Managed.meta", "modified folder");
            Assert.That(Conflicts(baseline), Does.Contain(root + "/Managed.meta (modified or deleted)"));
        }

        [Test]
        public void ReinstallPreservesUnownedEmptyFoldersAndTheirMetadata()
        {
            string backup = Path.GetFullPath(root + "/Backup");
            string destination = Path.GetFullPath(root + "/Destination");
            Directory.CreateDirectory(backup + "/Empty/Nested");
            File.WriteAllText(backup + "/Empty.meta", "keep folder identity");
            File.WriteAllText(backup + "/Empty/Nested.meta", "keep nested identity");
            var manifest = new UMAContentManifest { ownedPaths = Array.Empty<string>() };
            typeof(UMAContentPackageInstaller).GetMethod("RestoreUnownedPluginFiles", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { UMAContentKind.Uma2, manifest, manifest, backup, destination });
            Assert.That(Directory.Exists(destination + "/Empty/Nested"), Is.True);
            Assert.That(File.ReadAllText(destination + "/Empty.meta"), Is.EqualTo("keep folder identity"));
            Assert.That(File.ReadAllText(destination + "/Empty/Nested.meta"), Is.EqualTo("keep nested identity"));
        }

        private List<string> Conflicts(UMAContentManifest incoming)
        {
            object result = typeof(UMAContentPackageInstaller).GetMethod("AnalyzeLocalFiles", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { root, root + "/UMAContentManifest.json", incoming, baseline, null });
            return (List<string>)result.GetType().GetField("conflicts").GetValue(result);
        }

        private static UMAContentManifest Manifest(params UMAContentManifestAsset[] assets)
            => new UMAContentManifest { assets = assets };

        private static UMAContentManifestAsset Record(string path) => new UMAContentManifestAsset
        {
            path = path, guid = "0123456789abcdef0123456789abcdef",
            bytes = File.Exists(path) ? new FileInfo(path).Length : 0,
            sha256 = File.Exists(path) ? Hash(path) : "",
            metaBytes = new FileInfo(path + ".meta").Length, metaSha256 = Hash(path + ".meta")
        };

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var input = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(input).Select(b => b.ToString("x2")));
        }
    }
}
#endif
