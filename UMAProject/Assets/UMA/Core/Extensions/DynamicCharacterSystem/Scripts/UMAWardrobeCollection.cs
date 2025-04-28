using UnityEngine;
#if UNITY_EDITOR
#endif
using System.Collections.Generic;


namespace UMA.CharacterSystem
{
    //Because this is a class for user generated content it is marked as partial so it can be extended without modifying the underlying code
    public partial class UMAWardrobeCollection : UMATextRecipe
	{
		[Tooltip("Cover images for the collection as a whole. Use these for a promotional images for this collection, presenting the goodies inside.")]
		public List<Sprite> coverImages = new List<Sprite>();
		public WardrobeCollectionList wardrobeCollection = new WardrobeCollectionList();
		[Tooltip("WardrobeCollections can also contain an arbitrary list of wardrobeRecipes, not associated with any particular race.You can use this to make a 'hairStyles' pack or a 'tattoos' pack for example")]
		public List<string> arbitraryRecipes = new List<string>();

		#region CONSTRUCTOR

		public UMAWardrobeCollection()
		{
			recipeType = "WardrobeCollection";
			wardrobeSlot = "FullOutfit";
		}

		#endregion

		#region PUBLIC METHODS
		/// <summary>
		/// Gets the CoverImage for the collection at the desired index (if set) if none is set falls back to the first wardrobeThumb (if set)
		/// </summary>
		public Sprite GetCoverImage(int desiredIndex = 0)
		{
			if (coverImages.Count > desiredIndex)
			{
				return coverImages[desiredIndex];
			}
			else if (coverImages.Count > 0)
			{
				return coverImages[0];
			}
			else if (wardrobeRecipeThumbs.Count > 0)
			{
				return wardrobeRecipeThumbs[0].thumb;
			}
			else
			{
				return null;
			}
		}

		public List<WardrobeSettings> GetRacesWardrobeSet(string race)
		{
			var thisContext = UMAAssetIndexer.Instance;
			var thisRace = thisContext.GetRace(race);
			return GetRacesWardrobeSet(thisRace);
		}
		/// <summary>
		/// Gets the wardrobeSet set in this collection for the given race
		/// Or wardrobeSet for first matched cross compatible race the given race has
		/// </summary>
		public List<WardrobeSettings> GetRacesWardrobeSet(RaceData race)
		{
			var setToUse = wardrobeCollection[race.raceName];
			//if no set was directly compatible with the active race, check if it has sets for any cross compatible races that race may have
			if (setToUse.Count == 0)
			{
				var thisDCACCRaces = race.GetCrossCompatibleRaces();
				for (int i = 0; i < thisDCACCRaces.Count; i++)
				{
					if (wardrobeCollection[thisDCACCRaces[i]].Count > 0)
					{
						setToUse = wardrobeCollection[thisDCACCRaces[i]];
						break;
					}
				}
			}
			return setToUse;
		}

		/// <summary>
		/// Gets the recipe names for the given race from the WardrobeCollection
		/// </summary>
		public List<string> GetRacesRecipeNames(string race)
		{
			var recipesToGet = GetRacesWardrobeSet(race);
			List<string> recipesWeGot = new List<string>();
			for (int i = 0; i < recipesToGet.Count; i++)
			{
				recipesWeGot.Add(recipesToGet[i].recipe);
			}
			return recipesWeGot;
		}
		/// <summary>
		/// Gets the wardrobeRecipes for the given race from the WardrobeCollection
		/// </summary>
		public List<UMATextRecipe> GetRacesRecipes(string race)
		{
			var recipesToGet = GetRacesWardrobeSet(race);
			List<UMATextRecipe> recipesWeGot = new List<UMATextRecipe>();
			for (int i = 0; i < recipesToGet.Count; i++)
			{
				var recipe = UMAAssetIndexer.Instance.GetRecipeWardrobeTextCollection(recipesToGet[i].recipe);
                recipesWeGot.Add(recipe);
			}
			return recipesWeGot;
		}

		/// <summary>
		/// Gets a DCSUnversalPackRecipeModel that has the wardrobeSet set to be the set in this collection for the given race of the sent avatar
		/// Or if this recipe is cross compatible returns the wardrobe set for the first matched cross compatible race
		/// </summary>
		public DCSUniversalPackRecipe GetUniversalPackRecipe(DynamicCharacterAvatar dca)
		{
			var thisPackRecipe = PackedLoadDCSInternal();
			RaceData race = dca.activeRace.racedata;
			if (dca.activeRace.racedata == null)
			{
				race = dca.activeRace.data;
			}
			var setToUse = GetRacesWardrobeSet(race);
            thisPackRecipe.wardrobeSet = setToUse;
			thisPackRecipe.race = dca.activeRace.name;
			return thisPackRecipe;
		}

		//Override Load from PackedRecipeBase
		/// <summary>
		/// NOTE: Use GetUniversalPackRecipe to get a recipe that includes a wardrobeSet. Load this Recipe's recipeString into the specified UMAData.UMARecipe.
		/// </summary>
		public override void Load(UMA.UMAData.UMARecipe umaRecipe, bool loadSlots = true)
		{
			if ((recipeString != null) && (recipeString.Length > 0))
			{
				var packedRecipe = PackedLoadDCSInternal();
				if(packedRecipe != null)
                {
                    UnpackRecipe(umaRecipe, packedRecipe, loadSlots);
                }
            }
		}
		#endregion

#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/DCS/Wardrobe Collection")]
		public static void CreateWardrobeCollectionAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAWardrobeCollection>();
		}
#endif
	}
}
