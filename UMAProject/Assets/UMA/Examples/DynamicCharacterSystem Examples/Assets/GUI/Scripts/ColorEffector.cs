using UnityEngine;
using UnityEngine.UI;
using UMA;

namespace UMA
{

    public class ColorEffector : MonoBehaviour
    {
        public IColorSelector colorEffector;
        public string colorName;
        public OverlayColorData color;

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