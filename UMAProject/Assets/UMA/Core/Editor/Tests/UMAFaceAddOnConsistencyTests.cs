#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Every UMA 3 face add-on behaves like the others: it goes in the Face slot, applies its own overlay with the
    /// character's shared skin color, shows a thumbnail for each race it fits, and has a name of its own.
    /// The faces are read from the folder, so a new face is covered without touching this fixture.
    /// </summary>
    public class UMAFaceAddOnConsistencyTests
    {
        const string Folder = "Assets/UMA/UMA3/Textures/AlternateFaces";
        const string RecipeSuffix = "_Wardrobe";

        static List<UMAWardrobeRecipe> FaceRecipes()
        {
            var recipes = new List<UMAWardrobeRecipe>();
            if (!AssetDatabase.IsValidFolder(Folder)) Assert.Ignore("The UMA 3 content is not installed.");
            foreach (string guid in AssetDatabase.FindAssets("t:UMAWardrobeRecipe", new[] { Folder }))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (recipe != null) recipes.Add(recipe);
            }
            if (recipes.Count == 0) Assert.Ignore("The UMA 3 content is not installed.");
            return recipes;
        }

        static string FaceName(UMAWardrobeRecipe recipe)
        {
            return recipe.name.EndsWith(RecipeSuffix) ? recipe.name.Substring(0, recipe.name.Length - RecipeSuffix.Length) : recipe.name;
        }

        [Test]
        public void EveryFaceGoesInTheFaceSlotAndAppliesItsOwnOverlay()
        {
            var problems = new List<string>();
            foreach (var recipe in FaceRecipes())
            {
                string face = FaceName(recipe);
                if (recipe.wardrobeSlot != "Face") problems.Add(recipe.name + ": wardrobe slot is '" + recipe.wardrobeSlot + "'");
                if (recipe.compatibleRaces == null || recipe.compatibleRaces.Count == 0) problems.Add(recipe.name + ": no compatible race");

                var unpacked = new UMAData.UMARecipe();
                recipe.Load(unpacked, true);
                var overlays = unpacked.slotDataList.Where(s => s != null).SelectMany(s => s.GetOverlayList()).Select(o => o.overlayName).ToList();
                if (overlays.Count != 1 || overlays[0] != face + "_Overlay")
                {
                    problems.Add(recipe.name + ": applies [" + string.Join(", ", overlays) + "], expected only " + face + "_Overlay");
                }
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryFaceUsesTheSharedSkinColor()
        {
            // A face with a color of its own keeps that color when the character's skin color changes,
            // and shows as a patch on a character whose skin is tinted through the overlay color.
            var problems = new List<string>();
            foreach (var recipe in FaceRecipes())
            {
                var unpacked = new UMAData.UMARecipe();
                recipe.Load(unpacked, true);
                foreach (var slot in unpacked.slotDataList)
                {
                    if (slot == null) continue;
                    foreach (var overlay in slot.GetOverlayList())
                    {
                        if (overlay.colorData == null || overlay.colorData.name != "Skin")
                        {
                            problems.Add(recipe.name + ": " + overlay.overlayName + " uses color '" + (overlay.colorData != null ? overlay.colorData.name : "none") + "'");
                        }
                    }
                }
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryFaceHasAThumbnailForEachRaceItFits()
        {
            var problems = new List<string>();
            foreach (var recipe in FaceRecipes())
            {
                foreach (string race in recipe.compatibleRaces)
                {
                    var entry = recipe.wardrobeRecipeThumbs.FirstOrDefault(t => t != null && t.race == race);
                    if (entry == null || entry.thumb == null) problems.Add(recipe.name + ": no thumbnail for " + race);
                }
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void FaceDisplayNamesAreUnique()
        {
            var duplicates = FaceRecipes()
                .GroupBy(r => r.DisplayValue)
                .Where(g => g.Count() > 1)
                .Select(g => "'" + g.Key + "' is shown for " + string.Join(", ", g.Select(r => r.name)))
                .ToList();
            Assert.That(duplicates, Is.Empty, string.Join("\n", duplicates));
        }
    }
}
#endif
