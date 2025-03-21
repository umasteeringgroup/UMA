using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA.Editors;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;

namespace UMA
{
    
    public class WelcomeToUMA : EditorWindow
    {

        public static WelcomeToUMA Instance
        {
            get; set;
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

            public LogLine(string message, GUIStyle style, int index, LogType logType = LogType.Info)
            {
                Message = message;
                Style = style;
                this.index = index;
                this.logType = logType;
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

        public Rect HeaderRect;
        public Rect NavigationRect;
        public Rect ContentRect;

        public int currentButton;
        private Vector2 scrollPosition;
        public bool processing = false;
        


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
            ErrorFound.normal.textColor = new Color(0.3f,0,0,1);
            ErrorFound.richText = true;
            ErrorFound.alignment = TextAnchor.MiddleLeft;

            Warning = new GUIStyle(EditorStyles.label);
            Warning.normal.textColor = Color.yellow;
            Warning.richText = true;
            Warning.alignment = TextAnchor.MiddleLeft;

            InfoStyle = new GUIStyle(EditorStyles.label);
            InfoStyle.alignment = TextAnchor.MiddleLeft;
            InfoStyle.richText = true;

            currentButton = 0;
            DoWelcome();
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

        private LogLine AddText(string text, GUIStyle style, LogLineAction buttonAction)
        {
            LogLine line = new(text, style, buttonAction, LoggedItems.Count);
            LoggedItems.Add(line);
            Repaint();
            return line;
        }


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

        private void ScanProject()
        {
            // Check library... if it's empty, rebuild
            // Check library filters.
            // if no filters for animators, then complain
            // make sure there are slots, overlays, racedata assigned
            // 
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
                generators[0].InitialScaleFactor = 4;
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

        private void DoLinksPage()
        {
            var settings = UMASettings.GetOrCreateSettings();
            ClearLog();
            ShowLink("Invite", "Join the UMA Discord", settings.DiscordInvite);
            ShowLink("Discord", "Go Directly to UMA Discord", settings.DiscordURL);
            ShowLink("Wiki", "UMA Wiki", settings.WikiURL);
            ShowLink("Forum", "UMA Forum", settings.ForumURL);
            ShowLink("Asset Store", "UMA on the Asset Store", settings.AssetStoreURL);
        }
        private void StartCoroutine(IEnumerator routine)
        {
            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = () =>
            {
                if (!routine.MoveNext())
                {
                    EditorApplication.update -= updateCallback;
                }
            };
            EditorApplication.update += updateCallback;
        }
    }
}
