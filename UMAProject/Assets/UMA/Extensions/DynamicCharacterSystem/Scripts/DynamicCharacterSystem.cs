using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using System;
/*using System.IO;*/
using UMA;
using UMAAssetBundleManager;

namespace UMACharacterSystem
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
		[Tooltip("Limit the Resources search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesCharactersFolder = "CharacterRecipes";
		[Tooltip("Limit the Resources search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesRecipesFolder = "Recipes";
		public bool dynamicallyAddFromAssetBundles;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForCharactersToSearch;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForRecipesToSearch;

		[HideInInspector]
		public UMAContext context;
		//This is a ditionary of asset bundles that were loaded into the library. This can be queried to store a list of active assetBundles that might be useful to preload etc
		public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
		[System.NonSerialized]
		[HideInInspector]
		public bool downloadAssetsEnabled = true;

		//bool updatedThisFrame = false;
		//Removed becuase they slow down loadSceneAsync- checks added to Refresh instead
		/*public override void Awake()
        {
            if (initializeOnAwake)
            {
                if (!initialized)
                {
                    Init();
                }
            }
        }*/

		/*public override void OnEnable()
        {
            if (!initialized || refresh)
            {
                if (refresh)
                {
                    Refresh();
                }
                else
                {
                    Init();
                }
            }
        }*/

		public override void Start()
		{
			if (!initialized)
			{
				Init();
			}

		}

		public override void Update()
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
				context = UMAContext.FindInstance();
			}
			isInitializing = true;

			Recipes.Clear();
			var possibleRaces = (context.raceLibrary as DynamicRaceLibrary).GetAllRaces();
			for (int i = 0; i < possibleRaces.Length; i++)
			{
                //we need to check that this is not null- the user may not have downloaded it yet
                if (possibleRaces[i] == null)
                    continue;
                if (DynamicAssetLoader.Instance != null && possibleRaces[i].raceName == DynamicAssetLoader.Instance.placeholderRace.raceName)
                    continue;
                Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
            }

            GatherCharacterRecipes();
			GatherRecipeFiles();
			initialized = true;
			isInitializing = false;
		}

		//Refresh just adds to what is there rather than clearing it all
		//used after asset bundles have been loaded to add any new recipes to the dictionaries
		//This is slow and should only be called when you know something has been added
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
				possibleRaces = context.raceLibrary.GetAllRaces();//if any new races are added by this then RaceLibrary will Re-Trigger this if there was anything new so dont do anything else
			}
			else
			{
				possibleRaces = (context.raceLibrary as DynamicRaceLibrary).GetAllRacesBase();
				for (int i = 0; i < possibleRaces.Length; i++)
				{
					//we need to check that this is not null- the user may not have downloaded it yet
					if (possibleRaces[i] != null)
					{
						if (!Recipes.ContainsKey(possibleRaces[i].raceName) && possibleRaces[i].raceName != DynamicAssetLoader.Instance.placeholderRace.raceName)
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
            // JRRM - find slowdown
			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForCharactersToSearch;
           if (DynamicAssetLoader.Instance != null)
			    DynamicAssetLoader.Instance.AddAssets<TextAsset>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, downloadAssetsEnabled, assetBundleToGather, resourcesCharactersFolder, null, filename, AddCharacterRecipes);
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
			//This doesn't actually seem to do anything apart from slow things down
			//StartCoroutine(CleanFilesFromResourcesAndBundles());
		}

		private void GatherRecipeFiles(string filename = "", string bundleToGather = "")
		{
            // JRRM
			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForRecipesToSearch;
            if (DynamicAssetLoader.Instance != null)
			    DynamicAssetLoader.Instance.AddAssets<UMATextRecipe>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, downloadAssetsEnabled, assetBundleToGather, resourcesRecipesFolder, null, filename, AddRecipesFromAB);
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

		public void AddRecipes(UMATextRecipe[] uparts, string filename = "")
		{
			foreach (UMATextRecipe u in uparts)
			{
				if (filename == "" || (filename != "" && filename.Trim() == u.name))
				{
					//we might be refreshing so check its not already there
					if (!RecipeIndex.ContainsKey(u.name))
						RecipeIndex.Add(u.name, u);
					else
					{
						RecipeIndex[u.name] = u;
					}
					for (int i = 0; i < u.compatibleRaces.Count; i++)
					{
						if (Recipes.ContainsKey(u.compatibleRaces[i]))
						{
							Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes[u.compatibleRaces[i]];

							if (!RaceRecipes.ContainsKey(u.wardrobeSlot))
							{
								RaceRecipes.Add(u.wardrobeSlot, new List<UMATextRecipe>());
							}
							//we might be refreshing so replace anything that is already there with the downloaded versions- else add
							bool added = false;
							for (int ir = 0; ir < RaceRecipes[u.wardrobeSlot].Count; ir++)
							{
								if (RaceRecipes[u.wardrobeSlot][ir].name == u.name)
								{
									RaceRecipes[u.wardrobeSlot][ir] = u;
									added = true;
								}
							}
							if (!added)
							{
								RaceRecipes[u.wardrobeSlot].Add(u);
							}
						}
						//backwards compatible race slots
						foreach (string racekey in Recipes.Keys)
						{
							//here we also need to check that the race itself has a wardrobe slot that matches the one i the compatible race
							if ((context.raceLibrary as DynamicRaceLibrary).GetRace(racekey).backwardsCompatibleWith.Contains(u.compatibleRaces[i]) && (context.raceLibrary as DynamicRaceLibrary).GetRace(racekey).wardrobeSlots.Contains(u.wardrobeSlot))
							{
								Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes[racekey];
								if (!RaceRecipes.ContainsKey(u.wardrobeSlot))
								{
									RaceRecipes.Add(u.wardrobeSlot, new List<UMATextRecipe>());
								}
								//we might be refreshing so replace anything that is already there with the downloaded versions- else add
								bool added = false;
								for (int ir = 0; ir < RaceRecipes[u.wardrobeSlot].Count; ir++)
								{
									if (RaceRecipes[u.wardrobeSlot][ir].name == u.name)
									{
										RaceRecipes[u.wardrobeSlot][ir] = u;
										added = true;
									}
								}
								if (!added)
								{
									RaceRecipes[u.wardrobeSlot].Add(u);
								}
							}
						}
					}
				}
			}
			//This doesn't actually seem to do anything apart from slow things down
			//StartCoroutine(CleanFilesFromResourcesAndBundles());
		}

		public virtual UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
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
				Debug.Log(recipeName + " was not found in any loaded AssetBundle");
			}
			else
			{
				Debug.Log("originatingAssetBundle for " + recipeName + " was " + originatingAssetBundle);
			}
			return originatingAssetBundle;
		}
	}
}
