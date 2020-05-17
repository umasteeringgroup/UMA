using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA;
using UnityEngine;
using UnityEngine.UI;

public class SaveAndLoadSample : MonoBehaviour
{
    public DynamicCharacterAvatar Avatar;
    public UMARandomAvatar Randomizer;
    public Button LoadButton;
    public bool useAvatarDefinition;

    public string saveString;
    
    public void GenerateANewUMA()
    {
        Randomizer.Randomize(Avatar);
        Avatar.BuildCharacter(false);
    }

    public void SaveUMA()
    {
        if (useAvatarDefinition)
        {
            saveString = Avatar.GetAvatarDefinitionString(true);
        }
        else
        {
            saveString = Avatar.GetCurrentRecipe();
        }
        LoadButton.interactable = true;
    }

    public void LoadUMA()
    {
        if (string.IsNullOrEmpty(saveString))
            return;
        if (useAvatarDefinition)
        {
            Avatar.LoadAvatarDefinition(saveString);
            Avatar.BuildCharacter();
        }
        else
        {
            Avatar.LoadFromRecipeString(saveString);
        }
    }
}
