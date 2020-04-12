using System;
using UMA.CharacterSystem;
using UnityEngine;

public class SliderController : MonoBehaviour
{
    public GameObject SearchMe;
    public DynamicCharacterAvatar Avatar;   
    public string DnaName = "headSize";    // case matters here. 

    public void SetDNA(float Value)
    {
        if (Avatar == null) Avatar = FindAvatar();
        if (Avatar == null) return;

        // Set the DNA on the Avatar.
        // Case must match.
        // If you cache DNA, you must do it after the character is completely built
        var MyDNA = Avatar.GetDNA();
        if (MyDNA.ContainsKey(DnaName))
        {
            MyDNA[DnaName].Set(Value);
            Avatar.ForceUpdate(true); 
        }
    }

    private DynamicCharacterAvatar FindAvatar()
    {
       return SearchMe.GetComponentInChildren<DynamicCharacterAvatar>();
    }
}
