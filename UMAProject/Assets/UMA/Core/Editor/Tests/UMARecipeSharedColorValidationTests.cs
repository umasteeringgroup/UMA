using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    [TestFixture]
    [Category("UMA")]
    [Category("Content Validation")]
    public sealed class UMARecipeSharedColorValidationTests
    {
        [Test]
        public void AllRecipeSharedColorsDisableAutomaticUpdates()
        {
            // Query the AssetDatabase, not the UMA index: unindexed recipes and
            // recipe sub-assets must be checked too. Wardrobe recipes derive from
            // UMATextRecipe; deduplicate the explicit queries before loading.
            var paths = AssetDatabase.FindAssets("t:UMATextRecipe")
                .Concat(AssetDatabase.FindAssets("t:UMAWardrobeRecipe"))
                .Distinct()
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);
            var failures = new List<string>();
            int recipes = 0, wardrobeRecipes = 0, sharedColors = 0;
            foreach (string path in paths)
            {
                foreach (var recipe in AssetDatabase.LoadAllAssetsAtPath(path).OfType<UMATextRecipe>())
                {
                    recipes++;
                    if (recipe is UMAWardrobeRecipe) wardrobeRecipes++;
                    string label = $"{path} — {recipe.GetType().Name} '{recipe.name}'";
                    if (string.IsNullOrWhiteSpace(recipe.recipeString)) continue;

                    UMAPackedRecipeBase.UMAPackRecipe packed;
                    try
                    {
                        // PackedLoad also resolves races through the global indexer.
                        // Only serialized color settings are needed for this audit.
                        packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(recipe.recipeString);
                        if (packed == null) throw new FormatException("Recipe JSON is null.");
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{label}: could not scan recipe JSON: {exception.Message}");
                        continue;
                    }

                    // fColors contains shared colors first, followed by per-overlay
                    // colors. Only the first sharedColorCount entries are shared.
                    bool usesFloatColors = packed.fColors != null && packed.fColors.Length > 0;
                    int colorCount = usesFloatColors ? packed.fColors.Length : packed.colors?.Length ?? 0;
                    if (packed.sharedColorCount < 0 || packed.sharedColorCount > colorCount)
                        failures.Add($"{label}: invalid sharedColorCount {packed.sharedColorCount} for {colorCount} colors.");
                    int count = Math.Max(0, Math.Min(packed.sharedColorCount, colorCount));
                    sharedColors += count;
                    // Legacy byte colors cannot serialize either update flag.
                    if (!usesFloatColors) continue;
                    for (int i = 0; i < count; i++)
                    {
                        var color = packed.fColors[i];
                        if (color == null)
                        {
                            failures.Add($"{label}: shared color [{i}] is null and could not be scanned.");
                            continue;
                        }
                        if (!color.alwaysUpdate && !color.alwaysUpdateParms) continue;
                        string flags = color.alwaysUpdate && color.alwaysUpdateParms
                            ? "Always Update, Parms Only"
                            : color.alwaysUpdate ? "Always Update" : "Parms Only";
                        failures.Add($"{label}: shared color [{i}] '{color.name}' enables {flags}.");
                    }
                }
            }

            Debug.Log($"Shared-color audit: {recipes} recipes ({wardrobeRecipes} wardrobe, " +
                $"{recipes - wardrobeRecipes} other text recipes), {sharedColors} shared colors, {failures.Count} issues.");
            Assert.That(recipes, Is.GreaterThan(0), "No recipe assets were found; the audit did not scan any content.");
            Assert.That(failures, Is.Empty,
                "Recipe shared colors must disable Always Update and Parms Only:\n" + string.Join("\n", failures));
        }
    }
}
