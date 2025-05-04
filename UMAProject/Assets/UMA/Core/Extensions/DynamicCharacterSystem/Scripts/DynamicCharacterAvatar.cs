#undef SUPER_LOGGINGCOLLECTIONS

using UnityEngine;
//For loading a recipe directly from the web @2465
using UnityEngine.Networking;
#if UNITY_EDITOR 
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine.Serialization;//for converting old characterColors.Colors to new colors

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UMA.PoseTools;//so we can set the expression set based on the race
using UnityEngine.SceneManagement;

#if UMA_ADDRESSABLES
using UnityEngine.ResourceManagement.AsyncOperations;
using AsyncOp = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<System.Collections.Generic.IList<UnityEngine.Object>>;
using System.Threading.Tasks;
#endif

namespace UMA.CharacterSystem
{
    [ExecuteInEditMode]
    public class DynamicCharacterAvatar : UMAAvatarBase
    {
        public float DelayUnload = 2.0f;
        public bool BundleCheck = true;
        private bool StartGuard = false;
        public bool KeepAnimatorController = false;
        [Tooltip("If true, the Animator will be rebuilt anytime the race changes")]
        public bool RecreateAnimatorOnRaceChange = true;
        public string userInformation = "";
#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/UMA/Create New Dynamic Character Avatar", false, 10)]
        public static void CreateDynamicCharacterAvatarMenuItem()
        {
            var res = new GameObject("New Dynamic Character Avatar");
            var da = res.AddComponent<DynamicCharacterAvatar>();
            da.ChangeRace("HumanMale");
            UnityEditor.Selection.activeGameObject = res;
        }

#endif
        #region Extra Events
        /// <summary>
        /// Callback event when the character recipe is updated. Use this to tweak the resulting recipe BEFORE the UMA is actually generated
        /// </summary>
        public UMADataEvent RecipeUpdated;
        public UMADataWardrobeEvent WardrobeAdded;
        public UMADataWardrobeEvent WardrobeRemoved;
        public UMACharacterEvent CharacterStart = new UMACharacterEvent();
        public UMADataEvent BuildCharacterBegun = new UMADataEvent();
        public UMASlotsEvent SlotsHidden = new UMASlotsEvent();
        public UMARecipesEvent WardrobeSuppressed= new UMARecipesEvent();

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

        //because the character might need to be preloaded, we may want everything required to create it to happen
        //but for it to still not be shown immediately or you may want to hide it anyway
        [Tooltip("If checked will turn off the SkinnedMeshRenderer after the character has been created to hide it. If not checked will turn it on again.")]
        public bool hide = false;
        [Tooltip("If checked, then any generated textures will be freed when hidden, and regenerated when shown again. Does nothing at edit time. At playtime, this only works when the 'hide' property is set of cleared. This will do nothing if you are not generating textures.")]
        public bool leanHiding = false;
        [NonSerialized]
        public bool lastHide;

        [Tooltip("When this is true, the meshcombiner will upload the data and the mesh will no longer be readable. Set this to false if you use a 3rd party asset that needs to read the mesh data.")]
        public bool markNotReadable = true;
        [Tooltip("When this is true, the meshcombiner will mark the mesh as dynamic. This will slightly decrease build time and slightly increase rendering.")]
        public bool markDynamic = true;
        [Tooltip("If true, then the meshcombiner will merge blendshapes found on slots that are part of this umaData")]
        public bool loadBlendShapes = false;

        [Tooltip("If true, then the meshcombiner will merge blendshapes that have active DNA")]
        public bool loadOnlyUsedBlendshapes = true;
        [Tooltip("If true, then normals will be loaded from the blendshapes if they exist")]
        public bool loadBlendshapeNormals = true;
        [Tooltip("If true, then tangents will be loaded from the blendshapes if they exist")]
        public bool loadBlendshapeTangents = true;
        [Tooltip("If true, then all frames of the blendshapes will be loaded. If false, only the LAST frame will be loaded.")]
        public bool loadAllFrames = true;
        [Tooltip("List of blendshapes that should always be kept, even if they would otherwise be optimized out.")]
        public List<string> forceKeepBlendshapes = new List<string>();
        [Tooltip("Limit the blendshapes to only those in this list. If empty, all blendshapes will be loaded. This can be set automatically when loading an Avatar Definition if you pass true to 'Limit Blendshapes to DNA'")]
        private HashSet<string> blendShapes = new HashSet<string>();

        [Tooltip("If true, will reuse the mecanim avatar if it exists.")]
        public bool keepAvatar;

        [Tooltip("If checked, will not animate or modify the vertexes")]
        public bool rawAvatar;

        [Tooltip("If checked, the predefined DNA will always be loaded every time the character is built.")]
        public bool keepPredefinedDNA;

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
        [Tooltip("Always rebuild the skeleton. This will clear out additional animated bones from slots.")]
        public bool alwaysRebuildSkeleton = false;
        [Tooltip("This will force the animator to rebind after avatar generation. You will know if you need to do this.")]
        public bool forceRebindAnimator = false;

        //the dictionary of active recipes this character is using to create itself
        private Dictionary<string, UMATextRecipe> _wardrobeRecipes = new Dictionary<string, UMATextRecipe>();
        private Dictionary<string, List<UMATextRecipe>> _additiveRecipes = new Dictionary<string, List<UMATextRecipe>>();
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

        public UMAPredefinedDNA predefinedDNA;
        /// <summary>
        /// When override DNA is used, the previous DNA is saved into savedDNA, so it can be restored later.
        /// </summary>
        private UMAPredefinedDNA savedDNA = new UMAPredefinedDNA();
        /// <summary>
        /// Any override DNA is accumulated in overrideDNA in BuildCharacter. This is then applied during the build process (after saving).
        /// </summary>
        private UMAPredefinedDNA overrideDNA = new UMAPredefinedDNA();

        //Load and Save fields
        //load
        public loadPathTypes loadPathType;
        public string loadPath;
        public string loadFilename;
        public string loadString;
        public bool loadFileOnStart;

        [Tooltip("This will make the slot use the UMAMaterial of the first overlay")]
        public bool forceSlotMaterials;

#if UMA_ADDRESSABLES
        private Queue<AsyncOp> LoadedHandles = new Queue<AsyncOp>();
#endif
        [Tooltip("Change to lower this specific DCA's atlas resolution. Leave 1.0f for resolution to be automatic.")]
        [Range(0.0f, 1.0f)]
        public float AtlasResolutionScale = 1.0f;

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

        private List<UMATextRecipe> SuppressedRecipes = new List<UMATextRecipe>();
        private List<SlotData> HiddenSlots = new List<SlotData>();
#if UNITY_EDITOR

        [Tooltip("Use editor time generation")]
        public bool editorTimeGeneration = true;
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
        public bool _buildCharacterEnabled = true;
        //everytime an avatar changes race a cache state can (optionally) be created so that when the user 
        //switches between races they do not loose their previous changes to the avatar when it was set to be that race
        private Dictionary<string, string> cacheStates = new Dictionary<string, string>();
        //the wardrobe slots that are hidden by the avatars current wardrobe
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

        // Any wardrobe slots in this list are force suppressed. That is, their recipes are NOT
        // merged in. 
        private List<string> forceSuppressedWardrobeSlots = new List<string>();
        // Any base slot that is in this list is forceably nulled out in the slot list after merging recipes
        // so that the slot will not be included in the next build. 
        private HashSet<string> forceRemovedBaseSlots = new HashSet<string>();

        private List<string> forceSuppressSlotsContaining = new List<string>();


        private HashSet<string> forceRemovedTags = new HashSet<string>();
#if UNITY_EDITOR
        private PreviewModel lastPreviewModel;
        private GameObject lastCustomModel;
        private Material mat;
        private Mesh previewMesh;
#endif
        #endregion

        #region PROPERTIES

        public HashSet<string> ForceRemovedTags { get { return forceRemovedTags; } }
        public HashSet<string> ForceRemovedBaseSlots { get { return forceRemovedBaseSlots; } }
        public List<string> ForceSuppressedWardrobeSlots { get { return forceSuppressedWardrobeSlots; } }

        public List<string> ForceSupressSlotsContaining { get {  return forceSuppressSlotsContaining; } }

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
                return UMAAssetIndexer.Instance.GetRecipes(activeRace.name);
            }
        }

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
                if (umaData != null)
                {
                    return umaData.umaRecipe.sharedColors;
                }

                return new OverlayColorData[0];
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

        public Dictionary<string, List<UMATextRecipe>> AdditiveRecipes
        {
            get
            {
                return _additiveRecipes;
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
            get
            {
                return _buildCharacterEnabled;
            }
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
                                BuildFromComponentSettings();
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
                            SetExpressionSet();
                            BuildCharacter(true, !BundleCheck);
                        }
                    }
                }
                else
                {
                    _buildCharacterEnabled = value;
                }
            }
        }
        /// <summary>
        /// Does the avatar build using the race/wardrobe/colors in the component? False if any of loadString/loadFilename/umaRecipe have been set before Start
        /// </summary>
        bool BuildUsingComponentSettings
        {
            get
            {
				bool startRecipeEmpty = (loadString == "" && loadFilename == "" /* && umaRecipe == null */);
                if (loadFileOnStart && !startRecipeEmpty) // was &&
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        #endregion

        #region METHODS 

        #region Start Update and Inititalization

        public void Awake()
        {
#if UNITY_EDITOR
            // Cleanup from any edit-time uma generation
            if (Application.isPlaying)
            {
                UMAData ud = GetComponent<UMAData>();

                if (ud != null)
                {
                    // cleanup any edit-time umaData
                    /// Having UMA's visible in the editor comes at a cost.
                    /// Have to clean up from edit time stuff.
                    if (editorTimeGeneration && Application.isPlaying)
                    {
                        List<GameObject> Cleaners = GetRenderers(gameObject);
                        Hide(false);
                        
                        for (int i=0;i<Cleaners.Count;i++)
                        {
                            GameObject go = Cleaners[i];
                            DestroyImmediate(go);
                        }
                    }
                   // ud.umaRoot = null;
                }
            }

#else
           UMAData ud = GetComponent<UMAData>();

           if (ud != null)
           {
               List<GameObject> Cleaners = GetRenderers(gameObject);
               Hide(false);
                for (int i=0;i<Cleaners.Count;i++)
                {
                    GameObject go = Cleaners[i];
                    DestroyImmediate(go);
                }
               //ud.umaRoot = null;
           }
#endif

            cacheStates = new Dictionary<string, string>();
        }

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // Try again after compiling and updating finished.
                EditorApplication.delayCall += OnScriptsReloaded;
                return;
            }

            EditorApplication.delayCall += RebuildAllEditTimeAvatars;
        }

        private static void RebuildAllEditTimeAvatars()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                DynamicCharacterAvatar[] dcas = GameObject.FindObjectsOfType<DynamicCharacterAvatar>();
                for (int i = 0; i < dcas.Length; i++)
                {
                    DynamicCharacterAvatar dca = dcas[i];
                    if (dca.editorTimeGeneration)
                    {
                        dca.GenerateSingleUMA();
                    }
                }
            }
        }
#endif
        // Use this for initialization
        public override void Start()
        {
            InitialStartup();
        }


        public void InitialStartup()
        {
            if (StartGuard)
            {
                return;
            }

            StartGuard = true;

            lastHide = !hide;

#if SUPER_LOGGING
			Debug.Log("Start on DynamicCharacterAvatar: " + gameObject.name);
#endif
            AddCharacterStateCache("NULL");
            InitializeAvatar();

            SetUMADataOptions();

            if (CharacterStart != null)
            {
                CharacterStart.Invoke(this);
            }

            if (animationController == null)
            {
                Animator a = GetComponent<Animator>();
                if (a)
                {
                    animationController = a.runtimeAnimatorController;
                }
            }
            //if the animator has been set the 'old' way respect that...
            if (raceAnimationControllers.defaultAnimationController == null && animationController != null)
            {
                raceAnimationControllers.defaultAnimationController = animationController;
            }
            //
            if (BuildCharacterEnabled == false)
            {
                return;
            }

            if (Application.isPlaying)
            {
                if (_isFirstSettingsBuild)
                {
                    if (BuildUsingComponentSettings)
                    {
                        _isFirstSettingsBuild = false;
                        BuildFromComponentSettings();
                    }
                    else //we have an umaRecipe set or a text string set or a file defined to load
                    {
                        _isFirstSettingsBuild = false;
                        BuildFromStartingFileOrRecipe();
                    }
                }
            }
#if UNITY_EDITOR
            else
            {
                if (editorTimeGeneration)
                {
                    GenerateSingleUMA();
                }
            }
#endif
        }

        private void SetUMADataOptions()
        {
            if (umaData == null)
            {
                Debug.LogWarning("UMAData is null, cannot set blendshape settings");
                return;
            }
            if (umaData.blendShapeSettings == null)
            {
                umaData.blendShapeSettings = new BlendShapeSettings();
            }
            umaData.blendShapeSettings.ignoreBlendShapes = !loadBlendShapes;
            umaData.blendShapeSettings.loadTangents = loadBlendshapeTangents;
            umaData.blendShapeSettings.loadNormals = loadBlendshapeNormals;
            umaData.blendShapeSettings.loadAllFrames = loadAllFrames;
            umaData.blendShapeSettings.filteredBlendshapes = blendShapes;
            umaData.markNotReadable = markNotReadable;
            umaData.markDynamic = markDynamic;
        }

        List<GameObject> GetRenderers(GameObject parent)
        {
            List<GameObject> objs = new List<GameObject>();
            int transformcount = parent.transform.childCount;
            for(int i=0;i<transformcount;i++)
            {
                Transform t = parent.transform.GetChild(i);
                if (t.GetComponent<SkinnedMeshRenderer>() != null)
                {
                    objs.Add(t.gameObject);
                }
            }
            return objs;
        }

        public void InitializeFromPreset(UMAPreset preset)
        {
            preloadWardrobeRecipes = preset.DefaultWardrobe;
            predefinedDNA = preset.PredefinedDNA;
            characterColors = preset.DefaultColors;
        }

        public void InitializeFromPreset(string presetstring)
        {
            UMAPreset prs = JsonUtility.FromJson<UMAPreset>(presetstring);
            InitializeFromPreset(prs);
        }


        /// <summary>
        /// Generate the UMA synchronously in this thread.
        /// This is for those times when you need the character to be created inline.
        /// After this call, you have a completely generated character.
        /// This will NOT work with Addressables!!!
        /// </summary>
        public void GenerateNow()
        {
            // We need to find the generator if we don't already have a reference to it.
            UMAGenerator ugb = umaGenerator;
            if (ugb != null)
            {
                activeRace.SetRaceData();
                if (activeRace.racedata != null)
                {
                    BuildCharacter(false, true);
                    ugb.GenerateSingleUMA(umaData, true); // don't fire completed events in the editor
                    ugb.Clear();
                }
            }
        }

#if UNITY_EDITOR

        public bool nextBuildSlotsOnly = false;
        private int generateWait = 0;
        const int maxWait = 60;

        public void GenerateSingleUMA(bool slotsOnly = false)
        {
            generateWait = 0;
            nextBuildSlotsOnly = slotsOnly;
            EditorApplication.delayCall += InternalGenerateSingleUMA;
        }

        private void InternalGenerateSingleUMA()
        {
            generateWait++;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (generateWait >= maxWait)
                {
                    // Don't try anymore.
                    return; 
                }
                // Try again after compiling and updating finished.
                EditorApplication.delayCall += InternalGenerateSingleUMA;
                return;
            }

            if (this == null || this.gameObject == null)
            {
                return;
            }
            bool slotsOnly = nextBuildSlotsOnly;
            UMAGenerator ugb = umaGenerator;
            if (ugb != null)
            {
                /* if (slotsOnly)
                 {
                     ugb.UpdateSlots(umaData);
                     return;
                 } */ // TODO: Fix this
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(gameObject.transform))
                {
                    // Unfortunately we must unpack the prefab or it will blow up.
                    GameObject go = PrefabUtility.GetOutermostPrefabInstanceRoot(this.gameObject);
                    UnityEditor.PrefabUtility.UnpackPrefabInstance(go, UnityEditor.PrefabUnpackMode.OutermostRoot, UnityEditor.InteractionMode.AutomatedAction);
                }
                if (umaData != null)
                {
                    umaData.SaveMountedItems();
                }
                CleanupGeneratedData();
                activeRace.SetRaceData();
                if (activeRace.racedata != null)
                {
                    LoadDefaultWardrobe();

                    // save the predefined DNA...
                    var dna = predefinedDNA.Clone();
                    BuildCharacter(false, true);
                    predefinedDNA = dna;

                    int oldScaleFactor = ugb.InitialScaleFactor;
                    int oldAtlasResolution = ugb.atlasResolution;

                    umaData.rawAvatar = rawAvatar;
                    ugb.FreezeTime = true;
                    ugb.InitialScaleFactor = ugb.editorInitialScaleFactor;
                    ugb.atlasResolution = ugb.editorAtlasResolution;

                    ugb.GenerateSingleUMA(umaData, false); // don't fire completed events in the editor

                    ugb.FreezeTime = false;
                    ugb.InitialScaleFactor = oldScaleFactor;
                    ugb.atlasResolution = oldAtlasResolution;
                    ugb.Clear();
                    umaData.RestoreSavedItems();
                }
            }
        }

        public void CleanupGeneratedData()
        {
            List<GameObject> Cleaners = GetRenderers(gameObject);
            Hide(false);
            for (int i=0;i<Cleaners.Count;i++)
            {
                var go = Cleaners[i];
                DestroyImmediate(go);
            }
            //DestroyImmediate(umaData);
            //umaData = null;
            ClearSlots();
        }
#endif
        public void ToggleHide(bool toggle)
        {
            hide = toggle;
            SetRenderers(toggle);
            if (umaData != null)
            {
                umaData.hideRenderers = this.hide;
            }
            lastHide = hide;
        }

        public void SetRenderers(bool val)
        {
            if (umaData == null)
            {
                return;
            }

            if (umaData.rendererCount > 0)
            {
                SkinnedMeshRenderer frenderer = umaData.GetRenderer(0);
                if (frenderer != null)
                {
                    if (frenderer.enabled && hide == true)
                    {
                        SkinnedMeshRenderer[] array = umaData.GetRenderers();
                        for (int i = 0; i < array.Length; i++)
                        {
                            SkinnedMeshRenderer smr = array[i];
                            if (smr != null && smr.enabled == true)
                            {
                                smr.enabled = false; 
                            }
                        }
                    }

                }

                if (hide)
                {
                    // dump all generated textures
                    if (leanHiding && UnityEngine.Application.isPlaying)
                    {
                        umaData.CleanTextures();
                    }
                }
                else
                {
                    if (leanHiding)
                    {
                        // restore generated textures, then unhide.
                        umaData.Dirty(false, true, false);
                        if (UnityEngine.Application.isPlaying)
                        {
                            umaData.Dirty(false, true, false);
                            umaData.OnCharacterUpdated += UnhideRenderers;
                        }
                        else
                        {
                            // In the editor, we need to unhide immediately.
                            UnhideRenderers(umaData);
                        }
                    }
                    else
                    {
                        UnhideRenderers(umaData);
                    }
                }
            }
        }

        private void UnhideRenderers(UMAData data)
        {
            if (umaData == null)
            {
                return;
            }
            umaData.OnCharacterUpdated -= UnhideRenderers;
            SkinnedMeshRenderer frenderer = umaData.GetRenderer(0);
            if (!frenderer.enabled && hide == false)
            {
                SkinnedMeshRenderer[] array = umaData.GetRenderers();
                for (int i = 0; i < array.Length; i++)
                {
                    SkinnedMeshRenderer smr = array[i];
                    if (smr != null && smr.enabled == false)
                    {
                        smr.enabled = true;
                    }
                }
            }
        }

        void Update()
        {
            if (umaData != null)
            {
#if UNITY_EDITOR
                if (!hide && editorTimeGeneration && Application.isPlaying == false)
                {
                    var r = umaData.GetRenderers();
                    if (r != null)
                    {
                        if (r.Length > 0 && r[0] != null && r[0].sharedMesh == null)
                        {
                            GenerateSingleUMA();
                        }
                    }
                }
#endif

                if (hide != lastHide)
                {
                    ToggleHide(hide);
                }
            }
        }

        void OnDisable()
        {
        }

        void OnDestroy()
        {
            Cleanup();
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

            if (showPlaceholder && editorTimeGeneration == false)
            {
                // Check for mesh Change
                if (!previewMesh || lastPreviewModel != previewModel || customModel != lastCustomModel)
                {
                    LoadMesh();
                }

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
            {
                model = customModel;
            }
            else
            {
                //search string finds both male and female!
                string[] assets = UnityEditor.AssetDatabase.FindAssets("t:Model Male_Unified");
                string male = "";
                string female = "";

                for (int i = 0; i < assets.Length; i++)
                {
                    string guid = assets[i];
                    string thePath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (thePath.ToLower().Contains("female"))
                    {
                        female = thePath;
                    }
                    else
                    {
                        male = thePath;
                    }
                }
                if (previewModel == PreviewModel.Male)
                {
                    if (!string.IsNullOrEmpty(male))
                    {
                        model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(male);
                    }
                    else
                    {
                        if (Debug.isDebugBuild)
                        {
                            Debug.LogWarning("Could not load Male_Unified model for preview!");
                        }
                    }
                }

                if (previewModel == PreviewModel.Female)
                {
                    if (!string.IsNullOrEmpty(female))
                    {
                        model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(female);
                    }
                    else
                    {
                        if (Debug.isDebugBuild)
                        {
                            Debug.LogWarning("Could not load Female_Unified model for preview!");
                        }
                    }
                }
            }
            if (model != null)
            {
                previewMesh = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            }
        }
#endif

        void BuildFromComponentSettings()
        {
            if (!SetActiveRace())
            {
                return;
            }

            if (WardrobeRecipes.Count == 0)
            {
                LoadDefaultWardrobe();
            }

            SetExpressionSet();
            SetAnimatorController(true);
            BuildCharacter(false, !BundleCheck);
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
        #region AVATAR Definition

        private bool isDefaultDna(float val)
        {
            // Because of the way text recipes store DNA, 
            // they are not exact.
            if (val >= 0.4999f && val < 0.502f)
            {
                return true;
            }

            return false;
        }


        /// <summary>
        /// Gets a an AvatarDefinition of the character
        /// </summary>
        /// <param name="skipRaceDefaults">Skip DNA default values on the race. You should pass this if you create from a prefab or from code each time.</param>
        /// <param name="skipColorDefaults">Skip default colors. If you do this, any shared color default values in a recipe should be default values to avoid issues.</param>
        /// <returns></returns>
        public AvatarDefinition GetAvatarDefinition(bool skipRaceDefaults, bool skipColorDefaults = true)
        {
            RaceData r = activeRace.data;

            Dictionary<string, DnaSetter> DefaultRaceDNA = new Dictionary<string, DnaSetter>();
            if (skipRaceDefaults)
            {
                UMAData.UMARecipe recipe = r.baseRaceRecipe.GetCachedRecipe(false);
                DefaultRaceDNA = GetDNA(recipe);
            }

            // *****************************************************
            // Get Wardrobe
            // *****************************************************
            List<string> Wardrobe = new List<string>();
            foreach (UMATextRecipe utr in WardrobeRecipes.Values)
            {
                Wardrobe.Add(utr.name);
            }

            foreach (var kp in _additiveRecipes.Values)
            {
                for (int i = 0; i < kp.Count; i++)
                {
                    UMATextRecipe utr = kp[i];
                    Wardrobe.Add(utr.name);
                }
            }

            // *****************************************************
            // Get DNA
            // *****************************************************
            List<DnaDef> Dna = new List<DnaDef>();
            var CurrentDNA = GetDNA().Values;

            foreach (DnaSetter d in CurrentDNA)
            {
                if (skipRaceDefaults)
                {
                    if (DefaultRaceDNA.ContainsKey(d.Name) && d.Value != DefaultRaceDNA[d.Name].Value)
                    {
                        DnaDef def = new DnaDef(d.Name, d.Value);
                        Dna.Add(def);
                    }
                }
                else
                {
                    DnaDef def = new DnaDef(d.Name, d.Value);
                    Dna.Add(def);
                }
            }

            // *****************************************************
            // Get Colors
            // *****************************************************
            List<SharedColorDef> Colors = new List<SharedColorDef>();

            var CurrentColors = characterColors.Colors;
            

            for (int i1 = 0; i1 < CurrentColors.Count; i1++)
            {
                ColorValue col = CurrentColors[i1];
                SharedColorDef scd = new SharedColorDef(col.name, col.channelCount);
                List<ColorDef> colorchannels = new List<ColorDef>();
                if (col.HasProperties)
                {
                    scd.shaderParms = col.PropertyBlock.GetPropertyStrings();
                }
                for (int i = 0; i < col.channelCount; i++)
                {
                    if (skipColorDefaults)
                    {
                        if (!col.isDefault(i))
                        {
                            Color Mask = col.channelMask[i];
                            Color Additive = col.channelAdditiveMask[i];
                            colorchannels.Add(new ColorDef(i, ColorDef.ToUInt(Mask), ColorDef.ToUInt(Additive)));
                        }
                    }
					else 
					{
						Color Mask = col.channelMask[i];
						Color Additive = col.channelAdditiveMask[i];
						colorchannels.Add(new ColorDef(i, ColorDef.ToUInt(Mask), ColorDef.ToUInt(Additive)));
					}
                }
                if (colorchannels.Count > 0)
                {
                    scd.SetChannels(colorchannels.ToArray());
                    Colors.Add(scd);
                }
            }

            // Save the Avatar def.
            AvatarDefinition adf = new AvatarDefinition();
            adf.RaceName = this.activeRace.name;
            adf.Wardrobe = Wardrobe.ToArray();
            adf.Dna = Dna.ToArray();
            adf.Colors = Colors.ToArray();
            return adf;
        }

        /// <summary>
        /// Gets a string representation of the avatar
        /// </summary>
        /// <param name="skipDefaults">Skip DNA default values on the race. You should pass this if you create from a prefab or from code each time.</param>
        /// <param name="skipColorDefaults">Skip default colors. If you do this, any shared color default values in a recipe should be default values to avoid issues.</param>
        /// <returns></returns>
        public string GetAvatarDefinitionString(bool skipDefaults, bool skipColorDefaults = false)
        {
            AvatarDefinition adf = GetAvatarDefinition(skipDefaults, skipColorDefaults);
            return JsonUtility.ToJson(adf);
        }

        private void LoadColors(AvatarDefinition adf, bool resetColors)
        {
            if (adf.Colors == null)
            {
                return;
            }

            if (resetColors && activeRace != null && activeRace.data != null)
            {
                characterColors.Colors.Clear();
                List<OverlayColorData> colors = activeRace.data.GetDefaultColors();
                for (int i = 0; i < colors.Count; i++)
                {
                    OverlayColorData ocd = colors[i];
                    if (ocd.HasName())
                    {
                        characterColors.SetRawColor(ocd.name, ocd);
                    }
                }
            }

            for (int i1 = 0; i1 < adf.Colors.Length; i1++)
            {
                SharedColorDef sc = adf.Colors[i1];
                if (characterColors.GetColor(sc.name, out OverlayColorData ocd))
                {
                    if (sc.channels == null)
                    {
                        continue;
                    }

                    // Make sure it's in the default state.
                    ocd.EnsureChannels(sc.count);
                    for (int i = 0; i < ocd.channelCount; i++)
                    {
                        ocd.channelMask[i] = Color.white;
                        ocd.channelAdditiveMask[i] = new Color(0, 0, 0, 0);
                    }

                    for (int i = 0; i < sc.channels.Length; i++)
                    {
                        ColorDef def = sc.channels[i];
                        ocd.channelMask[def.chan] = ColorDef.ToColor(def.mCol);
                        ocd.channelAdditiveMask[def.chan] = ColorDef.ToColor(def.aCol);
                    }
                    if (sc.shaderParms != null)
                    {
                        ocd.PropertyBlock = new UMAMaterialPropertyBlock();
                        ocd.PropertyBlock.SetPropertyStrings(sc.shaderParms);
                    }
                }
                else
                {
                    OverlayColorData nocd = new OverlayColorData(sc.count);
                    for (int i = 0; i < sc.channels.Length; i++)
                    {
                        ColorDef def = sc.channels[i];
                        nocd.channelMask[def.chan] = ColorDef.ToColor(def.mCol);
                        nocd.channelAdditiveMask[def.chan] = ColorDef.ToColor(def.aCol);
                    }
                    if (sc.shaderParms != null)
                    {
                        nocd.PropertyBlock = new UMAMaterialPropertyBlock();
                        nocd.PropertyBlock.SetPropertyStrings(sc.shaderParms);
                    }
                    characterColors.SetRawColor(sc.name, nocd);
                }
            }
        }

        private void LoadWardrobe(AvatarDefinition adf, bool loadDefaultWardobe, bool ResetWardrobe)
        {
            if (ResetWardrobe)
            {
                this.ClearSlots();
            }
            if (loadDefaultWardobe)
            {
                LoadDefaultWardrobe();
            }

            if (adf.Wardrobe == null)
            {
                return;
            }

            var recipes = UMAAssetIndexer.Instance.GetRecipes(adf.RaceName);
            for (int i = 0; i < adf.Wardrobe.Length; i++)
            {
                string s = adf.Wardrobe[i];
                UMATextRecipe utr = UMAAssetIndexer.Instance.GetRecipe(s, false);
                if (utr != null)
                {
                    SetSlot(utr);
                }
            }
        }

        private void PreloadAvatarDefinition(AvatarDefinition adf, bool loadDefaultWardrobe, bool resetDNA, bool resetWardrobe, bool resetColors, bool optimizeBlendShapes)
        {
            RacePreset = adf.RaceName;
            LoadColors(adf, resetColors);
            LoadWardrobe(adf, loadDefaultWardrobe, resetWardrobe);

            PreloadDNA(adf, resetDNA, optimizeBlendShapes);
        }

        private void PreloadDNA(AvatarDefinition adf, bool resetDNA, bool optimizeBlendshapes)
        {
            if (resetDNA)
            {
                predefinedDNA = new UMAPredefinedDNA();
                // get DNA from race...
                if (activeRace.data != null)
                {
                    Dictionary<string, float> defaultDNA = activeRace.data.GetDefaultDNA();
                    foreach (var kp in defaultDNA)
                    {
                        predefinedDNA.AddDNA(kp.Key, kp.Value);
                    }
                }
            }
            if (adf.Dna != null)
            {
                for (int i = 0; i < adf.Dna.Length; i++)
                {
                    DnaDef d = adf.Dna[i];
                    predefinedDNA.AddDNA(d.Name, d.Value);
                }
            }
            if (optimizeBlendshapes && loadBlendShapes)
            {
                SetFilteredBlendshapes(adf.Dna);
            }
        }

        private void SetFilteredBlendshapes(DnaDef[] dna)
        {
            if (activeRace.data == null)
            {
                return;
            }
            Dictionary<string, List<string>> DnaToBlendshapes = activeRace.data.GetDNAToBlendShapes();

            blendShapes.Clear();

            blendShapes.Clear();
            for (int i = 0; i < forceKeepBlendshapes.Count; i++)
            {
                blendShapes.Add(forceKeepBlendshapes[i]);
            }


            for (int i=0;i<dna.Length;i++)
            {
                DnaDef d = dna[i];

                if (d.Value > 0.4999f && d.Value < 0.502f)
                {
                    continue;
                }
                
                if (DnaToBlendshapes.ContainsKey(d.Name))
                {
                    List<string> bshapes = DnaToBlendshapes[d.Name];
                    for (int i1 = 0; i1 < bshapes.Count; i1++)
                    {
                        string bshape = bshapes[i1];
                        if (!blendShapes.Contains(bshape))
                        {
                            blendShapes.Add(bshape);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Load the avatar definition into the character
        /// </summary>
        /// <param name="adf">Avatar Definition to load</param>
        /// <param name="loadDefaultWardrobe">load the default wardrobe before loading wardrobe</param>
        /// <param name="ResetDNA">Reset DNA to default values before loading</param>
        /// <param name="ResetWardrobe">Reset Wardrobe</param>
        /// <param name="ResetColors">Reset colors</param>
        /// <param name="optimizeBlendShapes">Force only used Blendshapes to load</param>
        public void LoadAvatarDefinition(AvatarDefinition adf, bool loadDefaultWardrobe = false, bool ResetDNA = true, bool ResetWardrobe = true, bool ResetColors = true, bool optimizeBlendShapes = false)
        {
            if (umaData == null)
            {
                PreloadAvatarDefinition(adf, loadDefaultWardrobe, ResetDNA, ResetWardrobe, ResetColors, optimizeBlendShapes);
                return;
            }

            if (adf.RaceName != null)
            {
                activeRace.name = adf.RaceName;
                activeRace.SetRaceData();
            }

            LoadColors(adf, ResetColors);
            WardrobeRecipes.Clear();
            LoadWardrobe(adf, loadDefaultWardrobe, ResetWardrobe);
            PreloadDNA(adf, ResetDNA, optimizeBlendShapes);
        }

        public void LoadAvatarDefinition(string adfstring, bool loadDefaultWardrobe = false, bool ResetDNA = true, bool ResetWardrobe = true, bool ResetColors = true, bool optimizeBlendShapes = false)
        {
            if (adfstring.StartsWith("AA*"))
            {
                AvatarDefinition adf = AvatarDefinition.FromCompressedString(adfstring);
                LoadAvatarDefinition(adf, loadDefaultWardrobe, ResetDNA, ResetWardrobe, ResetColors, optimizeBlendShapes);
            }
            else
            {
                AvatarDefinition adf = JsonUtility.FromJson<AvatarDefinition>(adfstring);
                LoadAvatarDefinition(adf, loadDefaultWardrobe, ResetDNA, ResetWardrobe, ResetColors,optimizeBlendShapes);
            }
        }
        #endregion

        #region SETTINGS MODIFICATION (RACE RELATED)

        /// <summary>
        /// Sets the starting race of the avatar based on the value of the 'activeRace'. 
        /// </summary>
        bool SetActiveRace()
        {
            if (activeRace.name == "" || activeRace.name == "None Set")
            {
                activeRace.data = null;
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("No activeRace set. Aborting build");
                }

                return false;
            }
            //ImportSettingsCO might have changed the activeRace.name so we may still need to change the actual racedata if activeRace.racedata.raceName is different
            if (activeRace.data != null && activeRace.name == activeRace.racedata.raceName)
            {
                activeRace.name = activeRace.racedata.raceName;
                umaRecipe = activeRace.racedata.baseRaceRecipe;
            }
            //otherwise...
            else if (activeRace.name != "")
            {
                activeRace.data = UMAAssetIndexer.Instance.GetRace(activeRace.name);
                if (activeRace.racedata != null)
                {
                    umaRecipe = activeRace.racedata.baseRaceRecipe;
                }
            }
            //if we are loading an old UMARecipe from the recipe field and the old race is not in resources the race will be null but the recipe wont be 
            if (umaRecipe == null)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("[SetActiveRace] could not find baseRaceRecipe for the race " + activeRace.name + ". Have you set one in the raceData?");
                }

                return false;
            }
            return true;
        }

        public void ChangeRace(string racename, bool force)
        {
            ChangeRace(racename, ChangeRaceOptions.useDefaults, force);
        }

        /// <summary>
        /// Change the race of the Avatar, optionally overriding the 'onChangeRace' settings in the avatar component itself
        /// </summary>
        /// <param name="racename">race to change to</param>
        /// <param name="customChangeRaceOptions">flags for the race change options</param>
        public bool ChangeRace(string racename, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults, bool ForceChange = false)
        {
            // never been built, just use the race preset.
            if (activeRace.racedata == null && ForceChange == false)
            {
                RacePreset = racename;
                return true;
            }

            if (UpdatePending())
            {
                return false;
            }
            RaceData thisRace = null;
            if (racename != "None Set")
            {
                thisRace = UMAAssetIndexer.Instance.GetRace(racename);
            }

            ChangeRace(thisRace, customChangeRaceOptions, ForceChange);
            return true;
        }

        public bool ForceRaceChange(string racename)
        {
            // never been built, just use the race preset.
            RaceData thisRace = UMAAssetIndexer.Instance.GetRace(racename);
            ChangeRace(thisRace, ChangeRaceOptions.useDefaults, true);
            return true;
        }

        public void ChangeRaceData(string raceName)
        {
            if (activeRace.racedata == null)
            {
                RacePreset = raceName;
                return;
            }
            this.activeRace.name = raceName;
            SetActiveRace();
        }

        /// <summary>
        /// Change the race of the Avatar, optionally overriding the 'onChangeRace' settings in the avatar component itself
        /// </summary>
        /// <param name="race"></param>
        /// <param name="customChangeRaceOptions">flags for the race change options</param>
        public void ChangeRace(RaceData race, ChangeRaceOptions customChangeRaceOptions = ChangeRaceOptions.useDefaults, bool ForceChange = false)
        {
            bool actuallyChangeRace = ForceChange;
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
            {
                actuallyChangeRace = true;
            }
            else if (activeRace.name != race.raceName)
            {
                actuallyChangeRace = true;
            }

            if (actuallyChangeRace)
            {
                PerformRaceChange(race, customChangeRaceOptions);
            }
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
                ImportSettings(UMATextRecipe.PackedLoadDCS((race.baseRaceRecipe as UMATextRecipe).recipeString), thisLoadFlags, true);
            }
        }

        #endregion

        #region SETTINGS MODIFICATION (WARDROBE RELATED)

        /// <summary>
        /// Loads the default wardobe items set in 'defaultWardrobeRecipes' in the CharacterAvatar itself onto the Avatar's base race recipe. Use this to make a naked avatar always have underwear or a set of clothes for example
        /// </summary>
        public void LoadDefaultWardrobe()
        {
            if (activeRace.name == "" || activeRace.name == "None Set")
            {
                return;
            }

            if (!preloadWardrobeRecipes.loadDefaultRecipes && preloadWardrobeRecipes.recipes.Count == 0)
            {
                return;
            }

            if (activeRace.racedata == null)
            {
                activeRace.SetRaceData();
            }

            List<WardrobeRecipeListItem> validRecipes = preloadWardrobeRecipes.GetRecipesForRace(activeRace.name, activeRace.racedata);
            if (validRecipes.Count > 0)
            {
                for (int i = 0; i < validRecipes.Count; i++)
                {
                    WardrobeRecipeListItem recipe = validRecipes[i];
                    if (recipe._recipe != null && recipe._enabledInDefaultWardrobe)
                    {
                        if (((recipe._recipe.compatibleRaces.Count == 0 || recipe._recipe.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.IsCrossCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))))
                        {
                            //the check activeRace.data.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot) makes sure races that are cross compatible 
                            //with another race but which dont have all of that races wardrobeslots, dont try to load things they dont have wardrobeslots for
                            //However we need to make sure that if a slot has already been assigned that is DIRECTLY compatible with the race it is not overridden
                            //by one that is cross compatible
                            if (activeRace.racedata.IsCrossCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))
                            {
                                if (recipe._recipe.Appended)
                                {
                                    SetSlot(recipe._recipe);
                                }
                                else if (!WardrobeRecipes.ContainsKey(recipe._recipe.wardrobeSlot))
                                {
                                    SetSlot(recipe._recipe);
                                }
                            }
                            else
                            {
                                SetSlot(recipe._recipe);
                            }
                        }
                    }
                    else
                    {
                        if (Debug.isDebugBuild && recipe._recipe == null)
                        {
                            Debug.LogWarning("[DynamicCharacterAvatar:LoadDefaultWardrobe] recipe._recipe was null for " + recipe._recipeName);
                        }
                    }
                }
            }
        }

        public UMATextRecipe FindSlotRecipe(string Slotname, string Recipename)
        {
#if SUPER_LOGGINGCOLLECTIONS
            Debug.Log("Looking for Available recipes for wardrobe slot: " + Slotname);
#endif

            var recipes = AvailableRecipes;

            if (recipes.ContainsKey(Slotname) != true)
            {
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Available Recipes does not contain Slot: " +Slotname);
#endif
                return null;
            }

            List<UMATextRecipe> SlotRecipes = recipes[Slotname];
			if(SlotRecipes == null) { //VES added
				Debug.LogWarning("DCA SlotRecipes is null");
				return null;
			}
            for (int i = 0; i < SlotRecipes.Count; i++)
            {
				UMATextRecipe utr;
				try { //VES added try catch workaround
					utr = SlotRecipes[i];
				} catch(Exception exception) {
					System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
					stringBuilder.Append("DynamicCharacterAvatar.FindSlotRecipe() failed to access list element, so skipping ");
					stringBuilder.Append(Slotname);
					stringBuilder.Append(", iter ");
					stringBuilder.Append(i);
					stringBuilder.Append(" of ");
					stringBuilder.Append(SlotRecipes.Count);
					Debug.Log(stringBuilder.ToString());
					stringBuilder.Append(exception.Message); //bypassing editor warning for not using exception
					continue;
				}
				if (utr != null && utr.name == Recipename) //VES added null check
                {
                    return utr;
                }
            }
#if SUPER_LOGGINGCOLLECTIONS
            Debug.Log("Available Recipes does not contain Recipe: "+Recipename+" for slot "+ Slotname);
#endif

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
            UMATextRecipe utr = GetWardrobeItem(SlotName);
            if (utr != null)
            {
                return utr.name;
            }

            return "";
        }

        public UMATextRecipe GetWardrobeItem(string SlotName)
        {
            if (WardrobeRecipes.ContainsKey(SlotName))
            {
                return WardrobeRecipes[SlotName];
            }
            return null;
        }

        /// <summary>
        /// Sets the avatars wardrobe slot to use the given wardrobe recipe (not to be mistaken with an UMA SlotDataAsset)
        /// </summary>
        /// <param name="utr">The WardrobeRecipe it WardrobeCollection to add to the Avatar</param>
        private void internalSetSlot(UMATextRecipe utr, string thisRecipeSlot)
        {
            if (utr.Appended)
            {
                if (!_additiveRecipes.ContainsKey(thisRecipeSlot))
                {
                    _additiveRecipes.Add(thisRecipeSlot, new List<UMATextRecipe>());
                }
                _additiveRecipes[thisRecipeSlot].Add(utr);
                if (WardrobeAdded != null)
                {
                    WardrobeAdded.Invoke(umaData, utr as UMAWardrobeRecipe);
                }

                return;
            }
            if (_wardrobeRecipes.ContainsKey(thisRecipeSlot))
            {
                //New event that allows for tweaking the resulting recipe before the character is actually generated
                if (WardrobeRemoved != null)
                {
                    WardrobeRemoved.Invoke(umaData, _wardrobeRecipes[thisRecipeSlot] as UMAWardrobeRecipe);
                }

                _wardrobeRecipes[thisRecipeSlot] = utr;
                if (WardrobeAdded != null)
                {
                    WardrobeAdded.Invoke(umaData, utr as UMAWardrobeRecipe);
                }
            }
            else
            {
                _wardrobeRecipes.Add(thisRecipeSlot, utr);
                if (WardrobeAdded != null)
                {
                    WardrobeAdded.Invoke(umaData, utr as UMAWardrobeRecipe);
                }
            }
        }

        /// <summary>
        /// Sets the avatars wardrobe slot to use the given wardrobe recipe (not to be mistaken with an UMA SlotDataAsset)
        /// </summary>
        /// <param name="utr">The WardrobeRecipe it WardrobeCollection to add to the Avatar</param>
        public bool SetSlot(UMATextRecipe utr)
        {
			if (utr == null) {
				return false;
			}
            if (utr is UMAWardrobeCollection)
            {
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Loading wardrobe collection: " + utr.name);
#endif
                LoadWardrobeCollection((utr as UMAWardrobeCollection));
                return true;
            }

            // This is set to not load
            if (utr.wardrobeSlot == "None")
            {
                return false;
            }

            // No race set yet - must be a preload.
            if (string.IsNullOrEmpty(activeRace.name))
            {
                internalSetSlot(utr, utr.wardrobeSlot);
                return true;
            }

            // No compatible races set... Oh well, just allow it.
            // Must work for everything! 
            if (utr.compatibleRaces.Count == 0)
            {
                internalSetSlot(utr, utr.wardrobeSlot);
                return true;
            }

            if (activeRace.racedata == null)
            {
                activeRace.SetRaceData();
            }

            // If it's for this race, or the race is compatible with another race
            if (utr.compatibleRaces.Contains(activeRace.name) || activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces))
            {
                internalSetSlot(utr, utr.wardrobeSlot);
                return true;
            }

            // must be incompatible
            return false;
        }

        public void SetSlot(string Slotname)
        {
            var UTR = UMAAssetIndexer.Instance.GetRecipe(Slotname,false);
            if (UTR != null)
            {
                SetSlot(UTR);
            }
        }

        public void SetSlot(string Slotname, string Recipename)
        {
            UMATextRecipe utr = FindSlotRecipe(Slotname, Recipename);
            if (!utr)
            {
                //throw new Exception("Unable to find slot or recipe for Slotname "+ Slotname+" Recipename "+ Recipename);
                //it may just be that the race has changed and the current wardrobe didn't fit? If so we dont want to stop everything.
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("Unable to find slot or recipe for Slotname " + Slotname + " Recipename " + Recipename);
                }
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
            if (_additiveRecipes.ContainsKey(ws))
            {
                _additiveRecipes.Remove(ws);
            }
        }

        /// <summary>
        /// Clears the listed wardrobe slots of any wardrobeRecipes that have been set on the avatar
        /// </summary>
        public void ClearSlots(List<string> slotsToClear)
        {
            for (int i = 0; i < slotsToClear.Count; i++)
            {
                string slot = slotsToClear[i];
                ClearSlot(slot);
            }
        }
        /// <summary>
        /// Clears all the wardrobe slots of any wardrobeRecipes that have been set on the avatar
        /// </summary>
        public void ClearSlots()
        {
            WardrobeRecipes.Clear();
            _additiveRecipes.Clear();
        }

        /// <summary>
        /// Adds the given wardrobe collection to the avatars WardrobeCollection list and loads its recipes (if it has a wardrobe set for the current race)
        /// </summary>
        public void LoadWardrobeCollection(string collectionName)
        {
            UMATextRecipe utr = UMAAssetIndexer.Instance.GetRecipe(collectionName, false);

            if (!utr || !(utr is UMAWardrobeCollection))
            {
                //Dont show a warning. When editing the avatar wardrobe collections stay in the list until the avatar is saved (or RemoveUnusedCollections is called)
                //so that switching back to the race that does use the collection causes it to load again
                //Debug.LogWarning("Unable to find a WardrobeCollection for collectionName " + collectionName);
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Unable to find slot recipe!"+collectionName);
#endif
            }
            else
            {
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Calling LoadWardrobeCollection for collection " + utr.name);
#endif
                LoadWardrobeCollection((utr as UMAWardrobeCollection));
            }
        }

        public void LoadWardrobeCollection(UMAWardrobeCollection uwr)
        {
            //If there is already a WardrobeCollection belonging to this group applied to the Avatar, unload and remove it
            if (_wardrobeCollections.ContainsKey(uwr.wardrobeSlot))
            {
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Unloading old wardrobe collection: " + uwr.wardrobeSlot);
#endif
                UnloadWardrobeCollectionGroup(uwr.wardrobeSlot);
            }
#if SUPER_LOGGINGCOLLECTIONS
            Debug.Log("Adding to slot: " + uwr.wardrobeSlot);
#endif
            _wardrobeCollections.Add(uwr.wardrobeSlot, uwr);
#if SUPER_LOGGINGCOLLECTIONS
            Debug.Log("Unpacking Collection");
#endif

            var thisSettings = uwr.GetUniversalPackRecipe(this);
            //if there is a wardrobe set for this race treat this like a 'FullOutfit'
            if (thisSettings.wardrobeSet.Count > 0)
            {
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Unpacking slot return "+thisSettings.wardrobeSet.Count+" items");
#endif

                LoadWardrobeSet(thisSettings.wardrobeSet, false);
                if (thisSettings.sharedColorCount > 0)
                {
                    ImportSharedColors(thisSettings.sharedColors, LoadOptions.loadWardrobeColors);
                }
            }
#if SUPER_LOGGINGCOLLECTIONS
            else
            {
                Debug.Log("Unpacking slot return 0 items.");
            }
#endif

            return;
        }

        /// <summary>
        /// Call this when slots are removed from WardrobeRecipes so that any slots that are left empty by the change have any wardrobe collection recipes re-applied
        /// </summary>
        public void ReapplyWardrobeCollections()
        {
            if (_wardrobeCollections.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
            {
                var wardrobeSet = kp.Value.GetRacesWardrobeSet(activeRace.racedata);
                if (wardrobeSet.Count > 0)
                {
                    for (int i = 0; i < wardrobeSet.Count; i++)
                    {
                        WardrobeSettings ws = wardrobeSet[i];
                        if (!_wardrobeRecipes.ContainsKey(ws.slot) && !string.IsNullOrEmpty(ws.recipe))
                        {
                            SetSlot(ws.slot, ws.recipe);
                        }
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
            {
                return false;
            }

            var collectionSet = GetWardrobeCollection(collectionName).GetRacesWardrobeSet(activeRace.racedata);
            if (collectionSet.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < collectionSet.Count; i++)
            {
                WardrobeSettings ws = collectionSet[i];
                bool wasApplied = false;
                foreach (UMATextRecipe utr in _wardrobeRecipes.Values)
                {
                    if (ws.recipe == utr.name)
                    {
                        if (!fullyApplied)
                        {
                            return true;
                        }

                        wasApplied = true;
                    }
                }
                if (!wasApplied && fullyApplied)
                {
                    return false;
                }
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
                                {
                                    ClearSlot(wardrobeSet[si].slot);
                                }
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
            List<string> collectionsToRemove = new List<string>();
            foreach (KeyValuePair<string, UMAWardrobeCollection> kp in _wardrobeCollections)
            {
                if (!IsCollectionApplied(kp.Value.name))
                {
                    collectionsToRemove.Add(kp.Key);
                }
            }
            for (int i = 0; i < collectionsToRemove.Count; i++)
            {
                string c = collectionsToRemove[i];
                _wardrobeCollections.Remove(c);
            }
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
            for (int i = 0; i < collectionsToUnload.Count; i++)
            {
                string c = collectionsToUnload[i];
                UnloadWardrobeCollection(c);
            }
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
            List<WardrobeRecipeListItem> validDefaultRecipes = preloadWardrobeRecipes.GetRecipesForRace(activeRace.name, activeRace.racedata);
            fallbackSet = fallbackSet ?? new List<WardrobeSettings>();
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
                                for (int pcri = 0; pcri < pWardrobeSet.Count; pcri++)
                                {
                                    if (pWardrobeSet[pcri].slot == wkp.Key && pWardrobeSet[pcri].recipe == wkp.Value.name)
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
                                            var thisWCRecipe = UMAAssetIndexer.Instance.GetRecipe(aWardrobeSet[acri].recipe, false);
                                            newWardrobeRecipes.Add(aWardrobeSet[acri].slot, thisWCRecipe);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (pWardrobeSet.Count > 0 && aWardrobeSet.Count == 0)
                    {
                        //remove each recipe in pWardrobeSet from wrBU so they dont get re added below
                        for (int i = 0; i < pWardrobeSet.Count; i++)
                        {
                            WardrobeSettings pws = pWardrobeSet[i];
                            if (wrBU.ContainsKey(pws.slot))
                            {
                                if (wrBU[pws.slot].name == pws.recipe)
                                {
                                    wrBU.Remove(pws.slot);
                                }
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
            if (fallbackSet.Count > 0)
            {
                for (int i = 0; i < fallbackSet.Count; i++)
                {
                    if (!newWardrobeRecipes.ContainsKey(fallbackSet[i].slot))
                    {
                        var fbRecipe = UMAAssetIndexer.Instance.GetRecipe(fallbackSet[i].recipe, false);
                        newWardrobeRecipes.Add(fallbackSet[i].slot, fbRecipe);
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
        /// <param name="wardrobeSet">List of wardrobe settings.</param>
        /// <param name="clearExisting">Defaults to false. Set to true to clear the existing wardrobe recipes.</param>
        public void LoadWardrobeSet(List<WardrobeSettings> wardrobeSet, bool clearExisting = false)
        {
            // _isFirstSettingsBuild = false;
            if (clearExisting || wardrobeSet.Count == 0)
            {
#if SUPER_LOGGINGCOLLECTIONS
                Debug.Log("Clearing recipes for set");
#endif
                _wardrobeRecipes.Clear();
            }
            if (wardrobeSet.Count > 0)
            {
                //we have to do WardrobeCollections first because they may only be partially applied
                for (int i = 0; i < wardrobeSet.Count; i++)
                {
                    WardrobeSettings ws = wardrobeSet[i];
                    if (ws.slot == "WardrobeCollection")
                    {
#if SUPER_LOGGINGCOLLECTIONS
                        Debug.Log("Slot is Wardrobe Collection.");
#endif

                        if (string.IsNullOrEmpty(ws.recipe))
                        {
#if SUPER_LOGGINGCOLLECTIONS
                            Debug.Log("Recipe is empty. Skipping");
#endif
                            continue;
                        }
#if SUPER_LOGGINGCOLLECTIONS
                        Debug.Log("Loading the recipe: "+ws.recipe);
#endif

                        LoadWardrobeCollection(ws.recipe);
                    }
                }
                for (int i = 0; i < wardrobeSet.Count; i++)
                {
                    WardrobeSettings ws = wardrobeSet[i];
#if SUPER_LOGGINGCOLLECTIONS
                    Debug.Log("Processing Wardrobeset " + ws.slot);
#endif

                    if (ws.slot != "WardrobeCollection")
                    {

#if SUPER_LOGGINGCOLLECTIONS
                        Debug.Log("Processing Wardrobeset " + ws.slot);
#endif
                        if (!string.IsNullOrEmpty(ws.recipe))
                        {
#if SUPER_LOGGINGCOLLECTIONS
                            Debug.Log("Setting slot " + ws.slot + " to "+ ws.recipe);
#endif
                            SetSlot(ws.slot, ws.recipe);
                        }
                        else
                        {
                            ClearSlot(ws.slot);
                        }
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
                    var collectionSet = uwr.GetRacesWardrobeSet(activeRace.racedata);
                    if (collectionSet.Count > 0)
                    {
                        bool wasApplied = false;
                        for (int i = 0; i < collectionSet.Count; i++)
                        {
                            WardrobeSettings ws = collectionSet[i];
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
            if (removeUnappliedCollections && groupsToClear.Count > 0)
            {
                for (int i = 0; i < groupsToClear.Count; i++)
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
            {
                return ocd;
            }

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
        /// Simpler version of SetColor
        /// </summary>
        /// <param name="SharedColorName"></param>
        /// <param name="AlbedoColor"></param>
        /// <param name="UpdateTexture"></param>
        public void SetColorValue(string SharedColorName, Color AlbedoColor)
        {
            OverlayColorData ocd = new OverlayColorData(3);
            ocd.channelMask[0] = AlbedoColor;
            SetColor(SharedColorName, ocd, false);
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

        public void SetRawColor(string Name, OverlayColorData colorData, bool UpdateTexture = true)
        {
            characterColors.SetRawColor(Name, colorData);
            if (UpdateTexture)
            {
                UpdateColors();
                ForceUpdate(false, UpdateTexture, false);
            }
        }

        /// <summary>
        /// Remove a previously added color
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="UpdateTexture"></param>
        public void ClearColor(string Name, bool Update = true)
        {
            characterColors.RemoveColor(Name);
            if (Update)
            {
                BuildCharacter(true, !BundleCheck);
            }
        }

        /// <summary>
        /// Applies these colors to the loaded Avatar and adds any colors the loaded Avatar has which are missing from this list, to this list
        /// </summary>
        //NOTE needs to be public for the editor
        public void UpdateColors(bool triggerDirty = false)
        {
            if (umaData.umaRecipe.sharedColors == null)
            {
                return;
            }

            // Process the always update colors first
            for (int i = 0; i < umaData.umaRecipe.sharedColors.Length; i++)
            {
                OverlayColorData ucd = umaData.umaRecipe.sharedColors[i];
                if (ucd.HasName())
                {
                    if (ucd.PropertyBlock != null && ucd.PropertyBlock.alwaysUpdate)
                    {
                        characterColors.SetRawColor(ucd.name, ucd);
                    }
                    if (ucd.PropertyBlock != null && ucd.PropertyBlock.alwaysUpdateParms)
                    {
                        characterColors.SetRawColorParms(ucd.name, ucd);
                    }
                }
            }

            OverlayColorData c;
            for (int i = 0; i < umaData.umaRecipe.sharedColors.Length; i++)
            {
                OverlayColorData ucd = umaData.umaRecipe.sharedColors[i];
                if (ucd.HasName())
                {
                    if (!(ucd.PropertyBlock != null && ucd.PropertyBlock.alwaysUpdate))
                    {
                        if (characterColors.GetColor(ucd.name, out c))
                        {
                            // if the character has the color, then the color from the character overwrites what is in the
                            // recipe.
                            ucd.AssignFrom(c);
                        }
                        else
                        {
                            // if the character doesn't have the color, then the color is loaded into the character with the 
                            // default value from the recipe color.
                            characterColors.SetColor(ucd.name, ucd);
                        }
                    }
                }
            }

            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slotData = umaData.umaRecipe.slotDataList[i];
                if (slotData != null)
                {
                    var overlays = slotData.GetOverlayList();
                    for (int ovl = 0; ovl < overlays.Count; ovl++)
                    {
                        OverlayData od = overlays[ovl];
                        if (od != null)
                        {
                            if (od.colorData.HasProperties)
                            {
                                UMAMaterialPropertyBlock propertyBlock = od.colorData.PropertyBlock;

                                for (int property = 0; property < propertyBlock.shaderProperties.Count; property++)
                                {
                                    var theProp = propertyBlock.shaderProperties[property] as UMAOverlayTransformProperty;
                                    if (theProp != null)
                                    {
                                        od.instanceTransformed = true;
                                        od.Translate = theProp.Translate;
                                        od.Rotation = theProp.Rotate;
                                        od.Scale = theProp.Scale;
                                    }
                                }
                            }
                        }
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
            if (activeRace == null)
            {
                Debug.Log("No activeRace found");
                return bodyColorNames;
            }
            if (activeRace.data == null)
            {
                Debug.Log("No raceData found for " + activeRace.name);
                return bodyColorNames;
            }
            if (activeRace.data.baseRaceRecipe == null)
            {
                Debug.Log("No baseRaceRecipe found for " + activeRace.name);
                return bodyColorNames;
            }
            var baseRaceRecipeTemp = UMATextRecipe.PackedLoadDCS((activeRace.data.baseRaceRecipe as UMATextRecipe).recipeString);
            for (int i = 0; i < baseRaceRecipeTemp.sharedColors.Length; i++)
            {
                OverlayColorData col = baseRaceRecipeTemp.sharedColors[i];
                bodyColorNames.Add(col.name);
            }
            return bodyColorNames;
        }

        /// <summary>
        /// Loads any shared colors from the given recipe to the CharacterColors List, only if they are also defined in the current baseRaceRecipe, optionally applying then to the current UMAData.UMARecipe
        /// </summary>
        /// <param name="colorsToLoad"></param>
        /// <param name="apply"></param>
        /// <returns></returns>
        public List<OverlayColorData> LoadBodyColors(OverlayColorData[] colorsToLoad, bool apply = false)
        {
            return LoadBodyOrWardrobeColors(colorsToLoad, true, apply);
        }
        /// <summary>
        /// Loads any shared colors from the given recipe to the CharacterColors List, only if they are NOT defined in the current baseRaceRecipe, optionally applying then to the current UMAData.UMARecipe
        /// </summary>
        /// <param name="colorsToLoad"></param>
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
            {
                for (int i = 0; i < colorsToLoad.Length; i++)
                {
                    OverlayColorData col = colorsToLoad[i];
                    if (bodyColorNames.Contains(col.name))
                    {
                        SetColor(col.name, col, false);
                        if (!newSharedColors.Contains(col))
                        {
                            newSharedColors.Add(col);
                        }
                    }
                }
            }
            else if (!loadingBody)
            {
                for (int i = 0; i < colorsToLoad.Length; i++)
                {
                    OverlayColorData col = colorsToLoad[i];
                    if (!bodyColorNames.Contains(col.name))
                    {
                        SetColor(col.name, col, false);
                        if (!newSharedColors.Contains(col))
                        {
                            newSharedColors.Add(col);
                        }
                    }
                }
            }

            if (apply)
            {
                umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
            }

            return newSharedColors;
        }
        /// <summary>
        /// Restores the body colors to the ones defined in the component on start, optionally applying these to the UMAData.UMARecipe
        /// </summary>
        /// <param name="apply"></param>
        /// <param name="fullRestore"></param>
        /// <returns></returns>
        public List<OverlayColorData> RestoreCachedBodyColors(bool apply = false, bool fullRestore = false)
        {
            return RestoreCachedBodyOrWardrobeColors(true, apply, fullRestore);
        }
        /// <summary>
        ///  Restores the wardrobe colors to the ones defined in the component on start, optionally applying these to the UMAData.UMARecipe
        /// </summary>
        /// <param name="apply"></param>
        /// <param name="fullRestore"></param>
        /// <returns></returns>
        public List<OverlayColorData> RestoreCachedWardrobeColors(bool apply = false, bool fullRestore = false)
        {
            return RestoreCachedBodyOrWardrobeColors(false, apply, fullRestore);
        }

        private List<OverlayColorData> RestoreCachedBodyOrWardrobeColors(bool restoringBody = true, bool apply = false, bool fullRestore = false)
        {
            List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
            if (!cacheStates.ContainsKey("NULL"))
            {
                return newSharedColors;
            }
            else
            {
                var thisCacheData = JsonUtility.FromJson<UMATextRecipe.DCSPackRecipe>(cacheStates["NULL"]);
                List<string> bodyColorNames = GetBodyColorNames();
                if (restoringBody)
                {
                    for (int i = 0; i < thisCacheData.sharedColors.Length; i++)
                    {
                        OverlayColorData col = thisCacheData.sharedColors[i];
                        if (bodyColorNames.Contains(col.name))
                        {
                            if (!GetColor(col.name) || fullRestore)
                            {
                                SetColor(col.name, col, false);
                                if (!newSharedColors.Contains(col))
                                {
                                    newSharedColors.Add(col);
                                }
                            }
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < thisCacheData.sharedColors.Length; i++)
                    {
                        OverlayColorData col = thisCacheData.sharedColors[i];
                        if (!bodyColorNames.Contains(col.name))
                        {
                            if (!GetColor(col.name) || fullRestore)
                            {
                                SetColor(col.name, col, false);
                                if (!newSharedColors.Contains(col))
                                {
                                    newSharedColors.Add(col);
                                }
                            }
                        }
                    }
                }
            }
            if (apply)
            {
                umaData.umaRecipe.sharedColors = newSharedColors.ToArray();
            }

            return newSharedColors;
        }

        #endregion

        #region SETTINGS MODIFICATION (DNA RELATED)

        private void TryImportDNAValues(UMADnaBase[] prevDna)
        {
            //dont bother if there is nothing to do...
            if (prevDna.Length == 0)
            {
                return;
            }
            //umaData.umaRecipe.ApplyDNA(umaData, true);Don't think we need to apply- if we dont we can do the dna reverting when BuildCharacterEnabled is false
            var activeDNA = umaData.umaRecipe.GetAllDna();
            for (int i = 0; i < activeDNA.Length; i++)
            {
                if (activeDNA[i] is DynamicUMADnaBase)
                {
                    //iterate over each dna in prev dna and try to apply its values to this dna
                    for (int i1 = 0; i1 < prevDna.Length; i1++)
                    {
                        UMADnaBase dna = prevDna[i1];
                        ((DynamicUMADnaBase)activeDNA[i]).ImportUMADnaValues(dna);
                    }
                }
            }
        }


        /// <summary>
        /// Gets the default DNA for the race.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, float> GetDefaultDNA()
        {
            Dictionary<string, float> dna = new Dictionary<string, float>();
            if (activeRace == null)
            {
                Debug.Log($"No race set getting default race DNA on character {gameObject.name}");
                return dna;
            }

            // if the race has never been set, 
            if (activeRace.racedata == null)
            {
                activeRace.SetRaceData();
            }

            // if anything has gone wrong getting the race, then just return nothing.
            if (activeRace.racedata == null)
            {
                Debug.Log($"Unable to get racedata for race {activeRace.name} on character {gameObject.name}");
                return dna;
            }

            return activeRace.racedata.GetDefaultDNA();
        }

        /// <summary>
        /// Get DNA names and Values for the current character.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, float> GetDNAValues(UMAData.UMARecipe recipe = null)
        {
            Dictionary<string, float> dna = new Dictionary<string, float>();

            if (activeRace == null)
            {
                Debug.Log($"No race set getting default race DNA on character {gameObject.name}");
                return dna;
            }

            if (activeRace.racedata == null)
            {
                activeRace.SetRaceData();
            }
            // if anything has gone wrong getting the race, then just return nothing.
            if (activeRace.racedata == null)
            {
                Debug.Log($"Unable to get racedata for race {activeRace.name} on character {gameObject.name}");
                return dna;
            }

            if (umaData == null)
            {
                return GetDefaultDNA();
            }

            UMADnaBase[] dnaBase = umaData.GetAllDna();

            if (recipe == null)
            {
                dnaBase = umaData.GetAllDna();
            }
            else
            {
                dnaBase = recipe.GetAllDna();
            }

            for (int i1 = 0; i1 < dnaBase.Length; i1++)
            {
                UMADnaBase db = dnaBase[i1];
                string Category = db.GetType().ToString();

                //TODO racedata.GetConverter is obsolete because lots of converters can use the same dna names (dnaAsset) now 
                //I'm just gonna use the first found one- we can do something more advanced if/when we need to
                IDNAConverter[] dcb = activeRace.racedata.GetConverters(db);
                if (dcb.Length > 0 && dcb[0] != null && (!string.IsNullOrEmpty(dcb[0].DisplayValue)))
                {
                    Category = dcb[0].DisplayValue;
                }

                for (int i = 0; i < db.Count; i++)
                {
                    if (dna.ContainsKey(db.Names[i]))
                    {
                        dna[db.Names[i]] = db.Values[i];// DnaSetter(db.Names[i], db.Values[i], i, db, Category);
                    }
                    else
                    {
                        dna.Add(db.Names[i], db.Values[i]);
                    }
                }
            }
            return dna;
        }


        /// <summary>
        /// Get all of the DNA for the current character, and return it as a list of DnaSetters.
        /// Each DnaSetter will track the DNABase that it came from, and the character that it is attached
        /// to. To modify the DNA on the character, use the Set function on the Setter.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, DnaSetter> GetDNA(UMAData.UMARecipe recipe = null)
        {
            Dictionary<string, DnaSetter> dna = new Dictionary<string, DnaSetter>();

            if (activeRace == null)
            {
                Debug.Log($"No race set getting default race DNA on character {gameObject.name}");
                return dna;
            }

            if (activeRace.racedata == null)
            {
                activeRace.SetRaceData();
            }
            if (umaData == null)
            {
                return dna;
            }

            UMADnaBase[] dnaBase = umaData.GetAllDna();

            if (recipe == null)
            {
                dnaBase = umaData.GetAllDna();
            }
            else
            {
                dnaBase = recipe.GetAllDna();
            }

            for (int i1 = 0; i1 < dnaBase.Length; i1++)
            {
                UMADnaBase db = dnaBase[i1];
                string Category = db.GetType().ToString();

                //TODO racedata.GetConverter is obsolete because lots of converters can use the same dna names (dnaAsset) now 
                //I'm just gonna use the first found one- we can do something more advanced if/when we need to
                IDNAConverter[] dcb = activeRace.racedata.GetConverters(db);
                if (dcb.Length > 0 && dcb[0] != null && (!string.IsNullOrEmpty(dcb[0].DisplayValue)))
                {
                    Category = dcb[0].DisplayValue;
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

        public void SetDNA(string DNAName, float value, bool rebuild = false)
        {

            var dna = GetDNA();
            if (dna.ContainsKey(DNAName))
            {
                dna[DNAName].Set(value);
            }
            if (rebuild)
            { 
                ForceUpdate(true); 
            }
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
            {
                thisExpressionPlayer = gameObject.AddComponent<UMAExpressionPlayer>();
            }

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

        private void InitializeExpressionPlayer(bool enable = true)
        {
            var thisExpressionPlayer = gameObject.GetComponent<UMAExpressionPlayer>();
            if (thisExpressionPlayer == null)
            {
                return;
            }
            //turn this off if we are not Humanoid cos it wont work
            if (umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid)
            {
                if (thisExpressionPlayer.expressionSet == null)
                {
                    return;
                }

                thisExpressionPlayer.enabled = true;
                thisExpressionPlayer.Initialize();
            }
            else
            {
                thisExpressionPlayer.enabled = false;
            }
        }

        /// <summary>
        /// Sets the Animator Controller for the Avatar based on the best match found for the Avatars race. If no animator for the active race has explicitly been set, the default animator is used
        /// </summary>
        public void SetAnimatorController(bool addAnimator = false)
        {
            // Somehow we can get here while Unity Addressables is instantiating an object, before it is constructed.
            if (this == null)
            {
                return;
            }

            if (KeepAnimatorController == true && animationController != null)
            {
                return;
            }

            if (activeRace == null)
            {
                return;
            }

            RuntimeAnimatorController controllerToUse = raceAnimationControllers.GetAnimatorForRace(activeRace.name);

            if (controllerToUse == null)
            {
                List<string> compat = activeRace.data.GetCrossCompatibleRaces();
                for (int i = 0; i < compat.Count; i++)
                {
                    string s = compat[i];
                    controllerToUse = raceAnimationControllers.GetAnimatorForRace(s);
                    if (controllerToUse)
                    {
                        break;
                    }
                }
                if (controllerToUse == null)
                {
#if UNITY_DEBUG
                    Debug.LogError("Unable to find animator! This will not be good.");
#endif
                }
            }
            //changing the animationController in 5.6 resets the rotation of this game object
            //so store the rotation and set it back
            var originalRot = Quaternion.identity;
            animationController = controllerToUse;

            if (umaData != null)
            {
                originalRot = umaData.transform.localRotation;
            }

            var thisAnimator = gameObject.GetComponent<Animator>();

            if (RecreateAnimatorOnRaceChange)
            {
                GameObject.DestroyImmediate(thisAnimator);
                // todo.. this sometimes blows up with the component already exists!!!
                thisAnimator = gameObject.AddComponent<Animator>();
            }

            if (controllerToUse != null)
            {
                if (thisAnimator == null && addAnimator)
                {
                    thisAnimator = gameObject.AddComponent<Animator>();
                }

                if (thisAnimator != null)
                {
                    thisAnimator.runtimeAnimatorController = controllerToUse;
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
            {
                umaData.transform.localRotation = originalRot;
                umaData.animationController = thisAnimator.runtimeAnimatorController;
            }
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
                {
                    thisDCASA |= SaveOptions.saveDNA;
                }

                if (saveWardrobe)
                {
                    thisDCASA |= SaveOptions.saveWardrobe;
                }

                if (saveColors)
                {
                    thisDCASA |= SaveOptions.saveColors;
                }
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
            {
                thisSaveOpts |= SaveOptions.saveColors;
            }

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
            {
                EnsureSharedColors();
            }

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
        public string GetCurrentRecipe(bool backwardsCompatible = false)
        {
            Dictionary<string, UMATextRecipe> wardrobeCache = new Dictionary<string, UMATextRecipe>(_wardrobeRecipes);
            Dictionary<string, UMAWardrobeCollection> wcCache = new Dictionary<string, UMAWardrobeCollection>(_wardrobeCollections);
            var prevSharedColors = umaData.umaRecipe.sharedColors;//not sure if this is gonna work
            if (ensureSharedColors)//we dont want to keep the colors in the recipe though (otherwise effectively ensureSharedColors is going to be true hereafter)
            {
                EnsureSharedColors();
            }

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
            {
                EnsureSharedColors();
            }

            ClearWardrobeCollectionsRecipes(true);
            string extension = saveAsAsset ? "asset" : "txt";
            var origSaveType = savePathType;
            if (saveAsAsset)
            {
                savePathType = savePathTypes.FileSystem;
            }

            if (filePath == "")
            {
                filePath = GetSavePath(extension);
            }

            savePathType = origSaveType;
            if (filePath != "")
            {
                //var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
                var asset = ScriptableObject.CreateInstance<UMADynamicCharacterAvatarRecipe>();
                var recipeName = saveFilename != "" ? saveFilename : gameObject.name + "_DCSRecipe";
                asset.SaveDCS(this, recipeName, saveOptionsToUse);
#if UNITY_EDITOR
                if (!saveAsAsset)
                {
                    FileUtils.WriteAllText(filePath, asset.recipeString);
                }
                else
                {
                    asset.recipeType = "DynamicCharacterAvatar";

                    AssetDatabase.CreateAsset(asset, filePath);
                    AssetDatabase.SaveAssets();
                }
#else
                FileUtils.WriteAllText(filePath, asset.recipeString);
#endif
                if (Debug.isDebugBuild)
                {
                    Debug.Log("Recipe saved to " + filePath);
                }

                if (savePathType == savePathTypes.Resources)
                {
#if UNITY_EDITOR
                    AssetDatabase.Refresh();
#endif
                }
                if (!saveAsAsset)
                {
                    UMAUtils.DestroySceneObject(asset);
                }
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
                    {
                        return "";//user cancelled save
                    }
                    else
                    {
                        saveFilename = Path.GetFileNameWithoutExtension(path);
                    }
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
                if (Debug.isDebugBuild)
                {
                    Debug.LogError("CharacterSystem Save Error! Could not save file, check you have set the filename and path correctly...");
                }

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
                {
                    thisDCALO |= LoadOptions.loadRace;
                }

                if (loadDNA)
                {
                    thisDCALO |= LoadOptions.loadDNA;
                }

                if (loadWardrobe)
                {
                    thisDCALO |= LoadOptions.loadWardrobe;
                }

                if (loadBodyColors)
                {
                    thisDCALO |= LoadOptions.loadBodyColors;
                }

                if (loadWardrobeColors)
                {
                    thisDCALO |= LoadOptions.loadWardrobeColors;
                }

                thisDCALO &= ~LoadOptions.useDefaults;

                return thisDCALO;
            }
        }

        #region PARTIAL IMPORT - HelperMethods

        //DOS 11012017 changed the following so that they dont load race- if you want to load the race call LoadFromRecipeString directly with the appropriate flags
        public void LoadWardrobeFromRecipeString(string recipeString, bool loadColors = true, bool clearExisting = false)
        {
            if (clearExisting)
            {
                ClearSlots();
            }

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

        public void InitializeAvatar()
        {
            Initialize();
            umaData.OnCharacterBegun += this.SetAndSaveOverrideDNA;
            umaData.OnCharacterDnaUpdated += this.RestoreOverrideDna;
        }

        /// <summary>
        /// Sets the recipe string that will be loaded when the Avatar starts. If trying to load a recipe after the character has been created use 'LoadFromRecipeString'
        /// [DEPRICATED] Please use SetLoadString instead, this will work regardless of whether the character has been created or not.
        /// </summary>
        /// <param name="Recipe"></param>
        public void Preload(string Recipe)
        {
            if (Debug.isDebugBuild)
            {
                Debug.LogWarning("DEPRICATED please use SetLoadString instead");
            }

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
        /// <param name="filename"></param>
        /// <param name="newLoadPathType"></param>
        public void SetLoadFilename(string filename, loadPathTypes newLoadPathType)
        {
            loadFilename = filename;
            loadPathType = newLoadPathType;
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
                if (Debug.isDebugBuild)
                {
                    Debug.LogError("The assigned UMATextRecipe was a Wardrobe Recipe. You cannot load a character from a Wardrobe Recipe");
                }

                return;
            }
            ImportSettings(UMATextRecipe.PackedLoadDCS((settingsToLoad as UMATextRecipe).recipeString), customLoadOptions);
        }
        /// <summary>
        /// Load settings from an existing recipe string, optionally customizing what is loaded from the recipe
        /// </summary>
        /// <param name="settingsToLoad"></param>
        /// <param name="customLoadOptions"></param>
        public void LoadFromRecipeString(string settingsToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults, bool ClearWardrobe = false)
        {
            if (ClearWardrobe)
            {
                this._wardrobeRecipes.Clear();
            }
            ImportSettings(UMATextRecipe.PackedLoadDCS(settingsToLoad), customLoadOptions);
        }

        bool ImportSettings(UMATextRecipe.DCSUniversalPackRecipe settingsToLoad, LoadOptions customLoadOptions = LoadOptions.useDefaults, bool forceDCSLoad = false)
        {
            var thisLoadOptions = customLoadOptions == LoadOptions.useDefaults ? defaultLoadOptions : customLoadOptions;
            //When ChangeRace calls this, it calls it with forceDCSLoad to be true so we need settingsToLoad.wardrobeSet fixed if its null
            if (forceDCSLoad)
            {
                if (settingsToLoad.wardrobeSet == null)
                {
                    settingsToLoad.wardrobeSet = new List<WardrobeSettings>();
                }
            }
            //we only want to know the value of BuildCharacter at the time this recipe was sent- not what it may have been changed to over the process of the coroutine
            var wasBuildCharacterEnabled = _buildCharacterEnabled;
            _isFirstSettingsBuild = false;
            var prevDna = new UMADnaBase[0];

            if (umaData == null)
            {
                InitializeAvatar();

            }
            if ((!thisLoadOptions.HasFlagSet(LoadOptions.loadDNA) || settingsToLoad.packedDna.Count == 0) && activeRace.racedata != null)
            {
                prevDna = umaData.umaRecipe.GetAllDna();
            }
            if (thisLoadOptions.HasFlagSet(LoadOptions.loadRace))
            {
                if (settingsToLoad.race == null || settingsToLoad.race == "")
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.LogError("The sent recipe did not have an assigned Race. Avatar could not be created from the recipe");
                    }

                    return false;
                }
                activeRace.name = settingsToLoad.race;
                SetActiveRace();
                //If the UmaRecipe is still after that null, bail - we cant go any further (and SetStartingRace will have shown an error)
                if (umaRecipe == null)
                {
                    return false;
                }
            }
            //this will be null for old UMA recipes without any wardrobe
            if (settingsToLoad.wardrobeSet != null)
            {
                //if we are loading wardrobe override everything that was previously set (by the default wardrobe or any previous user modifications)
                //sending an empty wardrobe set will clear the current wardrobe. If preLoadDefaultWardrobe is true and the wardrobe is empty LoadDefaultWardrobe gets called
                if (thisLoadOptions.HasFlagSet(LoadOptions.loadWardrobe))//sending an empty wardrobe set will clear the current wardrobe
                {
                    _buildCharacterEnabled = false;
                    LoadWardrobeSet(settingsToLoad.wardrobeSet);
                    if (_wardrobeRecipes.Count == 0)
                    {
                        if (preloadWardrobeRecipes.loadDefaultRecipes)
                        {
                            LoadDefaultWardrobe();
                        }

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
                    SetExpressionSet();
                }
                //update any wardrobe collections so if they are no longer active they dont show as active
                //UpdateWardrobeCollections();
                //Sort out colors
                umaData.umaRecipe.sharedColors = ImportSharedColors(settingsToLoad.sharedColors, thisLoadOptions);
                UpdateColors();//updateColors is called by LoadCharacter which is called by BuildCharacter- but we may not be Building

                // TODO: this was moved to after the DNA was added.
                //       I'm not sure how it worked before. Still
                //       I'm leaving this here so later on if something happens
                //       because of this, we'll know where it was.
                //if (wasBuildCharacterEnabled)
                //{
                //    BuildCharacter(false,!BundleCheck);
                //}
                //
                if (thisLoadOptions.HasFlagSet(LoadOptions.loadDNA) && settingsToLoad.packedDna.Count > 0)
                {
                    umaData.umaRecipe.ClearDna();
                    UMADnaBase[] array = settingsToLoad.GetAllDna();
                    for (int i = 0; i < array.Length; i++)
                    {
                        UMADnaBase dna = array[i];
                        umaData.umaRecipe.AddDna(dna);
                    }
                }
                else if (prevDna.Length > 0)
                {
                    TryImportDNAValues(prevDna);
                }

                // This was before the DNA was set. I'm 
                // not sure how it worked that way.
                if (wasBuildCharacterEnabled)
                {
                    BuildCharacter(true, !BundleCheck);
                }

                if (cacheCurrentState)
                {
                    AddCharacterStateCache();
                }
            }
            else
            {
                ImportOldUma(settingsToLoad, thisLoadOptions, wasBuildCharacterEnabled);
            }
            return true;
        }
        /// <summary>
        /// Do not call this directly use LoadFromRecipe(yourOldUMArecipe instead)
        /// </summary>
        /// <param name="settingsToLoad"></param>
        /// <param name="thisLoadOptions"></param>
        /// <param name="wasBuildCharacterEnabled"></param>
        /// <returns></returns>
        void ImportOldUma(UMATextRecipe.DCSUniversalPackRecipe settingsToLoad, LoadOptions thisLoadOptions, bool wasBuildCharacterEnabled = true)
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
            UMATextRecipe.UnpackRecipe(umaData.umaRecipe, settingsToLoad);
            //
            ClearSlots();//old umas dont have any wardrobe
            SetAnimatorController(true);
            SetExpressionSet();
            //shared colors
            umaData.umaRecipe.sharedColors = ImportSharedColors(settingsToLoad.sharedColors, thisLoadOptions);
            UpdateColors();
            //additionalRecipes
            umaData.AddAdditionalRecipes(umaAdditionalRecipes, false);
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
        }

        /// <summary>
        /// Loads the text file in the loadFilename field to get its recipe string, and then calls   to to process the recipe and load the Avatar.
        /// </summary>
        public void DoLoad()
        {
            GetRecipeStringToLoad();
        }

        public void LoadFromAssetFile(string Name)
        {
            UMAAssetIndexer UAI = UMAAssetIndexer.Instance;
            AssetItem ai = UAI.GetAssetItem<UMATextRecipe>(Name.Trim());
            if (ai != null)
            {
                string recipeString = (ai.Item as UMATextRecipe).recipeString;
                LoadFromRecipeString(recipeString);
                return;
            }
            if (Debug.isDebugBuild)
            {
                Debug.LogWarning("Asset '" + Name + "' Not found in Global Index");
            }
        }

        public void LoadFromTextFile(string Name)
        {
            UMAAssetIndexer UAI = UMAAssetIndexer.Instance;
            AssetItem ai = UAI.GetAssetItem<TextAsset>(Name.Trim());
            if (ai != null)
            {
                string recipeString = (ai.Item as TextAsset).text;
                LoadFromRecipeString(recipeString);
                return;
            }
            if (Debug.isDebugBuild)
            {
                Debug.LogWarning("Asset '" + Name + "' Not found in Global Index");
            }
        }

        void GetRecipeStringToLoad()
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
                    else
                    {
                        recipeString = UMAAssetIndexer.Instance.GetCharacterRecipe(loadFilename.Trim());
                    }
                }
            }
            if (loadPathType == loadPathTypes.FileSystem)
            {
#if UNITY_EDITOR
                if (Application.isEditor)
                {
                    path = EditorUtility.OpenFilePanel("Load saved Avatar", Application.dataPath, "txt");
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }
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
                        if (Debug.isDebugBuild)
                        {
                            Debug.LogWarning("[CharacterAvatar.DoLoad] No filename specified to load!");
                        }

                        BuildFromComponentSettings();
                        return;
                    }
                    else
                    {
                        if (path.Contains("://"))
                        {
                            StartCoroutine(DoWebLoad(path));
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
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("[CharacterAvatar.DoLoad] No TextRecipe found with filename " + loadFilename);
                }

                BuildFromComponentSettings();
            }
        }

        IEnumerator DoWebLoad(string path)
        {
            UnityWebRequest www = UnityWebRequest.Get(path + loadFilename);
#if UNITY_2017_2_OR_NEWER
            yield return www.SendWebRequest();
#else
            yield return www.Send();
#endif
            LoadFromRecipeString(www.downloadHandler.text);
        }
        #endregion
        #endregion

        #region CHARACTER FINAL ASSEMBLY

        public bool DNAIsValid(UMADnaBase[] CurrentDNA)
        {
            if (CurrentDNA == null)
            {
                return false;
            }

            if (CurrentDNA.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < CurrentDNA.Length; i++)
            {
                UMADnaBase dna = CurrentDNA[i];
                if (dna.Values == null)
                {
                    return false;
                }
            }
            return true;
        }

        public UMATextRecipe[] GetVisibleWearables()
        {
            List<UMATextRecipe> visibleWearables = new List<UMATextRecipe>();
            foreach (UMATextRecipe utr in WardrobeRecipes.Values)
            {
                if (utr.wardrobeSlot != "")
                {
                    visibleWearables.Add(utr);
                }
            }
            return visibleWearables.ToArray();
        }


        /// <summary>
        /// Builds the character by combining the Avatar's raceData.baseRecipe with the any wardrobe recipes that have been applied to the avatar.
        /// </summary>
        /// <returns>Can also be used to return an array of additional slots if this avatars flagForReload field is set to true before calling</returns>
        /// <param name="RestoreDNA">If updating the same race set this to true to restore the current DNA.</param>
        public void BuildCharacter(bool RestoreDNA = true, bool skipBundleCheck = false, bool useBundleParameter = true)
        {
            //Debug.Log($"Buildcharacter {gameObject.name}");
            InitialStartup(); // This is to make sure that the UMAContext is set up correctly

            overrideDNA.Clear();

            if (activeRace.racedata == null)
            {
                activeRace.SetRaceData();
            }

            if (activeRace.racedata != null)
            {
                umaRecipe = activeRace.racedata.baseRaceRecipe;
            }

            List<string> HiddenSlots = new List<string>();//why was this HashSet list is faster for our purposes (http://stackoverflow.com/questions/150750/hashset-vs-list-performance)

            _isFirstSettingsBuild = false;
            //clear these values each time we build
            wasCrossCompatibleBuild = false;
            crossCompatibleRaces.Clear();

            // clear the hiddenslots and hidden mesh assets
            // so they can be accumulate anew from the recipe
            HiddenSlots.Clear();

            // MeshHideDictionary.Clear();
            Dictionary<string, List<MeshHideAsset>> MeshHideDictionary = new Dictionary<string, List<MeshHideAsset>>();

            UMADnaBase[] CurrentDNA = null;
            if (umaData != null)
            {
                if (umaData.umaRecipe != null)
                {
                    CurrentDNA = umaData.umaRecipe.GetDefinedDna();
                }
                umaData.userInformation = userInformation;
                SetUMADataOptions();
                umaData.ClearModifiers();
            }
            if (DNAIsValid(CurrentDNA) == false)
            {
                RestoreDNA = false;
            }

            if (BuildCharacterBegun != null)
            {
                BuildCharacterBegun.Invoke(umaData);
            }

            List<UMAWardrobeRecipe> ReplaceRecipes = new List<UMAWardrobeRecipe>();
            List<UMARecipeBase> Recipes = new List<UMARecipeBase>();
            List<string> SuppressSlotsStrings = new List<string>(forceSuppressedWardrobeSlots);
            List<string> HideTags = new List<string>();

            if ((WardrobeRecipes.Count > 0) && activeRace.racedata != null)
            {
                foreach (UMATextRecipe utr in WardrobeRecipes.Values)
                {
					if (utr.disabled) { 
						continue; 
					}


                    if (utr.suppressWardrobeSlots != null)
                    {
                        if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
                        {
                            if (!SuppressSlotsStrings.Contains(utr.wardrobeSlot))
                            {
                                for (int i = 0; i < utr.suppressWardrobeSlots.Count; i++)
                                {
                                    string suppressedSlot = utr.suppressWardrobeSlots[i];
                                    SuppressSlotsStrings.Add(suppressedSlot);
                                }
                            }
                        }
                    }
                }

                List<UMATextRecipe> allRecipes = new List<UMATextRecipe>(WardrobeRecipes.Values);

                if (_additiveRecipes != null && AdditiveRecipes.Count > 0)
                {
                    foreach (List<UMATextRecipe> addlRecipes in _additiveRecipes.Values)
                    {
                        allRecipes.AddRange(addlRecipes);
                    }
                }


                for (int i = 0; i < allRecipes.Count; i++)
                {
                    UMATextRecipe utr = allRecipes[i];
					if(utr.disabled) {
						continue;
					}
					// don't gather hides from suppresed slots...
                    if (SuppressSlotsStrings.Contains(utr.wardrobeSlot))
                    {
                        continue;
                    }

                    // add the Override DNA here.
                    if (utr.OverrideDNA != null && utr.OverrideDNA.Count > 0)
                    {
                        overrideDNA.AddRange(utr.OverrideDNA);
                    }

                    //Collect all HideTags
                    if (utr.HideTags.Count > 0)
                    {
                        HideTags.AddRange(utr.HideTags);
                    }
                    //Collect all the MeshHideAssets on all the wardrobe recipes
                    if (utr.MeshHideAssets != null)// && !SuppressSlotsStrings.Contains(utr.wardrobeSlot))
                    {
                        for (int i1 = 0; i1 < utr.MeshHideAssets.Count; i1++)
                        {
                            MeshHideAsset meshHide = utr.MeshHideAssets[i1];
                            if (meshHide != null)
                            {
                                if (!MeshHideDictionary.ContainsKey(meshHide.AssetSlotName))
                                {   //If this meshHide.asset isn't already in the dictionary, then let's add it and start a new list.
                                    MeshHideDictionary.Add(meshHide.AssetSlotName, new List<MeshHideAsset>());
                                }

                                //If this meshHide.asset is already in the dictionary AND the meshHide isn't already in the list, then add it.
                                if (!MeshHideDictionary[meshHide.AssetSlotName].Contains(meshHide))
                                {
                                    MeshHideDictionary[meshHide.AssetSlotName].Add(meshHide);
                                }
                            }
                        }
                    }
                }

                SuppressedRecipes.Clear();
                for (int i = 0; i < activeRace.racedata.wardrobeSlots.Count; i++)//this doesn't need to validate racedata- we wouldn't be here if it was null
                {
                    string ws = activeRace.racedata.wardrobeSlots[i];
                    if (SuppressSlotsStrings.Contains(ws))
                    {
                        if (WardrobeRecipes.ContainsKey(ws))
                        {
                            SuppressedRecipes.Add(WardrobeRecipes[ws]);
                        }
                        continue;
                    }
                    if (WardrobeRecipes.ContainsKey(ws))
                    {
                        UMATextRecipe utr = WardrobeRecipes[ws];
                        if (utr.disabled) { continue; }
                        //we can use the race data here to filter wardrobe slots
                        //if checking a backwards compatible race we also need to check the race has a compatible wardrobe slot, 
                        //since while a race can be backwards compatible it does not *have* to have all the same wardrobeslots as the race it is compatible with
                        if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
                        {
                            UMAWardrobeRecipe umr = (utr as UMAWardrobeRecipe);
                            if (umr != null)
                            {
                                if (umr.MeshModifiers != null)
                                {
                                    for (int i1 = 0; i1 < umr.MeshModifiers.Count; i1++)
                                    {
                                        umaData.AddMeshModifiers(umr.MeshModifiers[i1].Modifiers);
                                    }
                                }
                            }

                            //check if this recipe is directly or only cross compatible
                            bool utrIsCrossCompatible = (activeRace.racedata.IsCrossCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot));
                            if (utrIsCrossCompatible)
                            {
                                //FixCrossCompatibleSlots will be called to remove any slots added by cross compatible recipes that are 'equivalent' according to this races 'Cross Compatibility' settings
                                wasCrossCompatibleBuild = true;
                                for (int ccri = 0; ccri < utr.compatibleRaces.Count; ccri++)
                                {
                                    if (!crossCompatibleRaces.Contains(utr.compatibleRaces[ccri]))
                                    {
                                        crossCompatibleRaces.Add(utr.compatibleRaces[ccri]);
                                    }
                                }
                            }

                            if (umr != null && umr.HasReplaces)
                            {
                                ReplaceRecipes.Add(umr);
                            }
                            else
                            {
                                if (!utr.disabled)
                                {
                                    Recipes.Add(utr);
                                }
                            }
                            if (utr.Hides.Count > 0)
                            {
                                for (int i1 = 0; i1 < utr.Hides.Count; i1++)
                                {
                                    string s = utr.Hides[i1];
                                    HiddenSlots.Add(s);
                                    //if the current race is only 'CrossCompatible' with the races this recipe is compatible with
                                    //get the equivalent slot from the races crossCompatibility settings so that is hidden too;
                                    if (utrIsCrossCompatible)
                                    {
                                        var equivalentSlot = activeRace.racedata.FindEquivalentSlot(utr.compatibleRaces, s, false);
                                        if (!string.IsNullOrEmpty(equivalentSlot))
                                        {
                                            HiddenSlots.Add(equivalentSlot);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (AdditiveRecipes.ContainsKey(ws))
                    {
                        // must check additive after wardrobe
                        Recipes.AddRange(AdditiveRecipes[ws]);
                    }
                }
            }

            if (umaAdditionalRecipes != null)
            {
                for (int i = 0; i < umaAdditionalRecipes.Length; i++)
                {
                    UMATextRecipe utr = (UMATextRecipe)umaAdditionalRecipes[i];
                    if (utr)
                    {
                        if (utr.disabled) { continue; }
                        if (utr.Hides.Count > 0)
                        {
                            for (int i1 = 0; i1 < utr.Hides.Count; i1++)
                            {
                                string s = utr.Hides[i1];
                                HiddenSlots.Add(s);
                            }
                        }
                    }
                }
            }

            foreach (string s in forceRemovedBaseSlots)
            {
                HiddenSlots.Add(s);
            }

            foreach(string s in forceRemovedTags)
            {
                HideTags.Add(s);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                skipBundleCheck = true;
            }
            else
            {
                if (useBundleParameter)
                {
                    skipBundleCheck = !BundleCheck;
                }
            }

#else
                if (useBundleParameter)
                    skipBundleCheck = !BundleCheck;
#endif
            LoadCharacter(umaRecipe, ReplaceRecipes, Recipes, umaAdditionalRecipes, MeshHideDictionary, HiddenSlots, HideTags, CurrentDNA, RestoreDNA, skipBundleCheck);
        }

        public void SetAndSaveOverrideDNA(UMAData udata)
        {
            savedDNA.Clear();
            if (overrideDNA.Count > 0)
            {
                // save the override DNA.
                // set the new DNA
                var currentDNA = GetDNA();

                for (int i = 0; i < overrideDNA.PreloadValues.Count; i++)
                {
                    DnaValue d = overrideDNA.PreloadValues[i];
                    if (currentDNA.ContainsKey(d.Name))
                    {
                        // in case it ends up being added twice somehow, protect from overwriting
                        // the original value.
                        if (!savedDNA.ContainsName(d.Name))
                        {
                            savedDNA.AddDNA(d.Name, currentDNA[d.Name].Value);
                            currentDNA[d.Name].Set(d.Value);
                        }
                    }
                }
            }
        }

        public void RestoreOverrideDna(UMAData udata)
        {
            if (savedDNA.Count > 0)
            {
                var currentDNA = GetDNA();
                for (int i = 0; i < savedDNA.PreloadValues.Count; i++)
                {
                    DnaValue d = savedDNA.PreloadValues[i];
                    if (currentDNA.ContainsKey(d.Name))
                    {
                        currentDNA[d.Name].Set(d.Value);
                    }
                }
            }
        }

#if UMA_ADDRESSABLES
        private class BuildSave
        {
            public UMARecipeBase _umaRecipe;
            public List<UMAWardrobeRecipe> _Replaces;
            public List<UMARecipeBase> _umaAdditionalSerializedRecipes;
            public UMARecipeBase[] _AdditionalRecipes;
            public List<string> _hiddenSlots;
            public bool _restoreDNA;
            public UMADnaBase[] _currentDNA;
            public Dictionary<string, List<MeshHideAsset>> _MeshHideDictionary;
            public List<string> _HideTags;

            public BuildSave(UMARecipeBase umaRecipe, List<UMAWardrobeRecipe> Replaces, List<UMARecipeBase> umaAdditionalSerializedRecipes, UMARecipeBase[] AdditionalRecipes, Dictionary<string, List<MeshHideAsset>> MeshHideDictionary, List<string> hiddenSlots, List<string> HideTags, UMADnaBase[] CurrentDNA, bool restoreDNA)
            {
                _umaRecipe = umaRecipe;
                _Replaces = Replaces;
                _umaAdditionalSerializedRecipes = umaAdditionalSerializedRecipes;
                _hiddenSlots = hiddenSlots;
                _restoreDNA = restoreDNA;
                _currentDNA = CurrentDNA;
                _AdditionalRecipes = AdditionalRecipes;
                _MeshHideDictionary = MeshHideDictionary;
                _HideTags = HideTags;
            }
        }

        Dictionary<AsyncOp, BuildSave> LoadQueue = new Dictionary<AsyncOp, BuildSave>();


        public bool AddressableBuildPending
        {
            get
            {
                return LoadQueue.Count > 0;
            }
        }

        private void LoadWhenReady(AsyncOp Op)
        {
            try
            {
                if (Op.IsDone)
                {
                    BuildSave bs = LoadQueue[Op];
                    LoadCharacter(bs._umaRecipe, bs._Replaces, bs._umaAdditionalSerializedRecipes,bs._AdditionalRecipes, bs._MeshHideDictionary, bs._hiddenSlots,bs._HideTags, bs._currentDNA, bs._restoreDNA, true);
                    LoadQueue.Remove(Op);
                    if (LoadedHandles.Count > 1)
                    {
                        AsyncOp OldOp = LoadedHandles.Dequeue();
                        if (gameObject.activeInHierarchy && DelayUnload > 0.0f)  
                        {
                            StartCoroutine(CleanupAfterDelay(OldOp));
                        }
                        else
                        {
                            UnloadOldestQueuedHandle(OldOp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }
        private void UnloadOldestQueuedHandle(AsyncOp Op)
        {
            if (Op.IsValid())
            {
                // Todo: Should we call AssetIndexer.Instance.Unload(Op) instead?
                //       Unity seems to handle this OK with it's internal reference counting.
                UnityEngine.AddressableAssets.Addressables.Release(Op);
            }
        }

        /// <summary>
        /// This function will delay the unload
        /// </summary>
        /// <returns></returns>
        IEnumerator CleanupAfterDelay(AsyncOp Op)
        {
            yield return new WaitForSeconds(DelayUnload);
            UnloadOldestQueuedHandle(Op);
        } 
#endif
        private void ApplyPredefinedDNA()
        {
            if (this.predefinedDNA != null)
            {
                if (this.predefinedDNA.PreloadValues.Count > 0)
                {
                    var dna = GetDNA();

                    for (int i = 0; i < predefinedDNA.PreloadValues.Count; i++)
                    {
                        DnaValue dv = predefinedDNA.PreloadValues[i];
                        if (dna.ContainsKey(dv.Name))
                        {
                            dna[dv.Name].Set(dv.Value);
                        }
                    }
                    // Use this when you are managing DNA outside of UMA, and rebuild the character over and over.
                    if (!keepPredefinedDNA)
                    {
                    this.predefinedDNA.Clear();
                }
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
            if (Debug.isDebugBuild)
            {
                Debug.Log(" With a DynamicCharacterAvatar you do not call Load directly. If you want to load an UMATextRecipe directly call ImportSettings(yourUMATextRecipe)");
            }

            return;
        }
        /// <summary>
        /// Loads the Avatar from the given recipe and additional recipe. 
        /// Has additional functions for removing any slots that should be hidden by any 'wardrobe Recipes' that are in the additional recipes array.
        /// </summary>
        /// <param name="umaRecipe"></param>
        /// <param name="Replaces"></param>
        /// <param name="umaAdditionalSerializedRecipes"></param>
        /// <returns>Returns true if the final recipe load caused more assets to download</returns>
        private void LoadCharacter(UMARecipeBase umaRecipe, List<UMAWardrobeRecipe> Replaces, List<UMARecipeBase> umaAdditionalSerializedRecipes, UMARecipeBase[] AdditionalRecipes, Dictionary<string, List<MeshHideAsset>> MeshHideDictionary, List<string> hiddenSlots, List<string> HideTags, UMADnaBase[] CurrentDNA, bool restoreDNA, bool skipBundleCheck)
        {
#if UMA_ADDRESSABLES
#if UNITY_EDITOR
            // If we are in the editor, and we don't want to look & load bundles, just go ahead.
            if (umaGenerator != null && umaGenerator.skipAddressableRecipeLookupInEditor)
            {
                skipBundleCheck = true;
            }
#endif
            if (!skipBundleCheck)
            {
                /* Load every recipe into a class and save it         */
                /* Stick it in a dictionary, keyed on the AsyncOp     */
                /* So we can look it up when LoadWhenReady is called. */
                /* LoadWhenReady will plug in the saved values        */
                /* and call this function again, telling it to skip   */
                /* bundle checking                                    */

                var theOp = UMAAssetIndexer.Instance.Preload(this);
                LoadedHandles.Enqueue(theOp);
                LoadQueue.Add(theOp,new BuildSave( umaRecipe,Replaces,umaAdditionalSerializedRecipes,AdditionalRecipes, MeshHideDictionary, hiddenSlots, HideTags, CurrentDNA, restoreDNA));
                theOp.Completed += LoadWhenReady;
#if SUPER_LOGGING
                Debug.Log("LoadCharacter waiting for preload...");
#endif
                return;
            }
#endif
            // In the unlikely event the avatar has been destroyed in the few frames before this is loaded.
            if (this == null)
            {
                return;
            }
            //set the expression set to match the new character- needs to happen before load...
            if (activeRace.racedata != null && !restoreDNA)
            {
                SetAnimatorController(true);
                SetExpressionSet();
            }

#if SUPER_LOGGING
                Debug.Log("Load Character: " + gameObject.name);
#endif
            if (umaData == null)
            {
                InitializeAvatar();
            }
            SetUMADataOptions();
            umaData.defaultRendererAsset = defaultRendererAsset;
            umaData.blendShapeSettings.ignoreBlendShapes = !loadBlendShapes;
            umaData.atlasResolutionScale = this.AtlasResolutionScale;
            umaData.hideRenderers = this.hide;

            //set the umaData.animator if we have an animator already
            if (this.gameObject.GetComponent<Animator>())
            {
                umaData.animator = this.gameObject.GetComponent<Animator>();
            }

            this.umaRecipe = umaRecipe; //??? This seems to be pulling the recipe from the character, and then resetting it to itself.

            umaRecipe.Load(umaData.umaRecipe);
            umaData.umaRecipe.SetRace(this.activeRace.racedata); // JRRM Test
            umaData.umaRecipe.MeshHideDictionary = MeshHideDictionary;

            umaData.AddAdditionalRecipes(AdditionalRecipes, false);
            if (umaAdditionalSerializedRecipes != null)
            {
                AddAdditionalSerializedRecipes(umaAdditionalSerializedRecipes);
            }

            //not sure if we do this first or not
            if (wasCrossCompatibleBuild)
            {
                FixCrossCompatibleSlots(hiddenSlots);
            }

            // Wildcard Slots -- renamed
            PostProcessSlots(hiddenSlots, HideTags);

            for (int i = 0; i < Replaces.Count; i++)
            {
                UMAWardrobeRecipe umr = Replaces[i];
                ReplaceSlot(umr);
            }

            // Send wardrobe slots in LoadCharacter so we can be sure that the slots are loaded.
            if (SuppressedRecipes.Count > 0 && WardrobeSuppressed != null)
            {
                WardrobeSuppressed.Invoke(SuppressedRecipes);
            }

            List<SlotData> smooshSlots = new List<SlotData>();
            List<SlotData> clippingPlanes = new List<SlotData>();


            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData sd = umaData.umaRecipe.slotDataList[i];
                if (sd.OverlayCount > 1)
                {
                    List<OverlayData> Overlays = sd.GetOverlayList();
                    List<OverlayData> SortedOverlays = new List<OverlayData>(Overlays.Count);

                    for (int i1 = 0; i1 < Overlays.Count; i1++)
                    {
                        OverlayData od = Overlays[i1];
                        if (od.asset.overlayType == OverlayDataAsset.OverlayType.Cutout)
                        {
                            continue;
                        }
                        SortedOverlays.Add(od);
                    }

                    for (int i1 = 0; i1 < Overlays.Count; i1++)
                    {
                        OverlayData od = Overlays[i1];
                        if (od.asset.overlayType == OverlayDataAsset.OverlayType.Cutout)
                        {
                            SortedOverlays.Add(od);
                        }
                    }
                    sd.altMaterial = SortedOverlays[0].asset.material;
                    sd.UpdateOverlayList(SortedOverlays);
                }
                if (sd.asset.isClippingPlane)
                {
                    clippingPlanes.Add(sd);
                }
                if (sd.asset.isSmooshable)
                {
                    smooshSlots.Add(sd);
                    if (umaData.VertexOverrides.ContainsKey(sd.slotName))
                    {
                        umaData.VertexOverrides.Remove(sd.slotName);
                    }
                }
            }

            // Loop through all planes, and then smoosh the smooshables that are targeted by the plane.
            for (int i = 0; i < clippingPlanes.Count; i++)
            {
                SlotData clipSlot = clippingPlanes[i];
                List<SlotDataAsset> SmooshThese = new List<SlotDataAsset>();
                SlotDataAsset SmooshTarget = null;

                string smooshTargetTag = clipSlot.smooshTargetTag;
                string smooshableTag = clipSlot.smooshableTag;

                if (string.IsNullOrEmpty(smooshTargetTag))
                {
                    smooshTargetTag = "Smooshtarget";
                }

                if (string.IsNullOrEmpty(smooshableTag))
                {
                    smooshableTag = "Smooshable";
                }

                List<SlotData> Smooshables = new List<SlotData>();

                for (int i1 = 0; i1 < umaData.umaRecipe.slotDataList.Length; i1++)
                {
                    SlotData lookSloot = umaData.umaRecipe.slotDataList[i1];
                    if (lookSloot.HasTag(smooshTargetTag))
                    {
                        SmooshTarget = lookSloot.asset;
                    }
                }

                for (int i1 = 0; i1 < smooshSlots.Count; i1++)
                {
                    SlotData lookSloot = smooshSlots[i1];
                    if (lookSloot.HasTag(smooshableTag))
                    {
                        SmooshThese.Add(lookSloot.asset);
                    }
                }

                if (SmooshTarget != null && SmooshThese.Count > 0)
                {
                    for (int i1 = 0; i1 < SmooshThese.Count; i1++)
                    {
                        SlotDataAsset smooshslot = SmooshThese[i1];
                        SmooshSlotPhysics(umaData, smooshslot, clipSlot.asset, SmooshTarget, clipSlot.smooshInvertX, clipSlot.smooshInvertY, clipSlot.smooshInvertZ, clipSlot.smooshInvertDist, clipSlot.smooshDistance, clipSlot.overSmoosh);
                    }
                }
            }

            UpdateColors();

            //New event that allows for tweaking the resulting recipe before the character is actually generated
            if (RecipeUpdated != null)
            {
                RecipeUpdated.Invoke(umaData);
            }

            if (umaRace != umaData.umaRecipe.raceData)
            {
                umaData.RebuildSkeleton = rebuildSkeleton;
                umaData.raceChanged = true;
                UpdateNewRace();
            }
            else
            {
                umaData.RebuildSkeleton = false;
                umaData.raceChanged = false;
                UpdateSameRace();
            }

			umaData.RebuildSkeleton = false;
            if (alwaysRebuildSkeleton)
            {
                umaData.RebuildSkeleton = true;
            }

            ApplyPredefinedDNA();
            umaData.rawAvatar = rawAvatar;
            umaData.KeepAvatar = keepAvatar;
            umaData.ForceRebindAnimator = forceRebindAnimator;
            //But the ExpressionPlayer needs to be Initialized AFTER Load
            if (activeRace.racedata != null && !restoreDNA)
            {
                if (CharacterUpdated != null)
                {
                    this.CharacterUpdated.AddListener(InitializeExpressionPlayer);
                }
            }

            // Add saved DNA
            if (restoreDNA)
            {
                umaData.umaRecipe.ClearDna();
                for (int i = 0; i < CurrentDNA.Length; i++)
                {
                    UMADnaBase ud = CurrentDNA[i];
                    umaData.umaRecipe.AddDna(ud);
                }
            }
            ApplyDNAToModifiers();
        }

        private void ApplyDNAToModifiers()
        {
            var modifiers = umaData.Modifiers;

            if (modifiers == null || modifiers.Count == 0)
            {
                return;
            }
            var DNA = GetDNA();

            foreach (var slot in umaData.umaRecipe.slotDataList)
            {
                if (slot.asset != null && slot.asset.meshData != null && modifiers.ContainsKey(slot.slotName))
                {
                    var slotModifiers = modifiers[slot.slotName];

                    foreach (var modifier in slotModifiers)
                    {
                        if (modifier != null && DNA.ContainsKey(modifier.DNAName))
                        {
                            modifier.Scale = DNA[modifier.DNAName].Value;
                        }
                    }
                }
            }
        }



        public Vector3 GetDestVertPhys(Vector3 originVertex, Vector3 center, float PlaneDist, SlotDataAsset SmooshTarget, PhysicsScene ps, int vertindex, float smooshDistance, float overSmoosh)
        {
            Vector3 result = new Vector3();
            if (overSmoosh == 0)
            {
                overSmoosh = 0.00001f;
            }

            result.Set(originVertex.x, originVertex.y, originVertex.z);

            Vector3 direction = (center - originVertex);
            float distance = direction.magnitude;


            if (ps.Raycast(originVertex, direction, out RaycastHit hit))//, 5.0f, -5, QueryTriggerInteraction.Ignore))
            {

                var facing = Vector3.Dot(hit.normal, direction);

                if (facing > 0)
            {
                    return result;
                } 

                // todo: offset by smoosh distance
                result.Set(hit.point.x, hit.point.y, hit.point.z);

                float vertdistance = (hit.point - originVertex).magnitude;
                if (vertdistance < distance)
                {
                    distance = vertdistance;
                    float newSmooshDistance = smooshDistance;
                    // Smooth smooshing
                    if (PlaneDist > 0.0f)
                    {
                        newSmooshDistance = smooshDistance + (smooshDistance * (PlaneDist / overSmoosh));
                    }
                    Vector3 newLocation = hit.point + (direction * (0 - newSmooshDistance));
                    result.Set(newLocation.x, newLocation.y, newLocation.z);
                }
                else
                {
                    result.Set(hit.point.x, hit.point.y, hit.point.z);
                }
            }

                return result;
        }

 
        // cache these
        private static Scene SmooshScene;
        // todo: Should cache these. Maybe in a dictionary by slot name?
        private static Dictionary<string, Mesh> SmooshTargets = new Dictionary<string, Mesh>();

        private static void CreateSmooshScene()
        {
            if (SmooshScene.IsValid())
            {
                CleanScene(SmooshScene);
            }
            else
            {
#if UNITY_EDITOR
                SmooshScene = EditorSceneManager.NewPreviewScene();
#else
            CreateSceneParameters csp = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
            SmooshScene = SceneManager.CreateScene("SmooshScene", csp);
#endif
            }
            }

        private static void CleanScene(Scene scene)
        {
            var rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject rootObject = rootObjects[i];
#if UNITY_EDITOR
                GameObject.DestroyImmediate(rootObject);
#else
                GameObject.Destroy(rootObject);
#endif
            }
        }

        public bool debugVertexes = true;

        public void SmooshSlotPhysics(UMAData umaData, SlotDataAsset SmooshMe, SlotDataAsset SmooshPlane, SlotDataAsset SmooshTarget, bool invertX, bool invertY, bool invertZ, bool invertDist, float smooshDistance, float overSmoosh)
        {
            int smooshCount = 0;
            int unsmooshCount = 0;
            if (SmooshMe == null || SmooshPlane == null || SmooshTarget == null)
            {
                return;
            }
            Mesh m = new Mesh();

            m.SetVertices(SmooshTarget.meshData.GetVertices());

            int[] triangles = new int[SmooshTarget.meshData.submeshes[0].getBaseTriangles().Length];
            Array.Copy(SmooshTarget.meshData.submeshes[0].getBaseTriangles(), triangles, SmooshTarget.meshData.submeshes[0].getBaseTriangles().Length);
            m.SetTriangles(triangles, 0);

            Physics.BakeMesh(m.GetInstanceID(), false);

            CreateSmooshScene();

            GameObject go = new GameObject();
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            try
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = m;


                SceneManager.MoveGameObjectToScene(go, SmooshScene);
                PhysicsScene physicsScene = SmooshScene.GetPhysicsScene();
                // Physics.SyncTransforms();

                var a = SmooshPlane.meshData.vertices[0] + SmooshPlane.clippingPlaneOffset[0];
                var b = SmooshPlane.meshData.vertices[1] + SmooshPlane.clippingPlaneOffset[1];
                var c = SmooshPlane.meshData.vertices[2] + SmooshPlane.clippingPlaneOffset[2];
                var d = SmooshPlane.meshData.vertices[3] + SmooshPlane.clippingPlaneOffset[3];

                if (invertY)
                {
                    a.y = -a.y;
                    b.y = -b.y;
                    c.y = -c.y;
                }
                if (invertX)
                {
                    a.x = -a.x;
                    b.x = -b.x;
                    c.x = -c.x;
                }

                //Plane p = new Plane(a, b, c);
                Plane p = new Plane(a, c, b);

                Vector3 total = Vector3.zero;
                for(int i=0; i<SmooshPlane.meshData.vertices.Length; i++)
                {
                    total += SmooshPlane.meshData.vertices[i];
                }

                Vector3 center = total / SmooshPlane.meshData.vertices.Length;

                Vector3[] newVerts = new Vector3[SmooshMe.meshData.vertices.Length];

                Vector3[] sourceVertexes = SmooshMe.meshData.vertices;

                if (umaData.VertexOverrides.ContainsKey(SmooshMe.slotName))
                {
                    sourceVertexes = umaData.VertexOverrides[SmooshMe.slotName];
                }

                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                Vector3 SmooshMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 SmooshMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                if (debugVertexes)
                {
                    foreach(Vector3 v in SmooshPlane.meshData.vertices)
                    {
                        if (v.x < SmooshMin.x)
                        {
                            SmooshMin.x = v.x;
                        }
                        if (v.y < SmooshMin.y)
                        {
                            SmooshMin.y = v.y;
                        }
                        if (v.z < SmooshMin.z)
                        {
                            SmooshMin.z = v.z;
                        }
                        if (v.x > SmooshMax.x)
                        {
                            SmooshMax.x = v.x;
                        }
                        if (v.y > SmooshMax.y)
                        {
                            SmooshMax.y = v.y;
                        }
                        if (v.z > SmooshMax.z)
                        {
                            SmooshMax.z = v.z;
                        }
                    }
                }

                for (int i = 0; i < newVerts.Length; i++)
                {
                    // 
                    Vector3 currentVert = sourceVertexes[i]; 

                    if (debugVertexes)
                    {
                        if (currentVert.x < min.x)
                        {
                            min.x = currentVert.x;
                        }
                        if (currentVert.y < min.y)
                        {
                            min.y = currentVert.y;
                        }
                        if (currentVert.z < min.z)
                        {
                            min.z = currentVert.z;
                        }
                        if (currentVert.x > max.x)
                        {
                            max.x = currentVert.x;
                        }
                        if (currentVert.y > max.y)
                        {
                            max.y = currentVert.y;
                        }
                        if (currentVert.z > max.z)
                        {
                            max.z = currentVert.z;
                        }
                    }



                    float dist = p.GetDistanceToPoint(currentVert);
                    if (invertDist)
                    {
                        dist *= -1;
                    }

                    Vector3 newVector = currentVert;
                    if (dist > -overSmoosh)
                    {
                        newVector = GetDestVertPhys(currentVert, center, dist, SmooshTarget, physicsScene, i, smooshDistance, overSmoosh);
                        if (Mathf.Abs((newVector - currentVert).magnitude) > 0.18f)
                        {
                            newVector = currentVert;
                        }
                        smooshCount++;
                    }
                    else
                    {
                        unsmooshCount++;
                    }

                   newVector = SmooshMe.smooshOffset + newVector;
                   newVerts[i].Set(newVector.x * SmooshMe.smooshExpand.x, newVector.y * SmooshMe.smooshExpand.y, newVector.z * SmooshMe.smooshExpand.z);
                }
                if (debugVertexes)
                {
                    DrawBox("HairBounds", min, max, Color.red);
                    DrawBox("PlaneBounds", SmooshMin, SmooshMax, Color.blue);
                    Debug.LogWarning("Smooshed " + smooshCount + " verts. Unsmooshed: "+unsmooshCount);
                }
                umaData.AddVertexOverride(SmooshMe, newVerts);
            }
            finally
            {
                CleanScene(SmooshScene);
            }
        }


        public void DrawBox(string boxName, Vector3 Min, Vector3 Max, Color c)
        {
            GameObject DebugBox = GameObject.Find(boxName);

            Vector3 center = (Max + Min) * 0.5f;
            Vector3 size = Max - Min;
            if (DebugBox != null)
            {
                DebugBox.transform.localScale = size;
                DebugBox.transform.position = center;
            }
        }

        void UpdateBounds()
        {
            for (int i = 0; i < activeRace.data.dnaConverterList.Length; i++)
            {
                IDNAConverter id = activeRace.data.dnaConverterList[i];
                if (id is DynamicDNAConverterController)
                {
                    DynamicDNAConverterController dcc = id as DynamicDNAConverterController;
                    dcc.overallModifiers.UpdateCharacter(umaData, umaData.skeleton, false);
                }
            }
        }


        /// <summary>
        /// Called when setting activeRace.data or name to null- destroys the generated renderer and bone game objects and sets the recipe and umaData to null
        /// </summary>
        private void UnloadAvatar()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!activeRace.isValid)
            {
                foreach (Transform child in gameObject.transform)
                {
                    UMAUtils.DestroySceneObject(child.gameObject);
                }
            }
            ClearSlots();
            umaRecipe = null;
            umaData.umaRecipe.ClearDna();
            umaData.umaRecipe.SetSlots(new SlotData[0]);
            umaData.umaRecipe.sharedColors = new OverlayColorData[0];
            animationController = null;

            if (gameObject.GetComponent<UMAExpressionPlayer>())
            {
                gameObject.GetComponent<UMAExpressionPlayer>().enabled = false;
            }
        }

        public void AddAdditionalSerializedRecipes(List<UMARecipeBase> umaAdditionalSerializedRecipes)
        {
            if (umaAdditionalSerializedRecipes != null)
            {
                for (int i = 0; i < umaAdditionalSerializedRecipes.Count; i++)
                {
                    UMARecipeBase umaAdditionalRecipe = umaAdditionalSerializedRecipes[i];
                    if (umaAdditionalRecipe != null)
                    {
                        UMAData.UMARecipe cachedRecipe = umaAdditionalRecipe.GetCachedRecipe();
                        umaData.umaRecipe.Merge(cachedRecipe, false, true, false, activeRace.racedata.raceName);
                    }
                    else
                    {
                        if (Debug.isDebugBuild)
                        {
                            Debug.Log("Null recipe in additional serialized recipes");
                        }
                    }
                }
                // compress the umaData.umaRecipe.slotDataList
                umaData.umaRecipe.Compress();
            }
        }

        /// <summary>
        /// Checks whether the resulting slots in the recipe should actually be there when the build contained recipes that were cross compatible.
        /// For example applying HumanMale underwear to the HumanMaleHighPoly race loads HumanMale's legs over the top of HumanMaleHighPolys legs
        /// This method will check whether the slots defined in that recipe have an equivalent slot defined in the race. If they do the slot in the recipe
        /// will be removed and its overlays applied to the equivalent slot in the base mesh, if overlays are defined as matching. Otherwise the overlays will not
        /// be applied and a warning will be shown.
        /// </summary>
        void FixCrossCompatibleSlots(List<string> hiddenSlots)
        {
            var recipeSlots = umaData.umaRecipe.slotDataList;
            string equivalentSlot = "";
            for (int i = 0; i < recipeSlots.Length; i++)
            {
                equivalentSlot = "";
                SlotData sd = recipeSlots[i];
                if (sd == null)
                {
                    continue;
                }
                //if the race defines this slot as 'equivalent' to any of its own baseRecipe slots in its cross compatibility settings
                //find that slot in the slotDataList attempt to apply this slots overlays to that slot and then remove that slot
                equivalentSlot = activeRace.racedata.FindEquivalentSlot(crossCompatibleRaces, sd.slotName, false);
                if (equivalentSlot != "")
                {
                    SlotData esd = null;
                    for (int ei = 0; ei < recipeSlots.Length; ei++)
                    {
                        if (recipeSlots[ei] == null)
                        {
                            continue;
                        }

                        if (recipeSlots[ei].slotName == equivalentSlot)
                        {
                            esd = recipeSlots[ei];
                            break;
                        }
                    }
                    if (esd != null)
                    {
                        if (activeRace.racedata.GetOverlayCompatibility(sd.slotName))
                        {
                            //this is not going to work because some how we need to know what the base overlays are on the slot we are copying from so we only atempt to add the additional ones
                            //so some kind of GetBaseOverlaysForSlot or GetOverlaysAddedToBaseSlot
                            //something seems to go worong when the slot has shared overlays
                            //esd.SetOverlayList(sd.GetOverlayList());
                            var overlaysToAdd = sd.GetOverlayList();
                            var overlaysOnSlot = esd.GetOverlayList();
                            for (int oi = 0; oi < overlaysToAdd.Count; oi++)
                            {
                                if (!overlaysOnSlot.Contains(overlaysToAdd[oi]))
                                {
                                    esd.AddOverlay(overlaysToAdd[oi]);
                                }
                            }
                        }
                        //09072019 if the equivalent slot is the same as the slot we are checking, then the user has added an unnecessary entry to the compatibility settings
                        //but its very easy to do when base races share things like the inner mouth and eyes, so just skip it.
                        if (!hiddenSlots.Contains(sd.slotName) && equivalentSlot != sd.slotName)
                        {
                            hiddenSlots.Add(sd.slotName);
                        }
                    }
                }
            }
            //if we make this happen after RemoveHiddenSlots() we need to call it again
            RemoveHiddenSlots(hiddenSlots);
        }


        void OldReplaceSlot(UMAWardrobeRecipe Replacer)
        {
            //we need to check if *this* recipe is directly or only cross compatible with this race
            bool isCrossCompatibleRecipe = (activeRace.racedata.IsCrossCompatibleWith(Replacer.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(Replacer.wardrobeSlot));
            string replaceSlot = Replacer.replaces;
            if (isCrossCompatibleRecipe)
            {
                var equivalentSlot = activeRace.racedata.FindEquivalentSlot(Replacer.compatibleRaces, replaceSlot, true);
                if (!string.IsNullOrEmpty(equivalentSlot))
                {
                    replaceSlot = equivalentSlot;
                }
            }
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData originalSlot = umaData.umaRecipe.slotDataList[i];
                if (originalSlot == null)
                {
                    continue;
                }

                if (originalSlot.slotName == replaceSlot)
                {
                    UMAPackedRecipeBase.UMAPackRecipe PackRecipe = Replacer.PackedLoad();
                    UMAData.UMARecipe TempRecipe = UMATextRecipe.UnpackRecipe(PackRecipe);
                    if (TempRecipe.slotDataList.Length > 0)
                    {
                        List<OverlayData> originalOverlays = originalSlot.GetOverlayList();
                        for (int i1 = 0; i1 < TempRecipe.slotDataList.Length; i1++)
                        {
                            SlotData replacementSlot = TempRecipe.slotDataList[i1];
                            if (replacementSlot != null)
                            {
                                if (originalOverlays.Count > 1)
                                {
                                    for (int j = 1; j < originalOverlays.Count; j++)
                                    {
                                        replacementSlot.AddOverlay(originalOverlays[j]);
                                    }
                                }
                                // replacementSlot.SetOverlayList(originalOverlays);
                                umaData.umaRecipe.slotDataList[i] = replacementSlot;
                                break;
                            }
                        }
                    }
                }
            }
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
                {
                    replaceSlot = equivalentSlot;
                }
            }


            SlotData replacedSlot = null;
            List<OverlayData> ReplacedOverlays = null;

            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData originalSlot = umaData.umaRecipe.slotDataList[i];
                if (originalSlot == null)
                {
                    continue;
                }
                if (originalSlot.slotName == replaceSlot)
                {
                    UMAPackedRecipeBase.UMAPackRecipe PackRecipe = Replacer.PackedLoad();
                    UMAData.UMARecipe TempRecipe = UMATextRecipe.UnpackRecipe(PackRecipe);
                    SlotData newSlot = TempRecipe.GetFirstSlot();
                    if (newSlot != null)
                    {
                        replacedSlot = originalSlot;
                        var newOverlays = newSlot.GetOverlayList();

                        newSlot.SetOverlayList(replacedSlot.GetOverlayList());
                        newSlot.AddOverlayList(newOverlays);
                        ReplacedOverlays = newSlot.GetOverlayList();
                        umaData.umaRecipe.slotDataList[i] = newSlot;
                    }
                }
            }

            if (ReplacedOverlays != null)
            {
                for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
                {
                    SlotData checkSlot = umaData.umaRecipe.slotDataList[i];
                    if (checkSlot == null)
                    {
                        continue;
                    }
                    var ovl = checkSlot.GetOverlay(0);
                    var replacedOvl = replacedSlot.GetOverlay(0);

                    if (ovl != null && replacedOvl != null && ovl.overlayName == replacedSlot.GetOverlay(0).overlayName)
                    {
                        checkSlot.SetOverlayList(ReplacedOverlays);
                    }
                }
            }
        }



        /// <summary>
        /// JRM - Renamed from ProcessHiddenSlots for Wildcards
        ///     - This is the only function changed for Wildcards
        /// </summary>
        /// <param name="hiddenSlots"></param>
        /// <param name="hideTags"></param>
        void PostProcessSlots(List<string> hiddenSlots, List<string> hideTags = null)
        {
            List<SlotData> WildCards = null;// = new List<SlotData>();

            List<SlotData> NewSlots = new List<SlotData>();
            List<SlotData> SwapSlots = new List<SlotData>();
            Dictionary<string,List<OverlayData>> SwapOverlays = new  Dictionary<string, List<OverlayData>>();

            // first, gather any swap slot tags. These are slots that will be replaced by other slots.
            HiddenSlots.Clear();
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData sd = umaData.umaRecipe.slotDataList[i];
                if (sd == null || sd.asset == null)
                {
                    continue;
                }
                // all swap slots are hidden by default.
                if (sd.isSwapSlot)
                {
                    sd.tempHidden = true;
                    SwapSlots.Add(sd);
                }
                else
                {
                    for (int j=0; j<sd.GetOverlayList().Count; j++)
                    {
                        OverlayData od = sd.GetOverlay(j);
                        if (od.mergedFromSlot != null && od.mergedFromSlot.isSwapSlot)
                        {
                            od.Supressed = true;
                            if (SwapOverlays.ContainsKey(od.mergedFromSlot.slotName))
                            {
                                SwapOverlays[od.mergedFromSlot.slotName].Add(od);
                            }
                            else
                            {
                                SwapOverlays.Add(od.mergedFromSlot.slotName, new List<OverlayData>() { od });
                            }
                        }
                    }
                }
            }

            // if there are any swap slots, we need to go through the recipe and find any slots that have the swap tag.
            // if there are any with the swap tag, then they will be hidden, and the swap slot will be shown instead.
            for (int i = 0; i < SwapSlots.Count; i++)
            {
                SlotData swap = SwapSlots[i];
                for (int i1 = 0; i1 < umaData.umaRecipe.slotDataList.Length; i1++)
                {
                    SlotData sd = umaData.umaRecipe.slotDataList[i1];
                    if (sd == null || sd.asset == null)
                    {
                        continue;
                    }
                    if (sd.HasTag(swap.swapTag))
                    {
                        swap.tempHidden = false;
                        sd.tempHidden = true;
                        if (SwapOverlays.ContainsKey(sd.slotName))
                        {
                            foreach(OverlayData od in SwapOverlays[sd.slotName])
                            {
                                od.Supressed = false;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData sd = umaData.umaRecipe.slotDataList[i];
                if (sd == null || sd.asset == null)
                {
                    continue;
                }

                if (forceSuppressSlotsContaining.Count > 0)
                {
                    for(int j = 0;j<forceSuppressSlotsContaining.Count;j++)
                    {
                        if (sd.slotName.Contains(forceSuppressSlotsContaining[j]))
                        {
                            sd.tempHidden = true;
                            break;
                        }
                    }
                }

                if (sd.tempHidden || sd.isDisabled)
                {
                    HiddenSlots.Add(sd);
                    continue;
                }

                sd.RemoveOverlayTags(hideTags);

                if (sd.HasTag(hideTags))
                {
                    HiddenSlots.Add(sd);
                    continue;
                }

                if (!hiddenSlots.Contains(sd.asset.slotName))
                {
                    if (sd.asset.isWildCardSlot)
                    {
                        if (sd.Races.Length > 0)
                        {
                            for (int i1 = 0; i1 < sd.Races.Length; i1++)
                            {
                                string s = sd.Races[i1];
                                if (s == activeRace.racedata.raceName)
                                {
                                    // if we have races defined,
                                    // then only process them if the race matches.
                                    if (WildCards == null)
                                    {
                                        WildCards = new List<SlotData>();
                                    }

                                    WildCards.Add(sd);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (WildCards == null)
                            {
                                WildCards = new List<SlotData>();
                            }

                            WildCards.Add(sd);
                        }
                    }
                    else
                    {
                        NewSlots.Add(sd);
                    }
                }
                else
                {
                    HiddenSlots.Add(sd);
                }
            }

            /* process newSlots. Add any overlays to the *first* matching slot.*/
            if (WildCards != null && WildCards.Count > 0)
            {
                for (int i = 0; i < WildCards.Count; i++)
                {
                    SlotData wc = WildCards[i];
                    for (int i1 = 0; i1 < NewSlots.Count; i1++)
                    {
						// Only stick an overlay on it if it has tags and a mesh
						SlotData sd = NewSlots[i1];
						if(sd.tags != null && sd.tags.Length > 0 && sd.asset.meshData != null && sd.asset.meshData.subMeshCount > 0)
                        {
                            if (sd.HasTag(wc.tags))
                            {
                                sd.AddOverlayList(wc.GetOverlayList());
                            }
                        }
                    }
                }
            }

            umaData.umaRecipe.slotDataList = NewSlots.ToArray();
            if (HiddenSlots.Count > 0 && SlotsHidden != null)
            {
                SlotsHidden.Invoke(HiddenSlots);
            }
        }

        void RemoveHiddenSlots(List<string> hiddenSlots)
        {
            List<SlotData> NewSlots = new List<SlotData>();
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData sd = umaData.umaRecipe.slotDataList[i];
                if (sd == null)
                {
                    continue;
                }

                if (!hiddenSlots.Contains(sd.asset.slotName))
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
            umaData.rawAvatar = rawAvatar;
            umaData.Dirty(DnaDirty, TextureDirty, MeshDirty);
        }

        //@jaimi not sure what calls this. Generator maybe?
        //@david - I can't find anything calling it
        public void AvatarCreated(UMAData uMAData)
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
            if (!Application.isPlaying)
            {
                return;
            }

            if (cacheStateName == "NULL")//we are caching the state before the Avatar is loaded- which is basically just the colors
            {
                var thisModel = new UMATextRecipe.DCSPackRecipe();
                var packedcolors = new List<UMAPackedRecipeBase.PackedOverlayColorDataV3>();
                for (int i = 0; i < characterColors.Colors.Count; i++)
                {
                    ColorValue cv = characterColors.Colors[i];
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

        #region CLEANUP 

        /// <summary>
        /// Cleanup UMA system
        /// </summary>
        public void Cleanup()
        {
            // Unload any items to free memory.
#if UMA_ADDRESSABLES
            while(LoadedHandles.Count > 0)
            {
                AsyncOp Op = LoadedHandles.Dequeue();
                UnloadOldestQueuedHandle(Op);
            }
#endif
            if (umaData != null)
            {
                if (umaData.umaGenerator != null)
                {
                    umaData.umaGenerator.removeUMA(umaData);
                }
            }
        }


        /// <summary>
        /// Looks through the dirtylist to see if it is being update.
        /// You should probably not do this every frame.
        /// </summary>
        /// <returns></returns>
        public bool UpdatePending()
        {
            if (umaData != null)
            {
                if (umaData == null)
                {
                    return false;
                }

                if (umaData.umaGenerator == null)
                {
                    return false;
                }

                return umaData.umaGenerator.updatePending(umaData);
            }
            return false;
        }
        #endregion

#endregion

        #region SPECIALTYPES // these types should only be needed by DynamicCharacterAvatar

        [Serializable]
        public class RaceSetter
        {
            public string name;

            [NonSerialized]
            RaceData _theRaceData;

            public bool isValid
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return false;
                    }

                    if (_theRaceData == null)
                    {
                        return false;
                    }

                    return true;
                }
            }

            //These properties use camelCase rather than lower case to deliberately hide the fact they are properties
            /// <summary>
            /// Will return the racedata for the current activeRace.name - if it is in an asset bundle or in resources it will find it (if it has already been downloaded) but wont cause it to download. If you ony need to know if the data is there use the racedata field instead.
            /// </summary>
            public RaceData data
            {
                get
                {
                    SetRaceData();
                    return _theRaceData;
                }
                set
                {
                    _theRaceData = value;
                }
            }
            /// <summary>
            /// returns the active raceData (quick)
            /// </summary>
            public RaceData racedata
            {
                get { return _theRaceData; }
            }
             
            public void SetRaceData()
            {
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }
                if (UMAAssetIndexer.Instance == null)
                {
                    return;
                }
                _theRaceData = UMAAssetIndexer.Instance.GetRace(name);
            }
        }

        [Serializable]
        public class WardrobeRecipeListItem
        {
            public string _recipeName;
            [System.NonSerialized]
            public UMATextRecipe _recipe;
            public bool _enabledInDefaultWardrobe = true;
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

            public List<WardrobeRecipeListItem> GetRecipesForRace(string raceName = "", RaceData race = null)
            {
                List<WardrobeRecipeListItem> validRecipes = new List<WardrobeRecipeListItem>();

                for (int i = 0; i < recipes.Count; i++)
                {
                    WardrobeRecipeListItem WLIRecipe = recipes[i];
                    if (WLIRecipe._recipe == null && UMAAssetIndexer.Instance.HasRecipe(WLIRecipe._recipeName))
                    {
                        WLIRecipe._recipe = UMAAssetIndexer.Instance.GetRecipe(WLIRecipe._recipeName, false);
                    }
                    if (WLIRecipe._recipe == null)
                    {
                        continue;
                    }

                    WLIRecipe._compatibleRaces = new List<string>(WLIRecipe._recipe.compatibleRaces);
                    if (raceName == "" || WLIRecipe._recipe.compatibleRaces.Contains(raceName) || (race != null && race.IsCrossCompatibleWith(WLIRecipe._recipe.compatibleRaces)))
                    {
                        validRecipes.Add(WLIRecipe);
                    }
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

            public RuntimeAnimatorController GetAnimatorForRace(string racename)
            {
                RuntimeAnimatorController controllerToUse = defaultAnimationController;
                for (int i = 0; i < animators.Count; i++)
                {
                    if (animators[i].raceName == racename)
                    {
                        if (animators[i].animatorController == null)
                        {
                            animators[i].animatorController = UMAAssetIndexer.Instance.GetAsset<RuntimeAnimatorController>(animators[i].animatorControllerName);
                        }
                        if (animators[i].animatorController != null)
                        {
                            controllerToUse = animators[i].animatorController;
                        }
                        break;
                    }
                }
                return controllerToUse;
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
            [SerializeField]
            private bool Raw;

            public bool valuesConverted = false;

            public string Name
            {
                get
                {
                    if (_name != null)
                    {
                        ConvertOldFieldsToNew();
                    }

                    return name;
                }
                set { name = value; }
            }

            public Color Color
            {
                get
                {
                    if (_name != null)
                    {
                        ConvertOldFieldsToNew();
                    }

                    return color;
                }
                set { color = value; }
            }

            public Color MetallicGloss
            {
                get
                {
                    if (_name != null)
                    {
                        ConvertOldFieldsToNew();
                    }

                    if (channelAdditiveMask.Length >= 3)
                    {
                        return channelAdditiveMask[2];
                    }
                    return new Color(0, 0, 0, 0);
                }
                set
                {
                    if (channelAdditiveMask.Length >= 3)
                    {
                        channelAdditiveMask[2] = value;
                    }
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
                if (!string.IsNullOrEmpty(_name))
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
                for (int i = 0; i < colors.Length; i++)
                {
                    OverlayColorData ocd = colors[i];
                    SetColor(ocd.name, ocd);
                }
            }

#if UNITY_EDITOR
            public bool RemoveDeletedItems()
            {
                List<ColorValue> newColors = new List<ColorValue>();
                for (int i = 0; i < Colors.Count; i++)
                {
                    ColorValue cv = Colors[i];
                    if (cv == null)
                    {
                        continue;
                    }
                    if (cv.Name == null)
                    {
                        continue;
                    }
                    if (cv.Color == null)
                    {
                        continue;
                    }
                    if (cv.deleteThis)
                    {
                        continue;
                    }
                    newColors.Add(cv);
                }
                if (Colors.Count != newColors.Count)
                {
                    Colors = newColors;
                    return true;
                }
                return false;
            }
#endif

            public ColorValueList(List<ColorValue> colorValueList)
            {
                Colors = colorValueList;
            }

            #endregion


            private ColorValue GetColorValue(string name)
            {
                for (int i = 0; i < Colors.Count; i++)
                {
                    ColorValue cv = Colors[i];
                    if (cv.Name == name)
                    {
                        return cv;
                    }
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

            public void SetRawColorParms(string name, OverlayColorData c)
            {
                ColorValue cv = GetColorValue(name);
                if (cv != null)
                {
                    cv.AssignFrom(c,true);
                }
                else
                {
                    SetRawColor(name,c);
                }
            }


            public void SetRawColor(string name, OverlayColorData c)
            {
                ColorValue cv = GetColorValue(name);
                if (cv != null)
                {
                    cv.AssignFrom(c);
                }
                else
                {
                    Colors.Add(new ColorValue(name, c));
                }
            }


            public void RemoveColor(string name)
            {
                List<ColorValue> newColors = new List<ColorValue>();

                for (int i = 0; i < Colors.Count; i++)
                {
                    ColorValue cv = Colors[i];
                    if (cv.Name != name)
                    {
                        newColors.Add(cv);
                    }
                }

                Colors = newColors;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Copy From Current Wardrobe")]
        void CopyDefaultWardrobe()
        {
            string recipeString = JsonUtility.ToJson(new UMATextRecipe.DCSPackRecipe(this, "", "DynamicCharacterAvatar", defaultSaveOptions));
            EditorGUIUtility.systemCopyBuffer = recipeString;
            Debug.Log("Copied: " + EditorGUIUtility.systemCopyBuffer);
        }

        [ContextMenu("Paste To Default Wardrobe")]
        void PasteDefaultWardrobe()
        {
            string buffer = EditorGUIUtility.systemCopyBuffer;
            Debug.Log("Pasting: " + buffer);

            UMATextRecipe.DCSPackRecipe copiedList = JsonUtility.FromJson<UMATextRecipe.DCSPackRecipe>(buffer);

            ChangeRace(copiedList.race);

            if (copiedList.wardrobeSet.Count > 0)
            {
                preloadWardrobeRecipes.recipes.Clear();
            }

            for (int i = 0; i < copiedList.wardrobeSet.Count; i++)
            {
                WardrobeSettings wardrobe = copiedList.wardrobeSet[i];
                UMATextRecipe recipe = UMAAssetIndexer.Instance.GetAsset<UMATextRecipe>(wardrobe.recipe);
                if (recipe != null)
                {
                    WardrobeRecipeListItem item = new WardrobeRecipeListItem(recipe);
                    preloadWardrobeRecipes.recipes.Add(item);
                }
            }

            if (copiedList.characterColors.Count > 0)
            {
                characterColors._colors.Clear();
            }

            for (int i = 0; i < copiedList.characterColors.Count; i++)
            {
                UMAPackedRecipeBase.PackedOverlayColorDataV3 color = copiedList.characterColors[i];
                OverlayColorData colorData = new OverlayColorData();
                color.SetOverlayColorData(colorData);
                characterColors.SetColor(color.name, colorData);
            }
        }

#endif
    }

    #endregion


}
