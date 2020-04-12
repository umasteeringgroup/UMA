using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA.CharacterSystem
{
	public class DynamicCharacterSystem : DynamicCharacterSystemBase
	{
		public Dictionary<string, UMATextRecipe> RecipeIndex = new Dictionary<string, UMATextRecipe>();
		public Dictionary<string, Dictionary<string, List<UMATextRecipe>>> Recipes = new Dictionary<string, Dictionary<string, List<UMATextRecipe>>>();
		public Dictionary<string, string> CharacterRecipes = new Dictionary<string, string>();

		public bool initializeOnAwake = true;

		[HideInInspector]
		[System.NonSerialized]
		public bool initialized = false;
		private bool isInitializing = false;

		public bool dynamicallyAddFromResources;
		[Tooltip("Limit the Global Library search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesCharactersFolder = "";
		[Tooltip("Limit the Global Library search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesRecipesFolder = "";
		public bool dynamicallyAddFromAssetBundles;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForCharactersToSearch;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForRecipesToSearch;
		[Tooltip("If true will automatically scan and add all UMATextRecipes from any downloaded bundles.")]
		public bool addAllRecipesFromDownloadedBundles = true;
		[HideInInspector]
		public UMAContextBase context;
		//This is a ditionary of asset bundles that were loaded into the library. This can be queried to store a list of active assetBundles that might be useful to preload etc
		public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
		[System.NonSerialized]
		[HideInInspector]
		public bool downloadAssetsEnabled = true;

		public override void Awake()
		{
			if (initializeOnAwake)
			{
				if (!initialized)
				{
					Init();
				}
			}
		}

		public override void Start()
		{
			if (!initialized)
			{
				Init();
			}

		}

		public override void Init()
		{
			if (initialized || isInitializing)
			{
				return;
			}
			if (context == null)
			{
				context = UMAContextBase.Instance;
			}
			isInitializing = true;

			Recipes.Clear();
			var possibleRaces = context.GetAllRaces();
			for (int i = 0; i < possibleRaces.Length; i++)
			{
				//we need to check that this is not null- the user may not have downloaded it yet
				if (possibleRaces[i] == null)
					continue;
				if (possibleRaces[i].raceName == "RaceDataPlaceholder")
					continue;
				if (Recipes.ContainsKey(possibleRaces[i].raceName))
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("Warning: multiple races found for key:" + possibleRaces[i].raceName);
				}
				else
				{
					Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
				}
			}

			GatherCharacterRecipes();
			GatherRecipeFiles();
			initialized = true;
			isInitializing = false;
		}
		/// <summary>
		/// Ensures DCS has a race key for the given race in its dictionaries. Use when you want to add recipes to DCS before the actual racedata has been downloaded
		/// </summary>
		public void EnsureRaceKey(string race)
		{
			if (!Recipes.ContainsKey(race))
			{
				Recipes.Add(race, new Dictionary<string, List<UMATextRecipe>>());
			}
		}
		/// <summary>
		/// Refreshes DCS Dictionaries based on the current races in the RaceLibrary. Ensures any newly added races get backwards compatible recipes assigned to them
		/// </summary>
		public void RefreshRaceKeys()
		{
			if (!initialized)
			{
				Init();
				return;
			}
			if (addAllRecipesFromDownloadedBundles)
			{
				Refresh(false, "");
				return;
			}
			var possibleRaces = context.GetAllRacesBase();
			for (int i = 0; i < possibleRaces.Length; i++)
			{
				if (!Recipes.ContainsKey(possibleRaces[i].raceName) && possibleRaces[i].raceName != "RaceDataPlaceholder")
				{
					Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
				}
				//then make sure any currently added recipes are also assigned to this race if they are compatible
				foreach (string race in Recipes.Keys)
				{
					if (race != possibleRaces[i].raceName)
					{
						foreach (KeyValuePair<string, List<UMATextRecipe>> kp in Recipes[race])
						{
							AddRecipes(kp.Value.ToArray());
						}
					}
				}
			}
		}
		/// <summary>
		/// Forces DCS to update its recipes to include all recipes that are in Resources or in downloaded assetBundles (optionally filtering by assetBundle name)
		/// </summary>
		/// <param name="forceUpdateRaceLibrary">If true will force RaceLibrary to add any races that were in any downloaded assetBundles and then call this Refresh again.</param>
		/// <param name="bundleToGather">Limit the recipes found tto a defined asset bundle</param>
		public override void Refresh(bool forceUpdateRaceLibrary = true, string bundleToGather = "")
		{
			if (!initialized)
			{
				Init();
				return;
			}
			RaceData[] possibleRaces = new RaceData[0];
			if (forceUpdateRaceLibrary)
			{
				possibleRaces = context.GetAllRaces();//if any new races are added by this then RaceLibrary will Re-Trigger this if there was anything new so dont do anything else
			}
			else
			{
				possibleRaces = context.GetAllRacesBase();
				for (int i = 0; i < possibleRaces.Length; i++)
				{
					//we need to check that this is not null- the user may not have downloaded it yet
					if (possibleRaces[i] != null)
					{
						if (!Recipes.ContainsKey(possibleRaces[i].raceName) && possibleRaces[i].raceName != "RaceDataPlaceholder")
                        {
							Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
						}
					}
				}
				GatherCharacterRecipes("", bundleToGather);
				GatherRecipeFiles("", bundleToGather);
			}
		}

		private void GatherCharacterRecipes(string filename = "", string bundleToGather = "")
		{
			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForCharactersToSearch;
			//DCS may do this before DAL has downloaded the AssetBundleIndex so in that case we want to turn 'downloadAssetsEnabled' off 
			if (DynamicAssetLoader.Instance != null)
			{
				bool downloadAssetsEnabledNow = DynamicAssetLoader.Instance.isInitialized ? downloadAssetsEnabled : false;
				DynamicAssetLoader.Instance.AddAssets<TextAsset>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, downloadAssetsEnabledNow, assetBundleToGather, resourcesCharactersFolder, null, filename, AddCharacterRecipes);
			}
		}

		private void AddCharacterRecipes(TextAsset[] characterRecipes)
		{
			foreach (TextAsset characterRecipe in characterRecipes)
			{
				if (!CharacterRecipes.ContainsKey(characterRecipe.name))
					CharacterRecipes.Add(characterRecipe.name, characterRecipe.text);
				else
					CharacterRecipes[characterRecipe.name] = characterRecipe.text;
			}
			//This doesn't actually seem to do anything apart from slow things down- maybe we can hook into the UMAGarbage collection rate and do this at the same time? Or just after?
			//StartCoroutine(CleanFilesFromResourcesAndBundles());
		}

		private void GatherRecipeFiles(string filename = "", string bundleToGather = "")
		{
			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForRecipesToSearch;
			//DCS may do this before DAL has downloaded the AssetBundleIndex so in that case we want to turn 'downloadAssetsEnabled' off 
			if (DynamicAssetLoader.Instance != null)
			{
				bool downloadAssetsEnabledNow = DynamicAssetLoader.Instance.isInitialized ? downloadAssetsEnabled : false;
				//if we are only adding stuff from a downloaded assetbundle, dont search resources
				bool dynamicallyAddFromResourcesNow = bundleToGather == "" ? dynamicallyAddFromResources : false;
				bool found = false;
				DynamicAssetLoader.Instance.debugOnFail = false;

				found = DynamicAssetLoader.Instance.AddAssets<UMAWardrobeRecipe>(ref assetBundlesUsedDict, dynamicallyAddFromResourcesNow, dynamicallyAddFromAssetBundles, downloadAssetsEnabledNow, assetBundleToGather, resourcesRecipesFolder, null, filename, AddRecipesFromAB);
				if ((!found && filename != "") || (filename == "" && (Application.isPlaying == false || addAllRecipesFromDownloadedBundles || bundleToGather == "")))//The WardrobeSetMasterEditor asks DCS to get all collections, but normally collections are only requested by name
					found = DynamicAssetLoader.Instance.AddAssets<UMAWardrobeCollection>(ref assetBundlesUsedDict, dynamicallyAddFromResourcesNow, dynamicallyAddFromAssetBundles, downloadAssetsEnabledNow, assetBundleToGather, resourcesRecipesFolder, null, filename, AddRecipesFromAB);
				if (!found && filename != "")
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("[DynamicCharacterSystem] could not find " + filename + " in Resources or any AssetBundles. Do you need to rebuild your UMAResources Index or AssetBundles?");
				}
				DynamicAssetLoader.Instance.debugOnFail = true;

			}
		}


		/*IEnumerator CleanFilesFromResourcesAndBundles()
        {
            yield return null;
            Resources.UnloadUnusedAssets();
            yield break;
        }*/

		public void AddRecipesFromAB(UMATextRecipe[] uparts)
		{
			AddRecipes(uparts, "");
		}

		public void AddRecipe(UMATextRecipe upart)
		{
			if (upart != null)
				AddRecipes(new UMATextRecipe[] { upart });
		}
		//This could be private I think- TODO confirm
		public void AddRecipes(UMATextRecipe[] uparts, string filename = "")
		{
			foreach (UMATextRecipe u in uparts)
			{
				if (filename == "" || (filename != "" && filename.Trim() == u.name))
				{
					var thisWardrobeSlot = u.wardrobeSlot;
					if (u.GetType() == typeof(UMAWardrobeCollection))
					{
						//we have a problem here because when the placeholder asset is returned its wardrobeCollection.sets.Count == 0
						//so we need to do it the other way round i.e. add it when its downloading but if the libraries contain it when its downloaded and the sets count is 0 remove it
						if ((u as UMAWardrobeCollection).wardrobeCollection.sets.Count == 0)
						{
							if (RecipeIndex.ContainsKey(u.name))
							{
								if (Debug.isDebugBuild)
									Debug.LogWarning("DCS removed " + u.name + " from RecipeIndex");
								RecipeIndex.Remove(u.name);
							}
							else if (!DynamicAssetLoader.Instance.downloadingAssetsContains(u.name))
							{
								continue;
							}
						}
						thisWardrobeSlot = "WardrobeCollection";
					}
					//we might be refreshing so check its not already there
					if (!RecipeIndex.ContainsKey(u.name))
						RecipeIndex.Add(u.name, u);
					else
					{
						RecipeIndex[u.name] = u;
					}
					for (int i = 0; i < u.compatibleRaces.Count; i++)
					{
						if (u.GetType() == typeof(UMAWardrobeCollection) && (u as UMAWardrobeCollection).wardrobeCollection.sets.Count > 0)
						{
							//if the collection doesn't have a wardrobeSet for this race continue
							//again when its downloading this data isn't there
							if ((u as UMAWardrobeCollection).wardrobeCollection[u.compatibleRaces[i]].Count == 0)
							{
								if (Recipes.ContainsKey(u.compatibleRaces[i]))
								{
									if (Recipes[u.compatibleRaces[i]].ContainsKey("WardrobeCollection"))
									{
										if (Recipes[u.compatibleRaces[i]]["WardrobeCollection"].Contains(u))
										{
											if (Debug.isDebugBuild)
												Debug.LogWarning("DCS removed " + u.name + " from Recipes");
											Recipes[u.compatibleRaces[i]]["WardrobeCollection"].Remove(u);
											if (RecipeIndex.ContainsKey(u.name))
											{
												if (Debug.isDebugBuild)
													Debug.LogWarning("DCS removed " + u.name + " from RecipeIndex");
												RecipeIndex.Remove(u.name);
											}
											continue;
										}
									}
								}
								if (DynamicAssetLoader.Instance != null)
									if (!DynamicAssetLoader.Instance.downloadingAssetsContains(u.name))
									{
										continue;
									}
							}
						}
						//When recipes that are compatible with multiple races are downloaded we may not have all the races actually downloaded
						//but that should not stop DCS making an index of recipes that are compatible with that race for when it becomes available
						if (!Recipes.ContainsKey(u.compatibleRaces[i]))
						{
							Recipes.Add(u.compatibleRaces[i], new Dictionary<string, List<UMATextRecipe>>());
						}
						if (Recipes.ContainsKey(u.compatibleRaces[i]))
						{
							Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes[u.compatibleRaces[i]];

							if (!RaceRecipes.ContainsKey(thisWardrobeSlot))
							{
								RaceRecipes.Add(thisWardrobeSlot, new List<UMATextRecipe>());
							}
							//we might be refreshing so replace anything that is already there with the downloaded versions- else add
							bool added = false;
							for (int ir = 0; ir < RaceRecipes[thisWardrobeSlot].Count; ir++)
							{
								if (RaceRecipes[thisWardrobeSlot][ir].name == u.name)
								{
									RaceRecipes[thisWardrobeSlot][ir] = u;
									added = true;
								}
							}
							if (!added)
							{
								RaceRecipes[thisWardrobeSlot].Add(u);
							}
						}
						//backwards compatible race slots
						foreach (string racekey in Recipes.Keys)
						{
							//here we also need to check that the race itself has a wardrobe slot that matches the one in the compatible race
							//11012017 Dont trigger backwards compatible races to download
							RaceData raceKeyRace = context.GetRace(racekey);
							if (raceKeyRace == null)
								continue;
							if (raceKeyRace.IsCrossCompatibleWith(u.compatibleRaces[i]) && (raceKeyRace.wardrobeSlots.Contains(thisWardrobeSlot) || thisWardrobeSlot == "WardrobeCollection"))
                            {
								Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes[racekey];
								if (!RaceRecipes.ContainsKey(thisWardrobeSlot))
								{
									RaceRecipes.Add(thisWardrobeSlot, new List<UMATextRecipe>());
								}
								//we might be refreshing so replace anything that is already there with the downloaded versions- else add
								bool added = false;
								for (int ir = 0; ir < RaceRecipes[thisWardrobeSlot].Count; ir++)
								{
									if (RaceRecipes[thisWardrobeSlot][ir].name == u.name)
									{
										RaceRecipes[thisWardrobeSlot][ir] = u;
										added = true;
									}
								}
								if (!added)
								{
									RaceRecipes[thisWardrobeSlot].Add(u);
								}
							}
						}
					}
				}
			}
			//This doesn't actually seem to do anything apart from slow things down
			//StartCoroutine(CleanFilesFromResourcesAndBundles());
		}

		/// <summary>
		/// Get a recipe from the DCS dictionary, optionally 'dynamicallyAdding' it from Resources/AssetBundles if the component is set up to do this.
		/// </summary>
		public UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
		{
			UMATextRecipe foundRecipe = null;
			if (RecipeIndex.ContainsKey(filename))
			{
				foundRecipe = RecipeIndex[filename];
			}
			else
			{
				if (dynamicallyAdd)
				{
					GatherRecipeFiles(filename);
					if (RecipeIndex.ContainsKey(filename))
					{
						foundRecipe = RecipeIndex[filename];
					}
				}
			}
			return foundRecipe;
		}
		/// <summary>
		/// Gets the originating asset bundle for a given recipe.
		/// </summary>
		/// <returns>The originating asset bundle.</returns>
		/// <param name="recipeName">Recipe name.</param>
		public string GetOriginatingAssetBundle(string recipeName)
		{
			string originatingAssetBundle = "";
			if (assetBundlesUsedDict.Count == 0)
				return originatingAssetBundle;
			else
			{
				foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
				{
					if (kp.Value.Contains(recipeName))
					{
						originatingAssetBundle = kp.Key;
						break;
					}
				}
			}
			if (originatingAssetBundle == "")
			{
				if (Debug.isDebugBuild)
					Debug.Log(recipeName + " was not found in any loaded AssetBundle");
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.Log("originatingAssetBundle for " + recipeName + " was " + originatingAssetBundle);
			}
			return originatingAssetBundle;
		}

		#region OVERRIDES FROM BASE - REQUIRED BY RECIPE EDITOR IF IT IS IN STANDARD ASSETS
		/// <summary>
		/// Gets the recipe names in the DynamicCharacterSystem libraries for the given race and slot (used by RecipeEditor because of StandardAssets)
		/// </summary>
		public override List<string> GetRecipeNamesForRaceSlot(string race, string slot)
		{
			Refresh();
			List<string> recipeNamesForRaceSlot = new List<string>();
			if (Recipes.ContainsKey(race))
			{
				if (Recipes[race].ContainsKey(slot))
				{
					foreach (UMATextRecipe utr in Recipes[race][slot])
					{
						recipeNamesForRaceSlot.Add(utr.name);
					}
				}
			}
			return recipeNamesForRaceSlot;
		}
		/// <summary>
		/// Gets the recipes in the DynamicCharacterSystem libraries for the given race and slot (used by RecipeEditor because of StandardAssets)
		/// </summary>
		public override List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot)
		{
			Refresh();
			List<UMARecipeBase> recipesForRaceSlot = new List<UMARecipeBase>();
			if (Recipes.ContainsKey(race))
			{
				if (Recipes[race].ContainsKey(slot))
				{
					foreach (UMATextRecipe utr in Recipes[race][slot])
					{
						recipesForRaceSlot.Add(utr);
					}
				}
			}
			return recipesForRaceSlot;
		}

		/// <summary>
		/// Checks if a given recipe name is available from the dynamic libraries (used by Recipe Editor because of Standard Assets)
		/// </summary>
		/// <param name="recipeName"></param>
		/// <returns></returns>
		public override bool CheckRecipeAvailability(string recipeName)
		{
			if (Application.isPlaying)
				return true;
			bool searchResources = true;
			bool searchAssetBundles = true;
			string resourcesFolderPath = "";
			string assetBundlesToSearch = "";
			bool found = false;
			DynamicAssetLoader.Instance.debugOnFail = false;
			found = DynamicAssetLoader.Instance.AddAssets<UMAWardrobeRecipe>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, recipeName, null);
			if (!found)
				found = DynamicAssetLoader.Instance.AddAssets<UMATextRecipe>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, recipeName, null);
			if (!found)
				found = DynamicAssetLoader.Instance.AddAssets<UMAWardrobeCollection>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, recipeName, null);
			DynamicAssetLoader.Instance.debugOnFail = true;
			return found;
		}
		/// <summary>
		/// Use GetRecipe unless your calling script resides in StandardAssets. Returns the recipe of the given filename from the dictionary as an UMARecipeBase
		/// </summary>
		public override UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd = true)
		{
			return GetRecipe(filename, dynamicallyAdd);
		}
		#endregion
	}
}
