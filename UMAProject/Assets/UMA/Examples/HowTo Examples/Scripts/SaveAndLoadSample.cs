using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA;
using UnityEngine;

public class SaveAndLoadSample : MonoBehaviour
{
    public DynamicCharacterAvatar Avatar;
    public UMARandomAvatar Randomizer;

    public string saveString;
    
    public void GenerateANewUMA()
    {
        Randomizer.Randomize(Avatar);
        Avatar.BuildCharacter(false);
    }

    public void SaveUMA()
    {

    }

    public void LoadUMA()
    {

    }
}
