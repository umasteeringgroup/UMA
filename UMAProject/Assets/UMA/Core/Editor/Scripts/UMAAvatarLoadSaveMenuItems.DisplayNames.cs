using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    public partial class UMAAvatarLoadSaveMenuItems
    {
        [MenuItem("Assets/UMA/Update blank display names", false, 2001)]
        private static void UpdateBlankWardrobeDisplayNames()
        {
            int changed = UMAWardrobeRecipeDisplayNames.UpdateBlankDisplayNames(GetSelectedWardrobeRecipes());
            Debug.Log($"Updated {changed} blank wardrobe recipe display name(s). Changes can be undone.");
        }

        [MenuItem("Assets/UMA/Update blank display names", true)]
        private static bool UpdateBlankWardrobeDisplayNamesValidate() => GetSelectedWardrobeRecipes().Count > 0;
    }

    public static class UMAWardrobeRecipeDisplayNames
    {
        /// <summary>Remove naming markers, then use Unity's formatting to separate CamelCase words.</summary>
        public static string FromObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return string.Empty;
            string[] tokens = objectName.Replace('_', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int end = tokens.Length;
            while (end > 0 && tokens[end - 1].Equals("Recipe", StringComparison.OrdinalIgnoreCase)) end--;

            var words = new List<string>();
            string genderFallback = null;
            bool hadHair = false;
            for (int i = 0; i < end; i++)
            {
                string token = tokens[i];
                if (token.Equals("bb", StringComparison.OrdinalIgnoreCase)) continue;
                // Also handles race prefixes such as HumanMale and HumanFemale.
                if (token.EndsWith("male", StringComparison.OrdinalIgnoreCase))
                {
                    genderFallback = token.EndsWith("female", StringComparison.OrdinalIgnoreCase) ? "Female" : "Male";
                    continue;
                }
                if (token.Equals("Hair", StringComparison.OrdinalIgnoreCase))
                {
                    hadHair = true;
                    continue;
                }
                if (words.Count == 0 && token.StartsWith("Hair", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(4);
                    hadHair = true;
                }
                words.Add(ObjectNames.NicifyVariableName(token));
            }

            if (words.Count > 0) return string.Join(" ", words);
            // bb_female_hair_recipe should still have a useful name: Female.
            return genderFallback ?? (hadHair ? "Hair" : string.Empty);
        }

        /// <summary>Fill only blank display values on the supplied editable assets, as one undoable operation.</summary>
        public static int UpdateBlankDisplayNames(IEnumerable<UMAWardrobeRecipe> recipes)
        {
            if (recipes == null) return 0;
            int changed = 0;
            var visited = new HashSet<UMAWardrobeRecipe>();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string undoName = "Update blank display names";
            Undo.SetCurrentGroupName(undoName);
            try
            {
                foreach (UMAWardrobeRecipe recipe in recipes)
                {
                    if (recipe == null || !visited.Add(recipe) || !string.IsNullOrWhiteSpace(recipe.DisplayValue)) continue;
                    if (!EditorUtility.IsPersistent(recipe) || !AssetDatabase.IsOpenForEdit(recipe)) continue;
                    string displayName = FromObjectName(recipe.name);
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    Undo.RecordObject(recipe, undoName);
                    recipe.DisplayValue = displayName;
                    EditorUtility.SetDirty(recipe);
                    AssetDatabase.SaveAssetIfDirty(recipe);
                    changed++;
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            return changed;
        }
    }
}
