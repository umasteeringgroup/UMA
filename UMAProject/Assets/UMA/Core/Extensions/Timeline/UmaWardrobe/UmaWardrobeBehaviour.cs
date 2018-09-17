using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA;
using UMA.CharacterSystem;

[Serializable]
public class UmaWardrobeBehaviour : PlayableBehaviour 
{
    public enum WardrobeOptions
    {
        AddRecipes,
        ClearSlots,
        ClearAllSlots
    }

    public WardrobeOptions wardrobeOption = 0;
    public List<UMAWardrobeRecipe> recipesToAdd = new List<UMAWardrobeRecipe>();
    public List<string> slotsToClear = new List<string>();
    [Tooltip("Whether to rebuild the uma avatar immediately on setting/clearing or instead accummulate the changes.")]
    public bool rebuildImmediately = true;

	[HideInInspector]
	public bool isAdded = false;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        bool dcaUpdated = false;
        DynamicCharacterAvatar avatar = playerData as DynamicCharacterAvatar;
        if (avatar == null)
            return;

        if (!isAdded)
        {
            isAdded = true;
            dcaUpdated = true;

            if (wardrobeOption == UmaWardrobeBehaviour.WardrobeOptions.AddRecipes)
            {
                if (recipesToAdd != null)
                {
                    foreach (UMAWardrobeRecipe recipe in recipesToAdd)
                    {
                        avatar.SetSlot(recipe);
                        dcaUpdated = true;
                    }
                }
                else
                {
                    if(Debug.isDebugBuild)
                        Debug.LogWarning("Wardrobe recipes not set!");
                }
            }

            if (wardrobeOption == UmaWardrobeBehaviour.WardrobeOptions.ClearSlots)
            {
                avatar.ClearSlots(slotsToClear);
                dcaUpdated = true;
            }

            if (wardrobeOption == UmaWardrobeBehaviour.WardrobeOptions.ClearAllSlots)
            {
                avatar.ClearSlots();
                dcaUpdated = true;
            }
        }

        if (dcaUpdated && rebuildImmediately)
        {
            avatar.BuildCharacter();
        }
    }
}
