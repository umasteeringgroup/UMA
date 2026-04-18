using UnityEngine;
using UnityEngine.UI;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class OptionsGUI : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public UMATextRecipe wardrobeRecipe;

        private Toggle toggle;

        // Use this for initialization
        void Start()
        {
            toggle = GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(SetWardrobe);
        }

        void SetWardrobe(bool active)
        {
            if (wardrobeRecipe == null)
            {
                return;
            }

            if (active)
            {
                avatar.SetSlot(wardrobeRecipe);
            }
            else
            {
                avatar.ClearSlot(wardrobeRecipe.wardrobeSlot);
            }

            avatar.BuildCharacter();
        }
    }
}
