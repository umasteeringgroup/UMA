using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using PackedColor = UMA.UMAPackedRecipeBase.PackedOverlayColorDataV3;
using PackedRecipe = UMA.UMAPackedRecipeBase.UMAPackRecipe;
using PackedSlot = UMA.UMAPackedRecipeBase.PackedSlotDataV3;

namespace UMA.Editors
{
    /// <summary>An overlay to put on a slot that is being added to recipes.</summary>
    [Serializable]
    public sealed class UMARecipeOverlayAddition
    {
        public OverlayDataAsset overlay;

        [Tooltip("Name of the shared color the overlay follows. Empty: the overlay has a color of its own.")]
        public string sharedColorName = string.Empty;

        [Tooltip("The overlay's own color. Used when it follows no shared color.")]
        public Color color = Color.white;
    }

    /// <summary>A shared color to add to recipes. A recipe that already has one of this name keeps its own.</summary>
    [Serializable]
    public sealed class UMARecipeSharedColorAddition
    {
        public string name = string.Empty;
        public Color color = Color.white;
    }

    /// <summary>A slot to add to recipes, with what the recipe says about it: when it applies, and what it wears.</summary>
    [Serializable]
    public sealed class UMARecipeSlotAddition
    {
        public SlotDataAsset slot;

        [Tooltip("The slot's tags in the recipe. For a wildcard slot these are the tags it matches.")]
        public List<string> tags = new List<string>();

        [Tooltip("Race names the slot is added for. Empty: every race.")]
        public List<string> races = new List<string>();

        [Tooltip("A swap slot is only added when the character already has a slot with the swap tag, which it replaces.")]
        public bool isSwapSlot;

        public string swapTag = string.Empty;

        public List<UMARecipeOverlayAddition> overlays = new List<UMARecipeOverlayAddition>();

        public List<UMARecipeSharedColorAddition> sharedColors = new List<UMARecipeSharedColorAddition>();
    }

    public enum UMARecipeSlotAddOutcome
    {
        Added,
        /// <summary>The recipe already holds a slot of that name and was left as it is.</summary>
        AlreadyHasSlot,
        /// <summary>The recipe is of a kind, or in a format, this cannot add to. It was left as it is.</summary>
        NotSupported,
    }

    /// <summary>What adding a slot to a set of recipes did.</summary>
    public sealed class UMARecipeSlotAddSummary
    {
        public int Added;
        public int AlreadyHadSlot;
        public int NotSupported;
        /// <summary>One line for every recipe that was not changed, saying why.</summary>
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Adds a slot, with its matching criteria and optionally overlays and shared colors, to recipes.
    /// </summary>
    /// <remarks>
    /// The recipe's stored data is changed in place rather than loaded, edited and saved again.
    /// Loading a recipe drops every slot and overlay it cannot find in the Global Library, so a load
    /// and save round trip over many recipes could quietly lose content; working on the stored data
    /// keeps everything the recipe already has exactly as it is. The new slot itself is packed by
    /// UMA's own packer, so it is stored the way the recipe editor would store it.
    /// </remarks>
    public static class UMARecipeSlotAdder
    {
        private const int FallbackChannels = 3;   // what UMA assumes for an overlay whose material it cannot find

        /// <summary>True when the recipe is one this can add a slot to. Otherwise <paramref name="reason"/> says why not.</summary>
        public static bool Supports(UMATextRecipe recipe, out string reason)
        {
            reason = null;
            if (recipe == null)
            {
                reason = "No recipe.";
                return false;
            }
            Type type = recipe.GetType();
            if (type != typeof(UMATextRecipe) && type != typeof(UMAWardrobeRecipe))
            {
                reason = $"'{recipe.name}' is a {type.Name}, which does not hold slots of its own.";
                return false;
            }
            // A plain text recipe can hold another kind of recipe as text, with data of its own that
            // rewriting it as slots would lose.
            if (!string.IsNullOrEmpty(recipe.recipeType) && recipe.recipeType != "Standard" && recipe.recipeType != "Wardrobe")
            {
                reason = $"'{recipe.name}' holds a {recipe.recipeType} recipe, which does not hold slots of its own.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Checks that an addition can be made at all. Rows of the overlay and shared color lists that
        /// were left empty are not a problem; they are skipped.
        /// </summary>
        public static bool Validate(UMARecipeSlotAddition addition, out string problem)
        {
            problem = null;
            if (addition == null || addition.slot == null)
            {
                problem = "Choose the slot to add.";
                return false;
            }
            if (string.IsNullOrEmpty(addition.slot.slotName))
            {
                problem = $"The slot asset '{addition.slot.name}' has no slot name.";
                return false;
            }
            if (addition.isSwapSlot && string.IsNullOrWhiteSpace(addition.swapTag))
            {
                problem = "A swap slot needs the tag of the slot it replaces.";
                return false;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (addition.sharedColors != null)
            {
                foreach (UMARecipeSharedColorAddition shared in addition.sharedColors)
                {
                    if (shared == null || string.IsNullOrWhiteSpace(shared.name))
                    {
                        continue;
                    }
                    if (shared.name == OverlayColorData.UNSHARED)
                    {
                        problem = $"'{OverlayColorData.UNSHARED}' is the name UMA gives colors that are not shared; give the shared color another name.";
                        return false;
                    }
                    if (!names.Add(shared.name))
                    {
                        problem = $"The shared color '{shared.name}' is listed twice.";
                        return false;
                    }
                }
            }
            if (addition.overlays != null)
            {
                foreach (UMARecipeOverlayAddition overlay in addition.overlays)
                {
                    if (overlay != null && overlay.overlay != null && string.IsNullOrEmpty(overlay.overlay.overlayName))
                    {
                        problem = $"The overlay asset '{overlay.overlay.name}' has no overlay name.";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Adds the slot to every recipe that can take it, as one undo step, and saves the recipes it
        /// changed. Recipes that already have the slot are left alone.
        /// </summary>
        public static UMARecipeSlotAddSummary AddToRecipes(IEnumerable<UMATextRecipe> recipes, UMARecipeSlotAddition addition)
        {
            var summary = new UMARecipeSlotAddSummary();
            if (!Validate(addition, out string problem))
            {
                throw new ArgumentException(problem, nameof(addition));
            }
            if (recipes == null)
            {
                return summary;
            }
            var seen = new HashSet<UMATextRecipe>();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Slot to Recipes");
            try
            {
                foreach (UMATextRecipe recipe in recipes)
                {
                    if (recipe == null || !seen.Add(recipe))
                    {
                        continue;
                    }
                    Undo.RecordObject(recipe, "Add Slot to Recipes");
                    switch (AddToRecipe(recipe, addition, out string note))
                    {
                        case UMARecipeSlotAddOutcome.Added:
                            summary.Added++;
                            EditorUtility.SetDirty(recipe);
                            if (EditorUtility.IsPersistent(recipe))
                            {
                                AssetDatabase.SaveAssetIfDirty(recipe);
                            }
                            // What the recipe editor does after a change: characters in the scene that wear the recipe are rebuilt.
                            UMAUpdateProcessor.UpdateRecipe(recipe);
                            break;
                        case UMARecipeSlotAddOutcome.AlreadyHasSlot:
                            summary.AlreadyHadSlot++;
                            summary.Notes.Add(note);
                            break;
                        default:
                            summary.NotSupported++;
                            summary.Notes.Add(note);
                            break;
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
            return summary;
        }

        /// <summary>
        /// Adds the slot to one recipe by changing its stored recipe text. The caller records undo and
        /// saves. A recipe that is not changed gets the reason in <paramref name="note"/>.
        /// </summary>
        public static UMARecipeSlotAddOutcome AddToRecipe(UMATextRecipe recipe, UMARecipeSlotAddition addition, out string note)
        {
            if (!Validate(addition, out string problem))
            {
                throw new ArgumentException(problem, nameof(addition));
            }
            if (!Supports(recipe, out note))
            {
                return UMARecipeSlotAddOutcome.NotSupported;
            }
            // The stored text, read as it is. UMATextRecipe.PackedLoad would also fill in a missing
            // race name from the compatible races, which would then be saved with the slot.
            PackedRecipe packed = string.IsNullOrEmpty(recipe.recipeString) ? new PackedRecipe() : JsonUtility.FromJson<PackedRecipe>(recipe.recipeString);
            if (packed == null)
            {
                note = $"'{recipe.name}': its recipe text could not be read.";
                return UMARecipeSlotAddOutcome.NotSupported;
            }
            UMARecipeSlotAddOutcome outcome = AddToPacked(packed, addition, out note);
            if (outcome == UMARecipeSlotAddOutcome.Added)
            {
                recipe.recipeString = JsonUtility.ToJson(packed);
            }
            else if (note != null)
            {
                note = $"'{recipe.name}': {note}";
            }
            return outcome;
        }

        /// <summary>
        /// Adds the slot to packed recipe data. Slots and colors the data already holds keep their
        /// content; only the color numbers of overlays move up where a new shared color is inserted
        /// before the colors that are not shared.
        /// </summary>
        public static UMARecipeSlotAddOutcome AddToPacked(PackedRecipe packed, UMARecipeSlotAddition addition, out string note)
        {
            note = null;
            if (packed == null)
            {
                throw new ArgumentNullException(nameof(packed));
            }
            if (!Validate(addition, out string problem))
            {
                throw new ArgumentException(problem, nameof(addition));
            }
            if (packed.version != 3)
            {
                bool holdsOlderSlots = PackedRecipe.ArrayHasData(packed.packedSlotDataList) || PackedRecipe.ArrayHasData(packed.slotsV2);
                if (holdsOlderSlots)
                {
                    note = $"it is stored in the older recipe format (version {packed.version}). Open it in the recipe editor and save it once, then add the slot.";
                    return UMARecipeSlotAddOutcome.NotSupported;
                }
                packed.version = 3;   // an empty recipe
            }

            PackedSlot[] slots = packed.slotsV3 ?? new PackedSlot[0];
            string slotName = addition.slot.slotName;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && !slots[i].isPlaceholderSlot && string.Equals(slots[i].id, slotName, StringComparison.Ordinal))
                {
                    note = $"it already has the slot '{slotName}'.";
                    return UMARecipeSlotAddOutcome.AlreadyHasSlot;
                }
            }

            PackedRecipe added = Pack(addition);
            PackedSlot newSlot = added.slotsV3[0];
            PackedColor[] addedColors = added.fColors ?? new PackedColor[0];
            int addedShared = Mathf.Clamp(added.sharedColorCount, 0, addedColors.Length);

            var colors = new List<PackedColor>(packed.fColors ?? new PackedColor[0]);
            int shared = Mathf.Clamp(packed.sharedColorCount, 0, colors.Count);
            var newNumber = new int[addedColors.Length];

            // Shared colors: one the recipe already has by that name is used as it is; a new one goes
            // at the end of the recipe's shared colors, which moves every unshared color up one.
            for (int i = 0; i < addedShared; i++)
            {
                int at = -1;
                for (int c = 0; c < shared; c++)
                {
                    if (colors[c] != null && string.Equals(colors[c].name, addedColors[i].name, StringComparison.Ordinal))
                    {
                        at = c;
                        break;
                    }
                }
                if (at < 0)
                {
                    at = shared;
                    colors.Insert(at, addedColors[i]);
                    MoveColorNumbersUp(slots, at);
                    shared++;
                }
                newNumber[i] = at;
            }
            // The slot's unshared colors go at the very end.
            for (int i = addedShared; i < addedColors.Length; i++)
            {
                newNumber[i] = colors.Count;
                colors.Add(addedColors[i]);
            }
            if (newSlot.overlays != null)
            {
                for (int i = 0; i < newSlot.overlays.Length; i++)
                {
                    if (newSlot.overlays[i] != null)
                    {
                        newSlot.overlays[i].colorIdx = newNumber[newSlot.overlays[i].colorIdx];
                    }
                }
            }

            var allSlots = new List<PackedSlot>(slots) { newSlot };
            packed.slotsV3 = allSlots.ToArray();
            packed.fColors = colors.ToArray();
            packed.sharedColorCount = shared;
            return UMARecipeSlotAddOutcome.Added;
        }

        /// <summary>Every overlay that points at a color numbered <paramref name="from"/> or higher now points one further.</summary>
        private static void MoveColorNumbersUp(PackedSlot[] slots, int from)
        {
            for (int s = 0; s < slots.Length; s++)
            {
                if (slots[s] == null || slots[s].overlays == null)
                {
                    continue;
                }
                for (int o = 0; o < slots[s].overlays.Length; o++)
                {
                    if (slots[s].overlays[o] != null && slots[s].overlays[o].colorIdx >= from)
                    {
                        slots[s].overlays[o].colorIdx++;
                    }
                }
            }
        }

        /// <summary>
        /// Builds the slot the way the recipe editor would, in a recipe of its own, and packs that with
        /// UMA's packer: one slot, the shared colors first among its colors, then the unshared ones.
        /// </summary>
        private static PackedRecipe Pack(UMARecipeSlotAddition addition)
        {
            var slotData = new SlotData(addition.slot)
            {
                tags = Clean(addition.tags),
                Races = Clean(addition.races),
                isSwapSlot = addition.isSwapSlot,
                swapTag = addition.isSwapSlot ? addition.swapTag.Trim() : string.Empty,
            };

            // The overlays first: each tells how many channels its color needs.
            var overlays = new List<OverlayData>();
            var follows = new List<string>();
            if (addition.overlays != null)
            {
                foreach (UMARecipeOverlayAddition row in addition.overlays)
                {
                    if (row == null || row.overlay == null)
                    {
                        continue;
                    }
                    var overlay = new OverlayData(row.overlay);
                    int channels = overlay.colorData != null && overlay.colorData.channelCount > 0 ? overlay.colorData.channelCount : FallbackChannels;
                    var own = new OverlayColorData(channels) { name = OverlayColorData.UNSHARED };
                    own.color = row.color;
                    overlay.colorData = own;
                    overlays.Add(overlay);
                    follows.Add(string.IsNullOrWhiteSpace(row.sharedColorName) ? null : row.sharedColorName.Trim());
                }
            }

            // The shared colors: the listed ones, and any an overlay names that is not listed.
            var sharedColors = new List<OverlayColorData>();
            var sharedByName = new Dictionary<string, OverlayColorData>(StringComparer.Ordinal);
            // A shared color starts with the usual number of channels and grows to fit the overlays that follow it.
            int slotChannels = FallbackChannels;
            if (addition.sharedColors != null)
            {
                foreach (UMARecipeSharedColorAddition row in addition.sharedColors)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.name))
                    {
                        continue;
                    }
                    var color = new OverlayColorData(slotChannels) { name = row.name.Trim() };
                    color.color = row.color;
                    sharedColors.Add(color);
                    sharedByName.Add(color.name, color);
                }
            }
            for (int i = 0; i < overlays.Count; i++)
            {
                if (follows[i] == null)
                {
                    continue;
                }
                if (!sharedByName.TryGetValue(follows[i], out OverlayColorData color))
                {
                    color = new OverlayColorData(slotChannels) { name = follows[i] };
                    sharedColors.Add(color);
                    sharedByName.Add(color.name, color);
                }
                color.EnsureChannels(overlays[i].colorData.channelCount);
                overlays[i].colorData = color;
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                slotData.AddOverlay(overlays[i]);
            }
            var recipe = new UMAData.UMARecipe
            {
                slotDataList = new[] { slotData },
                sharedColors = sharedColors.ToArray(),
            };
            return UMAPackedRecipeBase.PackRecipeV3(recipe);
        }

        private static string[] Clean(List<string> values)
        {
            var result = new List<string>();
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value.Trim()))
                    {
                        result.Add(value.Trim());
                    }
                }
            }
            return result.ToArray();
        }
    }
}
