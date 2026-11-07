using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMAWardrobeRecipeDisplayNameTests
    {
        [TestCase("ShortHair_Recipe", "Short Hair")]
        [TestCase("Hair_Straight_Recipe", "Straight")]
        [TestCase("bb_female_hair_recipe", "Female")]
        [TestCase("bb_Male_Military_Hair_Recipe", "Military")]
        [TestCase("HairLongCurly_Recipe", "Long Curly")]
        [TestCase("HumanMale_Hair_Crew_Cut_Recipe", "Crew Cut")]
        [TestCase("HUMANFEMALE_LongHair_RECIPE", "Long Hair")]
        [TestCase("female_Long_SleeveShirt_Recipe", "Long Sleeve Shirt")]
        [TestCase("male_sportpants_blueWhite_Recipe", "Sportpants Blue White")]
        [TestCase("__bb__female__Hair__LongHair__Recipe__", "Long Hair")]
        [TestCase("Hair_Recipe", "Hair")]
        [TestCase("Malevolent_Chair_Recipe", "Malevolent Chair")]
        [TestCase("  Hair_LongHair\tRecipe  ", "Long Hair")]
        [TestCase("bb_Recipe", "")]
        [TestCase("___", "")]
        [TestCase(null, "")]
        public void CleansMarkersAndUsesUnityWordSpacing(string objectName, string expected)
        {
            string actual = UMAWardrobeRecipeDisplayNames.FromObjectName(objectName);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual, Does.Not.Contain("_"));
        }

        [Test]
        public void MenuUpdatesOnlySelectedBlankNamesAndSupportsUndo()
        {
            string folder = "Assets/__WardrobeDisplayNames_" + Guid.NewGuid().ToString("N");
            Object[] previousSelection = Selection.objects;
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            UMAWardrobeRecipe blank = null, whitespace = null, named = null, untouched = null;
            try
            {
                blank = CreateRecipe(folder, "ShortHair_Recipe", null);
                whitespace = CreateRecipe(folder, "bb_Male_Military_Hair_Recipe", " \t ");
                named = CreateRecipe(folder, "Hair_Straight_Recipe", "My_Custom Name");
                untouched = CreateRecipe(folder, "bb_female_hair_recipe", "");
                string originalGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(blank));
                Selection.objects = new Object[] { blank, whitespace, named };

                Assert.That(EditorApplication.ExecuteMenuItem("Assets/UMA/Update blank display names"), Is.True);

                Assert.That(blank.DisplayValue, Is.EqualTo("Short Hair"));
                Assert.That(whitespace.DisplayValue, Is.EqualTo("Military"));
                Assert.That(named.DisplayValue, Is.EqualTo("My_Custom Name"));
                Assert.That(untouched.DisplayValue, Is.Empty);
                Assert.That(blank.name, Is.EqualTo("ShortHair_Recipe"));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(blank)), Is.EqualTo(originalGuid));
                Assert.That(EditorUtility.IsDirty(blank), Is.False, "The command should save the updated asset.");
                Assert.That(UMAWardrobeRecipeDisplayNames.UpdateBlankDisplayNames(new[] { blank, blank, null }), Is.Zero);

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(string.IsNullOrEmpty(blank.DisplayValue), Is.True);
                Assert.That(whitespace.DisplayValue, Is.EqualTo(" \t "));
                Assert.That(named.DisplayValue, Is.EqualTo("My_Custom Name"));
                Undo.PerformRedo();
                Assert.That(blank.DisplayValue, Is.EqualTo("Short Hair"));
                Assert.That(whitespace.DisplayValue, Is.EqualTo("Military"));
            }
            finally
            {
                Selection.objects = previousSelection;
                foreach (var recipe in new[] { blank, whitespace, named, untouched })
                    if (recipe != null) Undo.ClearUndo(recipe);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static UMAWardrobeRecipe CreateRecipe(string folder, string name, string displayName)
        {
            var recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            recipe.DisplayValue = displayName;
            AssetDatabase.CreateAsset(recipe, folder + "/" + name + ".asset");
            return recipe;
        }
    }
}
