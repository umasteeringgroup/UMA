using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;
using System;

namespace UMA
{
    /// <summary>
    /// Functions to emulate the old UMAContext, which is removed in the UMA 3.0 release.
    /// </summary>
    public partial class UMAAssetIndexer 
	{
        [NonSerialized]
        public List<UMAData> dirtyList = new List<UMAData>();

        /// <summary>
        /// Gets a race by name, if it has been added to the library
        /// </summary>
        /// <returns>The race.</returns>
        /// <param name="name">Name.</param>
        public RaceData HasRace(string name)
		{
			return GetAsset<RaceData>(name);
		}


		/// <summary>
		/// Gets a race by name, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public RaceData GetRace(string name)
		{
			return GetAsset<RaceData>(name);
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public RaceData[] GetAllRaces()
		{
			return GetAllAssets<RaceData>().ToArray();
		}

		public RaceData[] GetAllRacesBase()
		{
			return GetAllRaces();
		}


		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public void AddRace(RaceData race)
		{
			AssetItem ai = new AssetItem(typeof(RaceData), race);
			AddAsset(typeof(RaceData), race.raceName,"", race);
		}

		/// <summary>
		/// Instantiate a slot by name.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		public  SlotData InstantiateSlot(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating slot: " + name);
#endif
			SlotDataAsset source = GetAsset<SlotDataAsset>(name);
			if (source == null)
			{
				if (Debug.isDebugBuild)
				{
					Debug.LogError("UMAGlobalContext: Unable to find SlotDataAsset: " + name);
				}
				return null;
			}
			return new SlotData(source);
		}

		/// <summary>
		/// Instantiate a slot by name hash.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		public SlotData InstantiateSlot(int nameHash)
		{
			SlotDataAsset source = GetAsset<SlotDataAsset>(nameHash);
			if (source == null)
			{
				throw new UMAResourceNotFoundException("UMAGlobalContext: Unable to find SlotDataAsset: " + name);
			}
			return new SlotData(source);
		}

		/// <summary>
		/// Instantiate a slot by name, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		/// <param name="overlayList">Overlay list.</param>
		public SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating slot: " + name);
#endif
			SlotData res = InstantiateSlot(name);
			res.SetOverlayList(overlayList);
			return res;
		}
		/// <summary>
		/// Instantiate a slot by name hash, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="overlayList">Overlay list.</param>
		public SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
		{
			SlotData res = InstantiateSlot(nameHash);
			res.SetOverlayList(overlayList);
			return res;
		}

		/// <summary>
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasSlot(string name)
		{
			return HasAsset<SlotDataAsset>(name);
		}

		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasSlot(int nameHash)
		{
			return HasAsset<SlotDataAsset>(nameHash);
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public void AddSlotAsset(SlotDataAsset slot)
		{
			AddAsset(typeof(SlotDataAsset), slot.slotName, "", slot);	
		}

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasOverlay(string name)
		{
			return HasAsset<OverlayDataAsset>(name);
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasOverlay(int nameHash)
		{
			return HasAsset<OverlayDataAsset>(nameHash);
		}

		/// <summary>
		/// Instantiate an overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public OverlayData InstantiateOverlay(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating Overlay: " + name);
#endif
			OverlayDataAsset source = GetAsset<OverlayDataAsset>(name);
			if (source == null)
			{
				throw new UMAResourceNotFoundException("UMAGlobalContext: Unable to find OverlayDataAsset: " + name);
			}
			return new OverlayData(source);
		}

		/// <summary>
		/// Instantiate an overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public OverlayData InstantiateOverlay(int nameHash)
		{
			OverlayDataAsset source = GetAsset<OverlayDataAsset>(nameHash);
			if (source == null)
			{
				throw new UMAResourceNotFoundException("UMAGlobalContext: Unable to find OverlayDataAsset: " + nameHash);
			}
			return new OverlayData(source);
		}

		/// <summary>
		/// Instantiate a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Color.</param>
		public OverlayData InstantiateOverlay(string name, Color color)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating Overlay: " + name);
#endif
			OverlayData res = InstantiateOverlay(name);
			res.colorData.color = color;
			return res;
		}
		/// <summary>
		/// Instantiate a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Color.</param>
		public OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			OverlayData res = InstantiateOverlay(nameHash);
			res.colorData.color = color;
			return res;
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public void AddOverlayAsset(OverlayDataAsset overlay)
		{
			AddAsset(typeof(OverlayDataAsset), overlay.overlayName, "", overlay);
		}

		// Get all DNA
		public List<DynamicUMADnaAsset> GetAllDNA()
		{
			return GetAllAssets<DynamicUMADnaAsset>();
		}

		// Get a DNA Asset By Name
		public DynamicUMADnaAsset GetDNA(string Name)
		{
			return GetAsset<DynamicUMADnaAsset>(Name);
		}

		public RuntimeAnimatorController GetAnimatorController(string Name)
		{
			return GetAsset<RuntimeAnimatorController>(Name);
		}

		public List<RuntimeAnimatorController> GetAllAnimatorControllers()
		{
			return GetAllAssets<RuntimeAnimatorController>();
		}

		public void AddRecipe(UMATextRecipe recipe)
		{
			AddAsset(recipe.GetType(), recipe.name, "", recipe);
		}

		public UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
		{
			if (HasAsset<UMAWardrobeRecipe>(filename))
			{
				return GetAsset<UMAWardrobeRecipe>(filename);
			}
            if (HasAsset<UMATextRecipe>(filename))
            {
                return GetAsset<UMATextRecipe>(filename);
            }
            if (HasAsset<UMAWardrobeCollection>(filename))
            {
                return GetAsset<UMAWardrobeCollection>(filename);
            }
			return null;
		}

		public UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd)
		{
			return GetRecipe(filename, dynamicallyAdd);
		}

		public string GetCharacterRecipe(string filename)
		{
		//	if (dynamicCharacterSystem.CharacterRecipes.ContainsKey(filename))
		//		return dynamicCharacterSystem.CharacterRecipes[filename];
			return "";
		}

		public List<string> GetRecipeFiles()
		{
			List<string> keys = new List<string>();
			// keys.AddRange(dynamicCharacterSystem.CharacterRecipes.Keys);
			return keys;
		}

		public bool HasRecipe(string Name)
		{
			bool found = HasAsset<UMAWardrobeRecipe>(Name);
			if (!found)
            {
                found = HasAsset<UMAWardrobeCollection>(Name);
            }

            return found;
		}

		/// <summary>
		/// This checks through everything, not just the currently loaded index.
		/// </summary>
		/// <param name="recipeName"></param>
		/// <returns></returns>
		public bool CheckRecipeAvailability(string recipeName)
		{
			return HasAsset<UMAWardrobeRecipe>(recipeName);
		}
	}
}
