using UnityEngine;
using UnityEngine.UI;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class ColorPickerGUI : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public string sharedColorName = "";

        private Button button;

        // Use this for initialization
        void Start()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(SetColor);
        }

        void SetColor()
        {
            // Change only the albedo tint; the color's other channels and shader parameters stay as they are.
            OverlayColorData current = avatar.GetColor(sharedColorName);
            OverlayColorData color = current != null ? current.Clone() : new OverlayColorData(3);
            color.channelMask[0] = button.image.color;
            avatar.SetRawColor(sharedColorName, color, false);
            avatar.UpdateColors(true);
        }
    }
}
