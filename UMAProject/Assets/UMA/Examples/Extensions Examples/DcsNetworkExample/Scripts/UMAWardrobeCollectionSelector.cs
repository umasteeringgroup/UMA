using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UMA.CharacterSystem
{
    public class UMAWardrobeCollectionSelector : MonoBehaviour
    {
        public string WardrobeName = "";
        public DynamicCharacterAvatar avatar 
        {
            get { return _avatar; }
            set { _avatar = value; Initalize(); }
        }
        private DynamicCharacterAvatar _avatar;

        private Dropdown _dropdown;
        private List<UMATextRecipe> _availableRecipes = new List<UMATextRecipe>();

        private uint wardrobeMask = 0;
        private string previousRace = "";

        void Start()
        {
            _dropdown = GetComponent<Dropdown>();
        }

        private void Initalize()
        {
            if (_avatar == null)
                return;

            _avatar.umaData.CharacterUpdated.AddListener(OnCharacterUpdated);
            previousRace = _avatar.activeRace.name;

            if (WardrobeName == "Hair")     wardrobeMask = NetworkDCA.HairMask;
            if (WardrobeName == "Feet")     wardrobeMask = NetworkDCA.FeetMask;
            if (WardrobeName == "Chest")    wardrobeMask = NetworkDCA.ChestMask;
            if (WardrobeName == "Legs")     wardrobeMask = NetworkDCA.LegsMask;
            if(WardrobeName == "Hands")     wardrobeMask = NetworkDCA.HandsMask;
            if(WardrobeName == "Helmet")    wardrobeMask = NetworkDCA.HelmetMask;
            if (WardrobeName == "Underwear")wardrobeMask = NetworkDCA.UnderwearMask;
            
            UpdateDropdown();
        }

        private void OnCharacterUpdated(UMAData umaData)
        {
            UpdateDropdown();

            if (_avatar.activeRace.name != previousRace)
            {
                previousRace = _avatar.activeRace.name;
                _dropdown.value = 0;
            }
        }

        public void UpdateDropdown()
        {
            if (string.IsNullOrEmpty(WardrobeName))
                Debug.LogWarning("WardrobeName is empty!");
            UpdateAvailableRecipes(WardrobeName);
            UpdateOptionsWithRecipes();
        }

        public void OnSelect(int index)
        {
            if (_avatar == null)
                return;

            string optionName = _dropdown.options[index].text;
            if (optionName == "none")
                optionName = "";

            _avatar.GetComponent<NetworkDCA>().CmdSetWardrobe(wardrobeMask, optionName);
        }

        public void UpdateAvailableRecipes(string recipeType)
        {
            if (_avatar == null)
            {
                Debug.LogError("Avatar is null!");
                return;
            }

            _availableRecipes = new List<UMATextRecipe>();
            Dictionary<string, List<UMATextRecipe>> recipes = _avatar.AvailableRecipes;

            if (recipes.ContainsKey(recipeType))
                _availableRecipes = recipes[recipeType];                
        }

        public void UpdateOptionsWithRecipes()
        {
            if (_avatar == null)
            {
                Debug.LogError("Avatar is null!");
                return;
            }

            if (_dropdown == null)
                return;

            _dropdown.ClearOptions();
            if (_availableRecipes == null || _availableRecipes.Count == 0)
            {
                List<string> names = new List<string>();
                names.Add("none");
                _dropdown.AddOptions(names);
            }
            else
            {
                List<string> names = getNames(_availableRecipes);
                names.Insert(0, "none");
                _dropdown.AddOptions(names);
            }
        }

        private List<string> getNames(List<UMATextRecipe> recipes)
        {
            List<string> names = new List<string>();

            foreach (UMATextRecipe recipe in recipes)
                names.Add(recipe.name);

            return names;
        }
    }
}
