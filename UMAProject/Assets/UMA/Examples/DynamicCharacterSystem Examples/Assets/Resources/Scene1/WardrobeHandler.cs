using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine.UI;

namespace UMA.CharacterSystem.Examples
{
    public class WardrobeHandler : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;
        public UMATextRecipe Recipe;
        public string Slot;
		public string theText;

		private Color32 LoadedColor = new Color32(0, 128, 0, 255);
		private Color32 UnloadedColor = new Color32(128, 0, 0, 255);

		public bool isReady
		{
			get
			{
#if UMA_ADDRESSABLES
				if (Recipe == null)
					return false;

				if (UMAAssetIndexer.Instance.Preloads.ContainsKey(Recipe.name)) return true;
	
				return false;
#else
				return true;
#endif
			}
		}
		public void SetColors()
		{
			Text txt = GetComponentInChildren<Text>();
			txt.text = theText;
			if (Recipe == null)
			{
				txt.color = Color.black;
				txt.text = theText;
			}
			else if (!isReady)
			{
				txt.color = UnloadedColor;
				txt.text = "(U) " + theText;
			}
			else
			{
				txt.text = "(L)" + theText;
				txt.color = LoadedColor;
			}

			if (txt.text.Length > 20)
			{
				txt.text = txt.text.Substring(0, 20);
			}
		}

		public void Setup(DynamicCharacterAvatar avatar, UMATextRecipe recipe, string slot, string text)
        {
            Avatar = avatar;
            Recipe = recipe;
            Slot = slot;
			theText = text;
        }

        public void OnClick()
        {
			SetColors();
            // If there is no recipe, then just remove whatever is there (if any)
            if (Recipe == null)
            {
                Avatar.ClearSlot(Slot);
                Avatar.BuildCharacter(true);
                return;
            }
#if UMA_ADDRESSABLES
			SetRecipe();
/*			if (isReady)
			{
				SetRecipe();
			}
			else
			{
				var op = UMAAssetIndexer.Instance.Preload(Recipe);
				op.Completed += Op_Completed;
			} */
#else
			SetRecipe();
#endif
		}

		private void SetRecipe()
		{
			// We have a recipe.
			// The wardrobe slot is defined in the recipe itself, so setting a recipe is all 
			// that is needed to "put on" a wardrobe item. 
			// Any recipe that already exists at that slot will be removed - so, for example,
			// putting on a shirt will replace the existing shirt if one exists.
			Avatar.SetSlot(Recipe);
			// Rebuild the character so its wearing the new wardrobe item.
			Avatar.BuildCharacter(true);
			// Avatar.ForceUpdate(true, true, true);
		}

#if UMA_ADDRESSABLES
		private void Op_Completed(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<Object>> obj)
		{
			SetColors();
			SetRecipe();
		}
#endif
	}
}