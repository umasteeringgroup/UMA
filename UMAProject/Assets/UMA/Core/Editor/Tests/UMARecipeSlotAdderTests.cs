using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using PackedColor = UMA.UMAPackedRecipeBase.PackedOverlayColorDataV3;
using PackedOverlay = UMA.UMAPackedRecipeBase.PackedOverlayDataV3;
using PackedRecipe = UMA.UMAPackedRecipeBase.UMAPackRecipe;
using PackedSlot = UMA.UMAPackedRecipeBase.PackedSlotDataV3;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Assets > UMA > Add a slot to selected recipes: the slot lands in the recipe's stored data with
    /// its matching criteria, overlays and shared colors, and what the recipe already held is kept.
    /// </summary>
    public sealed class UMARecipeSlotAdderTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            asset.hideFlags = HideFlags.HideAndDontSave;
            created.Add(asset);
            return asset;
        }

        private SlotDataAsset Slot(string slotName, params string[] tags)
        {
            // A slot's name is its asset's name unless an older name was saved with it.
            var slot = Make<SlotDataAsset>(slotName);
            slot.tags = tags;
            return slot;
        }

        private OverlayDataAsset Overlay(string overlayName)
        {
            return Make<OverlayDataAsset>(overlayName);
        }

        private static PackedColor Color(string name, params short[] values)
        {
            return new PackedColor { name = name, colors = values };
        }

        private static PackedOverlay PackedOverlayOf(string id, int color)
        {
            return new PackedOverlay { id = id, colorIdx = color, rect = new float[4], blendModes = new int[0], Tags = new string[0], tiling = new bool[0] };
        }

        /// <summary>
        /// A recipe as it is stored: a shared color "Skin", one unshared color, and two slots that no
        /// library knows, the second wearing one overlay on each of the two colors.
        /// </summary>
        private UMAWardrobeRecipe StoredRecipe(string name)
        {
            var packed = new PackedRecipe
            {
                version = 3,
                sharedColorCount = 1,
                fColors = new[]
                {
                    Color("Skin", 10, 20, 30, 255, 0, 0, 0, 0),
                    Color(OverlayColorData.UNSHARED, 200, 100, 50, 255, 0, 0, 0, 0),
                },
                slotsV3 = new[]
                {
                    new PackedSlot { id = "A slot no library has", Tags = new[] { "kept" }, Races = new string[0], overlays = new PackedOverlay[0] },
                    new PackedSlot
                    {
                        id = "Another slot no library has", Tags = new string[0], Races = new[] { "SomeRace" },
                        overlays = new[] { PackedOverlayOf("An overlay no library has", 1), PackedOverlayOf("One that follows the skin", 0) },
                    },
                },
            };
            var recipe = Make<UMAWardrobeRecipe>(name);
            recipe.recipeString = JsonUtility.ToJson(packed);
            return recipe;
        }

        private static PackedRecipe Stored(UMATextRecipe recipe) => JsonUtility.FromJson<PackedRecipe>(recipe.recipeString);

        [Test]
        public void TheSlotIsAddedWithItsCriteriaAndEverythingElseStaysAsItWas()
        {
            UMAWardrobeRecipe recipe = StoredRecipe("criteria");
            PackedRecipe before = Stored(recipe);
            var addition = new UMARecipeSlotAddition
            {
                slot = Slot("Added Slot", "fromTheAsset"),
                tags = new List<string> { "Hat", " Hat ", " ", "Helmet" },
                races = new List<string> { "HumanMale" },
                isSwapSlot = true,
                swapTag = " LongHair ",
            };

            Assert.AreEqual(UMARecipeSlotAddOutcome.Added, UMARecipeSlotAdder.AddToRecipe(recipe, addition, out string note), note);
            PackedRecipe after = Stored(recipe);

            Assert.AreEqual(3, after.version);
            Assert.AreEqual(3, after.slotsV3.Length, "the new slot is added after the ones the recipe had");
            for (int i = 0; i < before.slotsV3.Length; i++)
            {
                Assert.AreEqual(JsonUtility.ToJson(before.slotsV3[i]), JsonUtility.ToJson(after.slotsV3[i]), "a slot no library knows is kept exactly as it was");
            }
            Assert.AreEqual(before.sharedColorCount, after.sharedColorCount);
            Assert.AreEqual(JsonUtility.ToJson(before.fColors[0]), JsonUtility.ToJson(after.fColors[0]));
            Assert.AreEqual(JsonUtility.ToJson(before.fColors[1]), JsonUtility.ToJson(after.fColors[1]));
            Assert.AreEqual(2, after.fColors.Length, "no overlays were asked for, so no colors are added");

            PackedSlot added = after.slotsV3[2];
            Assert.AreEqual("Added Slot", added.id);
            Assert.IsFalse(added.isPlaceholderSlot);
            CollectionAssert.AreEqual(new[] { "Hat", "Helmet" }, added.Tags, "the tags given, trimmed and once each; not the asset's own");
            CollectionAssert.AreEqual(new[] { "HumanMale" }, added.Races);
            Assert.IsTrue(added.isSwapSlot);
            Assert.AreEqual("LongHair", added.swapTag);
            Assert.AreEqual(-1, added.copyIdx);
            Assert.That(added.overlays == null || added.overlays.Length == 0);

            // A second time: the recipe has the slot now and is left alone.
            string stored = recipe.recipeString;
            Assert.AreEqual(UMARecipeSlotAddOutcome.AlreadyHasSlot, UMARecipeSlotAdder.AddToRecipe(recipe, addition, out note));
            StringAssert.Contains("Added Slot", note);
            StringAssert.Contains("criteria", note, "the note names the recipe");
            Assert.AreSame(stored, recipe.recipeString);

            // Without the swap box ticked no swap tag is stored.
            UMAWardrobeRecipe plain = StoredRecipe("plain");
            addition.isSwapSlot = false;
            Assert.AreEqual(UMARecipeSlotAddOutcome.Added, UMARecipeSlotAdder.AddToRecipe(plain, addition, out note), note);
            Assert.IsFalse(Stored(plain).slotsV3[2].isSwapSlot);
            Assert.That(Stored(plain).slotsV3[2].swapTag, Is.Null.Or.Empty);
        }

        [Test]
        public void OverlaysAndSharedColorsAreNumberedIntoTheRecipesColors()
        {
            UMAWardrobeRecipe recipe = StoredRecipe("colors");
            var addition = new UMARecipeSlotAddition
            {
                slot = Slot("Added Slot"),
                overlays = new List<UMARecipeOverlayAddition>
                {
                    new UMARecipeOverlayAddition { overlay = Overlay("Follows Skin"), sharedColorName = "Skin" },
                    new UMARecipeOverlayAddition { overlay = Overlay("Follows Trim"), sharedColorName = " Trim " },
                    new UMARecipeOverlayAddition { overlay = null },   // a row left empty
                    new UMARecipeOverlayAddition { overlay = Overlay("Own Color"), color = new Color(1f, 0f, 0f, 1f) },
                },
                sharedColors = new List<UMARecipeSharedColorAddition>
                {
                    new UMARecipeSharedColorAddition { name = "Trim", color = new Color(0f, 0f, 1f, 1f) },
                    new UMARecipeSharedColorAddition { name = "  " },   // a row left empty
                    new UMARecipeSharedColorAddition { name = "Unused", color = new Color(0f, 1f, 0f, 1f) },
                },
            };

            Assert.AreEqual(UMARecipeSlotAddOutcome.Added, UMARecipeSlotAdder.AddToRecipe(recipe, addition, out string note), note);
            PackedRecipe after = Stored(recipe);

            // Shared colors first: the recipe's own Skin, then the two new ones; then the unshared ones.
            Assert.AreEqual(3, after.sharedColorCount);
            Assert.AreEqual(5, after.fColors.Length);
            Assert.AreEqual("Skin", after.fColors[0].name);
            CollectionAssert.AreEqual(new short[] { 10, 20, 30, 255, 0, 0, 0, 0 }, after.fColors[0].colors, "a recipe that has the shared color keeps its own values");
            Assert.AreEqual("Trim", after.fColors[1].name);
            CollectionAssert.AreEqual(new short[] { 0, 0, 255, 255 }, new[] { after.fColors[1].colors[0], after.fColors[1].colors[1], after.fColors[1].colors[2], after.fColors[1].colors[3] });
            Assert.AreEqual("Unused", after.fColors[2].name, "a shared color no overlay follows is added all the same");
            Assert.AreEqual(OverlayColorData.UNSHARED, after.fColors[3].name);
            CollectionAssert.AreEqual(new short[] { 200, 100, 50, 255, 0, 0, 0, 0 }, after.fColors[3].colors, "the recipe's unshared color moved up, unchanged");
            Assert.AreEqual(OverlayColorData.UNSHARED, after.fColors[4].name);
            CollectionAssert.AreEqual(new short[] { 255, 0, 0, 255 }, new[] { after.fColors[4].colors[0], after.fColors[4].colors[1], after.fColors[4].colors[2], after.fColors[4].colors[3] });

            // The overlays the recipe had still point at the colors they had.
            Assert.AreEqual(3, after.slotsV3[1].overlays[0].colorIdx, "it followed the unshared color, which is now number 3");
            Assert.AreEqual(0, after.slotsV3[1].overlays[1].colorIdx);
            Assert.AreEqual("An overlay no library has", after.slotsV3[1].overlays[0].id);

            // The new slot's overlays, in the order given, the empty row skipped.
            PackedSlot added = after.slotsV3[2];
            Assert.AreEqual(3, added.overlays.Length);
            Assert.AreEqual("Follows Skin", added.overlays[0].id);
            Assert.AreEqual(0, added.overlays[0].colorIdx);
            Assert.AreEqual("Follows Trim", added.overlays[1].id);
            Assert.AreEqual(1, added.overlays[1].colorIdx);
            Assert.AreEqual("Own Color", added.overlays[2].id);
            Assert.AreEqual(4, added.overlays[2].colorIdx);
        }

        [Test]
        public void OnlyRecipesThatHoldSlotsAreTaken()
        {
            var addition = new UMARecipeSlotAddition { slot = Slot("Added Slot") };

            var collection = Make<UMAWardrobeCollection>("a collection");
            Assert.IsFalse(UMARecipeSlotAdder.Supports(collection, out string reason));
            StringAssert.Contains("UMAWardrobeCollection", reason);
            string storedBefore = collection.recipeString;
            Assert.AreEqual(UMARecipeSlotAddOutcome.NotSupported, UMARecipeSlotAdder.AddToRecipe(collection, addition, out _));
            Assert.AreEqual(storedBefore, collection.recipeString);

            // A text recipe that holds a saved character keeps data a slot list has no room for.
            var character = Make<UMATextRecipe>("a saved character");
            character.recipeType = "DynamicCharacterAvatar";
            character.recipeString = "{\"packedRecipeType\":\"DynamicCharacterAvatar\",\"wardrobeSet\":[]}";
            Assert.AreEqual(UMARecipeSlotAddOutcome.NotSupported, UMARecipeSlotAdder.AddToRecipe(character, addition, out _));
            StringAssert.Contains("wardrobeSet", character.recipeString);
            Assert.IsFalse(UMARecipeSlotAdder.Supports(null, out _));

            // The older stored formats are refused with a way forward, not rewritten.
            var old = Make<UMATextRecipe>("an old recipe");
            old.recipeString = JsonUtility.ToJson(new PackedRecipe { version = 2, slotsV2 = new[] { new UMAPackedRecipeBase.PackedSlotDataV2 { id = "old" } } });
            string oldStored = old.recipeString;
            Assert.AreEqual(UMARecipeSlotAddOutcome.NotSupported, UMARecipeSlotAdder.AddToRecipe(old, addition, out string note));
            StringAssert.Contains("version 2", note);
            Assert.AreSame(oldStored, old.recipeString);

            // A new, empty recipe takes the slot, as a plain text recipe or a wardrobe recipe.
            var empty = Make<UMATextRecipe>("an empty recipe");
            Assert.IsTrue(UMARecipeSlotAdder.Supports(empty, out _));
            Assert.AreEqual(UMARecipeSlotAddOutcome.Added, UMARecipeSlotAdder.AddToRecipe(empty, addition, out note), note);
            Assert.AreEqual(3, Stored(empty).version);
            Assert.AreEqual(1, Stored(empty).slotsV3.Length);
            Assert.AreEqual("Added Slot", Stored(empty).slotsV3[0].id);
        }

        [Test]
        public void AnAdditionIsCheckedBeforeAnythingIsChanged()
        {
            Assert.IsFalse(UMARecipeSlotAdder.Validate(null, out string problem));
            Assert.IsFalse(UMARecipeSlotAdder.Validate(new UMARecipeSlotAddition(), out problem));
            StringAssert.Contains("slot", problem);

            var addition = new UMARecipeSlotAddition { slot = Slot("Added Slot") };
            Assert.IsTrue(UMARecipeSlotAdder.Validate(addition, out problem), problem);

            addition.isSwapSlot = true;
            Assert.IsFalse(UMARecipeSlotAdder.Validate(addition, out problem));
            StringAssert.Contains("swap", problem);
            addition.swapTag = "LongHair";
            Assert.IsTrue(UMARecipeSlotAdder.Validate(addition, out problem), problem);

            addition.sharedColors.Add(new UMARecipeSharedColorAddition { name = "Trim" });
            addition.sharedColors.Add(new UMARecipeSharedColorAddition { name = "Trim" });
            Assert.IsFalse(UMARecipeSlotAdder.Validate(addition, out problem));
            StringAssert.Contains("twice", problem);
            addition.sharedColors[1].name = OverlayColorData.UNSHARED;
            Assert.IsFalse(UMARecipeSlotAdder.Validate(addition, out problem));
            addition.sharedColors[1].name = string.Empty;   // an empty row is skipped, not a problem
            Assert.IsTrue(UMARecipeSlotAdder.Validate(addition, out problem), problem);

            var unnamed = Make<SlotDataAsset>(string.Empty);
            Assert.IsFalse(UMARecipeSlotAdder.Validate(new UMARecipeSlotAddition { slot = unnamed }, out problem));

            // Nothing is changed when the addition is refused.
            UMAWardrobeRecipe recipe = StoredRecipe("refused");
            string stored = recipe.recipeString;
            addition.sharedColors[1].name = "Trim";
            Assert.Throws<System.ArgumentException>(() => UMARecipeSlotAdder.AddToRecipes(new UMATextRecipe[] { recipe }, addition));
            Assert.AreSame(stored, recipe.recipeString);
        }

        [Test]
        public void SeveralRecipesAreDoneInOneGoAndTheRestIsReported()
        {
            var addition = new UMARecipeSlotAddition { slot = Slot("Added Slot"), tags = new List<string> { "Hat" } };
            UMAWardrobeRecipe first = StoredRecipe("first");
            UMAWardrobeRecipe second = StoredRecipe("second");
            UMAWardrobeRecipe hasIt = StoredRecipe("has it");
            Assert.AreEqual(UMARecipeSlotAddOutcome.Added, UMARecipeSlotAdder.AddToRecipe(hasIt, addition, out _));
            string hasItStored = hasIt.recipeString;
            var collection = Make<UMAWardrobeCollection>("a collection");

            UMARecipeSlotAddSummary summary = UMARecipeSlotAdder.AddToRecipes(new UMATextRecipe[] { first, hasIt, null, second, first, collection }, addition);

            Assert.AreEqual(2, summary.Added, "each recipe once, however often it is listed");
            Assert.AreEqual(1, summary.AlreadyHadSlot);
            Assert.AreEqual(1, summary.NotSupported);
            Assert.AreEqual(2, summary.Notes.Count, "one line for every recipe that was not changed");
            Assert.AreEqual(3, Stored(first).slotsV3.Length);
            Assert.AreEqual(3, Stored(second).slotsV3.Length);
            Assert.AreSame(hasItStored, hasIt.recipeString);
            Assert.AreEqual(0, UMARecipeSlotAdder.AddToRecipes(null, addition).Added);
        }

        [Test]
        public void TheAddedSlotLoadsThroughUMAWithItsOverlayAndSharedColor()
        {
            // Real assets from the Global Library, so UMA's own loader can find them again.
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            Assert.IsNotNull(indexer, "the UMA Global Library");
            SlotDataAsset slot = null;
            OverlayDataAsset overlay = null;
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
            if (slot == null || overlay == null)
            {
                Assert.Ignore("The Global Library holds no slot with a mesh, or no overlay with a material.");
            }

            var recipe = Make<UMAWardrobeRecipe>("loads");
            var addition = new UMARecipeSlotAddition
            {
                slot = slot,
                tags = new List<string> { "AddedByTest" },
                races = new List<string> { "A Race For The Test" },
                overlays = new List<UMARecipeOverlayAddition> { new UMARecipeOverlayAddition { overlay = overlay, sharedColorName = "Trim" } },
                sharedColors = new List<UMARecipeSharedColorAddition> { new UMARecipeSharedColorAddition { name = "Trim", color = new Color(0f, 0f, 1f, 1f) } },
            };
            Assert.AreEqual(UMARecipeSlotAddOutcome.Added, UMARecipeSlotAdder.AddToRecipe(recipe, addition, out string note), note);

            var loaded = new UMAData.UMARecipe();
            recipe.Load(loaded);
            Assert.AreEqual(1, loaded.slotDataList.Length);
            SlotData loadedSlot = loaded.slotDataList[0];
            Assert.IsNotNull(loadedSlot, $"UMA finds the slot '{slot.slotName}' again");
            Assert.AreEqual(slot.slotName, loadedSlot.asset.slotName);
            CollectionAssert.AreEqual(new[] { "AddedByTest" }, loadedSlot.tags);
            CollectionAssert.AreEqual(new[] { "A Race For The Test" }, loadedSlot.Races);
            Assert.AreEqual(1, loadedSlot.OverlayCount);
            OverlayData loadedOverlay = loadedSlot.GetOverlay(0);
            Assert.AreEqual(overlay.overlayName, loadedOverlay.asset.overlayName);
            Assert.AreEqual(1, loaded.sharedColors.Length);
            Assert.AreEqual("Trim", loaded.sharedColors[0].name);
            Assert.AreSame(loaded.sharedColors[0], loadedOverlay.colorData, "the overlay follows the shared color");
            Assert.That(loaded.sharedColors[0].channelCount, Is.GreaterThanOrEqualTo(overlay.material.channels.Length), "the shared color has a value for every channel of the overlay's material");
            Color trim = loaded.sharedColors[0].color;
            Assert.That(new Vector3(trim.r, trim.g, trim.b), Is.EqualTo(new Vector3(0f, 0f, 1f)));
        }
    }
}
