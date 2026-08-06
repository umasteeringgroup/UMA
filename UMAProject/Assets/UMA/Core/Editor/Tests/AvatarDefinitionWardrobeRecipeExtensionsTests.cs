using System;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Tests
{
    public class AvatarDefinitionWardrobeRecipeExtensionsTests
    {
        [Test]
        public void ToWardrobeRecipeCreatesRuntimeRecipeUsingFirstColor()
        {
            SlotDataAsset slotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset overlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            UMAWardrobeRecipe recipe = null;

            try
            {
                slotAsset._oldSlotName = "RuntimeSlot";
                overlayAsset._oldOverlayName = "RuntimeOverlay";
                overlayAsset.textureList = Array.Empty<Texture>();

                AvatarDefinition definition = CreateAvatarDefinition();
                recipe = definition.ToWardrobeRecipe(
                    slotAsset,
                    overlayAsset,
                    "RuntimeRecipe",
                    "Runtime Recipe",
                    "Chest",
                    new[] { "HumanMale", "HumanFemale" });

                Assert.AreEqual("RuntimeRecipe", recipe.name);
                Assert.AreEqual("Runtime Recipe", recipe.DisplayValue);
                Assert.AreEqual("Chest", recipe.wardrobeSlot);
                CollectionAssert.AreEqual(
                    new[] { "HumanMale", "HumanFemale" },
                    recipe.compatibleRaces);
                Assert.AreEqual("height", recipe.OverrideDNA.PreloadValues[0].Name);
                Assert.AreEqual(
                    0.73f,
                    recipe.OverrideDNA.PreloadValues[0].Value,
                    0.0001f);

                UMAPackedRecipeBase.UMAPackRecipe packed =
                    recipe.PackedLoad();
                Assert.IsTrue(packed.isWardrobe);
                Assert.AreEqual("HumanMale", packed.race);
                Assert.AreEqual(1, packed.fColors.Length);
                Assert.AreEqual("Clothing", packed.fColors[0].name);
                Assert.AreEqual("RuntimeSlot", packed.slotsV3[0].id);
                Assert.AreEqual(
                    "RuntimeOverlay",
                    packed.slotsV3[0].overlays[0].id);
                Assert.AreEqual(
                    0,
                    packed.slotsV3[0].overlays[0].colorIdx);
            }
            finally
            {
                if (recipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(recipe);
                }
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void ToWardrobeRecipeCanUseOnlyAnExistingWardrobeRecipe()
        {
            SlotDataAsset slotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset overlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            UMAWardrobeRecipe sourceRecipe =
                ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            UMAWardrobeRecipe resultRecipe = null;

            try
            {
                slotAsset._oldSlotName = "SourceRecipeSlot";
                overlayAsset._oldOverlayName = "SourceRecipeOverlay";
                overlayAsset.textureList = Array.Empty<Texture>();

                var slot = new SlotData(slotAsset);
                slot.AddOverlay(new OverlayData(overlayAsset));
                var umaRecipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { slot },
                    sharedColors = Array.Empty<OverlayColorData>()
                };
                umaRecipe.ClearDna();

                sourceRecipe.name = "SourceRecipe";
                sourceRecipe.DisplayValue = "Source Recipe";
                sourceRecipe.wardrobeSlot = "Hair";
                sourceRecipe.compatibleRaces.Add("HumanMale");
                sourceRecipe.Hides.Add("Head");
                sourceRecipe.UserField = "source-metadata";
                sourceRecipe.Save(umaRecipe);

                AvatarDefinition definition = CreateAvatarDefinition();
                resultRecipe = definition.ToWardrobeRecipe(sourceRecipe);

                Assert.AreNotSame(sourceRecipe, resultRecipe);
                Assert.AreEqual(sourceRecipe.name, resultRecipe.name);
                Assert.AreEqual(
                    sourceRecipe.DisplayValue,
                    resultRecipe.DisplayValue);
                Assert.AreEqual(
                    sourceRecipe.wardrobeSlot,
                    resultRecipe.wardrobeSlot);
                CollectionAssert.AreEqual(
                    sourceRecipe.compatibleRaces,
                    resultRecipe.compatibleRaces);
                CollectionAssert.AreEqual(
                    sourceRecipe.Hides,
                    resultRecipe.Hides);
                Assert.AreEqual(
                    sourceRecipe.UserField,
                    resultRecipe.UserField);

                UMAPackedRecipeBase.UMAPackRecipe packed =
                    resultRecipe.PackedLoad();
                Assert.AreEqual("SourceRecipeSlot", packed.slotsV3[0].id);
                Assert.AreEqual(
                    "SourceRecipeOverlay",
                    packed.slotsV3[0].overlays[0].id);
                Assert.AreEqual(
                    definition.Colors[0].name,
                    packed.fColors[0].name);
                Assert.AreEqual(
                    definition.Dna[0].Name,
                    resultRecipe.OverrideDNA.PreloadValues[0].Name);
            }
            finally
            {
                if (resultRecipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(resultRecipe);
                }
                UnityEngine.Object.DestroyImmediate(sourceRecipe);
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void ToWardrobeRecipeImportsSlotsAndOverlaysFromDefinitionWardrobe()
        {
            FieldInfo indexerInstanceField =
                typeof(UMAAssetIndexer).GetField(
                    "theIndexer",
                    BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(indexerInstanceField);

            object originalIndexer = indexerInstanceField.GetValue(null);
            UMAAssetIndexer testIndexer =
                ScriptableObject.CreateInstance<UMAAssetIndexer>();
            SlotDataAsset sourceSlotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset sourceOverlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            UMAWardrobeRecipe sourceRecipe =
                ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            UMAWardrobeRecipe combinedRecipe = null;

            try
            {
                sourceSlotAsset._oldSlotName = "DefinitionWardrobeSlot";
                sourceOverlayAsset._oldOverlayName =
                    "DefinitionWardrobeOverlay";
                sourceOverlayAsset.textureList = Array.Empty<Texture>();

                var sourceSlot = new SlotData(sourceSlotAsset);
                sourceSlot.AddOverlay(new OverlayData(sourceOverlayAsset));
                var sourceUmaRecipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { sourceSlot },
                    sharedColors = Array.Empty<OverlayColorData>()
                };
                sourceUmaRecipe.ClearDna();

                sourceRecipe.name = "DefinitionWardrobeRecipe";
                sourceRecipe.wardrobeSlot = "Chest";
                sourceRecipe.Save(sourceUmaRecipe);

                indexerInstanceField.SetValue(null, testIndexer);
                testIndexer.AddAsset(
                    typeof(UMAWardrobeRecipe),
                    sourceRecipe.name,
                    string.Empty,
                    sourceRecipe);

                AvatarDefinition definition = CreateAvatarDefinition();
                definition.Wardrobe = new[] { sourceRecipe.name };
                combinedRecipe = definition.ToWardrobeRecipe(
                    "CombinedRuntimeRecipe",
                    "Combined Runtime Recipe",
                    "Outfit",
                    new[] { "HumanMale" });

                UMAPackedRecipeBase.UMAPackRecipe packed =
                    combinedRecipe.PackedLoad();
                Assert.AreEqual(1, packed.slotsV3.Length);
                Assert.AreEqual(
                    "DefinitionWardrobeSlot",
                    packed.slotsV3[0].id);
                Assert.AreEqual(1, packed.slotsV3[0].overlays.Length);
                Assert.AreEqual(
                    "DefinitionWardrobeOverlay",
                    packed.slotsV3[0].overlays[0].id);
                Assert.AreEqual(
                    0,
                    packed.slotsV3[0].overlays[0].colorIdx);
                Assert.AreEqual(1, packed.fColors.Length);
                Assert.AreEqual(
                    definition.Colors[0].name,
                    packed.fColors[0].name);
            }
            finally
            {
                indexerInstanceField.SetValue(null, originalIndexer);
                if (combinedRecipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(combinedRecipe);
                }
                UnityEngine.Object.DestroyImmediate(sourceRecipe);
                UnityEngine.Object.DestroyImmediate(sourceOverlayAsset);
                UnityEngine.Object.DestroyImmediate(sourceSlotAsset);
                UnityEngine.Object.DestroyImmediate(testIndexer);
            }
        }

        [Test]
        public void ToAvatarDefinitionReturnsDefinitionAndRecipeOnlyData()
        {
            SlotDataAsset slotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset overlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            UMAWardrobeRecipe recipe = null;
            UMAWardrobeRecipe recreatedRecipe = null;

            try
            {
                slotAsset._oldSlotName = "RoundTripSlot";
                overlayAsset._oldOverlayName = "RoundTripOverlay";
                overlayAsset.textureList = Array.Empty<Texture>();

                AvatarDefinition sourceDefinition = CreateAvatarDefinition();
                recipe = sourceDefinition.ToWardrobeRecipe(
                    slotAsset,
                    overlayAsset,
                    "RoundTripRecipe",
                    "Round Trip Recipe",
                    "Hands",
                    new[] { "HumanMale" });
                recipe.Appended = true;
                recipe.Hides.Add("Hands");
                recipe.HideTags.Add("Gloves");
                recipe.UserField = "runtime-data";
                recipe.replaces = "OldGloves";

                WardrobeRecipeAvatarDefinition converted =
                    recipe.ToAvatarDefinition();

                Assert.AreEqual(
                    sourceDefinition.RaceName,
                    converted.AvatarDefinition.RaceName);
                CollectionAssert.AreEqual(
                    new[] { "RoundTripRecipe" },
                    converted.AvatarDefinition.Wardrobe);
                Assert.AreEqual(
                    sourceDefinition.Colors[0].name,
                    converted.AvatarDefinition.Colors[0].name);
                Assert.AreEqual(
                    sourceDefinition.Colors[0].channels[0].mCol,
                    converted.AvatarDefinition.Colors[0].channels[0].mCol);
                Assert.AreEqual(
                    sourceDefinition.Dna[0].Name,
                    converted.AvatarDefinition.Dna[0].Name);
                Assert.AreEqual(
                    sourceDefinition.Dna[0].Value,
                    converted.AvatarDefinition.Dna[0].Value,
                    0.0001f);

                WardrobeRecipeAdditionalData additionalData =
                    converted.AdditionalData;
                Assert.AreEqual("RoundTripRecipe", additionalData.RecipeName);
                Assert.AreEqual("Hands", additionalData.WardrobeSlot);
                Assert.IsTrue(additionalData.Appended);
                CollectionAssert.AreEqual(
                    new[] { "Hands" },
                    additionalData.Hides);
                CollectionAssert.AreEqual(
                    new[] { "Gloves" },
                    additionalData.HideTags);
                Assert.AreEqual("runtime-data", additionalData.UserField);
                Assert.AreEqual("OldGloves", additionalData.Replaces);
                Assert.AreEqual(
                    "RoundTripSlot",
                    additionalData.PackedRecipe.slotsV3[0].id);

                recreatedRecipe =
                    converted.AvatarDefinition.ToWardrobeRecipe(
                        converted.AdditionalData);
                Assert.AreEqual(recipe.name, recreatedRecipe.name);
                Assert.AreEqual(recipe.wardrobeSlot, recreatedRecipe.wardrobeSlot);
                Assert.AreEqual(recipe.UserField, recreatedRecipe.UserField);
                Assert.AreEqual(
                    recipe.PackedLoad().slotsV3[0].id,
                    recreatedRecipe.PackedLoad().slotsV3[0].id);
            }
            finally
            {
                if (recreatedRecipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(recreatedRecipe);
                }
                if (recipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(recipe);
                }
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void ToWardrobeRecipeRequiresAColor()
        {
            SlotDataAsset slotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset overlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();

            try
            {
                var definition = new AvatarDefinition
                {
                    Colors = Array.Empty<SharedColorDef>()
                };

                Assert.Throws<ArgumentException>(() =>
                    definition.ToWardrobeRecipe(
                        slotAsset,
                        overlayAsset,
                        "NoColor",
                        "No Color",
                        "Chest",
                        Array.Empty<string>()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        private static AvatarDefinition CreateAvatarDefinition()
        {
            var firstColor = new SharedColorDef("Clothing", 1)
            {
                channels = new[]
                {
                    new ColorDef(
                        0,
                        ColorDef.ToUInt(
                            new Color32(64, 128, 192, 255)),
                        ColorDef.ToUInt(
                            new Color32(1, 2, 3, 4)))
                },
                shaderParms = Array.Empty<string>()
            };
            var secondColor = new SharedColorDef("Ignored", 1)
            {
                channels = new[]
                {
                    new ColorDef(
                        0,
                        ColorDef.ToUInt(new Color32(255, 0, 0, 255)),
                        0)
                },
                shaderParms = Array.Empty<string>()
            };

            return new AvatarDefinition
            {
                RaceName = "HumanMale",
                Wardrobe = Array.Empty<string>(),
                Colors = new[] { firstColor, secondColor },
                Dna = new[] { new DnaDef("height", 0.73f) }
            };
        }
    }
}
