using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    public partial class UMAAvatarLoadSaveMenuItems
    {
        [MenuItem("Assets/UMA/Remove invalid races from selected recipes", false, 2001)]
        private static void RemoveInvalidRacesFromSelectedRecipes()
        {
            int removed = UMAWardrobeRecipeRaceCleanup.RemoveInvalidRaces(GetSelectedWardrobeRecipes(),
                UMAWardrobeRecipeRaceCleanup.FindExistingRaceNames(), out int changed);
            EditorUtility.DisplayDialog("Remove Invalid Races",
                removed == 0 ? "The selected recipes have no invalid race references." :
                    $"Removed {removed} invalid race reference(s) from {changed} selected recipe(s). This can be undone.", "OK");
        }

        [MenuItem("Assets/UMA/Remove invalid races from selected recipes", true)]
        private static bool RemoveInvalidRacesFromSelectedRecipesValidate() => GetSelectedWardrobeRecipes().Count > 0;
    }

    public static class UMAWardrobeRecipeRaceCleanup
    {
        /// <summary>Inspect assets, not the index: unindexed installed races are still valid.</summary>
        public static HashSet<string> FindExistingRaceNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:RaceData"))
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (asset is RaceData race && !string.IsNullOrWhiteSpace(race.raceName)) names.Add(race.raceName);
            return names;
        }

        public static int RemoveInvalidRaces(IEnumerable<UMAWardrobeRecipe> recipes, ISet<string> validNames, out int changedRecipes)
        {
            if (validNames == null) throw new ArgumentNullException(nameof(validNames));
            changedRecipes = 0; int removed = 0;
            if (recipes == null) return 0;
            var visited = new HashSet<UMAWardrobeRecipe>();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove Invalid Recipe Races");
            try
            {
                foreach (UMAWardrobeRecipe recipe in recipes)
                {
                    if (recipe == null || !visited.Add(recipe) || recipe.compatibleRaces == null) continue;
                    var invalid = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string name in recipe.compatibleRaces)
                        if (string.IsNullOrWhiteSpace(name) || !validNames.Contains(name)) invalid.Add(name);
                    if (invalid.Count == 0) continue;
                    Undo.RecordObject(recipe, "Remove Invalid Recipe Races");
                    removed += recipe.compatibleRaces.RemoveAll(invalid.Contains);
                    recipe.wardrobeRecipeThumbs?.RemoveAll(thumb => thumb != null && invalid.Contains(thumb.race));
                    EditorUtility.SetDirty(recipe);
                    AssetDatabase.SaveAssetIfDirty(recipe);
                    changedRecipes++;
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            return removed;
        }
    }
}
