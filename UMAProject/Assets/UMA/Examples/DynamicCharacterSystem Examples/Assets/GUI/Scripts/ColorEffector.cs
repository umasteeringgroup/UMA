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
            image.color = color.color;
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