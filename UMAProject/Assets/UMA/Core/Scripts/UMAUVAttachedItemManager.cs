#define USING_BAKEMESH
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UMA;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEngine;


namespace UMA
{
    public class UMAUVAttachedItemManager : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public Dictionary<string, UMAUVAttachedItem> attachedItems = new Dictionary<string, UMAUVAttachedItem>();
        public SkinnedMeshRenderer skin;
        public List<UMAUVAttachedItem> pendingAttachedItemsList = new List<UMAUVAttachedItem>();
        private UMAData umaData;

        public void Start()
        {
            //Debug.Log($"Start {GetInstanceID()}");
        }

        public void OnDestroy()
        {
            //Debug.Log($"Destroy {GetInstanceID()}");
            if (gameObject != null)
            {
                //Debug.Log($"Destroying Item {GetInstanceID()}");
                foreach(var attachedItem in attachedItems.Values)
                {
                    attachedItem.CleanUp();
                }
            }
        }

        public void OnEnable()
        {
            //Debug.Log($"Enable {GetInstanceID()}");
        }
        public void OnDisable()
        {
            //Debug.Log($"Disable {GetInstanceID()}");
        }


        public void Setup(UMAData umaData)
        {
            Debug.Log("Manager Setup");
            avatar = umaData.GetComponent<DynamicCharacterAvatar>();
            avatar.CharacterUpdated.AddListener(UMAUpdated);
            avatar.CharacterBegun.AddListener(UMABegun);
            this.umaData = umaData;
        }

        public void UMABegun(UMAData umaData)
        {
            Debug.Log("Manager UMABegun. Clearing Pending Items");
            pendingAttachedItemsList.Clear();
        }

        // This is updated. Now we need to find the slot for each one, and process it 
        public void UMAUpdated(UMAData umaData)
        {
            Debug.Log("Manager UMAUpdated");
            // step one:
            // Remove all recipes, except ones that are in this update. These will be in the 
            // "pendingAttachedItemsList".

            List<UMAUVAttachedItem> saveAttachedItems = new List<UMAUVAttachedItem>();

            // save the items that we already have, and then remove them
            // so we only have unused items in the list
            foreach(UMAUVAttachedItem item in pendingAttachedItemsList)
            {
                if (attachedItems.ContainsKey(item.sourceSlotName))
                {
                    saveAttachedItems.Add(item);
                    attachedItems.Remove(item.sourceSlotName);
                }
            }

            // the items that we don't have are left in attachedItems. 
            // These need to be destroyed. 
            // TODO: Ask anthony should we destroy and recreate them?
            foreach(var uv in attachedItems.Values)
            {
                uv.CleanUp();
            }
            attachedItems.Clear();  

            // Add the items back to the list.
            foreach(var item in saveAttachedItems)
            {
                attachedItems.Add(item.sourceSlotName, item);
            }
            foreach(var item in pendingAttachedItemsList)
            {
                if (!attachedItems.ContainsKey(item.sourceSlotName))
                {
                    attachedItems[item.sourceSlotName] = item;
                }
            }

            // find the slot in the recipe.
            // find the overlay in the recipe
            // calculate the new UV coordinates (in case it changed).
            // Loop through the vertexes, and find the one with the closest UV.
            skin = umaData.GetRenderer(0);
            var slots = umaData.umaRecipe.GetIndexedSlotsByTag();

            foreach(var item in attachedItems.Values)
            {
                if (slots.ContainsKey(item.slotName))
                {
                    item.ProcessSlot(umaData, slots[item.slotName], avatar);
                }
            }
        }

        public void LateUpdate()
        {
			if(umaData == null)
				return;
            SkinnedMeshRenderer skin = umaData.GetRenderer(0);
            foreach(UMAUVAttachedItem item in attachedItems.Values)
            {
                item.DoLateUpdate(skin, transform, avatar);
            }
        }

        public void AddAttachedItem(UMAData umaData, UMAUVAttachedItemLauncher uMAUVAttachedItemLauncher)
        {
            Debug.Log("Adding attached item: " + uMAUVAttachedItemLauncher.sourceSlot.slotName);
            UMAUVAttachedItem uvai = new UMAUVAttachedItem();
            uvai.Setup(umaData, uMAUVAttachedItemLauncher);
            pendingAttachedItemsList.Add(uvai);
            Debug.Log($"Pending list count {pendingAttachedItemsList.Count}");
        }
    }
}

