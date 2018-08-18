using UnityEngine;
using System.Collections;
using UMA.CharacterSystem;

namespace UMA.CharacterSystem.Examples
{
    public class WardrobeHandler : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;
        public UMATextRecipe Recipe;
        public string Slot;

        public void Setup(DynamicCharacterAvatar avatar, UMATextRecipe recipe, string slot)
        {
            Avatar = avatar;
            Recipe = recipe;
            Slot = slot;
        }

        public void OnClick()
        {
            // If there is no recipe, then just remove whatever is there (if any)
            if (Recipe == null)
            {
                Avatar.ClearSlot(Slot);
                Avatar.BuildCharacter(true);
                Avatar.ForceUpdate(true, true, true);
                return;
            }
            // We have a recipe.
            // The wardrobe slot is defined in the recipe itself, so setting a recipe is all 
            // that is needed to "put on" a wardrobe item. 
            // Any recipe that already exists at that slot will be removed - so, for example,
            // putting on a shirt will replace the existing shirt if one exists.
            Avatar.SetSlot(Recipe);
            // Rebuild the character so its wearing the new wardrobe item.
            Avatar.BuildCharacter(true);
            Avatar.ForceUpdate(true, true, true);
        }
    }
}
