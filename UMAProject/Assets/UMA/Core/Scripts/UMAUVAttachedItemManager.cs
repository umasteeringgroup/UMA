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
        public Dictionary<string, UMAUVAttachedItem> attachedItems {
			get {
				return attachedItemLookup;
			}
		}
        public List<UMAUVAttachedItem> pendingAttachedItemsList = new List<UMAUVAttachedItem>();
        private UMAData umaData;
		public Dictionary<string, UMAUVAttachedItem> attachedItemLookup = new Dictionary<string, UMAUVAttachedItem>();

		// events
		public event System.Action<UMAData> UmaUvAttachedItemManagerUpdated;

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

        public void UMAUpdated(UMAData umaData)
        {
            UMAUVAttachedItemPreprocessor uMAUVAttachedItemPreprocessor = umaData.gameObject.GetComponent<UMAUVAttachedItemPreprocessor>();

            // Get all the hidden items. Manually call setup on them. They will be added to the
            // pendingAttachedItemsList with prefabStatus = ShouldBeDeactivated.
            // (all non-hidden items should be on the pendingAttachedItemsList with prefabStatus = ShouldBeActivated already
            //  this happens in the OnDnaAppliedBootstrapper function in UMAUVAttachedItemLauncher.cs )
            foreach (UMAUVAttachedItemLauncher ul in uMAUVAttachedItemPreprocessor.launchers)
            {
				if(ul != null) {
					ul.Setup(umaData, false);
				}
            }


            // these are the items that should be active.
            foreach (UMAUVAttachedItem item in pendingAttachedItemsList)
            {
                // items in pending list should either be active or inactive.
                if (attachedItemLookup.ContainsKey(item.sourceSlotName)) 
				{
					item.prefabInstance = attachedItemLookup[item.sourceSlotName].prefabInstance;
					attachedItemLookup.Remove(item.sourceSlotName);
                }
            }


			// any items left in attachedItemLookup should be destroyed -
			// they are not currently in the recipe, or they have been hidden or suppressed.

			// Items that are in AttachedItems, but should not exist, should be destroyed.
			foreach(var ai in attachedItemLookup.Values)
            {
                ai.prefabStatus = UMAUVAttachedItem.PrefabStatus.ShouldBeDeleted;
				ai.CleanUp();
            }
			attachedItemLookup.Clear();

			//********************************************************
			// Now go through all the slots we know about - the unhidden, the hidden, and the leftovers that should be deleted.
			// and process all of them.
			// The activating, deactivating, and deleting will happen in the ProcessSlot function.
			var slots = umaData.umaRecipe.GetFirstIndexedSlotsByTag();
            foreach (var item in pendingAttachedItemsList)
            {
                if (slots.ContainsKey(item.slotName))
                {
                    item.ProcessSlot(umaData, slots[item.slotName], avatar);
                }
                else
                {
                    item.ProcessSlot(umaData, null, avatar);
                }
				attachedItemLookup.Add(item.sourceSlotName, item);
            }
			UmaUvAttachedItemManagerUpdated?.Invoke(umaData); 
		}



        // This is updated. Now we need to find the slot for each one, and process it 
        public void OldUMAUpdated(UMAData umaData)
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
					item.prefabInstance = attachedItemLookup[item.sourceSlotName].prefabInstance;
                    saveAttachedItems.Add(item);
                    attachedItems.Remove(item.sourceSlotName);
                }
            }

            //TODO: get the UMAUVAttachedItemPreprocessor.
            // If the attached item is in there, then don't delete it. 
            // instead, just set it inactive.

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
            var slots = umaData.umaRecipe.GetFirstIndexedSlotsByTag();

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

		public void AddAttachedItem(UMAData umaData, UMAUVAttachedItemLauncher uMAUVAttachedItemLauncher, bool Activate) {
			UMAUVAttachedItem existingItem = null;

			for(int i = 0; i < pendingAttachedItemsList.Count; i++) {
				var pa = pendingAttachedItemsList[i];
				if(pa.sourceSlotName == uMAUVAttachedItemLauncher.sourceSlot.slotName) {
					existingItem = pa;
				}
			}

			// deactivated items should be added first. 
			// Activated items should be added second.
			// They should only be in here once...
			// But deactivated should override any previous status
			if(existingItem != null) 
			{
				// If this item is already in the list, and the new item is supposed to be activated.
				// it's either been deactivated, or something weird is going on. Just leave. 
				if( Activate) 
				{
					return;		
				}
				// Don't add an item twice. if for some reason (not sure how) we got an active item
				// before a deactivated item, just update it to be deactivated.
				existingItem.prefabStatus = UMAUVAttachedItem.PrefabStatus.ShouldBeDeactivated;
				return;
			}

			Debug.Log("Adding attached item: " + uMAUVAttachedItemLauncher.sourceSlot.slotName);
			UMAUVAttachedItem uvai = new UMAUVAttachedItem();
			uvai.Setup(umaData, uMAUVAttachedItemLauncher, Activate);


			pendingAttachedItemsList.Add(uvai);
			Debug.Log($"Pending list count {pendingAttachedItemsList.Count}");
		}
    }
}

