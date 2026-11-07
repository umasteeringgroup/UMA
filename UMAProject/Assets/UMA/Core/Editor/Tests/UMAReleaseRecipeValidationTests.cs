using System;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Packed = UMA.UMAPackedRecipeBase;

namespace UMA.Editors.Tests
{
    public sealed class UMAReleaseRecipeValidationTests
    {
        private UMAAssetIndexer index;
        private string folder;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/UMAReleaseRecipeTests-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            AssetDatabase.CreateFolder(folder, "Nested");
            index = ScriptableObject.CreateInstance<UMAAssetIndexer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(index);
            // Finish pending importer metadata writes before removing the disposable tree.
            AssetDatabase.ImportAsset(folder, ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ImportRecursive);
            AssetDatabase.DeleteAsset(folder);
        }

        private T Asset<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            AssetDatabase.CreateAsset(asset, folder + "/Nested/" + name + ".asset");
            return asset;
        }

        private AssetItem Index(Object asset, string lookupName = null)
        {
            var item = new AssetItem(asset.GetType(), lookupName ?? asset.name,
                AssetDatabase.GetAssetPath(asset), asset);
            index.SerializedItems.Add(item);
            return item;
        }

        private UMAWardrobeRecipe Recipe(int version, bool indexed = true)
        {
            var recipe = Asset<UMAWardrobeRecipe>("TrackerScarfM");
            var packed = new Packed.UMAPackRecipe { version = version };
            if (version == 1)
                packed.packedSlotDataList = new[] { new Packed.packedSlotData {
                    slotID = "M_Ranger 2 Scarf", OverlayDataList = new[] {
                        new Packed.packedOverlayData { overlayID = "Ranger Scarf" }
                    }
                } };
            else if (version == 2)
                packed.slotsV2 = new[] { new Packed.PackedSlotDataV2 {
                    id = "M_Ranger 2 Scarf", overlays = new[] {
                        new Packed.PackedOverlayDataV2 { id = "Ranger Scarf" }
                    }
                } };
            else
                packed.slotsV3 = new[] { new Packed.PackedSlotDataV3 {
                    id = "M_Ranger 2 Scarf", overlays = new[] {
                        new Packed.PackedOverlayDataV3 { id = "Ranger Scarf" }
                    }
                } };
            recipe.PackedSave(packed);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            if (indexed) Index(recipe);
            return recipe;
        }

        private UMAReleaseValidationReport Validate(bool discover = false)
        {
            index.DoInitialDictionaryLoad();
            var report = new UMAReleaseValidationReport();
            UMAReleaseTests.ValidateRecipeAssets(report, index,
                discover ? new[] { folder } : Array.Empty<string>());
            return report;
        }

        private UMAWardrobeRecipe CopyRecipe(UMAWardrobeRecipe recipe, bool indexed = true)
        {
            AssetDatabase.CreateFolder(folder, "Other");
            string path = folder + "/Other/" + recipe.name + ".asset";
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(recipe), path), Is.True);
            var copy = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(path);
            if (indexed) Index(copy);
            return copy;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void MissingNamesFailTheReportForEveryRecipeVersion(int version)
        {
            var recipe = Recipe(version);
            var report = Validate();
            Assert.That(report.passed, Is.False);
            Assert.That(report.issueCount, Is.EqualTo(2));
            Assert.That(report.issues.All(i => i.ownerAssetPath == AssetDatabase.GetAssetPath(recipe)), Is.True);
            Assert.That(report.issues.Any(i => i.referencedAssetName == "M_Ranger 2 Scarf" &&
                i.kind == "Unavailable recipe slot"), Is.True);
            Assert.That(report.issues.Any(i => i.referencedAssetName == "Ranger Scarf" &&
                i.kind == "Unavailable recipe overlay"), Is.True);
            var roundTrip = JsonUtility.FromJson<UMAReleaseValidationReport>(JsonUtility.ToJson(report));
            Assert.That(roundTrip.passed, Is.False);
            Assert.That(roundTrip.issueCount, Is.EqualTo(2));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void LoadableIndexedAssetsPassWithoutDuplicateFolderDiscovery(int version)
        {
            Recipe(version);
            Index(Asset<SlotDataAsset>("Scarf_slot"), "M_Ranger 2 Scarf");
            Index(Asset<OverlayDataAsset>("Scarf_overlay"), "Ranger Scarf");
            var report = Validate(true);
            Assert.That(report.issues, Is.Empty);
            Assert.That(report.passed, Is.True);
            Assert.That(report.assets.Count, Is.EqualTo(1));
        }

        [Test]
        public void UnindexedNestedRecipeIsStillCheckedAndDoesNotRepairTheIndex()
        {
            var recipe = Recipe(2, false);
            var report = Validate(true);
            Assert.That(report.issues.Any(i => i.kind == "Recipe missing from index"), Is.True);
            Assert.That(report.issues.Any(i => i.referencedAssetName == "M_Ranger 2 Scarf"), Is.True);
            Assert.That(report.issueCount, Is.EqualTo(3));
            Assert.That(index.GetAssetItem<UMAWardrobeRecipe>(recipe.name), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedDuplicateNamesReportTheSelectedAssetInsteadOfMissingMembership(bool discover)
        {
            var recipe = Recipe(2);
            var copy = CopyRecipe(recipe);
            Index(Asset<SlotDataAsset>("Scarf_slot"), "M_Ranger 2 Scarf");
            Index(Asset<OverlayDataAsset>("Scarf_overlay"), "Ranger Scarf");
            var report = Validate(discover);
            Assert.That(report.assets.Count, Is.EqualTo(2));
            var issue = report.issues.Single();
            Assert.That(issue.kind, Is.EqualTo("Recipe name collision"));
            Assert.That(issue.ownerAssetPath, Is.EqualTo(AssetDatabase.GetAssetPath(recipe)));
            Assert.That(issue.referencedAssetPath, Is.EqualTo(AssetDatabase.GetAssetPath(copy)));
            Assert.That(issue.referencedAssetGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(issue.referencedAssetPath)));
            Assert.That(index.GetAssetItem<UMAWardrobeRecipe>(recipe.name)._SerializedItem, Is.SameAs(copy));
            Assert.That(UMAReleaseValidationRepairUtility.CanRelocate(issue), Is.False);
            Assert.That(UMAReleaseValidationRepairUtility.BuildAutoMovePlan(report), Is.Empty);
        }

        [Test]
        public void ShadowedIndexedRecipeStillHasItsContentsValidated()
        {
            var recipe = Recipe(2);
            var copy = CopyRecipe(recipe);
            copy.PackedSave(new Packed.UMAPackRecipe { version = 2 });
            var report = Validate();
            Assert.That(report.assets.Count, Is.EqualTo(2));
            Assert.That(report.issues.Any(i => i.kind == "Recipe missing from index"), Is.False);
            Assert.That(report.issues.Any(i => i.ownerAssetPath == AssetDatabase.GetAssetPath(recipe) &&
                i.referencedAssetName == "M_Ranger 2 Scarf"), Is.True);
            Assert.That(report.issues.Any(i => i.ownerAssetPath == AssetDatabase.GetAssetPath(recipe) &&
                i.referencedAssetName == "Ranger Scarf"), Is.True);
        }

        [Test]
        public void RepeatedIndexEntryForTheSameAssetIsNotANameCollision()
        {
            var recipe = Recipe(2);
            Index(recipe);
            Index(Asset<SlotDataAsset>("Scarf_slot"), "M_Ranger 2 Scarf");
            Index(Asset<OverlayDataAsset>("Scarf_overlay"), "Ranger Scarf");
            var report = Validate(true);
            Assert.That(report.issues, Is.Empty);
            Assert.That(report.assets.Count, Is.EqualTo(1));
        }

        [Test]
        public void AnIndexedNameDoesNotHideAnUnindexedDifferentAssetWithThatName()
        {
            var recipe = Recipe(2);
            var unindexed = CopyRecipe(recipe, false);
            Index(Asset<SlotDataAsset>("Scarf_slot"), "M_Ranger 2 Scarf");
            Index(Asset<OverlayDataAsset>("Scarf_overlay"), "Ranger Scarf");
            var report = Validate(true);
            var issue = report.issues.Single();
            Assert.That(issue.kind, Is.EqualTo("Recipe missing from index"));
            Assert.That(issue.ownerAssetPath, Is.EqualTo(AssetDatabase.GetAssetPath(unindexed)));
        }

        [Test]
        public void StaleIndexedAssetsFailEvenWhenTheIndexContainsTheirNames()
        {
            Recipe(2);
            index.SerializedItems.Add(new AssetItem(typeof(SlotDataAsset), "M_Ranger 2 Scarf",
                folder + "/MissingSlot.asset", null));
            index.SerializedItems.Add(new AssetItem(typeof(OverlayDataAsset), "Ranger Scarf",
                folder + "/MissingOverlay.asset", null));
            var report = Validate();
            Assert.That(index.GetAssetItem<SlotDataAsset>("M_Ranger 2 Scarf"), Is.Not.Null);
            Assert.That(report.issueCount, Is.EqualTo(2));
            Assert.That(report.passed, Is.False);
        }

        [Test]
        public void GuidFallbackLoadsMovedAssetsWithoutChangingIndexedPaths()
        {
            Recipe(2);
            var slot = Index(Asset<SlotDataAsset>("Scarf_slot"), "M_Ranger 2 Scarf");
            Index(Asset<OverlayDataAsset>("Scarf_overlay"), "Ranger Scarf");
            slot._Path = folder + "/OldSlotPath.asset";
            var report = Validate(true);
            Assert.That(report.issues, Is.Empty);
            Assert.That(slot._Path, Is.EqualTo(folder + "/OldSlotPath.asset"));
        }

        [Test]
        public void PlaceholderSlotNeedsNoAssetButItsOverlaysAreChecked()
        {
            var recipe = Recipe(3);
            var packed = recipe.PackedLoad();
            packed.slotsV3[0].isPlaceholderSlot = true;
            recipe.PackedSave(packed);
            var report = Validate();
            Assert.That(report.issueCount, Is.EqualTo(1));
            Assert.That(report.issues[0].kind, Is.EqualTo("Unavailable recipe overlay"));
        }

        [Test]
        public void MalformedRecipeIsReportedAlongsideOtherRecipeFailures()
        {
            Recipe(2);
            var broken = Asset<UMAWardrobeRecipe>("BrokenRecipe");
            broken.recipeString = "{invalid json";
            Index(broken);
            var report = Validate();
            Assert.That(report.issues.Any(i => i.ownerAssetName == broken.name && i.kind == "Recipe load failed"), Is.True);
            Assert.That(report.issues.Any(i => i.referencedAssetName == "M_Ranger 2 Scarf"), Is.True);
        }

        [Test]
        public void MissingGlobalLibraryFailsTheReport()
        {
            var report = new UMAReleaseValidationReport();
            UMAReleaseTests.ValidateRecipeAssets(report, null);
            Assert.That(report.passed, Is.False);
            Assert.That(report.issues.Single().kind, Is.EqualTo("Recipe index unavailable"));
        }
    }
}
