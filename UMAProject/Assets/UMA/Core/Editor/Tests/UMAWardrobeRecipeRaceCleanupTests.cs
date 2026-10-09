using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors.Tests
{
    public sealed class UMAWardrobeRecipeRaceCleanupTests
    {
        private string folder;
        private UMAWardrobeRecipe selected, untouched;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/RecipeRaceCleanupTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            selected = CreateRecipe("Selected");
            untouched = CreateRecipe("Untouched");
        }

        private UMAWardrobeRecipe CreateRecipe(string name)
        {
            var recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            recipe.compatibleRaces = new List<string> { "Existing", "Missing", "", "Missing" };
            recipe.wardrobeRecipeThumbs = new List<WardrobeRecipeThumb> {
                new WardrobeRecipeThumb { race = "Existing" }, new WardrobeRecipeThumb { race = "Missing" } };
            AssetDatabase.CreateAsset(recipe, folder + "/" + name + ".asset");
            return recipe;
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(selected); Undo.ClearUndo(untouched);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void RemovesOnlyInvalidReferencesFromSelectedRecipesAndSupportsUndo()
        {
            int removed = UMAWardrobeRecipeRaceCleanup.RemoveInvalidRaces(new[] { selected, selected, null },
                new HashSet<string> { "Existing" }, out int changed);
            Assert.That(removed, Is.EqualTo(3)); Assert.That(changed, Is.EqualTo(1));
            Assert.That(selected.compatibleRaces, Is.EqualTo(new[] { "Existing" }));
            Assert.That(selected.wardrobeRecipeThumbs.Count, Is.EqualTo(1));
            Assert.That(untouched.compatibleRaces.Count, Is.EqualTo(4));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(selected.compatibleRaces, Is.EqualTo(new[] { "Existing", "Missing", "", "Missing" }));
            Assert.That(selected.wardrobeRecipeThumbs.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExistingUnindexedRaceIsValidAndUsesRaceNameRatherThanFileName()
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            race._oldRaceName = "Unindexed-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateAsset(race, folder + "/DifferentFileName.asset");
            selected.compatibleRaces = new List<string> { race.raceName };
            var valid = UMAWardrobeRecipeRaceCleanup.FindExistingRaceNames();
            Assert.That(valid.Contains(race.raceName), Is.True);
            Assert.That(UMAWardrobeRecipeRaceCleanup.RemoveInvalidRaces(new[] { selected }, valid, out int changed), Is.Zero);
            Assert.That(changed, Is.Zero);
        }
    }
}
