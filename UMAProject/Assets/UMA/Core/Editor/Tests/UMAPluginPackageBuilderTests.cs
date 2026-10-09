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
            var messages = new System.Collections.Generic.List<string>();
            var fractions = new System.Collections.Generic.List<float>();
            string found = UMAPluginPackageFiles.FindPackage(UMAContentKind.HairCards, new[] { directory },
                out var validated, progress: (message, fraction) => { messages.Add(message); fractions.Add(fraction); });
            Assert.That(found, Is.EqualTo(archive));
            Assert.That(validated, Is.Not.Null, "Installation should reuse this validated archive instead of unpacking it again.");
            Assert.That(validated.Manifest.contentId, Is.EqualTo(UMAContentCatalog.Id(UMAContentKind.HairCards)));
            Assert.That(messages.Any(m => m.StartsWith("Looking for local packages")), Is.True);
            Assert.That(messages.Any(m => m.StartsWith("Unpacking and hashing")), Is.True);
            Assert.That(messages.Any(m => m.StartsWith("Checking package assets and references")), Is.True);
            Assert.That(fractions.All(f => f >= 0f && f <= 1f), Is.True);
        }

        [Test]
        public void DiscoveryReturnsNoPackageForMissingDirectoryOrWrongPlugin()
        {
            Assert.That(UMAPluginPackageFiles.FindPackage(UMAContentKind.HairCards, new[] { directory + "-missing" }), Is.Null);
            Assert.That(UMAPluginPackageFiles.FindPackage(UMAContentKind.OverlayPainter, new[] { directory }, out var validated, archive), Is.Null);
            Assert.That(validated, Is.Null, "Rejected candidates must not be passed to the installer.");
        }

        [Test]
        public void RebuildReplacesArchiveWithAnotherValidPackage()
        {
            byte[] previous = File.ReadAllBytes(archive);
            Assert.That(UMAPluginPackageBuilder.Build(UMAContentKind.HairCards, directory), Is.EqualTo(archive));
            Assert.That(File.ReadAllBytes(archive), Is.EqualTo(previous), "Identical sources should yield reproducible archives.");
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PublishingReplacesExistingArchiveIncludingReadOnlyBuildOutput(bool readOnly)
        {
            string target = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".unitypackage");
            string staged = target + ".tmp";
            File.WriteAllText(target, "old package");
            File.Copy(archive, staged);
            if (readOnly) File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
            Publish(staged, target);
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(File.ReadAllBytes(archive)));
            Assert.That(File.Exists(staged), Is.False);
            Assert.That(Directory.GetFiles(directory, Path.GetFileName(target) + "*.previous"), Is.Empty);
        }

        [Test]
        public void LockedDestinationKeepsBothPackagesAndReportsTheirFullPaths()
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
                Assert.Ignore("Windows sharing-lock behavior.");
            string output = Path.Combine(directory, "locked");
            Directory.CreateDirectory(output);
            string target = Path.Combine(output, Path.GetFileName(archive));
            File.Copy(archive, target);
            byte[] original = File.ReadAllBytes(target);
            IOException failure;
            using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                failure = Assert.Throws<IOException>(() => UMAPluginPackageBuilder.Build(UMAContentKind.HairCards, output));
            Assert.That(failure.Message, Does.Contain(target));
            string staged = Directory.GetFiles(output, "*.tmp").Single();
            Assert.That(failure.Message, Does.Contain(staged));
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(original));
            Assert.That(UMAContentPackageArchiveValidator.TryValidate(staged, UMAContentKind.HairCards, out _, out string error), Is.True, error);
            Publish(staged, target);
            Assert.That(File.Exists(staged), Is.False, "The preserved archive can be published after releasing the lock.");
        }

        [Test]
        public void PublishingRetriesBriefDestinationLocks()
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
                Assert.Ignore("Windows sharing-lock behavior.");
            string target = Path.Combine(directory, "brief-lock.unitypackage");
            string staged = target + ".tmp";
            File.WriteAllText(target, "old package");
            File.Copy(archive, staged);
            using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var release = new System.Threading.Timer(_ => locked.Dispose(), null, 150, System.Threading.Timeout.Infinite))
                Publish(staged, target);
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(File.ReadAllBytes(archive)));
        }

        [Test]
        public void FailureMovingNewArchiveRestoresPreviousArchive()
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
                Assert.Ignore("Windows sharing-lock behavior.");
            string target = Path.Combine(directory, "rollback.unitypackage");
            string staged = target + ".tmp";
            File.WriteAllText(target, "previous package");
            File.Copy(archive, staged);
            using (var locked = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = Assert.Throws<IOException>(() => Publish(staged, target));
                Assert.That(failure.Message, Does.Contain("previous package was restored"));
            }
            Assert.That(File.ReadAllText(target), Is.EqualTo("previous package"));
            Assert.That(File.ReadAllBytes(staged), Is.EqualTo(File.ReadAllBytes(archive)));
            Assert.That(Directory.GetFiles(directory, "rollback*.previous"), Is.Empty);
            File.Delete(staged);
        }

        private static void Publish(string staged, string target)
        {
            var method = typeof(UMAPluginPackageBuilder).GetMethod("PublishValidatedArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            try { method.Invoke(null, new object[] { staged, target }); }
            catch (System.Reflection.TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        [Test]
        public void BatchBuildContinuesAfterFailureAndReportsEveryPackage()
        {
            var kinds = new[] { UMAContentKind.HairCards, UMAContentKind.HairCardsExamples, UMAContentKind.HairCardsTests };
            var attempted = new System.Collections.Generic.List<UMAContentKind>();
            string failedPath = Path.Combine(directory, "HairCardsExamples.unitypackage");
            var results = RunBatch(kinds, kind =>
            {
                attempted.Add(kind);
                if (kind == UMAContentKind.HairCardsExamples) throw new IOException("Could not replace: " + failedPath);
                return Path.Combine(directory, kind + ".unitypackage");
            }, _ => false);
            CollectionAssert.AreEqual(kinds, attempted);
            Assert.That(results[0].Package, Is.Not.Null);
            Assert.That(results[1].Error, Is.TypeOf<IOException>());
            Assert.That(results[2].Package, Is.Not.Null);
            string report = FormatReport(results);
            Assert.That(report, Does.Contain("Built: 2   Failed: 1"));
            Assert.That(report, Does.Contain("Built — Hair Card Editor"));
            Assert.That(report, Does.Contain("Failed — Hair Card Examples"));
            Assert.That(report, Does.Contain("Built — Hair Card Tests"));
            Assert.That(report, Does.Contain(failedPath));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BatchCancellationKeepsCompletedResultsAndListsRemainingPackages(bool cancelDuringBuild)
        {
            var kinds = new[] { UMAContentKind.HairCards, UMAContentKind.HairCardsExamples, UMAContentKind.HairCardsTests };
            int attempts = 0;
            var results = RunBatch(kinds, kind =>
            {
                attempts++;
                if (cancelDuringBuild && kind == kinds[1]) throw new OperationCanceledException();
                return Path.Combine(directory, kind + ".unitypackage");
            }, i => !cancelDuringBuild && i == 1);
            Assert.That(attempts, Is.EqualTo(cancelDuringBuild ? 2 : 1));
            Assert.That(results[0].Package, Is.Not.Null);
            Assert.That(results.Skip(1).All(r => r.Cancelled && r.Error == null), Is.True);
            Assert.That(FormatReport(results), Does.Contain("Built: 1   Failed: 0   Cancelled / not built: 2"));
            Assert.That(FormatReport(results), Does.Contain("Not built (cancelled) — Hair Card Tests"));
        }

        [Test]
        public void EmptyBatchReportsNoInstalledSources()
        {
            var results = RunBatch(Array.Empty<UMAContentKind>(), _ => throw new Exception("Must not build"),
                _ => throw new Exception("Must not show progress"));
            Assert.That(results, Is.Empty);
            Assert.That(FormatReport(results), Is.EqualTo("No installed plugin sources were found."));
        }

        private static UMAPluginPackageBuilder.PackageBuildResult[] RunBatch(UMAContentKind[] kinds,
            Func<UMAContentKind, string> build, Func<int, bool> cancel) =>
            (UMAPluginPackageBuilder.PackageBuildResult[])typeof(UMAPluginPackageBuilder)
                .GetMethod("BuildBatch", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { kinds, build, cancel });

        private static string FormatReport(UMAPluginPackageBuilder.PackageBuildResult[] results) =>
            (string)typeof(UMAPluginPackageBuilder)
                .GetMethod("FormatBuildReport", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { results });

        [Test]
        public void BuildingInsideAssetsIsRejectedBeforeWriting()
        {
            Assert.Throws<ArgumentException>(() => UMAPluginPackageBuilder.Build(UMAContentKind.HairCards, "Assets/PluginBuildShouldNotExist"));
            Assert.That(Directory.Exists("Assets/PluginBuildShouldNotExist"), Is.False);
        }
    }
}
#endif
