using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

public class ConstructDCAFromScratch : MonoBehaviour
{
    /// <summary>
    /// Note: It is generally recommended to create predefined prefabs, with the default wardrobe setup
    /// as needed, which you can then just instantiate. This really is doing things "the hard way". 
    /// </summary>
    public string raceName = "HumanFemale";
    public RuntimeAnimatorController raceController;
    public List<UMAWardrobeRecipe> wardrobeItems;
    public Color hairColor = Color.red;

    // Start is called before the first frame update
    void Start()
    {
        GameObject go = new GameObject();
        var DCA = go.AddComponent<DynamicCharacterAvatar>();

        // Set the race
        DCA.RacePreset = raceName;
        DCA.raceAnimationControllers.defaultAnimationController = raceController;

        // Set any predefined DNA.
        DCA.predefinedDNA = new UMA.UMAPredefinedDNA();
        DCA.predefinedDNA.AddDNA("headSize", 0.9f);

        // Setup any wardrobe items you want to preload.
        foreach(UMAWardrobeRecipe recipe in wardrobeItems)
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

    // Update is called once per frame
    void Update()
    {
        
    }
}
