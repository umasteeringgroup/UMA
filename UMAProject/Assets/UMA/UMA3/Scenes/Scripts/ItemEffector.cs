using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UMA
{

    public class ItemEffector : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public IItemSelector itemSelector;
        public UMAWardrobeRecipe recipe;
        public string category;
        public Sprite clearImage;

        // Hover header support
        private Text categoryHeaderText;
        private string categoryHeaderDefault;
        private bool presetMode;
        private UMAPreset preset;
        private System.Action<UMAPreset> selectPreset;
        private Sprite ownedPresetSprite;

        public void SetupPreset(UMAPreset value, System.Action<UMAPreset> onSelected)
        {
            ReleasePresetSprite();
            presetMode = true;
            preset = value;
            selectPreset = onSelected;
            recipe = null;
            itemSelector = null;
            if (value != null && value.Icon != null)
                ownedPresetSprite = Sprite.Create(value.Icon, new Rect(0, 0, value.Icon.width, value.Icon.height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            foreach (var image in GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (image.name == "ItemImage")
                {
                    image.sprite = ownedPresetSprite;
                    image.preserveAspect = true;
                    image.enabled = true;
                }
            var label = GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (label != null) label.text = ownedPresetSprite != null || value == null ? "" : value.name;
        }

        private void ReleasePresetSprite()
        {
            if (ownedPresetSprite == null) return;
            if (Application.isPlaying) Destroy(ownedPresetSprite);
            else DestroyImmediate(ownedPresetSprite);
            ownedPresetSprite = null;
        }

        private void OnDestroy() => ReleasePresetSprite();


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
            ReleasePresetSprite();
            presetMode = false;
            preset = null;
            selectPreset = null;
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
            if (presetMode)
            {
                if (preset != null) selectPreset?.Invoke(preset);
                return;
            }
            if (recipe != null)
            {
                itemSelector.SetItem(recipe);
            }
            else
            {
                itemSelector.ClearItem(category);
            }
        }

        /// <summary>
        /// Store a reference to the category header Text so hover can update it.
        /// </summary>
        public void SetCategoryHeader(Text headerText)
        {
            categoryHeaderText = headerText;
            if (categoryHeaderText != null)
            {
                categoryHeaderDefault = categoryHeaderText.text;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (presetMode)
            {
                if (preset == null || categoryHeaderText == null) return;
                categoryHeaderDefault = categoryHeaderText.text;
                categoryHeaderText.text = preset.name;
                return;
            }
            if (recipe == null || categoryHeaderText == null)
                return;

            categoryHeaderDefault = categoryHeaderText.text;
            string displayName = !string.IsNullOrEmpty(recipe.DisplayValue) ? recipe.DisplayValue : recipe.name;
            categoryHeaderText.text = displayName;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (categoryHeaderText == null)
                return;

            categoryHeaderText.text = categoryHeaderDefault;
        }
    }
}
