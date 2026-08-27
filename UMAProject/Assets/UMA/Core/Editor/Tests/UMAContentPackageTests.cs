using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UMA.Editors.PackageSupport;

namespace UMA.Editors.Tests
{
    [TestFixture]
    [Category("UMA")]
    [Category("Content Packages")]
    public sealed class UMAContentPackageTests
    {
        [Test]
        public void ContentTreesAreProjectOwnedAndPackageReady()
        {
            Assert.That(UMAContentCatalog.Root(UMAContentKind.Uma3),
                Is.EqualTo("Assets/UMA/UMA3"));
            Assert.That(UMAContentCatalog.Root(UMAContentKind.Uma2),
                Is.EqualTo("Assets/UMA/UMA2"));

            bool hasUma3 = Directory.Exists(Absolute(UMAPathUtility.Uma3ContentRoot));
            bool hasUma2 = Directory.Exists(Absolute(UMAPathUtility.Uma2ContentRoot));
            if (hasUma2)
            {
                Assert.That(hasUma3, Is.True,
                    "UMA2 content cannot be installed without UMA3 content.");
                Assert.That(File.Exists(Absolute(
                    UMAPathUtility.Uma2ContentRoot + "/UMA2.Content.asmdef")), Is.True);
                Assert.That(File.Exists(Absolute(
                    UMAPathUtility.Uma2ContentRoot + "/package.json")), Is.False,
                    "UMA2 is editable project content, not a nested UPM package.");
            }

            if (Directory.Exists(Absolute("Assets/UMA")))
            {
                string[] nestedPackages = Directory.GetFiles(
                        Absolute("Assets/UMA"), "*.unitypackage",
                        SearchOption.AllDirectories)
                    .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "SRP" +
                        Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                    .ToArray();
                Assert.That(nestedPackages, Is.Empty,
                    "Editable content must not contain nested unitypackage installers.");
            }
        }

        [Test]
        public void CorePackageExcludesEditableContent()
        {
            string npmIgnore = File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(
                UMAPathUtility.ResolveInstallAssetPath(".npmignore")));
            Assert.That(npmIgnore, Does.Match(@"(?m)^UMA3/$"));
            Assert.That(npmIgnore, Does.Match(@"(?m)^UMA3\.meta$"));
            Assert.That(npmIgnore, Does.Match(@"(?m)^UMA2/$"));
            Assert.That(npmIgnore, Does.Match(@"(?m)^UMA2\.meta$"));
            Assert.That(npmIgnore, Does.Match(@"(?m)^Settings/$"));
            Assert.That(npmIgnore, Does.Match(@"(?m)^Settings\.meta$"));
            Assert.That(npmIgnore, Does.Match(@"(?m)^SRP/\*$"));
            if (!UMAPathUtility.IsPackageInstallation)
                Assert.That(File.Exists(Absolute("Build/Build-UMAContentPackages.ps1")),
                    Is.True);
        }

        [Test]
        public void ContentManifestRequiresExactDependencyAndVersionContracts()
        {
            const string folderPath = "Assets/UMA/UMA3/Races";
            const string assetPath = "Assets/UMA/UMA3/Races/Human.asset";
            var manifest = new UMAContentManifest
            {
                formatVersion = UMAContentCatalog.CurrentManifestFormatVersion,
                contentId = "uma3",
                contentVersion = "3.0.4",
                requiredCoreVersion = "3.0.4",
                minimumCoreVersion = "3.0.4",
                maximumCoreVersionExclusive = "3.1.0",
                installRoot = "Assets/UMA/UMA3",
                dependencies = new[] { "core", "srp" },
                requiredPaths = new[] { folderPath },
                ownedPaths = new[]
                {
                    folderPath,
                    assetPath,
                    "Assets/UMA/UMA3/UMAContentManifest.json"
                },
                assets = new[]
                {
                    new UMAContentManifestAsset
                    {
                        path = folderPath,
                        guid = new string('a', 32),
                        bytes = 0,
                        sha256 = string.Empty,
                        metaBytes = 1,
                        metaSha256 = new string('b', 64)
                    },
                    new UMAContentManifestAsset
                    {
                        path = assetPath,
                        guid = new string('c', 32),
                        bytes = 1,
                        sha256 = new string('d', 64),
                        metaBytes = 1,
                        metaSha256 = new string('e', 64)
                    }
                }
            };

            Assert.That(UMAContentPackageArchiveValidator.TryValidateManifestStructure(
                manifest, UMAContentKind.Uma3, out string validError), Is.True,
                validError);

            manifest.dependencies = new[] { "core" };
            Assert.That(UMAContentPackageArchiveValidator.TryValidateManifestStructure(
                manifest, UMAContentKind.Uma3, out string dependencyError), Is.False);
            Assert.That(dependencyError, Does.Contain("dependencies"));

            manifest.dependencies = new[] { "core", "srp" };
            manifest.ownedPaths = new[]
            {
                folderPath,
                "Assets/UMA/UMA3/UMAContentManifest.json"
            };
            manifest.assets = new[] { manifest.assets[0] };
            Assert.That(UMAContentPackageArchiveValidator.TryValidateManifestStructure(
                manifest, UMAContentKind.Uma3, out string emptyFolderError), Is.False);
            Assert.That(emptyFolderError, Does.Contain("empty leaf folder"));

            manifest.ownedPaths = new[]
            {
                folderPath,
                assetPath,
                "Assets/UMA/UMA3/UMAContentManifest.json"
            };
            manifest.assets = new[]
            {
                new UMAContentManifestAsset
                {
                    path = folderPath,
                    guid = new string('a', 32),
                    bytes = 0,
                    sha256 = string.Empty,
                    metaBytes = 1,
                    metaSha256 = new string('b', 64)
                },
                new UMAContentManifestAsset
                {
                    path = assetPath,
                    guid = new string('c', 32),
                    bytes = 1,
                    sha256 = new string('d', 64),
                    metaBytes = 1,
                    metaSha256 = new string('e', 64)
                }
            };
            manifest.requiredPaths = Array.Empty<string>();
            Assert.That(UMAContentPackageArchiveValidator.TryValidateManifestStructure(
                manifest, UMAContentKind.Uma3, out string requiredError), Is.False);
            Assert.That(requiredError, Does.Contain("no required paths"));

            manifest.requiredPaths = new[] { folderPath };
            manifest.ownedPaths[1] = "Assets/UMA/UMA3/Races\\Human.asset";
            manifest.assets[1].path = manifest.ownedPaths[1];
            Assert.That(UMAContentPackageArchiveValidator.TryValidateManifestStructure(
                manifest, UMAContentKind.Uma3, out string unsafePathError), Is.False);
            Assert.That(unsafePathError, Does.Contain("invalid"));
        }

        [Test]
        public void GeneratedContentArchivesPassTheEditorValidator()
        {
            string releaseDirectory = Environment.GetEnvironmentVariable(
                "UMA_CONTENT_RELEASE_DIRECTORY");
            if (string.IsNullOrEmpty(releaseDirectory))
                releaseDirectory = Absolute("Build/Content");
            if (!Directory.Exists(releaseDirectory))
                Assert.Ignore("Run Build/Build-UMAContentPackages.ps1 to create release artifacts.");

            string uma3Archive = FindSingleArchive(releaseDirectory, "UMA3Content-*.unitypackage");
            string uma2Archive = FindSingleArchive(releaseDirectory, "UMA2Content-*.unitypackage");

            Assert.That(UMAContentPackageArchiveValidator.TryValidate(uma3Archive,
                UMAContentKind.Uma3, out UMAContentPackageArchiveInfo uma3,
                out string uma3Error), Is.True, uma3Error);
            Assert.That(UMAContentPackageArchiveValidator.TryValidate(uma2Archive,
                UMAContentKind.Uma2, out UMAContentPackageArchiveInfo uma2,
                out string uma2Error), Is.True, uma2Error);
            Assert.That(uma3.Manifest.dependencies ?? Array.Empty<string>(),
                Does.Not.Contain("uma2"));
            Assert.That(uma2.Manifest.dependencies ?? Array.Empty<string>(),
                Does.Contain("uma3"));
            Assert.That(uma2.Manifest.dependencies ?? Array.Empty<string>(),
                Does.Contain("srp"));
            Assert.That(uma2.Manifest.requiredCoreVersion,
                Is.EqualTo(uma3.Manifest.requiredCoreVersion));
            Assert.That(uma3.Manifest.formatVersion,
                Is.EqualTo(UMAContentCatalog.CurrentManifestFormatVersion));
            Assert.That(uma3.Manifest.minimumCoreVersion,
                Is.EqualTo(uma2.Manifest.minimumCoreVersion));
            Assert.That(uma3.Manifest.maximumCoreVersionExclusive,
                Is.EqualTo(uma2.Manifest.maximumCoreVersionExclusive));
            if (AssetDatabase.IsValidFolder(UMAPathUtility.Uma3ContentRoot) &&
                AssetDatabase.IsValidFolder(UMAPathUtility.Uma2ContentRoot))
            {
                AssertArchiveReferencesResolve(uma3.Archive, "UMA3");
                AssertArchiveReferencesResolve(uma2.Archive, "UMA2");
            }
        }

        [Test]
        public void GeneratedCoreStagingHasNoRawContent()
        {
            string stagingRoot = Environment.GetEnvironmentVariable(
                "UMA_CORE_STAGING_DIRECTORY");
            if (string.IsNullOrEmpty(stagingRoot))
                stagingRoot = Absolute("Build/CorePackage");
            if (!Directory.Exists(stagingRoot))
                Assert.Ignore("Run the content-package builder with Core staging enabled.");

            Assert.That(Directory.Exists(Path.Combine(stagingRoot, "UMA3")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(stagingRoot, "UMA2")), Is.False);
            Assert.That(File.Exists(Path.Combine(stagingRoot, "UMA3.meta")), Is.False);
            Assert.That(File.Exists(Path.Combine(stagingRoot, "UMA2.meta")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(stagingRoot, "Settings")), Is.False);
            Assert.That(File.Exists(Path.Combine(stagingRoot, "Settings.meta")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(stagingRoot, "Temp")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(stagingRoot, "Tasks")), Is.False);
            Assert.That(File.Exists(Path.Combine(stagingRoot, "SRP",
                "UMAURPManifest.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(stagingRoot, "package.json")), Is.True);
        }

        private static string FindSingleArchive(string directory, string pattern)
        {
            string[] matches = Directory.GetFiles(directory, pattern,
                SearchOption.TopDirectoryOnly);
            Assert.That(matches, Has.Length.EqualTo(1),
                "Expected exactly one release archive matching " + pattern + ".");
            return matches[0];
        }

        private static void AssertArchiveReferencesResolve(
            UMASrpPackageArchiveInfo archive, string label)
        {
            var packagedGuids = new HashSet<string>(archive.GuidByPath.Values,
                StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            foreach (KeyValuePair<string, IReadOnlyCollection<string>> pair in
                     archive.ReferencedGuidsByPath)
            {
                foreach (string guid in pair.Value)
                {
                    if (packagedGuids.Contains(guid) ||
                        guid.StartsWith("0000000000000000",
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                        continue;
                    failures.Add(pair.Key + " -> " + guid);
                }
            }
            Assert.That(failures, Is.Empty,
                label + " contains unresolved serialized GUID references:\n" +
                string.Join("\n", failures));
        }

        private static string Absolute(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
