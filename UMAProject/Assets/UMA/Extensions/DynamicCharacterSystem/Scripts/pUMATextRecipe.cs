using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;

public partial class UMATextRecipe : UMAPackedRecipeBase
{

    public string recipeType = "Standard";

    //we dont need this any more its been replaced by race. Its only here incase Jaimi needs it for some reason...
    public UMACharacterSystem.Sex Sex;

    [SerializeField]
    public List<string> compatibleRaces = new List<string>();

    [SerializeField]
    public List<WardrobeRecipeThumb> wardrobeRecipeThumbs = new List<WardrobeRecipeThumb>();

    public string wardrobeSlot = "None";

    [SerializeField]
    public List<string> Hides = new List<string>();

    [SerializeField]
    public List<string> suppressWardrobeSlots = new List<string>();

    public Sprite GetWardrobeRecipeThumbFor(string racename)
    {
        Sprite foundSprite = null;
        if (wardrobeRecipeThumbs.Count > 0)
        {
            foreach (WardrobeRecipeThumb wdt in wardrobeRecipeThumbs)
            {
                //Set a default when the first option with a value is found
                if (foundSprite == null && wdt.thumb != null){
                    foundSprite = wdt.thumb;
                }
                //Override that if there is a specific one is found for this race
                if (wdt.race == racename)
                {
                    if(wdt.thumb != null)
                    {
                        foundSprite = wdt.thumb;
                    }
                }
            }
        }
        return foundSprite;
    }
    
    //New stuff added for json saving
    /// <summary>
    /// Deserialize recipeString data into packed recipe.
    /// </summary>
    /// <returns>The packed recipe.</returns>
    /// <param name="context">Context.</param>
    public UMAPackRecipeCharacterSystem PackedLoadCharacterSystem(UMAContext context)
    {
        if ((recipeString == null) || (recipeString.Length == 0))
        {
            Debug.LogWarning("[pUMATextRecipe.PackedLoadCharacterSystem] Recipe string was empty!");
            return new UMAPackRecipeCharacterSystem();
        }
        return JsonUtility.FromJson<UMAPackRecipeCharacterSystem>(recipeString);
    }

    /// <summary>
    /// Serialize recipeString data into packed recipe.
    /// </summary>
    /// <param name="packedRecipe">Packed recipe.</param>
    /// <param name="context">Context.</param>
    public void PackedSaveCharacterSystem(UMAPackedRecipeBase.UMAPackRecipe packedRecipe, UMAContext context)
    {
        recipeString = JsonUtility.ToJson(packedRecipe);
    }

    /// <summary>
    /// Load data into the specified UMA recipe.
    /// </summary>
    /// <param name="umaRecipe">UMA recipe.</param>
    /// <param name="context">Context.</param>
    public void LoadCharacterSystem(UMA.UMADataCharacterSystem.UMACharacterSystemRecipe umaRecipe, UMAContext context)
    {
        var packedRecipe = PackedLoadCharacterSystem(context);
        switch (packedRecipe.version)
        {
            case 2:
                UnpackRecipeVersion2(umaRecipe, packedRecipe, context);
                umaRecipe.wardrobeRecipes = WardrobeRecipesUnStringifyJson(packedRecipe.wardrobeRecipesJson);
                
                break;

            case 1:
            default:
                if (UnpackRecipeVersion1(umaRecipe, packedRecipe, context))
                {
                    umaRecipe.MergeMatchingOverlays();
                    umaRecipe.wardrobeRecipes = WardrobeRecipesUnStringifyJson(packedRecipe.wardrobeRecipesJson);
                }
                break;
        }
    }

    /// <summary>
    /// Save data from the specified UMA recipe.
    /// </summary>
    /// <param name="umaRecipe">UMA recipe.</param>
    /// <param name="context">Context.</param>
    /// <param name="wardrobeRecipes">Wardrobe recipes.</param>
    public void SaveCharacterSystem(UMA.UMAData.UMARecipe umaRecipe, UMAContext context, Dictionary<string, UMATextRecipe> wardrobeRecipes = null)
    {
        UMA.UMAData.UMARecipe umaRecipeToSave = umaRecipe.Mirror();
        umaRecipeToSave.sharedColors = umaRecipe.sharedColors;
        umaRecipeToSave.MergeMatchingOverlays();
        var packedRecipe = PackRecipeV2(umaRecipeToSave);
        var packedCharacterSystemRecipe = new UMAPackRecipeCharacterSystem(packedRecipe);
        if (wardrobeRecipes != null)
        {
            packedCharacterSystemRecipe.wardrobeRecipesJson = WardrobeRecipesStringifyJson(wardrobeRecipes);
        }
        //TODO Check if we can destroy here or not
        //Destroy (packedRecipe);
        PackedSaveCharacterSystem(packedCharacterSystemRecipe, context);
    }

    public WardrobeSettings[] WardrobeRecipesStringifyJson(Dictionary<string, UMATextRecipe> wardrobeRecipes)
    {
        List<WardrobeSettings> wardrobeRecipeStringifiedJson = new List<WardrobeSettings>();
        foreach (KeyValuePair<string, UMATextRecipe> kp in wardrobeRecipes)
        {
            wardrobeRecipeStringifiedJson.Add(new WardrobeSettings(kp.Key, kp.Value.name));
        }
        return wardrobeRecipeStringifiedJson.ToArray();
    }

    public Dictionary<string,string> WardrobeRecipesUnStringifyJson(WardrobeSettings[] wardrobeList)
    {
        Dictionary<string, string> wardrobeRecipesUnStringified = new Dictionary<string, string>();
        foreach(WardrobeSettings ws in wardrobeList)
        {
            wardrobeRecipesUnStringified.Add(ws.slot, ws.recipe);
        }
        return wardrobeRecipesUnStringified;
    }

    [System.Serializable]
    public class WardrobeSettings
    {
        public string slot;
        public string recipe;
        public WardrobeSettings()
        {

        }
        public WardrobeSettings(string _slot, string _recipe)
        {
            slot = _slot;
            recipe = _recipe;
        }
    }

    [System.Serializable]
    public class UMAPackRecipeCharacterSystem : UMAPackRecipe
    {
        [SerializeField]
        public WardrobeSettings[] wardrobeRecipesJson = new WardrobeSettings[0]; 

        public UMAPackRecipeCharacterSystem()
        {

        }
        public UMAPackRecipeCharacterSystem(UMAPackRecipe umaPackRecipe)
        {
            version = umaPackRecipe.version;
            packedSlotDataList = umaPackRecipe.packedSlotDataList;
            slotsV2 = umaPackRecipe.slotsV2;
            colors = umaPackRecipe.colors;
            fColors = umaPackRecipe.fColors;
            sharedColorCount = umaPackRecipe.sharedColorCount;
            race = umaPackRecipe.race;
            umaDna = umaPackRecipe.umaDna;
            packedDna = umaPackRecipe.packedDna;
        }
    }
}
