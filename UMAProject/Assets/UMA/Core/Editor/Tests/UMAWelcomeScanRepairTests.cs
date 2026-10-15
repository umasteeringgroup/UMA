#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class UMAWelcomeScanRepairTests
    {
        private readonly List<Object> assets = new List<Object>();
        private string folder;

        private T Asset<T>(string name) where T : ScriptableObject
        {
            if (folder == null)
            {
                folder = "Assets/WelcomeScanRepairTest_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            }
            var a = ScriptableObject.CreateInstance<T>(); a.name = name;
            AssetDatabase.CreateAsset(a, folder + "/" + name + ".asset"); assets.Add(a); return a;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var asset in assets) if (asset != null) Undo.ClearUndo(asset);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
            assets.Clear(); folder = null;
        }

        [Test]
        public void MissingModesDefaultToNormalAndRetainExistingModes()
        {
            var original = new[] { OverlayDataAsset.OverlayBlend.Multiply };
            var resized = UMAScanRepairUtility.ResizeBlendModes(original, 3);
            Assert.That(resized, Is.EqualTo(new[] { OverlayDataAsset.OverlayBlend.Multiply, OverlayDataAsset.OverlayBlend.Normal, OverlayDataAsset.OverlayBlend.Normal }));
            Assert.That(original.Length, Is.EqualTo(1));
        }

        [Test]
        public void ExtraModesAreTrimmedWithoutChangingRetainedModes()
        {
            var original = new[] { OverlayDataAsset.OverlayBlend.Screen, OverlayDataAsset.OverlayBlend.Multiply, OverlayDataAsset.OverlayBlend.Overlay };
            Assert.That(UMAScanRepairUtility.ResizeBlendModes(original, 2), Is.EqualTo(new[] { original[0], original[1] }));
            Assert.That(UMAScanRepairUtility.ResizeBlendModes(original, 0), Is.Empty);
            Assert.That(UMAScanRepairUtility.ResizeBlendModes(null, 2), Is.EqualTo(new[] { OverlayDataAsset.OverlayBlend.Normal, OverlayDataAsset.OverlayBlend.Normal }));
        }

        [Test]
        public void BoundsRepairCopiesExtentsAndCenterAndPreservesOtherSettings()
        {
            var source = Asset<RaceData>("Source"); var target = Asset<RaceData>("Target");
            source.useManualRendererBounds = target.useManualRendererBounds = true;
            source.manualRendererBounds = new Vector3(1, 2, 3); source.manualRendererBoundsCenter = new Vector3(0, 1, 0);
            target.manualRendererBounds = Vector3.zero; target.forceRebuildRaceSlots = true;
            Assert.That(UMAScanRepairUtility.FixBoundsAndSave(target, source), Is.True);
            Assert.That(target.manualRendererBounds, Is.EqualTo(source.manualRendererBounds));
            Assert.That(target.manualRendererBoundsCenter, Is.EqualTo(source.manualRendererBoundsCenter));
            Assert.That(target.forceRebuildRaceSlots, Is.True);
            Assert.That(EditorUtility.IsDirty(target), Is.False);
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(target), folder + "/Saved.asset"), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<RaceData>(folder + "/Saved.asset").manualRendererBounds, Is.EqualTo(source.manualRendererBounds));
        }

        [Test]
        public void InvalidBoundsSourceCannotChangeTarget()
        {
            var source = Asset<RaceData>("Source"); var target = Asset<RaceData>("Target");
            target.useManualRendererBounds = true; target.manualRendererBounds = new Vector3(-1, 2, 3);
            source.useManualRendererBounds = true; source.manualRendererBounds = Vector3.one;
            source.manualRendererBoundsCenter = new Vector3(float.NaN, 0, 0);
            Assert.Throws<InvalidOperationException>(() => UMAScanRepairUtility.FixBoundsAndSave(target, source));
            Assert.That(target.manualRendererBounds.x, Is.EqualTo(-1));
            source.manualRendererBoundsCenter = Vector3.zero; source.manualRendererBounds = new Vector3(1, float.PositiveInfinity, 1);
            Assert.That(UMAScanRepairUtility.HasValidManualBounds(source), Is.False);
        }

        [Test]
        public void AlreadyValidOrDisabledBoundsAreNotOverwritten()
        {
            var source = Asset<RaceData>("Source"); var target = Asset<RaceData>("Target");
            source.useManualRendererBounds = target.useManualRendererBounds = true;
            source.manualRendererBounds = Vector3.one * 2; target.manualRendererBounds = Vector3.one;
            Assert.That(UMAScanRepairUtility.FixBoundsAndSave(target, source), Is.False);
            Assert.That(target.manualRendererBounds, Is.EqualTo(Vector3.one));
            target.useManualRendererBounds = false; target.manualRendererBounds = Vector3.zero;
            Assert.That(UMAScanRepairUtility.FixBoundsAndSave(target, source), Is.False);
        }

        [Test]
        public void OverlayRepairSavesNativeAssetPreservesTexturesAndSupportsUndoRedo()
        {
            var overlay = Asset<OverlayDataAsset>("Overlay");
            overlay.textureList = new Texture[3]; overlay.overlayBlend = new[] { OverlayDataAsset.OverlayBlend.Multiply };
            var textures = overlay.textureList;
            Undo.IncrementCurrentGroup();
            Assert.That(UMAScanRepairUtility.FixBlendModesAndSave(overlay), Is.True);
            Assert.That(overlay.textureList, Is.SameAs(textures));
            Assert.That(overlay.overlayBlend.Length, Is.EqualTo(3));
            Assert.That(EditorUtility.IsDirty(overlay), Is.False);
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(overlay), folder + "/Saved.asset"), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(folder + "/Saved.asset").overlayBlend, Is.EqualTo(overlay.overlayBlend));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(overlay.overlayBlend.Length, Is.EqualTo(1));
            Undo.PerformRedo(); Assert.That(overlay.overlayBlend.Length, Is.EqualTo(3));
            Assert.That(overlay.overlayBlend[0], Is.EqualTo(OverlayDataAsset.OverlayBlend.Multiply));
        }

        [Test]
        public void RecipeScanReadsLegacySlotsWithoutChangingTheirStorage()
        {
            const string json = "{\"version\":2,\"slotsV2\":[{\"id\":\"\",\"copyIdx\":-1},{\"id\":\"MaleFace\",\"copyIdx\":-1,\"overlays\":[{\"id\":\"Skin\"}]}],\"slotsV3\":null}";
            var packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(json);
            string before = JsonUtility.ToJson(packed);
            var slots = UMAScanRepairUtility.GetRecipeSlotsForScan(packed);
            Assert.That(slots.Length, Is.EqualTo(2));
            Assert.That(UMAScanRepairUtility.IsEmptySlotEntry(slots[0]), Is.True);
            Assert.That(slots[1].id, Is.EqualTo("MaleFace"));
            Assert.That(slots[1].overlays[0].id, Is.EqualTo("Skin"));
            Assert.That(JsonUtility.ToJson(packed), Is.EqualTo(before));
            packed.version = 1;
            packed.packedSlotDataList = new[]
            {
                new UMAPackedRecipeBase.packedSlotData { slotID = "LegacyFace", copyOverlayIndex = -1,
                    OverlayDataList = new[] { new UMAPackedRecipeBase.packedOverlayData { overlayID = "LegacySkin" } } }
            };
            slots = UMAScanRepairUtility.GetRecipeSlotsForScan(packed);
            Assert.That(slots[0].id, Is.EqualTo("LegacyFace"));
            Assert.That(slots[0].overlays[0].id, Is.EqualTo("LegacySkin"));
        }

        [Test]
        public void RecipeScanUsesDeclaredVersionAndRetainsPlaceholderAndSharingInformation()
        {
            var packed = new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3,
                slotsV2 = new[] { new UMAPackedRecipeBase.PackedSlotDataV2 { id = "StaleLegacy" } },
                slotsV3 = new[] { new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Placeholder", isPlaceholderSlot = true, copyIdx = 2 } }
            };
            var slots = UMAScanRepairUtility.GetRecipeSlotsForScan(packed);
            Assert.That(slots[0].id, Is.EqualTo("Placeholder"));
            Assert.That(slots[0].isPlaceholderSlot, Is.True);
            Assert.That(slots[0].copyIdx, Is.EqualTo(2));
            packed.slotsV3 = null;
            Assert.That(UMAScanRepairUtility.GetRecipeSlotsForScan(packed), Is.Null);
        }

        [Test]
        public void MissingRecipeRaceRemovalPreservesJsonAndSupportsSaveUndoRedo()
        {
            var recipe = Asset<UMATextRecipe>("MissingRace");
            const string original = "{\"version\":3, \"race\" : \"NewFemale\",\"custom\":{\"race\":\"Nested\",\"n\":12345678901234567890},\"slotsV3\":[]}";
            recipe.recipeString = original;
            Undo.IncrementCurrentGroup();
            Assert.That(UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "NewFemale", null, _ => false), Is.True);
            string repaired = original.Replace("\"NewFemale\"", "\"\"");
            Assert.That(recipe.recipeString, Is.EqualTo(repaired));
            Assert.That(EditorUtility.IsDirty(recipe), Is.False);
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(recipe), folder + "/Saved.asset"), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<UMATextRecipe>(folder + "/Saved.asset").recipeString, Is.EqualTo(repaired));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(recipe.recipeString, Is.EqualTo(original));
            Undo.PerformRedo(); Assert.That(recipe.recipeString, Is.EqualTo(repaired));
        }

        [Test]
        public void MissingRecipeRaceReplacementUsesSelectedRaceAndPreservesOtherFields()
        {
            var recipe = Asset<UMATextRecipe>("MissingRace");
            var race = Asset<RaceData>("Replacement");
            const string original = "{\"version\":3,\"race\":\"NewFemale\",\"custom\":\"NewFemale\",\"slotsV3\":[]}";
            recipe.recipeString = original;
            recipe.compatibleRaces = new List<string> { "Other" };
            Assert.That(UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "NewFemale", race, name => name == race.raceName), Is.True);
            Assert.That(recipe.recipeString, Is.EqualTo(original.Replace("\"race\":\"NewFemale\"", "\"race\":\"" + race.raceName + "\"")));
            Assert.That(recipe.compatibleRaces, Is.EqualTo(new[] { "Other" }));
            string repaired = recipe.recipeString;
            Assert.That(UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "NewFemale", null, name => name == race.raceName), Is.False);
            Assert.That(recipe.recipeString, Is.EqualTo(repaired));
        }

        [Test]
        public void MissingRecipeRaceRepairRejectsStaleScanAndInvalidReplacement()
        {
            var recipe = Asset<UMATextRecipe>("MissingRace");
            var race = Asset<RaceData>("NotIndexed");
            const string original = "{\"version\":3,\"race\":\"ChangedSinceScan\",\"slotsV3\":[]}";
            recipe.recipeString = original;
            Assert.Throws<InvalidOperationException>(() => UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "NewFemale", null, _ => false));
            Assert.Throws<InvalidOperationException>(() => UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "ChangedSinceScan", race, _ => false));
            Assert.That(recipe.recipeString, Is.EqualTo(original));
        }

        [Test]
        public void MissingRecipeRaceRepairHandlesEscapesAndRejectsDuplicateFields()
        {
            var recipe = Asset<UMATextRecipe>("MissingRace");
            recipe.recipeString = "{\"version\":3,\"ra\\u0063e\":\"New\\\"Female\",\"note\":\"\\\"race\\\":\\\"value\\\"\"}";
            Assert.That(UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "New\"Female", null, _ => false), Is.True);
            Assert.That(recipe.recipeString, Does.Contain("\"ra\\u0063e\":\"\""));
            const string duplicate = "{\"version\":3,\"race\":\"Missing\",\"race\":\"Missing\"}";
            recipe.recipeString = duplicate;
            Assert.Throws<InvalidOperationException>(() => UMAScanRepairUtility.FixMissingRecipeRaceAndSave(recipe, "Missing", null, _ => false));
            Assert.That(recipe.recipeString, Is.EqualTo(duplicate));
        }

        [Test]
        public void PlaceholderRecipesCanOmitRaceWithoutSlotAssetLookup()
        {
            var packed = new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3,
                slotsV3 = new[] { new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Placeholder", isPlaceholderSlot = true } }
            };
            Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed,
                _ => throw new Exception("Placeholders do not require an asset")), Is.True);
            packed.slotsV3 = Array.Empty<UMAPackedRecipeBase.PackedSlotDataV3>();
            Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed, _ => null), Is.False);
        }

        [Test]
        public void UtilityRecipesCanOmitRaceButMixedAndMissingSlotsCannot()
        {
            var utility = ScriptableObject.CreateInstance<SlotDataAsset>();
            var mesh = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                utility.isClippingPlane = true;
                mesh.meshData = new UMAMeshData { vertexCount = 3 };
                SlotDataAsset Resolve(string name) => name == "Utility" ? utility : name == "Mesh" ? mesh : null;
                var packed = new UMAPackedRecipeBase.UMAPackRecipe
                {
                    version = 3,
                    slotsV3 = new[]
                    {
                        new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Placeholder", isPlaceholderSlot = true },
                        new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Utility" }
                    }
                };
                Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed, Resolve), Is.True);
                packed.slotsV3[1].id = "Mesh";
                Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed, Resolve), Is.False);
                packed.slotsV3[1].id = "Missing";
                Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed, Resolve), Is.False);
                packed.slotsV3 = null;
                packed.version = 2;
                packed.slotsV2 = new[] { new UMAPackedRecipeBase.PackedSlotDataV2 { id = "Utility" } };
                Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed, Resolve), Is.True);
                packed.slotsV2 = null;
                packed.version = 1;
                packed.packedSlotDataList = new[] { new UMAPackedRecipeBase.packedSlotData { slotID = "Utility" } };
                Assert.That(UMAScanRepairUtility.IsRaceIndependentRecipe(packed, Resolve), Is.True);
            }
            finally { Object.DestroyImmediate(utility); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EmptySlotRepairPreservesNamedSlotsAndRemapsSharedOverlays()
        {
            var recipe = Asset<UMAWardrobeRecipe>("EmptySlots");
            recipe.wardrobeSlot = "Tattoo";
            recipe.compatibleRaces = new List<string> { "TestRace" };
            recipe.recipeString = JsonUtility.ToJson(new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3, race = "TestRace", sharedColorCount = 2,
                slotsV3 = new[]
                {
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "" },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Torso", copyIdx = -1,
                        overlays = new[] { new UMAPackedRecipeBase.PackedOverlayDataV3 { id = "Tattoo", colorIdx = 1 } } },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "  " },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Arms", copyIdx = 1 },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Placeholder", isPlaceholderSlot = true, Tags = new[] { "Body" } }
                }
            });
            string original = recipe.recipeString;
            Undo.IncrementCurrentGroup();
            Assert.That(UMAScanRepairUtility.FixEmptySlotsAndSave(recipe), Is.EqualTo(2));
            var packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(recipe.recipeString);
            Assert.That(packed.slotsV3.Length, Is.EqualTo(3));
            Assert.That(packed.slotsV3[0].id, Is.EqualTo("Torso"));
            Assert.That(packed.slotsV3[1].copyIdx, Is.EqualTo(0));
            Assert.That(packed.slotsV3[0].overlays[0].id, Is.EqualTo("Tattoo"));
            Assert.That(packed.slotsV3[2].isPlaceholderSlot, Is.True);
            Assert.That(packed.slotsV3[2].Tags, Is.EqualTo(new[] { "Body" }));
            Assert.That(packed.race, Is.EqualTo("TestRace"));
            Assert.That(packed.sharedColorCount, Is.EqualTo(2));
            Assert.That(recipe.wardrobeSlot, Is.EqualTo("Tattoo"));
            Assert.That(recipe.compatibleRaces, Is.EqualTo(new[] { "TestRace" }));
            Assert.That(EditorUtility.IsDirty(recipe), Is.False);
            string repaired = recipe.recipeString;
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(recipe), folder + "/Saved.asset"), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(folder + "/Saved.asset").recipeString, Is.EqualTo(repaired));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(recipe.recipeString, Is.EqualTo(original));
            Undo.PerformRedo(); Assert.That(recipe.recipeString, Is.EqualTo(repaired));
            Assert.That(UMAScanRepairUtility.FixEmptySlotsAndSave(recipe), Is.Zero);
        }

        [Test]
        public void EmptySlotRepairRetainsOverlaysReferencedThroughRemovedEntries()
        {
            var recipe = Asset<UMAWardrobeRecipe>("OverlayDonor");
            recipe.recipeString = JsonUtility.ToJson(new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3,
                slotsV3 = new[]
                {
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "", copyIdx = -1,
                        overlays = new[] { new UMAPackedRecipeBase.PackedOverlayDataV3 { id = "Tattoo" } } },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "", copyIdx = 0 },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Torso", copyIdx = 1 },
                    new UMAPackedRecipeBase.PackedSlotDataV3 { id = "Arms", copyIdx = 2 }
                }
            });
            Assert.That(UMAScanRepairUtility.FixEmptySlotsAndSave(recipe), Is.EqualTo(2));
            var slots = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(recipe.recipeString).slotsV3;
            Assert.That(slots[0].copyIdx, Is.EqualTo(-1));
            Assert.That(slots[0].overlays[0].id, Is.EqualTo("Tattoo"));
            Assert.That(slots[1].copyIdx, Is.EqualTo(0));
        }

        [Test]
        public void EmptySlotRepairRejectsBrokenDependenciesWithoutChangingRecipe()
        {
            var recipe = Asset<UMAWardrobeRecipe>("BrokenDonor");
            recipe.recipeString = "{\"version\":3,\"slotsV3\":[{\"id\":\"\",\"copyIdx\":0},{\"id\":\"Torso\",\"copyIdx\":0}]}";
            string original = recipe.recipeString;
            Assert.Throws<InvalidOperationException>(() => UMAScanRepairUtility.FixEmptySlotsAndSave(recipe));
            Assert.That(recipe.recipeString, Is.EqualTo(original));
        }

        [Test]
        public void EmptySlotRepairHandlesAllEmptyAndMissingSlotArrays()
        {
            var recipe = Asset<UMAWardrobeRecipe>("AllEmpty");
            recipe.recipeString = "{\"version\":3,\"slotsV3\":[{\"id\":\"\"},{\"id\":\" \"}]}";
            Assert.That(UMAScanRepairUtility.FixEmptySlotsAndSave(recipe), Is.EqualTo(2));
            Assert.That(JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(recipe.recipeString).slotsV3, Is.Empty);
            recipe.recipeString = "{\"version\":3}";
            Assert.That(UMAScanRepairUtility.FixEmptySlotsAndSave(recipe), Is.Zero);
            Assert.That(recipe.recipeString, Is.EqualTo("{\"version\":3}"));
            Assert.That(UMAScanRepairUtility.IsEmptySlotEntry(null), Is.True);
        }
    }
}
#endif
