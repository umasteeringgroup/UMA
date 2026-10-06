#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UMA.Editors.PackageSupport;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginPackageBuilderTests
    {
        private string directory;
        private string archive;

        [OneTimeSetUp]
        public void BuildInstalledHairPlugin()
        {
            if (!Directory.Exists(UMAContentCatalog.Root(UMAContentKind.HairCards)))
                Assert.Ignore("Hair Cards source is optional and is not installed.");
            directory = Path.GetFullPath("Library/UMA/PluginBuildTests/" + Guid.NewGuid().ToString("N"));
            archive = UMAPluginPackageBuilder.Build(UMAContentKind.HairCards, directory);
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            string root = Path.GetFullPath("Library/UMA/PluginBuildTests") + Path.DirectorySeparatorChar;
            if (directory != null && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void BuiltArchiveValidatesEveryGuidAndAssetHashWithoutOwningUserData()
        {
            Assert.That(UMAContentPackageArchiveValidator.TryValidate(archive, UMAContentKind.HairCards, out var info, out string error), Is.True, error);
            Assert.That(info.Manifest.dependencies, Is.EqualTo(new[] { "core" }));
            Assert.That(info.Manifest.ownedPaths, Has.None.EqualTo("Assets/UMA/HairCards"));
            Assert.That(info.Manifest.ownedPaths.All(p => p.StartsWith("Assets/UMA/HairCards/", StringComparison.Ordinal)), Is.True);
            Assert.That(info.Manifest.assets.Any(a => a.path.EndsWith("Documentation/Hair Cards - Quick Start.md", StringComparison.Ordinal)), Is.True);
            Assert.That(info.Manifest.requiredPluginApiVersion, Is.EqualTo(UMAPluginApi.Version));
        }

        [Test]
        public void DiscoveryFindsValidatedPackageAndIgnoresInvalidHigherVersion()
        {
            File.WriteAllText(Path.Combine(directory, "HairCards-999.0.0.unitypackage"), "Not a package");
            Assert.That(UMAPluginPackageFiles.FindPackage(UMAContentKind.HairCards, new[] { directory }), Is.EqualTo(archive));
        }

        [Test]
        public void DiscoveryReturnsNoPackageForMissingDirectoryOrWrongPlugin()
        {
            Assert.That(UMAPluginPackageFiles.FindPackage(UMAContentKind.HairCards, new[] { directory + "-missing" }), Is.Null);
            Assert.That(UMAPluginPackageFiles.FindPackage(UMAContentKind.OverlayPainter, new[] { directory }, archive), Is.Null);
        }

        [Test]
        public void RebuildReplacesArchiveWithAnotherValidPackage()
        {
            byte[] previous = File.ReadAllBytes(archive);
            Assert.That(UMAPluginPackageBuilder.Build(UMAContentKind.HairCards, directory), Is.EqualTo(archive));
            Assert.That(File.ReadAllBytes(archive), Is.EqualTo(previous), "Identical sources should yield reproducible archives.");
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void BuildingInsideAssetsIsRejectedBeforeWriting()
        {
            Assert.Throws<ArgumentException>(() => UMAPluginPackageBuilder.Build(UMAContentKind.HairCards, "Assets/PluginBuildShouldNotExist"));
            Assert.That(Directory.Exists("Assets/PluginBuildShouldNotExist"), Is.False);
        }
    }
}
#endif
