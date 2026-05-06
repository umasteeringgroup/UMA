using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.UI;

namespace UMA
{

    public class ItemEffector : MonoBehaviour
    {
        public IItemSelector itemSelector;
        public UMAWardrobeRecipe recipe;
        public string category;
        public Sprite clearImage;


        public void SetupClearbutton()
        {
            Image[] img = GetComponentsInChildren<Image>();
            if (recipe == null)
            {
                foreach (Image i in img)
                {
                    if (i.name == "ItemImage")
                    {
                        i.sprite = clearImage;
                    }
                }
            }
            Text text = GetComponentInChildren<Text>();
            if (text != null)
            {
                    text.text = "Clear";
            }            
        }

        public void Setup(IItemSelector itemSelector, UMAWardrobeRecipe recipe, string category)
        {
            bool imageSet = false;
  
            this.itemSelector = itemSelector;
            this.recipe = recipe;
            this.category = category;

            if (recipe == null)
            {
                SetupClearbutton();
                return;
            }

            Image[] img = GetComponentsInChildren<Image>();
            
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
            if (recipe != null)
            {
                itemSelector.SetItem(recipe);
            }
            else
            {
                itemSelector.ClearItem(category);
            }
        }
    }
}