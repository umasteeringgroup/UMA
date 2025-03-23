using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TerrainTools;
using UnityEngine;

namespace UMA
{
    [InitializeOnLoad]
    public class WelcomeToUMA : EditorWindow
    {

        public static WelcomeToUMA Instance
        {
            get; set;
        }

        static WelcomeToUMA()
        {
            EditorApplication.delayCall += DelayedCall;
        }

        static void DelayedCall()
        {
            EditorApplication.update += Update;
        }

        public static void Update()
        {
            UMASettings settings = UMASettings.GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }
            if (settings.showWelcomeToUMA)
            {
                ShowWindow();
            }
            EditorApplication.update -= Update;
        }

        [MenuItem("UMA/Welcome to UMA",false,0)]
        public static void ShowWindow()
        {
            Texture umaTex = Resources.Load("UMABanner") as Texture;
            WelcomeToUMA win = EditorWindow.GetWindow<WelcomeToUMA>();
            win.position = new Rect(100, 100, 800, 600);
            win.titleContent = new GUIContent("Welcome to UMA", umaTex);
        }

        // Delegate that takes a LogLine   
        private delegate void LogLineAction(LogLine line);

        public enum LogType
        {
            Error,
            Warning,
            Info,
            Resolution,
            None
        }


        private class LogLine
        {
            public string Message;
            public GUIStyle Style;
            public int index;
            public LogLineAction ButtonAction;
            public LogType logType = LogType.Info;
            public AssetItem ReviewItem = null;
            public Texture2D Image = null;

            public LogLine(string message, GUIStyle style, int index, LogType logType = LogType.Info)
            {
                Message = message;
                Style = style;
                this.index = index;
                this.logType = logType;
            }

            public LogLine(Texture2D image)
            {
                Image = image;
            }

            public LogLine(string message, GUIStyle style, LogLineAction buttonAction,int index, LogType logType = LogType.Info)
            {
                Message = message;
                Style = style;
                ButtonAction = buttonAction;
                this.index = index;
                this.logType = logType;
            }

            public void Resolve(string message)
            {
                Message = "---> "+message;
                ButtonAction = null;
                logType = LogType.Resolution;
            }
            public void Error(string message)
            {
                Message = "!!-> "+message;
                ButtonAction = null;
                logType = LogType.Error;
            }
        }


        private List<LogLine> LoggedItems = new List<LogLine>();


        public Color ActiveColor = new Color32(0, 210, 0, 255);
        public Color InactiveColor = new Color32(235, 0, 0, 255);
        public Color PanelColor = new Color32(128, 128, 128, 64);
        public GUIStyle ActiveLargeStyle;
        public GUIStyle ErrorFound;
        public GUIStyle Warning;
        public GUIStyle InfoStyle;
        public GUIStyle Hyperlink;
        public GUIStyle DescriptionStyle;
        public GUIStyle SceneTitleStyle;

        public Rect HeaderRect;
        public Rect NavigationRect;
        public Rect ContentRect;

        public int currentButton;
        private Vector2 scrollPosition;
        public bool processing = false;
        public bool initialized = false;

        public UMASettings initialSettings;


        public void OnEnable()
        {
            Instance = this;
        }

        public void OnDisable()
        {
            Instance = null;
        }

        public void Awake()
        {
            EditorApplication.delayCall += DelayAwake;
        }

        public void DelayAwake()
        {
            ActiveLargeStyle = new GUIStyle(EditorStyles.largeLabel);
            ActiveLargeStyle.richText = true;
            ActiveLargeStyle.wordWrap = true;
            ActiveLargeStyle.fontSize = 32;
            ActiveLargeStyle.alignment = TextAnchor.MiddleCenter;

            Hyperlink = new GUIStyle(EditorStyles.label);
            Hyperlink.hover.textColor = Color.cyan;
            Hyperlink.active.textColor = Color.white;
            Hyperlink.richText = true;
            Hyperlink.alignment = TextAnchor.MiddleLeft;

            ErrorFound = new GUIStyle(EditorStyles.label);
            ErrorFound.normal.textColor = new Color(0.3f, 0, 0, 1);
            ErrorFound.richText = true;
            ErrorFound.alignment = TextAnchor.MiddleLeft;

            Warning = new GUIStyle(EditorStyles.label);
            Warning.normal.textColor = Color.yellow;
            Warning.richText = true;
            Warning.alignment = TextAnchor.MiddleLeft;

            InfoStyle = new GUIStyle(EditorStyles.label);
            InfoStyle.alignment = TextAnchor.MiddleLeft;
            InfoStyle.richText = true;

            DescriptionStyle = new GUIStyle(EditorStyles.label);
            DescriptionStyle.wordWrap = true;
            DescriptionStyle.richText = true;
            DescriptionStyle.alignment = TextAnchor.UpperLeft;

            SceneTitleStyle = new GUIStyle(EditorStyles.label);
            SceneTitleStyle.wordWrap = false;
            SceneTitleStyle.richText = true;
            SceneTitleStyle.alignment = TextAnchor.UpperLeft;

            //DescriptionStyle.fixedHeight = 48;

            initialSettings = UMASettings.GetOrCreateSettings();
            currentButton = 0;
            DoWelcome();
            initialized = true;
        }

        private void StartProcessing()
        {
            processing = true;
        }

        private void StopProcessing()
        {
            processing = false;
        }


        void OnGUI()
        {
            if (!initialized)
            {
                // this is here because the "awake" function is called before EditorStyles are loaded.
                // Causing an error when the window is initialized. So that was delayed.
                // now this is here because we have to be sure that initial delay happened.
                // Seems like this could have been better if the editor initialized itself before calling the awake functions.
                // Just saying.
                Repaint();
                return;
            }
            HeaderRect = new Rect(0, 0, position.width, 50);
            NavigationRect = new Rect(0, 50, 200, position.height - 50);
            ContentRect = new Rect(200, 50, position.width - 200, position.height - 50);

            DrawHeader();
            DrawNavigation();
            DrawContent(currentButton);
        }



        public void DrawHeader()
        {
            var settings = UMASettings.GetOrCreateSettings();
            GUIHelper.BeginInsetArea(PanelColor, HeaderRect, 2, 0, 4);
            EditorGUILayout.LabelField($"Welcome to {settings.UMAVersion}", ActiveLargeStyle);
            GUIHelper.EndInsetArea();
        }


        public void DrawNavigation()
        {
            GUIHelper.BeginInsetArea(PanelColor, NavigationRect, 4, 10);
            GUILayout.BeginVertical();
            if (GUILayout.Button("Welcome", GUILayout.Height(40)))
            {
                ClearLog();
                DoWelcome();
                currentButton = 0;
            }
            if (GUILayout.Button("Basics", GUILayout.Height(40)))
            {
                ClearLog();
                AddText("UMA is a runtime character creation system for Unity3D");
                AddText("It relies on a library of indexed items to create characters");
                AddText("The library data can be in Resources and/or in Addressable Bundles");
                AddSeperator();
                AddText("UMA uses a generator to create characters - UMA_GLIB");
                AddText("This prefab needs to be in a scene for UMA to work.");
                AddText("The generator has settings for texture merging, mesh combining, and more.");
                AddText("To get started, use the 'Add an UMA to the current scene' button");
                AddText("This will add an editable UMA and generator, if needed");
                AddSeperator();
                AddText("UMA uses recipes to define meshes, textures, and other data");
                AddText("   There are two types of recipes - basic <b>Text Recipes</b> and <b>Wardrobe Recipes.");
                AddText("   <b>Text recipes</b> are used to define the base character, or to provide utility functions (like add a capsule collider)");
                AddText("   <b>Wardrobe recipes</b> are used to define wearable items, who can use them, and what 'slot' they use when equipped.");
                AddText("   Wardrobe recipes have advanced functions to hide parts of the character, switch out slotdatas when needed, smoosh hair under a hat, etc.");
                AddText("");
                AddText("<b>Base parts of an UMA</b>");
                AddText(" ");
                AddText("   <b>SlotData:</b>");
                AddImage(initialSettings.Slots,"");
                AddText("A SlotData contains a mesh part, along with any rig parts needed.");
                AddText("These are combined into a Skinned Mesh when the character is built.");

                AddText(" ");
                AddText("   <b>OverlayData:</b>");
                AddImage(initialSettings.Overlays,"");
                AddText("An OverlayData contains texture parts that are colorized and combined to build textures.");
                AddText("Overlays contain all the textures needed for a single layer - for example, the albedo, normal, and metallic.");
                AddText("Overlays are layered on top of each other to build the final texture for a slotdata.");
                AddText(" ");
                AddText("   <b>DNA:</b>  This is used to adjust the meshes when built, either bone modifications or blendshapes");
                AddText("   <b>Recipes:</b>  These are used to tie slotdata and overlays together, to build skinned meshes");
                AddText("   <b>RaceData:</b>  This defines a base recipe for the character, what wardrobe slots are available, what DNA converters are used, etc.");
                AddSeperator();
                AddText("We recommend to watch the videos on youtube for a deeper dive into how UMA works");
                AddText("https://www.youtube.com/@SecretAnorak/videos");
                // explain about the generator
                // about the library
                // about races
                // about recipes
                // about slots
                // about overlays
                currentButton = 1;
            }
            if (GUILayout.Button("View Documentation", GUILayout.Height(40)))
            {
                ClearLog();
                currentButton = 6;
                DoDocumentation();
            }

            if (GUILayout.Button("Add an UMA to current scene", GUILayout.Height(40)))
            {
                ClearLog();
                DoAddToScenePage();
                currentButton = 2;
            }
            if (GUILayout.Button("Example Scenes", GUILayout.Height(40)))
            {
                ClearLog();
                scrollPosition = Vector2.zero;
                currentButton = 8;
            }
            if (GUILayout.Button("Rebuild Library", GUILayout.Height(40)))
            {
                ClearLog();
                currentButton = 7;
                RebuildLibrary();
            }
            if (GUILayout.Button("Recompile Shaders", GUILayout.Height(40)))
            {
                ClearLog();
                currentButton = 6;
                ReimportShaderFolder();
            }
            if (GUILayout.Button("Scan Scene", GUILayout.Height(40)))
            {
                ClearLog();
                ScanScene();
                currentButton = 3;
            }
            if (GUILayout.Button("Scan Project", GUILayout.Height(40)))
            {
                ClearLog();
                ScanProject();
                currentButton = 4;
            }
            if (GUILayout.Button("Links", GUILayout.Height(40)))
            {
                ClearLog();
                // Links page has to be done in content window
                currentButton = 5;
            }
            if (initialSettings.showWelcomeToUMA)
            {
                if (GUILayout.Button("Turn this off!!"))
                {
                    currentButton = 9;
                    ClearLog();
                    UMASettings settings = UMASettings.GetOrCreateSettings();
                    settings.showWelcomeToUMA = false;
                    EditorUtility.SetDirty(settings);
                    AddText("The welcome window will no longer show when Unity is opened");
                    AddText("To view it at any time, you can use the 'UMA/Welcome to UMA' menu item");
                    AddText("You can re-enable this in the UMA project settings.");
                }
            }
            GUILayout.EndVertical();
            GUIHelper.EndInsetArea();
        }

        private void ReimportShaderFolder()
        {
            ClearLog();
            string path = Application.dataPath;
            path = Path.Combine(path, "UMA", "Core", "ShaderPackages");

            if (Directory.Exists(path))
            {
                AddText($"Reimporting shaders in {path}");
                StartProcessing();
                AssetDatabase.ImportAsset("Assets/UMA/Core/ShaderPackages", ImportAssetOptions.ForceUpdate | ImportAssetOptions.DontDownloadFromCacheServer | ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
                StopProcessing();
                AddText(path + " reimported successfully!");
            }
            else
            {
                AddText($"Shader folder not found: {path}", LogType.Error);
                EditorUtility.DisplayDialog("UMA Shaders", "The UMA Shader folder is missing. Please reinstall UMA to get the shaders.", "OK");
            }
        }

        private void RebuildLibrary()
        {
            AddText("Rebuilding UMA Asset Library...");
            var UAI = UMAAssetIndexer.Instance;
            if (UAI == null)
            {
                AddText("UMA Asset Indexer not found!", LogType.Error);
                AddText("The library is a scriptable object named 'AssetIndexer' in the UMA/InternalDataStore/Ingame/Resources folder", LogType.Error);
                AddText("The library is needed to know where all the UMA Assets are (Either in Resources or in Addressable Bundles)", LogType.Error);
                AddText("UMA will not work without the library!", LogType.Error);
                AddText("Please reimport the UMA asset to fix this issue!", LogType.Error);
                return;
            }
            try
            {
                AddSeperator();
                AddText("Library rebuild found:");
                UAI.RebuildLibrary();
                var counts = UAI.GetCounts();
                foreach(var count in counts)
                {
                    AddText($"{count.Key}: ({count.Value}) item(s)");
                }
                AddSeperator();
                AddText("UMA Asset Library rebuilt successfully!");
            }
            catch (Exception ex)
            {
                AddText("Error rebuilding UMA Asset Library: " + ex.Message, LogType.Error);
                AddText("Stacktrace:");
                AddText(ex.StackTrace);
            }
        }

        private void DoDocumentation()
        {
            ClearLog();
            AddText("Opening UMA Documentation.PDF");

            // Open Assets/UMA/UMA Documentation.PDF
            string path = Path.Combine(Application.dataPath,"UMA", "UMA Documentation.PDF");

            if (System.IO.File.Exists(path))
            {
                AddText($"PDF File \"{path}\" should open in a new window");
                System.Diagnostics.Process.Start(path);
            }
            else
            {
                AddText($"UMA Documentation file not found: {path}", LogType.Error);
                EditorUtility.DisplayDialog("UMA Documentation", "The UMA Documentation file is missing. Please reinstall UMA to get the documentation.", "OK");
            }
        }

        private void DrawContent(int currentButton)
        {
            bool showLog = true;
            GUIHelper.BeginInsetArea(PanelColor, ContentRect, 4, 10);
            switch (currentButton)
            {
                case 5:
                    DoLinksPage();
                    showLog = false;
                    break;

                case 8:
                    DoScenesPage();
                    showLog = false;
                    break;
            }
            /* now show the logged items */
            if (showLog)
            {
                scrollPosition = GUILayout.BeginScrollView(scrollPosition);
                ShowLogItems();
                GUILayout.EndScrollView();
            }
            GUIHelper.EndInsetArea();
        }

        private void ShowLogItems()
        {
            LogLineAction ButtonAction = null;
            LogLine ButtonActionLine = null;
           
            foreach (var item in LoggedItems)
            {
                if (item.Image != null)
                {
                    GUILayout.BeginHorizontal();
                    if (!string.IsNullOrEmpty(item.Message))
                    {
                        GUILayout.Label(item.Message, InfoStyle);
                    }
                    GUILayout.Label(item.Image, GUILayout.Width(600));
                    GUILayout.EndHorizontal();
                    continue;
                }
                GUILayout.BeginHorizontal();                
                if (item.logType == LogType.Error)
                {
                    GUILayout.Label("Error: ", ErrorFound, GUILayout.Width(60));
                }
                else if (item.logType == LogType.Warning)
                {
                    GUILayout.Label("Warning: ", Warning, GUILayout.Width(60));
                }
                else
                {
                    // GUILayout.Label("Info: ", InfoStyle, GUILayout.Width(60));
                }
                if (item.ButtonAction != null)
                {
                    if (GUILayout.Button(item.Message))
                    {
                        ButtonAction = item.ButtonAction;
                        ButtonActionLine = item;
                    }
                }
                else
                {
                    GUILayout.Label(item.Message, item.Style);
                }
                GUILayout.EndHorizontal();
            }
            // Have to do the buttonaction outside of the loop in case the
            // action adds loglines (modifies the list).
            if (ButtonAction != null && ButtonActionLine != null)
            {
                ButtonAction(ButtonActionLine);
            }
        }

        private void ClearLog()
        {
            LoggedItems.Clear();
            Repaint();
        }

        private LogLine AddLargeText(string text)
        {
            LogLine line = new LogLine(text, ActiveLargeStyle,LoggedItems.Count);            
            LoggedItems.Add(line);
            Repaint();
            return line;
        }

        private void AddSeperator()
        {
            AddText("--------------------------------------------------",LogType.None);
        }

        private LogLine AddText(string text, LogType logType = LogType.Info, GUIStyle style= null)
        {
            if (style == null)
            {
                LogLine line = new(text, InfoStyle,LoggedItems.Count,logType);
                LoggedItems.Add(line);
                Repaint();
                return line;
            }
            else
            {
                LogLine line = new(text, style, LoggedItems.Count,logType);
                LoggedItems.Add(line);
                Repaint();
                return line;
            }
        }

        private LogLine AddImage(Texture2D image, string message)
        {
            LogLine line = new LogLine("", InfoStyle, LoggedItems.Count);
            line.Image = image;
            LoggedItems.Add(line);
            Repaint();
            return line;
        }

        private LogLine AddText(string text, GUIStyle style, LogLineAction buttonAction)
        {
            LogLine line = new(text, style, buttonAction, LoggedItems.Count);
            LoggedItems.Add(line);
            Repaint();
            return line;
        }

        #region Scene Scan Button
        private void ScanScene()
        {
            UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>(true);
            AddText("Checking for generator");
            if (generators.Length == 0)
            {
                AddText("UMA Generator not found in scene", LogType.Error);
                LogLine l = AddText(text:"Add UMA Generator to Scene", LogType.Error);
                l.ButtonAction = (line) => DoAddGenerator(l);
            }
            else if (generators.Length > 1)
            {
                AddText("Multiple UMA Generators found in scene!", LogType.Error);
                AddText("This can cause problems, please remove all but one generator from the scene", LogType.Error);
#if UNITY_6000_0_OR_NEWER
                // can we filter the view to the generators?
#else
                AddText("Note: You can use the 'Filter' field in the hierarchy with t:UMAGENARATOR to find them", LogType.Error);
#endif
            }
            else
            {
                UMAGenerator gen = generators[0];
                if (!gen.gameObject.activeInHierarchy)
                {
                    AddText("UMA Generator is not active in the scene", LogType.Error);
                    AddText("UMA Generator must be active in the scene to work correctly", LogType.Error);
                    LogLine l = AddText(text: "Activate Generator", LogType.Error);
                    l.ButtonAction = (line) => DoActivateGenerator(l);
                }
                else
                {
                    AddText("UMA Generator found and active in scene...");
                }
                AddSeperator();
                AddText("Checking Generator settings");
                if (gen.textureMerge != null)
                {
                    AddText("Texture Merge is set up correctly");
                }
                else
                {
                    AddText("Texture Merge is not set up correctly", LogType.Error);
                    AddText("Please assign a Texture Merge to the UMA Generator", LogType.Error);
                    LogLine l = AddText(text: "Add Texture Merge Object", LogType.Error);
                    l.ButtonAction = (line) => DoAddTextureMerge(l);
                }
                AddSeperator();
                if (gen.meshCombiner != null)
                {
                    AddText("Mesh Combiner is set up correctly");
                }
                else
                {
                    AddText("Mesh Combiner is not set up correctly", LogType.Error);
                    AddText("Please add an UMAMeshCombiner component to the generator and assign field!", LogType.Error);
                    LogLine l = AddText(text: "Add MeshCombiner automatically", LogType.Error);
                    l.ButtonAction = (line) => DoAddMeshCombiner(l);
                }
                if (gen.InitialScaleFactor != 1)
                {
                    AddSeperator();
                    AddText("Warning: Initial Scale Factor is not set to 1", LogType.Warning);
                    AddText("This will cause all textures to be scaled down.", LogType.Warning);
                    AddText("Please verify and ensure this is what you intend", LogType.Warning);
                    LogLine l = AddText(text: "Set Initial Scale Factor", LogType.Warning);
                    l.ButtonAction = (line) => DoSetInitialScaleFactor(l);
                }
                if (gen.editorInitialScaleFactor == 1)
                {
                    AddSeperator();
                    AddText("Warning: Editor Initial Scale Factor is set to 1", LogType.Warning);
                    AddText("-- This will cause all textures to be native size in the editor.");
                    AddText("-- This can cause slowdowns in the editor.");
                    AddText("Please verify this is what you intend", LogType.Warning);
                    LogLine l = AddText(text: "Set Editor Initial Scale Factor", LogType.Warning);
                    l.ButtonAction = (line) => DoSetEditorInitialScaleFactor(l);
                }
                if (gen.fitAtlas == false || gen.SharperFitTextures == false || gen.AtlasOverflowFitMethod != UMAGeneratorBase.FitMethod.BestFitSquare || gen.atlasResolution < 2048 || gen.convertMipMaps == false || gen.SaveAndRestoreIgnoredItems == false)
                {
                    AddSeperator();
                    AddText("Checking for optimal generator settings");
                    if (gen.fitAtlas == false)
                    {
                        AddText("Fit Atlas is NOT enabled", LogType.Warning);
                        AddText("-- This can cause textures to be missing");
                    }
                    if (gen.SharperFitTextures == false)
                    {
                        AddText("Sharper Fit Textures is NOT enabled", LogType.Warning);
                        AddText("-- This can cause blurry textures");
                    }
                    if (gen.AtlasOverflowFitMethod != UMAGeneratorBase.FitMethod.BestFitSquare)
                    {
                        AddText("Atlas Overflow Fit Method is NOT set to BestFitSquare", LogType.Warning);
                        AddText(" -- This can cause blurry textures on overflow!");
                    }
                    AddText("Please verify and ensure this is what you intend");

                    if (gen.SaveAndRestoreIgnoredItems == false)
                    {
                        AddText("Warning: Save and Restore Ignored Items is NOT enabled", LogType.Warning);
                        AddText("-- This can cause items to be lost IF you attach gameObjects to the rig");
                        AddText("Please verify and ensure this is what you intend", LogType.Warning);
                    }
                    if (gen.convertMipMaps == false)
                    {
                        AddText("Warning: Convert MipMaps is NOT enabled", LogType.Warning);
                        AddText("-- This can cause excess texture usage");
                        AddText("-- and loss of detail in far characters");
                        AddText("Please verify and ensure this is what you intend", LogType.Warning);
                    }
                    LogLine l = AddText(text: "Set optimal generator settings", LogType.Warning);
                    l.ButtonAction = (line) => DoSetAtlasGenerationParms(l);
                }

                // check all the UMA's in the scene.
                // If RaceData is not set, then give warning
                // Does the character have animators assigned? If not, does it have an animator and "keep animator" checked.
                // if race has blendshape DNA, but "Load Blendshapes" is not checked.
                // if race is NOT in library, give error
            }
        }
        #endregion

        private void ScanProject()
        {
            // Check library... if it's empty, rebuild
            // Check library filters.
            // if no filters for animators, then complain
            // make sure there are slots, overlays, racedata assigned
            // scan all UMAMaterials. If they are type "Use Existing Textures" make sure they are using the Tinted Texture in channels
            AddText("Checking library");
            if (UMAAssetIndexer.Instance == null)
            {
                AddText("Cannot load Global Library from resources! Please reimport or restore the file.");
                AddText("The library is normaly at the following location:");
                AddText(" Assets/UMA/InternalDataSore/InGame/Resources/AssetIndexer.asset");
                return;
            }

            CheckLibrary();
            AddSeperator();
            CheckMaterials();
            AddSeperator();
            CheckSlots();
            AddSeperator();
            CheckOverlays();
            AddSeperator();
            CheckTextRecipes();
            AddSeperator();
            CheckWardrobeRecipes();
            AddSeperator();
            CheckWardrobeCollections();
            AddSeperator();
            CheckRaces();
            AddSeperator();
            AddText("Project check completed. Please review any items that were flagged");
        }

        private void CheckLibrary()
        {
            AddText("Checking UMA Global Library");
            if (UMAAssetIndexer.Instance.IsValid() == false)
            {
                AddText("UMA Global Library is empty. Please rebuild library");
                LogLine l = AddText("Rebuild Library");
                l.ButtonAction = (line) => DoLibraryRebuild(l);
                AddText("Please rescan after running library rebuild");
                return;
            }


            var counts = UMAAssetIndexer.Instance.GetCounts();
            foreach (var count in counts)
            {
                AddText($"{count.Key}: ({count.Value}) item(s)");
            }

            var filters = UMAAssetIndexer.Instance.TypeFolderSearch;

            // get a list of keys from the filters
            List<string> types = new List<string>(filters.Keys);

            bool foundAnimatorController = false;
            for(int i=0;i < types.Count; i++)
            {
                if (types[i].ToLower().IndexOf("animatorcontroller") > -1)
                {
                    foundAnimatorController = true;
                    break;
                }
            }



            if (!foundAnimatorController)
            {
                AddText("Warning: No filters are setup for animator controllers! You should setup filters to limit the objects stored in the Asset Index!", LogType.Warning);
                AddText("Warning: Failure to do so could result in more objects stored in resources than needed!", LogType.Warning);
                AddText("Filters are configured using the 'Global Library Filters' option on the UMA menu", LogType.Warning);
            }
            else if (filters.Count == 0)
            {
                AddText("Warning: No filters are setup. You should setup filters to limit the objects stored in the Asset Index!", LogType.Warning);
                AddText("Warning: Failure to do so could result in more objects stored in resources than needed!", LogType.Warning);
                AddText("Filters are configured using the 'Global Library Filters' option on the UMA menu", LogType.Warning);
            }

            AddText("UMA Global Library check complete");
        }

        private void CheckSlots()
        {
            AddText("Checking Slots");
            var slots = UMAAssetIndexer.Instance.GetAssetItems<SlotDataAsset>();

            if (slots == null || slots.Count == 0)
            {
                AddText("No SlotDataAssets found in library", LogType.Warning);
            }
            else
            {
                foreach (var AI in slots)
                {
                    if (AI.Item == null)
                    {
                        AddText($"Error: SlotDataAsset {AI._Name} is missing!", LogType.Error);
                        LogLine l = AddText("Repair Library");
                        l.ButtonAction = (line) => DoLibraryRepair(l);
                    }
                    SlotDataAsset sd = AI.GetItem<SlotDataAsset>();
                    if (sd != null)
                    {
                        if (string.IsNullOrEmpty(sd.slotName))
                        {
                            AddText("Error: Error: SlotDataAsset {AI._Name} has no SlotName. Please fix, then rebuild library.");
                            ReviewAssetItem(AI);
                        }
                        if (sd.meshData != null && sd.meshData.vertices != null && sd.meshData.vertexCount > 0)
                        {
                            if (sd.material == null)
                            {
                                var material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(sd.materialName);
                                if (material != null)
                                {
                                    sd.material = material;
                                    AddText($"Warning: SlotDataAsset {AI._Name} did not have material set. This has been fixed.", LogType.Warning);
                                }
                            }
                            if (sd.material == null) // still not fixed
                            {
                                AddText($"Warning: SlotDataAsset {AI._Name} did not have material set, and Material was not found for slot material named '{sd.material}'", LogType.Error);
                                LogLine l = AddText("Review slot");
                                l.ButtonAction = (line) => ReviewItem(l);
                                l.ReviewItem = AI;
                            }
                        }
                        else
                        {
                            if (sd.isSmooshable)
                            {
                                if (sd.tags == null || sd.tags.Length < 1)
                                {
                                    AddText($"Warning: SlotDataAsset {AI._Name} is marked 'smooshable' but does not have any tags!", LogType.Warning);
                                    AddText("This slot cannot be found by the smoosher!");
                                    LogLine l = AddText("Review slot");
                                    l.ButtonAction = (line) => ReviewItem(l);
                                    l.ReviewItem = AI;
                                }
                            }
                            if (sd.isWildCardSlot)
                            {
                                if (sd.tags == null || sd.tags.Length < 1)
                                {
                                    AddText($"Warning: SlotDataAsset {AI._Name} is marked 'WildCard' but does not have any tags!", LogType.Warning);
                                    AddText("This slot will not find any matches!");
                                    LogLine l = AddText("Review slot");
                                    l.ButtonAction = (line) => ReviewItem(l);
                                    l.ReviewItem = AI;
                                }
                            }
                            if (sd.isClippingPlane && (sd.meshData == null) || sd.meshData.vertexCount < 4)
                            {
                                AddText($"Warning: SlotDataAsset {AI._Name} is marked as a clipping plane, but has no geometry!", LogType.Warning);
                                AddText("This slot will never clip anything!");
                                LogLine l = AddText("Review slot");
                                l.ButtonAction = (line) => ReviewItem(l);
                                l.ReviewItem = AI;
                            }
                        }
                    }
                }
            }
            AddText("Slot check complete");
        }

        private void CheckOverlays()
        {
            AddText("Checking Overlays");
            var overlays = UMAAssetIndexer.Instance.GetAssetItems<OverlayDataAsset>();

            if (overlays == null || overlays.Count == 0)
            {
                AddText("No Overlays found in library", LogType.Warning);
                return;
            }
            else
            {
                foreach (var AI in overlays)
                {
                    if (AI.Item == null)
                    {
                        AddText($"Error: OverlayDataAsset {AI._Name} is missing!", LogType.Error);
                        LogLine l = AddText("Repair Library");
                        l.ButtonAction = (line) => DoLibraryRepair(l);
                        return;
                    }
                    OverlayDataAsset od = AI.GetItem<OverlayDataAsset>();
                    if (string.IsNullOrEmpty(od.overlayName))
                    {
                        AddText("Error: Error: OverlayDataAsset {AI._Name} has no OverlayName. Please fix, then rebuild library.");
                        ReviewAssetItem(AI);
                    }
                    if (od.material == null)
                    {
                        var material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(od.materialName);
                        if (material != null)
                        {
                            od.material = material;
                            AddText($"Warning: SlotDataAsset {AI._Name} did not have material set. This has been fixed.", LogType.Warning);
                        }
                    }
                    if (od.material == null) // still not fixed
                    {
                        AddText($"Warning: OverlayDataAsset {AI._Name} did not have material set, and material was not found for overlay material named {od.materialName}", LogType.Error);
                        LogLine l = AddText("Review overlay");
                        l.ButtonAction = (line) => ReviewItem(l);
                        l.ReviewItem = AI;
                    }
                    else
                    {
                        if (od.textureList == null && od.material.materialType != UMAMaterial.MaterialType.UseExistingMaterial)
                        {
                            AddText($"Warning: OverlayDataAsset {AI._Name} does not have a texture list, and is not set to UseExistingMaterial", LogType.Warning);
                            LogLine l = AddText("Review overlay");
                            l.ButtonAction = (line) => ReviewItem(l);
                            l.ReviewItem = AI;
                        }
                    }

                    if (od.textureCount > 0)
                    {
                        if (od.material != null && od.textureCount != od.material.channels.Length)
                        {
                            AddText("Texture Count on overlay does not match material channel count!", LogType.Error);
                            ReviewAssetItem(AI);
                        }
                        bool texturesOK = true;

                        for (int ii = 0; ii < od.textureCount; ii++)
                        {
                            if (od.textureList[ii] == null)
                            {
                                texturesOK = false;       
                            }
                        }
                        if (!texturesOK)
                        {
                            AddText("Some textures on overlay are missing.", LogType.Warning);
                            AddText("This is OK for overlays that are not a base overlay. Please review to make sure this is what you expect.");
                            ReviewAssetItem(AI);
                        }
                    }
                }
            }
            AddText("Overlay check complete");
        }

        private void ReviewAssetItem(AssetItem AI)
        {
            LogLine l = AddText("Review Overlay");
            l.ButtonAction = (line) => ReviewItem(l);
            l.ReviewItem = AI;
        }

        private void RebuildFromAssetItem(AssetItem AI)
        {
            LogLine l = AddText("Rebuild Library");
            l.ButtonAction = (line) => DoLibraryRebuild(l);
        }

        private void CheckWardrobeCollections()
        {
            AddText("Checking Wardrobe Collections");
            UMAAssetIndexer lib = UMAAssetIndexer.Instance;

            var collections = UMAAssetIndexer.Instance.GetAssetItems<UMAWardrobeCollection>();
            foreach (var c in collections)
            {
                if (c.Item == null)
                {
                    AddText($"Wardrobe Collection {c._Name} was not found. Please repair library and rerun");
                    RebuildFromAssetItem(c);
                }
                UMAWardrobeCollection uwc = c.GetItem<UMAWardrobeCollection>();
                if (uwc == null)
                {
                    AddText($"Wardrobe Collection {c._Name} is not a valid Wardrobe Collection", LogType.Error);
                    ReviewAssetItem(c);
                }
                else
                {
                    bool invalid = false;

                    if (uwc.wardrobeSlot == null)
                    {
                        AddText($"Wardrobe Collection {c._Name} does not have a wardrobe slot assigned", LogType.Error);
                        invalid = true;
                    }
                    if (uwc.arbitraryRecipes != null && uwc.arbitraryRecipes.Count > 0)
                    {
                        foreach (var r in uwc.arbitraryRecipes)
                        {
                            if (!lib.HasAsset<UMAWardrobeRecipe>(r))
                            {
                                AddText($"Wardrobe Collection {c._Name} has an invalid recipe assigned ({r})", LogType.Error);
                                invalid = true;
                            }
                        }
                    }
                    if (uwc.compatibleRaces != null && uwc.compatibleRaces.Count > 0)
                    {
                        foreach (var r in uwc.compatibleRaces)
                        {
                            if (!lib.HasAsset<RaceData>(r))
                            {
                                AddText($"Wardrobe Collection {c._Name} has an invalid race assigned ({r})", LogType.Error);
                                invalid = true;
                            }
                            var raceRecipes = uwc.GetRacesRecipes(r);
                            var raceRecipeNames = uwc.GetRacesRecipeNames(r);
                            for (int ii = 0; ii < raceRecipes.Count; ii++)
                            {
                                if (raceRecipes[ii] == null)
                                {
                                    AddText($"Wardrobe Collection {c._Name} has an invalid recipe '{raceRecipeNames[ii]}' assigned for race {r}", LogType.Error);
                                    invalid = true;
                                }
                            }
                        }
                    }
                    if (invalid)
                    {
                        ReviewAssetItem(c);
                    }
                }
            }
        }
        
        private void CheckWardrobeRecipes()
        {
            UMAAssetIndexer lib = UMAAssetIndexer.Instance;

            AddText("Checking Wardrobe Recipes");
            var recipes = UMAAssetIndexer.Instance.GetAssetItems<UMAWardrobeRecipe>();
            foreach (var r in recipes)
            {
                if (r.Item == null)
                {
                    AddText($"Wardrobe recipe {r._Name} was not found. Please repair library and rerun");
                    RebuildFromAssetItem(r);
                }
                UMAWardrobeRecipe uwr = r.GetItem<UMAWardrobeRecipe>();
                UMAPackedRecipeBase.UMAPackRecipe PackRecipe = uwr.PackedLoad(null);

                bool invalid = false;

                if (string.IsNullOrEmpty(uwr.wardrobeSlot) || uwr.wardrobeSlot.ToLower() == "none")
                {
                    AddText($"Wardrobe Recipe {uwr.name} is not assigned to a wardrobe slot", LogType.Error);
                    invalid = true;
                }
                if (uwr.compatibleRaces == null || uwr.compatibleRaces.Count == 0)
                {
                    AddText($"Wardrobe Recipe {uwr.name} has no races assigned!", LogType.Error);
                    invalid = true;
                }
                else
                {
                    int validcount = 0;
                    foreach (var rn in uwr.compatibleRaces)
                    {
                        if (!lib.HasAsset<RaceData>(rn))
                        {
                            AddText($"Wardrobe Recipe {uwr.name} has an invalid race ({rn}) assigned!", LogType.Error);
                            invalid = true;
                        }
                        else
                        {
                            validcount++;
                        }
                    }
                    if (validcount == 0)
                    {
                        AddText($"Wardrobe Recipe {uwr.name} has no valid races assigned!", LogType.Error);
                        invalid = true;
                    }
                }

                if (invalid)
                {
                    ReviewAssetItem(r);
                }

                var Slots = PackRecipe.slotsV3;
                if (Slots == null)
                {
                    AddText($"Wardrobe Recipe {uwr.name} has no slots assigned!", LogType.Error);
                    ReviewAssetItem(r);
                    continue;
                }
                for (int i = 0; i < Slots.Length; i++)
                {
                    UMAPackedRecipeBase.PackedSlotDataV3 s = Slots[i];
                    if (s == null)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(s.id))
                    {
                        // this is OK
                        continue;
                    }
                    if (!lib.HasAsset<SlotDataAsset>(s.id))
                    {
                        AddText($"Wardrobe Recipe {uwr.name} has a slot '{s.id}' that does not exist in the library!", LogType.Error);
                        AddText("To fix this, restore the missing slot, add it to the library, and then validate the slot", LogType.Error);
                    }
                    else
                    {
                        // if slot is not a utility slot, verify it has overlays assigned.
                        SlotDataAsset sd = lib.GetAsset<SlotDataAsset>(s.id);
                        if (sd.isUtilitySlot || sd.isClippingPlane || sd.isWildCardSlot)
                        {
                            // nothing for now?
                        }
                        else
                        {
                            if (s.overlays == null || s.overlays.Length == 0)
                            {
                                AddText($"Wardrobe Recipe {uwr.name} has a slot '{s.id}' does not have any overlays assigned!", LogType.Warning);
                                ReviewAssetItem(r);
                            }
                        }
                    }
                }
            }
            AddText("Wardrobe Recipe check complete");
        }

        private void CheckTextRecipes()
        {
            UMAAssetIndexer lib = UMAAssetIndexer.Instance;

            AddText("Checking Text Recipes");
            var recipes = UMAAssetIndexer.Instance.GetAssetItems<UMATextRecipe>();
            foreach (var r in recipes)
            {
                if (r.Item == null)
                {
                    AddText($"Text recipe {r._Name} was not found. Please rebuild library and rerun");
                    RebuildFromAssetItem(r);
                }
                UMATextRecipe utr = r.GetItem<UMATextRecipe>();
                UMAPackedRecipeBase.UMAPackRecipe PackRecipe = utr.PackedLoad(null);

                bool invalid = false;

                // is DNA assigned?
                if (string.IsNullOrEmpty(PackRecipe.race))
                {
                    AddText($"Text Recipe {utr.name} does not have an assigned race!");
                    invalid = true;
                }
                else
                {
                    if (!lib.HasAsset<RaceData>(PackRecipe.race))
                    {
                        AddText($"Text Recipe {utr.name} has an invalid race", LogType.Warning);
                        invalid = true;
                    }
                }
                if (PackRecipe.umaDna == null || PackRecipe.umaDna.Count == 0)
                {
                    AddText($"Text Recipe {utr.name} does not have any DNA assigned!");
                    invalid = true;
                }

                if (invalid)
                {
                    ReviewAssetItem(r);
                }

                var Slots = PackRecipe.slotsV3;
                if (Slots == null)
                {
                    AddText($"Text Recipe {utr.name} has no slots assigned!", LogType.Error);
                    ReviewAssetItem(r);
                }
                else
                {
                    for (int i = 0; i < Slots.Length; i++)
                    {
                        UMAPackedRecipeBase.PackedSlotDataV3 s = Slots[i];
                        if (s == null)
                        {                            
                            continue;
                        }
                        if (string.IsNullOrEmpty(s.id))
                        {
                            // this is OK
                            continue;
                        }
                        if (!lib.HasAsset<SlotDataAsset>(s.id))
                        {
                            AddText($"Text Recipe {utr.name} has a slot '{s.id}' that does not exist in the library!", LogType.Error);
                            AddText("To fix this, restore the missing slot, add it to the library, and then validate the slot", LogType.Error);
                        }
                        else
                        {
                            // if slot is not a utility slot, verify it has overlays assigned.
                            SlotDataAsset sd = lib.GetAsset<SlotDataAsset>(s.id);
                            if (sd.isUtilitySlot || sd.isClippingPlane || sd.isWildCardSlot)
                            {
                                // nothing for now?
                            }
                            else
                            {
                                if (s.overlays == null || s.overlays.Length == 0)
                                {
                                    AddText($"Text Recipe {utr.name} has a slot '{s.id}' does not have any overlays assigned!", LogType.Warning);
                                    ReviewAssetItem(r);
                                }
                            }
                        }
                    }
                }
            }
            AddText("Text Recipe check complete");
        }

        private void CheckRaces()
        {
            AddText("Checking Races");
            var races = UMAAssetIndexer.Instance.GetAssetItems<RaceData>();
            foreach (var r in races)
            {
                bool invalid = false;
                if (r.Item == null)
                {
                    AddText($"RaceData {r._Name} was not found. Please rebuild library and rerun", LogType.Error);
                    RebuildFromAssetItem(r);
                    return;
                }
                RaceData race = r.Item as RaceData;
                if (string.IsNullOrEmpty(race.raceName))
                {
                    AddText($"Race {race.name} has no 'raceName' - This has been set to the asset name. ", LogType.Warning);
                    race.raceName = race.name;
                    EditorUtility.SetDirty(race);
                    AssetDatabase.SaveAssetIfDirty(race);
                    ReviewAssetItem(r);
                }
                if (race.dnaConverterList == null || race.dnaConverterList.Length == 0)
                {
                    AddText($"Race {race.name} has no DNA Converters assigned!", LogType.Error);
                    ReviewAssetItem(r);
                }
                else
                {
                    for (int i = 0; i < race.dnaConverterList.Length; i++)
                    {
                        if (race.dnaConverterList[i] == null)
                        {
                            AddText($"DynamicDNAConvertController {i} on Race {race.name} is invalid");
                            invalid = true;
                        }
                        else
                        {
                            var cvt = race.dnaConverterList[i];
                            var dnaasset = cvt.dnaAsset;
                            if (!UMAAssetIndexer.Instance.HasAsset<DynamicUMADnaAsset>(dnaasset.name))
                            {
                                AddText($"DynamicDNAConvertController {i} on Race {dnaasset.name} is not indexed! Adding...", LogType.Warning);
                                var ai = new AssetItem(typeof(DynamicUMADna), dnaasset);
                                UMAAssetIndexer.Instance.AddAssetItem(ai);
                                UMAAssetIndexer.Instance.ForceSave();
                            }
                        }
                    }
                }
                if (race.baseRaceRecipe == null)
                {
                    AddText($"Warning: RaceData {race.raceName} has no base race recipe assigned!", LogType.Error);
                    invalid = true;
                }
                if (invalid)
                {
                    ReviewAssetItem(r);
                }
            }
            AddText("Race check complete");
        }

        private void CheckMaterials()
        {
            AddText("Checking Materials");
            var Mats = UMAAssetIndexer.Instance.GetAssetItems<UMAMaterial>();
            int missingfiles = 0;
            for (int i = 0; i < Mats.Count; i++)
            {
                var ai = Mats[i];
                UMAMaterial mat = ai.Item as UMAMaterial;
                if (mat == null)
                {
                    AddText($"Unable to load UMAMaterial {ai._Name} at path {ai._Path} ");
                    missingfiles++;
                }
                else
                {
                    if (mat.material == null)
                    {
                        AddText($"Error: UMAMaterial {mat.name} has no texture assigned!", LogType.Error);
                        LogLine l = AddText("Inspect Material");
                        l.ReviewItem = ai;
                        l.ButtonAction = (line) => ReviewItem(l);
                    }
                    if (mat.channels.Length == 0 && mat.materialType != UMAMaterial.MaterialType.UseExistingMaterial)
                    {
                        AddText($"Warning: UMAMaterial {mat.name} has no texture channels. Is this expected?", LogType.Warning);
                        LogLine l = AddText("Review Material");
                        l.ReviewItem = ai;
                        l.ButtonAction = (line) => ReviewItem(l);
                    }
                    if (mat.channels.Length > 0 && mat.materialType == UMAMaterial.MaterialType.UseExistingTextures)
                    {
                        bool bad = false;
                        for (int ii = 0; ii < mat.channels.Length; ii++)
                        {
                            var chan = mat.channels[ii];
                            if (chan.channelType != UMAMaterial.ChannelType.TintedTexture)
                            {
                                bad = true;
                                chan.channelType = UMAMaterial.ChannelType.TintedTexture;
                            }
                        }
                        if (bad)
                        {
                            EditorUtility.SetDirty(mat);
                            AssetDatabase.SaveAssetIfDirty(mat);
                            AddText($"Material {mat.name} with 'Use Existing textures' had invalid channel type. Fixed.");
                        }
                    }
                    else
                    {
                        bool bad = false;
                        if (mat.material != null)
                        {
                            List<string> keywords = new List<string>(mat.material.GetTexturePropertyNames());
                            // Check channel keywords vs shader.
                            for (int ii = 0; ii < mat.channels.Length; ii++)
                            {
                                var chan = mat.channels[ii];
                                if (!keywords.Contains(chan.materialPropertyName))
                                {
                                    AddText($"Error: Material {mat.name} channel {ii} has invalid property name");
                                    bad = true;
                                }
                            }
                            if (bad)
                            {
                                LogLine l = AddText("Review Material");
                                l.ButtonAction = (line) => ReviewItem(l);
                                l.ReviewItem = ai;
                            }
                        }
                    }
                }
            }
            AddText("Material check complete");
        }

        #region repairs

        private void ReviewItem(LogLine line)
        {
            StartCoroutine(InspectObject(line.ReviewItem));
            Repaint();
        }

        private IEnumerator InspectObject(AssetItem ai)
        {
            InspectorUtlity.InspectTarget(ai.Item);
            return null;
        }
        private void DoSetAtlasGenerationParms(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>();
            if (generators.Length == 1)
            {
                generators[0].fitAtlas = true;
                generators[0].SharperFitTextures = true;
                generators[0].AtlasOverflowFitMethod = UMAGeneratorBase.FitMethod.BestFitSquare;
                generators[0].SaveAndRestoreIgnoredItems = true;
                generators[0].convertMipMaps = true;
                generators[0].atlasResolution = 2048;
                line.Resolve("Atlas Generation parameters set. Please verify the settings on the generator!");
                Repaint();
            }
        }

        private void DoSetInitialScaleFactor(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>();
            if (generators.Length == 1)
            {
                generators[0].InitialScaleFactor = 1;
                line.Resolve("Initial Scale Factor set");
            }
        }

        private void DoSetEditorInitialScaleFactor(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>();
            if (generators.Length == 1)
            {
                generators[0].editorInitialScaleFactor = 4;
                line.Resolve("Editor Initial Scale Factor set");
            }
        }

        private void DoAddMeshCombiner(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>();
            if (generators.Length == 1)
            {
                UMAMeshCombiner uc = generators[0].gameObject.AddComponent<UMAMeshCombiner>();
                line.Resolve("MeshCombiner added to generator. Be sure to save!");
            }
            else
            {
                line.Error("No or Multiple UMA Generators found in scene!");
            }
        }

        private void DoAddTextureMerge(LogLine line)
        {
            var settings = UMASettings.GetOrCreateSettings();
            // first find the 
            var tx = settings.textureMerge;
            if (tx == null)
            {
                line.Error("Texture Merge not found in project!");
            }
            else
            {
                UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>();
                if (generators.Length == 1)
                {
                    generators[0].textureMerge = tx;
                    line.Resolve("Texture Merge assigned to UMA Generator");
                }
                else
                {
                    line.Error("Multiple UMA Generators found in scene!");
                }
            }
            Repaint();
        }

        private void DoAddGenerator(LogLine line)
        {
            var m_settings = UMASettings.GetOrCreateSettings();
            GameObject go = GameObject.Instantiate(m_settings.generatorPrefab);
            go.name = "UMAGenerator";
            if (line != null)
            {
                line.Resolve("UMA Generator added to scene. Be sure to save.");
                Repaint();
            }
        }

        private void DoActivateGenerator(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsOfType<UMAGenerator>(true);
            if (generators.Length == 1)
            {
                generators[0].gameObject.SetActive(true);
                if (line != null)
                {
                    line.Resolve("UMA Generator activated in scene");
                    Repaint();
                }
            }
        }

        private void DoLibraryRebuild(LogLine line)
        {            
            RebuildLibrary();
            line.Resolve("Library Rebuilt");
        }

        private void DoLibraryRepair(LogLine line)
        {
            UMAAssetIndexer.Instance.RepairAndCleanup();
            line.Resolve("Library Repaired. Please rerun scan");
        }
        #endregion

        private void DoAddToScenePage()
        {
            UMASettings settings = UMASettings.GetOrCreateSettings();

            ClearLog();

            if (settings.characterPrefab == null)
            {
                AddText("Character prefab not found in project settings!", LogType.Error);
                AddText("Please assign a character prefab in the UMASettings object", LogType.Error);
                AddText("By default This is the UMADynamicCharacterAvatar prefab in the 'Getting Started' folder");
                return;
            }



            var generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None);

            if (generators.Length == 0)
            {
                if (settings.generatorPrefab == null)
                {
                    AddText("Generator prefab not found in project settings!", LogType.Error);
                    AddText("Please assign a generator prefab in the UMASettings object", LogType.Error);
                    AddText("By defalt this is the UMA_GLIB prefab in the 'Getting Started' folder");
                    return;
                }
                GameObject gen = GameObject.Instantiate(settings.generatorPrefab);
                gen.name = settings.generatorPrefab.name;
                AddText($"UMA Generator {settings.generatorPrefab.name} added to scene. Be sure to save.");
            }
            else
            {
                AddText("UMA Generator already found in scene - Not added.");
            }
            GameObject go = GameObject.Instantiate(settings.characterPrefab);
            go.name = settings.characterPrefab.name;
            AddText($"UMA Character {settings.characterPrefab.name} added to scene. Be sure to save.");
        }

        private void DoWelcome()
        {
            ClearLog();
            AddLargeText("Welcome to UMA");
            AddText("UMA is a powerful tool for creating performant characters in Unity. ");
            AddText("");
            AddText("If this is the first time after importing a new version, <b>you should rebuild the UMA library</b>");
            AddText("This only takes a minute, but is necessary to make sure UMA knows where everything is.");
            LogLine l = AddText("Rebuild Library after importing new version!");
            AddText("");
            AddText("To get started on your own, click on the <b>'Add UMA an to Current Scene'</b> button to the right");
            AddText("");
            AddText("If you are new to UMA, please check out the <b>'Basics'</b> section to the right");
            AddText("");
            AddText("To check out UMA in action, please open the sample scene using the button to the right");
            AddText("");
            // AddText("We are <b><i>definitely not</i></b> amused");

            AddText("Please join the <b>UMA Discord</b> for help and support (see Links)");
            AddText("You can also check out the <b>UMA Wiki</b> for documentation (see Links)");
            l.ButtonAction = (line) => DoLibraryRebuild(l);
        }

        #region LinksButton
        private void ShowLink(string label, string text, string URL)
        {

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(96));
            if (GUILayout.Button(text, Hyperlink))
            {
                Application.OpenURL(URL);
            }
            GUILayout.EndHorizontal();
        }

        private void DoLinksPage()
        {
            var settings = UMASettings.GetOrCreateSettings();
            ClearLog();
            ShowLink("Invite", "Join the UMA Discord", settings.DiscordInvite);
            ShowLink("Discord", "Go Directly to UMA Discord", settings.DiscordURL);
            ShowLink("Wiki", "UMA Wiki", settings.WikiURL);
            ShowLink("Forum", "UMA Forum", settings.ForumURL);
            ShowLink("Asset Store", "UMA on the Asset Store", settings.AssetStoreURL);
            ShowLink("GitHub", "UMA on GitHub", settings.GithubURL);
            ShowLink("Youtube", "SecretAnorak's UMA Videos", settings.YoutubeURL);
        }
        #endregion

        #region ScenesButton
        private void DoScenesPage()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            Color darkerRect = new Color(PanelColor.r * 0.75f, PanelColor.g * 0.75f, PanelColor.b * 0.75f, 0.5f);

            float ht = 60;
            Rect SceneRect = new Rect(0, 0, ContentRect.width, ht);


            UMAWelcomeScenes scenes = (UMAWelcomeScenes)Resources.Load("UMAWelcomeScenes");
           // UMAWelcomeScenes scenes = (UMAWelcomeScenes)AssetDatabase.LoadAssetAtPath("Assets/UMA/InternalDataStore/Resources/UMAWelcomeScenes.asset", typeof(UMAWelcomeScenes));
            if (scenes != null)
            {
                foreach (var scene in scenes.umaScenes)
                {
                    GUIHelper.BeginInsetArea(PanelColor, SceneRect, 2);
                    DisplayScene(scene, SceneRect);
                    SceneRect.y += ht;
                    GUIHelper.EndInsetArea();
                }
            }
            else
            {
                GUILayout.Label("No welcome scenes found. Please create a UMAWelcomeScenes asset in the project.");
            }
            // create a GUILayout area right at the bottom.
            GUILayout.Label("", GUILayout.Width(ContentRect.width-48), GUILayout.Height(SceneRect.y));
            GUILayout.EndScrollView();
        }

        private void DisplayScene(UMAWelcomeScenes.UMAScene scene, Rect SceneRect)
        {
            float gutter = 2f;
            float sqrSide = SceneRect.height - (gutter * 2.0f);
            Rect TitleRect = new Rect(sqrSide+(gutter * 2), gutter, SceneRect.width - (sqrSide + (gutter*2)), sqrSide);
            Rect InfoRect = new Rect(TitleRect.x, TitleRect.y, TitleRect.width, TitleRect.height);
            Rect textureRect = new Rect(gutter, gutter, sqrSide, sqrSide);

            //GUI.DrawTexture(textureRect, scene.sceneTexture);
            if (GUI.Button(textureRect, new GUIContent(scene.sceneTexture)))
            {
                var sc = EditorSceneManager.OpenScene(scene.scenePath);
            }
            GUI.Label(InfoRect, scene.sceneName, SceneTitleStyle);
            InfoRect.y += EditorGUIUtility.singleLineHeight;
            InfoRect.height -= EditorGUIUtility.singleLineHeight;
            GUI.TextArea(InfoRect, scene.sceneDescription,DescriptionStyle);
        }
        #endregion

        #region simple coroutine
        private void StartCoroutine(IEnumerator routine)
        {
            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = () =>
            {
                if (routine == null)
                {
                    EditorApplication.update -= updateCallback;
                    return;
                }
                if (!routine.MoveNext())
                {
                    EditorApplication.update -= updateCallback;
                    return;
                }
            };
            EditorApplication.update += updateCallback;
        }
        #endregion
    }
}
