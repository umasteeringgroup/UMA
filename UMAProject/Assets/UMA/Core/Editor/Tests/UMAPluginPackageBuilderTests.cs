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
            Assert.That(info.Manifest.contentVersion, Is.EqualTo(UMAPackageVersionUtility.Normalize(UMASettings.GetSettings().UMAVersion, out var umaVersion)));
            Assert.That(info.Manifest.umaVersion, Is.EqualTo(umaVersion));
            Assert.That(info.Manifest.ownedPaths.Any(p => UMAContentCatalog.IsCompanionPath(UMAContentKind.HairCards, p)), Is.False);
        }

        [Test]
        public void PluginRowsGroupSeparateCompanionsImmediatelyUnderEveryParent()
        {
            var order = UMAContentCatalog.PluginDisplayOrder.ToArray();
            foreach (var parent in UMAContentCatalog.Plugins.Where(p => !UMAContentCatalog.ParentPlugin(p).HasValue))
            {
                var companions = UMAContentCatalog.Companions(parent).ToArray();
                Assert.That(companions.Length, Is.EqualTo(2), UMAContentCatalog.DisplayName(parent));
                CollectionAssert.AreEqual(companions, order.Skip(Array.IndexOf(order, parent) + 1).Take(2));
                foreach (var companion in companions)
                    Assert.That(UMAContentCatalog.Dependencies(companion), Does.Contain(UMAContentCatalog.Id(parent)));
            }
        }

        [TestCase("UMA NextGen 3.1f2", "3.1.2", "3.1f2")]
        [TestCase("UMA 3.1f12", "3.1.12", "3.1f12")]
        [TestCase("3.1.2", "3.1.2", "3.1.2")]
        [TestCase("3.2b3", "3.2.0-beta.3", "3.2b3")]
        [TestCase("3.2a1", "3.2.0-alpha.1", "3.2a1")]
        public void PackageVersionPreservesUMAReleaseAndProducesSemanticVersion(string input, string expected, string display)
        {
            Assert.That(UMAPackageVersionUtility.Normalize(input, out var release), Is.EqualTo(expected));
            Assert.That(release, Is.EqualTo(display));
        }

        [TestCase("")]
        [TestCase("UMA NextGen")]
        [TestCase("3.1f")]
        public void PackageVersionRejectsMissingOrIncompleteRelease(string input)
        {
            Assert.Throws<InvalidDataException>(() => UMAPackageVersionUtility.Normalize(input, out _));
        }

        [TestCase(UMAContentKind.HairCardsExamples)]
        [TestCase(UMAContentKind.HairCardsTests)]
        [TestCase(UMAContentKind.Dismemberment)]
        [TestCase(UMAContentKind.DismembermentExamples)]
        [TestCase(UMAContentKind.DismembermentTests)]
        public void NewPackagesValidateAndHaveDisjointOwnership(UMAContentKind kind)
        {
            if (!UMAContentCatalog.PluginRequiredPaths(kind).All(File.Exists)) Assert.Ignore("Optional sources not installed.");
            string package = UMAPluginPackageBuilder.Build(kind, directory);
            Assert.That(UMAContentPackageArchiveValidator.TryValidate(package, kind, out var info, out string error), Is.True, error);
            Assert.That(info.Manifest.ownedPaths.All(path => UMAContentCatalog.OwnsPluginPath(kind, path)), Is.True);
            if (UMAContentCatalog.ParentPlugin(kind) is UMAContentKind parent)
            {
                string parentPackage = parent == UMAContentKind.HairCards ? archive : UMAPluginPackageBuilder.Build(parent, directory);
                Assert.That(UMAContentPackageArchiveValidator.TryValidate(parentPackage, parent, out var parentInfo, out error), Is.True, error);
                Assert.That(info.Manifest.assets.Select(a => a.guid).Intersect(parentInfo.Manifest.assets.Select(a => a.guid)), Is.Empty);
            }
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
