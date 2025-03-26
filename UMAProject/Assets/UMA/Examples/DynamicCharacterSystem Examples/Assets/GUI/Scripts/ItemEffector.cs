using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.UI;

namespace UMA
{

    public class ItemEffector : MonoBehaviour
    {
        public IItemSelector itemSelector;
        public UMAWardrobeRecipe recipe;

        public void Setup(IItemSelector itemSelector, UMAWardrobeRecipe recipe)
        {
            this.itemSelector = itemSelector;
            this.recipe = recipe;

            Image[] img = GetComponentsInChildren<Image>();

            bool imageSet = false;
            if (recipe.wardrobeRecipeThumbs.Count > 0)
            {
                for (int i = 0; i < img.Length; i++)
                {
                    if (img[i].name == "ItemImage")
                    {
                        img[i].sprite = recipe.wardrobeRecipeThumbs[0].thumb;
                        imageSet = true;
                    }
                }
            }

            Text text = GetComponentInChildren<Text>();
            if (text != null)
            {

                if (!imageSet)
                {
                    string itemName = recipe.name;
                    if (!string.IsNullOrEmpty(recipe.DisplayValue))
                    {
                        itemName = recipe.DisplayValue;
                    }
                    text.text = itemName.Substring(0, 12);
                }
                else
                {
                    text.text = "";
                }
            }
        }

        public void ImageClicked()
        {
            itemSelector.SetItem(recipe);
        }
    }
}