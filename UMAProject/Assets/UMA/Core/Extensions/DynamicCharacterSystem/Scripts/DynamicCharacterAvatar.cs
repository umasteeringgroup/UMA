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
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UMA.PoseTools;//so we can set the expression set based on the race

namespace UMA.CharacterSystem
{
	public class DynamicCharacterAvatar : UMAAvatarBase
	{
		#region Extra Events
		/// <summary>
		/// Callback event when the character recipe is updated. Use this to tweak the resulting recipe BEFORE the UMA is actually generated
		/// </summary>
		public UMADataEvent RecipeUpdated;

		#endregion

		#region ENUMS 
		[Flags]
		public enum ChangeRaceOptions
		{
			useDefaults = 0,
			none = 1,
			keepDNA = 2,
			keepWardrobe = 4,
			keepBodyColors = 8
		};

		[Flags]
		public enum LoadOptions
		{
			useDefaults = 0,
			loadRace = 1,
			loadDNA = 2,
			loadWardrobe = 4,
			loadBodyColors = 8,
			loadWardrobeColors = 16
		};

		[Flags]
		public enum SaveOptions
		{
			useDefaults = 0,
			saveDNA = 1,
			saveWardrobe = 2,
			saveColors = 4,
			saveAnimator = 8
		};

		public enum loadPathTypes { persistentDataPath, Resources, FileSystem, CharacterSystem, String };

		public enum savePathTypes { persistentDataPath, Resources, FileSystem };

		#endregion

		#region PUBLIC FIELDS

		//because the character might be loaded from an asset bundle, we may want everything required to create it to happen
		//but for it to still not be shown immediately or you may want to hide it anyway
		[Tooltip("If checked will turn off the SkinnedMeshRenderer after the character has been created to hide it. If not checked will turn it on again.")]
		public bool hide = false;

		[Tooltip("If true, then the meshcombiner will merge blendshapes found on slots that are part of this umaData")]
		public bool loadBlendShapes = false;

		//This will generate itself from a list available Races and set itself to the current value of activeRace.name
		[Tooltip("Selects the race to used. When initialized, the Avatar will use the base recipe from the RaceData selected.")]
		public RaceSetter activeRace = new RaceSetter();

		//To determine what recipes from a previous race can be applied to a the new race (on race change) sometimes we need to know what the previous race was
		//for example when a wardrobe collection is applied to a race, when the race changes we need to know if a given current slot was set because the wardrobe collection was compatible with the previous race.
		//if it was we know we need to look at that wardrobe collection and see if it has settings for the current race (rather than just saying the current slot is not compatible with the current race)
		private RaceData previousRace = null;

		[EnumFlags]
		public ChangeRaceOptions defaultChangeRaceOptions = ChangeRaceOptions.keepBodyColors;
		[Tooltip("When changing the race of the Avatar, cache the current state?")]
		public bool cacheCurrentState = true;
		[Tooltip("If true the existing skeleton is cleared and then rebuilt when the race is changed. Turn this off if you experience animation issues.")]
		public bool rebuildSkeleton = false;

		//the dictionary of active recipes this character is using to create itself
		private Dictionary<string, UMATextRecipe> _wardrobeRecipes = new Dictionary<string, UMATextRecipe>();
		//a list of active wardrobe collections on the avatar. If the collection has a wardrobe set for the active race these are loaded into _wardrobeRecipes
		private Dictionary<string, UMAWardrobeCollection> _wardrobeCollections = new Dictionary<string, UMAWardrobeCollection>();

		//a list of wardrobe recipes the avatar will fall back to if no other recipes are loaded in to 'WardrobeRecipes'
		[Tooltip("You can add wardrobe recipes for many races in here and only the ones that apply to the active race will be applied to the Avatar")]
		public WardrobeRecipeList preloadWardrobeRecipes = new WardrobeRecipeList();

		//a list of animation controllers that the avatar will use if it ends up being the race associated with that animation controller
		[Tooltip("Add animation controllers here for specific races. If no Controller is found for the active race, the Default Animation Controller is used")]
		public RaceAnimatorList raceAnimationControllers = new RaceAnimatorList();

		//a list of colors that the avatar will assign to matching 'shared colors' in the resulting recipe
		[Tooltip("Any colors here are set when the Avatar is first generated and updated as the values are changed using the color sliders")]
		public ColorValueList characterColors = new ColorValueList();

		//Load and Save fields
		//load
		public loadPathTypes loadPathType;
		public string loadPath;
		public string loadFilename;
		public string loadString;
		public bool loadFileOnStart;
		[Tooltip("If true the avatar will not build until all the assets it requires have been downloaded. Otherwise a placeholder avatar will be built while assets are downloading and be updated when everything is available.")]
		public bool waitForBundles = true;
		[EnumFlags]
		public LoadOptions defaultLoadOptions = LoadOptions.loadRace | LoadOptions.loadDNA | LoadOptions.loadWardrobe | LoadOptions.loadBodyColors | LoadOptions.loadWardrobeColors;

		//save
		public savePathTypes savePathType;
		public string savePath;
		public string saveFilename;
		[Tooltip("If true a GUID is generated and appended to the filename of the saved file")]
		public bool makeUniqueFilename = false;
		[Tooltip("If true ALL the colors in the 'characterColors' section of the component are added to the recipe on save. Otherwise only the colors used by the recipe are saved (UMA default)")]
		public bool ensureSharedColors = false;
		[EnumFlags]
		public SaveOptions defaultSaveOptions = SaveOptions.saveDNA | SaveOptions.saveWardrobe | SaveOptions.saveColors | SaveOptions.saveAnimator;
		//
		public Vector3 BoundsOffset;
		//
		[HideInInspector]
		[System.NonSerialized]
		public List<string> assetBundlesUsedbyCharacter = new List<string>();

#if UNITY_EDITOR
		[Tooltip("Show placeholder model or not.")]
		public bool showPlaceholder = true;
		public enum PreviewModel { Male, Female, Custom }
		[Tooltip("What model to show as placeholder.")]
		public PreviewModel previewModel;
		[Tooltip("Custom mesh to show as a placeholder.")]
		public GameObject customModel;
		[Tooltip("Custom rotation of the placeholder.")]
		public Vector3 customRotation;
		[Tooltip("What color to give the placeholder.")]
		public Color previewColor = Color.grey;
#endif
		#endregion

		#region PRIVATE FIELDS 
		//Is building the character enabled? Disable this to make multiple changes to the avatar that will be built
		//without creating multiple build calls. when you are finished set it to true and the character will build
		[SerializeField]
		[Tooltip("Builds the character on recipe load or race changed. If you want to load multiple recipes into a character you can disable this and enable it when you are done. By default this should be true.")]
		private bool _buildCharacterEnabled = true;
		//everytime an avatar changes race a cache state can (optionally) be created so that when the user 
		//switches between races they do not loose their previous changes to the avatar when it was set to be that race
		private Dictionary<string, string> cacheStates = new Dictionary<string, string>();
		//the wardrobe slots that are hidden by the avatars current wardrobe
		private List<string> HiddenSlots = new List<string>();//why was this HashSet list is faster for our purposes (http://stackoverflow.com/questions/150750/hashset-vs-list-performance)
															  //a list of downloading assets that the avatar can check the download status of.
		private List<string> requiredAssetsToCheck = new List<string>();
		//This is so we know if whether to use the 'default' settings as set in the componnt by colors/defaultRecipes/loadString/umaRecipe
		//or if an external script has already overridden these and so we should just build with the settings as they are (i.e. just call BuildCharacter)
		//Should be set to FALSE by any method that actually changes settings without calling ImportSettings
		private bool _isFirstSettingsBuild = true;

		//If BuildCharacter detects that wardrobe recipes applied to this character were only 'crossCompatible' (rather than being directly assigned as compatible with the current race)
		//this is set to true and will trigger FixCrossCompatibleSlots after the resulting recipe is compiled. 
		//This removes any 'equivalent slots' that may have been added by the cross compatible recipes.
		//This is reset at the beginning of every build operation
		private bool wasCrossCompatibleBuild = false;
		//Stores a list of the cross compatible races involved in this build so that the active race can find any 'equivalent' slots.
		//This is reset at the beginning of every build operation
		private List<string> crossCompatibleRaces = new List<string>();


#if UNITY_EDITOR
		private GameObject EditorUMAContext = null;

		private PreviewModel lastPreviewModel;
		private GameObject lastCustomModel;
		private Material mat;
		private Mesh previewMesh;
#endif
		#endregion

		#region PROPERTIES 
		//this previously get/set the base.umaRace value - but we dont want anyone to do that. because set wont actually change the race of the avatar 
		//and the value for get is only correct after the avatar has been built- not while we are generating the actual settings before we call 'Load'
		//If the want to set the Race when the avatar has built they should use ChangeRace. If they want to set it before they should use RacePreset
		//It could be used to return activeRace.raceData and/or could be made to call the right methods depending on whether the Avatar has been created
		/*RaceData RaceData
		{
			get { return base.umaRace; }
			set { base.umaRace = value; }
		}*/
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
		/// <summary>
		/// This returns all the recipes for the current race of the avatar.
		/// </summary>
		public Dictionary<string, List<UMATextRecipe>> AvailableRecipes
		{
			get
			{
				//Calling GetRace before calling .Recipes[activeRace.name] is required here for two reasons
				//a) if the race is in an assetBundle the race will be downloaded and a placeholder race used while it downloads
				//b) DCS will use that placeholder race to find the (backwards)compatible recipes for this race even before the raceData has finished downloading
				//when a race is added to the dictionary the recipes associated with it include the backwards compatible ones
				//when a new recipe is added from a download, when it is downloaded it gets added to the backwardsCompatible and compatible races it is for.
				(context.raceLibrary).GetRace(activeRace.name);
				return (context.dynamicCharacterSystem as DynamicCharacterSystem).Recipes[activeRace.name];
			}
		}
		//CurrentWardrobeSlots - if the race was in an asset bundle and has not finished downloading we will not have accurate data for this
		public List<string> CurrentWardrobeSlots
		{
			get
			{
				return activeRace.racedata.wardrobeSlots;
			}
		}
		//CurrentSharedColors may not be accurate if the character was in the middle of building
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
		//just a relay for the old name
		public Dictionary<string, UMATextRecipe> WardrobeRecipes
		{
			get
			{
				return _wardrobeRecipes;
			}
		}
		public Dictionary<string, UMAWardrobeCollection> WardrobeCollections
		{
			get
			{
				return _wardrobeCollections;
			}
		}
		/// <summary>
		/// Is building the character enabled? Set to FALSE to make multiple changes to the avatar that will only be built when this becomes TRUE again
		/// </summary>
		public bool BuildCharacterEnabled
		{
			get { return _buildCharacterEnabled; }
			set
			{
				if (_buildCharacterEnabled == false && value == true)
				{
					_buildCharacterEnabled = value;
					if (Application.isPlaying)
					{
						if (_isFirstSettingsBuild)
						{
							if (BuildUsingComponentSettings)
							{
								_isFirstSettingsBuild = false;
								StartCoroutine(BuildFromComponentSettingsCO());
							}
							else //we have an umaRecipe set or a text string set or a file defined to load
							{
								_isFirstSettingsBuild = false;
								BuildFromStartingFileOrRecipe();
							}
						}
						else
						{
							//otherwise the component settings have been set up via scripting
							//so just build
							SetAnimatorController(true);//may cause downloads to happen- So call BuildCharacterWhenReady() instead
							SetExpressionSet(true);
							StartCoroutine(BuildCharacterWhenReady());
						}
					}
				}
				else
					_buildCharacterEnabled = value;
			}
		}
		/// <summary>
		/// Does the avatar build using the race/wardrobe/colors in the component? False if any of loadString/loadFilename/umaRecipe have been set before Start
		/// </summary>
		bool BuildUsingComponentSettings
		{
			get
			{
				if (loadString == "" && loadFilename == "" && umaRecipe == null)
					return true;
				else
					return false;
			}
		}

		#endregion

		#region METHODS 

		#region Start Update and Inititalization

		public void Awake()
		{
#if UNITY_EDITOR
			EditorUMAContext = GameObject.Find("UMAEditorContext");
			if (EditorUMAContext != null)
			{
				EditorUMAContext.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
				EditorApplication.update -= CheckEditorContextNeeded;
				EditorApplication.update += CheckEditorContextNeeded;
			}
#endif
		}
		// Use this for initialization
		public override void Start()
		{
			AddCharacterStateCache("NULL");
			base.Start();

            umaData.blendShapeSettings.ignoreBlendShapes = !loadBlendShapes;

			//if the animator has been set the 'old' way respect that...
			if (raceAnimationControllers.defaultAnimationController == null && animationController != null)
			{
				raceAnimationControllers.defaultAnimationController = animationController;
			}
			//
			if (BuildCharacterEnabled == false)
				return;
			if (_isFirstSettingsBuild)
			{
				if (BuildUsingComponentSettings)
				{
					_isFirstSettingsBuild = false;
					StartCoroutine(BuildFromComponentSettingsCO());
				}
				else //we have an umaRecipe set or a text string set or a file defined to load
				{
					_isFirstSettingsBuild = false;
					BuildFromStartingFileOrRecipe();
				}
			}
		}

		void Update()
		{
			if (umaData != null)
			{
                umaData.blendShapeSettings.ignoreBlendShapes = !loadBlendShapes;

                if (umaData.rendererCount > 0)
                {
                    SkinnedMeshRenderer smr = umaData.GetRenderer(0);
                    if (smr != null && smr.enabled == hide)
                    {
                        umaData.GetRenderer(0).enabled = !hide;
                    }
                }
			}
			//This hardly ever happens now since the changeRace/LoadFromString/StartCO methods all yield themselves until asset bundles have been downloaded
			if (requiredAssetsToCheck.Count > 0 && !waitForBundles && BuildCharacterEnabled)
			{
				if (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck) == false)
				{
					Debug.Log("Update did build");
					UpdateAfterDownload();
					//actually we dont know in this case if we are restoring DNA or not
					//but a placeholder race should only have been used if defaultLoadOptions.waitForBundles is false
					//so we can atleast assume we dont want to restore the dna from that
					_isFirstSettingsBuild = false;
					BuildCharacter(waitForBundles);
				}
			}
		}

		void OnDisable()
		{
#if UNITY_EDITOR
			DestroyEditorUMAContext();
#endif
		}

		void OnDestroy()
		{
#if UNITY_EDITOR
			DestroyEditorUMAContext();
#endif
		}

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			// Build Shader
			if (!mat)
			{
				Shader shader = Shader.Find("Hidden/Internal-Colored");
				mat = new Material(shader);
				mat.hideFlags = HideFlags.HideAndDontSave;
			}

			if (showPlaceholder)
			{
				// Check for mesh Change
				if (!previewMesh || lastPreviewModel != previewModel || customModel != lastCustomModel)
					LoadMesh();

				mat.color = previewColor;
				if (!Application.isPlaying && previewMesh)
				{
					Quaternion rotation = Quaternion.Euler(-90, 180, 0);
					Vector3 scale = new Vector3(0.88f, 0.88f, 0.88f);
					if (previewModel == PreviewModel.Custom)
					{
						rotation = Quaternion.Euler(customRotation);
						scale = new Vector3(1, 1, 1);
					}
					mat.SetPass(0);
					Graphics.DrawMeshNow(previewMesh, Matrix4x4.TRS(transform.position, transform.rotation * rotation, scale));
				}
				lastPreviewModel = previewModel;
				lastCustomModel = customModel;
			}
		}

		void LoadMesh()
		{
			GameObject model = null;

			if (previewModel == PreviewModel.Custom)
				model = customModel;
			else
			{
				string modelPath = "HumanMale/FBX/Male_Unified.fbx";
				if (previewModel == PreviewModel.Female)
					modelPath = "HumanFemale/FBX/Female_Unified.fbx";
				model = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/UMA/Content/UMA_Core/" + modelPath, typeof(GameObject)) as GameObject;
			}
			if (model != null)
				previewMesh = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
		}
#endif

		IEnumerator BuildFromComponentSettingsCO()
		{
			//we cannot do anthing until this is DynamicAssetLoader.Instance.isInitialized, otherwise the dynamicLibraries will not find anything from AssetBundles
			//if you are not using assetbundles, this wait will be non-existant otherwise it will be until the AssetBundleIndex has downloaded
			while (!DynamicAssetLoader.Instance.isInitialized)
			{
				yield return null;
			}
			SetActiveRace();//This may cause a race to download. We can either bloat the AssetBundleIndex with virtually all the data in a RaceData OR we have to wait for it
			//if we are not waiting for bundles before building, build the placeholder avatar now
			if (!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
			{
				BuildCharacter(false);
			}
			bool needsUpdate = false;
			while (DynamicAssetLoader.Instance.downloadingAssetsContains(activeRace.name))
			{
				needsUpdate = true;
				yield return null;
			}
			if (needsUpdate)
			{
				activeRace.data = context.raceLibrary.GetRace(activeRace.name);
				umaRecipe = activeRace.data.baseRaceRecipe;
			}
			//If the UmaRecipe is still after that null, bail - we cant go any further (and SetStartingRace will have shown an error)
			if (umaRecipe == null)
			{
				yield break;
			}
			//If the user set up the component on Awake via scripting they may already have set the wardrobe so check that WardrobeRecipes is empty before doing LoadDefaultWardrobe
			if (WardrobeRecipes.Count == 0)
				LoadDefaultWardrobe(true);//this may cause downloads to happen
			SetExpressionSet();
			SetAnimatorController(true);
			yield return StartCoroutine(UpdateAfterDownloads());
			//Now we have everything, lets go!
			BuildCharacter(false);
		}

		void BuildFromStartingFileOrRecipe()
		{
			if ((loadFilename != "" && loadFileOnStart) || (loadPathType == loadPathTypes.String))
			{
				DoLoad();
			}
			else if (umaRecipe != null)
			{
				LoadFromRecipe(umaRecipe);
			}
		}

		#endregion

		#region SETTINGS MODIFICATION (RACE RELATED)

		/// <summary>
		/// Sets the starting race of the avatar based on the value of the 'activeRace'. 
		/// If it is in an assetbundle it will be downloaded and a placeholder race will be available while the racedata asset is downloading.
		/// </summary>
		void SetActiveRace()
		{
			if (activeRace.name == "" || activeRace.name == "None Set")
			{
				activeRace.data = null;
				Debug.LogWarning("No activeRace set. Aborting build");
				return;
			}
            //calling activeRace.data causes RaceLibrary to gather all racedatas from resources an returns all those along with any temporary assetbundle racedatas that are downloading
			//It will not cause any races to actually download
			//**Fix bug introduced in commit cf12f52 **//
			//ImportSettingsCO might have changed the activeRace.name so we may still need to change the actual racedata if activeRace.racedata.raceName is different
			if (activeRace.data != null && activeRace.name == activeRace.racedata.raceName)
			{
				activeRace.name = activeRace.racedata.raceName;
				umaRecipe = activeRace.racedata.baseRaceRecipe;
			}
			//otherwise...
			else if (activeRace.name != "")
			{
				//This only happens when the Avatar itself has an active race set to be one that is in an assetbundle
				activeRace.data = context.raceLibrary.GetRace(activeRace.name);// this will trigger a download if the race is in an asset bundle and return a temp asset
				if (activeRace.racedata != null)
				{
					umaRecipe = activeRace.racedata.baseRaceRecipe;
				}
			}
			//if we are loading an old UMARecipe from the recipe field and the old race is not in resources the race will be null but the recipe wont be 
			if (umaRecipe == null)
			{
				Debug.LogWarning("[SetActiveRace] could not find baseRaceRecipe for the race " + activeRace.name + ". Have you set one in the raceData?");
			}
			if (DynamicAssetLoader.Instance.downloadingAssetsContains(activeRace.name))
			{
				if (!requiredAssetsToCheck.Contains(activeRace.name))
					requiredAssetsToCheck.Add(activeRace.name);
			}
		}
		/// <summary>
		/// Change the race of the Avatar, optionally overriding the 'onChangeRace' settings in the avatar component itself
		/// </summary>
		/// <param name="racename">race to change to</param>
		/// <param name="customChangeRaceOptions">flags for the race change options</param>
		public void ChangeRace(string racename, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults)
		{
			RaceData thisRace = null;
			if (racename != "None Set")
				thisRace = (context.raceLibrary as DynamicRaceLibrary).GetRace(racename);
			ChangeRace(thisRace, customChangeRaceOptions);
		}

		/// <summary>
		/// Change the race of the Avatar, optionally overriding the 'onChangeRace' settings in the avatar component itself
		/// </summary>
		/// <param name="race"></param>
		/// <param name="customChangeRaceOptions">flags for the race change options</param>
		public void ChangeRace(RaceData race, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults)
		{
			bool actuallyChangeRace = false;
			if (race == null)
			{
				if (Application.isPlaying)
				{
					if (cacheCurrentState && BuildCharacterEnabled && activeRace.racedata != null)
					{
						AddCharacterStateCache();
					}
					//so do we need to make this actually destroy the Avatar? I guess we do...
					UnloadAvatar();
				}
				activeRace.data = null;
				activeRace.name = "";
				return;
			}
			if (activeRace.racedata == null)
				actuallyChangeRace = true;
			else if (activeRace.name != race.raceName)
				actuallyChangeRace = true;
			if (actuallyChangeRace)
				PerformRaceChange(race, customChangeRaceOptions);
		}

		private void PerformRaceChange(RaceData race, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults)
		{
            var thisChangeRaceOpts = customChangeRaceOptions == ChangeRaceOptions.useDefaults ? defaultChangeRaceOptions : customChangeRaceOptions;

			//we want to import settings with the set flags
			//if keepDNA then dont load dna from the racebase recipe == keep current dna
			//if keepWardrobe then dont load wardrobe from the racebaserecipe == keep current wardrobe
			//if keepBodyColors then dont load body colors from the racebaserecipe == keep current bodyColors
			LoadOptions thisLoadFlags = LoadOptions.loadDNA | LoadOptions.loadWardrobe | LoadOptions.loadWardrobeColors | LoadOptions.loadBodyColors;
			//we wont be able to keep anything if the race is currently null so dont change the flags in that case
			if (thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepBodyColors) && activeRace.racedata != null)
			{
				thisLoadFlags &= ~LoadOptions.loadBodyColors;//Dont load body colors - keep what we have
			}
			if (thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepDNA) && activeRace.racedata != null)
			{
				thisLoadFlags &= ~LoadOptions.loadDNA;//dont load dna keep what we have
			}
			if (thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepWardrobe) && activeRace.racedata != null)
			{
				thisLoadFlags &= ~LoadOptions.loadWardrobe;//dont load wardrobe- try to keep what we have
				thisLoadFlags &= ~LoadOptions.loadWardrobeColors;
			}
			if (Application.isPlaying)
			{
				//If BuildCharacterEnabled is false dont cache because the end user will have never made this version of the character
				if (cacheCurrentState && BuildCharacterEnabled && activeRace.racedata != null)
				{
					AddCharacterStateCache();
				}
				if (cacheCurrentState && cacheStates.ContainsKey(race.raceName))//we want to IMPORT these cached settings-cache states could now be DCS nodels
				{
					previousRace = activeRace.racedata;
					activeRace.name = race.raceName;
					activeRace.data = race;
					//call this here rather than letting ImportSettingsCO do it because the base recipe might have a different race to the race it's been assigned to!
					SetActiveRace();
					//if we are not going to try to keep the current wardrobe, clear it
					if (!thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepWardrobe))
					{
						_wardrobeRecipes.Clear();
					}
					LoadFromRecipeString(cacheStates[race.raceName], thisLoadFlags);
					return;
				}
			}
			previousRace = activeRace.racedata;
			activeRace.name = race.raceName;
			activeRace.data = race;
			if (Application.isPlaying)
			{
				//call this here rather than letting ImportSettingsCO do it because the base recipe might have a different race to the race it's been assigned to!
				SetActiveRace();
				//if there is no cached version and we are NOT keeping the current colors- we want to reset to the colors the component started with
				if (!thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepBodyColors))
				{
					//if keepBodyColors is FALSE we ALSO dont want to load them from the recipe- we want to load them from the null set
					thisLoadFlags &= ~LoadOptions.loadBodyColors;
					RestoreCachedBodyColors(false, true);
				}
				if (!thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepWardrobe))
				{
					//if keepWardrobe is FALSE we ALSO dont want to load colors from the recipe- we want to load them from the null set
					thisLoadFlags &= ~LoadOptions.loadWardrobeColors;
					RestoreCachedWardrobeColors(false, true);
				}
				//if we are not going to try to keep the current wardrobe, clear it
				if (!thisChangeRaceOpts.HasFlagSet(ChangeRaceOptions.keepWardrobe))
				{
					_wardrobeRecipes.Clear();
				}
				//by setting 'ForceDCSLoad' to true the loaded race will always be loaded like a new uma rather than the old uma way
				StartCoroutine(ImportSettingsCO(UMATextRecipe.PackedLoadDCS(context, (race.baseRaceRecipe as UMATextRecipe).recipeString), thisLoadFlags, true));
			}
		}

		#endregion

		#region SETTINGS MODIFICATION (WARDROBE RELATED)

		/// <summary>
		/// Loads the default wardobe items set in 'defaultWardrobeRecipes' in the CharacterAvatar itself onto the Avatar's base race recipe. Use this to make a naked avatar always have underwear or a set of clothes for example
		/// </summary>
		/// <param name="allowDownloadables">Optionally allow this function to trigger downloads of wardrobe recipes in an asset bundle</param>
		public void LoadDefaultWardrobe(bool allowDownloadables = false)
		{
			if (activeRace.name == "" || activeRace.name == "None Set")
				return;

			if (!preloadWardrobeRecipes.loadDefaultRecipes && preloadWardrobeRecipes.recipes.Count == 0)
				return;

			List<WardrobeRecipeListItem> validRecipes = preloadWardrobeRecipes.Validate(allowDownloadables, activeRace.name, activeRace.racedata);
			if (validRecipes.Count > 0)
			{
				foreach (WardrobeRecipeListItem recipe in validRecipes)
				{
					if (recipe._recipe != null)
					{
						if (((recipe._recipe.compatibleRaces.Count == 0 || recipe._recipe.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.IsCrossCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))))
						{
							//the check activeRace.data.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot) makes sure races that are cross compatible 
							//with another race but which dont have all of that races wardrobeslots, dont try to load things they dont have wardrobeslots for
							//However we need to make sure that if a slot has already been assigned that is DIRECTLY compatible with the race it is not overridden
							//by one that is cross compatible
							if (activeRace.racedata.IsCrossCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))
							{
								if (!WardrobeRecipes.ContainsKey(recipe._recipe.wardrobeSlot))
								{
									SetSlot(recipe._recipe);
									if (!requiredAssetsToCheck.Contains(recipe._recipeName) && DynamicAssetLoader.Instance.downloadingAssetsContains(recipe._recipeName))
									{
										requiredAssetsToCheck.Add(recipe._recipeName);
									}
								}
							}
							else
							{
								SetSlot(recipe._recipe);
								if (!requiredAssetsToCheck.Contains(recipe._recipeName) && DynamicAssetLoader.Instance.downloadingAssetsContains(recipe._recipeName))
								{
									requiredAssetsToCheck.Add(recipe._recipeName);
								}
							}
						}
					}
					else
					{
						Debug.LogWarning("[DynamicCharacterAvatar:LoadDefaultWardrobe] recipe._recipe was null for " + recipe._recipeName);
					}
				}
			}
		}

		public UMATextRecipe FindSlotRecipe(string Slotname, string Recipename)
		{
			//This line is here to ensure DCS downloads a recipe if it needs to do that.
			(context.dynamicCharacterSystem as DynamicCharacterSystem).GetRecipe(Recipename);

			var recipes = AvailableRecipes;

			if (recipes.ContainsKey(Slotname) != true) return null;

			List<UMATextRecipe> SlotRecipes = recipes[Slotname];

			for (int i = 0; i < SlotRecipes.Count; i++)
			{
				UMATextRecipe utr = SlotRecipes[i];
				if (utr.name == Recipename)
					return utr;
			}
			return null;
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
		/// <summary>
		/// Sets the avatars wardrobe slot to use the given wardrobe recipe (not to be mistaken with an UMA SlotDataAsset)
		/// </summary>
		/// <param name="utr"></param>
		public void SetSlot(UMATextRecipe utr)
		{
			var thisRecipeSlot = utr.wardrobeSlot;
			if (utr is UMAWardrobeCollection)
			{
				LoadWardrobeCollection((utr as UMAWardrobeCollection));
				return;
			}

			if (thisRecipeSlot != "" && thisRecipeSlot != "None")
			{
				if (_wardrobeRecipes.ContainsKey(thisRecipeSlot))
				{
					_wardrobeRecipes[thisRecipeSlot] = utr;
				}
				else
				{
					_wardrobeRecipes.Add(thisRecipeSlot, utr);
				}
				if (!requiredAssetsToCheck.Contains(utr.name) && DynamicAssetLoader.Instance.downloadingAssetsContains(utr.name))
				{
					requiredAssetsToCheck.Add(utr.name);
				}
			}
		}
		public void SetSlot(string Slotname, string Recipename)
		{
			UMATextRecipe utr = FindSlotRecipe(Slotname, Recipename);
			if (!utr)
			{
				//throw new Exception("Unable to find slot or recipe for Slotname "+ Slotname+" Recipename "+ Recipename);
				//it may just be that the race has changed and the current wardrobe didn't fit? If so we dont want to stop everything.
				Debug.LogWarning("Unable to find slot or recipe for Slotname " + Slotname + " Recipename " + Recipename);
			}
			else
			{
				SetSlot(utr);
			}
		}
		/// <summary>
		/// Clears the given wardrobe slot of any recipes that have been set on the Avatar
		/// </summary>
		/// <param name="ws"></param>
		public void ClearSlot(string ws)
		{
			if (_wardrobeRecipes.ContainsKey(ws))
			{
				_wardrobeRecipes.Remove(ws);
			}
		}

		/// <summary>
		/// Clears the listed wardrobe slots of any wardrobeRecipes that have been set on the avatar
		/// </summary>
		public void ClearSlots(List<string> slotsToClear)
		{
			foreach (string slot in slotsToClear)
				ClearSlot(slot);
		}
		/// <summary>
		/// Clears all the wardrobe slots of any wardrobeRecipes that have been set on the avatar
		/// </summary>
		public void ClearSlots()
		{
			WardrobeRecipes.Clear();
		}

		/// <summary>
		/// Adds the given wardrobe collection to the avatars WardrobeCollection list and loads its recipes (if it has a wardrobe set for the current race)
		/// </summary>
		public void LoadWardrobeCollection(string collectionName)
		{
			UMATextRecipe utr = FindSlotRecipe("WardrobeCollection", collectionName);
			if (!utr || !(utr is UMAWardrobeCollection))
			{
				//Dont show a warning. When editing the avatar wardrobe collections stay in the list until the avatar is saved (or RemoveUnusedCollections is called)
				//so that switching back to the race that does use the collection causes it to load again
				//Debug.LogWarning("Unable to find a WardrobeCollection for collectionName " + collectionName);
			}
			else
			{
				LoadWardrobeCollection((utr as UMAWardrobeCollection));
			}
		}

		public void LoadWardrobeCollection(UMAWardrobeCollection uwr)
		{
			if (!DynamicAssetLoader.Instance.downloadingAssetsContains(uwr.name))
			{
				//if this WardrobeCollection was added when it was downloading, checdk it actually has sets, otherwise it shouldn't be here- TODO check if this ever happens
				if (uwr.wardrobeCollection.sets.Count == 0)
				{
					if (_wardrobeCollections.ContainsValue(uwr))
					{
						_wardrobeCollections.Remove(uwr.wardrobeSlot);
					}
					return;
				}
				//If there is already a WardrobeCollection belonging to this group applied to the Avatar, unload and remove it
				if (_wardrobeCollections.ContainsKey(uwr.wardrobeSlot))
				{
					UnloadWardrobeCollectionGroup(uwr.wardrobeSlot);
				}
				_wardrobeCollections.Add(uwr.wardrobeSlot, uwr);
				var thisSettings = uwr.GetUniversalPackRecipe(this, context);
				//if there is a wardrobe set for this race treat this like a 'FullOutfit'
				if (thisSettings.wardrobeSet.Count > 0)
				{
					LoadWardrobeSet(thisSettings.wardrobeSet, false);
					if (thisSettings.sharedColorCount > 0)
					{
						ImportSharedColors(thisSettings.sharedColors, LoadOptions.loadWardrobeColors);
					}
				}
			}
			else //make sure the downloading collection is still added to _wardrobeCollections, otherwise it wont be processed in UpdateAfterDownload -> UpdateSetSlots
			{
				_wardrobeCollections.Add(uwr.wardrobeSlot, uwr);
			}
		}

		/// <summary>
		/// Call this when slots are removed from WardrobeRecipes so that any slots that are left empty by the change have any wardrobe collection recipes re-applied
		/// </summary>
		public void ReapplyWardrobeCollections()
		{
			if (_wardrobeCollections.Count == 0)
				return;

			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
			{
				var wardrobeSet = kp.Value.GetRacesWardrobeSet(activeRace.racedata);
				if (wardrobeSet.Count > 0)
				{
					foreach (WardrobeSettings ws in wardrobeSet)
					{
						if(!_wardrobeRecipes.ContainsKey(ws.slot) && !string.IsNullOrEmpty(ws.recipe))
							SetSlot(ws.slot, ws.recipe);
					}
				}
			}
		}

		/// <summary>
		/// Checks the Avatars Wardrobe Collection List for the given collection name returning it if it exists
		/// </summary>
		public UMAWardrobeCollection GetWardrobeCollection(string collectionName)
		{
			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
			{
				if (kp.Value.name == collectionName)
				{
					return kp.Value;
				}
			}
			return null;
		}

		/// <summary>
		/// Returns true if the given collection is adding any recipes to the Avatars WardrobeRecipes (optionally only returning true if ALL the collections recipes are being applied
		/// </summary>
		public bool IsCollectionApplied(string collectionName, bool fullyApplied = false)
		{
			if (!GetWardrobeCollection(collectionName))
				return false;
			var collectionSet = GetWardrobeCollection(collectionName).GetRacesWardrobeSet(activeRace.racedata);
			if (collectionSet.Count == 0)
				return false;
			foreach(WardrobeSettings ws in collectionSet)
			{
				bool wasApplied = false;
				foreach(UMATextRecipe utr in _wardrobeRecipes.Values)
				{
					if(ws.recipe == utr.name)
					{
						if (!fullyApplied)
							return true;
						wasApplied = true;
					}
				}
				if (!wasApplied && fullyApplied)
					return false;
			}
			return fullyApplied;
		}

		/// <summary>
		/// Checks whether the Avatars Wardrobe Collection List has an entry for the given collection group returning the collection assigned to the group if it exists
		/// </summary>
		public UMAWardrobeCollection GetWardrobeCollectionGroup(string groupToCheck)
		{
			if (_wardrobeCollections.ContainsKey(groupToCheck))
			{
				return _wardrobeCollections[groupToCheck];
			}
			return null;
		}

		/// <summary>
		/// Removes the wardrobe collection of the given name from the avatars WardrobeCollection list and clears any recipes it loaded into the avatars wardrobe slots
		/// </summary>
		public void UnloadWardrobeCollection(string collectionToUnload)
		{
			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
			{
				if (kp.Value.name == collectionToUnload)
				{
					var wardrobeSet = kp.Value.GetRacesWardrobeSet(activeRace.racedata);
					if (wardrobeSet.Count > 0)
					{
						for (int si = 0; si < wardrobeSet.Count; si++)
						{
							if (_wardrobeRecipes.ContainsKey(wardrobeSet[si].slot))
							{
								if (_wardrobeRecipes[wardrobeSet[si].slot].name == wardrobeSet[si].recipe)
									ClearSlot(wardrobeSet[si].slot);
							}
						}
					}
					_wardrobeCollections.Remove(kp.Value.wardrobeSlot);
					break;
				}
			}
		}

		/// <summary>
		/// Removes any wardrobe collections that are not being used by the Avatar in its current state
		/// </summary>
		public void RemoveUnusedCollections()
		{
			List<string> collectionsToRemove= new List<string>();
			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
			{
				if (!IsCollectionApplied(kp.Value.name))
					collectionsToRemove.Add(kp.Key);
			}
			foreach (string c in collectionsToRemove)
				_wardrobeCollections.Remove(c);
		}

		/// <summary>
		/// Unloads all the recipes applied by all the collections and removes the collections
		/// </summary>
		public void UnloadAllWardrobeCollections()
		{
			List<string> collectionsToUnload = new List<string>();
			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
			{
				collectionsToUnload.Add(kp.Value.name);
			}
			foreach (string c in collectionsToUnload)
				UnloadWardrobeCollection(c);
        }

		/// <summary>
		/// Removes the wardrobe collection of the given group from the avatars WardrobeCollection list and clears any recipes it loaded into the avatars wardrobe slots
		/// </summary>
		public void UnloadWardrobeCollectionGroup(string collectionGroupToUnload)
		{
			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
			{
				if (kp.Key == collectionGroupToUnload)
				{
					UnloadWardrobeCollection(kp.Value.name);
					break;
				}
			}
		}
		/// <summary>
		/// Searches the current wardrobeRecipes for recipes that are compatible or backwards compatible with this race. 
		/// If nothing is found the fallback set is applied or if and preloadWardrobeRecipes.loadDefaultRecipes is true, default recipes are applied
		/// </summary>
		void ApplyCurrentWardrobeToNewRace(List<WardrobeSettings> fallbackSet = null)
		{
            var newWardrobeRecipes = new Dictionary<string, UMATextRecipe>();
			List<WardrobeRecipeListItem> validDefaultRecipes = preloadWardrobeRecipes.Validate(true, activeRace.name, activeRace.racedata);
			fallbackSet = fallbackSet ?? new List<WardrobeSettings>();
			//to get the recipes from the fallbackSet we need DCS
			var thisContext = UMAContext.FindInstance();
			if (thisContext == null)
			{
#if UNITY_EDITOR
				thisContext = CreateEditorContext();
#else
				return;
#endif
			}
			var thisDCS = thisContext.dynamicCharacterSystem as DynamicCharacterSystem;
			Dictionary<string, UMATextRecipe> wrBU = new Dictionary<string, UMATextRecipe>(_wardrobeRecipes);
			ClearWardrobeCollectionsRecipes();
			ClearSlots();
			if (previousRace != null && _wardrobeCollections.Count > 0 && wrBU.Count > 0)
			{
				//loop over each wardrobe collection and check if its recipies were applied to the previous race
				foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
				{
					var pWardrobeSet = kp.Value.GetRacesWardrobeSet(previousRace);
					var aWardrobeSet = kp.Value.GetRacesWardrobeSet(activeRace.racedata);
					if (pWardrobeSet.Count > 0 && aWardrobeSet.Count > 0)
					{
						if (wrBU.Count > 0)
						{
							foreach (KeyValuePair<string, UMATextRecipe> wkp in wrBU)
							{
								//was the recipe set by the wardrobeCollection for the previous race
								bool collectionWasApplied = false;
								for(int pcri = 0; pcri < pWardrobeSet.Count; pcri++)
								{
									if(pWardrobeSet[pcri].slot == wkp.Key && pWardrobeSet[pcri].recipe == wkp.Value.name)
									{
										//we know this wardrobe collection had settings for the previous race and they were applied
										//so if aWardrobeSet count is not 0 then we know that this wardrobe collection ALSO has settings for this race
										//so apply them
										collectionWasApplied = true;
									}
								}
								if (collectionWasApplied)
								{
									//apply all the recipes in this aWardrobeSet to the new race
									for (int acri = 0; acri < aWardrobeSet.Count; acri++)
									{
										if (!newWardrobeRecipes.ContainsKey(aWardrobeSet[acri].slot))
										{
											var thisWCRecipe = thisDCS.GetRecipe(aWardrobeSet[acri].recipe);
											newWardrobeRecipes.Add(aWardrobeSet[acri].slot, thisWCRecipe);
											//do the downloading check
											if (!requiredAssetsToCheck.Contains(thisWCRecipe.name) && DynamicAssetLoader.Instance.downloadingAssetsContains(thisWCRecipe.name))
											{
												requiredAssetsToCheck.Add(thisWCRecipe.name);
											}
										}
									}
								}
							}
						}
					}
					else if(pWardrobeSet.Count > 0 && aWardrobeSet.Count == 0)
					{
						//remove each recipe in pWardrobeSet from wrBU so they dont get re added below
						foreach (WardrobeSettings pws in pWardrobeSet)
						{
							if (wrBU.ContainsKey(pws.slot))
							{
								if (wrBU[pws.slot].name == pws.recipe)
									wrBU.Remove(pws.slot);
							}
						}
					}
				}
			}

			//if we have wardrobe recipes
			//go through each one and see if its compatible- if it is add it to the new list
			//if its not and loadDefaultRecipes is true, load the default recipe for that slot
			if (wrBU.Count > 0)
			{
				//I think this needs to apply any wardrobe collections FIRST because the slots they set will be been set but may have been overridden- we need to replicate that flow
				foreach (KeyValuePair<string, UMATextRecipe> kp in wrBU)
				{
					if (kp.Value.compatibleRaces.Contains(activeRace.name) || activeRace.racedata.IsCrossCompatibleWith(kp.Value.compatibleRaces))
					{
						if (!newWardrobeRecipes.ContainsKey(kp.Key))
						{
							newWardrobeRecipes.Add(kp.Key, kp.Value);
						}
					}
				}
			}
			//if the fallback set is not empty make sure its recipes are applied if any slots it has are still empty
			if(fallbackSet.Count > 0)
			{
				for (int i = 0; i < fallbackSet.Count; i++)
				{
					if (!newWardrobeRecipes.ContainsKey(fallbackSet[i].slot))
					{
						var fbRecipe = thisDCS.GetRecipe(fallbackSet[i].recipe);
						newWardrobeRecipes.Add(fallbackSet[i].slot, fbRecipe);
						if (!requiredAssetsToCheck.Contains(fbRecipe.name) && DynamicAssetLoader.Instance.downloadingAssetsContains(fbRecipe.name))
						{
							requiredAssetsToCheck.Add(fbRecipe.name);
						}
					}
				}
			}
			//if loadDefaultRecipes is true make sure the new list has an entry for anything in the default recipes list for that slot
			//if it doesn't add the default recipe
			if (preloadWardrobeRecipes.loadDefaultRecipes)
			{
				for (int i = 0; i < validDefaultRecipes.Count; i++)
				{
					if (!newWardrobeRecipes.ContainsKey(validDefaultRecipes[i]._recipe.wardrobeSlot))
					{
						newWardrobeRecipes.Add(validDefaultRecipes[i]._recipe.wardrobeSlot, validDefaultRecipes[i]._recipe);
						//if the requested recipe ended up being downloaded add it to the requiredAssetsToCheck- this is what we use when waiting for bundles to check if we have everything we need
						if (!requiredAssetsToCheck.Contains(validDefaultRecipes[i]._recipe.name) && DynamicAssetLoader.Instance.downloadingAssetsContains(validDefaultRecipes[i]._recipe.name))
						{
							requiredAssetsToCheck.Add(validDefaultRecipes[i]._recipe.name);
						}
					}
				}
			}
			//then set to WardrobeRecipes
			_wardrobeRecipes = newWardrobeRecipes;
			//now load any wardrobeCollections slots if they are not already taken
			ReapplyWardrobeCollections();
		}

		/// <summary>
		/// Load a wardrobe set as defined in a UMATextRecipe.DCSPackRecipe model.
		/// </summary>
		/// <param name="wardrobeSet"></param>
		public void LoadWardrobeSet(List<WardrobeSettings> wardrobeSet, bool clearExisting = false)
		{
			_isFirstSettingsBuild = false;
			if (clearExisting || wardrobeSet.Count == 0)
				_wardrobeRecipes.Clear();
			if (wardrobeSet.Count > 0)
			{
				//we have to do WardrobeCollections first because they may only be partially applied
				foreach (WardrobeSettings ws in wardrobeSet)
				{
					if (ws.slot == "WardrobeCollection")
					{
						if (string.IsNullOrEmpty(ws.recipe))
						{
							continue;
						}
						LoadWardrobeCollection(ws.recipe);
					}
				}
				foreach (WardrobeSettings ws in wardrobeSet)
				{
					if (ws.slot != "WardrobeCollection")
					{
						if (!string.IsNullOrEmpty(ws.recipe))
							SetSlot(ws.slot, ws.recipe);
						else
							ClearSlot(ws.slot);
					}
				}

			}
		}
		/// <summary>
		/// Clears any recipes that were added by WardrobeCollections so that only the wardrobe collections themselves are added to the saved wardrobe set
		/// </summary>
		/// <param name="removeUnappliedCollections">If true removes any collections that are not actually applying anything</param>
		private void ClearWardrobeCollectionsRecipes(bool removeUnappliedCollections = false)
		{
			List<string> groupsToClear = new List<string>();
			if (_wardrobeCollections.Count > 0)
			{
				foreach (UMAWardrobeCollection uwr in _wardrobeCollections.Values)
				{
					//dont do anything to collections that are downloading
					if (DynamicAssetLoader.Instance.downloadingAssetsContains(uwr.name))
						continue;
					var collectionSet = uwr.GetRacesWardrobeSet(activeRace.racedata);
					if (collectionSet.Count > 0)
					{
						bool wasApplied = false;
						foreach (WardrobeSettings ws in collectionSet)
						{
							if (_wardrobeRecipes.ContainsKey(ws.slot))
							{
								if (_wardrobeRecipes[ws.slot].name == ws.recipe)
								{
									ClearSlot(ws.slot);
									wasApplied = true;
								}
							}
						}
						//we need to remove any wardrobe collections that are not actually applying anything
						if (!wasApplied)
						{
							groupsToClear.Add(uwr.wardrobeSlot);
						}
					}
				}
			}
			if(removeUnappliedCollections && groupsToClear.Count > 0)
			{
				for(int i = 0; i < groupsToClear.Count; i++)
				{
					_wardrobeCollections.Remove(groupsToClear[i]);
				}
			}
		}

		#endregion

		#region SETTINGS MODIFICATION (COLORS RELATED)

		/// <summary>
		/// Gets the color from the current characterColors.
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
		/// Sets a color given the base parameters.
		/// Does not update the color if you don't pass the "updateTexture = true" -- Call UpdateColors() when you are done updating the colors table
		/// </summary>
		/// <param name="SharedColorName"></param>
		/// <param name="AlbedoColor"></param>
		/// <param name="MetallicRGB"></param>
		/// <param name="Gloss"></param>
		/// <param name="UpdateTexture"></param>
		public void SetColor(string SharedColorName, Color AlbedoColor, Color MetallicRGB = new Color(), float Gloss = 0.0f, bool UpdateTexture = false)
		{
			OverlayColorData ocd = new OverlayColorData(3);
			MetallicRGB.a = Gloss;
			ocd.channelMask[0] = AlbedoColor;
			ocd.channelAdditiveMask[2] = MetallicRGB;
			SetColor(SharedColorName, ocd, UpdateTexture);
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
		//NOTE needs to be public for the editor
		public void UpdateColors(bool triggerDirty = false)
		{
			if (umaData.umaRecipe.sharedColors == null)
			return;

			foreach (UMA.OverlayColorData ucd in umaData.umaRecipe.sharedColors)
			{
				if (ucd.HasName())
				{
					OverlayColorData c;
					if (characterColors.GetColor(ucd.name, out c))
					{
						ucd.color = c.color;
						if (ucd.channelAdditiveMask.Length >= 3 && c.channelAdditiveMask.Length >= 3)
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

		private OverlayColorData[] ImportSharedColors(OverlayColorData[] colorsToLoad, LoadOptions thisLoadOptions)
		{
			List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
			if (thisLoadOptions.HasFlagSet(LoadOptions.loadBodyColors) && thisLoadOptions.HasFlagSet(LoadOptions.loadWardrobeColors) && colorsToLoad.Length > 0)
			{
				characterColors.Colors.Clear();
			}
			if (thisLoadOptions.HasFlagSet(LoadOptions.loadBodyColors) && colorsToLoad.Length > 0)
			{
				newSharedColors.AddRange(LoadBodyColors(colorsToLoad, false));
			}
			if (thisLoadOptions.HasFlagSet(LoadOptions.loadWardrobeColors) && colorsToLoad.Length > 0)
			{
				newSharedColors.AddRange(LoadWardrobeColors(colorsToLoad, false));
			}
			//if we were not loading both things then we want to restore any colors that were set in the Avatar settings if the characterColors does not already contain a color for that name
			if (!thisLoadOptions.HasFlagSet(LoadOptions.loadBodyColors) || !thisLoadOptions.HasFlagSet(LoadOptions.loadWardrobeColors) || colorsToLoad.Length == 0)
			{
				if (!thisLoadOptions.HasFlagSet(LoadOptions.loadBodyColors) || colorsToLoad.Length == 0)
				{
					newSharedColors.AddRange(RestoreCachedBodyColors(false));
				}
				if (!thisLoadOptions.HasFlagSet(LoadOptions.loadWardrobeColors) || colorsToLoad.Length == 0)
				{
					newSharedColors.AddRange(RestoreCachedWardrobeColors(false));
				}
			}
			return newSharedColors.ToArray();
		}

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
		public List<OverlayColorData> LoadBodyColors(OverlayColorData[] colorsToLoad, bool apply = false)
		{
			return LoadBodyOrWardrobeColors(colorsToLoad, true, apply);
		}
		/// <summary>
		/// Loads any shared colors from the given recipe to the CharacterColors List, only if they are NOT defined in the current baseRaceRecipe, optionally applying then to the current UMAData.UMARecipe
		/// </summary>
		/// <param name="recipeToLoad"></param>
		/// <param name="apply"></param>
		/// <returns></returns>
		public List<OverlayColorData> LoadWardrobeColors(OverlayColorData[] colorsToLoad, bool apply = false)
		{
			return LoadBodyOrWardrobeColors(colorsToLoad, false, apply);
		}

		private List<OverlayColorData> LoadBodyOrWardrobeColors(OverlayColorData[] colorsToLoad, bool loadingBody = true, bool apply = false)
		{
			List<string> bodyColorNames = GetBodyColorNames();
			List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
			if (loadingBody)
				foreach (OverlayColorData col in colorsToLoad)
				{
					if (bodyColorNames.Contains(col.name))
					{
						SetColor(col.name, col, false);
						if (!newSharedColors.Contains(col))
							newSharedColors.Add(col);
					}
				}
			else if (!loadingBody)
				foreach (OverlayColorData col in colorsToLoad)
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
		public List<OverlayColorData> RestoreCachedBodyColors(bool apply = false, bool fullRestore = false)
		{
			return RestoreCachedBodyOrWardrobeColors(true, apply, fullRestore);
		}
		/// <summary>
		///  Restores the wardrobe colors to the ones defined in the component on start, optionally applying these to the UMAData.UMARecipe
		/// </summary>
		/// <param name="apply"></param>
		/// <returns></returns>
		public List<OverlayColorData> RestoreCachedWardrobeColors(bool apply = false, bool fullRestore = false)
		{
			return RestoreCachedBodyOrWardrobeColors(false, apply, fullRestore);
		}

		private List<OverlayColorData> RestoreCachedBodyOrWardrobeColors(bool restoringBody = true, bool apply = false, bool fullRestore = false)
		{
			List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
			if (!cacheStates.ContainsKey("NULL"))
				return newSharedColors;
			else
			{
				var thisCacheData = JsonUtility.FromJson<UMATextRecipe.DCSPackRecipe>(cacheStates["NULL"]);
				List<string> bodyColorNames = GetBodyColorNames();
				if (restoringBody)
				{
					foreach (OverlayColorData col in thisCacheData.sharedColors)
					{
						if (bodyColorNames.Contains(col.name))
						{
							if (!GetColor(col.name) || fullRestore)
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
					foreach (OverlayColorData col in thisCacheData.sharedColors)
					{
						if (!bodyColorNames.Contains(col.name))
						{
							if (!GetColor(col.name) || fullRestore)
							{
								SetColor(col.name, col, false);
								if (!newSharedColors.Contains(col))
									newSharedColors.Add(col);
							}
						}
					}
				}
			}
			if (apply)
				umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
			return newSharedColors;
		}

		#endregion

		#region SETTINGS MODIFICATION (DNA RELATED)

		private void TryImportDNAValues(UMADnaBase[] prevDna)
		{
			//dont bother if there is nothing to do...
			if (prevDna.Length == 0)
				return;
			//umaData.umaRecipe.ApplyDNA(umaData, true);Don't think we need to apply- if we dont we can do the dna reverting when BuildCharacterEnabled is false
			var activeDNA = umaData.umaRecipe.GetAllDna();
			for (int i = 0; i < activeDNA.Length; i++)
			{
				if (activeDNA[i] is DynamicUMADnaBase)
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
		/// Get all of the DNA for the current character, and return it as a list of DnaSetters.
		/// Each DnaSetter will track the DNABase that it came from, and the character that it is attached
		/// to. To modify the DNA on the character, use the Set function on the Setter.
		/// </summary>
		/// <returns></returns>
		public Dictionary<string, DnaSetter> GetDNA()
		{
			Dictionary<string, DnaSetter> dna = new Dictionary<string, DnaSetter>();

			foreach (UMADnaBase db in umaData.GetAllDna())
			{
				string Category = db.GetType().ToString();

				DnaConverterBehaviour dcb = activeRace.racedata.GetConverter(db);
				if (dcb != null && (!string.IsNullOrEmpty(dcb.DisplayValue)))
				{
					Category = dcb.DisplayValue;
				}

				for (int i = 0; i < db.Count; i++)
				{
					if (dna.ContainsKey(db.Names[i]))
					{
						dna[db.Names[i]] = new DnaSetter(db.Names[i], db.Values[i], i, db, Category);
					}
					else
					{
						dna.Add(db.Names[i], new DnaSetter(db.Names[i], db.Values[i], i, db, Category));
					}
				}
			}
			return dna;
		}

		public UMADnaBase[] GetAllDNA()
		{
			return umaData.GetAllDna();
		}

		#endregion

		#region SETTINGS MODIFICATION (ANIMATION RELATED)

		/// <summary>
		/// Sets the Expression set for the Avatar based on the Avatars set race.
		/// </summary>
		/// 
		public void SetExpressionSet(bool addExressionPlayer = false)
		{
			var thisExpressionPlayer = gameObject.GetComponent<UMAExpressionPlayer>();
			if (thisExpressionPlayer == null && addExressionPlayer)
				thisExpressionPlayer = gameObject.AddComponent<UMAExpressionPlayer>();

			if (thisExpressionPlayer != null)
			{
				UMAExpressionSet expressionSetToUse = null;
				if (activeRace.racedata != null)
				{
					expressionSetToUse = activeRace.racedata.expressionSet;//this wont be in the placeholder race
				}
				if (expressionSetToUse != null)
				{
					//set the expression set and reset all the values
					thisExpressionPlayer.expressionSet = expressionSetToUse;
					thisExpressionPlayer.Values = new float[thisExpressionPlayer.Values.Length];
				}
			}
		}
		private void InitializeExpressionPlayer(UMAData umaData)
		{
			this.CharacterUpdated.RemoveListener(InitializeExpressionPlayer);
			InitializeExpressionPlayer();
		}

		private void InitializeExpressionPlayer()
		{
			var thisExpressionPlayer = gameObject.GetComponent<UMAExpressionPlayer>();
			if (thisExpressionPlayer == null)
				return;
			if (thisExpressionPlayer.expressionSet == null)
				return;
			thisExpressionPlayer.enabled = true;
			thisExpressionPlayer.Initialize();
		}

		/// <summary>
		/// Sets the Animator Controller for the Avatar based on the best match found for the Avatars race. If no animator for the active race has explicitly been set, the default animator is used
		/// </summary>
		public void SetAnimatorController(bool addAnimator = false)
		{
			RuntimeAnimatorController controllerToUse = raceAnimationControllers.GetAnimatorForRace(activeRace.name);

			//changing the animationController in 5.6 resets the rotation of this game object
			//so store the rotation and set it back
			var originalRot = Quaternion.identity;
			if (umaData != null)
				originalRot = umaData.transform.localRotation;

			animationController = controllerToUse;
			var thisAnimator = gameObject.GetComponent<Animator>();
			if (controllerToUse != null)
			{
				if (thisAnimator == null && addAnimator)
					thisAnimator = gameObject.AddComponent<Animator>();
				if (thisAnimator != null)
				{
					thisAnimator.runtimeAnimatorController = controllerToUse;
					if (!requiredAssetsToCheck.Contains(controllerToUse.name) && DynamicAssetLoader.Instance.downloadingAssetsContains(controllerToUse.name))
					{
						requiredAssetsToCheck.Add(controllerToUse.name);
					}
				}
			}
			else
			{
				if (thisAnimator != null)
				{
					thisAnimator.runtimeAnimatorController = null;
				}
			}
			if (umaData != null)
				umaData.transform.localRotation = originalRot;
		}

		#endregion

		#region SETTINGS EXPORT (SAVE)

		/// <summary>
		/// Helper method for getting the required DCA.SaveOptions flags. Set all to false for DCA.SaveOptions.UseDefaults
		/// </summary>
		public static SaveOptions GetSaveOptionsFlags(bool saveDNA, bool saveWardrobe, bool saveColors/*, bool saveAnimator*/)//not using saveAnimator yet
		{
			if (saveDNA && !saveWardrobe && !saveColors /*&& !saveAnimator*/)
			{
				SaveOptions thisDCASA = SaveOptions.useDefaults;
				return thisDCASA;
			}
			else
			{
				SaveOptions thisDCASA = SaveOptions.useDefaults;
				if (saveDNA)
					thisDCASA |= SaveOptions.saveDNA;
				if (saveWardrobe)
					thisDCASA |= SaveOptions.saveWardrobe;
				if (saveColors)
					thisDCASA |= SaveOptions.saveColors;
				/*if (saveAnimator)
					thisDCASA|= SaveOptions.saveAnimator;*/

				thisDCASA &= ~SaveOptions.useDefaults;

				return thisDCASA;
			}
		}

		#region PARTIAL EXPORT - HelperMethods

		public string GetCurrentWardrobeRecipe(string recipeName = "", bool includeColors = false, params string[] slotsToSave)
		{
			SaveOptions thisSaveOpts = SaveOptions.saveWardrobe;
			if (includeColors)
				thisSaveOpts |= SaveOptions.saveColors;
			return DoPartialSave(recipeName, thisSaveOpts);
		}

		public string GetCurrentColorsRecipe(string recipeName = "")
		{
			SaveOptions thisSaveOpts = SaveOptions.saveColors;
			return DoPartialSave(recipeName, thisSaveOpts);
		}

		public string GetCurrentDNARecipe(string recipeName = "")
		{
			SaveOptions thisSaveOpts = SaveOptions.saveDNA;
			return DoPartialSave(recipeName, thisSaveOpts);
		}

		private string DoPartialSave(string recipeName, SaveOptions thisSaveOpts)
		{
			Dictionary<string, UMATextRecipe> wardrobeCache = new Dictionary<string, UMATextRecipe>(_wardrobeRecipes);
			Dictionary<string, UMAWardrobeCollection> wcCache = new Dictionary<string, UMAWardrobeCollection>(_wardrobeCollections);
			var prevSharedColors = umaData.umaRecipe.sharedColors;//not sure if this is gonna work
			if (ensureSharedColors)//we dont want to keep the colors in the recipe though (otherwise effectively ensureSharedColors is going to be true hereafter)
				EnsureSharedColors();
			ClearWardrobeCollectionsRecipes(true);
			var DCSModel = new UMATextRecipe.DCSPackRecipe(this, recipeName, "DynamicCharacterAvatar", thisSaveOpts, null);
			if (ensureSharedColors)
			{
				umaData.umaRecipe.sharedColors = prevSharedColors;
				UpdateColors();
			}
			_wardrobeRecipes = wardrobeCache;
			_wardrobeCollections = wcCache;
			return JsonUtility.ToJson(DCSModel);
		}

		#endregion

		#region FULL EXPORT
		/// <summary>
		/// Returns the UMATextRecipe string with the addition of the Avatars current WardrobeSet.
		/// </summary>
		/// <param name="backwardsCompatible">If true, slot and overlay data is included and you can load the recipe into a non-dynamicCharacterAvatar.</param>
		/// <returns></returns>
		public string GetCurrentRecipe(bool backwardsCompatible = true)
		{
			Dictionary<string, UMATextRecipe> wardrobeCache = new Dictionary<string, UMATextRecipe>(_wardrobeRecipes);
			Dictionary<string, UMAWardrobeCollection> wcCache = new Dictionary<string, UMAWardrobeCollection>(_wardrobeCollections);
			var prevSharedColors = umaData.umaRecipe.sharedColors;//not sure if this is gonna work
			if (ensureSharedColors)//we dont want to keep the colors in the recipe though (otherwise effectively ensureSharedColors is going to be true hereafter)
				EnsureSharedColors();
			ClearWardrobeCollectionsRecipes(true);
			//if backwards compatible we want a DCSUniversalPackRecipe
			//otherwise we want a DCSPackRecipe
			string currentRecipeString = "";
			if (backwardsCompatible)
			{
				//If we are going to use a seperate type for a DCSSave then this shouldn't save the wardrobe set? Or if it does the Inspector should atleast offer a means of editing it...
				currentRecipeString = JsonUtility.ToJson(new UMATextRecipe.DCSUniversalPackRecipe(umaData.umaRecipe, WardrobeRecipes));
			}
			else
			{
				SaveOptions thisSaveOptions = SaveOptions.saveColors | SaveOptions.saveAnimator | SaveOptions.saveDNA | SaveOptions.saveWardrobe;
				currentRecipeString = JsonUtility.ToJson(new UMATextRecipe.DCSPackRecipe(this, this.name, "DynamicCharacterAvatar", thisSaveOptions));
			}
			if (ensureSharedColors)
			{
				umaData.umaRecipe.sharedColors = prevSharedColors;
				UpdateColors();
			}
			_wardrobeRecipes = wardrobeCache;
			_wardrobeCollections = wcCache;
			return currentRecipeString;
		}
		/// <summary>
		/// Saves the current DynamicCharacterAvatar using the optimized DCSPackRecipe model. This has smaller file size but the resulting recipe strings will not work with 'non-DynamicCharacterAvatar' avatars
		/// </summary>
		/// <param name="saveAsAsset">If true will save the resulting asset, otherwise saves the string to a txt file</param>
		/// <param name="filePath">If no file path is supplied it will be generated based on the settings in the Save section of the component</param>
		/// <param name="customSaveOptions">Override the default save options as defined in the avatar save section, to only save specific properties of the Avatar</param>
		public void DoSave(bool saveAsAsset = false, string filePath = "", SaveOptions customSaveOptions = SaveOptions.useDefaults)
		{
#if !UNITY_EDITOR
			saveAsAsset = false;
#endif
			var saveOptionsToUse = customSaveOptions == SaveOptions.useDefaults ? defaultSaveOptions : customSaveOptions;
			Dictionary<string, UMATextRecipe> wardrobeCache = new Dictionary<string, UMATextRecipe>(_wardrobeRecipes);
			Dictionary<string, UMAWardrobeCollection> wcCache = new Dictionary<string, UMAWardrobeCollection>(_wardrobeCollections);
			var prevSharedColors = umaData.umaRecipe.sharedColors;//not sure if this is gonna work
			if (ensureSharedColors)//we dont want to keep the colors in the recipe though (otherwise effectively ensureSharedColors is going to be true hereafter)
				EnsureSharedColors();
			ClearWardrobeCollectionsRecipes(true);
			string extension = saveAsAsset ? "asset" : "txt";
			var origSaveType = savePathType;
			if (saveAsAsset)
				savePathType = savePathTypes.FileSystem;
			if (filePath == "")
				filePath = GetSavePath(extension);
			savePathType = origSaveType;
			if (filePath != "")
			{
				//var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
				var asset = ScriptableObject.CreateInstance<UMADynamicCharacterAvatarRecipe>();
				var recipeName = saveFilename != "" ? saveFilename : gameObject.name + "_DCSRecipe";
				asset.SaveDCS(this, recipeName, saveOptionsToUse);
#if UNITY_EDITOR
				if (!saveAsAsset)
					FileUtils.WriteAllText(filePath, asset.recipeString);
				else
				{
					asset.recipeType = "DynamicCharacterAvatar";

					AssetDatabase.CreateAsset(asset, filePath);
					AssetDatabase.SaveAssets();
				}
#else
				FileUtils.WriteAllText(filePath, asset.recipeString);
#endif
				Debug.Log("Recipe saved to " + filePath);
				if (savePathType == savePathTypes.Resources)
				{
#if UNITY_EDITOR
					AssetDatabase.Refresh();
#endif
				}
				if (!saveAsAsset)
					UMAUtils.DestroySceneObject(asset);
			}
			if (ensureSharedColors)
			{
				umaData.umaRecipe.sharedColors = prevSharedColors;
				UpdateColors();
			}
			_wardrobeRecipes = wardrobeCache;
			_wardrobeCollections = wcCache;
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
					filePath = System.IO.Path.Combine(path, saveFilename + "." + extension);
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

		#region SETTINGS IMPORT (LOAD)

		/// <summary>
		/// Helper method for getting the required DCA.LoadOptions flags. Set all to false for DCA.LoadOptions.UseDefaults
		/// </summary>
		public static LoadOptions GetLoadOptionsFlags(bool loadRace, bool loadDNA, bool loadWardrobe, bool loadBodyColors, bool loadWardrobeColors)
		{
			if (!loadRace && !loadDNA && !loadWardrobe && !loadBodyColors && !loadWardrobeColors)
			{
				LoadOptions thisDCALO = LoadOptions.useDefaults;
				return thisDCALO;
			}
			else
			{
				LoadOptions thisDCALO = LoadOptions.useDefaults;
				if (loadRace)
					thisDCALO |= LoadOptions.loadRace;
				if (loadDNA)
					thisDCALO |= LoadOptions.loadDNA;
				if (loadWardrobe)
					thisDCALO |= LoadOptions.loadWardrobe;
				if (loadBodyColors)
					thisDCALO |= LoadOptions.loadBodyColors;
				if (loadWardrobeColors)
					thisDCALO |= LoadOptions.loadWardrobeColors;

				thisDCALO &= ~LoadOptions.useDefaults;

				return thisDCALO;
			}
		}

		#region PARTIAL IMPORT - HelperMethods

		//DOS 11012017 changed the following so that they dont load race- if you want to load the race call LoadFromRecipeString directly with the appropriate flags
		public void LoadWardrobeFromRecipeString(string recipeString, bool loadColors = true, bool clearExisting = false)
		{
			if (clearExisting)
				ClearSlots();
			LoadFromRecipeString(recipeString, GetLoadOptionsFlags(false, false, true, false, loadColors));
		}

		public void LoadColorsFromRecipeString(string recipeString, bool loadBodyColors = true, bool loadWardrobeColors = true)
		{
			LoadFromRecipeString(recipeString, GetLoadOptionsFlags(false, false, false, loadBodyColors, loadWardrobeColors));
		}

		public void LoadDNAFromRecipeString(string recipeString)
		{
			LoadFromRecipeString(recipeString, GetLoadOptionsFlags(false, true, false, false, false));
		}

		#endregion

		#region FULL CHARACTER IMPORT

		/// <summary>
		/// Sets the recipe string that will be loaded when the Avatar starts. If trying to load a recipe after the character has been created use 'LoadFromRecipeString'
		/// [DEPRICATED] Please use SetLoadString instead, this will work regardless of whether the character has been created or not.
		/// </summary>
		/// <param name="Recipe"></param>
		public void Preload(string Recipe)
		{
			Debug.LogWarning("DEPRICATED please use SetLoadString instead");
			loadString = Recipe;
			loadPathType = loadPathTypes.String;
			loadFileOnStart = true;
		}
		/// <summary>
		/// Sets the avatar to load from the given recipe string
		/// </summary>
		/// <param name="recipeString"></param>
		public void SetLoadString(string recipeString)
		{
			if (_isFirstSettingsBuild)
			{
				loadString = recipeString;
				loadPathType = loadPathTypes.String;
				loadFileOnStart = true;
			}
			else
			{
				LoadFromRecipeString(recipeString);
			}
		}

		/// <summary>
		/// Sets the avatar to load a recipe string from the given path
		/// </summary>
		/// <param name="recipeString"></param>
		public void SetLoadFilename(string filename, loadPathTypes _loadPathType)
		{
			loadFilename = filename;
			loadPathType = _loadPathType;
			loadFileOnStart = true;
		}

		/// <summary>
		/// Loads settings from an existing UMATextRecipe, optionally customizing what is loaded from the recipe
		/// </summary>
		/// <param name="settingsToLoad"></param>
		/// <param name="customLoadOptions"></param>
		public void LoadFromRecipe(UMARecipeBase settingsToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults)
		{
			if (UMATextRecipe.GetRecipesType((settingsToLoad as UMATextRecipe).recipeString) == "Wardrobe" || (settingsToLoad as UMATextRecipe).recipeType == "Wardrobe")
			{
				Debug.LogError("The assigned UMATextRecipe was a Wardrobe Recipe. You cannot load a character from a Wardrobe Recipe");
				return;
			}
			ImportSettings(UMATextRecipe.PackedLoadDCS(context, (settingsToLoad as UMATextRecipe).recipeString), customLoadOptions);
		}
		/// <summary>
		/// Load settings from an existing recipe string, optionally customizing what is loaded from the recipe
		/// </summary>
		/// <param name="settingsToLoad"></param>
		/// <param name="customLoadOptions"></param>
		public void LoadFromRecipeString(string settingsToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults)
		{
			ImportSettings(UMATextRecipe.PackedLoadDCS(context, settingsToLoad), customLoadOptions);
		}
		public void ImportSettings(UMATextRecipe.DCSUniversalPackRecipe settingsToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults)
		{
			StartCoroutine(ImportSettingsCO(settingsToLoad, customLoadOptions));
		}

		IEnumerator ImportSettingsCO(UMATextRecipe.DCSUniversalPackRecipe settingsToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults, bool forceDCSLoad = false)
		{
			var thisLoadOptions = customLoadOptions == LoadOptions.useDefaults ? defaultLoadOptions : customLoadOptions;
			//When ChangeRace calls this, it calls it with forceDCSLoad to be true so we need settingsToLoad.wardrobeSet fixed if its null
			if (forceDCSLoad)
			{
				if (settingsToLoad.wardrobeSet == null)
					settingsToLoad.wardrobeSet = new List<WardrobeSettings>();
			}
			//we only want to know the value of BuildCharacter at the time this recipe was sent- not what it may have been changed to over the process of the coroutine
			var wasBuildCharacterEnabled = _buildCharacterEnabled;
			_isFirstSettingsBuild = false;
			var prevDna = new UMADnaBase[0];
			bool needsUpdate = false;//gets set to true if anything caused downloads that we actually waited for
			while (!DynamicAssetLoader.Instance.isInitialized)
			{
				yield return null;
			}
			if (umaGenerator == null)
			{
				umaGenerator = UMAGenerator.FindInstance();
			}
			if (umaData == null)
			{
				Initialize();
			}
			if ((!thisLoadOptions.HasFlagSet(LoadOptions.loadDNA) || settingsToLoad.packedDna.Count == 0) && activeRace.racedata != null)
			{
				prevDna = umaData.umaRecipe.GetAllDna();
			}
			if (thisLoadOptions.HasFlagSet(LoadOptions.loadRace))
			{
				if (settingsToLoad.race == null || settingsToLoad.race == "")
				{
					Debug.LogError("The sent recipe did not have an assigned Race. Avatar could not be created from the recipe");
					yield break;
				}
				activeRace.name = settingsToLoad.race;
				SetActiveRace();
				//If the UmaRecipe is still after that null, bail - we cant go any further (and SetStartingRace will have shown an error)
				if (umaRecipe == null)
				{
					yield break;
				}
			}
			//this will be null for old UMA recipes without any wardrobe
			if (settingsToLoad.wardrobeSet != null)
			{
				//if we are not waiting for asset bundles we can build the placeholder avatar
				if (!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
				{
					BuildCharacter(false);
				}
				//before we can do anything with wardrobe we need to have the actual racedata downloaded so we know the wardrobe slots
				needsUpdate = false;
				while (DynamicAssetLoader.Instance.downloadingAssetsContains(activeRace.name))
				{
					needsUpdate = true;
					yield return null;
				}
				if (needsUpdate)
				{
					activeRace.data = context.raceLibrary.GetRace(activeRace.name);
					umaRecipe = activeRace.data.baseRaceRecipe;
				}
				//if we are loading wardrobe override everything that was previously set (by the default wardrobe or any previous user modifications)
				//sending an empty wardrobe set will clear the current wardrobe. If preLoadDefaultWardrobe is true and the wardrobe is empty LoadDefaultWardrobe gets called
				if (thisLoadOptions.HasFlagSet(LoadOptions.loadWardrobe))//sending an empty wardrobe set will clear the current wardrobe
				{
					_buildCharacterEnabled = false;
					LoadWardrobeSet(settingsToLoad.wardrobeSet);
					if (_wardrobeRecipes.Count == 0)
					{
						if(preloadWardrobeRecipes.loadDefaultRecipes)
							LoadDefaultWardrobe(true);
						ReapplyWardrobeCollections();
					}
					else
					{
						ReapplyWardrobeCollections();
					}
					_buildCharacterEnabled = wasBuildCharacterEnabled;
				}
				else
				{
					_buildCharacterEnabled = false;
					ApplyCurrentWardrobeToNewRace(settingsToLoad.wardrobeSet);
					_buildCharacterEnabled = wasBuildCharacterEnabled;
				}
				if (wasBuildCharacterEnabled)
				{
					SetAnimatorController(true);//may cause downloads to happen
					SetExpressionSet(true);
				}
				//loading new wardrobe items and animation controllers may have also caused downloads so wait for those- if we are not waiting we will have already created the placeholder avatar above
				yield return StartCoroutine(UpdateAfterDownloads());
				//update any wardrobe collections so if they are no longer active they dont show as active
				//UpdateWardrobeCollections();
				//Sort out colors
				umaData.umaRecipe.sharedColors = ImportSharedColors(settingsToLoad.sharedColors, thisLoadOptions);
				UpdateColors();//updateColors is called by LoadCharacter which is called by BuildCharacter- but we may not be Building

				if (wasBuildCharacterEnabled)
				{
					BuildCharacter(false);
				}
				//
				if (thisLoadOptions.HasFlagSet(LoadOptions.loadDNA) && settingsToLoad.packedDna.Count > 0)
				{
					umaData.umaRecipe.ClearDna();
					foreach (UMADnaBase dna in settingsToLoad.GetAllDna())
					{
						umaData.umaRecipe.AddDna(dna);
					}
				}
				else if (prevDna.Length > 0)
				{
					TryImportDNAValues(prevDna);
				}
				if (cacheCurrentState)
				{
					AddCharacterStateCache();
				}
			}
			else
			{
				StartCoroutine(ImportOldUma(settingsToLoad, thisLoadOptions, wasBuildCharacterEnabled));
			}

		}
		/// <summary>
		/// Do not call this directly use LoadFromRecipe(yourOldUMArecipe instead)
		/// </summary>
		/// <param name="settingsToLoad"></param>
		/// <param name="thisLoadOptions"></param>
		/// <param name="wasBuildCharacterEnabled"></param>
		/// <returns></returns>
		IEnumerator ImportOldUma(UMATextRecipe.DCSUniversalPackRecipe settingsToLoad, LoadOptions thisLoadOptions, bool wasBuildCharacterEnabled = true)
		{
			_isFirstSettingsBuild = false;
			var prevDna = new UMADnaBase[0];
			if ((!thisLoadOptions.HasFlagSet(LoadOptions.loadDNA) || settingsToLoad.packedDna.Count == 0) && activeRace.racedata != null)
			{
				prevDna = umaData.umaRecipe.GetAllDna();
			}
			//if its a standard UmaTextRecipe load it directly into UMAData since there wont be any wardrobe slots...
			//but make sure settingsToLoad race is the race that was actually used by SetStartingRace
			settingsToLoad.race = activeRace.name;
			UMATextRecipe.UnpackRecipe(umaData.umaRecipe, settingsToLoad, context);
			//
			ClearSlots();//old umas dont have any wardrobe
						 //old style recipes may still have had assets in an asset bundle. So if we are showing a placeholder rather than waiting...
			if (!waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
			{
				BuildCharacter(false);
			}
			SetAnimatorController(true);//may cause downloads to happen
			SetExpressionSet(true);
			//wait for any downloading assets
			yield return StartCoroutine(UpdateAfterDownloads());
			//shared colors
			umaData.umaRecipe.sharedColors = ImportSharedColors(settingsToLoad.sharedColors, thisLoadOptions);
			UpdateColors();
			//additionalRecipes
			umaData.AddAdditionalRecipes(umaAdditionalRecipes, context);
			//UMAs unpacking sets the DNA
			//but we can still try to set it back if thats what we want
			if (prevDna.Length > 0 && !thisLoadOptions.HasFlagSet(LoadOptions.loadDNA) && wasBuildCharacterEnabled)
			{
				TryImportDNAValues(prevDna);
			}
			if (wasBuildCharacterEnabled)
			{
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
			}
			UpdateAssetBundlesUsedbyCharacter();
		}

		/// <summary>
		/// Loads the text file in the loadFilename field to get its recipe string, and then calls LoadFromRecipeString to to process the recipe and load the Avatar.
		/// </summary>
		public void DoLoad()
		{
			StartCoroutine(GetRecipeStringToLoad());
		}

		public void LoadFromAssetFile(string Name)
		{
			UMAAssetIndexer UAI = UMAAssetIndexer.Instance;
			AssetItem ai = UAI.GetAssetItem<UMATextRecipe>(Name.Trim());
			if (ai != null)
			{
				string recipeString = (ai.Item as UMATextRecipe).recipeString;
				StartCoroutine(ProcessRecipeString(recipeString));
				return;
			}
			Debug.LogWarning("Asset '" + Name + "' Not found in Global Index");
		}

		public void LoadFromTextFile(string Name)
		{
			UMAAssetIndexer UAI = UMAAssetIndexer.Instance;
			AssetItem ai = UAI.GetAssetItem<TextAsset>(Name.Trim());
			if (ai != null)
			{
				string recipeString = (ai.Item as TextAsset).text;
				StartCoroutine(ProcessRecipeString(recipeString));
				return;
			}
			Debug.LogWarning("Asset '" + Name + "' Not found in Global Index");
		}

		IEnumerator ProcessRecipeString(string recipeString)
		{
			LoadFromRecipeString(recipeString);
			yield break;
		}

		IEnumerator GetRecipeStringToLoad()
		{
			string path = "";
			string recipeString = "";

			if (!string.IsNullOrEmpty(loadString) && loadPathType == loadPathTypes.String)
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
				UMAAssetIndexer UAI = UMAAssetIndexer.Instance;
				AssetItem ai = UAI.GetAssetItem<UMATextRecipe>(loadFilename.Trim());
				if (ai != null)
				{
					recipeString = (ai.Item as UMATextRecipe).recipeString;
				}
				else
				{
					ai = UAI.GetAssetItem<TextAsset>(loadFilename.Trim());

					if (ai != null)
					{
						recipeString = (ai.Item as TextAsset).text;
					}
					if (thisDCS.CharacterRecipes.ContainsKey(loadFilename.Trim()))
					{
						thisDCS.CharacterRecipes.TryGetValue(loadFilename.Trim(), out recipeString);
					}
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
		#endregion

		#endregion

		#region CHARACTER FINAL ASSEMBLY

		IEnumerator BuildCharacterWhenReady(bool RestoreDNA = true)
		{
			while (!DynamicAssetLoader.Instance.isInitialized)
			{
				yield return null;
			}
			yield return StartCoroutine(UpdateAfterDownloads());
			BuildCharacter(RestoreDNA);
		}

		/// <summary>
		/// Builds the character by combining the Avatar's raceData.baseRecipe with the any wardrobe recipes that have been applied to the avatar.
		/// </summary>
		/// <returns>Can also be used to return an array of additional slots if this avatars flagForReload field is set to true before calling</returns>
		/// <param name="RestoreDNA">If updating the same race set this to true to restore the current DNA.</param>
		public void BuildCharacter(bool RestoreDNA = true)
		{
			if (!_buildCharacterEnabled)
				return;
			_isFirstSettingsBuild = false;
			//clear these values each time we build
			wasCrossCompatibleBuild = false;
			crossCompatibleRaces.Clear();

            if (!DynamicAssetLoader.Instance.isInitialized)
			{
				StartCoroutine(BuildCharacterWhenReady(RestoreDNA));
				return;
			}
			if (waitForBundles && DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
			{
				StartCoroutine(BuildCharacterWhenReady(RestoreDNA));
				return;
			}

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

			List<UMAWardrobeRecipe> ReplaceRecipes = new List<UMAWardrobeRecipe>();
			List<UMARecipeBase> Recipes = new List<UMARecipeBase>();
			List<string> SuppressSlotsStrings = new List<string>();
			if ((WardrobeRecipes.Count > 0) && activeRace.racedata != null)
			{
				foreach (UMATextRecipe utr in WardrobeRecipes.Values)
				{
					if (utr.suppressWardrobeSlots != null)
					{
						if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
						{
							if (!SuppressSlotsStrings.Contains(utr.wardrobeSlot))
							{
								foreach (string suppressedSlot in utr.suppressWardrobeSlots)
								{
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
						if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
						{
							UMAWardrobeRecipe umr = (utr as UMAWardrobeRecipe);

							//check if this recipe is directly or only cross compatible
							bool utrIsCrossCompatible = (activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot));
							if (utrIsCrossCompatible)
							{
								//FixCrossCompatibleSlots will be called to remove any slots added by cross compatible recipes that are 'equivalent' according to this races 'Cross Compatibility' settings
								wasCrossCompatibleBuild = true;
								for (int ccri = 0; ccri < utr.compatibleRaces.Count; ccri++)
									if (!crossCompatibleRaces.Contains(utr.compatibleRaces[ccri]))
                                        crossCompatibleRaces.Add(utr.compatibleRaces[ccri]);
                            }

							if (umr != null && umr.HasReplaces)
							{
								ReplaceRecipes.Add(umr);
							}
							else
							{
								Recipes.Add(utr);
							}
                            if (utr.Hides.Count > 0)
							{
								foreach (string s in utr.Hides)
								{
									HiddenSlots.Add(s);
									//if the current race is only 'CrossCompatible' with the races this recipe is compatible with
									//get the equivalent slot from the races crossCompatibility settings so that is hidden too;
									if(utrIsCrossCompatible)
									{
										var equivalentSlot = activeRace.racedata.FindEquivalentSlot(utr.compatibleRaces,s,false);
										if (!string.IsNullOrEmpty(equivalentSlot))
											HiddenSlots.Add(equivalentSlot);
                                    }
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

			// Load all the recipes- if LoadCharacter returns true then loading the recipes caused assets to download- we need to wait and try again after they have finished
			if (LoadCharacter(umaRecipe, ReplaceRecipes, Recipes.ToArray()))
			{
				StartCoroutine(BuildCharacterWhenReady(RestoreDNA));
				return;
			}

			//But the ExpressionPlayer needs to be Initialized AFTER Load
			if (activeRace.racedata != null && !RestoreDNA)
			{
				this.CharacterUpdated.AddListener(InitializeExpressionPlayer);
			}

			// Add saved DNA
			if (RestoreDNA)
			{
				umaData.umaRecipe.ClearDna();
				foreach (UMADnaBase ud in CurrentDNA)
				{
					umaData.umaRecipe.AddDna(ud);
				}
			}
		}

		/// <summary>
		/// With a DynamicCharacterAvatar you do not call Load directly. If you want to load an UMATextRecipe directly call ImportSettings(yourUMATextRecipe)
		/// </summary>
		/// <param name="umaRecipe"></param>
		/// <param name="umaAdditionalSerializedRecipes"></param>
		//We dont really want the *actual* 'Load' method public because we dont want people to see/call that
		public override void Load(UMARecipeBase umaRecipe, params UMARecipeBase[] umaAdditionalSerializedRecipes)
		{
			Debug.Log(" With a DynamicCharacterAvatar you do not call Load directly. If you want to load an UMATextRecipe directly call ImportSettings(yourUMATextRecipe)");
			return;
		}
		/// <summary>
		/// Loads the Avatar from the given recipe and additional recipe. 
		/// Has additional functions for removing any slots that should be hidden by any 'wardrobe Recipes' that are in the additional recipes array.
		/// </summary>
		/// <param name="umaRecipe"></param>
		/// <param name="umaAdditionalRecipes"></param>
		/// <returns>Returns true if the final recipe load caused more assets to download</returns>
		bool LoadCharacter(UMARecipeBase umaRecipe, List<UMAWardrobeRecipe> Replaces, params UMARecipeBase[] umaAdditionalSerializedRecipes)
		{
			if (umaRecipe == null)
			{
				return false;
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

			//not sure if we do this first or not
			if (wasCrossCompatibleBuild)
			{
				FixCrossCompatibleSlots();
			}

			RemoveHiddenSlots();

			/*if (wasCrossCompatibleBuild)
			{
				FixCrossCompatibleSlots();
			}*/

			foreach (UMAWardrobeRecipe umr in Replaces)
			{
				ReplaceSlot(umr);
			}


			foreach(SlotData sd in umaData.umaRecipe.slotDataList)
			{
				if (sd.OverlayCount > 1)
				{
					List<OverlayData> Overlays = sd.GetOverlayList();
					List<OverlayData> SortedOverlays = new List<OverlayData>(Overlays.Count);

					foreach(OverlayData od in Overlays)
					{
						if (od.asset.overlayType == OverlayDataAsset.OverlayType.Cutout)
						{
							continue;
						}
						SortedOverlays.Add(od);
					}

					foreach(OverlayData od in Overlays)
					{
						if (od.asset.overlayType == OverlayDataAsset.OverlayType.Cutout)
						{
							SortedOverlays.Add(od);
						}
					}
					sd.SetOverlayList(SortedOverlays);
				}
			} 


			UpdateColors();

			//New event that allows for tweaking the resulting recipe before the character is actually generated
			RecipeUpdated.Invoke(umaData);

			//Did doing any of that cause more downloads?
			if (FinalRecipeAssetsDownloading())
			{
				Profiler.EndSample();
				return true;
			}

			if (umaRace != umaData.umaRecipe.raceData)
			{
				if (rebuildSkeleton)
				{
					foreach (Transform child in gameObject.transform)
					{
						UMAUtils.DestroySceneObject(child.gameObject);
					}
				}
				UpdateNewRace();
			}
			else
			{
				UpdateSameRace();
			}
			Profiler.EndSample();

			UpdateAssetBundlesUsedbyCharacter();

			return false;
		}

		bool FinalRecipeAssetsDownloading()
		{
			bool requiresWaiting = false;
			if (DynamicAssetLoader.Instance.downloadingAssets.downloadingItems.Count > 0)
			{
				var finalSlots = umaData.umaRecipe.GetAllSlots();

				for (int i = 0; i < finalSlots.Length; i++)
				{
					if (DynamicAssetLoader.Instance.downloadingAssetsContains(finalSlots[i].slotName))
					{
						if (!requiredAssetsToCheck.Contains(finalSlots[i].slotName))
						{
							requiredAssetsToCheck.Add(finalSlots[i].slotName);
							requiresWaiting = true;
						}
					}
					var thisSlotsOverlays = finalSlots[i].GetOverlayList();
					for (int oi = 0; oi < thisSlotsOverlays.Count; oi++)
					{
						if (DynamicAssetLoader.Instance.downloadingAssetsContains(thisSlotsOverlays[oi].overlayName))
						{
							if (!requiredAssetsToCheck.Contains(thisSlotsOverlays[oi].overlayName))
							{
								requiredAssetsToCheck.Add(thisSlotsOverlays[oi].overlayName);
								requiresWaiting = true;
							}
						}
					}
				}
			}
			return requiresWaiting;
		}

		/// <summary>
		/// Called when setting activeRace.data or name to null- destroys the generated renderer and bone game objects and sets the recipe and umaData to null
		/// </summary>
		private void UnloadAvatar()
		{
			if (!Application.isPlaying)
				return;

			foreach (Transform child in gameObject.transform)
			{
				UMAUtils.DestroySceneObject(child.gameObject);
			}
			ClearSlots();
			umaRecipe = null;
			umaData.umaRecipe.ClearDna();
			umaData.umaRecipe.SetSlots(new SlotData[0]);
			umaData.umaRecipe.sharedColors = new OverlayColorData[0];
			animationController = null;

			/*
            *For now, we are not going to clean this up as it resets the avatar rotation, but only in Unity 5.5 +
            if (gameObject.GetComponent<Animator>())
            {
                gameObject.GetComponent<Animator>().runtimeAnimatorController = null;
            }
            */
			if (gameObject.GetComponent<UMAExpressionPlayer>())
				gameObject.GetComponent<UMAExpressionPlayer>().enabled = false;
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

		/// <summary>
		/// Checks whether the resulting slots in the recipe should actually be there when the build contained recipes that were cross compatible.
		/// For example applying HumanMale underwear to the HumanMaleHighPoly race loads HumanMale's legs over the top of HumanMaleHighPolys legs
		/// This method will check whether the slots defined in that recipe have an equivalent slot defined in the race. If they do the slot in the recipe
		/// will be removed and its overlays applied to the equivalent slot in the base mesh, if overlays are defined as matching. Otherwise the overlays will not
		/// be applied and a warning will be shown.
		/// </summary>
		void FixCrossCompatibleSlots()
		{
			var recipeSlots = umaData.umaRecipe.slotDataList;
			string equivalentSlot = "";
            for (int i = 0; i < recipeSlots.Length; i++)
			{
				equivalentSlot = "";
				SlotData sd = recipeSlots[i];
				if (sd == null)
					continue;
				//if the race defines this slot as 'equivalent' to any of its own baseRecipe slots in its cross compatibility settings
				//find that slot in the slotDataList attempt to apply this slots overlays to that slot and then remove that slot
				equivalentSlot = activeRace.racedata.FindEquivalentSlot(crossCompatibleRaces, sd.slotName, false);
				if(equivalentSlot != "")
				{
					SlotData esd = null;
					for (int ei = 0; ei < recipeSlots.Length; ei++)
					{
						if (recipeSlots[ei] == null)
							continue;
						if (recipeSlots[ei].slotName == equivalentSlot)
						{
							esd = recipeSlots[ei];
							break;
						}
					}
					if(esd != null)
					{
						if (activeRace.racedata.GetOverlayCompatibility(sd.slotName))
						{
							//this is not going to work because some how we need to know what the base overlays are on the slot we are copying from so we only atempt to add the additional ones
							//so some kind of GetBaseOverlaysForSlot or GetOverlaysAddedToBaseSlot
							//something seems to go worong when the slot has shared overlays
							//esd.SetOverlayList(sd.GetOverlayList());
							var overlaysToAdd = sd.GetOverlayList();
							var overlaysOnSlot = esd.GetOverlayList();
							for(int oi = 0; oi < overlaysToAdd.Count; oi++)
							{
								if(!overlaysOnSlot.Contains(overlaysToAdd[oi]))
									esd.AddOverlay(overlaysToAdd[oi]);
							}
						}
						if (!HiddenSlots.Contains(sd.slotName))
							HiddenSlots.Add(sd.slotName);
					}
				}
			}
			//if we make this happen after RemoveHiddenSlots() we need to call it again
			RemoveHiddenSlots();
        }

		void ReplaceSlot(UMAWardrobeRecipe Replacer)
		{
			//we need to check if *this* recipe is directly or only cross compatible with this race
			bool isCrossCompatibleRecipe = (activeRace.racedata.IsCrossCompatibleWith(Replacer.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(Replacer.wardrobeSlot));
			string replaceSlot = Replacer.replaces;
			if (isCrossCompatibleRecipe)
			{
				var equivalentSlot = activeRace.racedata.FindEquivalentSlot(Replacer.compatibleRaces, replaceSlot, true);
				if (!string.IsNullOrEmpty(equivalentSlot))
					replaceSlot = equivalentSlot;
            }
			for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
			{
				SlotData sd = umaData.umaRecipe.slotDataList[i];
				if (sd == null)
					continue;
				if (sd.slotName == replaceSlot)
				{
					UMAPackedRecipeBase.UMAPackRecipe PackRecipe = Replacer.PackedLoad(context);
					UMAData.UMARecipe TempRecipe = UMATextRecipe.UnpackRecipe(PackRecipe, context);
					if (TempRecipe.slotDataList.Length > 0)
					{
						List<OverlayData> Overlays = sd.GetOverlayList();
						foreach (SlotData tsd in TempRecipe.slotDataList)
						{
							if (tsd != null)
							{
								tsd.SetOverlayList(Overlays);
								umaData.umaRecipe.slotDataList[i] = tsd;
								break;
							}
						}
					}
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

		public void UpdateUMA()
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

		//@jaimi not sure what calls this. Generator maybe?
		public void AvatarCreated()
		{
			ApplyBounds();
		}
		public void ApplyBounds()
		{
			SkinnedMeshRenderer smr = this.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
			smr.localBounds = new Bounds(smr.localBounds.center + BoundsOffset, smr.localBounds.size);
		}

		/// <summary>
		/// Adds the current state of the avatars wardrobe and colors and dna to a cache so that when the avatars race is set back to this current race, these can be restored
		/// </summary>
		/// <param name="cacheStateName"></param>
		void AddCharacterStateCache(string cacheStateName = "")
		{
			if (cacheStateName == "NULL")//we are caching the state before the Avatar is loaded- which is basically just the colors
			{
				var thisModel = new UMATextRecipe.DCSPackRecipe();
				var packedcolors = new List<UMAPackedRecipeBase.PackedOverlayColorDataV3>();
				foreach (ColorValue cv in characterColors.Colors)
				{
					packedcolors.Add(new UMAPackedRecipeBase.PackedOverlayColorDataV3(cv));
				}
				thisModel.characterColors = packedcolors;
				if (cacheStates.ContainsKey("NULL"))
				{
					cacheStates["NULL"] = JsonUtility.ToJson(thisModel);
				}
				else
				{
					cacheStates.Add("NULL", JsonUtility.ToJson(thisModel));
				}
			}
			else
			{
				bool wasEnsureSharedColors = ensureSharedColors;
				ensureSharedColors = true;
				var currentRecipeForRace = GetCurrentRecipe(false);
				if (cacheStates.ContainsKey(activeRace.name))
				{
					cacheStates[activeRace.name] = currentRecipeForRace;
				}
				else
				{
					cacheStates.Add(activeRace.name, currentRecipeForRace);
				}
				ensureSharedColors = wasEnsureSharedColors;
			}
		}

#endregion

#region UMACONTEXT RELATED

		//If the user inspects a DCA when there is no UMAContext in the scene it will blow up because RaceSetter needs one in order to find all the available races
		//and the Default Wardrobe and Race animators need one in order to assess whether the assets will be available at run time so create one on the fly like UMATextRecipe does
#if UNITY_EDITOR
		/// <summary>
		/// Creates a temporary UMAContext for use when editing DynamicCharacterAvatars when the open Scene does not have an UMAContext or libraries set up
		/// </summary>
		public UMAContext CreateEditorContext()
		{
			EditorUMAContext = UMAContext.CreateEditorContext();
			EditorApplication.update -= CheckEditorContextNeeded;
			EditorApplication.update += CheckEditorContextNeeded;
			return UMAContext.Instance;
		}

		private void DestroyEditorUMAContext()
		{
			if (EditorUMAContext != null)
			{
				foreach (Transform child in EditorUMAContext.transform)
				{
					DestroyImmediate(child.gameObject);
				}
				DestroyImmediate(EditorUMAContext);
				EditorApplication.update -= CheckEditorContextNeeded;
				Debug.Log("UMAEditorContext was removed");
			}
		}

		public void CheckEditorContextNeeded()
		{
			if (EditorUMAContext != null)
			{
				if (EditorUMAContext.GetComponentInChildren<UMAContext>() != null || EditorUMAContext.GetComponent<UMAContext>() != null)
				{
					if (this == null || gameObject == null || Selection.activeGameObject == null || Selection.activeGameObject != gameObject)
					{
						DestroyEditorUMAContext();
					}
				}
			}
			else
			{
				EditorApplication.update -= CheckEditorContextNeeded;
			}
		}
#endif

#endregion

#region ASSETBUNDLES RELATED

		/// <summary>
		/// Use when temporary wardrobe recipes have been used while the real ones have been downloading. Will replace the temp textrecipes with the downloaded ones.
		/// </summary>
		void UpdateSetSlots()
		{
			//what is the order with WardrobeCollections vs WardrobeRecipes i.e. if an avatar has a default wardrobe that has a chest slot and a wardrobe collection
			//that has a chest slot and a legs slot, which chest slot should show? I think WardrobeRecipes should take priority
			//BUT at this point what will we have if things were downloading?
			Dictionary<string, UMATextRecipe> newWardrobeRecipes = new Dictionary<string, UMATextRecipe>();
			var thisDCS = context.dynamicCharacterSystem as DynamicCharacterSystem;
			//because of WardrobeCollections we may need a more robust system here? maybe a 'placeholder' bool?
			foreach (KeyValuePair<string, UMATextRecipe> kp in _wardrobeRecipes)
			{
				if (!newWardrobeRecipes.ContainsKey(kp.Key))
				{
					newWardrobeRecipes.Add(kp.Key, thisDCS.GetRecipe(kp.Value.name, false));
				}
				else
				{
					newWardrobeRecipes[kp.Key] = thisDCS.GetRecipe(kp.Value.name, false);
				}
			}
			_wardrobeRecipes = newWardrobeRecipes;
			//if there was a wardrobe collection in the downloaded assets and its contents have not been added yet add them- if the slots are empty
			if (_wardrobeCollections.Count > 0)
			{
				Dictionary<string, UMAWardrobeCollection> newWardrobeCollections = new Dictionary<string, UMAWardrobeCollection>();
				foreach (UMAWardrobeCollection uwr in _wardrobeCollections.Values)
				{
					newWardrobeCollections.Add(uwr.wardrobeSlot, (thisDCS.GetRecipe(uwr.name, false) as UMAWardrobeCollection));
					var collectionSet = newWardrobeCollections[uwr.wardrobeSlot].GetRacesWardrobeSet(activeRace.racedata);
					if (collectionSet.Count > 0)
					{
						foreach (WardrobeSettings ws in collectionSet)
						{
							if (!WardrobeRecipes.ContainsKey(ws.slot))
								SetSlot(ws.slot, ws.recipe);
						}
					}
				}
				_wardrobeCollections = newWardrobeCollections;
			}
		}

		IEnumerator UpdateAfterDownloads()
		{
			bool needsUpdate = false;
			while (DynamicAssetLoader.Instance.downloadingAssetsContains(requiredAssetsToCheck))
			{
				needsUpdate = true;
				yield return null;
			}
			if (needsUpdate)
			{
				UpdateAfterDownload();
			}
			// UpdateAfterDownload.UpdateSetSlots might have also caused downloads to happen so if it did do this again
			if (requiredAssetsToCheck.Count > 0)
			{
				yield return StartCoroutine(UpdateAfterDownloads());
			}
		}

		void UpdateAfterDownload()
		{
			requiredAssetsToCheck.Clear();
			activeRace.data = context.raceLibrary.GetRace(activeRace.name);
			umaRecipe = activeRace.data.baseRaceRecipe;
			UpdateSetSlots();
			if (BuildCharacterEnabled)
			{
				SetExpressionSet(true);
				SetAnimatorController(true);
			}
		}

		/// <summary>
		/// Checks what assetBundles (if any) were used in the creation of this Avatar. NOTE: Query this UMA's AssetBundlesUsedbyCharacterUpToDate field before calling this function
		/// </summary>
		/// <param name="verbose">set this to true to get more information to track down when asset bundles are getting dependencies when they shouldn't because they are refrencing things from asset bundles you did not intend them to</param>
		/// <returns></returns>
		void UpdateAssetBundlesUsedbyCharacter(bool verbose = false)
		{
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
		}

#endregion

#endregion

#region SPECIALTYPES // these types should only be needed by DynamicCharacterAvatar

		[Serializable]
		public class RaceSetter
		{
			public string name;

			RaceData _data;

			//These properties use camelCase rather than lower case to deliberately hide the fact they are properties
			/// <summary>
			/// Will return the racedata for the current activeRace.name - if it is in an asset bundle or in resources it will find it (if it has already been downloaded) but wont cause it to download. If you ony need to know if the data is there use the racedata field instead.
			/// </summary>
			public RaceData data
			{
				get
				{
					Validate();
					return _data;
				}
				set
				{
					_data = value;
				}
			}
			/// <summary>
			/// returns the active raceData (quick)
			/// </summary>
			public RaceData racedata
			{
				get { return _data; }
			}

			void Validate()
			{
				var thisContext = UMAContext.FindInstance();
				if (thisContext == null)
				{
					Debug.LogWarning("UMAContext was missing this is required in scenes that use UMA. Please add the UMA_DCS prefab to the scene");
					return;
				}
				var thisDynamicRaceLibrary = (DynamicRaceLibrary)thisContext.raceLibrary as DynamicRaceLibrary;
				foreach (RaceData race in thisDynamicRaceLibrary.GetAllRaces())
				{
					if (race.raceName == this.name)
					{
						_data = race;
						break;
					}
				}
			}
		}

		[Serializable]
		public class WardrobeRecipeListItem
		{
			public string _recipeName;
			[System.NonSerialized]
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
				_compatibleRaces = new List<string>(recipe.compatibleRaces);
			}
		}

		[Serializable]
		public class WardrobeRecipeList
		{
			[Tooltip("If this is checked and the Avatar is NOT creating itself from a previously saved recipe, recipes in here will be added to the Avatar when it loads")]
			public bool loadDefaultRecipes = true;
			public List<WardrobeRecipeListItem> recipes = new List<WardrobeRecipeListItem>();

			public List<WardrobeRecipeListItem> Validate(bool allowDownloadables = false, string raceName = "", RaceData race = null)
			{
				List<WardrobeRecipeListItem> validRecipes = new List<WardrobeRecipeListItem>();
				var thisContext = UMAContext.FindInstance();
				if (thisContext == null)
				{
					return validRecipes;
				}
				var thisDCS = thisContext.dynamicCharacterSystem as DynamicCharacterSystem;
				if (thisDCS != null)
				{
					foreach (WardrobeRecipeListItem WLIRecipe in recipes)
					{
						//this needs to also check backwards compatible races so need the racedata, so send it or request it?
						if ((raceName == "" || (WLIRecipe._compatibleRaces.Contains(raceName) || (race != null && race.IsCrossCompatibleWith(WLIRecipe._compatibleRaces)))))
						{
							if (allowDownloadables)
							{
								WLIRecipe._recipe = thisDCS.GetRecipe(WLIRecipe._recipeName);
								if (WLIRecipe._recipe != null)
								{
									WLIRecipe._compatibleRaces = new List<string>(WLIRecipe._recipe.compatibleRaces);
									validRecipes.Add(WLIRecipe);
								}

							}
							else
							{
								if (thisDCS.RecipeIndex.ContainsKey(WLIRecipe._recipeName))
								{
									bool recipeFound = false;
									recipeFound = thisDCS.RecipeIndex.TryGetValue(WLIRecipe._recipeName, out WLIRecipe._recipe);
									if (recipeFound)
									{
										WLIRecipe._compatibleRaces = new List<string>(WLIRecipe._recipe.compatibleRaces);
										validRecipes.Add(WLIRecipe);
									}

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
			private RuntimeAnimatorController _animatorController;
			public RuntimeAnimatorController animatorController
			{
				get { return _animatorController; }
				set { _animatorController = value; }
			}
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

			public RuntimeAnimatorController GetAnimatorForRace(string racename)
			{
				RuntimeAnimatorController controllerToUse = defaultAnimationController;
				for (int i = 0; i < animators.Count; i++)
				{
					if (animators[i].raceName == racename)
					{
						if (animators[i].animatorController == null)
						{
							FindAnimatorByName(animators[i].animatorControllerName);
						}
						if (animators[i].animatorController != null)
							controllerToUse = animators[i].animatorController;
						break;
					}
				}
				return controllerToUse;
			}
			public void FindAnimatorByName(string animatorName)
			{
				bool dalDebugSetting = DynamicAssetLoader.Instance.debugOnFail;
				DynamicAssetLoader.Instance.debugOnFail = false;
				DynamicAssetLoader.Instance.AddAssets<RuntimeAnimatorController>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, true, assetBundleNames, resourcesFolderPath, null, animatorName, SetFoundAnimators);
				DynamicAssetLoader.Instance.debugOnFail = dalDebugSetting;
			}
			
			private void SetFoundAnimators(RuntimeAnimatorController[] foundControllers)
			{
				for (int fi = 0; fi < foundControllers.Length; fi++)
				{
					for (int i = 0; i < animators.Count; i++)
					{
						if (animators[i].animatorControllerName == foundControllers[fi].name)
						{
							animators[i].animatorController = foundControllers[fi];
						}
					}
				}
			}
		}

		//ColorValue is now a child class of OverlayColorData with extra properties that return the values ColorValue previously had, I did it like this so we dont break backwards compatibility for people
		[Serializable]
		public class ColorValue : OverlayColorData
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
				get
				{
					if (_name != null)
						ConvertOldFieldsToNew();
					return name;
				}
				set { name = value; }
			}

			public Color Color
			{
				get
				{
					if (_name != null)
						ConvertOldFieldsToNew();
					return color;
				}
				set { color = value; }
			}

			public Color MetallicGloss
			{
				get
				{
					if (_name != null)
						ConvertOldFieldsToNew();
					EnsureChannels(3);
					return channelAdditiveMask[2];
				}
				set
				{
					if (channelAdditiveMask.Length < 3)
						EnsureChannels(3);
					channelAdditiveMask[2] = value;
				}
			}

			public ColorValue()
			{
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

			/// <summary>
			/// This will be called to convert an old style ColorValue to a new style ColorValue based on whether name is null
			/// </summary>
			private void ConvertOldFieldsToNew()
			{
				if (!String.IsNullOrEmpty(_name))
				{
					EnsureChannels(3);
					name = _name;
					color = _color;
					channelAdditiveMask[2] = _metallicGloss;
					//marking _name as null ensures this doesn't happen again. Color doesn't have a null value
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

#region CONSTRUCTOR

			/// <summary>
			/// The default Constructor adds a delegate to EditorApplication.update which checks if any of the ColorValues were updated from old values to new values and marks the scene as dirty
			/// </summary>
			public ColorValueList()
			{

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
				return (OverlayColorData[])Colors.ToArray();
			}

			public OverlayColorData ToOverlayColorData(ColorValue cv)
			{
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

#endregion

#region DNASETTER
	/// <summary>
	/// A DnaSetter is used to set a specific piece of DNA on the avatar
	/// that it is pulled from.
	/// </summary>
	public class DnaSetter
	{
		public string Name; // The name of the DNA.
		public float Value; // Current value of the DNA.
		public string Category;

		protected int OwnerIndex;    // position of DNA in index, created at initialization
		protected UMADnaBase Owner;  // owning DNA class. Used to set the DNA by index

		/// <summary>
		/// Construct a DnaSetter
		/// </summary>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <param name="ownerIndex"></param>
		/// <param name="owner"></param>
		public DnaSetter(string name, float value, int ownerIndex, UMADnaBase owner, string category)
		{
			Name = name;
			Value = value;
			OwnerIndex = ownerIndex;
			Owner = owner;
			Category = category;
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
#endregion
}
