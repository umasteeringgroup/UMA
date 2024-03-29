using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

public class UMAUVAttachedItemPreprocessor : MonoBehaviour
{
    DynamicCharacterAvatar avatar;
    public List<UMAUVAttachedItemLauncher> launchers = new List<UMAUVAttachedItemLauncher>();

    /// <summary>
    /// Awake is called when the script instance is being loaded. This is before Start()
    /// This is done so that the preprocessor can add the event listeners before the avatar is built.
    /// </summary>
    void Awake()
    {
        avatar = GetComponent<DynamicCharacterAvatar>();
        avatar.BuildCharacterBegun.AddListener(OnBuildCharacterBegun);
        avatar.WardrobeSuppressed.AddListener(OnWardrobeSuppressed);
        avatar.SlotsHidden.AddListener(OnSlotsHidden);
    }

    /// <summary>
    /// This is called when the character is being built in BuildCharacter()
    /// </summary>
    /// <param name="umaData"></param>
    private void OnBuildCharacterBegun(UMAData umaData)
    {
        launchers = new List<UMAUVAttachedItemLauncher>();
    }

    /// <summary>
    /// This is called when the character has loaded all the slots/overlays in LoadCharacter()
    /// </summary>
    /// <param name="hiddenSlots"></param>
    private void OnSlotsHidden(List<SlotData> hiddenSlots)
    {

        for (int i1 = 0; i1 < hiddenSlots.Count; i1++)
        {
            SlotData slot = hiddenSlots[i1];
            if (slot.asset.SlotProcessed == null)
            {
                continue;
            }
            if (slot.asset.SlotProcessed.GetPersistentEventCount() == 0)
            {
                continue;
            }

            for(int i = 0; i < slot.asset.SlotProcessed.GetPersistentEventCount(); i++)
            {
                var target = slot.asset.SlotProcessed.GetPersistentTarget(i);
                if (target is UMAUVAttachedItemLauncher)
                {
                    launchers.Add(target as UMAUVAttachedItemLauncher);
                }
            }
        }
    }

    /// <summary>
    /// This is called when the character has loaded all the slots/overlays in LoadCharacter()
    /// </summary>
    /// <param name="suppressedRecipes"></param>
    private void OnWardrobeSuppressed(List<UMATextRecipe> suppressedRecipes)
    {
        for (int i1 = 0; i1 < suppressedRecipes.Count; i1++)
        {
            UMATextRecipe recipe = suppressedRecipes[i1];
            var items = UMAAssetIndexer.Instance.GetAssetItems(recipe);
            for (int i2 = 0; i2 < items.Count; i2++)
            {
                AssetItem ai = items[i2];
                if (ai._Type == typeof(SlotDataAsset))
                {
                    var slot = ai.Item as SlotDataAsset;
                    if (slot.SlotProcessed != null)
                    {
                        if (slot.SlotProcessed.GetPersistentEventCount() == 0)
                        {
                            continue;
                        }
                        for (int i = 0; i < slot.SlotProcessed.GetPersistentEventCount(); i++)
                        {
                            var target = slot.SlotProcessed.GetPersistentTarget(i);
                            if (target is UMAUVAttachedItemLauncher)
                            {
                                launchers.Add(target as UMAUVAttachedItemLauncher);
                            }
                        }
                    }
                    launchers.Add(ai.Item as UMAUVAttachedItemLauncher);
                }
            }
        }
    }
}