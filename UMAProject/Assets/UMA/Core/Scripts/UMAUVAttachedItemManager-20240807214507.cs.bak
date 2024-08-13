#define USING_BAKEMESH
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;


namespace UMA
{
    public class UMAUVAttachedItemManager : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public Dictionary<string, UMAUVAttachedItem> attachedItems = new Dictionary<string, UMAUVAttachedItem>();
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
            //********************************************************
            // get all currently attached items, both active and inactive.
            // index them so we can find them.
            UMAUVAttachedItemPreprocessor uMAUVAttachedItemPreprocessor = umaData.gameObject.GetComponent<UMAUVAttachedItemPreprocessor>();
            Dictionary<string, UMAUVAttachedItem> indexedAttachedItems = new Dictionary<string, UMAUVAttachedItem>();

            UMAUVAttachedItem[] currentlyAttachedItems = GetComponentsInChildren<UMAUVAttachedItem>(true);
            for (int i = 0; i < currentlyAttachedItems.Length; i++)
            {
                UMAUVAttachedItem item = currentlyAttachedItems[i];
                indexedAttachedItems.Add(item.sourceSlotName, item);
            }
            //********************************************************

            // Get all the hidden items. Manually call setup on them. They will be added to the
            // pendingAttachedItemsList with prefabStatus = ShouldBeDeactivated.
            // (all non-hidden items should be on the pendingAttachedItemsList with prefabStatus = ShouldBeActivated already
            //  this happens in the OnDnaAppliedBootstrapper function in UMAUVAttachedItemLauncher.cs )
            for (int i = 0; i < uMAUVAttachedItemPreprocessor.launchers.Count; i++)
            {
                UMAUVAttachedItemLauncher ul = uMAUVAttachedItemPreprocessor.launchers[i];
                ul.Setup(umaData, true);
            }


            // these are the items that should be active.
            for (int i = 0; i < pendingAttachedItemsList.Count; i++)
            {
                UMAUVAttachedItem item = pendingAttachedItemsList[i];
                // items in pending list should either be active or inactive.
                if (indexedAttachedItems.ContainsKey(item.sourceSlotName))
                {                
                    indexedAttachedItems.Remove(item.sourceSlotName);
                }
            }


            // any items left in indexedAttachedItems should be destroyed -
            // they are not currently in the recipe, or they have been hidden or suppressed.

            // Items that are in AttachedItems, but should not exist, should be destroyed.
            foreach (var ai in indexedAttachedItems.Values)
            {
                ai.prefabStatus = UMAUVAttachedItem.PrefabStatus.ShouldBeDeleted;
                pendingAttachedItemsList.Add(ai);
            }
			attachedItemLookup.Clear();

            //********************************************************
            // Now go through all the slots we know about - the unhidden, the hidden, and the leftovers that should be deleted.
            // and process all of them.
            // The activating, deactivating, and deleting will happen in the ProcessSlot function.
            var slots = umaData.umaRecipe.GetFirsIndexedSlotsByTag();
            for (int i = 0; i < pendingAttachedItemsList.Count; i++)
            {
                UMAUVAttachedItem item = pendingAttachedItemsList[i];
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


        public void LateUpdate()
        {
			if(umaData == null)
            {
                return;
            }

            SkinnedMeshRenderer skin = umaData.GetRenderer(0);
            foreach(UMAUVAttachedItem item in attachedItems.Values)
            {
                item.DoLateUpdate(skin, transform, avatar);
            }
        }

        public void AddAttachedItem(UMAData umaData, UMAUVAttachedItemLauncher uMAUVAttachedItemLauncher, bool Activate)
        {
            Debug.Log("Adding attached item: " + uMAUVAttachedItemLauncher.sourceSlot.slotName);
            UMAUVAttachedItem uvai = new UMAUVAttachedItem();
            uvai.Setup(umaData, uMAUVAttachedItemLauncher,Activate);
            pendingAttachedItemsList.Add(uvai);
            Debug.Log($"Pending list count {pendingAttachedItemsList.Count}");
        }
    }
}

