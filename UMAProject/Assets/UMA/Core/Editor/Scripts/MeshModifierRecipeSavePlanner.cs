using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// Works out how the Vertex Editor's "Save as Mesh Modifier" splits the session's edits: one Mesh
    /// Modifier for each wardrobe recipe whose slots were edited (so each recipe can carry the changes
    /// to its own slots), and one for slots that no worn wardrobe recipe has, such as the base body.
    /// </summary>
    public static class MeshModifierRecipeSavePlanner
    {
        /// <summary>One Mesh Modifier asset to write.</summary>
        public sealed class Plan
        {
            /// <summary>Where the asset is written.</summary>
            public string path;
            /// <summary>The wardrobe recipe the edited slots belong to; null for slots no worn wardrobe recipe has.</summary>
            public UMAWardrobeRecipe recipe;
            /// <summary>The session's modifiers for those slots, in session order.</summary>
            public readonly List<MeshModifier.Modifier> modifiers = new List<MeshModifier.Modifier>();
        }

        /// <summary>
        /// Finds the worn wardrobe recipe that holds a slot, by the key a modifier stores for it: the
        /// slot's logical (source) name, or for modifiers saved before October 2026 the slot's asset name.
        /// The first recipe wins when two hold the same slot.
        /// </summary>
        public static Func<string, UMAWardrobeRecipe> SlotOwnerLookup(IEnumerable<UMATextRecipe> wornRecipes)
        {
            var owners = new List<KeyValuePair<SlotDataAsset, UMAWardrobeRecipe>>();
            if (wornRecipes != null)
            {
                foreach (UMATextRecipe worn in wornRecipes)
                {
                    if (!(worn is UMAWardrobeRecipe wardrobe))
                    {
                        continue;
                    }
                    UMAData.UMARecipe recipe = wardrobe.GetCachedRecipe();
                    if (recipe == null || recipe.slotDataList == null)
                    {
                        continue;
                    }
                    foreach (SlotData slot in recipe.slotDataList)
                    {
                        if (slot != null && slot.asset != null)
                        {
                            owners.Add(new KeyValuePair<SlotDataAsset, UMAWardrobeRecipe>(slot.asset, wardrobe));
                        }
                    }
                }
            }
            return key =>
            {
                if (string.IsNullOrEmpty(key))
                {
                    return null;
                }
                foreach (var owner in owners)
                {
                    SlotDataAsset asset = owner.Key;
                    if (string.Equals(asset.sourceSlot, key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(asset.slotName, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return owner.Value;
                    }
                }
                foreach (var owner in owners)
                {
                    if (MeshModifier.IsLegacySlotKey(key, owner.Key))
                    {
                        return owner.Value;
                    }
                }
                return null;
            };
        }

        /// <summary>
        /// Splits the session's modifiers by the wardrobe recipe their slot belongs to. With one group,
        /// it is written to <paramref name="chosenPath"/>. With several, each recipe's group is written
        /// next to it as "&lt;name&gt;_&lt;recipe&gt;.asset", and the group no recipe has to the chosen path.
        /// </summary>
        public static List<Plan> PlanSaves(IList<MeshModifier.Modifier> modifiers, Func<string, UMAWardrobeRecipe> ownerOf, string chosenPath)
        {
            var plans = new List<Plan>();
            Plan unowned = null;
            if (modifiers != null)
            {
                foreach (MeshModifier.Modifier modifier in modifiers)
                {
                    if (modifier == null)
                    {
                        continue;
                    }
                    UMAWardrobeRecipe recipe = ownerOf != null ? ownerOf(modifier.SlotName) : null;
                    Plan plan = recipe == null ? unowned : plans.Find(p => ReferenceEquals(p.recipe, recipe));
                    if (plan == null)
                    {
                        plan = new Plan { recipe = recipe };
                        plans.Add(plan);
                        if (recipe == null)
                        {
                            unowned = plan;
                        }
                    }
                    plan.modifiers.Add(modifier);
                }
            }
            if (plans.Count == 0)
            {
                // Nothing per slot (an empty session): one asset, as before.
                plans.Add(new Plan { path = chosenPath });
                return plans;
            }
            if (plans.Count == 1)
            {
                plans[0].path = chosenPath;
                return plans;
            }
            string folder = Path.GetDirectoryName(chosenPath)?.Replace('\\', '/') ?? "Assets";
            string baseName = Path.GetFileNameWithoutExtension(chosenPath);
            foreach (Plan plan in plans)
            {
                plan.path = plan.recipe == null
                    ? chosenPath
                    : folder + "/" + baseName + "_" + SafeFileName(plan.recipe.name) + ".asset";
            }
            return plans;
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Recipe";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }
    }
}
