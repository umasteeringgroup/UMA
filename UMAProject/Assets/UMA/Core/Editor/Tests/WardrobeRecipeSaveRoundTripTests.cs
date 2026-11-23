using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static UMA.OverlayDataAsset;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Saving and reloading a recipe must keep what it stored: overlays that differ in any stored value stay
    /// apart, every packed overlay value comes back (ordinary and placeholder slots alike), local colors that
    /// differ only in shader properties stay apart, a slot or overlay the project lacks is reported rather than
    /// silently dropped (and the rest of the recipe still loads), a recipe that never had a slot can be saved,
    /// a race listed twice gets one thumbnail, and integer shader properties keep their names.
    /// </summary>
    public class WardrobeRecipeSaveRoundTripTests
    {
        static OverlayDataAsset NewOverlayAsset(string name)
        {
            var asset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            asset._oldOverlayName = name;
            asset.textureList = new Texture[2];
            asset.overlayBlend = new OverlayBlend[2];
            return asset;
        }

        [Test]
        public void EquivalentRequiresEveryStoredOverlayValueToMatch()
        {
            OverlayDataAsset asset = NewOverlayAsset("RoundTripEquivalentOverlay");
            try
            {
                var first = new OverlayData(asset);
                OverlayData NewSecond() => new OverlayData(asset) { colorData = first.colorData, rect = first.rect };

                Assert.IsTrue(OverlayData.Equivalent(first, NewSecond()), "identical overlays");

                var rotated = NewSecond(); rotated.Rotation = 45f;
                Assert.IsFalse(OverlayData.Equivalent(first, rotated), "rotation");
                var scaled = NewSecond(); scaled.Scale = new Vector2(2f, 1f);
                Assert.IsFalse(OverlayData.Equivalent(first, scaled), "scale");
                var moved = NewSecond(); moved.Translate = new Vector2(0.1f, 0f);
                Assert.IsFalse(OverlayData.Equivalent(first, moved), "translate");
                var transformed = NewSecond(); transformed.instanceTransformed = true;
                Assert.IsFalse(OverlayData.Equivalent(first, transformed), "instanceTransformed");
                var uv = NewSecond(); uv.UVSet = 2;
                Assert.IsFalse(OverlayData.Equivalent(first, uv), "UV set");
                var tiled = NewSecond(); tiled.SetTextureTiling(1, true);
                Assert.IsFalse(OverlayData.Equivalent(first, tiled), "tiling");
                var blended = NewSecond(); blended.SetOverlayBlendsLength(2); blended.SetOverlayBlend(0, OverlayBlend.Multiply);
                Assert.IsFalse(OverlayData.Equivalent(first, blended), "blend mode");
                var tagged = NewSecond(); tagged.tags = new[] { "Tattoo" };
                Assert.IsFalse(OverlayData.Equivalent(first, tagged), "tags");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void SavingKeepsSlotsWhoseOverlaysDifferOnlyInRotationApart()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripMergeOverlay");
            SlotDataAsset slotA = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset slotB = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotA._oldSlotName = "RoundTripMergeSlotA";
                slotB._oldSlotName = "RoundTripMergeSlotB";
                var shared = new OverlayColorData(2) { name = "Shared" };
                var a = new SlotData(slotA);
                a.AddOverlay(new OverlayData(overlayAsset) { colorData = shared, Rotation = 0f });
                var b = new SlotData(slotB);
                b.AddOverlay(new OverlayData(overlayAsset) { colorData = shared, Rotation = 90f });
                var recipe = new UMAData.UMARecipe { slotDataList = new[] { a, b }, sharedColors = new[] { shared } };

                recipe.MergeMatchingOverlays();
                UMAPackedRecipeBase.UMAPackRecipe packed = UMAPackedRecipeBase.PackRecipeV3(recipe);

                Assert.AreNotSame(a.GetOverlayList(), b.GetOverlayList(), "the overlay lists must not be merged");
                Assert.AreEqual(-1, packed.slotsV3[1].copyIdx, "slot B must keep its own overlays");
                Assert.AreEqual(90f, packed.slotsV3[1].overlays[0].rotation);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotA);
                UnityEngine.Object.DestroyImmediate(slotB);
            }
        }

        [Test]
        public void ReloadRestoresEveryPackedOverlayValueIncludingItsOwnUvSet()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripApplyOverlay");
            SlotDataAsset slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotAsset._oldSlotName = "RoundTripApplySlot";
                var overlay = new OverlayData(overlayAsset)
                {
                    rect = new Rect(0.1f, 0.2f, 0.3f, 0.4f),
                    instanceTransformed = true,
                    Rotation = 30f,
                    Scale = new Vector2(1.5f, 0.5f),
                    Translate = new Vector2(0.05f, -0.05f),
                    UVSet = 2,
                    tags = new[] { "Makeup" },
                };
                overlay.SetOverlayBlendsLength(2);
                overlay.SetOverlayBlend(1, OverlayBlend.Screen);
                overlay.SetTextureTiling(0, true);
                var slot = new SlotData(slotAsset) { UVSet = 1 };
                slot.AddOverlay(overlay);
                var recipe = new UMAData.UMARecipe { slotDataList = new[] { slot }, sharedColors = Array.Empty<OverlayColorData>() };

                UMAPackedRecipeBase.UMAPackRecipe packed = UMAPackedRecipeBase.PackRecipeV3(recipe);
                // Through JSON, as a saved recipe is (UMATextRecipe.PackedSave / PackedLoad).
                packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(JsonUtility.ToJson(packed));
                OverlayColorData[] allColors = UMAPackedRecipeBase.UnpackColors(packed);
                var restored = new OverlayData(overlayAsset);
                UMAPackedRecipeBase.ApplyPackedOverlay(restored, packed.slotsV3[0].overlays[0],
                    packed.sharedColorCount, Array.Empty<OverlayColorData>(), allColors);

                Assert.AreEqual(overlay.rect, restored.rect);
                Assert.IsTrue(restored.instanceTransformed);
                Assert.AreEqual(30f, restored.Rotation);
                Assert.AreEqual(overlay.Scale, restored.Scale);
                Assert.AreEqual(overlay.Translate, restored.Translate);
                Assert.AreEqual(2, restored.UVSet, "the overlay's own UV set, not the slot's (1)");
                Assert.AreEqual(OverlayBlend.Screen, restored.GetOverlayBlend(1));
                Assert.IsTrue(restored.IsTextureTiled(0));
                CollectionAssert.AreEqual(new[] { "Makeup" }, restored.tags);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void LocalColorsThatDifferOnlyInShaderPropertiesAreStoredApart()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripColorOverlay");
            SlotDataAsset slotA = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset slotB = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotA._oldSlotName = "RoundTripColorSlotA";
                slotB._oldSlotName = "RoundTripColorSlotB";
                OverlayColorData Tinted(Color tint)
                {
                    var color = new OverlayColorData(2) { name = OverlayColorData.UNSHARED, PropertyBlock = new UMAMaterialPropertyBlock() };
                    color.PropertyBlock.shaderProperties.Add(new UMAColorProperty { name = "_Tint", Value = tint });
                    return color;
                }
                var a = new SlotData(slotA);
                a.AddOverlay(new OverlayData(overlayAsset) { colorData = Tinted(Color.red) });
                var b = new SlotData(slotB);
                b.AddOverlay(new OverlayData(overlayAsset) { colorData = Tinted(Color.blue) });
                var recipe = new UMAData.UMARecipe { slotDataList = new[] { a, b }, sharedColors = Array.Empty<OverlayColorData>() };

                Assert.IsFalse(OverlayData.Equivalent(a.GetOverlay(0), b.GetOverlay(0)), "different tints must not merge");
                recipe.MergeMatchingOverlays();
                UMAPackedRecipeBase.UMAPackRecipe packed = UMAPackedRecipeBase.PackRecipeV3(recipe);

                Assert.AreNotEqual(packed.slotsV3[0].overlays[0].colorIdx, packed.slotsV3[1].overlays[0].colorIdx);
                Assert.AreEqual(2, packed.fColors.Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotA);
                UnityEngine.Object.DestroyImmediate(slotB);
            }
        }

        [Test]
        public void ASlotTheProjectLacksIsReportedNotSilentlyDropped()
        {
            const string missing = "UMATest_RoundTrip_NoSuchSlot_7f3c";
            var packed = new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3,
                isWardrobe = true,
                slotsV3 = new[] { new UMAPackedRecipeBase.PackedSlotDataV3 { id = missing, scale = 100, copyIdx = -1, overlays = Array.Empty<UMAPackedRecipeBase.PackedOverlayDataV3>() } },
                fColors = Array.Empty<UMAPackedRecipeBase.PackedOverlayColorDataV3>(),
            };
            LogAssert.ignoreFailingMessages = true; // the library logs that it cannot find the slot
            try
            {
                var recipe = new UMAData.UMARecipe();
                UMAPackedRecipeBase.UnpackRecipe(recipe, packed);

                Assert.IsNull(recipe.slotDataList[0], "the slot cannot be loaded");
                Assert.IsNotNull(recipe.missingAssets, "the load must say what it left out");
                StringAssert.Contains(missing, string.Join(";", recipe.missingAssets));

                var complete = new UMAData.UMARecipe { slotDataList = Array.Empty<SlotData>() };
                UMAPackedRecipeBase.UnpackRecipe(complete, new UMAPackedRecipeBase.UMAPackRecipe { version = 3, slotsV3 = Array.Empty<UMAPackedRecipeBase.PackedSlotDataV3>(), fColors = Array.Empty<UMAPackedRecipeBase.PackedOverlayColorDataV3>() });
                Assert.IsNull(complete.missingAssets, "a complete load reports nothing");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        static bool FindRealSlotAndOverlay(out SlotDataAsset slot, out OverlayDataAsset overlay)
        {
            // Real assets from the Global Library, so UMA's own loader can find them again.
            slot = null;
            overlay = null;
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null) return false;
            foreach (SlotDataAsset candidate in indexer.GetAllAssets<SlotDataAsset>())
            {
                if (candidate != null && !candidate.isWildCardSlot && !candidate.isUtilitySlot && !string.IsNullOrEmpty(candidate.slotName)
                    && candidate.meshData != null && candidate.meshData.vertexCount > 0)
                {
                    slot = candidate;
                    break;
                }
            }
            foreach (OverlayDataAsset candidate in indexer.GetAllAssets<OverlayDataAsset>())
            {
                if (candidate != null && candidate.material != null && candidate.material.channels != null && candidate.material.channels.Length > 0
                    && !string.IsNullOrEmpty(candidate.overlayName))
                {
                    overlay = candidate;
                    break;
                }
            }
            return slot != null && overlay != null;
        }

        [Test]
        public void AMissingOverlayIsReportedAndTheRestOfTheRecipeStillLoads()
        {
            if (!FindRealSlotAndOverlay(out SlotDataAsset slot, out OverlayDataAsset overlay))
            {
                Assert.Ignore("The Global Library holds no slot with a mesh, or no overlay with a material.");
            }
            const string missing = "UMATest_RoundTrip_NoSuchOverlay_2b9d";

            var source = new UMAData.UMARecipe();
            var first = new SlotData(slot);
            var lost = new OverlayData(overlay);
            lost.rect = new Rect(0, 0, 16, 16);
            first.AddOverlay(lost);
            first.AddOverlay(new OverlayData(overlay));
            var second = new SlotData(slot);
            second.AddOverlay(new OverlayData(overlay));
            source.slotDataList = new[] { first, second };
            // Through JSON, as a saved recipe is (the packed colors fill their shader-property lists there).
            UMAPackedRecipeBase.UMAPackRecipe packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(
                JsonUtility.ToJson(UMAPackedRecipeBase.PackRecipeV3(source)));
            Assert.AreEqual(2, packed.slotsV3[0].overlays.Length);
            packed.slotsV3[0].overlays[0].id = missing;

            LogAssert.ignoreFailingMessages = true; // the loader logs the overlay it skips
            try
            {
                var recipe = new UMAData.UMARecipe();
                // The library throws for a missing overlay; the load used to stop there.
                Assert.DoesNotThrow(() => UMAPackedRecipeBase.UnpackRecipe(recipe, packed));
                Assert.IsNotNull(recipe.missingAssets, "the load must say what it left out");
                StringAssert.Contains(missing, string.Join(";", recipe.missingAssets));
                Assert.IsNotNull(recipe.slotDataList[0], "the overlay's slot still loads");
                Assert.AreEqual(1, recipe.slotDataList[0].OverlayCount, "the slot keeps its other overlay");
                Assert.IsNotNull(recipe.slotDataList[1], "slots after the missing overlay still load");
                Assert.AreEqual(1, recipe.slotDataList[1].OverlayCount);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void AVersion2RecipeWithAMissingSlotReportsItInsteadOfFailing()
        {
            if (!FindRealSlotAndOverlay(out SlotDataAsset slot, out _))
            {
                Assert.Ignore("The Global Library holds no slot with a mesh.");
            }
            const string missingSlot = "UMATest_RoundTrip_NoSuchSlotV2_51e0";
            const string missingOverlay = "UMATest_RoundTrip_NoSuchOverlayV2_51e0";
            var packed = new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 2,
                isWardrobe = true,
                slotsV2 = new[]
                {
                    new UMAPackedRecipeBase.PackedSlotDataV2 { id = missingSlot, scale = 100, copyIdx = -1, overlays = Array.Empty<UMAPackedRecipeBase.PackedOverlayDataV2>() },
                    new UMAPackedRecipeBase.PackedSlotDataV2 { id = slot.slotName, scale = 100, copyIdx = -1,
                        overlays = new[] { new UMAPackedRecipeBase.PackedOverlayDataV2 { id = missingOverlay, colorIdx = 0, rect = new int[4] } } },
                    // Copies its overlays from the missing slot.
                    new UMAPackedRecipeBase.PackedSlotDataV2 { id = slot.slotName, scale = 100, copyIdx = 0 },
                },
            };
            LogAssert.ignoreFailingMessages = true; // the library logs that it cannot find the slot
            try
            {
                var recipe = new UMAData.UMARecipe();
                Assert.DoesNotThrow(() => UMAPackedRecipeBase.UnpackRecipe(recipe, packed));
                Assert.IsNull(recipe.slotDataList[0]);
                Assert.IsNotNull(recipe.slotDataList[1]);
                Assert.AreEqual(0, recipe.slotDataList[1].OverlayCount);
                Assert.IsNotNull(recipe.slotDataList[2]);
                string reported = string.Join(";", recipe.missingAssets);
                StringAssert.Contains(missingSlot, reported);
                StringAssert.Contains(missingOverlay, reported);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void ARaceListedTwiceGetsOneThumbnailThatStaysOne()
        {
            var a1 = new WardrobeRecipeThumb("A");
            var a2 = new WardrobeRecipeThumb("A");
            var b1 = new WardrobeRecipeThumb("B");
            var races = new[] { "A", "B", "A" };

            var thumbs = RecipeEditor.BuildRaceThumbs(races, new[] { a1, a2, b1 });
            CollectionAssert.AreEqual(new[] { a1, b1 }, thumbs, "the first stored thumbnail per race, in race order");
            // Drawn again: the inspector used to double the list on every draw (and save it).
            CollectionAssert.AreEqual(thumbs, RecipeEditor.BuildRaceThumbs(races, thumbs));

            var fresh = RecipeEditor.BuildRaceThumbs(new[] { "C" }, null);
            Assert.AreEqual(1, fresh.Count);
            Assert.AreEqual("C", fresh[0].race);
        }

        [Test]
        public void AnIntegerShaderPropertyKeepsItsName()
        {
            var property = new UMAIntProperty { name = "_Mode", Value = 3 };
            UMAProperty once = UMAProperty.FromString(property.ToString());
            UMAProperty twice = UMAProperty.FromString(once.ToString());
            Assert.AreEqual("_Mode", once.name);
            Assert.AreEqual("_Mode", twice.name, "every save used to add \"***\" to the name");
            Assert.AreEqual(3, ((UMAIntProperty)twice).Value);

            // As older saves wrote it: the name is repaired on load.
            UMAProperty old = UMAProperty.FromString(property.ToString() + "******");
            Assert.AreEqual("_Mode", old.name);
            Assert.AreEqual(3, ((UMAIntProperty)old).Value);
        }

        [Test]
        public void SharedColorShaderSettingsSurviveAReload()
        {
            // A shader-parameter-only color, as Add Shared Color Parms makes it (no channels).
            var parmsOnly = new OverlayColorData(0) { name = "Parms" };
            parmsOnly.SetColorProperty("_Tint", Color.red);
            // A color with channels whose property block holds only the update flags.
            var flagged = new OverlayColorData(3) { name = "Flagged" };
            flagged.PropertyBlock = new UMAMaterialPropertyBlock { alwaysUpdate = true, alwaysUpdateParms = true };

            var wardrobe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            try
            {
                wardrobe.recipeType = "Wardrobe";
                var recipe = new UMAData.UMARecipe { sharedColors = new[] { parmsOnly, flagged } };
                wardrobe.Save(recipe);
                var reloaded = new UMAData.UMARecipe();
                wardrobe.Load(reloaded);
                Assert.AreEqual(2, reloaded.sharedColors.Length);

                OverlayColorData parms = reloaded.sharedColors[0];
                Assert.AreEqual("Parms", parms.name);
                Assert.AreEqual(0, parms.channelMask.Length, "it was made with no channels");
                Assert.IsTrue(parms.HasProperties, "its shader parameter used to be lost on reload");
                Assert.AreEqual(1, parms.PropertyBlock.shaderProperties.Count);
                var tint = parms.PropertyBlock.shaderProperties[0] as UMAColorProperty;
                Assert.IsNotNull(tint);
                Assert.AreEqual("_Tint", tint.name);
                Assert.AreEqual(Color.red, tint.Value);

                OverlayColorData flags = reloaded.sharedColors[1];
                Assert.AreEqual(3, flags.channelMask.Length);
                Assert.IsTrue(flags.HasPropertyBlock, "a block holding only the update flags used to be dropped");
                Assert.IsTrue(flags.PropertyBlock.alwaysUpdate);
                Assert.IsTrue(flags.PropertyBlock.alwaysUpdateParms);

                // A plain color still comes back without a property block.
                var plain = new UMAData.UMARecipe { sharedColors = new[] { new OverlayColorData(3) { name = "Plain" } } };
                wardrobe.Save(plain);
                var plainReloaded = new UMAData.UMARecipe();
                wardrobe.Load(plainReloaded);
                Assert.IsFalse(plainReloaded.sharedColors[0].HasPropertyBlock);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wardrobe);
            }
        }

        [Test]
        public void OverlaysSharedFromAMissingSlotStayWithTheSlotsSharingThem()
        {
            if (!FindRealSlotAndOverlay(out SlotDataAsset slot, out OverlayDataAsset overlay))
            {
                Assert.Ignore("The Global Library holds no slot with a mesh, or no overlay with a material.");
            }
            const string missing = "UMATest_RoundTrip_NoSuchSharingSlot_9a41";
            var owner = new SlotData(slot);
            owner.AddOverlay(new OverlayData(overlay));
            var sharer = new SlotData(slot);
            sharer.SetOverlayList(owner.GetOverlayList());
            var secondSharer = new SlotData(slot);
            secondSharer.SetOverlayList(owner.GetOverlayList());
            var source = new UMAData.UMARecipe { slotDataList = new[] { owner, sharer, secondSharer } };
            UMAPackedRecipeBase.UMAPackRecipe packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(
                JsonUtility.ToJson(UMAPackedRecipeBase.PackRecipeV3(source)));
            Assert.AreEqual(0, packed.slotsV3[1].copyIdx, "the packer stores shared overlays once, with the first slot");
            packed.slotsV3[0].id = missing;

            LogAssert.ignoreFailingMessages = true;
            try
            {
                var recipe = new UMAData.UMARecipe();
                UMAPackedRecipeBase.UnpackRecipe(recipe, packed);
                Assert.IsNull(recipe.slotDataList[0]);
                StringAssert.Contains(missing, string.Join(";", recipe.missingAssets));
                Assert.AreEqual(1, recipe.slotDataList[1].OverlayCount, "the overlays stored with the missing slot are still loaded");
                Assert.AreSame(recipe.slotDataList[1].GetOverlayList(), recipe.slotDataList[2].GetOverlayList(), "and still shared");

                // Saved again (after Enable Editing), the overlays stay in the recipe.
                UMAPackedRecipeBase.UMAPackRecipe repacked = UMAPackedRecipeBase.PackRecipeV3(recipe);
                Assert.AreEqual(1, repacked.slotsV3[1].overlays.Length);
                Assert.AreEqual(1, repacked.slotsV3[2].copyIdx);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void NameOnlyShaderPropertiesKeepTheirNamesAndCopyWithoutValues()
        {
            Assert.AreEqual("_MainTex", UMAProperty.FromString(new UMATextureProperty { name = "_MainTex" }.ToString()).name);
            Assert.AreEqual("_Weights", UMAProperty.FromString(new UMAFloatArrayProperty { name = "_Weights" }.ToString()).name);
            Assert.AreEqual("_Projection", UMAProperty.FromString(new UMAMatrixProperty { name = "_Projection" }.ToString()).name);
            Assert.AreEqual("_Vectors", UMAProperty.FromString(new UMAVectorArrayProperty { name = "_Vectors" }.ToString()).name);

            // Loaded array properties have no value; copying them (an unshared color is copied on load) threw.
            UMAProperty loaded = UMAProperty.FromString(new UMAVectorArrayProperty { name = "_Vectors" }.ToString());
            Assert.DoesNotThrow(() => loaded.Clone());
            Assert.DoesNotThrow(() => UMAProperty.FromString(new UMAFloatArrayProperty { name = "_Weights" }.ToString()).Clone());
            Assert.DoesNotThrow(() => UMAProperty.FromString(new UMAMatrixArrayProperty { name = "_Bones" }.ToString()).Clone());
            var color = new OverlayColorData(1);
            color.PropertyBlock = new UMAMaterialPropertyBlock();
            color.PropertyBlock.shaderProperties.Add(loaded);
            Assert.DoesNotThrow(() => color.Clone());
        }

        [Test]
        public void HdrShaderColorsAndChannelLessSwatchesSurviveAReload()
        {
            var hdr = new UMAColorProperty { name = "_EmissionColor", Value = new Color(4f, 0.5f, 0f, 1f) };
            var reloaded = (UMAColorProperty)UMAProperty.FromString(hdr.ToString());
            Assert.AreEqual(4f, reloaded.Value.r, 1e-6f, "the hex form clamped this to 1");
            Assert.AreEqual(0.5f, reloaded.Value.g, 1e-6f);
            Assert.AreEqual("_EmissionColor", reloaded.name);

            // A color within 0-1 keeps the hex form older recipes use.
            var ldr = new UMAColorProperty { name = "_Tint", Value = Color.red };
            StringAssert.Contains("#FF0000FF", ldr.ToString());
            Assert.AreEqual(Color.red, ((UMAColorProperty)UMAProperty.FromString(ldr.ToString())).Value);

            var channelLess = new OverlayColorData(0) { name = "Swatch", displayColor = Color.red };
            var packed = JsonUtility.FromJson<UMAPackedRecipeBase.PackedOverlayColorDataV3>(
                JsonUtility.ToJson(new UMAPackedRecipeBase.PackedOverlayColorDataV3(channelLess)));
            var restored = new OverlayColorData();
            packed.SetOverlayColorData(restored);
            Assert.AreEqual(Color.red, restored.displayColor, "a color with no channels stored white");
        }

        [Test]
        public void AFlagOnlyShaderBlockIsNotTheSameAsNone()
        {
            var plain = new OverlayColorData(3);
            var flagged = new OverlayColorData(3) { PropertyBlock = new UMAMaterialPropertyBlock { alwaysUpdate = true } };
            Assert.IsFalse(OverlayColorData.SameShaderProperties(plain, flagged));
            Assert.IsFalse(OverlayColorData.SameShaderProperties(flagged, plain));
            var emptyBlock = new OverlayColorData(3) { PropertyBlock = new UMAMaterialPropertyBlock() };
            Assert.IsTrue(OverlayColorData.SameShaderProperties(plain, emptyBlock), "an empty block with no flags stores nothing");
        }

        [Test]
        public void ARaceKeepsTheThumbnailThatHasASprite()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            try
            {
                var empty = new WardrobeRecipeThumb("A");
                var withSprite = new WardrobeRecipeThumb("A") { thumb = sprite };
                var thumbs = RecipeEditor.BuildRaceThumbs(new[] { "A" }, new[] { empty, withSprite });
                Assert.AreEqual(1, thumbs.Count);
                Assert.AreSame(withSprite, thumbs[0], "characters use the entry with the sprite");

                // Two with sprites: characters take the last one (GetWardrobeRecipeThumbFor).
                var lastWithSprite = new WardrobeRecipeThumb("A") { thumb = sprite };
                thumbs = RecipeEditor.BuildRaceThumbs(new[] { "A" }, new[] { withSprite, empty, lastWithSprite });
                Assert.AreSame(lastWithSprite, thumbs[0]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void AVersion1RecipeKeepsOverlaysSharedFromAMissingSlot()
        {
            if (!FindRealSlotAndOverlay(out SlotDataAsset slot, out OverlayDataAsset overlay))
            {
                Assert.Ignore("The Global Library holds no slot with a mesh, or no overlay with a material.");
            }
            var races = UMAAssetIndexer.Instance.GetAllAssets<RaceData>();
            if (races == null || races.Count == 0 || races[0] == null) Assert.Ignore("The Global Library holds no race.");
            const string missing = "UMATest_RoundTrip_NoSuchSlotV1_c25e";
            var packed = new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 1,
                race = races[0].raceName,
                packedSlotDataList = new[]
                {
                    new UMAPackedRecipeBase.packedSlotData { slotID = missing, overlayScale = 100, copyOverlayIndex = -1,
                        OverlayDataList = new[] { new UMAPackedRecipeBase.packedOverlayData { overlayID = overlay.overlayName } } },
                    new UMAPackedRecipeBase.packedSlotData { slotID = slot.slotName, overlayScale = 100, copyOverlayIndex = 0 },
                    new UMAPackedRecipeBase.packedSlotData { slotID = slot.slotName, overlayScale = 100, copyOverlayIndex = 0 },
                },
            };
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var recipe = new UMAData.UMARecipe();
                UMAPackedRecipeBase.UnpackRecipe(recipe, packed);
                Assert.IsNull(recipe.slotDataList[0]);
                Assert.AreEqual(1, recipe.slotDataList[1].OverlayCount, "the overlays stored with the missing slot are loaded");
                Assert.AreSame(recipe.slotDataList[1].GetOverlayList(), recipe.slotDataList[2].GetOverlayList(), "and still shared");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void SlotScaleAndColorSwatchSurviveAReload()
        {
            if (!FindRealSlotAndOverlay(out SlotDataAsset slot, out _))
            {
                Assert.Ignore("The Global Library holds no slot with a mesh.");
            }
            // As a recipe loads a stored 53%: 53 * 0.01f, which is 0.52999997.
            var scaled = new SlotData(slot) { overlayScale = 53 * 0.01f };
            var packed = UMAPackedRecipeBase.PackRecipeV3(new UMAData.UMARecipe { slotDataList = new[] { scaled } });
            Assert.AreEqual(53, packed.slotsV3[0].scale, "truncating saved 53% as 52%");

            var color = new OverlayColorData(3) { displayColor = Color.red, showDisplayColor = true };
            color.channelMask[0] = Color.green;
            var packedColor = JsonUtility.FromJson<UMAPackedRecipeBase.PackedOverlayColorDataV3>(
                JsonUtility.ToJson(new UMAPackedRecipeBase.PackedOverlayColorDataV3(color)));
            var restored = new OverlayColorData();
            packedColor.SetOverlayColorData(restored);
            Assert.AreEqual(Color.red, restored.displayColor, "the swatch was stored as the first channel's color");
            Assert.IsTrue(restored.showDisplayColor);
            Assert.AreEqual(Color.green, restored.channelMask[0]);
        }

        [Test]
        public void AShaderParameterOnlySharedColorKeepsNoChannels()
        {
            if (!FindRealSlotAndOverlay(out SlotDataAsset slot, out OverlayDataAsset overlay))
            {
                Assert.Ignore("The Global Library holds no slot with a mesh, or no overlay with a material.");
            }
            var shared = new OverlayColorData(0) { name = "Parms" };
            shared.SetColorProperty("_Tint", Color.red);
            var linked = new OverlayData(overlay) { colorData = shared };
            var slotData = new SlotData(slot);
            slotData.AddOverlay(linked);
            var source = new UMAData.UMARecipe { slotDataList = new[] { slotData }, sharedColors = new[] { shared } };
            var packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(JsonUtility.ToJson(UMAPackedRecipeBase.PackRecipeV3(source)));

            var recipe = new UMAData.UMARecipe();
            UMAPackedRecipeBase.UnpackRecipe(recipe, packed);
            Assert.AreEqual(0, recipe.sharedColors[0].channelCount, "loading expanded it to the overlay material's channels");
            Assert.IsTrue(recipe.sharedColors[0].HasProperties);
        }

        [Test]
        public void LocalColorsThatDifferOnlyInTheirSwatchSettingAreStoredApart()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripSwatchOverlay");
            SlotDataAsset slotA = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset slotB = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotA._oldSlotName = "RoundTripSwatchSlotA";
                slotB._oldSlotName = "RoundTripSwatchSlotB";
                OverlayColorData Swatch(bool shown)
                {
                    var color = new OverlayColorData(2) { name = OverlayColorData.UNSHARED, displayColor = Color.red, showDisplayColor = shown };
                    color.channelMask[0] = Color.green;
                    return color;
                }
                var a = new SlotData(slotA);
                a.AddOverlay(new OverlayData(overlayAsset) { colorData = Swatch(true) });
                var b = new SlotData(slotB);
                b.AddOverlay(new OverlayData(overlayAsset) { colorData = Swatch(false) });
                var recipe = new UMAData.UMARecipe { slotDataList = new[] { a, b }, sharedColors = Array.Empty<OverlayColorData>() };

                UMAPackedRecipeBase.UMAPackRecipe packed = UMAPackedRecipeBase.PackRecipeV3(recipe);

                int idxA = packed.slotsV3[0].overlays[0].colorIdx;
                int idxB = packed.slotsV3[1].overlays[0].colorIdx;
                Assert.AreNotEqual(idxA, idxB, "the second overlay took the first one's swatch");
                Assert.IsTrue(packed.fColors[idxA].showDisplayColor);
                Assert.IsFalse(packed.fColors[idxB].showDisplayColor);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotA);
                UnityEngine.Object.DestroyImmediate(slotB);
            }
        }

        [Test]
        public void DnaOfATypeTheProjectLacksIsKeptWhenTheRecipeIsSaved()
        {
            var wardrobe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            bool ignore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                wardrobe.recipeType = "Wardrobe";
                wardrobe.Save(new UMAData.UMARecipe { sharedColors = Array.Empty<OverlayColorData>() });
                var pack = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(wardrobe.recipeString);
                pack.packedDna.Add(new UMAPackedRecipeBase.UMAPackedDna { dnaType = "UMADnaNoSuchTypeForTests", dnaTypeHash = 424242, packedDna = "{\"value\":1}" });
                wardrobe.recipeString = JsonUtility.ToJson(pack);

                var loaded = new UMAData.UMARecipe();
                wardrobe.Load(loaded);
                Assert.IsNotNull(loaded.unreadDna, "the entry the load could not read must be kept");
                Assert.AreEqual(1, loaded.unreadDna.Count);

                wardrobe.Save(loaded);
                var saved = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(wardrobe.recipeString);
                var kept = saved.packedDna.Find(d => d.dnaType == "UMADnaNoSuchTypeForTests");
                Assert.IsNotNull(kept, "an unrelated save removed the DNA it could not read");
                Assert.AreEqual(424242, kept.dnaTypeHash);
                Assert.AreEqual("{\"value\":1}", kept.packedDna);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
                UnityEngine.Object.DestroyImmediate(wardrobe);
            }
        }

        [Test]
        public void SmallShaderValuesKeepTheirPrecision()
        {
            var tiny = (UMAFloatProperty)UMAProperty.FromString(new UMAFloatProperty { name = "_Tiny", Value = 0.00004f }.ToString());
            Assert.AreEqual(0.00004f, tiny.Value, "0.00004 was written as 0.0000");
            StringAssert.Contains("0.5000", new UMAFloatProperty { name = "_Half", Value = 0.5f }.ToString(), "values four decimals hold keep their old form");

            var vector = (UMAVectorProperty)UMAProperty.FromString(new UMAVectorProperty { name = "_V", Value = new Vector4(1f, 0.123456f, -0.00002f, 3f) }.ToString());
            Assert.AreEqual(new Vector4(1f, 0.123456f, -0.00002f, 3f), vector.Value);

            var transform = (UMAOverlayTransformProperty)UMAProperty.FromString(
                new UMAOverlayTransformProperty(new Vector2(0.00003f, 0.5f), 12.34567f, new Vector2(1.000001f, 1f)) { name = "_T" }.ToString());
            Assert.AreEqual(0.00003f, transform.Translate.x);
            Assert.AreEqual(12.34567f, transform.Rotate);
            Assert.AreEqual(1.000001f, transform.Scale.x);
        }

        [Test]
        public void OneEditorLettingGoDoesNotUnlockARecipeAnotherHoldsPartially()
        {
            var wardrobe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            object graph = new object();
            object inspector = new object();
            try
            {
                CharacterBaseEditor.SetRecipeSaveBlocked(wardrobe, true, graph);
                CharacterBaseEditor.SetRecipeSaveBlocked(wardrobe, true, inspector);
                CharacterBaseEditor.SetRecipeSaveBlocked(wardrobe, false, inspector);
                Assert.IsTrue(CharacterBaseEditor.IsRecipeSaveBlocked(wardrobe), "closing the inspector unlocked the graph's partial copy");
                CharacterBaseEditor.SetRecipeSaveBlocked(wardrobe, false, graph);
                Assert.IsFalse(CharacterBaseEditor.IsRecipeSaveBlocked(wardrobe));
            }
            finally
            {
                CharacterBaseEditor.SetRecipeSaveBlocked(wardrobe, false, graph);
                CharacterBaseEditor.SetRecipeSaveBlocked(wardrobe, false, inspector);
                UnityEngine.Object.DestroyImmediate(wardrobe);
            }
        }

        sealed class FakeWorkingCopyOwner : CharacterBaseEditor.IRecipeWorkingCopyOwner
        {
            public UnityEngine.Object Asset;
            public UMAData.UMARecipe Recipe;
            public bool InSync = true;
            public int Written;
            public bool LastSaved;
            public int Pending;
            public UnityEngine.Object WorkingCopyAsset => Asset;
            public UMAData.UMARecipe WorkingCopy => Recipe;
            public bool WorkingCopyInSync => InSync;
            public bool WritesToDisk => false;
            public void OnWorkingCopyWritten(bool saved) { Written++; LastSaved = saved; }
            public void OnWorkingCopyEditPending() { Pending++; }
        }

        [Test]
        public void ASlotControlEditIsNotWrittenOverAnOutsideChange()
        {
            var wardrobe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            var owner = new FakeWorkingCopyOwner { Asset = wardrobe };
            bool automatic = CharacterBaseEditor._AutomaticUpdates;
            try
            {
                CharacterBaseEditor._AutomaticUpdates = false;
                wardrobe.recipeType = "Wardrobe";
                wardrobe.Save(new UMAData.UMARecipe { sharedColors = Array.Empty<OverlayColorData>() });
                string outside = wardrobe.recipeString;
                owner.Recipe = new UMAData.UMARecipe { sharedColors = new[] { new OverlayColorData(3) { name = "Control edit" } } };
                CharacterBaseEditor.RegisterWorkingCopyOwner(owner);

                // The asset changed elsewhere since the editor loaded it: keep the edit pending, write nothing.
                owner.InSync = false;
                Assert.IsFalse(CharacterBaseEditor.WriteWorkingCopy(wardrobe, owner.Recipe));
                Assert.AreEqual(outside, wardrobe.recipeString);
                Assert.AreEqual(1, owner.Pending);

                // A copy the editor has replaced (it reloaded) is never written back.
                LogAssert.Expect(LogType.Warning, new Regex("was not written"));
                Assert.IsFalse(CharacterBaseEditor.WriteWorkingCopy(wardrobe, new UMAData.UMARecipe()));
                Assert.AreEqual(outside, wardrobe.recipeString);

                // In step: written into the asset, and with Automatic Updates off reported as not on disk.
                owner.InSync = true;
                Assert.IsTrue(CharacterBaseEditor.WriteWorkingCopy(wardrobe, owner.Recipe));
                Assert.AreNotEqual(outside, wardrobe.recipeString);
                Assert.AreEqual(1, owner.Written);
                Assert.IsFalse(owner.LastSaved);
            }
            finally
            {
                CharacterBaseEditor.UnregisterWorkingCopyOwner(owner);
                CharacterBaseEditor._AutomaticUpdates = automatic;
                UnityEngine.Object.DestroyImmediate(wardrobe);
            }
        }

        [Test]
        public void OverlaysThatDifferOnlyInTheirSwatchAreNotLinkedBySaving()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripSwatchMergeOverlay");
            try
            {
                OverlayColorData Swatch(bool shown)
                {
                    var color = new OverlayColorData(2) { name = OverlayColorData.UNSHARED, displayColor = Color.red, showDisplayColor = shown };
                    color.channelMask[0] = Color.green;
                    return color;
                }
                var shown = new OverlayData(overlayAsset) { colorData = Swatch(true) };
                var hidden = new OverlayData(overlayAsset) { colorData = Swatch(false) };
                Assert.IsFalse(OverlayData.Equivalent(shown, hidden), "a save linked the two stacks, and both took one swatch");
                Assert.IsTrue(OverlayData.Equivalent(shown, new OverlayData(overlayAsset) { colorData = Swatch(true) }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
            }
        }

        [Test]
        public void AnOverlayWhoseMaterialIsMissingKeepsItsColors()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripMissingMaterialOverlay");
            bool ignore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                overlayAsset.material = null;
                overlayAsset.materialName = "UMA_NoSuchMaterialForTests";
                var color = new OverlayColorData(3);
                color.channelMask[0] = Color.blue;
                color.channelAdditiveMask[1] = Color.red;
                var overlay = new OverlayData(overlayAsset) { colorData = color };

                overlay.Validate();

                Assert.AreSame(color, overlay.colorData, "drawing the overlay reset its colors");
                Assert.AreEqual(Color.blue, overlay.colorData.channelMask[0]);
                Assert.AreEqual(Color.red, overlay.colorData.channelAdditiveMask[1]);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
                UnityEngine.Object.DestroyImmediate(overlayAsset);
            }
        }

        [Test]
        public void MergingARecipeKeepsTheDnaItCouldNotRead()
        {
            var unread = new UMAPackedRecipeBase.UMAPackedDna { dnaType = "UMADnaNoSuchTypeForTests", dnaTypeHash = 434343, packedDna = "{}" };
            var source = new UMAData.UMARecipe { unreadDna = new System.Collections.Generic.List<UMAPackedRecipeBase.UMAPackedDna> { unread } };
            var target = new UMAData.UMARecipe { sharedColors = Array.Empty<OverlayColorData>() };

            target.Merge(source, false);
            target.Merge(source, false);

            Assert.IsNotNull(target.unreadDna, "drag-merging a recipe dropped the DNA it could not read");
            Assert.AreEqual(1, target.unreadDna.Count, "merged twice, kept once");
            Assert.IsTrue(UMAPackedRecipeBase.GetPackedDNA(target).Exists(d => d.dnaType == "UMADnaNoSuchTypeForTests"));
        }

        [Test]
        public void ARaceSettingsRedrawKeepsTheDefaultThumbnail()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            try
            {
                // The only sprite is on a race that is no longer compatible; characters of races A and B
                // show it as the default (the first sprite in the list).
                var oldRace = new WardrobeRecipeThumb("OldRace") { thumb = sprite };
                var a = new WardrobeRecipeThumb("A");
                var thumbs = RecipeEditor.BuildRaceThumbs(new[] { "A", "B" }, new[] { oldRace, a });

                var recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
                try
                {
                    recipe.wardrobeRecipeThumbs = thumbs;
                    Assert.AreSame(sprite, recipe.GetWardrobeRecipeThumbFor("A"), "opening Race Settings removed the thumbnail");
                    Assert.AreSame(sprite, recipe.GetWardrobeRecipeThumbFor("B"));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(recipe);
                }
                CollectionAssert.AreEqual(thumbs, RecipeEditor.BuildRaceThumbs(new[] { "A", "B" }, thumbs), "a redraw keeps the list as it is");
                StringAssert.Contains("default", RecipeEditor.RaceThumbLabel(thumbs, new[] { "A", "B" }, 0));

                // Every race with a sprite of its own: the default entry is not needed.
                var withOwn = RecipeEditor.BuildRaceThumbs(new[] { "A" }, new[] { oldRace, new WardrobeRecipeThumb("A") { thumb = sprite } });
                Assert.AreEqual(1, withOwn.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void DragMergingARecipeKeepsItsSlotAndOverlaySettings()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripMergeCopyOverlay");
            SlotDataAsset slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotAsset._oldSlotName = "RoundTripMergeCopySlot";
                var overlay = new OverlayData(overlayAsset) { UVSet = 2, tiling = new[] { false, true } };
                var slot = new SlotData(slotAsset) { UVSet = 3, smooshTargetTag = "Target", smooshableTag = "Smooshable" };
                slot.AddOverlay(overlay);
                var source = new UMAData.UMARecipe { slotDataList = new[] { slot }, sharedColors = Array.Empty<OverlayColorData>() };
                var target = new UMAData.UMARecipe { slotDataList = Array.Empty<SlotData>(), sharedColors = Array.Empty<OverlayColorData>() };

                target.Merge(source, false);

                SlotData merged = Array.Find(target.slotDataList, s => s != null && s.asset == slotAsset);
                Assert.IsNotNull(merged);
                Assert.AreEqual(3, merged.UVSet, "the slot's UV set was lost");
                Assert.AreEqual("Target", merged.smooshTargetTag);
                Assert.AreEqual("Smooshable", merged.smooshableTag);
                OverlayData mergedOverlay = merged.GetOverlay(0);
                Assert.AreEqual(2, mergedOverlay.UVSet, "the overlay's UV set was lost");
                Assert.IsTrue(mergedOverlay.IsTextureTiled(1), "the overlay's tiling was lost");
                Assert.AreNotSame(overlay.tiling, mergedOverlay.tiling, "a copy, not the same array");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void AnOverlayKeepsTheSharedColorItIsLinkedTo()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripSharedLinkOverlay");
            SlotDataAsset slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotAsset._oldSlotName = "RoundTripSharedLinkSlot";
                // Two shared colors that look the same (same name and values): the overlay is linked to the second.
                var first = new OverlayColorData(2) { name = "Twin" };
                var second = new OverlayColorData(2) { name = "Twin" };
                var slot = new SlotData(slotAsset);
                slot.AddOverlay(new OverlayData(overlayAsset) { colorData = second });
                var recipe = new UMAData.UMARecipe { slotDataList = new[] { slot }, sharedColors = new[] { first, second } };

                var packed = UMAPackedRecipeBase.PackRecipeV3(recipe);

                Assert.AreEqual(1, packed.slotsV3[0].overlays[0].colorIdx, "saving relinked the overlay to the first look-alike");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void MergingNewerUnreadDnaReplacesTheOlderValues()
        {
            var older = new UMAPackedRecipeBase.UMAPackedDna { dnaType = "UMADnaNoSuchTypeForTests", dnaTypeHash = 454545, packedDna = "{\"v\":1}" };
            var newer = new UMAPackedRecipeBase.UMAPackedDna { dnaType = "UMADnaNoSuchTypeForTests", dnaTypeHash = 454545, packedDna = "{\"v\":2}" };
            var target = new UMAData.UMARecipe { sharedColors = Array.Empty<OverlayColorData>(), unreadDna = new System.Collections.Generic.List<UMAPackedRecipeBase.UMAPackedDna> { older } };

            target.Merge(new UMAData.UMARecipe { unreadDna = new System.Collections.Generic.List<UMAPackedRecipeBase.UMAPackedDna> { newer } }, false);

            Assert.AreEqual(1, target.unreadDna.Count);
            Assert.AreEqual("{\"v\":2}", target.unreadDna[0].packedDna, "the merged recipe's values win, as for DNA that loaded");
        }

        [Test]
        public void SlotsLinkedToTwoLookAlikeSharedColorsAreNotMergedBySaving()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripTwinSharedOverlay");
            try
            {
                var first = new OverlayColorData(2) { name = "Twin" };
                var second = new OverlayColorData(2) { name = "Twin" };
                var a = new OverlayData(overlayAsset) { colorData = first };
                var b = new OverlayData(overlayAsset) { colorData = second };
                Assert.IsFalse(OverlayData.Equivalent(a, b), "a save linked one slot's overlays to the other shared color");
                Assert.IsTrue(OverlayData.Equivalent(a, new OverlayData(overlayAsset) { colorData = first }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
            }
        }

        [Test]
        public void MergingAnOverlayWithoutAMaterialDoesNotThrow()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripNoMaterialOverlay");
            try
            {
                overlayAsset.material = null;
                var a = new OverlayData(overlayAsset);
                var b = new OverlayData(overlayAsset);
                Assert.DoesNotThrow(() => OverlayData.EquivalentAssetAndUse(a, b));
                Assert.IsTrue(OverlayData.EquivalentAssetAndUse(a, b));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
            }
        }

        [Test]
        public void MergingIntoAnExistingSlotTakesTheIncomingOverlaySettings()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripMergeExistingOverlay");
            SlotDataAsset slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slotAsset._oldSlotName = "RoundTripMergeExistingSlot";
                var existingSlot = new SlotData(slotAsset);
                existingSlot.AddOverlay(new OverlayData(overlayAsset));
                var target = new UMAData.UMARecipe { slotDataList = new[] { existingSlot }, sharedColors = Array.Empty<OverlayColorData>() };

                var incomingSlot = new SlotData(slotAsset);
                incomingSlot.AddOverlay(new OverlayData(overlayAsset) { UVSet = 2, instanceTransformed = true, Rotation = 30f, tiling = new[] { true, false } });
                var source = new UMAData.UMARecipe { slotDataList = new[] { incomingSlot }, sharedColors = Array.Empty<OverlayColorData>() };

                target.Merge(source, false);

                OverlayData merged = existingSlot.GetOverlay(0);
                Assert.AreEqual(1, existingSlot.OverlayCount, "the same overlay, not a second one");
                Assert.AreEqual(2, merged.UVSet, "only the colors came across");
                Assert.IsTrue(merged.instanceTransformed);
                Assert.AreEqual(30f, merged.Rotation);
                Assert.IsTrue(merged.IsTextureTiled(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void EachNewPlaceholderSlotGetsItsOwnName()
        {
            var recipe = new UMAData.UMARecipe { slotDataList = Array.Empty<SlotData>(), sharedColors = Array.Empty<OverlayColorData>() };
            string first = RecipeEditor.UniquePlaceholderName(recipe, "NewPlaceholderSlot");
            recipe.MergeSlot(SlotData.CreatePlaceholder(first, new string[0]), false);
            string second = RecipeEditor.UniquePlaceholderName(recipe, "NewPlaceholderSlot");
            recipe.MergeSlot(SlotData.CreatePlaceholder(second, new string[0]), false);

            Assert.AreEqual("NewPlaceholderSlot", first);
            Assert.AreNotEqual(first, second, "the second press gave back the first placeholder");
            Assert.AreEqual(2, recipe.slotDataList.Length);
        }

        [Test]
        public void ARecipeThatNeverHadASlotCanBeSavedAndReloaded()
        {
            var wardrobe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            try
            {
                wardrobe.recipeType = "Wardrobe";
                var blank = new UMAData.UMARecipe();
                Assert.IsNull(blank.slotDataList);
                blank.sharedColors = new[] { new OverlayColorData(3) { name = "Color 1" } };

                Assert.DoesNotThrow(() => wardrobe.Save(blank));
                var reloaded = new UMAData.UMARecipe();
                Assert.DoesNotThrow(() => wardrobe.Load(reloaded));
                Assert.AreEqual(1, reloaded.sharedColors.Length);
                Assert.AreEqual("Color 1", reloaded.sharedColors[0].name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wardrobe);
            }
        }

        static OverlayColorData NamedColor(string name, Color color)
        {
            var data = new OverlayColorData(3) { name = name };
            data.channelMask[0] = color;
            return data;
        }

        [Test]
        public void MergingKeepsTwoSharedColorsWithOneNameApart()
        {
            OverlayDataAsset overlayAsset = NewOverlayAsset("RoundTripMergeTwinColorOverlay");
            var slotAssets = new SlotDataAsset[3];
            try
            {
                for (int i = 0; i < slotAssets.Length; i++)
                {
                    slotAssets[i] = ScriptableObject.CreateInstance<SlotDataAsset>();
                    slotAssets[i]._oldSlotName = "RoundTripMergeTwinSlot" + i;
                }
                var targetSkin = NamedColor("Skin", Color.red);
                var targetSkinAgain = NamedColor("Skin", Color.white);
                var target = new UMAData.UMARecipe
                {
                    slotDataList = new[] { SlotData.CreatePlaceholder("Hold", new string[0]) },
                    sharedColors = new[] { targetSkin, targetSkinAgain },
                };

                var incomingSkin = NamedColor("Skin", Color.blue);
                var twin1 = NamedColor("Twin", Color.green);
                var twin2 = NamedColor("Twin", Color.yellow);
                var incomingSlots = new SlotData[4];
                OverlayColorData[] slotColors = { incomingSkin, twin1, twin2 };
                for (int i = 0; i < 3; i++)
                {
                    incomingSlots[i] = new SlotData(slotAssets[i]);
                    incomingSlots[i].AddOverlay(new OverlayData(overlayAsset) { colorData = slotColors[i] });
                }
                incomingSlots[3] = SlotData.CreatePlaceholder("Hold", new string[0]);
                incomingSlots[3].AddOverlay(new OverlayData(overlayAsset) { colorData = twin2 });
                var source = new UMAData.UMARecipe { slotDataList = incomingSlots, sharedColors = new[] { incomingSkin, twin1, twin2 } };

                target.Merge(source, false);

                Assert.AreEqual(4, target.sharedColors.Length, "Skin, Skin, Twin, Twin");
                Assert.AreEqual("Skin", target.sharedColors[1].name, "a second color with one name was renamed (\"Skin.\")");
                OverlayColorData skinUsed = target.slotDataList[1].GetOverlay(0).colorData;
                OverlayColorData twin1Used = target.slotDataList[2].GetOverlay(0).colorData;
                OverlayColorData twin2Used = target.slotDataList[3].GetOverlay(0).colorData;
                Assert.AreSame(targetSkin, skinUsed, "a color the recipe already has under that name is the one used");
                Assert.AreNotSame(twin1Used, twin2Used, "two shared colors with one name were merged into one");
                Assert.AreEqual(Color.green, twin1Used.channelMask[0]);
                Assert.AreEqual(Color.yellow, twin2Used.channelMask[0]);
                Assert.Contains(twin1Used, target.sharedColors);
                Assert.Contains(twin2Used, target.sharedColors);
                Assert.AreSame(twin2Used, target.slotDataList[0].GetOverlay(0).colorData, "a placeholder's merged overlay lost its shared color");

                target.Merge(new UMAData.UMARecipe { sharedColors = new OverlayColorData[0] }, false);
                CollectionAssert.AreEqual(new[] { "Skin", "Skin", "Twin", "Twin" }, Array.ConvertAll(target.sharedColors, c => c.name));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                foreach (var slotAsset in slotAssets) if (slotAsset != null) UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void ThePositionerFindsItsLayerAfterTwoLayersOfOneOverlaySwapPlaces()
        {
            if (!FindRealSlotAndOverlay(out var slotAsset, out var overlayAsset)) Assert.Ignore("Requires an indexed slot and overlay.");
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            try
            {
                asset.recipeType = "Wardrobe";
                int channels = overlayAsset.material.channels.Length;
                var slot = new SlotData(slotAsset);
                var start = new Rect(0, 0, 64, 64);
                var red = new OverlayData(overlayAsset) { rect = start, colorData = new OverlayColorData(channels) };
                red.colorData.channelMask[0] = Color.red;
                var blue = new OverlayData(overlayAsset) { rect = start, colorData = new OverlayColorData(channels) };
                blue.colorData.channelMask[0] = Color.blue;
                slot.SetOverlayList(new System.Collections.Generic.List<OverlayData> { red, blue });
                asset.Save(new UMAData.UMARecipe { slotDataList = new[] { slot }, sharedColors = new OverlayColorData[0] });

                var working = new UMAData.UMARecipe();
                asset.Load(working);
                OverlayData edited = working.slotDataList[0].GetOverlay(1);
                Assert.AreEqual(Color.blue, edited.colorData.channelMask[0]);
                var editor = new OverlayEditor(working, working.slotDataList[0], edited, null, asset);
                var expected = OverlayEditor.PopupState.Of(edited);

                // Elsewhere, the two layers swap places.
                var outside = new UMAData.UMARecipe();
                asset.Load(outside);
                var layers = outside.slotDataList[0].GetOverlayList();
                layers.Reverse();
                outside.slotDataList[0].SetOverlayList(layers);
                asset.Save(outside);

                edited.rect = new Rect(64, 64, 32, 32);
                var check = new UMAData.UMARecipe();
                asset.Load(check);
                Assert.IsTrue(editor.SameUnwrittenSettings(check.slotDataList[0].GetOverlay(0), OverlayEditor.PopupFields.Rect), "the moved layer must be recognised (else a dialog would open)");
                Assert.IsFalse(editor.SameUnwrittenSettings(check.slotDataList[0].GetOverlay(1), OverlayEditor.PopupFields.Rect));

                Assert.IsTrue(editor.WritePositionIntoCurrentRecipe(OverlayEditor.PopupFields.Rect, expected, null));

                var result = new UMAData.UMARecipe();
                asset.Load(result);
                OverlayData nowFirst = result.slotDataList[0].GetOverlay(0);
                OverlayData nowSecond = result.slotDataList[0].GetOverlay(1);
                Assert.AreEqual(Color.blue, nowFirst.colorData.channelMask[0]);
                Assert.AreEqual(new Rect(64, 64, 32, 32), nowFirst.rect, "the positioned layer");
                Assert.AreEqual(start, nowSecond.rect, "the layer now in the old place is the other one");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

#if UNITY_6000_4_OR_NEWER
        // The wardrobe graph window exists on Unity 6.4 and newer only.
        [Test]
        public void ConnectingAnOverlayKeepsTheTargetSlotsOwnOverlays()
        {
            if (!FindRealSlotAndOverlay(out _, out var overlayAsset)) Assert.Ignore("Requires an indexed wardrobe overlay.");
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe());
            string path = "Assets/GraphConnectTest_" + Guid.NewGuid().ToString("N") + ".asset";
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = false;
                var first = graph.AddPlaceholderSlotAt(Vector2.zero);
                var second = graph.AddPlaceholderSlotAt(new Vector2(0, 300));
                var mine = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 0));
                Assert.IsTrue(graph.ShareOverlayToSlot(mine, first, 0));
                var theirs = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 300));
                Assert.IsTrue(graph.ShareOverlayToSlot(theirs, second, 0));
                // Both slots now hold matching overlays. The file stores them as one stack, but the graph keeps them
                // apart: the second slot's overlay vanished, and editing one slot changed both.
                Assert.AreSame(theirs, second.GetOverlay(0), "the graph's slots kept their own overlay lists");
                Assert.AreNotSame(first.GetOverlayList(), second.GetOverlayList());
                var linked = new UMAData.UMARecipe();
                asset.Load(linked);
                Assert.AreSame(linked.slotDataList[0].GetOverlayList(), linked.slotDataList[1].GetOverlayList(), "the file still stores matching stacks once");
                mine.Rotation = 12f;
                mine.instanceTransformed = true;

                Assert.IsTrue(graph.ShareOverlayToSlot(mine, second, 1));
                Assert.AreEqual(2, second.OverlayCount, "the slot's own overlay was replaced by the other slot's stack");
                Assert.AreSame(theirs, second.GetOverlay(0));
                Assert.AreNotSame(mine, second.GetOverlay(1), "a copy: a recipe can only share whole stacks");
                Assert.AreEqual(12f, second.GetOverlay(1).Rotation);
                Assert.AreSame(mine, first.GetOverlay(0));

                var empty = graph.AddPlaceholderSlotAt(new Vector2(0, 600));
                Assert.IsTrue(graph.ShareOverlayToSlot(mine, empty, 0));
                Assert.AreSame(first.GetOverlayList(), empty.GetOverlayList(), "a slot with no overlays shares the stack");

                var decoded = new UMAData.UMARecipe();
                asset.Load(decoded);
                Assert.AreEqual(1, decoded.slotDataList[0].OverlayCount);
                Assert.AreEqual(2, decoded.slotDataList[1].OverlayCount);
                Assert.AreSame(decoded.slotDataList[0].GetOverlayList(), decoded.slotDataList[2].GetOverlayList());
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                EditorPrefs.DeleteKey("UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path) + "_View");
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void MovingAnOverlayConnectionMovesOnlyThatOverlay()
        {
            if (!FindRealSlotAndOverlay(out _, out var overlayAsset)) Assert.Ignore("Requires an indexed wardrobe overlay.");
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe());
            string path = "Assets/GraphMoveTest_" + Guid.NewGuid().ToString("N") + ".asset";
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = false;
                var source = graph.AddPlaceholderSlotAt(Vector2.zero);
                var target = graph.AddPlaceholderSlotAt(new Vector2(0, 300));
                var moved = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 0));
                var stays = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 100));
                var own = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 300));
                Assert.IsTrue(graph.ShareOverlayToSlot(moved, source, 0));
                Assert.IsTrue(graph.ShareOverlayToSlot(stays, source, 1));
                Assert.IsTrue(graph.ShareOverlayToSlot(own, target, 0));

                Assert.IsTrue(graph.MoveOverlayConnection(source, moved, target, 0, new Vector2(-300, 250)));

                CollectionAssert.AreEqual(new[] { stays }, source.GetOverlayList());
                CollectionAssert.AreEqual(new[] { moved, own }, target.GetOverlayList(), "the target was given the source's whole list in place of its own");
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                EditorPrefs.DeleteKey("UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path) + "_View");
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TypingInAGraphNoteIsOneUndoStepAndWritesOnceWhenItEnds()
        {
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe());
            string path = "Assets/GraphNoteTest_" + Guid.NewGuid().ToString("N") + ".asset";
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = true;
                Undo.IncrementCurrentGroup();
                graph.AddNoteNodeAt(new Vector2(10, 20));
                Undo.FlushUndoRecordObjects();
                string empty = asset.editorGraphData;
                byte[] beforeTyping = File.ReadAllBytes(path);
                var note = graph.Notes[0];

                foreach (string text in new[] { "H", "Hi", "Hi!" })
                {
                    Undo.IncrementCurrentGroup();
                    graph.SetNoteText(note, text);
                    Undo.FlushUndoRecordObjects();
                }
                StringAssert.Contains("Hi!", asset.editorGraphData);
                CollectionAssert.AreEqual(beforeTyping, File.ReadAllBytes(path), "each keystroke wrote the file");

                graph.FinishNoteTyping();
                StringAssert.Contains("Hi!", File.ReadAllText(path), "ending the typing writes it");

                Undo.PerformUndo();
                Assert.AreEqual(empty, asset.editorGraphData, "one Undo takes back the whole typing, not one character");
                Undo.PerformRedo();
                StringAssert.Contains("Hi!", asset.editorGraphData);
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                EditorPrefs.DeleteKey("UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path) + "_View");
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void UndoKeysWhileTypingInAGraphNoteUndoTheTyping()
        {
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe());
            string path = "Assets/GraphNoteKeysTest_" + Guid.NewGuid().ToString("N") + ".asset";
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = true;
                Undo.IncrementCurrentGroup();
                graph.AddNoteNodeAt(new Vector2(10, 20));
                Undo.FlushUndoRecordObjects();
                string empty = asset.editorGraphData;
                var note = graph.Notes[0];
                foreach (string text in new[] { "A", "A s", "A sentence." })
                {
                    Undo.IncrementCurrentGroup();
                    graph.SetNoteText(note, text);
                    Undo.FlushUndoRecordObjects();
                }

                const string noteControl = "UMAWardrobeGraphNoteText";
                var ctrlZ = new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, control = true };
                Assert.IsFalse(graph.HandleNoteUndoShortcut(ctrlZ, "SomeOtherField"), "only while a note's text has focus");
                Assert.IsFalse(graph.HandleNoteUndoShortcut(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z }, noteControl), "a plain Z is typing");

                Assert.IsTrue(graph.HandleNoteUndoShortcut(ctrlZ, noteControl));
                Assert.AreEqual(EventType.Used, ctrlZ.type);
                Assert.AreEqual(empty, asset.editorGraphData, "Ctrl+Z in the note's text undid nothing");
                Assert.AreEqual(0, GUIUtility.keyboardControl, "the text lets go, so it shows the restored note");
                StringAssert.DoesNotContain("A sentence.", File.ReadAllText(path), "Auto Save writes the undone state");

                Assert.IsTrue(graph.HandleNoteUndoShortcut(new Event { type = EventType.KeyDown, keyCode = KeyCode.Y, control = true }, noteControl));
                StringAssert.Contains("A sentence.", asset.editorGraphData);
                Assert.IsTrue(graph.HandleNoteUndoShortcut(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, control = true, shift = true }, noteControl), "Ctrl+Shift+Z is Redo too");
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                EditorPrefs.DeleteKey("UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path) + "_View");
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ASlotSharingAStackCanBeGivenItsOwnOverlays()
        {
            if (!FindRealSlotAndOverlay(out _, out var overlayAsset)) Assert.Ignore("Requires an indexed wardrobe overlay.");
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe());
            string path = "Assets/GraphUnshareTest_" + Guid.NewGuid().ToString("N") + ".asset";
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = false;
                var first = graph.AddPlaceholderSlotAt(Vector2.zero);
                var second = graph.AddPlaceholderSlotAt(new Vector2(0, 300));
                var layer = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 0));
                Assert.IsTrue(graph.ShareOverlayToSlot(layer, first, 0));
                Assert.IsTrue(graph.ShareOverlayToSlot(layer, second, 0));
                Assert.AreSame(first.GetOverlayList(), second.GetOverlayList());

                Assert.IsTrue(graph.GiveSlotItsOwnOverlays(second));
                Assert.AreNotSame(first.GetOverlayList(), second.GetOverlayList());
                Assert.AreNotSame(layer, second.GetOverlay(0));
                Assert.IsFalse(graph.GiveSlotItsOwnOverlays(second), "it has its own already");

                second.GetOverlay(0).Rotation = 45f;
                second.GetOverlay(0).instanceTransformed = true;
                Assert.IsTrue(graph.ShareOverlayToSlot(graph.AddDetachedOverlayNode(overlayAsset, new Vector2(-300, 600)), second, 1));
                var decoded = new UMAData.UMARecipe();
                asset.Load(decoded);
                Assert.AreNotSame(decoded.slotDataList[0].GetOverlayList(), decoded.slotDataList[1].GetOverlayList());
                Assert.AreEqual(0f, decoded.slotDataList[0].GetOverlay(0).Rotation, "editing the second slot changed the first");
                Assert.AreEqual(45f, decoded.slotDataList[1].GetOverlay(0).Rotation);
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                EditorPrefs.DeleteKey("UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path) + "_View");
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AuthoredGraphTravelsWithTheAssetAndDetachedLayersStayOutOfTheRuntimeRecipe()
        {
            if (!FindRealSlotAndOverlay(out _, out var overlayAsset)) Assert.Ignore("Requires an indexed wardrobe overlay.");
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe { sharedColors = new[] { new OverlayColorData(overlayAsset.material.channels.Length) { name = "Fabric" } } });
            string path = "Assets/GraphLayoutTest_" + Guid.NewGuid().ToString("N") + ".asset";
            string copyPath = path.Replace(".asset", "_Copy.asset");
            string prefs = null;
            string copyPrefs = null;
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                prefs = "UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path);
                EditorPrefs.SetString(prefs, "{\"keys\":[\"Output\"],\"positions\":[{\"x\":123.0,\"y\":456.0}],\"notes\":[{\"id\":\"legacy\",\"text\":\"Tailoring note\",\"rect\":{\"x\":2,\"y\":3,\"width\":200,\"height\":100}}],\"zoom\":1.5}");
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = false;
                Assert.IsTrue(string.IsNullOrEmpty(asset.editorGraphData));
                graph.SaveAssetToDisk(importLegacyLayout: true);
                StringAssert.Contains("Tailoring note", asset.editorGraphData);
                StringAssert.Contains("123", asset.editorGraphData);
                StringAssert.DoesNotContain("zoom", asset.editorGraphData);
                byte[] saved = File.ReadAllBytes(path);
                string migrated = asset.editorGraphData;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                graph.AddNoteNodeAt(new Vector2(94, 185));
                Undo.FlushUndoRecordObjects();
                string withNote = asset.editorGraphData;
                Assert.AreNotEqual(migrated, withNote);
                Undo.PerformUndo();
                Assert.AreEqual(migrated, asset.editorGraphData);
                Undo.PerformRedo();
                Assert.AreEqual(withNote, asset.editorGraphData);

                Undo.IncrementCurrentGroup();
                var layer = graph.AddDetachedOverlayNode(overlayAsset, new Vector2(731, 542));
                Assert.IsNotNull(layer);
                Undo.FlushUndoRecordObjects();
                var owner = (CharacterBaseEditor.IRecipeWorkingCopyOwner)graph;
                layer.instanceTransformed = true;
                layer.Rotation = 27;
                layer.rect = new Rect(11, 22, 33, 44);
                layer.tags = new[] { "Layer tag" };
                layer.colorData = owner.WorkingCopy.sharedColors[0];
                Assert.IsTrue(CharacterBaseEditor.WriteWorkingCopy(asset, owner.WorkingCopy, "Edit Detached Test Layer"));
                CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));
                var copy = (UMAWardrobeRecipe)graph.CreateRecipeCopy(copyPath);
                copyPrefs = "UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(copyPath);
                string authored = asset.editorGraphData;
                StringAssert.Contains("731", authored);
                AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceUpdate);
                graph.LoadRecipe(AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(copyPath));
                Assert.AreEqual(authored, copy.editorGraphData);
                Assert.IsEmpty(owner.WorkingCopy.slotDataList);
                Assert.AreEqual(1, graph.DetachedOverlays.Count);
                var restored = graph.DetachedOverlays[0];
                Assert.AreSame(overlayAsset, restored.asset);
                Assert.AreEqual(27, restored.Rotation);
                Assert.AreEqual(layer.rect, restored.rect);
                CollectionAssert.AreEqual(layer.tags, restored.tags);
                Assert.AreSame(owner.WorkingCopy.sharedColors[0], restored.colorData);
                CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));

                var target = graph.AddPlaceholderSlotAt(Vector2.zero);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                Assert.IsTrue(graph.ShareOverlayToSlot(restored, target, 0));
                Undo.FlushUndoRecordObjects();
                var runtimeCopy = new UMAData.UMARecipe();
                copy.Load(runtimeCopy);
                Assert.AreEqual(27, runtimeCopy.slotDataList[0].GetOverlay(0).Rotation);
                Assert.IsEmpty(graph.DetachedOverlays);
                Undo.PerformUndo();
                Assert.IsEmpty(owner.WorkingCopy.slotDataList[0].GetOverlayList());
                Assert.AreEqual(27, graph.DetachedOverlays[0].Rotation);
                Undo.PerformRedo();
                Assert.IsEmpty(graph.DetachedOverlays);
                Assert.AreEqual(27, owner.WorkingCopy.slotDataList[0].GetOverlay(0).Rotation);
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                if (prefs != null) { EditorPrefs.DeleteKey(prefs); EditorPrefs.DeleteKey(prefs + "_View"); }
                if (copyPrefs != null) EditorPrefs.DeleteKey(copyPrefs + "_View");
                AssetDatabase.DeleteAsset(copyPath);
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GraphEditsUseAssetUndoAndOnlyAutoSaveControlsDiskWrites(bool autoSave)
        {
            var asset = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            asset.recipeType = "Wardrobe";
            asset.Save(new UMAData.UMARecipe());
            asset.compatibleRaces = new System.Collections.Generic.List<string> { "GraphTestRace" };
            string path = "Assets/GraphUndoTest_" + Guid.NewGuid().ToString("N") + ".asset";
            UMAWardrobeRecipeGraphEditorWindow graph = null;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                byte[] originalFile = File.ReadAllBytes(path);
                graph = ScriptableObject.CreateInstance<UMAWardrobeRecipeGraphEditorWindow>();
                graph.LoadRecipe(asset);
                graph.AutoSave = autoSave;

                Undo.IncrementCurrentGroup();
                graph.AddPlaceholderSlotAt(Vector2.zero);
                Undo.FlushUndoRecordObjects();
                var decoded = new UMAData.UMARecipe();
                asset.Load(decoded);
                Assert.AreEqual(1, decoded.slotDataList.Length, "The asset must contain the edit immediately.");
                byte[] afterSlot = File.ReadAllBytes(path);
                if (autoSave) CollectionAssert.AreNotEqual(originalFile, afterSlot);
                else CollectionAssert.AreEqual(originalFile, afterSlot);

                string packed = asset.recipeString;
                Undo.IncrementCurrentGroup();
                graph.RemoveCompatibleRaceAt(0);
                Undo.FlushUndoRecordObjects();
                Assert.IsEmpty(asset.compatibleRaces);
                Assert.AreEqual(packed, asset.recipeString, "Metadata edits must preserve the packed text.");
                byte[] afterBothEdits = File.ReadAllBytes(path);
                if (autoSave) CollectionAssert.AreNotEqual(afterSlot, afterBothEdits);
                else CollectionAssert.AreEqual(originalFile, afterBothEdits);

                graph.LoadRecipe(null);
                graph.LoadRecipe(asset);
                Assert.AreEqual(1, ((CharacterBaseEditor.IRecipeWorkingCopyOwner)graph).WorkingCopy.slotDataList.Length);
                Assert.IsEmpty(asset.compatibleRaces);
                CollectionAssert.AreEqual(afterBothEdits, File.ReadAllBytes(path), "Switching recipes must not flush edits.");
                graph.SaveAssetToDisk();
                byte[] saved = File.ReadAllBytes(path);
                CollectionAssert.AreNotEqual(originalFile, saved);
                Assert.IsFalse(EditorUtility.IsDirty(asset));

                Undo.PerformUndo();
                CollectionAssert.AreEqual(new[] { "GraphTestRace" }, asset.compatibleRaces);
                Assert.AreEqual(packed, asset.recipeString);
                if (autoSave) CollectionAssert.AreEqual(afterSlot, File.ReadAllBytes(path));
                else CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));
                Undo.PerformUndo();
                Assert.IsEmpty(((CharacterBaseEditor.IRecipeWorkingCopyOwner)graph).WorkingCopy.slotDataList);
                Undo.PerformRedo();
                Assert.AreEqual(1, ((CharacterBaseEditor.IRecipeWorkingCopyOwner)graph).WorkingCopy.slotDataList.Length);
                Assert.AreEqual(packed, asset.recipeString);
            }
            finally
            {
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
                Undo.ClearUndo(asset);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) EditorPrefs.DeleteKey("UMA_WardrobeGraphLayout_" + guid + "_View");
                if (!AssetDatabase.DeleteAsset(path) && asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }
#endif
    }
}
