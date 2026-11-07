using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UMA;

namespace UMA
{

    public class ColorEffector : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public IColorSelector colorEffector;
        public string colorName;
        public OverlayColorData color;
        private Text categoryHeaderText;
        private string categoryHeaderDefault;
        private bool isHovered;

        public void SetCategoryHeader(Text headerText)
        {
            categoryHeaderText = headerText;
            categoryHeaderDefault = headerText != null ? headerText.text : string.Empty;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (categoryHeaderText == null || color == null) return;
            isHovered = true;
            categoryHeaderText.text = string.IsNullOrEmpty(color.name) ? "Unnamed Color" : color.name;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!isHovered) return;
            isHovered = false;
            if (categoryHeaderText != null) categoryHeaderText.text = categoryHeaderDefault;
        }

        private void OnDisable() => OnPointerExit(null);

        public void Setup(IColorSelector colorSelector, string colorName, OverlayColorData color)
        {
            this.colorEffector = colorSelector;
            this.colorName = colorName;
            this.color = color;
            Image image = GetComponent<Image>();
            if (!isWhite(color.displayColor))
            {
                image.color = color.displayColor;
                return;
            }
            image.color = color.color;
        }

        public bool isWhite(Color w)        {
            return w.r == 1f && w.g == 1f && w.b == 1f;
        }

        public void OnClick()
        {
            ColorChanged(color);
        }

        public void ColorChanged(OverlayColorData value)
        {
            colorEffector.SetColor(colorName, value);
        }
    }
}
