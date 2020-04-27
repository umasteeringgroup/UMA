using UnityEngine;
using System.Collections;
using UMA.CharacterSystem;
using UMA;

namespace UMA.CharacterSystem.Examples
{
    public class ColorHandler : MonoBehaviour
    {
        DynamicCharacterAvatar Avatar;
        string ColorName;
        OverlayColorData ColorValue;
        bool IsRemover;

        public void Setup(DynamicCharacterAvatar avatar, string colorName, OverlayColorData colorValue)
        {
            IsRemover = false;
            Avatar = avatar;
            ColorName = colorName;
            ColorValue = colorValue;
        }

        public void SetupRemover(DynamicCharacterAvatar avatar, string colorName)
        {
            IsRemover = true;
            Avatar = avatar;
            ColorName = colorName;
            ColorValue = new OverlayColorData(1);
        }

        public void OnClick()
        {
            if (IsRemover)
            {
                Avatar.ClearColor(ColorName);
            }
            else
            {
                Avatar.SetColor(ColorName, ColorValue);
            }
        }
    }
}
