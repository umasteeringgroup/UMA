using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;//for marking converted colors as needing saving
#endif
using UnityEngine.Serialization;//for converting old characterColors.Colors to new colors

#if UNITY_5_5_OR_NEWER
using UnityEngine.Profiling;
#endif

using System;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.PoseTools;//so we can set the expression set based on the race
using System.IO;
using UMAAssetBundleManager;


namespace UMACharacterSystem
{
    //TODO when a scene that contains this component is included in an asset bundle it ends up having dependencies on some things that are defined in here
    // namely the race data and the default animator - I think these need to have no refrence to any assets at all so that the scene looses these dependencies
    // because the avatar is supposed to be dynamic and therefore of an undefined race and using an undefined animator. The same is true of the expressions player
    //this need to not have an expressionSet set so again the scene does not end up dependent on the asset that contains it
    public class DynamicCharacterAvatar : UMAAvatarBase
	{
        /// <summary>
        /// Callback event when the character recipe is updated. Use this to tweak the resulting recipe BEFORE the UMA is actually generated
        /// </summary>
        public UMADataEvent RecipeUpdated;
        //because the character might be loaded from an asset bundle, we may want everything required to create it to happen
        //but for it to still not be shown immediately
        [Tooltip("If checked will turn off the SkinnedMeshRenderer after the character has been created to hide it. If not checked will turn it on again.")]
		public bool hide = false;


        /// <summary>
        /// Set this before initialization to determine the active race. This can be set in the inspector
        /// using the activeRace dropdown.
        /// </summary>
        public string RacePreset
        {
            get
            {
                return activeRace.name;
            }
            set
            {
                activeRace.name = value;
            }
        }

        //This will generate itself from a list available Races and set itself to the current value of activeRace.name
        [Tooltip("Selects the race to used. When initialized, the Avatar will use the base recipe from the RaceData selected.")]
        public RaceSetter activeRace = new RaceSetter();
		[Flags]
		public enum ChangeRaceOptions
		{
			useDefaults = 0,
			cacheCurrentState = 1,
			keepDNA = 2,
			keepWardrobe = 4,
			keepBodyColors = 8
		};
		[EnumFlags]
		public ChangeRaceOptions defaultChangeRaceOptions = ChangeRaceOptions.cacheCurrentState | ChangeRaceOptions.keepBodyColors;

		public Dictionary<string, UMATextRecipe> WardrobeRecipes = new Dictionary<string, UMATextRecipe>();

		[Tooltip("You can add wardrobe recipes for many races in here and only the ones that apply to the active race will be applied to the Avatar")]
		public WardrobeRecipeList preloadWardrobeRecipes = new WardrobeRecipeList();

		[Tooltip("Add animation controllers here for specific races. If no Controller is found for the active race, the Default Animation Controller is used")]
		public RaceAnimatorList raceAnimationControllers= new RaceAnimatorList();

		//This now uses 'OvelayColorDatas' as characterColors.Colors but this should be completely transparent to the user
		[Tooltip("Any colors here are set when the Avatar is first generated and updated as the values are changed using the color sliders")]
		public ColorValueList characterColors = new ColorValueList();

		//public List<OverlayColorData> avatarColors = new List<OverlayColorData>();

		//in order to preserve the state of the Avatar when switching races (rather than having it loose its wardrobe when switching back and forth) we need to cache its current state before changing to the desired race
		Dictionary<string, CacheStateInfo> cacheStates = new Dictionary<string, CacheStateInfo>();
		class CacheStateInfo
		{
			public Dictionary<string, UMATextRecipe> wardrobeCache = new Dictionary<string, UMATextRecipe>();
			public List<ColorValue> colorCache = new List<ColorValue>();
			//new colors version
			public CacheStateInfo(Dictionary<string, UMATextRecipe> _wardrobeCache, List<ColorValue> _colorCache)
			{
				wardrobeCache = _wardrobeCache;
				colorCache = _colorCache;
			}
			public CacheStateInfo(List<ColorValue> _colorCache)
			{
				wardrobeCache = null;
				colorCache = _colorCache;
			}
		}
		//load/save fields
		public enum loadPathTypes { persistentDataPath, Resources, FileSystem, CharacterSystem, String };
		public loadPathTypes loadPathType;
		public string loadPath;
		public string loadFilename;
        public string loadString;
		public bool loadFileOnStart;
		[Tooltip("If true the avatar will not build until all the assets it requires have been downloaded. Otherwise a placeholder avatar will be built while assets are downloading and be updated when everything is available.")]
		public bool waitForBundles = true;
		[Tooltip("If true, after a load operation the avatar will be built. You can make this false if you want to load multiple recipes into the avatar and only build at the end (by making it true for the last operation).")]
		public bool buildAfterLoad = true;
		//loadOptions are for what you want to load from a given recipe
		[Flags]
		public enum LoadOptions {
			useDefaults = 0,
			loadRace = 1,
			loadDNA = 2,
			loadWardrobe = 4,
			loadBodyColors = 8,
			loadWardrobeColors = 16
		};
		[EnumFlags]
		public LoadOptions defaultLoadOptions = LoadOptions.loadRace | LoadOptions.loadDNA | LoadOptions.loadWardrobe | LoadOptions.loadBodyColors | LoadOptions.loadWardrobeColors;
		//
		public enum savePathTypes { persistentDataPath, Resources, FileSystem };
		public savePathTypes savePathType;
		public string savePath;
		public string saveFilename;
		[Tooltip("If true a GUID is generated and appended to the filename of the saved file")]
		public bool makeUniqueFilename = false;
		[Tooltip("If true ALL the colors in the 'characterColors' section of the component are added to the recipe on save. Otherwise only the colors used by the recipe are saved (UMA default)")]
		public bool ensureSharedColors = false;
		//save options are for what you want to save about the current avatar state
		[Flags]
		public enum SaveOptions
		{
			useDefaults = 0,
			saveDNA = 1,
			saveWardrobe = 2,
			saveColors = 4,
			saveAnimator = 8
		};
		[EnumFlags]
		public SaveOptions defaultSaveOptions = SaveOptions.saveDNA | SaveOptions.saveWardrobe | SaveOptions.saveColors | SaveOptions.saveAnimator;

		//
		public Vector3 BoundsOffset;

		private HashSet<string> HiddenSlots = new HashSet<string>();
		[HideInInspector]
		public List<string> assetBundlesUsedbyCharacter = new List<string>();
		[HideInInspector]
		public bool AssetBundlesUsedbyCharacterUpToDate = true;

		//a list of downloading assets that the avatar can check the download status of.
		[HideInInspector]
		public List<string> requiredAssetsToCheck = new List<string>();


		//this gets set by UMA when the chracater is loaded, but we need to know the race before hand so we can decide what wardrobe slots to use, so we have a work around above with RaceSetter.activeRace
		//IE DONT USE THIS
		RaceData RaceData
		{
			get {
				Debug.LogWarning("Dont use the RaceData property as it does not get the race correctly if it had to be downloaded from an assetBundle use activeRace.racedata instead");
				return base.umaRace;
			}
			set {
				Debug.LogWarning("Dont use the RaceData property as it does not get the race correctly if it had to be downloaded from an assetBundle use activeRace.racedata instead");
				base.umaRace = value;
			}
		}

        /// <summary>
        /// This returns all the recipes for the current race of the avatar.
        /// </summary>
        public Dictionary<string, List<UMATextRecipe>> AvailableRecipes
        {
            get
            {
				//changed RaceData.raceName because that is not correct until a race has been downloaded, activeRace.name is correct while the asset is downloading
				return (context.dynamicCharacterSystem as DynamicCharacterSystem).Recipes[activeRace.name];
            }
        }

		public List<string> CurrentWardrobeSlots
        {
            get
            {
				//DOS Dont use RaceData use activeRace.racedata instead because this has the correct values even if the race is being downloaded from an assetBundle
                return activeRace.racedata.wardrobeSlots;
            }
        }

        public OverlayColorData[] CurrentSharedColors
        {
            get
            {
                return umaData.umaRecipe.sharedColors;
            }
        }

		public List<ColorValue> ActiveColors
		{
			get
			{
				return characterColors.Colors;
			}
		}



		public override void Start()
		{
			//cache the starting colors
			var tempCols = new List<ColorValue>(characterColors.Colors.Count);
			foreach (ColorValue col in characterColors.Colors)
			{
				tempCols.Add(new ColorValue(col));
			}
			var thisCacheStateInfo = new CacheStateInfo(tempCols);
			//there is an issue here because we so differentiate between 'bodyColors' by which I mean colors in the baseRaceRecipe and 'wardrobeColors' by which I mean anything else
			//when a recipe loaded from string does not load bodyColors and wardrobe colors, it tries to fall back to this
			//BUT if the race of the recipe does not match the race the avatar started with, if the starting race defined 'Eyes' colors and the new race doesn't, 'Eyes' gets added when wardrobe colors get added
			cacheStates.Add("NULL", thisCacheStateInfo);
			//
			StopAllCoroutines();
			base.Start();
			//if the animator has been set the 'old' way respect that...
			if (raceAnimationControllers.defaultAnimationController == null && animationController != null)
			{
				raceAnimationControllers.defaultAnimationController = animationController;
			}
			//@jaimi I left this here so you know why its moved- 
			//We still need to run this from the coroutine since we cannot get anthing until DynamicAssetLoader is ready- if using asset bundles this will be once the index has downloaded otherwise immediate.
			/*if ((loadFilename != "" && loadFileOnStart) || (loadPathType == loadPathTypes.String))
			{
				DoLoad();
			}
			else
			{*/
				StartCoroutine(StartCO());
			//}
		}

		public void Update()
		{
			if (hide == true)
			{
				if (umaData != null)
				{
					if (umaData.myRenderer != null)
					{
						umaData.myRenderer.enabled = false;
					}
				}
			}
			else
			{
				if (umaData != null)
				{
					if (umaData.myRenderer != null)
					{
						umaData.myRenderer.enabled = true;
					}
				}
			}
			//This hardly ever happens now since the changeRace/LoadFromString/StartCO methods all yield themselves until asset bundles have been downloaded
			if (requiredAssetsToCheck.Count > 0 && !waitForBundles)
			{
				if (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck) == false)
				{
					Debug.Log("Update did build");
					requiredAssetsToCheck.Clear();
					activeRace.data = context.raceLibrary.GetRace(activeRace.name);
					umaRecipe = activeRace.data.baseRaceRecipe;
					UpdateSetSlots();
					//actually we dont know in this case if we are restoring DNA or not
					//but a placeholder race should only have been used if defaultLoadOptions.waitForBundles is false
					//so we can atleast assume we dont want to restore the dna from that
					BuildCharacter(waitForBundles);
				}
			}
		}

		//I kept the commented out 'mayRequireDownloads' stuff in here since it ma still be possible to workout if an avatar requires downloads and if it doesn't not to wait for the assetBundleIndex (if using AssetBundles)
		IEnumerator StartCO()
		{
			//we cannot do anthing until this is DynamicAssetLoader.Instance.isInitialized, otherwise the dynamicLibraries will not find anything from AssetBundles
			//if you are not using assetbundles, this wait will be non-existant otherwise it will be until the AssetBundleIndex has downloaded
			while (!DynamicAssetLoader.Instance.isInitialized)
			{
				yield return null;
			}
			//because external scripts may have also been waiting for DynamicAssetLoader before doing things they needed to do to set up this avatar (check if something will be in the libraries for example)
			//and because we need to be sure that the uma is generated after any such things have happenned we need to also yield one frame here. 
			//Please be sure to subscribe to CharacterCreated when want to do something immediately after the Avatar is created.
			yield return null;
			//if there was a file set to load or a recipe string set directly...
			if ((loadFilename != "" && loadFileOnStart) || (loadPathType == loadPathTypes.String))
			{
				DoLoad();
				yield break;
			}
			//if there was a recipe set in the old style UMARecipe field...
			if (umaRecipe != null)
			{
				LoadFromRecipe(umaRecipe);
				yield break;
			}
			//bool mayRequireDownloads = false;
			
			//calling activeRace.data checks the actual RaceLibrary to see if the raceData is in there since in the editor the asset may still be refrenced directly if it shouldn't be (i.e. its in an asset bundle)
			if (activeRace.data == null)//activeRace.data will get the asset from the library OR add it from Resources but if it in an assetBundle it wont trigger a download
			{
				//mayRequireDownloads = true;//but we dont know if its wrong until the index is downloaded so we have to wait for that
			}
			//if (mayRequireDownloads)
			//{
			//done above now
			/*while (!DynamicAssetLoader.Instance.isInitialized)
			{
				yield return null;
			}*/
			//}
			if (umaRecipe == null)
			{
				SetStartingRace();
				//If the UmaRecipe is still null, bail - we cant go any further (and SetStartingRace will have shown an error)
				if (umaRecipe == null)
				{
					yield break;
				}
				/*if (requiredAssetsToCheck.Count > 0)
					mayRequireDownloads = true;*/
			}
			if (DynamicAssetLoader.Instance.downloadingAssetsContains(activeRace.name))
			{
				requiredAssetsToCheck.Add(activeRace.name);
			}
			if (preloadWardrobeRecipes.loadDefaultRecipes && preloadWardrobeRecipes.recipes.Count > 0 )
			{
				LoadDefaultWardrobe(true);//this may cause downloads to happen
				/*if (requiredAssetsToCheck.Count > 0)
					mayRequireDownloads = true;*/
			}
			if (raceAnimationControllers.animators.Count > 0)
			{
				var animators = raceAnimationControllers.Validate();//this may cause downloads to happen
				if (animators.Count > 0)
				{
					for (int i = 0; i < animators.Count; i++)
					{
						if (DynamicAssetLoader.Instance.downloadingAssetsContains(animators[i]))
							requiredAssetsToCheck.Add(animators[i]);
					}
				}
				/*if (requiredAssetsToCheck.Count > 0)
					mayRequireDownloads = true;*/
			}
			//if we are not waiting for bundles before building, build the placeholder avatar now
			if (!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
			{
				BuildCharacter(false);
			}
			if (requiredAssetsToCheck.Count > 0)
			{
				while (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					yield return null;
				}
			}
			requiredAssetsToCheck.Clear();
			//we do these extra steps here to replace any refrences that were set pointing to a placeholder asset (because the asset was donloading) to now point to the downloaded asset
			activeRace.data = context.raceLibrary.GetRace(activeRace.name);
			umaRecipe = activeRace.data.baseRaceRecipe;
			UpdateSetSlots();
			//Now we have everything, lets go!
			BuildCharacter(false);
		}

		/// <summary>
		/// Sets the starting race of the avatar based on the value of the 'activeRace' dropdown. If it is in an assetbundle it will be downloaded and the placeholder race will be returned while the asset is downloading.
		/// </summary>
		void SetStartingRace()
		{
			//calling activeRace.data causes RaceLibrary to gather all racedatas from resources an returns all those along with any temporary assetbundle racedatas that are downloading
			//It will not cause any races to actually download
			if (activeRace.data != null)
			{
				activeRace.name = activeRace.data.raceName;
				umaRecipe = activeRace.data.baseRaceRecipe;
			}
			//otherwise...
			else if (activeRace.name != null)
			{
				//This only happens when the Avatar itself has an active race set to be one that is in an assetbundle
				activeRace.data = context.raceLibrary.GetRace(activeRace.name);// this will trigger a download if the race is in an asset bundle and return a temp asset
				if(activeRace.racedata != null)
					umaRecipe = activeRace.racedata.baseRaceRecipe;
			}
			//Failsafe: if everything else fails we try to do something based on the name of the race that was set
			if (umaRecipe == null)
			{//if its still null just load the first available race- try to match the gender at least
				var availableRaces = context.raceLibrary.GetAllRaces();
				if (availableRaces.Length > 0)
				{
					bool raceFound = false;
					if (activeRace.name.IndexOf("Female") > -1)
					{
						foreach (RaceData race in availableRaces)
						{
							if (race != null)
							{
								if (race.raceName.IndexOf("Female") > -1 || race.raceName.IndexOf("female") > -1)
								{
									activeRace.name = race.raceName;
									activeRace.data = race;
									umaRecipe = activeRace.racedata.baseRaceRecipe;
									raceFound = true;
								}
							}
						}
					}
					if (!raceFound)
					{
						Debug.Log("No base recipe found for race " + activeRace.name);
						activeRace.name = availableRaces[0].raceName;
						activeRace.data = availableRaces[0];
						umaRecipe = activeRace.racedata.baseRaceRecipe;
					}
				}
				else
				{
					Debug.LogError("[DynamicCharacterAvatar] There were no available racedatas to make an UMA from! Please either put your RaceDatas in a Resources folder or add them to (Dynamic)RaceLibrary directly");
				}
			}
			if (DynamicAssetLoader.Instance.downloadingAssetsContains(activeRace.name))
			{
				if (!requiredAssetsToCheck.Contains(activeRace.name))
					requiredAssetsToCheck.Add(activeRace.name);
			}
		}

		/// <summary>
		/// Loads the default wardobe items set in 'defaultWardrobeRecipes' in the CharacterAvatar itself onto the Avatar's base race recipe. Use this to make a naked avatar always have underwear or a set of clothes for example
		/// </summary>
		/// <param name="allowDownloadables">Optionally allow this function to trigger downloads of wardrobe recipes in an asset bundle</param>
		public void LoadDefaultWardrobe(bool allowDownloadables = false)
		{
			List<WardrobeRecipeListItem> validRecipes = preloadWardrobeRecipes.Validate(allowDownloadables, activeRace.name);
			if (validRecipes.Count > 0)
			{
				foreach (WardrobeRecipeListItem recipe in validRecipes)
				{
					if (recipe._recipe != null)
					{
						if (activeRace.name == "")//should never happen TODO: Check if it does
						{
							SetSlot(recipe._recipe);
							if (!requiredAssetsToCheck.Contains(recipe._recipeName))
							{
								requiredAssetsToCheck.Add(recipe._recipeName);
							}
						}
						else if (((recipe._recipe.compatibleRaces.Count == 0 || recipe._recipe.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.findBackwardsCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))))
						{
							//the check activeRace.data.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot) makes sure races that are backwards compatible 
							//with another race but which dont have all of that races wardrobeslots, dont try to load things they dont have wardrobeslots for
							//However we need to make sure that if a slot has already been assigned that is DIRECTLY compatible with the race it is not overridden
							//by one that is backwards compatible
							if (activeRace.racedata.findBackwardsCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))
							{
								if (!WardrobeRecipes.ContainsKey(recipe._recipe.wardrobeSlot))
								{
									SetSlot(recipe._recipe);
									if (!requiredAssetsToCheck.Contains(recipe._recipeName))
									{
										requiredAssetsToCheck.Add(recipe._recipeName);
									}
								}
							}
							else
							{
								SetSlot(recipe._recipe);
								if (!requiredAssetsToCheck.Contains(recipe._recipeName))
								{
									requiredAssetsToCheck.Add(recipe._recipeName);
								}
							}
						}
					}
					else
					{
						if (allowDownloadables)
						{
							//this means a temporary recipe was not returned for some reason
							Debug.LogWarning("[CharacterAvatar:LoadDefaultWardrobe] recipe._recipe was null for " + recipe._recipeName);
						}
					}
				}
			}
		}
		/// <summary>
		/// Sets the Expression set for the Avatar based on the Avatars set race.
		/// </summary>
		public void SetExpressionSet()
		{
			if (this.gameObject.GetComponent<UMAExpressionPlayer>() == null)
			{
				return;
			}
			UMAExpressionSet expressionSetToUse = null;
			if (activeRace.racedata != null)
			{
				expressionSetToUse = activeRace.racedata.expressionSet;
			}
			if (expressionSetToUse != null)
			{
				//set the expression set and reset all the values
				var thisExpressionsPlayer = this.gameObject.GetComponent<UMAExpressionPlayer>();
				thisExpressionsPlayer.expressionSet = expressionSetToUse;
				thisExpressionsPlayer.Values = new float[thisExpressionsPlayer.Values.Length];
			}
		}

		/// <summary>
		/// Sets the Animator Controller for the Avatar based on the best match found for the Avatars race. If no animator for the active race has explicitly been set, the default animator is used
		/// </summary>
		public void SetAnimatorController()
		{
			
			int validControllers = raceAnimationControllers.Validate().Count;//triggers resources load or asset bundle download of any animators that are in resources/asset bundles

			RuntimeAnimatorController controllerToUse = raceAnimationControllers.defaultAnimationController;
			if (validControllers > 0)
			{
				foreach (RaceAnimator raceAnimator in raceAnimationControllers.animators)
				{
					if (raceAnimator.raceName == activeRace.name && raceAnimator.animatorController != null)
					{
						controllerToUse = raceAnimator.animatorController;
						break;
					}
				}
			}
			animationController = controllerToUse;
			if (this.gameObject.GetComponent<Animator>())
			{
				this.gameObject.GetComponent<Animator>().runtimeAnimatorController = controllerToUse;
			}
		}

        /// <summary>
        /// Gets the color from the current shared colors.
        /// </summary>
        /// <param name="Name"></param>
        /// <returns></returns>
        public OverlayColorData GetColor(string Name)
        {
			OverlayColorData ocd;
			if (characterColors.GetColor(Name, out ocd))
				return ocd;
			return null;
		}

		/// <summary>
		/// Sets the given color name to the given OverlayColorData optionally updating the texture (default:true)
		/// </summary>
		/// <param name="Name"></param>
		/// <param name="colorData"></param>
		/// <param name="UpdateTexture"></param>
		public void SetColor(string Name, OverlayColorData colorData, bool UpdateTexture = true)
		{
			characterColors.SetColor(Name, colorData);
			if (UpdateTexture)
			{
				UpdateColors();
				ForceUpdate(false, UpdateTexture, false);
			}
		}

		/// <summary>
		/// Applies these colors to the loaded Avatar and adds any colors the loaded Avatar has which are missing from this list, to this list
		/// </summary>
		public void UpdateColors(bool triggerDirty = false)
		{
			foreach (UMA.OverlayColorData ucd in umaData.umaRecipe.sharedColors)
			{
				if (ucd.HasName())
				{
					OverlayColorData c;
					if (characterColors.GetColor(ucd.name, out c))
					{
						ucd.color = c.color;
						if (ucd.channelAdditiveMask.Length >= 3 && c.channelAdditiveMask.Length >=3)
							ucd.channelAdditiveMask[2] = c.channelAdditiveMask[2];
					}
					else
					{
						characterColors.SetColor(ucd.name, ucd);
					}
				}
			}
			if (triggerDirty)
			{
				ForceUpdate(false, true, false);
			}
		}

		/// <summary>
		/// Ensures all the colors in CharacterColors have been added to the umaData.umaRecipe.sharedColors
		/// </summary>
		void EnsureSharedColors()
		{
			//We have to do it a bit long windedly because OverlayColorDatas are refrences. If we dont do it like this you cannot edit the colors properly after saving
			var newSharedColors = new List<OverlayColorData>(characterColors.Colors.Count);
			for (int i = 0; i < characterColors.Colors.Count; i++)
			{
				bool found = false;
				for (int ri = 0; ri < umaData.umaRecipe.sharedColors.Length; ri++)
				{
					if (umaData.umaRecipe.sharedColors[ri].HasName())
					{
						if (characterColors.Colors[i].name == umaData.umaRecipe.sharedColors[ri].name)
						{
							umaData.umaRecipe.sharedColors[ri].AssignFrom(characterColors.Colors[i]);
							newSharedColors.Add(umaData.umaRecipe.sharedColors[ri]);
							found = true;
						}
					}
				}
				if (found == false)
				{
					newSharedColors.Add(characterColors.Colors[i]);
				}
			}
			umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
		}

		/// <summary>
		/// Builds the character by combining the Avatar's raceData.baseRecipe with the any wardrobe recipes that have been applied to the avatar.
		/// </summary>
		/// <returns>Can also be used to return an array of additional slots if this avatars flagForReload field is set to true before calling</returns>
		/// <param name="RestoreDNA">If updating the same race set this to true to restore the current DNA.</param>
		/// <param name="prioritySlot">Priority slot- use this to make slots lower down the wardrobe slot list suppress ones that are higher.</param>
		/// <param name="prioritySlotOver">a list of slots the priority slot overrides</param>
		public UMARecipeBase[] BuildCharacter(bool RestoreDNA = true, string prioritySlot = "", List<string> prioritySlotOver = default(List<string>))
		{
			if (prioritySlotOver == default(List<string>))
				prioritySlotOver = new List<string>();

			HiddenSlots.Clear();

			UMADnaBase[] CurrentDNA = null;
			if (umaData != null)
			{
				if (umaData.umaRecipe != null)
				{
					CurrentDNA = umaData.umaRecipe.GetAllDna();
				}
			}
			if (CurrentDNA == null)
				RestoreDNA = false;

			List<UMARecipeBase> Recipes = new List<UMARecipeBase>();
			List<string> SuppressSlotsStrings = new List<string>();

			if ((preloadWardrobeRecipes.loadDefaultRecipes || WardrobeRecipes.Count > 0) && activeRace.racedata != null)
			{
				foreach (UMATextRecipe utr in WardrobeRecipes.Values)
				{
					if (utr.suppressWardrobeSlots != null)
					{
						if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.findBackwardsCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
						{
							if (prioritySlotOver.Count > 0)
							{
								foreach (string suppressedSlot in prioritySlotOver)
								{
									if (suppressedSlot == utr.wardrobeSlot)
									{
										SuppressSlotsStrings.Add(suppressedSlot);
									}
								}
							}
							if (!SuppressSlotsStrings.Contains(utr.wardrobeSlot))
							{
								foreach (string suppressedSlot in utr.suppressWardrobeSlots)
								{
									if (prioritySlot == "" || prioritySlot != suppressedSlot)
										SuppressSlotsStrings.Add(suppressedSlot);
								}
							}
						}
					}
				}

				foreach (string ws in activeRace.racedata.wardrobeSlots)//this doesn't need to validate racedata- we wouldn't be here if it was null
				{
					if (SuppressSlotsStrings.Contains(ws))
					{
						continue;
					}
					if (WardrobeRecipes.ContainsKey(ws))
					{
						UMATextRecipe utr = WardrobeRecipes[ws];
						//we can use the race data here to filter wardrobe slots
						//if checking a backwards compatible race we also need to check the race has a compatible wardrobe slot, 
						//since while a race can be backwards compatible it does not *have* to have all the same wardrobeslots as the race it is compatible with
						if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.findBackwardsCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
						{
							Recipes.Add(utr);
							if (utr.Hides.Count > 0)
							{
								foreach (string s in utr.Hides)
								{
									HiddenSlots.Add(s);
								}
							}
						}
					}
				}
			}

			foreach (UMATextRecipe utr in umaAdditionalRecipes)
			{
				if (utr.Hides.Count > 0)
				{
					foreach (string s in utr.Hides)
					{
						HiddenSlots.Add(s);
					}
				}
			}

			//set the expression set to match the new character- needs to happen before load...
			if (activeRace.racedata != null && !RestoreDNA)
			{
				SetAnimatorController();
				SetExpressionSet();
			}

			// Load all the recipes
			Load(umaRecipe, Recipes.ToArray());

			// Add saved DNA
			if (RestoreDNA)
			{
				umaData.umaRecipe.ClearDna();
				foreach (UMADnaBase ud in CurrentDNA)
				{
					umaData.umaRecipe.AddDna(ud);
				}
			}
			return null;
		}

		/// <summary>
		/// Loads the Avatar from the given recipe and additional recipe. 
		/// Has additional functions for removing any slots that should be hidden by any 'wardrobe Recipes' that are in the additional recipes array.
		/// </summary>
		/// <param name="umaRecipe"></param>
		/// <param name="umaAdditionalRecipes"></param>
		public override void Load(UMARecipeBase umaRecipe, params UMARecipeBase[] umaAdditionalSerializedRecipes)
		{
			if (umaRecipe == null)
			{
				return;
			}
			if (umaData == null)
			{
				Initialize();
			}
			//set the umaData.animator if we have an animator already
			if (this.gameObject.GetComponent<Animator>())
			{
				umaData.animator = this.gameObject.GetComponent<Animator>();
			}
			Profiler.BeginSample("Load");

			this.umaRecipe = umaRecipe;

			umaRecipe.Load(umaData.umaRecipe, context);

			umaData.AddAdditionalRecipes(umaAdditionalRecipes, context);
			AddAdditionalSerializedRecipes(umaAdditionalSerializedRecipes);

			RemoveHiddenSlots();
			UpdateColors();

			//New event that allows for tweaking the resulting recipe before the character is actually generated
			RecipeUpdated.Invoke(umaData);

			if (umaRace != umaData.umaRecipe.raceData)
			{
				UpdateNewRace();
			}
			else
			{
				UpdateSameRace();
			}
			Profiler.EndSample();

			StartCoroutine(UpdateAssetBundlesUsedbyCharacter());
		}

		public void AddAdditionalSerializedRecipes(UMARecipeBase[] umaAdditionalSerializedRecipes)
		{
			if (umaAdditionalSerializedRecipes != null)
			{
				foreach (var umaAdditionalRecipe in umaAdditionalSerializedRecipes)
				{
					UMAData.UMARecipe cachedRecipe = umaAdditionalRecipe.GetCachedRecipe(context);
					umaData.umaRecipe.Merge(cachedRecipe, false);
				}
			}
		}

		void RemoveHiddenSlots()
		{
			List<SlotData> NewSlots = new List<SlotData>();
			foreach (SlotData sd in umaData.umaRecipe.slotDataList)
			{
				if (sd == null)
					continue;
				if (!HiddenSlots.Contains(sd.asset.slotName))
				{
					NewSlots.Add(sd);
				}
			}
			umaData.umaRecipe.slotDataList = NewSlots.ToArray();
		}

		protected void UpdateUMA()
		{
			if (umaRace != umaData.umaRecipe.raceData)
			{
				UpdateNewRace();
			}
			else
			{
				UpdateSameRace();
			}
		}

		public void ForceUpdate(bool DnaDirty, bool TextureDirty = false, bool MeshDirty = false)
		{
			umaData.Dirty(DnaDirty, TextureDirty, MeshDirty);
		}

		public void AvatarCreated()
		{
			SkinnedMeshRenderer smr = this.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
			smr.localBounds = new Bounds(smr.localBounds.center + BoundsOffset, smr.localBounds.size);
		}

		/// <summary>
		/// Clears all the wardrobe slots of any wardrobeRecipes that have been set on the avatar
		/// </summary>
		public void ClearSlots()
		{
			WardrobeRecipes.Clear();
		}
		/// <summary>
		/// Clears the given wardrobe slot of any recipes that have been set on the Avatar
		/// </summary>
		/// <param name="ws"></param>
		public void ClearSlot(string ws)
		{
			if (WardrobeRecipes.ContainsKey(ws))
			{
				WardrobeRecipes.Remove(ws);
			}
		}
		/// <summary>
		/// Use when temporary wardrobe recipes have been used while the real ones have been downloading. Will replace the temp textrecipes with the downloaded ones.
		/// </summary>
		public void UpdateSetSlots(string recipeToUpdate = "")
		{
			string slotsInWardrobe = "";
			foreach (KeyValuePair<string, UMATextRecipe> kp in WardrobeRecipes)
			{
				slotsInWardrobe = slotsInWardrobe + " , " + kp.Key;
			}
			Dictionary<string, UMATextRecipe> newWardrobeRecipes = new Dictionary<string, UMATextRecipe>();
			var thisDCS = context.dynamicCharacterSystem as DynamicCharacterSystem;
			foreach (KeyValuePair<string, UMATextRecipe> kp in WardrobeRecipes)
			{
				if (thisDCS.GetRecipe(kp.Value.name, false) != null)
				{
					newWardrobeRecipes.Add(kp.Key, thisDCS.GetRecipe(kp.Value.name, false));
				}
				else
				{
					newWardrobeRecipes.Add(kp.Key, kp.Value);
				}
			}
			WardrobeRecipes = newWardrobeRecipes;
		}

        /// <summary>
        /// Returns the name for any wardrobe item at that wardrobeslot.
        /// otherwise, returns an empty string;
        /// </summary>
        /// <param name="SlotName"></param>
        /// <returns></returns>
        public string GetWardrobeItemName(string SlotName)
        {
            if (WardrobeRecipes.ContainsKey(SlotName))
            {
                UMATextRecipe utr = WardrobeRecipes[SlotName];
                if (utr != null) return utr.name;
            }
            return "";
        }

		//DOS for this to work with AssetBundles you have to call GetRecipe(recipeName) in order for DynmicAssetLoader to get it
		//AvailableRecipes also doesn't handle backwards compatibility
        public UMATextRecipe FindSlotRecipe(string Slotname, string Recipename)
        {
			(context.dynamicCharacterSystem as DynamicCharacterSystem).GetRecipe(Recipename);

            var recipes = AvailableRecipes;
			if (recipes.ContainsKey(Slotname) != true) return null;

            List<UMATextRecipe> SlotRecipes = recipes[Slotname];

            for(int i = 0; i < SlotRecipes.Count;i++)
            {
                UMATextRecipe utr = SlotRecipes[i];
                if (utr.name == Recipename)
                    return utr;
            }
            return null;
        }
		/// <summary>
		/// Sets the avatars wardrobe slot to use the given wardrobe recipe (not to be mistaken with an UMA SlotDataAsset)
		/// </summary>
		/// <param name="utr"></param>
		public void SetSlot(UMATextRecipe utr)
		{
			if (utr.wardrobeSlot != "" && utr.wardrobeSlot != "None")
			{
				if (WardrobeRecipes.ContainsKey(utr.wardrobeSlot))
				{
					WardrobeRecipes[utr.wardrobeSlot] = utr;
				}
				else
				{
					WardrobeRecipes.Add(utr.wardrobeSlot, utr);
				}
			}
		}
		public void SetSlot(string Slotname, string Recipename)
        {
            UMATextRecipe utr = FindSlotRecipe(Slotname, Recipename);            
			if (!utr)
            {
                throw new Exception("Unable to find slot or recipe");
            }
			//DOS if the requested recipe ended up being downloaded add it to the requiredAssetsToCheck- this is what we use when waiting for bundles to check if we have everything we need
			if (DynamicAssetLoader.Instance.downloadingAssetsContains(utr.name))
			{
				requiredAssetsToCheck.Add(utr.name);
			}
			SetSlot(utr);
        }
		/// <summary>
		/// Searches the current WardrobeRecipes for recipes that are compatible or backwards compatible with this race. 
		/// If nothing is found and preloadWardrobeRecipes.loadDefaultRecipes is true, attempts to find any default wardrobe for this race
		/// </summary>
		public void ApplyCurrentWardrobeToNewRace()
		{
			var newWardrobeRecipes = new Dictionary<string, UMATextRecipe>();
			//then if there are recipes already check if any of them will match the new race and if they do let them override the defaults
			if (WardrobeRecipes.Count > 0)
			{
				foreach (KeyValuePair<string, UMATextRecipe> kp in WardrobeRecipes)
				{
					if (kp.Value.compatibleRaces.Contains(activeRace.name) || activeRace.racedata.findBackwardsCompatibleWith(kp.Value.compatibleRaces))
					{
						newWardrobeRecipes.Add(kp.Key, kp.Value);
					}
				}
			}
			if (newWardrobeRecipes.Count == 0 && preloadWardrobeRecipes.loadDefaultRecipes)
			{
				//No existing clothing fitted so get the valid default recipes for this race
				List<WardrobeRecipeListItem> validDefaultRecipes = preloadWardrobeRecipes.Validate(true, activeRace.name);
				for (int i = 0; i < validDefaultRecipes.Count; i++)
				{
					if (validDefaultRecipes[i]._recipe.compatibleRaces.Contains(activeRace.name) || activeRace.racedata.findBackwardsCompatibleWith(validDefaultRecipes[i]._recipe.compatibleRaces))
					{
						newWardrobeRecipes.Add(validDefaultRecipes[i]._recipe.wardrobeSlot, validDefaultRecipes[i]._recipe);
					}
				}
			}
			//then set to WardrobeRecipes
			WardrobeRecipes = newWardrobeRecipes;
		}
		/// <summary>
		/// Change the race of the Avatar, optionally overriding the 'onChangeRace' settings in the avatar component itself
		/// </summary>
		/// <param name="racename"></param>
		/// <param name="keepDNA">If true will attempt to transfer the dna settings for the current race into the dna settings for the new race</param>
		/// <param name="keepWardrobe">If true will attempt to keep the wardrobe the avatar is wearing on the new race if any of it is compatible</param>
		/// <param name="keepBodyColors">If true will keep any colors that are set in the current baseRaceRecipe the same when switching to the new race</param>
		/// <param name="cacheCurrentState">If true will cache the current state of the avatar in this race so that when you switch back to this race it will retain its clothing and colors</param>
		public void ChangeRace(string racename, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults)
		{
			ChangeRace((context.raceLibrary as DynamicRaceLibrary).GetRace(racename), customChangeRaceOptions);
		}
		/// <summary>
		/// Change the race of the Avatar, optionally overriding the 'onChangeRace' settings in the avatar component itself
		/// </summary>
		/// <param name="race"></param>
		/// <param name="keepDNA">If true will attempt to transfer the dna settings for the current race into the dna settings for the new race</param>
		/// <param name="keepWardrobe">If true will attempt to keep the wardrobe the avatar is wearing on the new race if any of it is compatible</param>
		/// <param name="keepBodyColors">If true will keep any colors that are set in the current baseRaceRecipe the same when switching to the new race</param>
		/// <param name="cacheCurrentState">If true will cache the current state of the avatar in this race so that when you switch back to this race it will retain its clothing and colors</param>
		public void ChangeRace(RaceData race, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults)
		{
			bool actuallyChangeRace = false;
			if (activeRace.racedata == null)
				actuallyChangeRace = true;
			else if (activeRace.name != race.raceName)
				actuallyChangeRace = true;
			if(actuallyChangeRace)
				StartCoroutine(ChangeRaceCoroutine(race, customChangeRaceOptions));
		}

		private IEnumerator ChangeRaceCoroutine(RaceData race, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults)
		{
			var thisChangeRaceOpts = customChangeRaceOptions == ChangeRaceOptions.useDefaults ? defaultChangeRaceOptions : customChangeRaceOptions;
			if (Application.isPlaying)
			{
				if (thisChangeRaceOpts.HasFlag(ChangeRaceOptions.cacheCurrentState))
				{
					if (!cacheStates.ContainsKey(activeRace.name))
					{
						var tempCols = new List<ColorValue>();
						foreach (ColorValue col in characterColors.Colors)
						{
							tempCols.Add(new ColorValue(col));
						}
						var thisCacheStateInfo = new CacheStateInfo(new Dictionary<string, UMATextRecipe>(WardrobeRecipes), tempCols);
						cacheStates.Add(activeRace.name, thisCacheStateInfo);
					}
					else
					{
						cacheStates[activeRace.name].wardrobeCache = new Dictionary<string, UMATextRecipe>(WardrobeRecipes);
						var tempCols = new List<ColorValue>();
						foreach (ColorValue col in characterColors.Colors)
						{
							tempCols.Add(new ColorValue(col));
						}
						cacheStates[activeRace.name].colorCache = tempCols;
					}
				}
				if (cacheStates.ContainsKey(race.raceName))
				{
					activeRace.name = race.raceName;
					activeRace.data = race;
					umaRecipe = race.baseRaceRecipe;
					//Keep wardrobe will try and keep the wardrobe the previous race had if any of it is compatible
					//if cacheStates is on and keep wardrobe is false it will attempt to load the last wardrobe this avatar had the last time it was this race
					if (!thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepWardrobe))
					{
						WardrobeRecipes = new Dictionary<string, UMATextRecipe>(cacheStates[race.raceName].wardrobeCache);
						//if the WardrobeRecipes count == 0 and preloadWardrobePrecipes = true then the Avatar was first created with a 'old' recipe that did not have wardrobe data
						if (WardrobeRecipes.Count == 0 && preloadWardrobeRecipes.loadDefaultRecipes == true)
						{
							LoadDefaultWardrobe(true);
						}
					}
					if (!thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepBodyColors))
					{
						characterColors.Colors = new List<ColorValue>(cacheStates[race.raceName].colorCache);
					}
					var prevDna1 = new UMADnaBase[0];
					if (thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepDNA))
					{
						prevDna1 = umaData.umaRecipe.GetAllDna();
					}
					BuildCharacter(false);
					if (thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepDNA))
					{
						TryImportDNAValues(prevDna1);
					}
					yield break;
				}
			}
			activeRace.name = race.raceName;
			activeRace.data = race;
			if (DynamicAssetLoader.Instance.downloadingAssetsContains(race.raceName))
			{
				requiredAssetsToCheck.Add(race.raceName);
			}
			if (Application.isPlaying)
			{
				var prevDna = new UMADnaBase[0];
				if (thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepDNA))
				{
					prevDna = umaData.umaRecipe.GetAllDna();
				}
				umaRecipe = activeRace.racedata.baseRaceRecipe;
				//if the WardrobeRecipes count == 0 and preloadWardrobePrecipes = true then the Avatar was first created with a 'old' recipe that did not have wardrobe data
				if (!thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepWardrobe) || (WardrobeRecipes.Count == 0 && preloadWardrobeRecipes.loadDefaultRecipes == true))
				{
					ClearSlots();
					LoadDefaultWardrobe(true);//load defaultWardrobe will add anything it downloads to requiredAssetsToCheck
				}
				if(!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					BuildCharacter(false);//if we are not waiting for bundles before building, build the placeholder avatar now
				}
				while (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					yield return null;
				}
				requiredAssetsToCheck.Clear();
				activeRace.data = context.raceLibrary.GetRace(activeRace.name);
				umaRecipe = activeRace.data.baseRaceRecipe;
				if(!thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepWardrobe))
					UpdateSetSlots();
				//if we are not keeping the body colors that have been set we reset them to the colors set in the avatar component itself
				if (!thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepBodyColors))
				{
					characterColors.Colors = new List<ColorValue>(cacheStates["NULL"].colorCache);
				}

				BuildCharacter(false);

				if (thisChangeRaceOpts.HasFlag(ChangeRaceOptions.keepDNA))
				{
					TryImportDNAValues(prevDna);
				}
			}
			yield break;
		}

		private void TryImportDNAValues(UMADnaBase[] prevDna)
		{
			umaData.umaRecipe.ApplyDNA(umaData, true);
			var activeDNA = umaData.umaRecipe.GetAllDna();
			for (int i = 0; i < activeDNA.Length; i++)
			{
				if (activeDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
				{
					//iterate over each dna in prev dna and try to apply its values to this dna
					foreach (UMADnaBase dna in prevDna)
					{
						((DynamicUMADnaBase)activeDNA[i]).ImportUMADnaValues(dna);
					}
				}
			}
		}

		/// <summary>
		/// get all of the DNA for the current character, and return it as a list of DnaSetters.
		/// Each DnaSetter will track the DNABase that it came from, and the character that it is attached
		/// to. To modify the DNA on the character, use the Set function on the Setter.
		/// </summary>
		/// <returns></returns>
		public Dictionary<string, DnaSetter> GetDNA()
		{
			Dictionary<string, DnaSetter> dna = new Dictionary<string, DnaSetter>();

			foreach (UMADnaBase db in umaData.GetAllDna())
			{
				for (int i = 0; i < db.Count; i++)
				{
					dna.Add(db.Names[i], new DnaSetter(db.Names[i], db.Values[i], i, db));
				}
			}
			return dna;
		}

		public UMADnaBase[] GetAllDNA()
		{
			return umaData.GetAllDna();
		}

		#region Load Save File Methods

		#region WardrobeSet Loading/Saving

		//with this loadWardrobeSet method, we are effectively doing a 'patial load' so perhaps it should be possible to do a 'partial load' of just race/colors/dna aswell?
		//It is actually with 'loadFromString' assuming the Avatar does not actually get built inbetween the partial loads (that will work fine, but may not be what one wants, you might want to only build after you have added lots of partial things)
		/// <summary>
		/// Loads a wardrobe set previously saved using 'SaveWardrobeSet'.
		/// </summary>
		/// <param name="wardrobeSet"></param>
		//Basically right now this will do exactly the same as calling loadFromString with LoadOptions.loadWardrobe as the only flag apart from the fact it doesn't actually build
		public void LoadWardrobeSet(string json)
		{
			var tempRecipe = JsonUtility.FromJson<UMATextRecipe.DCSPackRecipe>(json);
			LoadWardrobeSet(tempRecipe.wardrobeSet);
		}
		/// <summary>
		/// Load a wardrobe set.
		/// </summary>
		/// <param name="wardrobeSet"></param>
		//maybe we can have an option here like 'overDefaults' which if true, and the Avatar has not yet been built, will load this WardroneSet over the default recipes
		//and perhaps we want an 'andBuild' option for this so that if you are done loading the seperate things you can trigger the build process?
		public void LoadWardrobeSet(List<WardrobeSettings> wardrobeSet)
		{
			foreach (WardrobeSettings ws in wardrobeSet)
			{
				if (!string.IsNullOrEmpty(ws.recipe))
					SetSlot(ws.slot, ws.recipe);
				else
					ClearSlot(ws.slot);
			}
		}
		/// <summary>
		/// Saves the current wardrobe set to a lightweight json string.
		/// </summary>
		/// <param name="name">the name of the resulting recipe</param>
		/// <param name="slotsToSave">Optionally limit the wardrobe slots that are saved</param>
		/// <returns></returns>
		public string SaveWardrobeSet(string recipeName, params string[] slotsToSave)
		{
			var modelToSave = new UMATextRecipe.DCSPackRecipe(this, recipeName, "WardrobeCollection", SaveOptions.saveWardrobe, slotsToSave);
			return JsonUtility.ToJson(modelToSave);
		}

		//@jaimi now we dont need this. The menu options call the DoSave method which saves to the optimized DCAPackRecipe model
		/*
		/// <summary>
		/// Save the character to a lightweight JSON string
		/// </summary>
		/// <param name="customSaveOptions">optional flags to determine what aspects of the Avatar are saved in the recipe. Falls back to the defaults set in the Avatar SaveOptions</param>
		/// <returns></returns>
		public string ToJson(string recipeName = "", SaveOptions customSaveOptions = SaveOptions.useDefaults)
        {
			var thisSaveOptions = customSaveOptions == SaveOptions.useDefaults ? defaultSaveOptions : customSaveOptions;
			var modelToSave = new UMATextRecipe.DCSPackRecipe(this, recipeName, "DynamicCharacterAvatar", thisSaveOptions);
			return JsonUtility.ToJson(modelToSave);
		}*/

		//@jaimi now we dont need this. UMATextRecipe can now handle the optimized DCAPackRecipe model regardless of whether it came from an asset or a txt file
		/*
        /// <summary>
        /// Load the character from the lightweight JSON string
        /// </summary>
        /// <param name="json"></param>
        public void FromJson(string json)
        {
			//just use loadFromString
			LoadFromRecipeString(json);
        }
		*/
		#endregion

		#region Colors loading/saving
		/// <summary>
		/// Gets the shared colornames in the Avatars current race base recipe
		/// </summary>
		private List<string> GetBodyColorNames()
		{
			List<string> bodyColorNames = new List<string>();
			var baseRaceRecipeTemp = UMATextRecipe.PackedLoadDCS(context, (activeRace.data.baseRaceRecipe as UMATextRecipe).recipeString);
			foreach (OverlayColorData col in baseRaceRecipeTemp.sharedColors)
			{
				bodyColorNames.Add(col.name);
			}
			return bodyColorNames;
		}

		/// <summary>
		/// Loads any shared colors from the given recipe to the CharacterColors List, only if they are also defined in the current baseRaceRecipe, optionally applying then to the current UMAData.UMARecipe
		/// </summary>
		/// <param name="recipeToLoad"></param>
		/// <param name="apply"></param>
		/// <returns></returns>
		public List<OverlayColorData> LoadBodyColors(UMATextRecipe.DCSUniversalPackRecipe recipeToLoad, bool apply = false)
		{
			return LoadBodyOrWardrobeColors(recipeToLoad, true, apply);
		}
		/// <summary>
		/// Loads any shared colors from the given recipe to the CharacterColors List, only if they are NOT defined in the current baseRaceRecipe, optionally applying then to the current UMAData.UMARecipe
		/// </summary>
		/// <param name="recipeToLoad"></param>
		/// <param name="apply"></param>
		/// <returns></returns>
		public List<OverlayColorData> LoadWardrobeColors(UMATextRecipe.DCSUniversalPackRecipe recipeToLoad, bool apply = false)
		{
			return LoadBodyOrWardrobeColors(recipeToLoad, false, apply);
		}
		private List<OverlayColorData> LoadBodyOrWardrobeColors(UMATextRecipe.DCSUniversalPackRecipe recipeToLoad, bool loadingBody = true, bool apply = false)
		{
			List<string> bodyColorNames = GetBodyColorNames();
			List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
			if (loadingBody)
				foreach (OverlayColorData col in recipeToLoad.sharedColors)
				{
					if (bodyColorNames.Contains(col.name))
					{
						SetColor(col.name, col, false);
						if (!newSharedColors.Contains(col))
							newSharedColors.Add(col);
					}
				}
			else if (!loadingBody)
				foreach (OverlayColorData col in recipeToLoad.sharedColors)
				{
					if (!bodyColorNames.Contains(col.name))
					{
						SetColor(col.name, col, false);
						if (!newSharedColors.Contains(col))
							newSharedColors.Add(col);
					}
				}
			if (apply)
				umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
			return newSharedColors;
		}
		/// <summary>
		/// Restores the body colors to the ones defined in the component on start, optionally applying these to the UMAData.UMARecipe
		/// </summary>
		/// <param name="apply"></param>
		/// <returns></returns>
		public List<OverlayColorData> RestoreCachedBodyColors(bool apply = false)
		{
			return RestoreCachedBodyOrWardrobeColors(true, apply);
		}
		/// <summary>
		///  Restores the wardrobe colors to the ones defined in the component on start, optionally applying these to the UMAData.UMARecipe
		/// </summary>
		/// <param name="apply"></param>
		/// <returns></returns>
		public List<OverlayColorData> RestoreCachedWardrobeColors(bool apply = false)
		{
			return RestoreCachedBodyOrWardrobeColors(false, apply);
		}
		private List<OverlayColorData> RestoreCachedBodyOrWardrobeColors(bool restoringBody = true, bool apply = false)
		{
			List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
			if (!cacheStates.ContainsKey("NULL"))
				return newSharedColors;
			List<string> bodyColorNames = GetBodyColorNames();
			if (restoringBody)
			{
				foreach (OverlayColorData col in cacheStates["NULL"].colorCache)
				{
					if (bodyColorNames.Contains(col.name))
					{
						if (!GetColor(col.name))
						{
							SetColor(col.name, col, false);
							if (!newSharedColors.Contains(col))
								newSharedColors.Add(col);
						}
					}
				}
			}
			else
			{
				foreach (OverlayColorData col in cacheStates["NULL"].colorCache)
				{
					if (!bodyColorNames.Contains(col.name))
					{
						if (!GetColor(col.name))
						{
							SetColor(col.name, col, false);
							if (!newSharedColors.Contains(col))
								newSharedColors.Add(col);
						}
					}
				}
			}
			if (apply)
				umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
			return newSharedColors;
		}

		#endregion

		//possible we could do something similar with DNA?

		#region Full Avatar Load/Save
		/// <summary>
		/// Returns the UMATextRecipe string with the addition of the Avatars current WardrobeSet.
		/// </summary>
		/// <param name="backwardsCompatible">If true, slot and overlay data is included and you can load the recipe into a non-dynamicCharacterAvatar.</param>
		/// <returns></returns>
		public string GetCurrentRecipe(bool backwardsCompatible = true)
		{
			// save 
			UMATextRecipe u = new UMATextRecipe();
			u.Save(umaData.umaRecipe, context, WardrobeRecipes, backwardsCompatible);
			return u.recipeString;
		}

        public void Preload(string Recipe)
        {
            loadString = Recipe;
            loadPathType = loadPathTypes.String;
            loadFileOnStart = true;
        }

		/// <summary>
		/// Load a DynamicCharacterAvatar from an UMATextRecipe Asset, optionally waiting for any assets that will need to be downloaded (according to the CharacterAvatar 'defaultLoadOptions.waitForBundles' setting) and optionally overriding the 'LoadOptions' settings for loading the Race/Dna/Wardrobe etc for the recipe
		/// </summary>
		/// <param name="umaRecipeToLoad"></param>
		/// <param name="customLoadOptions">Override the default LoadOptions and only load parts of the recipe</param>
		public void LoadFromRecipe(UMARecipeBase umaRecipeToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults)
		{
			umaRecipe = umaRecipeToLoad;
			if (UMATextRecipe.GetRecipesType((umaRecipe as UMATextRecipe).recipeString) == "Wardrobe" || (umaRecipe as UMATextRecipe).recipeType == "Wardrobe")
			{
				Debug.LogWarning("The assigned UMATextRecipe was a Wardrobe Recipe. You cannot load a character from a Wardrobe Recipe");
				return;
			}
			LoadFromRecipeString((umaRecipe as UMATextRecipe).recipeString, customLoadOptions);
		}
		/// <summary>
		/// Loads an avatar from a recipe string, optionally waiting for any assets that will need to be downloaded (according to the CharacterAvatar 'defaultLoadOptions.waitForBundles' setting) and optionally overriding the 'LoadOptions' settings for loading the Race/Dna/Wardrobe etc for the recipe
		/// This cannot be called before initialization. 
		/// Use Preload when calling before Initialization.
		/// </summary>
		/// <param name="recipeString"></param>
		/// <returns></returns>
		public void LoadFromRecipeString(string recipeText, LoadOptions customLoadOptions = LoadOptions.useDefaults)
		{
			StartCoroutine(LoadFromRecipeStringCO(recipeText, customLoadOptions));
		}

		private IEnumerator LoadFromRecipeStringCO(string recipeString, LoadOptions customLoadOptions = LoadOptions.useDefaults)
		{
			var thisLoadOptions = customLoadOptions == LoadOptions.useDefaults ? defaultLoadOptions : customLoadOptions;

			if (umaGenerator == null)
			{
				umaGenerator = UMAGenerator.FindInstance();
			}
            if (umaData == null)
            {
                Initialize();
            }
			//new model from UMATextRecipe.UMAPackRecipeCharacterSystem- but we would rather have jaimis model
			//Testing if the txt in the file was actually the text for an asset
			var tempRecipe = UMATextRecipe.PackedLoadDCS(context, recipeString);//this is the equivalent of packedLoad- so we want this to deliver something in the case of a 'standard' or wardrpbe load OR the 'right stuff' in the case of a DCSload
			var prevDna = new UMADnaBase[0];
			string raceString = tempRecipe.race;
			if (raceString != "")
			{
				if(context.GetRace(raceString) == null)
				{
					Debug.LogError("Race " + raceString + " not found in RaceLibrary");
					yield break;
				}
			}
			if ((!thisLoadOptions.HasFlag(LoadOptions.loadDNA) || tempRecipe.packedDna.Count == 0) && activeRace.racedata != null)
			{
				prevDna = umaData.umaRecipe.GetAllDna();
			}
			if (thisLoadOptions.HasFlag(LoadOptions.loadRace))
			{
				activeRace.name = tempRecipe.race;
				activeRace.data = context.GetRace(raceString);
				//if getting that race made a download happen log that
				if (DynamicAssetLoader.Instance.downloadingAssetsContains(activeRace.name))
					requiredAssetsToCheck.Add(activeRace.name);
				//but we can set startingRace while its downloading
				SetStartingRace();
				//If the UmaRecipe is still null it means no recipe was found anywhere so bail (setStartingRace will have logged a warning)
				if (umaRecipe == null)
				{
					yield break;
				}
			}
			//might have a problem here- this wont actually be null for old recipes...
			if(tempRecipe.wardrobeSet != null)//when we create the model this is null unless there is data so this will tell us if this was DCS style
			{
				//if we are not waiting for asset bundles we can build the placeholder avatar
				if (!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					BuildCharacter(false);
				}
				//before we can check if any of the existing wardrobe or any existing colors are compatible we need to have the actual racedata downloaded
				while (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					yield return null;
				}
				requiredAssetsToCheck.Clear();
				//we have the race now so replace the placeholder with the real one
				if (thisLoadOptions.HasFlag(LoadOptions.loadRace))
				{
					activeRace.data = context.raceLibrary.GetRace(activeRace.name);
					umaRecipe = activeRace.data.baseRaceRecipe;
				}
				//if we are loading wardrobe override everything that was previously set (by the default wardrobe or any previous user modifications)
				if (thisLoadOptions.HasFlag(LoadOptions.loadWardrobe))
				{
					ClearSlots();
					if (tempRecipe.wardrobeSet.Count > 0)
					{
						LoadWardrobeSet(tempRecipe.wardrobeSet);
                    }
				}
				//otherwise its a bit more complicated
				else
				{
					ApplyCurrentWardrobeToNewRace();
                }
				//loading new wardrobe items may have also caused downloads so wait for those- if we are not waiting we will have already created the placeholder avatar above
				while (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					yield return null;
				}
				requiredAssetsToCheck.Clear();
				UpdateSetSlots();
				//Sort out colors
				List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
				//if we are loading BOTH BodyColors and WardrobeColors clear the colorList so we only have those colors in it
				//Only do this if there are shared colors to use
				if (thisLoadOptions.HasFlag(LoadOptions.loadBodyColors) && thisLoadOptions.HasFlag(LoadOptions.loadWardrobeColors) && tempRecipe.sharedColors.Length > 0)
				{
					characterColors.Colors.Clear();
				}
				if (thisLoadOptions.HasFlag(LoadOptions.loadBodyColors) && tempRecipe.sharedColors.Length > 0)
				{
					newSharedColors.AddRange(LoadBodyColors(tempRecipe, false));
				}
				if (thisLoadOptions.HasFlag(LoadOptions.loadWardrobeColors) && tempRecipe.sharedColors.Length > 0)
				{
					newSharedColors.AddRange(LoadWardrobeColors(tempRecipe, false));
				}
				//if we were not loading both things then we want to restore any colors that were set in the Avatar settings if the characterColors does not already contain a color for that name
				if(!thisLoadOptions.HasFlag(LoadOptions.loadBodyColors) || !thisLoadOptions.HasFlag(LoadOptions.loadWardrobeColors) || tempRecipe.sharedColors.Length == 0)
				{
					if(!thisLoadOptions.HasFlag(LoadOptions.loadBodyColors) || tempRecipe.sharedColors.Length == 0)
					{
						newSharedColors.AddRange(RestoreCachedBodyColors(false));
					}
					if (!thisLoadOptions.HasFlag(LoadOptions.loadWardrobeColors) || tempRecipe.sharedColors.Length == 0)
					{
						newSharedColors.AddRange(RestoreCachedWardrobeColors(false));
					}
				}
				umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
				//
				if(buildAfterLoad)//but what if we are just adding multiple dna from multiple recipes and not building till we are done?
					BuildCharacter(false);
				//
				if (thisLoadOptions.HasFlag(LoadOptions.loadDNA) && tempRecipe.packedDna.Count > 0)
				{
					umaData.umaRecipe.ClearDna();
					foreach (UMADnaBase dna in tempRecipe.GetAllDna())
					{
						umaData.umaRecipe.AddDna(dna);
					}
				}
				else if(prevDna.Length > 0)
				{
					TryImportDNAValues(prevDna);
                }
			}
			else
			{
				//old style recipes may still have had assets in an asset bundle. So if we are showing a plaeholder rather than waiting...
				if (!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					BuildCharacter(false);
				}
				while (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					yield return null;
				}
				requiredAssetsToCheck.Clear();
				//we have the race now so replace the placeholder with the real one
				if (thisLoadOptions.HasFlag(LoadOptions.loadRace))
				{
					activeRace.data = context.raceLibrary.GetRace(activeRace.name);
					umaRecipe = activeRace.data.baseRaceRecipe;
				}
				ClearSlots();
				//if its a standard UmaTextRecipe load it directly into UMAData since there wont be any wardrobe slots...
				var umaTextRecipe = ScriptableObject.CreateInstance<UMATextRecipe>();
				umaTextRecipe.name = loadFilename;
				umaTextRecipe.recipeString = recipeString;
				umaTextRecipe.Load(umaData.umaRecipe, context);
				//BUT we do need to add the 'additional recipes'
				umaData.AddAdditionalRecipes(umaAdditionalRecipes, context);
				//shared colors
				umaData.umaRecipe.sharedColors = tempRecipe.sharedColors;
				//
				foreach (OverlayColorData col in umaData.umaRecipe.sharedColors)
				{
					SetColor(col.name, col, false);
				}
				SetAnimatorController();
				SetExpressionSet();
				UpdateColors();
				if (thisLoadOptions.HasFlag(LoadOptions.loadDNA) && tempRecipe.packedDna.Count > 0)
				{
					umaData.umaRecipe.ClearDna();
					foreach (UMADnaBase dna in tempRecipe.GetAllDna())
					{
						umaData.umaRecipe.AddDna(dna);
					}
				}
				else if (prevDna.Length > 0)
				{
					TryImportDNAValues(prevDna);
				}
				if (umaRace != umaData.umaRecipe.raceData)
				{
					UpdateNewRace();
				}
				else
				{
					UpdateSameRace();
				}
				StartCoroutine(UpdateAssetBundlesUsedbyCharacter());
				Destroy(umaTextRecipe);
			}
			tempRecipe = null;
			yield break;//never sure if we need to do this?
		}

		/// <summary>
		/// Loads the text file in the loadFilename field to get its recipe string, and then calls LoadFromRecipeString to to process the recipe and load the Avatar.
		/// </summary>
		public void DoLoad()
		{
			StartCoroutine(DoLoadCoroutine());
		}
		IEnumerator DoLoadCoroutine()
		{
			string path = "";
			string recipeString = "";

            if (!string.IsNullOrEmpty(loadString)&&loadPathType == loadPathTypes.String)
            {
                recipeString = loadString;
            }
#if UNITY_EDITOR
			if (loadFilename == "" && Application.isEditor && loadPathType != loadPathTypes.String)
			{
				loadPathType = loadPathTypes.FileSystem;
			}
#endif
			var thisDCS = context.dynamicCharacterSystem as DynamicCharacterSystem;
			if (loadPathType == loadPathTypes.CharacterSystem)
			{
				if (thisDCS.CharacterRecipes.ContainsKey(loadFilename.Trim()))
				{
					thisDCS.CharacterRecipes.TryGetValue(loadFilename.Trim(), out recipeString);
				}
			}
			if (loadPathType == loadPathTypes.FileSystem)
			{
#if UNITY_EDITOR
				if (Application.isEditor)
				{
					path = EditorUtility.OpenFilePanel("Load saved Avatar", Application.dataPath, "txt");
					if (string.IsNullOrEmpty(path)) yield break;
					recipeString = FileUtils.ReadAllText(path);
					path = "";
				}
				else
#endif
					loadPathType = loadPathTypes.persistentDataPath;
			}
			if (loadPathType == loadPathTypes.persistentDataPath)
			{
				path = Application.persistentDataPath;
			}
			if (path != "" || loadPathType == loadPathTypes.Resources)
			{
				if (loadPathType == loadPathTypes.Resources)
				{
					TextAsset[] textFiles = Resources.LoadAll<TextAsset>(loadPath);
					for (int i = 0; i < textFiles.Length; i++)
					{
						if (textFiles[i].name == loadFilename.Trim() || textFiles[i].name.ToLower() == loadFilename.Trim())
						{
							recipeString = textFiles[i].text;
						}
					}
				}
				else
				{
					path = (loadPath != "") ? System.IO.Path.Combine(path, loadPath.TrimStart('\\', '/').TrimEnd('\\', '/').Trim()) : path;
					if (loadFilename == "")
					{
						Debug.LogWarning("[CharacterAvatar.DoLoad] No filename specified to load!");
						yield break;
					}
					else
					{
						if (path.Contains("://"))
						{
							WWW www = new WWW(path + loadFilename);
							yield return www;
							recipeString = www.text;
						}
						else
						{
							recipeString = FileUtils.ReadAllText(System.IO.Path.Combine(path, loadFilename));
						}
					}
				}
			}
			if (recipeString != "")
			{
				LoadFromRecipeString(recipeString);
				yield break;
			}
			else
			{
				Debug.LogWarning("[CharacterAvatar.DoLoad] No TextRecipe found with filename " + loadFilename);
			}
			yield break;
		}

		/// <summary>
		/// Saves the current DynamicCharacterAvatar using the optimized DCSPackRecipe model. This has smaller file size but the resulting recipe strings will not work with 'non-DynamicCharacterAvatar' avatars
		/// </summary>
		/// <param name="saveAsAsset">If true will save the resulting asset, otherwise saves the string to a txt file</param>
		/// <param name="filePath">If no file path is supplied it will be generated based on the settings in the Save section of the component</param>
		/// <param name="customSaveOptions">Override the default save options as defined in the avatar save section, to only save specific properties of the Avatar</param>
		public void DoSave(bool saveAsAsset = false, string filePath = "", SaveOptions customSaveOptions = SaveOptions.useDefaults )
		{
#if !UNITY_EDITOR
			saveAsAsset = false;
#endif
			//umaData.umaRecipe.sharedColors = characterColors.ToOverlayColors();//@jaimi doing it this way meant we could no longer edit the colors in the inspector after saving
			var saveOptionsToUse = customSaveOptions == SaveOptions.useDefaults ? defaultSaveOptions : customSaveOptions;
			if(ensureSharedColors)
				EnsureSharedColors();
			string extension = saveAsAsset ? "asset" : "txt";
			var origSaveType = savePathType;
			if (saveAsAsset)
				savePathType = savePathTypes.FileSystem;
			if(filePath == "")
				filePath = GetSavePath(extension);
			savePathType = origSaveType;
			if (filePath != "")
			{
				var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
				var recipeName = saveFilename != "" ? saveFilename : gameObject.name+"_DCSRecipe";
				asset.SaveDCS(this, recipeName, saveOptionsToUse);
				if(!saveAsAsset)
					FileUtils.WriteAllText(filePath, asset.recipeString);
				else
				{
					asset.recipeType = "DynamicCharacterAvatar";

                    AssetDatabase.CreateAsset(asset, filePath);
					AssetDatabase.SaveAssets();
				}

				Debug.Log("Recipe saved to " + filePath);
				if (savePathType == savePathTypes.Resources)
				{
#if UNITY_EDITOR
					AssetDatabase.Refresh();
#endif
				}
				if (!saveAsAsset)
					ScriptableObject.Destroy(asset);
			}

		}

		string GetSavePath(string extension)
		{
			string path = "";
			string filePath = "";

#if UNITY_EDITOR
			if (saveFilename == "" && Application.isEditor)
			{
				savePathType = savePathTypes.FileSystem;
			}
#endif
			if (savePathType == savePathTypes.FileSystem)
			{
#if UNITY_EDITOR
				if (Application.isEditor)
				{
					path = EditorUtility.SaveFilePanel("Save Avatar", Application.dataPath, (saveFilename != "" ? saveFilename : ""), extension);
					if (path == "")
						return "";//user cancelled save
					else
						saveFilename = Path.GetFileNameWithoutExtension(path);

				}
				else
#endif
					savePathType = savePathTypes.persistentDataPath;

			}
			//I dont think we can save anywhere but persistentDataPath on most platforms
			if (savePathType == savePathTypes.Resources)
			{
#if UNITY_EDITOR
				if (!Application.isEditor)
#endif
					savePathType = savePathTypes.persistentDataPath;
			}
			if (savePathType == savePathTypes.Resources)
			{
				path = System.IO.Path.Combine(Application.dataPath, "Resources");//This needs to be exactly the right folder to work in the editor
			}
			else if (savePathType == savePathTypes.persistentDataPath)
			{
				path = Application.persistentDataPath;
			}
			if (path != "")
			{
				if (savePathType != savePathTypes.FileSystem)
				{
					if (!Directory.Exists(path))
					{
						Directory.CreateDirectory(path);
					}
				}
				if (makeUniqueFilename || (saveFilename == "" && savePathType != savePathTypes.FileSystem))
				{
					saveFilename = saveFilename + Guid.NewGuid().ToString();
				}
				if (savePathType != savePathTypes.FileSystem)
				{
					path = (savePath != "") ? System.IO.Path.Combine(path, savePath.TrimStart('\\', '/').TrimEnd('\\', '/').Trim()) : path;
					filePath = System.IO.Path.Combine(path, saveFilename + "."+ extension);
					FileUtils.EnsurePath(path);
				}
				else
				{
					filePath = path;
				}
				return filePath;
			}
			else
			{
				Debug.LogError("CharacterSystem Save Error! Could not save file, check you have set the filename and path correctly...");
				return "";
			}
		}
		#endregion

		#endregion

		/// <summary>
		/// Checks what assetBundles (if any) were used in the creation of this Avatar. NOTE: Query this UMA's AssetBundlesUsedbyCharacterUpToDate field before calling this function
		/// </summary>
		/// <param name="verbose">set this to true to get more information to track down when asset bundles are getting dependencies when they shouldn't because they are refrencing things from asset bundles you did not intend them to</param>
		/// <returns></returns>
		//DOS NOTES: The fact this is an Enumerator may cause issues for any script wanting to get upToDate data- on the otherhand we dont want to slow down rendering by making it wait for this list
		IEnumerator UpdateAssetBundlesUsedbyCharacter(bool verbose = false)
		{
			AssetBundlesUsedbyCharacterUpToDate = false;
			yield return null;
			assetBundlesUsedbyCharacter.Clear();
			if (umaData != null)
			{
				var raceLibraryDict = ((DynamicRaceLibrary)context.raceLibrary as DynamicRaceLibrary).assetBundlesUsedDict;
				var slotLibraryDict = ((DynamicSlotLibrary)context.slotLibrary as DynamicSlotLibrary).assetBundlesUsedDict;
				var overlayLibraryDict = ((DynamicOverlayLibrary)context.overlayLibrary as DynamicOverlayLibrary).assetBundlesUsedDict;
				var characterSystemDict = ((DynamicCharacterSystem)context.dynamicCharacterSystem as DynamicCharacterSystem).assetBundlesUsedDict;
				var raceAnimatorsDict = raceAnimationControllers.assetBundlesUsedDict;
				if (raceLibraryDict.Count > 0)
				{
					foreach (KeyValuePair<string, List<string>> kp in raceLibraryDict)
					{
						if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
							if (kp.Value.Contains(activeRace.name))
							{
								assetBundlesUsedbyCharacter.Add(kp.Key);
							}
					}
				}
				var activeSlots = umaData.umaRecipe.GetAllSlots();
				if (slotLibraryDict.Count > 0)
				{
					foreach (SlotData slot in activeSlots)
					{
						if (slot != null)
						{
							foreach (KeyValuePair<string, List<string>> kp in slotLibraryDict)
							{
								if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
									if (kp.Value.Contains(slot.asset.name))
									{
										if (verbose)
											assetBundlesUsedbyCharacter.Add(kp.Key + " (Slot:" + slot.asset.name + ")");
										else
											assetBundlesUsedbyCharacter.Add(kp.Key);
									}
							}
						}
					}
				}
				if (overlayLibraryDict.Count > 0)
				{
					foreach (SlotData slot in activeSlots)
					{
						if (slot != null)
						{
							var overLaysinSlot = slot.GetOverlayList();
							foreach (OverlayData overlay in overLaysinSlot)
							{
								foreach (KeyValuePair<string, List<string>> kp in overlayLibraryDict)
								{
									if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
										if (kp.Value.Contains(overlay.asset.name))
										{
											if (verbose)
												assetBundlesUsedbyCharacter.Add(kp.Key + " (Overlay:" + overlay.asset.name + ")");
											else
												assetBundlesUsedbyCharacter.Add(kp.Key);
										}
								}
							}
						}
					}
				}
				if (characterSystemDict.Count > 0)
				{
					foreach (KeyValuePair<string, UMATextRecipe> recipe in WardrobeRecipes)
					{
						foreach (KeyValuePair<string, List<string>> kp in characterSystemDict)
						{
							if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
								if (kp.Value.Contains(recipe.Key))
								{
									assetBundlesUsedbyCharacter.Add(kp.Key);
								}
						}
					}
				}
				string specificRaceAnimator = "";
				foreach (RaceAnimator raceAnimator in raceAnimationControllers.animators)
				{
					if (raceAnimator.raceName == activeRace.name && raceAnimator.animatorController != null)
					{
						specificRaceAnimator = raceAnimator.animatorControllerName;
						break;
					}
				}
				if (raceAnimatorsDict.Count > 0 && specificRaceAnimator != "")
				{
					foreach (KeyValuePair<string, List<string>> kp in raceAnimatorsDict)
					{
						if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
							if (kp.Value.Contains(specificRaceAnimator))
							{
								assetBundlesUsedbyCharacter.Add(kp.Key);
							}
					}
				}
			}
			AssetBundlesUsedbyCharacterUpToDate = true;
			yield break;
		}
		/// <summary>
		/// Returns the list of AssetBundles used by the avatar. IMPORTANT you must wait for AssetBundlesUsedbyCharacterUpToDate to be true before calling this method.
		/// </summary>
		/// <returns></returns>
		public List<string> GetAssetBundlesUsedByCharacter()
		{
			return assetBundlesUsedbyCharacter;
		}

#region special classes

		[Serializable]
		public class RaceSetter
		{
			public string name;
			[SerializeField]
			RaceData _data;//This was not properly reporting itself as 'missing' when it is set to an asset that is in an asset bundle, so now data is a property that validates itself
			[SerializeField]//Needs to be serialized for the inspector but otherwise no- TODO what happens in a build? will this get saved across sessions- because we dont want that
			RaceData[] _cachedRaceDatas;

			//we will do this as flags too- probably in the DCA fields above
			//public bool keepDNA = false;
			//public bool keepWardrobe = false;
			//public bool keepBodyColors = true;
			//public bool cacheCurrentState = true;


			/// <summary>
			/// Will return the current active racedata- if it is in an asset bundle or in resources it will find/download it. If you ony need to know if the data is there use the racedata field instead.
			/// </summary>
			//DOS NOTES this is because we have decided to make the libraries get stuff they dont have and return a temporary asset- but there are still occasions where we dont want this to happen
			// or at least there are occasions where we JUST WANT THE LIST and dont want it do do anything else...
			public RaceData data
			{
				get
				{
					if (Application.isPlaying)
						return Validate();
					else
						return _data;
				}
				set
				{
					_data = value;
					/*if (Application.isPlaying)
                        Validate();*/
				}
			}
			/// <summary>
			/// returns the active raceData (quick)
			/// </summary>
			public RaceData racedata
			{
				get { return _data; }
			}

			RaceData Validate()
			{
				RaceData racedata = null;
				var thisContext = UMAContext.FindInstance();
				var thisDynamicRaceLibrary = (DynamicRaceLibrary)thisContext.raceLibrary as DynamicRaceLibrary;
				_cachedRaceDatas = thisDynamicRaceLibrary.GetAllRaces();
				//Debug.LogWarning("[RaceSetter] called Validate()");
				foreach (RaceData race in _cachedRaceDatas)
				{
					if (race.raceName == this.name)
						racedata = race;
				}
				return racedata;
			}
		}

		[Serializable]
		public class WardrobeRecipeListItem
		{
			public string _recipeName;
			public UMATextRecipe _recipe;
			//store compatible races here because when a recipe is not downloaded we dont have access to this info...
			public List<string> _compatibleRaces;

			public WardrobeRecipeListItem()
			{

			}
			public WardrobeRecipeListItem(string recipeName)//TODO: Does this constructor ever get used? We dont want it to...
			{
				_recipeName = recipeName;
			}
			public WardrobeRecipeListItem(UMATextRecipe recipe)
			{
				_recipeName = recipe.name;
				_recipe = recipe;
				_compatibleRaces = recipe.compatibleRaces;
			}
		}

		[Serializable]
		public class WardrobeRecipeList
		{
			[Tooltip("If this is checked and the Avatar is NOT creating itself from a previously saved recipe, recipes in here will be added to the Avatar when it loads")]
			public bool loadDefaultRecipes = true;
			public List<WardrobeRecipeListItem> recipes = new List<WardrobeRecipeListItem>();

			public List<WardrobeRecipeListItem> Validate(/*DynamicCharacterSystem characterSystem,*/ bool allowDownloadables = false, string raceName = "")
			{
				List<WardrobeRecipeListItem> validRecipes = new List<WardrobeRecipeListItem>();
				var thisDCS = UMAContext.Instance.dynamicCharacterSystem as DynamicCharacterSystem;
				if (thisDCS != null)
				{
					foreach (WardrobeRecipeListItem recipe in recipes)
					{
						if (allowDownloadables && (raceName == "" || recipe._compatibleRaces.Contains(raceName)))
						{
							if (thisDCS.GetRecipe(recipe._recipeName, true) != null)
							{
								recipe._recipe = thisDCS.GetRecipe(recipe._recipeName);
								validRecipes.Add(recipe);
							}

						}
						else
						{
							if (thisDCS.RecipeIndex.ContainsKey(recipe._recipeName))
							{
								bool recipeFound = false;
								recipeFound = thisDCS.RecipeIndex.TryGetValue(recipe._recipeName, out recipe._recipe);
								if (recipeFound)
								{
									recipe._compatibleRaces = recipe._recipe.compatibleRaces;
									validRecipes.Add(recipe);
								}

							}
						}
					}
				}
				else
				{
					Debug.LogWarning("There was no DynamicCharacterSystem set up in UMAContext");
				}
				return validRecipes;
			}
		}

		[Serializable]
		public class RaceAnimator
		{
			public string raceName;
			public string animatorControllerName;
			public RuntimeAnimatorController animatorController;
		}

		[Serializable]
		public class RaceAnimatorList
		{
			public RuntimeAnimatorController defaultAnimationController;
			public List<RaceAnimator> animators = new List<RaceAnimator>();
			public bool dynamicallyAddFromResources;
			public string resourcesFolderPath;
			public bool dynamicallyAddFromAssetBundles;
			public string assetBundleNames;
			public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();

			public List<string> Validate()
			{
				UpdateAnimators();
				int validAnimators = 0;
				List<string> validAnimatorsList = new List<string>();
				foreach (RaceAnimator animator in animators)
				{
					if (animator.animatorController != null)
					{
						validAnimators++;
						validAnimatorsList.Add(animator.animatorControllerName);
					}
				}
				return validAnimatorsList;
			}
			public void UpdateAnimators()
			{
				foreach (RaceAnimator animator in animators)
				{
					if (animator.animatorController == null)
					{
						if (animator.animatorControllerName != "")
						{
							FindAnimatorByName(animator.animatorControllerName);
						}
					}
				}
			}
			public void FindAnimatorByName(string animatorName)
			{
				/*bool found = false;
                if (dynamicallyAddFromResources)
                {
                    found = DynamicAssetLoader.Instance.AddAssetsFromResources<RuntimeAnimatorController>("", null, animatorName, SetFoundAnimators);
                }
                if(found == false && dynamicallyAddFromAssetBundles)
                {
                    DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<RuntimeAnimatorController>(ref assetBundlesUsedDict, false, "", null, animatorName, SetFoundAnimators);
                }*/
				DynamicAssetLoader.Instance.AddAssets<RuntimeAnimatorController>(ref assetBundlesUsedDict, true, true, false, "", "", null, animatorName, SetFoundAnimators);

			}
			//This function is probably redundant since animators from this class should never cause assetbundles to download
			//and therefore there should never be any 'temp' assets that need to be replaced
			public void SetAnimator(RuntimeAnimatorController controller)
			{
				foreach (RaceAnimator animator in animators)
				{
					if (animator.animatorControllerName != "")
					{
						if (animator.animatorControllerName == controller.name)
						{
							animator.animatorController = controller;
						}
					}
				}
			}
			private void SetFoundAnimators(RuntimeAnimatorController[] foundControllers)
			{
				foreach (RuntimeAnimatorController foundController in foundControllers)
				{
					foreach (RaceAnimator animator in animators)
					{
						if (animator.animatorController == null)
						{
							if (animator.animatorControllerName != "")
							{
								if (animator.animatorControllerName == foundController.name)
								{
									animator.animatorController = foundController;
								}
							}
						}
					}
				}
				CleanAnimatorsFromResourcesAndBundles();
			}
			public void CleanAnimatorsFromResourcesAndBundles()
			{
				Resources.UnloadUnusedAssets();
			}
		}
#endregion

		//ColorValue is now a child class of OverlayColorData with extra properties that return the values ColorValue previously had, I did it like this so we dont break backwards compatibility for people
		[Serializable]
		public class ColorValue : OverlayColorData, ISerializationCallbackReceiver
		{
			[FormerlySerializedAs("Name")]
			[SerializeField]
			private string _name;
			[FormerlySerializedAs("Color")]
			[SerializeField]
			private Color _color = Color.white;
			[FormerlySerializedAs("MetallicGloss")]
			[SerializeField]
			private Color _metallicGloss = new Color(0, 0, 0, 0);

			public bool valuesConverted = false;

			public string Name
			{
				get {
					if (_name != null)
						convertOldFieldsToNew();
                    return name; }
				set { name = value; }
			}

			public Color Color
			{
				get {
					if (_name != null)
						convertOldFieldsToNew();
					return color; }
				set { color = value; }
			}

			public Color MetallicGloss
			{
				get {
					if (_name != null)
						convertOldFieldsToNew();
					return channelAdditiveMask[2];
					}
				set {
					if (channelAdditiveMask.Length < 3)
						EnsureChannels(3);
					channelAdditiveMask[2] = value;
                    }
			}

			public ColorValue() {
				channelMask = new Color[3];
				channelAdditiveMask = new Color[3];
				for (int i = 0; i < 3; i++)
				{
					channelMask[i] = Color.white;
					channelAdditiveMask[i] = new Color(0, 0, 0, 0);
				}
			}

			public ColorValue(int channels) : base(channels)
			{
				//
			}

			public ColorValue(string nameVal, Color colorVal)
			{
				channelMask = new Color[3];
				channelAdditiveMask = new Color[3];
				for (int i = 0; i < 3; i++)
				{
					channelMask[i] = Color.white;
					channelAdditiveMask[i] = new Color(0, 0, 0, 0);
				}
				name = nameVal;
				color = colorVal;
			}
			public ColorValue(string nameVal, OverlayColorData color)
			{
				AssignFrom(color);
				name = nameVal;

			}
			public ColorValue(ColorValue col)
			{
				AssignFrom(col);
			}
			public ColorValue(OverlayColorData col)
			{
				AssignFrom(col);
			}
#region ISerializationCallbackReceiver Implimentation
			public void OnBeforeSerialize()
			{
				
			}
			public void OnAfterDeserialize()
			{
				convertOldFieldsToNew();
			}
#endregion
			/// <summary>
			/// This will be called to convert an old style ColorValue to a new style ColorValue based on whether name is null
			/// </summary>
			private void convertOldFieldsToNew()
			{
				if (!String.IsNullOrEmpty(_name))
				{
					name = _name;
					color = _color;
					if (channelAdditiveMask.Length < 3)
						EnsureChannels(3);
					channelAdditiveMask[2] = _metallicGloss;
					//marking _name as ull ensures this doesn't happen again. Color doesn't have a null value
					_name = null;
					valuesConverted = true;
				}
			}
		}


		//This is now a list of ColorValues which are themselves OvelayColorDatas. I made "Colors" 'FormerlySerializedAs' so anything people had previously set will automatcally convert
		[Serializable]
		[ExecuteInEditMode]
		public class ColorValueList
		{
			[FormerlySerializedAs("Colors")]
			public List<ColorValue> _colors = new List<ColorValue>();

			public List<ColorValue> Colors
			{
				get { return _colors; }
				set { _colors = value; }
			}

			public bool HasConvertedValues
			{
				get
				{
					foreach(ColorValue col in _colors)
					{
						if (col.valuesConverted)
							return true;
					}
					return false;
				}
				set
				{
					foreach (ColorValue col in _colors)
					{
						col.valuesConverted = false;
					}
				}
			}
#region CONSTRUCTOR

			/// <summary>
			/// The default Constructor adds a delegate to EditorApplication.update which checks if any of the ColorValues were updated from old values to new values and marks the scene as dirty
			/// </summary>
			public ColorValueList()
			{
#if UNITY_EDITOR
				EditorApplication.update += CheckValuesConverted;
#endif
			}

			public ColorValueList(OverlayColorData[] colors)
			{
				foreach (OverlayColorData ocd in colors)
				{
					SetColor(ocd.name, ocd);
				}
			}

			public ColorValueList(List<ColorValue> colorValueList)
			{
				Colors = colorValueList;
			}

#endregion

#if UNITY_EDITOR
			/// <summary>
			/// If any of the ColorValues were 'old' ColorValues (i.e. were not OverlayColorDatas) and were converted to 'new' ColorValues this method marks the scene as 'dirty' prompting the user to save
			/// </summary>
			private void CheckValuesConverted()
			{
				if (HasConvertedValues)
				{
					if (EditorSceneManager.GetActiveScene().IsValid())
					{
						if (!EditorSceneManager.GetActiveScene().isDirty)
						{
							Debug.LogWarning("Some of the characterColors in your DynamicCharacterAvatars were updated from the old format to the new format. Make sure you save the scene when you are done ;)");
						}
						EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
						HasConvertedValues = false;
						EditorApplication.update -= CheckValuesConverted;
					}
				}
			}
#endif


			private ColorValue GetColorValue(string name)
			{
				foreach (ColorValue cv in Colors)
				{
					if (cv.Name == name)
						return cv;
				}
				return null;
			}

			public OverlayColorData[] ToOverlayColors()
			{
				/*List<OverlayColorData> overlayColors = new List<OverlayColorData>();
				foreach (ColorValue c in Colors)
				{
					overlayColors.Add(ToOverlayColorData(c));
				}
				return overlayColors.ToArray();*/
				//not sure this will work so kept the above just in case
				return (OverlayColorData[])Colors.ToArray();
			}

			public OverlayColorData ToOverlayColorData(ColorValue cv)
			{

				/*OverlayColorData c = new OverlayColorData(3);
				c.name = cv.Name;
				c.color = cv.Color;
				c.channelAdditiveMask[2] = cv.MetallicGloss;*/
				//not sure this will work so kept the above just in case
				return (OverlayColorData)cv;
			}

			public bool GetColor(string Name, out Color c)
			{
				ColorValue cv = GetColorValue(Name);
				if (cv != null)
				{
					c = cv.Color;
					return true;
				}
				c = Color.white;
				return false;
			}

			public bool GetColor(string Name, out OverlayColorData c)
			{
				ColorValue cv = GetColorValue(Name);
				if (cv != null)
				{
					c = ToOverlayColorData(cv);
					return true;
				}
				c = new OverlayColorData(3);
				return false;
			}

			public void SetColor(string name, Color c)
			{
				ColorValue cv = GetColorValue(name);
				if (cv != null)
				{
					cv.Color = c;
				}
				else
				{
					Colors.Add(new ColorValue(name, c));
				}
			}

			public void SetColor(string name, OverlayColorData c)
			{
				ColorValue cv = GetColorValue(name);
				if (cv != null)
				{
					cv.Color = c.color;
					if (c.channelAdditiveMask.Length == 3)
					{
						cv.MetallicGloss = c.channelAdditiveMask[2];
					}
				}
				else
				{
					Colors.Add(new ColorValue(name, c));
				}
			}

			public void RemoveColor(string name)
			{
				foreach (ColorValue cv in Colors)
				{
					if (cv.Name == name)
						Colors.Remove(cv);
				}
			}
		}
	}

	/*
    /// <summary>
    /// Lightweight class for serializing/deserializing and network transmission.
    /// </summary>
    [Serializable]
    public class DynamicCharacterModel
    {
        private string race;
        private string name;
        private List<UMAPackedRecipeBase.UMAPackedDna> dna;
        private List<UMAPackedRecipeBase.PackedOverlayColorDataV3> colors;
       // public string[] Wardrobe;
		private List<WardrobeSetting> wardrobeSettings = new List<WardrobeSetting>();

        public DynamicCharacterModel(DynamicCharacterAvatar dca, bool includeDNA)
        {
            Colors = dca.characterColors;
            Race = dca.activeRace.name;
            Name = dca.name;
            if (includeDNA)
            {
                DNA = UMAPackedRecipeBase.GetPackedDNA(dca.umaData.umaRecipe);
            }
            else
            {
                // just generate an empty DNA
                DNA = new List<UMAPackedRecipeBase.UMAPackedDna>();
            }
            List<string> WardrobeSTR = new List<string>();
            foreach(UMATextRecipe utr in dca.WardrobeRecipes.Values)
            {
                WardrobeSTR.Add(utr.name);
            }
            Wardrobe = WardrobeSTR.ToArray();
			List<WardrobeSetting> wardrobeSettings = new List<WardrobeSetting>();
			foreach (UMATextRecipe utr in dca.WardrobeRecipes.Values)
			{
				wardrobeSettings.Add(new WardrobeSetting(utr));
			}
		}
		//UMATextRecipe.WardrobeSettings
		public class WardrobeSetting
		{
			public string slot;
			public string recipe;
			public WardrobeSetting()
			{

			}
			public WardrobeSetting(string _slot, string _recipe)
			{
				slot = _slot;
				recipe = _recipe;
			}
			public WardrobeSetting(UMATextRecipe utr)
			{
				slot = utr.wardrobeSlot;
				recipe = utr.name;
			}
		}
    }*/

	/// <summary>
	/// A DnaSetter is used to set a specific piece of DNA on the avatar
	/// that it is pulled from.
	/// </summary>
	public class DnaSetter
    {
        public string Name; // The name of the DNA.
        public float Value; // Current value of the DNA.

        protected int OwnerIndex;    // position of DNA in index, created at initialization
        protected UMADnaBase Owner;  // owning DNA class. Used to set the DNA by index

        /// <summary>
        /// Construct a DnaSetter
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="ownerIndex"></param>
        /// <param name="owner"></param>
        public DnaSetter(string name, float value, int ownerIndex, UMADnaBase owner)
        {
            Name = name;
            Value = value;
            OwnerIndex = ownerIndex;
            Owner = owner;
        }

        /// <summary>
        /// Set the current DNA value. You will need to rebuild the character to see 
        /// the results change.
        /// </summary>
        public void Set(float val)
        {
            Value = val;
            Owner.SetValue(OwnerIndex, val);
        }

        /// <summary>
        /// Set the current DNA value. You will need to rebuild the character to see 
        /// the results change.
        /// </summary>
        public void Set()
        {
            Owner.SetValue(OwnerIndex, Value);
        }

		/// <summary>
        /// Gets the current DNA value.
        /// </summary>
        public float Get()
        {
            return Owner.GetValue(OwnerIndex); 
        }
    }
}
