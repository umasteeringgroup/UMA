using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;

namespace UMA
{
	/// <summary>
	/// Gloal container for various UMA objects in the scene.
	/// </summary>
	public class UMAGlobalContext : UMAContextBase
	{
#pragma warning disable 618
		public override void Start()
		{
			Instance = this;
		}

		/// <summary>
		/// Validates the library contents.
		/// </summary>
		public override void ValidateDictionaries()
		{
			UMAAssetIndexer.Instance.RebuildIndex();
#if UNITY_EDITOR
			UMAAssetIndexer.Instance.ForceSave();
#endif
		}

		/// <summary>
		/// Gets a race by name, if it has been added to the library
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceData HasRace(string name)
		{
			return UMAAssetIndexer.Instance.GetAsset<RaceData>(name);
		}
		/// <summary>
		/// Gets a race by name hash, if it has been added to the library.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceData HasRace(int nameHash)
		{
			throw new System.NotImplementedException();
		} 

		public override void EnsureRaceKey(string name)
		{
			//if (dynamicCharacterSystem != null)
			//{
			//	dynamicCharacterSystem.EnsureRaceKey(name);
			//}
		}

		/// <summary>
		/// Gets a race by name, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceData GetRace(string name)
		{
			return UMAAssetIndexer.Instance.GetAsset<RaceData>(name);
		}
		/// <summary>
		/// Gets a race by name hash, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceData GetRace(int nameHash)
		{
			throw new System.NotImplementedException("GetRace(int nameHash)");
		}

		public override RaceData GetRaceWithUpdate(int nameHash, bool allowUpdate)
		{
			throw new System.NotImplementedException("UMAGlobalContext.GetRaceWithUpdate");
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public override RaceData[] GetAllRaces()
		{
			return UMAAssetIndexer.Instance.GetAllAssets<RaceData>().ToArray();
		}

		public override RaceData[] GetAllRacesBase()
		{
			return GetAllRaces();
		}


		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public override void AddRace(RaceData race)
		{
			AssetItem ai = new AssetItem(typeof(RaceData), race);
			UMAAssetIndexer.Instance.AddAsset(typeof(RaceData), race.raceName,"", race);
			//if (dynamicCharacterSystem != null)
			//{
			//	dynamicCharacterSystem.RefreshRaceKeys();
			//}
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
			SlotDataAsset source = UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(name);
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
		public override SlotData InstantiateSlot(int nameHash)
		{
			SlotDataAsset source = UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(nameHash);
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
		public override SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
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
		public override SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
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
		public override bool HasSlot(string name)
		{
			return UMAAssetIndexer.Instance.HasAsset<SlotDataAsset>(name);
		}

		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasSlot(int nameHash)
		{
			return UMAAssetIndexer.Instance.HasAsset<SlotDataAsset>(nameHash);
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddSlotAsset(SlotDataAsset slot)
		{
			UMAAssetIndexer.Instance.AddAsset(typeof(SlotDataAsset), slot.slotName, "", slot);	
		}

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasOverlay(string name)
		{
			return UMAAssetIndexer.Instance.HasAsset<OverlayDataAsset>(name);
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasOverlay(int nameHash)
		{
			return UMAAssetIndexer.Instance.HasAsset<OverlayDataAsset>(nameHash);
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
			OverlayDataAsset source = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(name);
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
		public override OverlayData InstantiateOverlay(int nameHash)
		{
			OverlayDataAsset source = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(nameHash);
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
		public override OverlayData InstantiateOverlay(string name, Color color)
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
		public override OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			OverlayData res = InstantiateOverlay(nameHash);
			res.colorData.color = color;
			return res;
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public override void AddOverlayAsset(OverlayDataAsset overlay)
		{
			UMAAssetIndexer.Instance.AddAsset(typeof(OverlayDataAsset), overlay.overlayName, "", overlay);
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
			UMAAssetIndexer.Instance.AddAsset(recipe.GetType(), recipe.name, "", recipe);
		}

		public override UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
		{
			if (UMAAssetIndexer.Instance.HasAsset<UMAWardrobeRecipe>(filename))
			{
				return UMAAssetIndexer.Instance.GetAsset<UMAWardrobeRecipe>(filename);
			}
            if (UMAAssetIndexer.Instance.HasAsset<UMATextRecipe>(filename))
            {
                return UMAAssetIndexer.Instance.GetAsset<UMATextRecipe>(filename);
            }
            if (UMAAssetIndexer.Instance.HasAsset<UMAWardrobeCollection>(filename))
            {
                return UMAAssetIndexer.Instance.GetAsset<UMAWardrobeCollection>(filename);
            }
			return null;
		}

		public override UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd)
		{
			return GetRecipe(filename, dynamicallyAdd);
		}

		public override string GetCharacterRecipe(string filename)
		{
		//	if (dynamicCharacterSystem.CharacterRecipes.ContainsKey(filename))
		//		return dynamicCharacterSystem.CharacterRecipes[filename];
			return "";
		}

		public override List<string> GetRecipeFiles()
		{
			List<string> keys = new List<string>();
			// keys.AddRange(dynamicCharacterSystem.CharacterRecipes.Keys);
			return keys;
		}

		public override bool HasRecipe(string Name)
		{
			bool found = UMAAssetIndexer.Instance.HasAsset<UMAWardrobeRecipe>(Name);
			if (!found)
            {
                found = UMAAssetIndexer.Instance.HasAsset<UMAWardrobeCollection>(Name);
            }

            return found;
		}

		/// <summary>
		/// This checks through everything, not just the currently loaded index.
		/// </summary>
		/// <param name="recipeName"></param>
		/// <returns></returns>
		public override bool CheckRecipeAvailability(string recipeName)
		{
			return UMAAssetIndexer.Instance.HasAsset<UMAWardrobeRecipe>(recipeName);
		}

		public override List<string> GetRecipeNamesForRaceSlot(string race, string slot)
		{
			return UMAAssetIndexer.Instance.GetRecipeNamesForRaceSlot(race, slot);
		}

		public override List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot)
		{
			return UMAAssetIndexer.Instance.GetRecipesForRaceSlot(race, slot);
		}

		public override Dictionary<string, List<UMATextRecipe>> GetRecipes(string raceName)
		{
			return UMAAssetIndexer.Instance.GetRecipes(raceName);
		}
	}
}
