using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    public class ConstructDCAFromAPrefab : MonoBehaviour
    {
        public string raceName = "HumanFemale";
        public RuntimeAnimatorController raceController;
        public List<UMAWardrobeRecipe> wardrobeItems;
        public Color hairColor = Color.red;
        public GameObject DCAPrefab;
        [TextArea(3, 12)]
        public string CharacterString;

        // Start is called before the first frame update
        void Start()
        {
            GameObject go = GameObject.Instantiate(DCAPrefab);
            var DCA = go.GetComponent<DynamicCharacterAvatar>();

            // Just load some items into the character.
            if (string.IsNullOrEmpty(CharacterString))
            {
                // Change the race if we want
                DCA.RacePreset = raceName;

                // Set any predefined DNA.
                DCA.predefinedDNA = new UMA.UMAPredefinedDNA();
                DCA.predefinedDNA.AddDNA("headSize", 0.9f);

                // Setup any wardrobe items you want to preload.
                // Or don't do this, and use whatever is on the prefab
                foreach (UMAWardrobeRecipe recipe in wardrobeItems)
                {
                    DCA.preloadWardrobeRecipes.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(recipe));
                }

                // Setup any predefined colors
                // This version of "SetColor" only sets the albedo on the first texture 
                // channel.
                // If you need full control over color channels, use DCA.SetRawColor("Hair",overlayColorData);
                DCA.SetColor("Hair", hairColor);

                // Set any predefined wardrobe items.
                go.transform.position = new Vector3(0f, 0.5f, 0f);
                go.SetActive(true);
            }
            else
            {
                DCA.LoadAvatarDefinition(CharacterString);
                go.transform.position = new Vector3(0f, 0.5f, 0f);
                go.SetActive(true);
            }
        }
    }
}
