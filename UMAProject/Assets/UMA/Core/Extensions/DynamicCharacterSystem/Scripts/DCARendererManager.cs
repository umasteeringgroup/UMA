using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace UMA.CharacterSystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    public class DCARendererManager : MonoBehaviour
    {
        [System.Serializable]
        public class RendererElement
        {
            public UMARendererAsset rendererAsset;
            public List<SlotDataAsset> slotAssets = new List<SlotDataAsset>();
            public List<string> wardrobeSlots = new List<string>();
        }
        public List<RendererElement> RendererElements = new List<RendererElement>();

        private DynamicCharacterAvatar avatar;
        private UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
        List<SlotDataAsset> wardrobeSlotAssets = new List<SlotDataAsset>();
        private UMAContext context;

        // Use this for initialization
        void Start()
        {
            avatar = GetComponent<DynamicCharacterAvatar>();
            avatar.CharacterBegun.AddListener(CharacterBegun);
            context = UMAContext.FindInstance();
        }

        void CharacterBegun(UMAData umaData)
        {
            //If mesh is not dirty then we haven't changed slots.
            if (!umaData.isMeshDirty)
                return;

            SlotData[] slots = umaData.umaRecipe.slotDataList;

            foreach(RendererElement element in RendererElements)
            {
                wardrobeSlotAssets.Clear();

                //First, lets collect a list of the slotDataAssets that are present in the wardrobe recipes of the wardrobe slots we've specified
                foreach (string wardrobeSlot in element.wardrobeSlots)
                {
                    UMATextRecipe recipe = avatar.GetWardrobeItem(wardrobeSlot);
                    if (recipe != null)
                    {
                        recipe.Load(umaRecipe, context);

                        if (umaRecipe.slotDataList != null)
                        {
                            for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
                            {
                                SlotData slotData = umaRecipe.slotDataList[i];
                                if(slotData != null && slotData.asset != null)
                                    wardrobeSlotAssets.Add(slotData.asset);
                            }
                        }
                    }
                }

                //Next, check each slot for if they are in the list of specified slots or exist in one of the wardrobe recipes of the wardrobe slot we specified.
                foreach (SlotData slot in slots)
                {
                    if (element.slotAssets.Contains(slot.asset) || wardrobeSlotAssets.Contains(slot.asset))
                    {
                        slot.rendererAsset = element.rendererAsset;
                    }
                }
            }
            wardrobeSlotAssets.Clear();
        }
    }
}
