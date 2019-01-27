using System.Collections;
using System.Collections.Generic;
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
            avatar.SetColor(sharedColorName, button.image.color);
            avatar.UpdateColors(true);
        }
    }
}
