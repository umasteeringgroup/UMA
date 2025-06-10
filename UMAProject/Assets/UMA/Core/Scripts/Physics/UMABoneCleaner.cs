using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

// UMA "Extra Bone Removal System" Butchered by SecretAnorak, clever parts written by Jaimi (UMA Developer Extraordinaire)

namespace UMA
{
    public class UMABoneCleaner : MonoBehaviour
	{
		private List<string> KillBones = new List<string>();
		private List<Transform> AllExceptions = new List<Transform>();

		private List<UMAJiggleBoneListing> removalRegister = new List<UMAJiggleBoneListing>();
		private UMAData uMAData;
		private DynamicCharacterAvatar avatar;
	
		public void Awake()
		{
			avatar = gameObject.GetComponentInChildren<DynamicCharacterAvatar>();
			avatar.CharacterBegun.AddListener(CleanBones);
		}

		protected void OnDisable()
		{
			avatar.CharacterBegun.RemoveListener(CleanBones);
		}
	
		public void CleanBones(UMAData umaData)
		{
			uMAData = gameObject.GetComponentInChildren<UMAData>();
			List<UMAJiggleBoneListing> listingsToDelete = new List<UMAJiggleBoneListing>();
			KillBones = new List<string>();
			AllExceptions = new List<Transform>();
			foreach(UMAJiggleBoneListing listing in removalRegister)
			{
				string WardrobeItemName = avatar.GetWardrobeItemName(listing.carrierSlot);
				// lets see if the wardrobe item in the listing is no longer in the slot.
				// if so, add the bones for the listing to the "kill" list.
				// and remove this listing from the RemovalRegister (it's already processed and removed from the character)
				if (WardrobeItemName != listing.recipe)
				{
					// make sure no *other* listing is using this bone. 
					KillBones.Add(listing.boneName);
					listingsToDelete.Add(listing);
					AllExceptions.AddRange(listing.exceptions);					
				}
			}

			foreach(UMAJiggleBoneListing listing in listingsToDelete)
			{
				removalRegister.Remove(listing);
			}

			// Now that we've got the list of bones to delete, and we've removed them from
			// the removal register, let's make sure something else isn't using them.
			foreach(UMAJiggleBoneListing listing in removalRegister)
            {
				// remove it if it exists.
				KillBones.Remove(listing.boneName);
            }
			listingsToDelete.Clear();
			
			ProcessBones(gameObject.transform, AllExceptions);
		}
	
		private void ProcessBones(Transform transform, List<Transform> Exceptions)
		{
			foreach(Transform t in transform)
			{
				if (Exceptions.Contains(t))
                {
					continue;
                }
				if (KillBones.Contains(t.gameObject.name))
				{
					RecursivelyRemoveChildBones(t,Exceptions);
					GameObject.DestroyImmediate(t.gameObject);
				}
				else
				{
					ProcessBones(t,Exceptions);
				}
			}
		}
	
		private void RecursivelyRemoveChildBones(Transform transform, List<Transform> Exceptions)
		{
			uMAData.skeleton.RemoveBone(UMAUtils.StringToHash(transform.name));
			foreach(Transform t in transform)
			{
				if (Exceptions.Contains(t))
                {
                    continue;
                }

                RecursivelyRemoveChildBones(t,Exceptions);
			}
		}
		
		public void RegisterJiggleBone(UMAJiggleBoneListing boneListing)
		{
			foreach(UMAJiggleBoneListing listing in removalRegister)
            {
				if (listing.recipe == boneListing.recipe)
                {
					// Don't continue to add the same recipe.
					return;
                }
            }
			removalRegister.Add(boneListing);
		}
	}
	
	public class UMAJiggleBoneListing
	{
		public String boneName;
		public String carrierSlot;
		public String recipe;
		public List<Transform> exceptions;
	}
}