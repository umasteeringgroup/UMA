using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEditor;

namespace UMA.Editors.Tests
{
    public sealed class UMA2CompatibilityTests
    {
        private string directory;
        private UMAContentManifest manifest;

        [OneTimeSetUp]
        public void BuildCompatibilityArchive()
        {
            directory = Path.GetFullPath("Library/UMA/CompatibilityBuildTests/" + Guid.NewGuid().ToString("N"));
            string archive = UMAPluginPackageBuilder.Build(UMAContentKind.Uma2, directory);
            Assert.That(UMAContentPackageArchiveValidator.TryValidate(archive, UMAContentKind.Uma2,
                out var info, out string error), Is.True, error);
            manifest = info.Manifest;
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            string root = Path.GetFullPath("Library/UMA/CompatibilityBuildTests") + Path.DirectorySeparatorChar;
            if (directory != null && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void MainLegacyContentRetainsEveryPayloadAndGuid()
        {
            string root = UMAContentCatalog.Root(UMAContentKind.Uma2);
            string[] source = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                    p != UMAContentCatalog.ManifestPath(UMAContentKind.Uma2)).ToArray();
            CollectionAssert.AreEquivalent(source, manifest.assets.Where(a => a.bytes > 0).Select(a => a.path));
            foreach (var asset in manifest.assets)
                Assert.That(AssetDatabase.AssetPathToGUID(asset.path), Is.EqualTo(asset.guid), asset.path);
            Assert.That(manifest.contentId, Is.EqualTo("uma2"));
            Assert.That(manifest.dependencies, Is.EqualTo(new[] { "core", "srp", "uma3" }));
            Assert.That(manifest.requiredPluginApiVersion, Is.EqualTo(UMAPluginApi.Version));
            Assert.That(manifest.contentVersion, Is.EqualTo(UMAPackageVersionUtility.Normalize(UMASettings.GetSettings().UMAVersion, out var release)));
            Assert.That(manifest.umaVersion, Is.EqualTo(release));
        }

        [Test]
        public void MainArchiveExcludesOptionalWearablesAndCompanions()
        {
            Assert.That(Directory.Exists("Assets/UMA2/Wearables"), Is.False);
            Assert.That(manifest.dependencies, Does.Not.Contain("uma2-compatibility-examples"));
            Assert.That(manifest.assets.Any(a => a.path.StartsWith("Assets/UMA2/Wearables/", StringComparison.Ordinal)), Is.False);
            Assert.That(manifest.assets.Any(a => a.path.StartsWith("Assets/UMA2CompatibilityExamples/", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void MainAssetsHaveNoDependenciesOnOptionalExamples()
        {
            var failures = AssetDatabase.FindAssets("", new[] { "Assets/UMA2" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => !AssetDatabase.IsValidFolder(p))
                .SelectMany(p => AssetDatabase.GetDependencies(p, true)
                    .Where(d => d.StartsWith("Assets/UMA2CompatibilityExamples/", StringComparison.Ordinal))
                    .Select(d => p + " -> " + d)).ToArray();
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void MainRecipesResolveSlotsAndOverlaysWithoutExamples()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UMATextRecipe", new[] { "Assets/UMA2/Races" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var source = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(path);
                var recipe = new UMAData.UMARecipe();
                source.Load(recipe);
                foreach (var slot in recipe.slotDataList ?? Array.Empty<SlotData>())
                {
                    if (slot == null) continue; // Packed recipes can contain empty slots.
                    if (!slot.isPlaceholderSlot)
                    {
                        Assert.That(slot.asset, Is.Not.Null, path);
                        AssertRequiredByMain(slot.asset, path);
                    }
                    foreach (var overlay in slot.GetOverlayList())
                    {
                        Assert.That(overlay.asset, Is.Not.Null, path);
                        AssertRequiredByMain(overlay.asset, path);
                    }
                }
            }
        }

        private static void AssertRequiredByMain(UnityEngine.Object asset, string recipe)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            Assert.That(path.StartsWith("Assets/UMA2CompatibilityExamples/", StringComparison.Ordinal), Is.False,
                recipe + " resolves an optional example asset: " + path);
        }

        [TestCase("HumanMale")]
        [TestCase("HumanFemale")]
        [TestCase("HumanMaleHighPoly")]
        [TestCase("HumanFemaleHighPoly")]
        [TestCase("HumanBoy")]
        [TestCase("HumanGirl")]
        [TestCase("Elf Male")]
        [TestCase("Elf Female")]
        public void LegacyBaseRaceBuildsIndependentlyOfWearableExamples(string raceName)
        {
            var race = AssetDatabase.FindAssets("t:RaceData", new[] { "Assets/UMA2/Races" })
                .Select(g => AssetDatabase.LoadAssetAtPath<RaceData>(AssetDatabase.GUIDToAssetPath(g)))
                .Single(r => r.raceName == raceName);
            var report = UMARaceSmokeTestRunner.Run(race);
            Assert.That(report.HasErrors, Is.False, report.ToLogString());
        }

        [Test]
        public void OptionalUtilitySlotsRetainTheirScriptAssemblyAndPrefabReferences()
        {
            const string examples = "Assets/UMA2CompatibilityExamples";
            if (!File.Exists(examples + "/UMA2.Content.asmref")) Assert.Ignore("Optional examples not installed.");
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { examples }))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(script.GetClass(), Is.Not.Null, script.name);
                Assert.That(script.GetClass().Assembly.GetName().Name, Is.EqualTo("UMA2.Content"));
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { examples + "/Wearables/Example/AdditionalSlots" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                foreach (var transform in prefab.GetComponentsInChildren<UnityEngine.Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, path);
            }
        }

        [Test]
        public void LegacyPathResolutionPreservesCurrentRootAndMapsPreviousNestedRoot()
        {
            Assert.That(UMAPathUtility.Uma2ContentRoot, Is.EqualTo("Assets/UMA2"));
            Assert.That(UMAPathUtility.ResolveLegacyInstallAssetPath("Assets/UMA2/Races"), Is.EqualTo("Assets/UMA2/Races"));
            Assert.That(UMAPathUtility.ResolveLegacyInstallAssetPath("Assets/UMA/UMA2/Races"), Is.EqualTo("Assets/UMA2/Races"));
            Assert.That(UMAPathUtility.IsProjectOwnedUmaAssetPath("Assets/UMA2/Races"), Is.True);
        }

        [Test]
        public void WelcomeGroupsCompanionsUnderCompatibilityPlugin()
        {
            var order = UMAContentCatalog.PluginDisplayOrder.ToArray();
            int index = Array.IndexOf(order, UMAContentKind.Uma2);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            CollectionAssert.AreEqual(new[] { UMAContentKind.Uma2CompatibilityExamples, UMAContentKind.Uma2CompatibilityTests }, order.Skip(index + 1).Take(2));
            Assert.That(UMAContentCatalog.DisplayName(UMAContentKind.Uma2), Is.EqualTo("UMA2Compatibility"));
        }

        [Test]
        public void DocumentationRegistersSeparatelyAndRemovalPreservesGuide()
        {
            string descriptor = UMAContentCatalog.Root(UMAContentKind.Uma2) + "/UMAPluginDocumentation.json";
            try
            {
                UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor);
                var registration = UMAPluginDocumentationRegistry.Registrations.Single(r => r.pluginId == "uma2");
                Assert.That(registration.displayName, Is.EqualTo("UMA2Compatibility"));
                Assert.That(registration.DocumentationPath, Is.EqualTo("Assets/UMA2/UMA2Docs"));
                Assert.That(UMAPluginDocumentationRegistry.IsPluginDocument(registration.DocumentationPath + "/UMA2Compatibility.md"), Is.True);
                UMAPluginDocumentationRegistry.Unregister("uma2");
                Assert.That(UMAPluginDocumentationRegistry.Registrations.Any(r => r.pluginId == "uma2"), Is.False);
                Assert.That(File.Exists("Assets/UMA2/UMA2Docs/UMA2Compatibility.md"), Is.True);
            }
            finally { UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor); }
        }

        [TestCase(UMAContentKind.Uma2CompatibilityExamples)]
        [TestCase(UMAContentKind.Uma2CompatibilityTests)]
        public void CompanionArchivesHaveSeparateOwnership(UMAContentKind kind)
        {
            if (!UMAContentCatalog.PluginRequiredPaths(kind).All(File.Exists)) Assert.Ignore("Optional companion sources not installed.");
            string archive = UMAPluginPackageBuilder.Build(kind, directory);
            Assert.That(UMAContentPackageArchiveValidator.TryValidate(archive, kind, out var info, out string error), Is.True, error);
            Assert.That(info.Manifest.dependencies, Does.Contain("uma2"));
            Assert.That(info.Manifest.assets.Select(a => a.guid).Intersect(manifest.assets.Select(a => a.guid)), Is.Empty);
            Assert.That(info.Manifest.ownedPaths.All(p => UMAContentCatalog.OwnsPluginPath(kind, p)), Is.True);
        }
    }
}
