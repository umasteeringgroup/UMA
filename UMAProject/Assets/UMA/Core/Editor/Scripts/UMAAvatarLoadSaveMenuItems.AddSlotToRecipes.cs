using System.Collections.Generic;
using UnityEditor;

namespace UMA.Editors
{
    public partial class UMAAvatarLoadSaveMenuItems
    {
        private const string AddSlotToRecipesMenu = "Assets/UMA/Add a slot to selected recipes";

        [MenuItem(AddSlotToRecipesMenu, false, 2000)]
        private static void AddSlotToSelectedRecipesMenu()
        {
            UmaAddSlotToRecipesWindow.Open(GetSelectedRecipesThatHoldSlots());
        }

        [MenuItem(AddSlotToRecipesMenu, true)]
        private static bool AddSlotToSelectedRecipesMenu_Validate() => GetSelectedRecipesThatHoldSlots().Count > 0;

        /// <summary>The selected text and wardrobe recipes; wardrobe collections and saved characters hold no slots of their own.</summary>
        private static List<UMATextRecipe> GetSelectedRecipesThatHoldSlots()
        {
            List<UMATextRecipe> selected = GetSelectedTextRecipes();
            selected.RemoveAll(recipe => !UMARecipeSlotAdder.Supports(recipe, out _));
            return selected;
        }
    }
}
