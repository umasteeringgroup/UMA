using UnityEngine;
using UMA.CharacterSystem;
using System.Collections.Generic;
using UnityEngine.UI;

namespace UMA.CharacterSystem.Examples
{
    public class SlotHandler : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;
        public GameObject WardrobePanel;
        public GameObject WardrobeButtonPrefab;
        public GameObject LabelPrefab;
        public string SlotName;



        public void Setup(DynamicCharacterAvatar avatar, string slotName, GameObject wardrobePanel)
        {
            Avatar = avatar;
            SlotName = slotName;
            WardrobePanel = wardrobePanel;
        }

        public void OnClick()
        {
            // Get the available UMATextRecipes for this slot.
            List<UMATextRecipe> SlotRecipes = Avatar.AvailableRecipes[SlotName];
            // Cleanup old buttons
            Cleanup();

            AddLabel(SlotName);
            AddButton("Remove", SlotName);

            // Find all the wardrobe items for the current slot, and create a button for them.
            foreach (UMATextRecipe utr in SlotRecipes)
            {
                string name;
                if (string.IsNullOrEmpty(utr.DisplayValue))
                    name = utr.name;
                else
                    name = utr.DisplayValue;

                AddButton(name, SlotName, utr);
            }
        }

        private void AddLabel(string theText)
        {
            GameObject go = GameObject.Instantiate(LabelPrefab);
            go.transform.SetParent(WardrobePanel.transform);
            Text txt = go.GetComponentInChildren<Text>();
            txt.text = theText;
        }

        private void AddButton(string theText, string SlotName, UMATextRecipe utr = null)
        {
            GameObject go = GameObject.Instantiate(WardrobeButtonPrefab);
            WardrobeHandler wh = go.GetComponent<WardrobeHandler>();
            wh.Setup(Avatar, utr, SlotName);
            Text txt = go.GetComponentInChildren<Text>();
            txt.text = theText;
            go.transform.SetParent(WardrobePanel.transform);
        }

        private void Cleanup()
        {
            if (WardrobePanel.transform.childCount > 0)
            {
                foreach (Transform t in WardrobePanel.transform)
                {
                    UMAUtils.DestroySceneObject(t.gameObject);
                }
            }
        }
    }
}
