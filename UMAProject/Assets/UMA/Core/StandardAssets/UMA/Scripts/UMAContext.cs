using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// Gloal container for various UMA objects in the scene.
    /// </summary>
    public class UMAContext : UMAContextBase
	{
		/// <summary>
		/// The race library.
		/// </summary>
		public RaceLibraryBase raceLibrary;
		/// <summary>
		/// The slot library.
		/// </summary>
		public SlotLibraryBase slotLibrary;
		/// <summary>
		/// The overlay library.
		/// </summary>
		public OverlayLibraryBase overlayLibrary;

		public UMA.CharacterSystem.DynamicCharacterSystem dynamicCharacterSystem;

#pragma warning disable 618
		public override void Start()
		{
			if (!slotLibrary)
			{
				slotLibrary = GameObject.Find("SlotLibrary").GetComponent<SlotLibraryBase>();
			}
			if (!raceLibrary)
			{
				raceLibrary = GameObject.Find("RaceLibrary").GetComponent<RaceLibraryBase>();
			}
			if (!overlayLibrary)
			{
				overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent<OverlayLibraryBase>();
			}
			// Note: Removed null check so that this is always assigned if you have a UMAContextBase in your scene
			// This will avoid those occasions where someone drops in a bogus context in a test scene, and then 
			// later loads a valid scene (and everything breaks)
			Instance = this;
		}

		/// <summary>
		/// Validates the library contents.
		/// </summary>
		public override void ValidateDictionaries()
		{
			slotLibrary.ValidateDictionary();
			raceLibrary.ValidateDictionary();
			overlayLibrary.ValidateDictionary();
			if (dynamicCharacterSystem != null)
			{
				dynamicCharacterSystem.Refresh(false);
				dynamicCharacterSystem.RefreshRaceKeys();
			}
		}

		/// <summary>
		/// Gets a race by name, if it has been added to the library
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceData HasRace(string name)
		{
			return raceLibrary.HasRace(name);
		}
		/// <summary>
		/// Gets a race by name hash, if it has been added to the library.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceData HasRace(int nameHash)
		{
			return raceLibrary.HasRace(nameHash);
		}

		public override void EnsureRaceKey(string name)
		{
			if (dynamicCharacterSystem != null)
			{
				dynamicCharacterSystem.EnsureRaceKey(name);
			}
		}

		/// <summary>
		/// Gets a race by name, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceData GetRace(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Getting Race: " + name);
#endif
			return raceLibrary.GetRace(name);
		}
		/// <summary>
		/// Gets a race by name hash, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceData GetRace(int nameHash)
		{
			return raceLibrary.GetRace(nameHash);
		}

		public override RaceData GetRaceWithUpdate(int nameHash, bool allowUpdate)
		{
			return raceLibrary.GetRace(nameHash);
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public override RaceData[] GetAllRaces()
		{
			return raceLibrary.GetAllRaces();
		}

		public override RaceData[] GetAllRacesBase()
		{
			return raceLibrary.GetAllRaces();
		}


		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public override void AddRace(RaceData race)
		{
			raceLibrary.AddRace(race);
			raceLibrary.UpdateDictionary();
			if (dynamicCharacterSystem != null)
			{
				dynamicCharacterSystem.RefreshRaceKeys();
			}
		}

		/// <summary>
		/// Instantiate a slot by name.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		public override SlotData InstantiateSlot(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating slot: " + name);
#endif
			return slotLibrary.InstantiateSlot(name);
		}

		/// <summary>
		/// Instantiate a slot by name hash.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override SlotData InstantiateSlot(int nameHash)
		{
			return slotLibrary.InstantiateSlot(nameHash);
		}

		/// <summary>
		/// Instantiate a slot by name, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		/// <param name="overlayList">Overlay list.</param>
		public override SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating slot: " + name);
#endif
			return slotLibrary.InstantiateSlot(name, overlayList);
		}
		/// <summary>
		/// Instantiate a slot by name hash, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="overlayList">Overlay list.</param>
		public override SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
		{
			return slotLibrary.InstantiateSlot(nameHash, overlayList);
		}

		/// <summary>
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasSlot(string name)
		{
			if (slotLibrary.HasSlot(name))
            {
                return true;
            }
            else
			{
				if (UMAAssetIndexer.Instance.GetAssetItem<SlotDataAsset>(name) != null)
                {
                    return true;
                }
            }

			return false;
		}
		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasSlot(int nameHash)
		{
			if (slotLibrary.HasSlot(nameHash))
            {
                return true;
            }
            else
			{
				if (UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(nameHash) != null)
                {
                    return true;
                }
            }

			return false;
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddSlotAsset(SlotDataAsset slot)
		{
			slotLibrary.AddSlotAsset(slot);
		}

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasOverlay(string name)
		{
			return overlayLibrary.HasOverlay(name);
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasOverlay(int nameHash)
		{ 
			return overlayLibrary.HasOverlay(nameHash);
		}

		/// <summary>
		/// Instantiate an overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public override OverlayData InstantiateOverlay(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating Overlay: " + name);
#endif
			return overlayLibrary.InstantiateOverlay(name);
		}
		/// <summary>
		/// Instantiate an overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override OverlayData InstantiateOverlay(int nameHash)
		{
			return overlayLibrary.InstantiateOverlay(nameHash);
		}

		/// <summary>
		/// Instantiate a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Color.</param>
		public override OverlayData InstantiateOverlay(string name, Color color)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating Overlay: " + name);
#endif
			return overlayLibrary.InstantiateOverlay(name, color);
		}
		/// <summary>
		/// Instantiate a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Color.</param>
		public override OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			return overlayLibrary.InstantiateOverlay(nameHash, color);
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public override void AddOverlayAsset(OverlayDataAsset overlay)
		{
			overlayLibrary.AddOverlayAsset(overlay);
		}

		// Get all DNA
		public override List<DynamicUMADnaAsset> GetAllDNA()
		{
			return UMAAssetIndexer.Instance.GetAllAssets<DynamicUMADnaAsset>();
		}

		// Get a DNA Asset By Name
		public override DynamicUMADnaAsset GetDNA(string Name)
		{
			return UMAAssetIndexer.Instance.GetAsset<DynamicUMADnaAsset>(Name);
		}

		public override RuntimeAnimatorController GetAnimatorController(string Name)
		{
			return UMAAssetIndexer.Instance.GetAsset<RuntimeAnimatorController>(Name);
		}

		public override List<RuntimeAnimatorController> GetAllAnimatorControllers()
		{
			return UMAAssetIndexer.Instance.GetAllAssets<RuntimeAnimatorController>();
		}

		public override void AddRecipe(UMATextRecipe recipe)
		{
			dynamicCharacterSystem.AddRecipe(recipe);
		}

		public override UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
		{
			return dynamicCharacterSystem.GetRecipe(filename, dynamicallyAdd);
		}

		public override UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd)
		{
			return GetRecipe(filename, dynamicallyAdd);
		}

		public override string GetCharacterRecipe(string filename)
		{
			if (dynamicCharacterSystem.CharacterRecipes.ContainsKey(filename))
            {
                return dynamicCharacterSystem.CharacterRecipes[filename];
            }

            return "";
		}

		public override List<string> GetRecipeFiles()
		{
			List<string> keys = new List<string>();
			keys.AddRange(dynamicCharacterSystem.CharacterRecipes.Keys);
			return keys;
		}

		public override bool HasRecipe(string Name)
		{
			if (dynamicCharacterSystem == null)
            {
                return false;
            }

            return dynamicCharacterSystem.RecipeIndex.ContainsKey(Name);
		}

		/// <summary>
		/// This checks through everything, not just the currently loaded index.
		/// </summary>
		/// <param name="recipeName"></param>
		/// <returns></returns>
		public override bool CheckRecipeAvailability(string recipeName)
		{
			return dynamicCharacterSystem.CheckRecipeAvailability(recipeName);
		}

		public override List<string> GetRecipeNamesForRaceSlot(string race, string slot)
		{
			return dynamicCharacterSystem.GetRecipeNamesForRaceSlot(race, slot);
		}

		public override List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot)
		{
			return dynamicCharacterSystem.GetRecipesForRaceSlot(race, slot);
		}

		public override Dictionary<string, List<UMATextRecipe>> GetRecipes(string raceName)
		{
			return dynamicCharacterSystem.Recipes[raceName];
		}
	}
}
