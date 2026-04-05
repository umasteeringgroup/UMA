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
            UMASettings settings = null;
            try
            {
                settings = UMASettings.GetOrCreateSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"WelcomeToUMA: Failed to get settings. {ex.Message}");
            }

            if (settings == null)
            {
                EditorApplication.update -= Update;
                return;
            }
            if (settings.showWelcomeToUMA)
            {
                ShowWindow();
            }
            EditorApplication.update -= Update;
        }

        [MenuItem("UMA/Welcome to UMA", false, 0)]
        public static void ShowWindow()
        {
            Texture umaTex = null;
            try
            {
                umaTex = Resources.Load("UMABanner") as Texture;
            }
            catch { /* ignore */ }

            try
            {
                WelcomeToUMA win = EditorWindow.GetWindow<WelcomeToUMA>();
                win.position = new Rect(100, 100, 800, 600);
                win.titleContent = new GUIContent("Welcome to UMA", umaTex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"WelcomeToUMA: Unable to open window. {ex.Message}");
            }
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

            public LogLine(string message, GUIStyle style, LogLineAction buttonAction, int index, LogType logType = LogType.Info)
            {
                Message = message;
                Style = style;
                ButtonAction = buttonAction;
                this.index = index;
                this.logType = logType;
            }

            public void Resolve(string message)
            {
                Message = "---> " + message;
                ButtonAction = null;
                logType = LogType.Resolution;
            }
            public void Error(string message)
            {
                Message = "!!-> " + message;
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

        private static bool IsHiddenInternalShader(string shaderName)
        {
            return !string.IsNullOrEmpty(shaderName) && shaderName.StartsWith("Hidden/Internal", StringComparison.OrdinalIgnoreCase);
        }

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
            try
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
            }
            catch (Exception ex)
            {
                Debug.LogError($"WelcomeToUMA: Failed to initialize styles. {ex.Message}");
            }

            try
            {
                initialSettings = UMASettings.GetOrCreateSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"WelcomeToUMA: Failed to load UMASettings. {ex.Message}");
                initialSettings = null;
            }

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
            UMASettings settings = null;
            try
            {
                settings = UMASettings.GetOrCreateSettings();
            }
            catch { /* ignore */ }

            GUIHelper.BeginInsetArea(PanelColor, HeaderRect, 2, 0, 4);
            var version = settings != null && !string.IsNullOrEmpty(settings.UMAVersion) ? settings.UMAVersion : "UMA";
            EditorGUILayout.LabelField($"Welcome to {version}", ActiveLargeStyle);
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
                AddText("UMA uses a generator to create characters. This is a scriptable object.");
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
                if (initialSettings != null && initialSettings.Slots != null)
                {
                    AddImage(initialSettings.Slots, "");
                }
                else
                {
                    AddText("(Preview image not available)", LogType.Info);
                }
                AddText("A SlotData contains a mesh part, along with any rig parts needed.");
                AddText("These are combined into a Skinned Mesh when the character is built.");

                AddText(" ");
                AddText("   <b>OverlayData:</b>");
                if (initialSettings != null && initialSettings.Overlays != null)
                {
                    AddImage(initialSettings.Overlays, "");
                }
                else
                {
                    AddText("(Preview image not available)", LogType.Info);
                }
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
                currentButton = 5;
            }
            if (initialSettings != null && initialSettings.showWelcomeToUMA)
            {
                if (GUILayout.Button("Turn this off!!"))
                {
                    currentButton = 9;
                    ClearLog();
                    UMASettings settings = null;
                    try
                    {
                        settings = UMASettings.GetOrCreateSettings();
                    }
                    catch { /* ignore */ }
                    if (settings != null)
                    {
                        settings.showWelcomeToUMA = false;
                        EditorUtility.SetDirty(settings);
                        AddText("The welcome window will no longer show when Unity is opened");
                        AddText("To view it at any time, you can use the 'UMA/Welcome to UMA' menu item");
                        AddText("You can re-enable this in the UMA project settings.");
                    }
                    else
                    {
                        AddText("Unable to update UMASettings to turn off welcome screen.", LogType.Error);
                    }
                }
            }
            GUILayout.EndVertical();
            GUIHelper.EndInsetArea();
        }

        private void ReimportShaderFolder()
        {
            ClearLog();

            string path = null;
            try
            {
                path = UMAEditorUtilities.FindUMAFullPath();
            }
            catch (Exception ex)
            {
                AddText($"Error locating UMA folder: {ex.Message}", LogType.Error);
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                AddText("UMA folder path is empty.", LogType.Error);
                return;
            }

            try
            {
                path = Path.Combine(path, "Core", "ShaderPackages");
            }
            catch (Exception ex)
            {
                AddText($"Error building shader path: {ex.Message}", LogType.Error);
                return;
            }

            if (Directory.Exists(path))
            {
                AddText($"Reimporting shaders in {path}");
                try
                {
                    StartProcessing();
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.DontDownloadFromCacheServer | ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
                    StopProcessing();
                }
                catch (Exception ex)
                {
                    StopProcessing();
                    AddText($"Error during shader reimport: {ex.Message}", LogType.Error);
                    return;
                }

                AddText(path + " reimported successfully!");

                // After shader reimport, fix up materials via all MaterialShaderRegistry assets
                int registryCount = 0;
                int totalMaterials = 0;
                int reassigned = 0;
                var unresolvedErrorMats = new List<string>();

                bool IsErrorShader(Shader s)
                {
                    if (s == null) return false;
                    var n = s.name ?? string.Empty;
                    return n.Equals("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase)
                           || n.StartsWith("Hidden/Internal", StringComparison.OrdinalIgnoreCase);
                }

                try
                {
                    var registryGuids = AssetDatabase.FindAssets("t:MaterialShaderRegistry");
                    foreach (var guid in registryGuids)
                    {
                        var regPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(regPath)) continue;

                        var registry = AssetDatabase.LoadAssetAtPath<MaterialShaderRegistry>(regPath);
                        if (registry == null) continue;

                        registryCount++;
                        registry.BuildIndex();

                        var entries = registry.Entries;
                        if (entries == null) continue;

                        foreach (var e in entries)
                        {
                            if (e == null) continue;

                            var mat = e.material;
                            if (mat == null) continue;

                            totalMaterials++;

                            // If the material is on the error shader AND we have no original shader name, notify and skip resolution.
                            if (IsErrorShader(mat.shader) && (string.IsNullOrEmpty(e.shaderName) && e.shader == null))
                            {
                                unresolvedErrorMats.Add(mat.name);
                                continue;
                            }

                            // Resolve shader: prefer by stored name after reimport
                            Shader resolved = null;
                            if (!string.IsNullOrEmpty(e.shaderName))
                            {
                                resolved = Shader.Find(e.shaderName);
                            }

                            if (resolved != null && mat.shader != resolved)
                            {
                                mat.shader = resolved;
                                EditorUtility.SetDirty(mat);
                                reassigned++;
                            }

                            // Keep registry entry synchronized if possible
                            if (e.shader == null && resolved != null)
                            {
                                e.shader = resolved;
                                EditorUtility.SetDirty(registry);
                            }
                            if (resolved != null && !string.IsNullOrEmpty(resolved.name) && e.shaderName != resolved.name && !IsHiddenInternalShader(resolved.name))
                            {
                                e.shaderName = resolved.name;
                                EditorUtility.SetDirty(registry);
                            }
                        }
                    }

                    AssetDatabase.SaveAssets();
                }
                catch (Exception ex)
                {
                    AddText($"Error while resolving shaders/materials: {ex.Message}", LogType.Error);
                }

                // If any materials are stuck on the error shader without a known original name, alert the user.
                if (unresolvedErrorMats.Count > 0)
                {
                    string list = string.Join("\n - ", unresolvedErrorMats);
                    string msg = "The following materials are using the error shader and cannot be resolved because the original shader name is not available:\n - " + list + "\n\nPlease update their MaterialShaderRegistry entries with the correct shader name.";
                    EditorUtility.DisplayDialog("UMA Shader Resolution Error", msg, "OK");
                    AddText("Some materials could not be resolved and are using the error shader:", LogType.Error);
                    foreach (var m in unresolvedErrorMats)
                    {
                        AddText($" - {m}", LogType.Error);
                    }
                }

                // Rebuild all edit-time UMAs to pick up shader/material changes
                try
                {
                    var avatars = UMAUpdateProcessor.GetSceneEditTimeAvatars();
                    int rebuilt = 0;
                    foreach (var dca in avatars)
                    {
                        if (dca != null && dca.editorTimeGeneration)
                        {
                            dca.GenerateSingleUMA();
                            rebuilt++;
                        }
                    }
                    AddText($"Rebuilt {rebuilt} edit-time UMA(s).");
                }
                catch (Exception ex)
                {
                    AddText($"Error rebuilding edit-time UMAs: {ex.Message}", LogType.Error);
                }

                AddText($"MaterialShaderRegistry processed: {registryCount} asset(s).");
                AddText($"Materials scanned: {totalMaterials}, shaders reassigned: {reassigned}.");
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
            UMAAssetIndexer UAI = null;
            try
            {
                UAI = UMAAssetIndexer.Instance;
            }
            catch (Exception ex)
            {
                AddText($"Error accessing UMAAssetIndexer: {ex.Message}", LogType.Error);
                return;
            }

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
                if (counts != null)
                {
                    foreach (var count in counts)
                    {
                        AddText($"{count.Key}: ({count.Value}) item(s)");
                    }
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

            string basePath = null;
            try
            {
                basePath = UMAEditorUtilities.FindUMAFullPath();
            }
            catch (Exception ex)
            {
                AddText($"Error finding UMA base path: {ex.Message}", LogType.Error);
                return;
            }

            if (string.IsNullOrEmpty(basePath))
            {
                AddText("UMA base path not found.", LogType.Error);
                return;
            }

            string path = string.Empty;
            try
            {
                path = Path.Combine(basePath, "UMA Documentation.PDF");
            }
            catch (Exception ex)
            {
                AddText($"Error building documentation path: {ex.Message}", LogType.Error);
                return;
            }

            try
            {
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
            catch (Exception ex)
            {
                AddText($"Error opening documentation: {ex.Message}", LogType.Error);
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

            if (LoggedItems == null) return;

            foreach (var item in LoggedItems)
            {
                if (item == null) continue;

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
                if (item.ButtonAction != null)
                {
                    if (GUILayout.Button(item.Message ?? string.Empty))
                    {
                        ButtonAction = item.ButtonAction;
                        ButtonActionLine = item;
                    }
                }
                else
                {
                    GUILayout.Label(item.Message ?? string.Empty, item.Style ?? InfoStyle);
                }
                GUILayout.EndHorizontal();
            }
            if (ButtonAction != null && ButtonActionLine != null)
            {
                try
                {
                    ButtonAction(ButtonActionLine);
                }
                catch (Exception ex)
                {
                    AddText($"Button action failed: {ex.Message}", LogType.Error);
                }
            }
        }

        private void ClearLog()
        {
            if (LoggedItems == null) LoggedItems = new List<LogLine>();
            LoggedItems.Clear();
            Repaint();
        }

        private LogLine AddLargeText(string text)
        {
            if (LoggedItems == null) LoggedItems = new List<LogLine>();
            LogLine line = new LogLine(text ?? string.Empty, ActiveLargeStyle, LoggedItems.Count);
            LoggedItems.Add(line);
            Repaint();
            return line;
        }

        private void AddSeperator()
        {
            AddText("--------------------------------------------------", LogType.None);
        }

        private LogLine AddText(string text, LogType logType = LogType.Info, GUIStyle style = null)
        {
            if (LoggedItems == null) LoggedItems = new List<LogLine>();
            if (style == null)
            {
                LogLine line = new(text ?? string.Empty, InfoStyle, LoggedItems.Count, logType);
                LoggedItems.Add(line);
                Repaint();
                return line;
            }
            else
            {
                LogLine line = new(text ?? string.Empty, style, LoggedItems.Count, logType);
                LoggedItems.Add(line);
                Repaint();
                return line;
            }
        }

        private LogLine AddImage(Texture2D image, string message)
        {
            if (LoggedItems == null) LoggedItems = new List<LogLine>();
            LogLine line = new LogLine("", InfoStyle, LoggedItems.Count);
            line.Image = image;
            LoggedItems.Add(line);
            Repaint();
            return line;
        }

        private LogLine AddText(string text, GUIStyle style, LogLineAction buttonAction)
        {
            if (LoggedItems == null) LoggedItems = new List<LogLine>();
            LogLine line = new(text ?? string.Empty, style ?? InfoStyle, buttonAction, LoggedItems.Count);
            LoggedItems.Add(line);
            Repaint();
            return line;
        }

        #region Scene Scan Button
        private void ScanScene()
        {
            UMAGenerator[] generators;
            try
            {
                generators = FindObjectsByType<UMAGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            catch (Exception ex)
            {
                AddText($"Error scanning scene for UMAGenerator: {ex.Message}", LogType.Error);
                return;
            }

            AddText("Checking for generator");
            if (generators == null || generators.Length == 0)
            {
                AddText("UMA Generator not found in scene", LogType.Error);
                LogLine l = AddText(text: "Add UMA Generator to Scene", LogType.Error);
                l.ButtonAction = (line) => DoAddGenerator(l);
            }
            else if (generators.Length > 1)
            {
                AddText("Multiple UMA Generators found in scene!", LogType.Error);
                AddText("This can cause problems, please remove all but one generator from the scene", LogType.Error);
#if UNITY_6000_0_OR_NEWER
#else
                AddText("Note: You can use the 'Filter' field in the hierarchy with t:UMAGENARATOR to find them", LogType.Error);
#endif
            }
            else
            {
                UMAGenerator gen = generators[0];
                if (gen == null || gen.gameObject == null)
                {
                    AddText("UMA Generator reference is invalid", LogType.Error);
                    return;
                }

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
            }
        }
        #endregion

        private void ScanProject()
        {
            AddText("Checking library");
            UMAAssetIndexer indexer = null;
            try { indexer = UMAAssetIndexer.Instance; } catch { /* ignore */ }

            if (indexer == null)
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
            UMAAssetIndexer idx = null;
            try { idx = UMAAssetIndexer.Instance; } catch { /* ignore */ }

            if (idx == null || !idx.IsValid())
            {
                AddText("UMA Global Library is empty. Please rebuild library");
                LogLine l = AddText("Rebuild Library");
                l.ButtonAction = (line) => DoLibraryRebuild(l);
                AddText("Please rescan after running library rebuild");
                return;
            }

            Dictionary<string, int> counts = null;
            try { counts = idx.GetCounts(); } catch { /* ignore */ }
            if (counts != null)
            {
                foreach (var count in counts)
                {
                    AddText($"{count.Key}: ({count.Value}) item(s)");
                }
            }

            var filters = idx.TypeFolderSearch ?? new Dictionary<string, List<string>>();
            List<string> types = new List<string>(filters.Keys);

            bool foundAnimatorController = false;
            for (int i = 0; i < types.Count; i++)
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

        private static bool NormalizeTags(ref string[] tags)
        {
            if (tags == null || tags.Length == 0) return false;

            List<string> result = new List<string>(tags.Length);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;

            for (int i = 0; i < tags.Length; i++)
            {
                string t = tags[i] ?? "";
                string trimmed = t.Trim();
                if (trimmed.Length == 0)
                {
                    if (!string.IsNullOrEmpty(t)) changed = true;
                    continue;
                }
                if (!seen.Contains(trimmed))
                {
                    seen.Add(trimmed);
                    result.Add(trimmed);
                }
                else
                {
                    changed = true;
                }
            }

            if (changed)
            {
                tags = result.ToArray();
            }
            return changed;
        }

        private void CheckSlots()
        {
            AddText("Checking Slots");
            List<AssetItem> slots = null;
            try { slots = UMAAssetIndexer.Instance.GetAssetItems<SlotDataAsset>(); } catch { /* ignore */ }

            if (slots == null || slots.Count == 0)
            {
                AddText("No SlotDataAssets found in library", LogType.Warning);
            }
            else
            {
                foreach (var AI in slots)
                {
                    if (AI == null)
                    {
                        continue;
                    }
                    if (AI.Item == null)
                    {
                        AddText($"Error: SlotDataAsset {AI._Name} is missing!", LogType.Error);
                        LogLine l = AddText("Repair Library");
                        l.ButtonAction = (line) => DoLibraryRepair(l);
                    }
                    SlotDataAsset sd = null;
                    try { sd = AI.GetItem<SlotDataAsset>(); } catch { /* ignore */ }

                    if (sd != null)
                    {
                        if (string.IsNullOrEmpty(sd.slotName))
                        {
                            AddText($"Error: Error: SlotDataAsset {AI._Name} has no SlotName. Please fix, then rebuild library.");
                            ReviewAssetItem(AI, "SlotDataAsset");
                        }

                        // Normalize and deduplicate tags if present
                        if (sd.tags != null && sd.tags.Length > 0)
                        {
                            string[] oldTags = sd.tags;
                            if (NormalizeTags(ref sd.tags))
                            {
                                EditorUtility.SetDirty(sd);
                                AssetDatabase.SaveAssetIfDirty(sd);
                                AddText($"Normalized tags for SlotDataAsset '{AI._Name}'.");
                            }
                        }

                     if (sd.meshData != null && sd.meshData.vertices != null && sd.meshData.vertexCount > 0)
                        {
                            // SlotDataAsset materials are now derived from overlays at the SlotData level.
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
                            if (sd.isWildCardSlot && sd.slotName.ToLower() != "wildcard")
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
                            if (sd.isClippingPlane && (sd.meshData == null || sd.meshData.vertexCount < 4))
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
            List<AssetItem> overlays = null;
            try { overlays = UMAAssetIndexer.Instance.GetAssetItems<OverlayDataAsset>(); } catch { /* ignore */ }

            if (overlays == null || overlays.Count == 0)
            {
                AddText("No Overlays found in library", LogType.Warning);
                return;
            }
            else
            {
                foreach (var AI in overlays)
                {
                    if (AI == null)
                    {
                        continue;
                    }
                    if (AI.Item == null)
                    {
                        AddText($"Error: OverlayDataAsset {AI._Name} is missing!", LogType.Error);
                        LogLine l = AddText("Repair Library");
                        l.ButtonAction = (line) => DoLibraryRepair(l);
                        return;
                    }
                    OverlayDataAsset od = null;
                    try { od = AI.GetItem<OverlayDataAsset>(); } catch { /* ignore */ }

                    if (od == null)
                    {
                        AddText($"Error: OverlayDataAsset entry invalid: {AI._Name}", LogType.Error);
                        continue;
                    }

                    if (string.IsNullOrEmpty(od.overlayName))
                    {
                        AddText("Error: Error: OverlayDataAsset {AI._Name} has no OverlayName. Please fix, then rebuild library.");
                        ReviewAssetItem(AI, "OverlayDataAsset");
                    }

                    // Auto-fix materialName if material assigned
                    if (od.material != null)
                    {
                        if (string.IsNullOrEmpty(od.materialName) || od.materialName != od.material.name)
                        {
                            od.materialName = od.material.name;
                            EditorUtility.SetDirty(od);
                            AssetDatabase.SaveAssetIfDirty(od);
                            AddText($"Fixed OverlayDataAsset '{AI._Name}' materialName to '{od.materialName}'.");
                        }
                    }

                    if (od.material == null)
                    {
                        UMAMaterial material = null;
                        try { material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(od.materialName); } catch { /* ignore */ }
                        if (material != null)
                        {
                            od.material = material;
                            AddText($"Warning: OverlayDataAsset {AI._Name} did not have material set. This has been fixed.", LogType.Warning);
                            // also sync name
                            if (string.IsNullOrEmpty(od.materialName) || od.materialName != material.name)
                            {
                                od.materialName = material.name;
                            }
                            EditorUtility.SetDirty(od);
                            AssetDatabase.SaveAssetIfDirty(od);
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
                            AddText($"Texture Count on overlay {AI._Name} does not match material channel count ({od.textureCount} vs {od.material.channels.Length})!", LogType.Error);
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

        private void ReviewAssetItem(AssetItem AI, string type = "")
        {
            if (AI == null)
            {
                AddText("Cannot review null asset item.", LogType.Error);
                return;
            }

            if (type == "")
            {
                type = AI._BaseTypeName;
            }
            LogLine l = AddText($"Review {type}");
            l.ButtonAction = (line) => ReviewItem(l);
            l.ReviewItem = AI;
        }

        private void RebuildFromAssetItem(AssetItem AI)
        {
            if (AI == null)
            {
                AddText("Cannot rebuild from null asset item.", LogType.Error);
                return;
            }
            LogLine l = AddText("Rebuild Library");
            l.ButtonAction = (line) => DoLibraryRebuild(l);
        }

        private void CheckWardrobeCollections()
        {
            AddText("Checking Wardrobe Collections");
            UMAAssetIndexer lib = null;
            try { lib = UMAAssetIndexer.Instance; } catch { /* ignore */ }
            if (lib == null)
            {
                AddText("UMAAssetIndexer unavailable.", LogType.Error);
                return;
            }

            var collections = lib.GetAssetItems<UMAWardrobeCollection>();
            foreach (var c in collections)
            {
                if (c == null) continue;

                if (c.Item == null)
                {
                    AddText($"Wardrobe Collection {c._Name} was not found. Please repair library and rerun");
                    RebuildFromAssetItem(c);
                }
                UMAWardrobeCollection uwc = null;
                try { uwc = c.GetItem<UMAWardrobeCollection>(); } catch { /* ignore */ }

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
                            if (raceRecipes != null)
                            {
                                for (int ii = 0; ii < raceRecipes.Count; ii++)
                                {
                                    if (raceRecipes[ii] == null)
                                    {
                                        AddText($"Wardrobe Collection {c._Name} has an invalid recipe '{raceRecipeNames?[ii]}' assigned for race {r}", LogType.Error);
                                        invalid = true;
                                    }
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
            UMAAssetIndexer lib = null;
            try { lib = UMAAssetIndexer.Instance; } catch { /* ignore */ }
            if (lib == null)
            {
                AddText("UMAAssetIndexer unavailable.", LogType.Error);
                return;
            }

            AddText("Checking Wardrobe Recipes");
            var recipes = lib.GetAssetItems<UMAWardrobeRecipe>();
            foreach (var r in recipes)
            {
                if (r == null) continue;

                if (r.Item == null)
                {
                    AddText($"Wardrobe recipe {r._Name} was not found. Please repair library and rerun");
                    RebuildFromAssetItem(r);
                }
                UMAWardrobeRecipe uwr = null;
                try { uwr = r.GetItem<UMAWardrobeRecipe>(); } catch { /* ignore */ }

                if (uwr == null)
                {
                    AddText($"Wardrobe recipe entry invalid: {r._Name}", LogType.Error);
                    continue;
                }

                UMAPackedRecipeBase.UMAPackRecipe PackRecipe = null;
                try { PackRecipe = uwr.PackedLoad(); } catch { /* ignore */ }

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

                var Slots = PackRecipe?.slotsV3;
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
                        continue;
                    }
                    if (!lib.HasAsset<SlotDataAsset>(s.id))
                    {
                        AddText($"Wardrobe Recipe {uwr.name} has a slot '{s.id}' that does not exist in the library!", LogType.Error);
                        AddText("To fix this, restore the missing slot, add it to the library, and then validate the slot", LogType.Error);
                    }
                    else
                    {
                        SlotDataAsset sd = null;
                        try { sd = lib.GetAsset<SlotDataAsset>(s.id); } catch { /* ignore */ }

                        if (sd != null && !(sd.isUtilitySlot || sd.isClippingPlane || sd.isWildCardSlot))
                        {
                            if (s.overlays == null || s.overlays.Length == 0)
                            {
                                AddText($"Wardrobe Recipe {uwr.name} has a slot '{s.id}' does not have any overlays assigned!", LogType.Warning);
                                ReviewAssetItem(r);
                            }
                            else
                            {
                                // Validate overlay references exist in the library
                                for (int oi = 0; oi < s.overlays.Length; oi++)
                                {
                                    var ov = s.overlays[oi];
                                    if (ov == null || string.IsNullOrEmpty(ov.id)) continue;
                                    if (!lib.HasAsset<OverlayDataAsset>(ov.id))
                                    {
                                        AddText($"Wardrobe Recipe {uwr.name} slot '{s.id}' references missing Overlay '{ov.id}'!", LogType.Error);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            AddText("Wardrobe Recipe check complete");
        }

        private void CheckTextRecipes()
        {
            UMAAssetIndexer lib = null;
            try { lib = UMAAssetIndexer.Instance; } catch { /* ignore */ }
            if (lib == null)
            {
                AddText("UMAAssetIndexer unavailable.", LogType.Error);
                return;
            }

            AddText("Checking Text Recipes");
            var recipes = lib.GetAssetItems<UMATextRecipe>();
            foreach (var r in recipes)
            {
                if (r == null) continue;

                if (r.Item == null)
                {
                    AddText($"Text recipe {r._Name} was not found. Please rebuild library and rerun");
                    RebuildFromAssetItem(r);
                }
                UMATextRecipe utr = null;
                try { utr = r.GetItem<UMATextRecipe>(); } catch { /* ignore */ }

                if (utr == null)
                {
                    AddText($"Text Recipe entry invalid: {r._Name}", LogType.Error);
                    continue;
                }

                UMAPackedRecipeBase.UMAPackRecipe PackRecipe = null;
                try { PackRecipe = utr.PackedLoad(); } catch { /* ignore */ }

                bool invalid = false;

                if (string.IsNullOrEmpty(PackRecipe?.race))
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
                if (PackRecipe == null || PackRecipe.umaDna == null || PackRecipe.umaDna.Count == 0)
                {
                    AddText($"Text Recipe {utr.name} does not have any DNA assigned!");
                    invalid = true;
                }

                if (invalid)
                {
                    ReviewAssetItem(r);
                }

                var Slots = PackRecipe?.slotsV3;
                var Slot2 = PackRecipe?.slotsV2;

                if (Slots == null && Slot2 == null)
                {
                    AddText($"Text Recipe {utr.name} has no slots assigned!", LogType.Error);
                    ReviewAssetItem(r);
                }
                else
                {
                    if (Slots != null)
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
                                continue;
                            }
                            if (!lib.HasAsset<SlotDataAsset>(s.id))
                            {
                                AddText($"Text Recipe {utr.name} has a slot '{s.id}' that does not exist in the library!", LogType.Error);
                                AddText("To fix this, restore the missing slot, add it to the library, and then validate the slot", LogType.Error);
                            }
                            else
                            {
                                SlotDataAsset sd = null;
                                try { sd = lib.GetAsset<SlotDataAsset>(s.id); } catch { /* ignore */ }

                                if (sd != null && !(sd.isUtilitySlot || sd.isClippingPlane || sd.isWildCardSlot))
                                {
                                    if (s.overlays == null || s.overlays.Length == 0)
                                    {
                                        AddText($"Text Recipe {utr.name} has a slot '{s.id}' does not have any overlays assigned!", LogType.Warning);
                                        ReviewAssetItem(r);
                                    }
                                    else
                                    {
                                        // Validate overlay references exist in the library
                                        for (int oi = 0; oi < s.overlays.Length; oi++)
                                        {
                                            var ov = s.overlays[oi];
                                            if (ov == null || string.IsNullOrEmpty(ov.id)) continue;
                                            if (!lib.HasAsset<OverlayDataAsset>(ov.id))
                                            {
                                                AddText($"Text Recipe {utr.name} slot '{s.id}' references missing Overlay '{ov.id}'!", LogType.Error);
                                            }
                                        }
                                    }
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
            List<AssetItem> races = null;
            try { races = UMAAssetIndexer.Instance.GetAssetItems<RaceData>(); } catch { /* ignore */ }

            if (races == null)
            {
                AddText("Unable to enumerate races.", LogType.Error);
                return;
            }

            foreach (var r in races)
            {
                bool invalid = false;
                if (r == null || r.Item == null)
                {
                    AddText($"RaceData {r?._Name ?? "(unknown)"} was not found. Please rebuild library and rerun", LogType.Error);
                    if (r != null) RebuildFromAssetItem(r);
                    return;
                }
                RaceData race = r.Item as RaceData;
                if (race == null)
                {
                    AddText($"Invalid RaceData entry: {r._Name}", LogType.Error);
                    continue;
                }
                if (!string.IsNullOrEmpty(race._oldRaceName))
                {
                    AddText($"Race {race.name} is using the legacy 'raceName'", LogType.Warning);
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
                            if (dnaasset != null && !UMAAssetIndexer.Instance.HasAsset<DynamicUMADnaAsset>(dnaasset.name))
                            {
                                AddText($"DynamicDNAConvertController {i} on Race {dnaasset.name} is not indexed! Adding...", LogType.Warning);
                                var ai = new AssetItem(typeof(DynamicUMADna), dnaasset);
                                UMAAssetIndexer.Instance.AddAssetItem(ai);
                                UMAAssetIndexer.Instance.ForceSave();
                            }
                        }
                    }
                }
                // Validate base race recipe alignment with race name
                if (race.baseRaceRecipe != null)
                {
                    try
                    {
                        var packedBase = race.baseRaceRecipe as UMAPackedRecipeBase;
                        if (packedBase != null)
                        {
                            var pack = packedBase.PackedLoad();
                            if (pack != null && !string.IsNullOrEmpty(pack.race) && !string.Equals(pack.race, race.raceName, StringComparison.Ordinal))
                            {
                                AddText($"Warning: Base race recipe for '{race.raceName}' is set up for race '{pack.race}'. Verify this is intended.", LogType.Warning);
                                ReviewAssetItem(r);
                            }
                        }
                        else
                        {
                            // Base recipe is not a packed recipe type we can inspect
                            AddText($"Warning: Base race recipe for '{race.raceName}' is not a packed recipe type (UMAPackedRecipeBase).", LogType.Warning);
                        }
                    }
                    catch { /* ignore */ }
                }
                else
                {
                    AddText($"Warning: RaceData {race.raceName} has no base race recipe assigned!", LogType.Error);
                    invalid = true;
                }

                // Validate cross compatible races exist
                try
                {
                    var compat = race.GetCrossCompatibleRaces();
                    if (compat != null && compat.Count > 0)
                    {
                        foreach (var rn in compat)
                        {
                            if (!UMAAssetIndexer.Instance.HasAsset<RaceData>(rn))
                            {
                                AddText($"Warning: Race '{race.raceName}' lists cross-compatible race '{rn}' which is not in the library.", LogType.Warning);
                            }
                        }
                    }
                }
                catch { /* some races may not implement or may return null */ }

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
            if (line == null || line.ReviewItem == null)
            {
                AddText("Nothing selected to inspect.", LogType.Warning);
                return;
            }
            StartCoroutine(InspectObject(line.ReviewItem));
            Repaint();
        }

        private IEnumerator InspectObject(AssetItem ai)
        {
            if (ai == null || ai.Item == null) yield break;
            InspectorUtlity.InspectTarget(ai.Item);
            yield break;
        }
        private void DoSetAtlasGenerationParms(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None);
            if (generators.Length == 1 && generators[0] != null)
            {
                generators[0].fitAtlas = true;
                generators[0].SharperFitTextures = true;
                generators[0].AtlasOverflowFitMethod = UMAGeneratorBase.FitMethod.BestFitSquare;
                generators[0].SaveAndRestoreIgnoredItems = true;
                generators[0].convertMipMaps = true;
                generators[0].atlasResolution = 2048;
                line?.Resolve("Atlas Generation parameters set. Please verify the settings on the generator!");
                Repaint();
            }
            else
            {
                line?.Error("No or Multiple UMA Generators found in scene!");
            }
        }

        private void DoSetInitialScaleFactor(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None);
            if (generators.Length == 1 && generators[0] != null)
            {
                generators[0].InitialScaleFactor = 1;
                line?.Resolve("Initial Scale Factor set");
            }
            else
            {
                line?.Error("No or Multiple UMA Generators found in scene!");
            }
        }

        private void DoSetEditorInitialScaleFactor(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None);
            if (generators.Length == 1 && generators[0] != null)
            {
                generators[0].editorInitialScaleFactor = 4;
                line?.Resolve("Editor Initial Scale Factor set");
            }
            else
            {
                line?.Error("No or Multiple UMA Generators found in scene!");
            }
        }

        private void DoAddMeshCombiner(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None);
            if (generators.Length == 1 && generators[0] != null)
            {
                UMAMeshCombiner uc = generators[0].gameObject.AddComponent<UMAMeshCombiner>();
                line?.Resolve("MeshCombiner added to generator. Be sure to save!");
            }
            else
            {
                line?.Error("No or Multiple UMA Generators found in scene!");
            }
        }

        private void DoAddTextureMerge(LogLine line)
        {
            var settings = UMASettings.GetOrCreateSettings();
            var tx = settings != null ? settings.textureMerge : null;
            if (tx == null)
            {
                line?.Error("Texture Merge not found in project!");
            }
            else
            {
                UMAGenerator[] generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None);
                if (generators.Length == 1 && generators[0] != null)
                {
                    generators[0].textureMerge = tx;
                    line?.Resolve("Texture Merge assigned to UMA Generator");
                }
                else
                {
                    line?.Error("No or Multiple UMA Generators found in scene!");
                }
            }
            Repaint();
        }

        private void DoAddGenerator(LogLine line)
        {
            UMASettings m_settings = null;
            try { m_settings = UMASettings.GetOrCreateSettings(); } catch { /* ignore */ }
            if (m_settings == null || m_settings.generatorPrefab == null)
            {
                line?.Error("Generator prefab not found in project settings!");
                return;
            }
            GameObject go = null;
            try
            {
                go = GameObject.Instantiate(m_settings.generatorPrefab);
                if (go != null) go.name = "UMAGenerator";
                line?.Resolve("UMA Generator added to scene. Be sure to save.");
                Repaint();
            }
            catch (Exception ex)
            {
                line?.Error($"Failed to add UMA Generator: {ex.Message}");
            }
        }

        private void DoActivateGenerator(LogLine line)
        {
            UMAGenerator[] generators = FindObjectsByType<UMAGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (generators.Length == 1 && generators[0] != null && generators[0].gameObject != null)
            {
                generators[0].gameObject.SetActive(true);
                if (line != null)
                {
                    line.Resolve("UMA Generator activated in scene");
                    Repaint();
                }
            }
            else
            {
                line?.Error("No or Multiple UMA Generators found in scene!");
            }
        }

        private void DoLibraryRebuild(LogLine line)
        {
            RebuildLibrary();
            line?.Resolve("Library Rebuilt");
        }

        private void DoLibraryRepair(LogLine line)
        {
            try
            {
                UMAAssetIndexer.Instance.RepairAndCleanup();
                line?.Resolve("Library Repaired. Please rerun scan");
            }
            catch (Exception ex)
            {
                line?.Error($"Library repair failed: {ex.Message}");
            }
        }
        #endregion

        private void DoAddToScenePage()
        {
            UMASettings settings = null;
            try { settings = UMASettings.GetOrCreateSettings(); } catch { /* ignore */ }

            ClearLog();

            if (settings == null)
            {
                AddText("UMASettings not found!", LogType.Error);
                return;
            }

            if (settings.characterPrefab == null)
            {
                AddText("Character prefab not found in project settings!", LogType.Error);
                AddText("Please assign a character prefab in the UMASettings object", LogType.Error);
                AddText("By default This is the UMADynamicCharacterAvatar prefab in the 'Getting Started' folder");
                return;
            }

            UMAGenerator[] generators = null;
            try { generators = FindObjectsByType<UMAGenerator>(FindObjectsSortMode.None); } catch { /* ignore */ }
            generators = generators ?? Array.Empty<UMAGenerator>();

            if (generators.Length == 0)
            {
                if (settings.generatorPrefab == null)
                {
                    AddText("Generator prefab not found in project settings!", LogType.Error);
                    AddText("Please assign a generator prefab in the UMASettings object", LogType.Error);
                    AddText("By defalt this is the UMA_GLIB prefab in the 'Getting Started' folder");
                    return;
                }
                try
                {
                    GameObject gen = GameObject.Instantiate(settings.generatorPrefab);
                    if (gen != null) gen.name = settings.generatorPrefab.name;
                    AddText($"UMA Generator {settings.generatorPrefab.name} added to scene. Be sure to save.");
                }
                catch (Exception ex)
                {
                    AddText($"Failed to add UMA Generator: {ex.Message}", LogType.Error);
                }
            }
            else
            {
                AddText("UMA Generator already found in scene - Not added.");
            }

            try
            {
                GameObject go = GameObject.Instantiate(settings.characterPrefab);
                if (go != null) go.name = settings.characterPrefab.name;
                AddText($"UMA Character {settings.characterPrefab.name} added to scene. Be sure to save.");
            }
            catch (Exception ex)
            {
                AddText($"Failed to add UMA Character: {ex.Message}", LogType.Error);
            }
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
            AddText("To get started on your own, click on the <b>'Add UMA an to Current Scene'</b> button to the left");
            AddText("");
            AddText("If you are new to UMA, please check out the <b>'Basics'</b> section to the left");
            AddText("");
            AddText("To check out UMA in action, please open a sample scene using the button to the left");
            AddText("");
            AddText("Please join the <b>UMA Discord</b> for help and support (see Links)");
            AddText("You can also check out the <b>UMA Wiki</b> for documentation (see Links)");
            l.ButtonAction = (line) => DoLibraryRebuild(l);
        }

        #region LinksButton
        private void ShowLink(string label, string text, string URL)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label ?? "Link", EditorStyles.boldLabel, GUILayout.Width(96));
            if (!string.IsNullOrEmpty(URL))
            {
                if (GUILayout.Button(text ?? "(open)", Hyperlink))
                {
                    Application.OpenURL(URL);
                }
            }
            else
            {
                GUILayout.Label(text ?? "(unavailable)", InfoStyle);
            }
            GUILayout.EndHorizontal();
        }

        private void DoLinksPage()
        {
            var settings = UMASettings.GetOrCreateSettings();
            ClearLog();
            if (settings == null)
            {
                AddText("UMASettings not found, cannot display links.", LogType.Error);
                return;
            }
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

            float ht = 60;
            Rect SceneRect = new Rect(0, 0, ContentRect.width, ht);

            UMAWelcomeScenes scenes = null;
            try { scenes = (UMAWelcomeScenes)Resources.Load("UMAWelcomeScenes"); } catch { /* ignore */ }

            if (scenes != null && scenes.umaScenes != null)
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
            GUILayout.Label("", GUILayout.Width(ContentRect.width - 48), GUILayout.Height(SceneRect.y));
            GUILayout.EndScrollView();
        }

        private void DisplayScene(UMAWelcomeScenes.UMAScene scene, Rect SceneRect)
        {
            if (scene == null)
            {
                GUILayout.Label("Invalid scene entry.", Warning);
                return;
            }

            float gutter = 2f;
            float sqrSide = SceneRect.height - (gutter * 2.0f);
            Rect TitleRect = new Rect(sqrSide + (gutter * 2), gutter, SceneRect.width - (sqrSide + (gutter * 2)), sqrSide);
            Rect InfoRect = new Rect(TitleRect.x, TitleRect.y, TitleRect.width - 32, TitleRect.height);
            Rect textureRect = new Rect(gutter, gutter, sqrSide, sqrSide);

            var preview = scene.sceneTexture != null ? new GUIContent(scene.sceneTexture) : new GUIContent("Open");
            bool canOpen = !string.IsNullOrEmpty(scene.scenePath);
            using (new EditorGUI.DisabledScope(!canOpen))
            {
                if (GUI.Button(textureRect, preview) && canOpen)
                {
                    try
                    {
                        EditorSceneManager.OpenScene(scene.scenePath);
                    }
                    catch (Exception ex)
                    {
                        AddText($"Failed to open scene '{scene.sceneName}': {ex.Message}", LogType.Error);
                    }
                }
            }

            GUI.Label(InfoRect, scene.sceneName ?? "(Unnamed Scene)", SceneTitleStyle);
            InfoRect.y += EditorGUIUtility.singleLineHeight;
            InfoRect.height -= EditorGUIUtility.singleLineHeight;
            GUI.TextArea(InfoRect, scene.sceneDescription ?? string.Empty, DescriptionStyle);
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
                try
                {
                    if (!routine.MoveNext())
                    {
                        EditorApplication.update -= updateCallback;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"WelcomeToUMA coroutine error: {ex.Message}");
                    EditorApplication.update -= updateCallback;
                }
            };
            EditorApplication.update += updateCallback;
        }
        #endregion
    }
}