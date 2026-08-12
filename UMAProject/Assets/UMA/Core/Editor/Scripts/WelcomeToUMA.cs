using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA
{
    [InitializeOnLoad]
    public class WelcomeToUMA : EditorWindow
    {
        private const string WhatsNewDocumentPath = "Docs/!WhatsNewInUMA3.md";

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
                umaTex = UMAPathUtility.LoadInstallAsset<Texture>(
                    "InternalDataStore/InGame/Resources/UmaBanner.png");
                if (umaTex == null)
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
        private bool projectScanHasRun;
        public bool processing = false;
        public bool initialized = false;

        public UMASettings initialSettings;
        private string displayedSettingsVersion;

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
                displayedSettingsVersion = initialSettings != null
                    ? initialSettings.UMAVersion
                    : null;
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
            RefreshSettingsReference();
            HeaderRect = new Rect(0, 0, position.width, 50);
            NavigationRect = new Rect(0, 50, 200, position.height - 50);
            ContentRect = new Rect(200, 50, position.width - 200, position.height - 50);

            DrawHeader();
            DrawNavigation();
            DrawContent(currentButton);
        }

        private void RefreshSettingsReference()
        {
            UMASettings current = null;
            try
            {
                current = UMASettings.GetOrCreateSettings();
            }
            catch
            {
                return;
            }
            if (current == null) return;

            string version = current.UMAVersion;
            bool changed = current != initialSettings ||
                !string.Equals(version, displayedSettingsVersion,
                    StringComparison.Ordinal);
            if (!changed) return;

            initialSettings = current;
            displayedSettingsVersion = version;
            switch (currentButton)
            {
                case 0:
                    DoWelcome();
                    break;
                case 1:
                    DoGettingStarted();
                    break;
                case 2:
                    DoWhatsNew();
                    break;
                case 5:
                    DoLinksPage();
                    break;
            }
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
            if (GUILayout.Button("Getting Started", GUILayout.Height(40)))
            {
                DoGettingStarted();
                currentButton = 1;
            }

            if (GUILayout.Button("What's New", GUILayout.Height(40)))
            {
                UMAMarkdownViewer.Open(
                    UMAPathUtility.ResolveInstallAssetPath(WhatsNewDocumentPath));
            }
            if (GUILayout.Button("Documentation Browser", GUILayout.Height(40)))
            {
                UMADocumentationWindow.ShowWindow();
            }

            if (GUILayout.Button("Create UMA Character", GUILayout.Height(40)))
            {
                CreateUMACharacter();
                currentButton = 10;
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
            if (GUILayout.Button("Refresh UMA Shaders", GUILayout.Height(40)))
            {
                ClearLog();
                currentButton = 6;
                RefreshShaderFolder();
            }
            if (GUILayout.Button("Scan UMA 3 Scene", GUILayout.Height(40)))
            {
                ClearLog();
                ScanScene();
                currentButton = 3;
            }
            if (GUILayout.Button("Scan UMA 3 Project", GUILayout.Height(40)))
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
                if (GUILayout.Button("Don't Show at Startup", GUILayout.Height(30)))
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

        private string GetVersionName()
        {
            try
            {
                UMASettings settings = UMASettings.GetOrCreateSettings();
                if (settings != null && !string.IsNullOrEmpty(settings.UMAVersion))
                {
                    initialSettings = settings;
                    displayedSettingsVersion = settings.UMAVersion;
                    return settings.UMAVersion;
                }
            }
            catch
            {
                // The rest of the welcome window remains useful without UMASettings.
            }

            if (initialSettings != null &&
                !string.IsNullOrEmpty(initialSettings.UMAVersion))
                return initialSettings.UMAVersion;

            return "UMA";
        }

        private void DoGettingStarted()
        {
            ClearLog();
            scrollPosition = Vector2.zero;
            AddLargeText("Getting Started with " + GetVersionName());

            AddText("UMA is a runtime character creation system for Unity. It builds characters from indexed assets and recipes.");
            AddText("Assets can be loaded from Resources or Addressables. Use <b>UMA > Global Library</b> to browse the index, and rebuild it after importing or moving UMA content.");
            AddSeperator();

            AddText("<b>1. Create a character</b>");
            AddText("Click <b>Create UMA Character</b> on the left, or use <b>GameObject > UMA > Create New Dynamic Character Avatar</b>.");
            AddText("Select the new Dynamic Character Avatar to choose its race, wardrobe, colors, DNA, build options, and editor-time generation settings.");
            AddText("UMA creates the generator at runtime when one is needed. A generator normally does not need to be placed in every scene by hand.");
            AddSeperator();

            AddText("<b>2. Work with the core assets</b>");
            AddText("<b>RaceData:</b> Defines the base recipe, compatible wardrobe slots, DNA converters, cross-compatible races, and race-specific build data.");
            AddText("<b>SlotDataAsset:</b> Contains a skinned mesh part, its rig data, material assignment, tags, blendshapes, LOD settings, and animated-bone metadata.");
            if (initialSettings != null && initialSettings.Slots != null)
            {
                AddImage(initialSettings.Slots, "");
            }
            AddText("<b>OverlayDataAsset:</b> Supplies texture layers and material-channel data. Overlays can be tinted, share colors, and be positioned or aligned in recipe editors.");
            if (initialSettings != null && initialSettings.Overlays != null)
            {
                AddImage(initialSettings.Overlays, "");
            }
            AddText("<b>Wardrobe Recipe:</b> Packages wearable content, compatible races, slot suppression, mesh hiding, replacement rules, thumbnails, and other wardrobe behavior.");
            AddText("<b>DNA:</b> Drives modular character changes such as bone transforms, blendshapes, poses, mesh modifiers, and colors.");
            AddSeperator();

            AddText("<b>3. Use the current editor workflow</b>");
            AddText("Enable the <b>UMA Toolbar</b> from the Scene view Overlays menu for character rebuild modes, mesh-combiner selection, focus and skeleton controls, editor-generation pause, diagnostics, and common tools.");
            AddText("Use <b>UMA > Content Creation</b> for slot, wardrobe, bone, and prefab authoring. Asset management, texture, animation, project setup, testing, and debug commands are grouped in their matching UMA submenus.");
            AddText("Open the <b>Documentation Browser</b> for the Markdown guides shipped with this UMA installation.");
        }

        private void CreateUMACharacter()
        {
            ClearLog();
            scrollPosition = Vector2.zero;
            AddLargeText("Create UMA Character");

            const string menuPath = "GameObject/UMA/Create New Dynamic Character Avatar";
            if (EditorApplication.ExecuteMenuItem(menuPath))
            {
                AddText("Created and selected a new Dynamic Character Avatar in the current scene.");
                AddText("Use its Inspector to select a race, equip wardrobe, edit DNA and colors, and enable editor-time generation.");
            }
            else
            {
                AddText("Unity could not run <b>" + menuPath + "</b>.", LogType.Error);
                AddText("Confirm that the Dynamic Character System scripts are present and compiled.", LogType.Warning);
            }
        }

        private void DoWhatsNew()
        {
            ClearLog();
            scrollPosition = Vector2.zero;
            AddLargeText("What's New in " + GetVersionName());

            AddText("UMA NextGen combines a refreshed authoring workflow with faster character builds, expanded deformation tools, and better diagnostics for modern Unity projects.");
            AddSeperator();

            AddText("<b>Performance and character building</b>");
            AddText("- Jobified mesh combining and texture merging reduce main-thread character-build work.");
            AddText("- Bone-baking mesh combiners can bake supported skeletal motion into meshes, with race-baked blendshape and second-pass material support.");
            AddText("- Partial rebuild modes can update the rig and DNA, mesh, or textures without forcing a full character rebuild.");
            AddText("- Array pooling, parallel bone baking, improved combiner pass-two processing, and texture-lifetime fixes reduce allocations and retained temporary resources.");
            AddSeperator();

            AddText("<b>Scene and diagnostics workflow</b>");
            AddText("- The dockable Scene view UMA Toolbar provides rebuild commands, combiner switching, character focus, skeleton visualization, generation pause, diagnostics, and shortcuts to common tools.");
            AddText("- Selected-character diagnostics report mesh, skeleton, generator, and build state.");
            AddText("- Render Texture Diagnostics helps find live UMA render textures and track unexpected texture lifetime.");
            AddText("- Editor tests and race smoke tests are available from <b>UMA > Testing</b>.");
            AddSeperator();

            AddText("<b>Authoring and deformation</b>");
            AddText("- The modular DNA system supports bone transforms, blendshapes, bone poses, mesh modifiers, overlay UVs, shared colors, and live editor updates.");
            AddText("- Mesh Modifier sculpt mode includes Add, Remove, Smooth, Grab, Crease, Pinch, Plane, Boundary, and Elastic Deform brushes, plus falloff, masking, mirroring, and save options. Cloth simulation brushes are reserved for a later update.");
            AddText("- The Clothing Conformer can bind wardrobe meshes to a built character and conform selected clothing slots.");
            AddText("- Decal tools support slot-based content and RenderTexture stamping for tattoos, scars, wounds, makeup, and other layered details.");
            AddText("- Slot building supports UDIM workflows, race-baked blendshapes, unbaked animated bones, and updated mesh-processing tools.");
            AddText("- Bone Pose tools include updated building, mixing, conversion, extraction, and IK-assisted workflows.");
            AddSeperator();

            AddText("<b>Recipes, overlays, and content management</b>");
            AddText("- Recipe editors include overlay positioning and alignment tools, improved shared-color handling, icon creation, mesh hiding, and updated slot workflows.");
            AddText("- Wildcard placeholder slots can carry overlays and apply them to slots selected by matching rules and tags.");
            AddText("- DynamicCharacterAvatar's wearable-item API distinguishes replacing an equipped item from appending layered wardrobe content.");
            AddText("- Global Library maintenance and filtering are consolidated into dedicated top-level tools, while the remaining UMA commands are organized by workflow.");
            AddText("- Asset cleanup, validation, Quick Finder, Favorites, and Addressables workflows are available from the Global Library and Asset Management tools.");
            AddText("- Refreshed UMA 3 races, wardrobe, materials, poses, and sample scenes demonstrate character creation, DNA, decals, equipment, save/load, Timeline, and runtime construction.");
            AddSeperator();

            AddText("<b>Documentation and Unity support</b>");
            AddText($"- The Documentation Browser lists the Markdown guides shipped in <b>{UMAPathUtility.ResolveInstallAssetPath("Docs")}</b>; the Markdown viewer adds an outline, source preview, zoom, links, and automatic reload.");
            AddText("- ShaderGraph packages and UMA materials cover Built-in, URP, and HDRP workflows, including multiple RenderTexture formats and existing-texture channels.");
            AddText("- Unity 6.4 or newer includes an experimental node-graph editor for wardrobe recipes. Continue using the standard recipe editor for the complete production workflow while graph-editor parity work is in progress.");
            AddSeperator();

            AddText("After importing an UMA update or moving content, use <b>UMA > Global Library Maintenance</b> to rebuild or repair the asset index. Open the Documentation Browser for detailed setup, migration, and authoring guides.");
        }

        private void RefreshShaderFolder()
        {
            ClearLog();

            string path;
            try
            {
                UMASettings settings = UMASettings.GetOrCreateSettings();
                string configuredPath = settings != null
                    ? settings.ShaderFolder
                    : null;
                if (string.IsNullOrWhiteSpace(configuredPath))
                    configuredPath = UMAPathUtility.ShaderPackagesRelativePath;

                configuredPath = UMAPathUtility.Normalize(configuredPath);
                bool isAssetPath = configuredPath.StartsWith(
                    "Assets/", StringComparison.OrdinalIgnoreCase) ||
                    configuredPath.StartsWith(
                        "Packages/", StringComparison.OrdinalIgnoreCase);
                path = isAssetPath
                    ? UMAPathUtility.ResolveLegacyInstallAssetPath(configuredPath)
                    : UMAPathUtility.ResolveInstallAssetPath(configuredPath);
            }
            catch (Exception ex)
            {
                AddText($"Error locating the UMA shader folder: {ex.Message}",
                    LogType.Error);
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                AddText("UMA shader folder path is empty.", LogType.Error);
                return;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                AddText($"Refreshing UMA shaders in {path}");
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

                AddText(path + " refreshed successfully!");

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
                if (currentButton == 4 && projectScanHasRun)
                {
                    DrawProjectScanControls();
                }
                scrollPosition = GUILayout.BeginScrollView(scrollPosition);
                ShowLogItems();
                GUILayout.EndScrollView();
            }
            GUIHelper.EndInsetArea();
        }

        private void DrawProjectScanControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Filter to errors only", GUILayout.Width(180)))
            {
                FilterLogToErrorsOnly();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void FilterLogToErrorsOnly()
        {
            if (LoggedItems == null) return;

            List<LogLine> filteredItems = new List<LogLine>();
            bool keepActionsForError = false;
            foreach (var line in LoggedItems)
            {
                if (line == null)
                {
                    continue;
                }

                if (line.logType == LogType.Error)
                {
                    filteredItems.Add(line);
                    keepActionsForError = true;
                    continue;
                }

                if (line.ButtonAction != null && keepActionsForError)
                {
                    filteredItems.Add(line);
                    continue;
                }

                keepActionsForError = false;
            }

            LoggedItems.Clear();
            LoggedItems.AddRange(filteredItems);
            for (int i = 0; i < LoggedItems.Count; i++)
            {
                LoggedItems[i].index = i;
            }
            scrollPosition = Vector2.zero;
            Repaint();
        }

        private void ShowLogItems()
        {
            LogLineAction ButtonAction = null;
            LogLine ButtonActionLine = null;
            LogLine PingLine = null;

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
                    if (item.ReviewItem != null)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(item.Message ?? string.Empty))
                        {
                            ButtonAction = item.ButtonAction;
                            ButtonActionLine = item;
                        }
                        if (GUILayout.Button("Ping", GUILayout.Width(96)))
                        {
                            PingLine = item;
                        }
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        if (GUILayout.Button(item.Message ?? string.Empty))
                        {
                            ButtonAction = item.ButtonAction;
                            ButtonActionLine = item;
                        }
                    }
                }
                else
                {
                    GUILayout.Label(item.Message ?? string.Empty, item.Style ?? InfoStyle);
                }
                GUILayout.EndHorizontal();
            }
            if (PingLine != null)
            {
                PingReviewItem(PingLine);
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

        private void PingReviewItem(LogLine line)
        {
            UnityEngine.Object reviewObject = line?.ReviewItem?.Item;
            if (reviewObject == null)
            {
                AddText("Nothing selected to ping.", LogType.Warning);
                return;
            }

            EditorGUIUtility.PingObject(reviewObject);
        }

        private void ClearLog()
        {
            if (LoggedItems == null) LoggedItems = new List<LogLine>();
            LoggedItems.Clear();
            projectScanHasRun = false;
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
            AddText("UMA 3 Scene Scan");
            int errors = 0;
            int warnings = 0;

            CheckUMASettingsAndGenerator(ref errors, ref warnings);

            DynamicCharacterAvatar[] avatars;
            try
            {
                avatars = FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            catch (Exception ex)
            {
                AddText($"Unable to enumerate Dynamic Character Avatars: {ex.Message}", LogType.Error);
                return;
            }

            int avatarCount = avatars != null ? avatars.Length : 0;
            if (avatarCount == 0)
            {
                AddText("No Dynamic Character Avatars were found in the active scene.",
                    LogType.Info);
            }
            else
            {
                AddText($"Found {avatarCount} Dynamic Character Avatar{(avatarCount == 1 ? string.Empty : "s")}.");
            }

            UMAAssetIndexer indexer = null;
            bool indexerFailureReported = false;
            try { indexer = UMAAssetIndexer.Instance; }
            catch (Exception ex)
            {
                AddText($"Unable to load the UMA Global Library: {ex.Message}",
                    LogType.Error);
                errors++;
                indexerFailureReported = true;
            }
            if (indexer == null && !indexerFailureReported)
            {
                AddText("The UMA Global Library is unavailable. Scene race and recipe references cannot be resolved.",
                    LogType.Error);
                errors++;
            }

            for (int avatarIndex = 0; avatarIndex < avatarCount; avatarIndex++)
            {
                DynamicCharacterAvatar avatar = avatars[avatarIndex];
                if (avatar == null) continue;

                string avatarLabel = GetHierarchyPath(avatar.transform);
                bool loadsStartingRecipe = avatar.loadFileOnStart &&
                    (avatar.loadPathType == DynamicCharacterAvatar.loadPathTypes.String
                        ? !string.IsNullOrWhiteSpace(avatar.loadString)
                        : !string.IsNullOrWhiteSpace(avatar.loadFilename));
                string raceName = avatar.activeRace != null
                    ? avatar.activeRace.name
                    : null;
                RaceData race = null;

                if (string.IsNullOrWhiteSpace(raceName) ||
                    string.Equals(raceName, DynamicCharacterAvatar.NO_RACE,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!loadsStartingRecipe)
                    {
                        AddText($"{avatarLabel}: no race or starting character recipe is configured.",
                            LogType.Error);
                        AddReviewObject(avatar, "Inspect avatar");
                        errors++;
                    }
                }
                else if (indexer != null)
                {
                    try { race = indexer.GetRace(raceName); }
                    catch { /* reported as an unresolved race below */ }
                    if (race == null)
                    {
                        AddText($"{avatarLabel}: race '{raceName}' is not available from the Global Library.",
                            LogType.Error);
                        AddReviewObject(avatar, "Inspect avatar");
                        errors++;
                    }
                }

                if (avatar.loadFileOnStart && !loadsStartingRecipe)
                {
                    AddText($"{avatarLabel}: Load File On Start is enabled, but its recipe source is empty.",
                        LogType.Error);
                    AddReviewObject(avatar, "Inspect avatar");
                    errors++;
                }

                if (!avatar.BuildCharacterEnabled)
                {
                    AddText($"{avatarLabel}: character building is disabled. The avatar will not apply queued changes until it is enabled.",
                        LogType.Warning);
                    warnings++;
                }

                if (avatar.editorTimeGeneration && avatar.isActiveAndEnabled &&
                    !DynamicCharacterAvatar.EditorGenerationPaused &&
                    race != null && avatar.umaData == null)
                {
                    AddText($"{avatarLabel}: editor-time generation is enabled but no UMAData has been created.",
                        LogType.Warning);
                    AddReviewObject(avatar, "Inspect avatar");
                    warnings++;
                }

                ValidateGeneratedAvatar(avatar, avatarLabel, ref errors,
                    ref warnings);
            }

            AddText($"Scene scan complete: {errors} error(s), {warnings} warning(s).");
        }
        #endregion

        private void ScanProject()
        {
            projectScanHasRun = true;
            AddText("UMA 3 Project Scan");
            int settingsErrors = 0;
            int settingsWarnings = 0;
            CheckUMASettingsAndGenerator(ref settingsErrors,
                ref settingsWarnings);
            AddSeperator();

            UMAAssetIndexer indexer = null;
            try { indexer = UMAAssetIndexer.Instance; }
            catch (Exception ex)
            {
                AddText($"Cannot load the UMA Global Library: {ex.Message}",
                    LogType.Error);
            }

            if (indexer == null)
            {
                AddText("The UMA Global Library could not be loaded. UMA cannot resolve races, slots, overlays, or recipes.",
                    LogType.Error);
                return;
            }

            CheckLibrary();
            AddSeperator();
            CheckRaces();
            AddSeperator();
            CheckSlots();
            AddSeperator();
            CheckOverlays();
            AddSeperator();
            CheckMaterials();
            AddSeperator();
            CheckTextRecipes();
            AddSeperator();
            CheckWardrobeRecipes();
            AddSeperator();
            CheckWardrobeCollections();
            AddSeperator();
            AddText("Project scan complete. This scan is diagnostic; it does not rewrite UMA content.");
        }

        private void CheckUMASettingsAndGenerator(ref int errors,
            ref int warnings)
        {
            AddText("Checking UMA settings and generator");
            UMASettings settings = null;
            try { settings = UMASettings.GetOrCreateSettings(); }
            catch (Exception ex)
            {
                AddText($"UMASettings could not be loaded: {ex.Message}",
                    LogType.Error);
                errors++;
                return;
            }

            if (settings == null)
            {
                AddText("UMASettings could not be loaded.", LogType.Error);
                errors++;
                return;
            }

            if (settings.generatorPrefab == null)
            {
                AddText("UMASettings has no Generator Prefab. Characters cannot be generated.",
                    LogType.Error);
                AddReviewObject(settings, "Inspect UMA settings");
                errors++;
                return;
            }

            UMAGenerator generator =
                settings.generatorPrefab.GetComponent<UMAGenerator>();
            if (generator == null)
            {
                AddText($"Generator Prefab '{settings.generatorPrefab.name}' has no UMAGenerator component.",
                    LogType.Error);
                AddReviewObject(settings.generatorPrefab,
                    "Inspect Generator Prefab");
                errors++;
                return;
            }

            if (generator.meshCombiner == null)
            {
                AddText($"Generator Prefab '{settings.generatorPrefab.name}' has no mesh combiner.",
                    LogType.Error);
                AddReviewObject(settings.generatorPrefab,
                    "Inspect Generator Prefab");
                errors++;
            }
            if (generator.textureMerge == null)
            {
                AddText($"Generator Prefab '{settings.generatorPrefab.name}' has no TextureMerge configuration.",
                    LogType.Error);
                AddReviewObject(settings.generatorPrefab,
                    "Inspect Generator Prefab");
                errors++;
            }
            if (generator.defaultRendererAsset == null)
            {
                AddText($"Generator Prefab '{settings.generatorPrefab.name}' has no default Renderer Asset. Slots without an override will use Unity defaults.",
                    LogType.Warning);
                AddReviewObject(settings.generatorPrefab,
                    "Inspect Generator Prefab");
                warnings++;
            }
        }

        private void ValidateGeneratedAvatar(DynamicCharacterAvatar avatar,
            string avatarLabel, ref int errors, ref int warnings)
        {
            UMAData data = avatar.umaData;
            if (data == null) return;

            SkinnedMeshRenderer[] renderers = data.GetRenderers();
            if (renderers == null || renderers.Length == 0) return;

            for (int rendererIndex = 0; rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    AddText($"{avatarLabel}: generated renderer [{rendererIndex}] is missing.",
                        LogType.Error);
                    errors++;
                    continue;
                }
                if (renderer.sharedMesh == null ||
                    renderer.sharedMesh.vertexCount == 0)
                {
                    AddText($"{avatarLabel}: renderer '{renderer.name}' has no generated mesh.",
                        LogType.Error);
                    errors++;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null ||
                        IsErrorShader(material.shader))
                    {
                        AddText($"{avatarLabel}: renderer '{renderer.name}' has a missing or error shader at material [{materialIndex}].",
                            LogType.Error);
                        errors++;
                    }
                }
            }

            if (data.umaRecipe == null || data.umaRecipe.raceData == null)
            {
                AddText($"{avatarLabel}: generated UMAData has no resolved race recipe.",
                    LogType.Warning);
                warnings++;
            }
        }

        private static bool IsErrorShader(Shader shader)
        {
            return shader != null &&
                (string.Equals(shader.name, "Hidden/InternalErrorShader",
                    StringComparison.OrdinalIgnoreCase) ||
                 IsHiddenInternalShader(shader.name));
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "(missing avatar)";
            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void AddReviewObject(UnityEngine.Object target, string label)
        {
            if (target == null) return;
            LogLine line = AddText(label);
            line.ButtonAction = clickedLine =>
            {
                Selection.activeObject = target;
                EditorGUIUtility.PingObject(target);
            };
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

            AddText("UMA Global Library check complete");
        }

        private void CheckSlots()
        {
            AddText("Checking UMA 3 slots and mesh data");
            List<AssetItem> slots = null;
            try { slots = UMAAssetIndexer.Instance.GetAssetItems<SlotDataAsset>(); } catch { /* ignore */ }

            if (slots == null || slots.Count == 0)
            {
                AddText("No SlotDataAssets were found in the Global Library.",
                    LogType.Error);
                return;
            }

            var slotNames = new HashSet<string>(StringComparer.Ordinal);
            bool offeredLibraryRepair = false;
            foreach (AssetItem assetItem in slots)
            {
                if (assetItem == null) continue;
                SlotDataAsset slot = null;
                try { slot = assetItem.GetItem<SlotDataAsset>(); }
                catch { /* reported below */ }

                if (slot == null)
                {
                    if (IsGeneratedBakedSlotReference(assetItem._Name))
                        continue;

                    AddText($"Indexed slot '{assetItem._Name}' cannot be loaded from '{assetItem._Path}'.",
                        LogType.Error);
                    if (!offeredLibraryRepair)
                    {
                        LogLine repairLine = AddText("Repair Global Library");
                        repairLine.ButtonAction = line =>
                            DoLibraryRepair(repairLine);
                        offeredLibraryRepair = true;
                    }
                    continue;
                }

                bool invalid = false;
                if (string.IsNullOrWhiteSpace(slot.slotName))
                {
                    AddText($"Slot asset '{slot.name}' has an empty slot name.",
                        LogType.Error);
                    invalid = true;
                }
                else if (!slotNames.Add(slot.slotName))
                {
                    AddText($"Slot name '{slot.slotName}' is indexed more than once. Recipe lookup will be ambiguous.",
                        LogType.Error);
                    invalid = true;
                }

                var meshReasons = new List<string>();
                if (!slot.ValidateMeshData(meshReasons))
                {
                    for (int reasonIndex = 0; reasonIndex < meshReasons.Count;
                         reasonIndex++)
                    {
                        AddText($"Slot '{slot.name}': {meshReasons[reasonIndex]}",
                            LogType.Error);
                    }
                    invalid = true;
                }

                if ((slot.isSmooshable || slot.isWildCardSlot) &&
                    !HasUsableTags(slot.tags))
                {
                    string feature = slot.isSmooshable
                        ? "smooshing"
                        : "wildcard matching";
                    AddText($"Slot '{slot.slotName}' uses {feature} but has no usable tags.",
                        LogType.Warning);
                    invalid = true;
                }

                if (HasInvalidTags(slot.tags))
                {
                    AddText($"Slot '{slot.slotName}' contains empty or duplicate tags. Normalize them in the Slot inspector.",
                        LogType.Warning);
                    invalid = true;
                }

                if (slot.isClippingPlane &&
                    (UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) ||
                     slot.meshData.vertexCount < 4))
                {
                    AddText($"Clipping-plane slot '{slot.slotName}' needs at least four vertices.",
                        LogType.Error);
                    invalid = true;
                }

                if (invalid) ReviewAssetItem(assetItem, "slot");
            }
            AddText("Slot check complete");
        }

        private static bool HasUsableTags(string[] tags)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Length; i++)
                if (!string.IsNullOrWhiteSpace(tags[i])) return true;
            return false;
        }

        private static bool HasInvalidTags(string[] tags)
        {
            if (tags == null) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i]?.Trim();
                if (string.IsNullOrEmpty(tag) || !seen.Add(tag)) return true;
            }
            return false;
        }

        private void CheckOverlays()
        {
            AddText("Checking UMA 3 overlays and channel layouts");
            List<AssetItem> overlays = null;
            try { overlays = UMAAssetIndexer.Instance.GetAssetItems<OverlayDataAsset>(); } catch { /* ignore */ }

            if (overlays == null || overlays.Count == 0)
            {
                AddText("No OverlayDataAssets were found in the Global Library.",
                    LogType.Error);
                return;
            }

            var overlayNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssetItem assetItem in overlays)
            {
                if (assetItem == null) continue;
                OverlayDataAsset overlay = null;
                try { overlay = assetItem.GetItem<OverlayDataAsset>(); }
                catch { /* reported below */ }

                if (overlay == null)
                {
                    AddText($"Indexed overlay '{assetItem._Name}' cannot be loaded from '{assetItem._Path}'.",
                        LogType.Error);
                    continue;
                }

                bool invalid = false;
                if (string.IsNullOrWhiteSpace(overlay.overlayName))
                {
                    AddText($"Overlay asset '{overlay.name}' has an empty overlay name.",
                        LogType.Error);
                    invalid = true;
                }
                else if (!overlayNames.Add(overlay.overlayName))
                {
                    AddText($"Overlay name '{overlay.overlayName}' is indexed more than once. Recipe lookup will be ambiguous.",
                        LogType.Error);
                    invalid = true;
                }

                UMAMaterial material = overlay.material;
                if (material == null && !string.IsNullOrWhiteSpace(
                        overlay.materialName))
                {
                    try
                    {
                        material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(
                            overlay.materialName);
                    }
                    catch { /* reported below */ }
                }
                if (material == null)
                {
                    AddText($"Overlay '{overlay.overlayName}' has no resolvable UMAMaterial.",
                        LogType.Error);
                    invalid = true;
                }
                else
                {
                    int channelCount = material.channels != null
                        ? material.channels.Length
                        : 0;
                    if (overlay.textureCount > channelCount)
                    {
                        AddText($"Overlay '{overlay.overlayName}' has {overlay.textureCount} textures but UMAMaterial '{material.name}' has only {channelCount} channels.",
                            LogType.Error);
                        invalid = true;
                    }
                    if (overlay.textureList == null &&
                        material.materialType !=
                            UMAMaterial.MaterialType.UseExistingMaterial)
                    {
                        AddText($"Overlay '{overlay.overlayName}' has no texture list for material type {material.materialType}.",
                            LogType.Warning);
                        invalid = true;
                    }
                }

                if (overlay.textureList != null)
                {
                    if (overlay.overlayBlend == null ||
                        overlay.overlayBlend.Length != overlay.textureList.Length)
                    {
                        AddText($"Overlay '{overlay.overlayName}' blend-mode count does not match its texture count.",
                            LogType.Error);
                        invalid = true;
                    }
                    if (overlay.textureNames != null &&
                        overlay.textureNames.Length != overlay.textureList.Length)
                    {
                        AddText($"Overlay '{overlay.overlayName}' texture-name count does not match its texture count.",
                            LogType.Warning);
                        invalid = true;
                    }
                }

                if (HasInvalidTags(overlay.tags))
                {
                    AddText($"Overlay '{overlay.overlayName}' contains empty or duplicate tags.",
                        LogType.Warning);
                    invalid = true;
                }

                if (invalid) ReviewAssetItem(assetItem, "overlay");
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

                    if (string.IsNullOrWhiteSpace(uwc.wardrobeSlot) ||
                        string.Equals(uwc.wardrobeSlot, "None",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AddText($"Wardrobe Collection '{c._Name}' does not have a collection region assigned.", LogType.Error);
                        invalid = true;
                    }
                    if (uwc.arbitraryRecipes != null && uwc.arbitraryRecipes.Count > 0)
                    {
                        foreach (var r in uwc.arbitraryRecipes)
                        {
                            if (string.IsNullOrWhiteSpace(r) ||
                                !lib.HasAsset<UMAWardrobeRecipe>(r))
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
                            else
                            {
                                RaceData race = lib.GetAsset<RaceData>(r);
                                if (race != null && race.wardrobeSlots != null &&
                                    !string.IsNullOrWhiteSpace(uwc.wardrobeSlot) &&
                                    !race.wardrobeSlots.Contains(uwc.wardrobeSlot))
                                {
                                    AddText($"Wardrobe Collection '{c._Name}' uses region '{uwc.wardrobeSlot}', which is not defined by race '{r}'.",
                                        LogType.Error);
                                    invalid = true;
                                }
                            }
                            var raceRecipes = uwc.GetRacesRecipes(r);
                            var raceRecipeNames = uwc.GetRacesRecipeNames(r);
                            if (raceRecipes != null)
                            {
                                for (int ii = 0; ii < raceRecipes.Count; ii++)
                                {
                                    if (raceRecipes[ii] == null)
                                    {
                                        string recipeName = raceRecipeNames != null &&
                                            ii < raceRecipeNames.Count
                                            ? raceRecipeNames[ii]
                                            : "(unknown)";
                                        AddText($"Wardrobe Collection {c._Name} has an invalid recipe '{recipeName}' assigned for race {r}", LogType.Error);
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
            AddText("Wardrobe Collection check complete");
        }

        private static bool IsGeneratedBakedSlotReference(string slotName)
        {
            // Race-baked slots are transient index entries. They are generated from
            // their source SlotDataAsset when the race requests them.
            return !string.IsNullOrEmpty(slotName)
                && slotName.IndexOf("_baked_", StringComparison.OrdinalIgnoreCase) >= 0;
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
                try { PackRecipe = uwr.PackedLoad(); }
                catch (Exception ex)
                {
                    AddText($"Wardrobe Recipe '{uwr.name}' could not be parsed: {ex.Message}",
                        LogType.Error);
                }

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
                            RaceData compatibleRace = lib.GetAsset<RaceData>(rn);
                            if (compatibleRace != null &&
                                compatibleRace.wardrobeSlots != null &&
                                !compatibleRace.wardrobeSlots.Contains(
                                    uwr.wardrobeSlot))
                            {
                                AddText($"Wardrobe Recipe '{uwr.name}' uses region '{uwr.wardrobeSlot}', which is not defined by compatible race '{rn}'.",
                                    LogType.Error);
                                invalid = true;
                            }
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

                if (PackRecipe == null)
                {
                    AddText($"Wardrobe Recipe '{uwr.name}' has no readable packed recipe data.",
                        LogType.Error);
                    ReviewAssetItem(r, "wardrobe recipe");
                    continue;
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
                        AddText($"Wardrobe Recipe '{uwr.name}' has a slot entry with no slot or placeholder name.",
                            LogType.Error);
                        invalid = true;
                        continue;
                    }
                    if (s.isPlaceholderSlot)
                    {
                        if (!HasUsableTags(s.Tags))
                        {
                            AddText($"Wardrobe Recipe '{uwr.name}' placeholder '{s.id}' has no matching tags.",
                                LogType.Warning);
                            invalid = true;
                        }
                    }
                    else
                    {
                        bool slotExists = lib.HasAsset<SlotDataAsset>(s.id);
                        if (!slotExists && !IsGeneratedBakedSlotReference(s.id))
                        {
                            AddText($"Wardrobe Recipe {uwr.name} has a slot '{s.id}' that does not exist in the library!", LogType.Error);
                            AddText("To fix this, restore the missing slot, add it to the library, and then validate the slot", LogType.Error);
                        }
                        else if (slotExists)
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
                                            invalid = true;
                                        }
                                    }
                                }
                            }
                        } 
                    }
                }

                if (uwr.MeshHideAssets != null)
                {
                    for (int hideIndex = 0;
                         hideIndex < uwr.MeshHideAssets.Count; hideIndex++)
                    {
                        if (uwr.MeshHideAssets[hideIndex] != null) continue;
                        AddText($"Wardrobe Recipe '{uwr.name}' has a missing MeshHideAsset at index {hideIndex}.",
                            LogType.Error);
                        invalid = true;
                    }
                }
                if (uwr.MeshModifiers != null)
                {
                    for (int modifierIndex = 0;
                         modifierIndex < uwr.MeshModifiers.Count;
                         modifierIndex++)
                    {
                        if (uwr.MeshModifiers[modifierIndex] != null) continue;
                        AddText($"Wardrobe Recipe '{uwr.name}' has a missing UMA 3 MeshModifier at index {modifierIndex}.",
                            LogType.Error);
                        invalid = true;
                    }
                }

                if (invalid) ReviewAssetItem(r, "wardrobe recipe");
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

            AddText("Checking full-character recipes");
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

                if (string.Equals(utr.recipeType, "Wardrobe",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(utr.recipeType, "WardrobeCollection",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                bool invalid = false;
                if (string.IsNullOrWhiteSpace(utr.recipeString))
                {
                    AddText($"Character recipe '{utr.name}' has no serialized recipe data.",
                        LogType.Error);
                    ReviewAssetItem(r, "character recipe");
                    continue;
                }

                UMAPackedRecipeBase.UMAPackRecipe PackRecipe = null;
                try { PackRecipe = utr.PackedLoad(); }
                catch (Exception ex)
                {
                    AddText($"Character recipe '{utr.name}' could not be parsed: {ex.Message}",
                        LogType.Error);
                    ReviewAssetItem(r, "character recipe");
                    continue;
                }

                if (PackRecipe == null)
                {
                    AddText($"Character recipe '{utr.name}' returned no packed recipe data.",
                        LogType.Error);
                    ReviewAssetItem(r, "character recipe");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(PackRecipe.race))
                {
                    AddText($"Character recipe '{utr.name}' has no race.",
                        LogType.Error);
                    invalid = true;
                }
                else if (!lib.HasAsset<RaceData>(PackRecipe.race))
                {
                    AddText($"Character recipe '{utr.name}' references missing race '{PackRecipe.race}'.",
                        LogType.Error);
                    invalid = true;
                }

                var Slots = PackRecipe?.slotsV3;
                var Slot2 = PackRecipe?.slotsV2;

                if (Slots == null && Slot2 == null)
                {
                    AddText($"Character recipe '{utr.name}' has no slot data.",
                        LogType.Error);
                    invalid = true;
                }
                else if (Slots != null)
                {
                    for (int i = 0; i < Slots.Length; i++)
                    {
                        UMAPackedRecipeBase.PackedSlotDataV3 slot = Slots[i];
                        if (slot == null || string.IsNullOrEmpty(slot.id) ||
                            slot.isPlaceholderSlot)
                            continue;

                        if (!lib.HasAsset<SlotDataAsset>(slot.id) &&
                            !IsGeneratedBakedSlotReference(slot.id))
                        {
                            AddText($"Character recipe '{utr.name}' references missing slot '{slot.id}'.",
                                LogType.Error);
                            invalid = true;
                        }
                        if (slot.overlays == null) continue;
                        for (int overlayIndex = 0;
                             overlayIndex < slot.overlays.Length;
                             overlayIndex++)
                        {
                            var overlay = slot.overlays[overlayIndex];
                            if (overlay != null &&
                                !string.IsNullOrEmpty(overlay.id) &&
                                !lib.HasAsset<OverlayDataAsset>(overlay.id))
                            {
                                AddText($"Character recipe '{utr.name}' slot '{slot.id}' references missing overlay '{overlay.id}'.",
                                    LogType.Error);
                                invalid = true;
                            }
                        }
                    }
                }

                // UMA 3 races supply DNA defaults from their DNA collection.
                // A character recipe is therefore valid without packed legacy DNA.
                if (invalid) ReviewAssetItem(r, "character recipe");
            }
            AddText("Character recipe check complete");
        }

        private void CheckRaces()
        {
            AddText("Checking UMA 3 races, DNA, expressions, and bounds");
            List<AssetItem> races = null;
            try { races = UMAAssetIndexer.Instance.GetAssetItems<RaceData>(); } catch { /* ignore */ }

            if (races == null || races.Count == 0)
            {
                AddText("No RaceData assets were found in the Global Library.",
                    LogType.Error);
                return;
            }

            var raceNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssetItem assetItem in races)
            {
                bool invalid = false;
                if (assetItem == null || assetItem.Item == null)
                {
                    AddText($"Indexed race '{assetItem?._Name ?? "(unknown)"}' cannot be loaded. Repair the Global Library.",
                        LogType.Error);
                    if (assetItem != null) RebuildFromAssetItem(assetItem);
                    continue;
                }
                RaceData race = assetItem.Item as RaceData;
                if (race == null)
                {
                    AddText($"Invalid RaceData entry: {assetItem._Name}",
                        LogType.Error);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(race.raceName))
                {
                    AddText($"Race asset '{race.name}' has an empty race name.",
                        LogType.Error);
                    invalid = true;
                }
                else if (!raceNames.Add(race.raceName))
                {
                    AddText($"Race name '{race.raceName}' is indexed more than once. Avatar race resolution will be ambiguous.",
                        LogType.Error);
                    invalid = true;
                }

                UMATestReport report =
                    UMARaceValidation.ValidateRaceData(race, false);
                for (int messageIndex = 0;
                     messageIndex < report.Messages.Count; messageIndex++)
                {
                    UMATestMessage message = report.Messages[messageIndex];
                    if (message.Severity == UMATestSeverity.Info ||
                        message.Severity == UMATestSeverity.Pass)
                        continue;

                    LogType logType = message.Severity == UMATestSeverity.Error
                        ? LogType.Error
                        : LogType.Warning;
                    AddText($"Race '{race.raceName}' [{message.Category}]: {message.Message}",
                        logType);
                    invalid = true;
                }

                if (race.forceRebuildRaceSlots)
                {
                    AddText($"Race '{race.raceName}' has Force Rebuild Race Slots enabled. This is an authoring option and should normally be disabled for production.",
                        LogType.Warning);
                    invalid = true;
                }

                if (race.useNewDNA)
                {
                    ValidateNewDnaCollection(race, ref invalid);
                }

                if (race.useManualRendererBounds &&
                    (race.manualRendererBounds.x <= 0f ||
                     race.manualRendererBounds.y <= 0f ||
                     race.manualRendererBounds.z <= 0f))
                {
                    AddText($"Race '{race.raceName}' enables manual renderer bounds, but every extent must be greater than zero.",
                        LogType.Error);
                    invalid = true;
                }

                if (!race.UsesFbxRoute && race.baseRaceRecipe is UMAPackedRecipeBase packedBase)
                {
                    try
                    {
                        var pack = packedBase.PackedLoad();
                        if (pack != null &&
                            !string.IsNullOrEmpty(pack.race) &&
                            !string.Equals(pack.race, race.raceName,
                                StringComparison.Ordinal))
                        {
                            AddText($"Race '{race.raceName}' uses a base recipe saved for race '{pack.race}'.",
                                LogType.Warning);
                            invalid = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddText($"Race '{race.raceName}' base recipe could not be read: {ex.Message}",
                            LogType.Error);
                        invalid = true;
                    }
                }

                try
                {
                    var compat = race.GetCrossCompatibleRaces();
                    var compatibleNames = new HashSet<string>(
                        StringComparer.Ordinal);
                    if (compat != null)
                    {
                        foreach (string compatibleRace in compat)
                        {
                            if (string.IsNullOrWhiteSpace(compatibleRace) ||
                                !compatibleNames.Add(compatibleRace))
                            {
                                AddText($"Race '{race.raceName}' has an empty or duplicate cross-compatible race entry.",
                                    LogType.Warning);
                                invalid = true;
                            }
                            else if (!UMAAssetIndexer.Instance.HasAsset<RaceData>(
                                         compatibleRace))
                            {
                                AddText($"Race '{race.raceName}' references missing cross-compatible race '{compatibleRace}'.",
                                    LogType.Error);
                                invalid = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddText($"Race '{race.raceName}' compatibility data could not be checked: {ex.Message}",
                        LogType.Error);
                    invalid = true;
                }

                if (race.expressionGroup != null)
                {
                    var expressionMessages =
                        new List<ExpressionValidationMessage>();
                    race.expressionGroup.Validate(expressionMessages);
                    for (int messageIndex = 0;
                         messageIndex < expressionMessages.Count;
                         messageIndex++)
                    {
                        ExpressionValidationMessage message =
                            expressionMessages[messageIndex];
                        LogType logType = message.severity ==
                            ExpressionValidationSeverity.Error
                            ? LogType.Error
                            : message.severity ==
                              ExpressionValidationSeverity.Warning
                                ? LogType.Warning
                                : LogType.Info;
                        AddText($"Race '{race.raceName}' expression group: {message.message}",
                            logType);
                        if (logType != LogType.Info) invalid = true;
                    }
                }

                if (invalid)
                {
                    ReviewAssetItem(assetItem, "race");
                }
            }
            AddText("Race check complete");
        }

        private void ValidateNewDnaCollection(RaceData race,
            ref bool invalid)
        {
            if (race.DNACollection == null ||
                race.DNACollection.DNAGroups == null)
                return; // The central race validator reports this.

            var dnaNames = new HashSet<string>(StringComparer.Ordinal);
            for (int groupIndex = 0;
                 groupIndex < race.DNACollection.DNAGroups.Count;
                 groupIndex++)
            {
                DNAGroup group = race.DNACollection.DNAGroups[groupIndex];
                if (group == null)
                {
                    AddText($"Race '{race.raceName}' has a null DNA group at index {groupIndex}.",
                        LogType.Error);
                    invalid = true;
                    continue;
                }
                if (group.dnaList == null || group.dnaList.Count == 0)
                {
                    AddText($"Race '{race.raceName}' DNA group '{group.name}' contains no DNA items.",
                        LogType.Warning);
                    invalid = true;
                    continue;
                }

                for (int dnaIndex = 0; dnaIndex < group.dnaList.Count;
                     dnaIndex++)
                {
                    DNA dna = group.dnaList[dnaIndex];
                    if (dna == null)
                    {
                        AddText($"Race '{race.raceName}' DNA group '{group.name}' has a null DNA item at index {dnaIndex}.",
                            LogType.Error);
                        invalid = true;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(dna.name) ||
                        !dnaNames.Add(dna.name))
                    {
                        AddText($"Race '{race.raceName}' has an empty or duplicate UMA 3 DNA name '{dna.name}'.",
                            LogType.Error);
                        invalid = true;
                    }
                    if (dna.defaultValue < 0f || dna.defaultValue > 1f)
                    {
                        AddText($"Race '{race.raceName}' DNA '{dna.name}' has default value {dna.defaultValue}; UMA 3 DNA values must be between 0 and 1.",
                            LogType.Error);
                        invalid = true;
                    }
                    if (dna.effects == null)
                    {
                        AddText($"Race '{race.raceName}' DNA '{dna.name}' has a null effects list.",
                            LogType.Error);
                        invalid = true;
                        continue;
                    }
                    for (int effectIndex = 0;
                         effectIndex < dna.effects.Count; effectIndex++)
                    {
                        if (dna.effects[effectIndex] != null) continue;
                        AddText($"Race '{race.raceName}' DNA '{dna.name}' has a null effect at index {effectIndex}.",
                            LogType.Error);
                        invalid = true;
                    }
                }
            }
        }

        private void CheckMaterials()
        {
            AddText("Checking UMA 3 materials and shaders");
            List<AssetItem> materials =
                UMAAssetIndexer.Instance.GetAssetItems<UMAMaterial>();
            if (materials == null || materials.Count == 0)
            {
                AddText("No UMAMaterial assets were found in the Global Library.",
                    LogType.Error);
                return;
            }

            for (int materialIndex = 0;
                 materialIndex < materials.Count; materialIndex++)
            {
                AssetItem assetItem = materials[materialIndex];
                if (assetItem == null) continue;
                UMAMaterial umaMaterial = assetItem.Item as UMAMaterial;
                if (umaMaterial == null)
                {
                    AddText($"Indexed UMAMaterial '{assetItem._Name}' cannot be loaded from '{assetItem._Path}'.",
                        LogType.Error);
                    continue;
                }

                bool invalid = false;
                if (umaMaterial.material == null)
                {
                    AddText($"UMAMaterial '{umaMaterial.name}' has no Unity Material assigned.",
                        LogType.Error);
                    invalid = true;
                }
                else if (umaMaterial.material.shader == null ||
                         IsErrorShader(umaMaterial.material.shader))
                {
                    AddText($"UMAMaterial '{umaMaterial.name}' uses a missing or error shader.",
                        LogType.Error);
                    invalid = true;
                }

                int channelCount = umaMaterial.channels != null
                    ? umaMaterial.channels.Length
                    : 0;
                if (channelCount == 0 &&
                    umaMaterial.materialType !=
                        UMAMaterial.MaterialType.UseExistingMaterial)
                {
                    AddText($"UMAMaterial '{umaMaterial.name}' has no texture channels for material type {umaMaterial.materialType}.",
                        LogType.Error);
                    invalid = true;
                }

                var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                for (int channelIndex = 0; channelIndex < channelCount;
                     channelIndex++)
                {
                    UMAMaterial.MaterialChannel channel =
                        umaMaterial.channels[channelIndex];
                    string propertyName = channel.materialPropertyName;
                    if (channel.NonShaderTexture) continue;
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        AddText($"UMAMaterial '{umaMaterial.name}' channel [{channelIndex}] has no shader property name.",
                            LogType.Error);
                        invalid = true;
                    }
                    else
                    {
                        if (!propertyNames.Add(propertyName))
                        {
                            AddText($"UMAMaterial '{umaMaterial.name}' uses shader property '{propertyName}' on more than one channel.",
                                LogType.Warning);
                            invalid = true;
                        }
                        if (umaMaterial.material != null &&
                            !umaMaterial.material.HasProperty(propertyName))
                        {
                            AddText($"UMAMaterial '{umaMaterial.name}' channel [{channelIndex}] references shader property '{propertyName}', which is not present on shader '{umaMaterial.material.shader?.name}'.",
                                LogType.Error);
                            invalid = true;
                        }
                    }
                }

                if (invalid) ReviewAssetItem(assetItem, "UMA material");
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
            line.ButtonAction = null;
            StartCoroutine(InspectObject(line.ReviewItem));
            Repaint();
        }

        private IEnumerator InspectObject(AssetItem ai)
        {
            if (ai == null || ai.Item == null) yield break;
            InspectorUtlity.InspectTarget(ai.Item);
            yield break;
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

        private void DoWelcome()
        {
            ClearLog();
            scrollPosition = Vector2.zero;
            AddLargeText("Welcome to " + GetVersionName());

            AddText("UMA NextGen builds customizable, runtime-ready characters from races, slots, overlays, recipes, DNA, and indexed project content.");
            AddText("This window links the most useful setup, authoring, maintenance, diagnostics, and documentation workflows in the current UMA editor.");
            AddSeperator();

            AddText("<b>Quick start</b>");
            AddText("1. Click <b>Create UMA Character</b> to add and select a Dynamic Character Avatar.");
            AddText("2. Choose a race and enable editor-time generation in the avatar Inspector.");
            AddText("3. Use <b>Getting Started</b> for the core asset model, or open an <b>Example Scene</b>.");
            AddText("4. Enable the <b>UMA Toolbar</b> in the Scene view Overlays menu for rebuild, focus, skeleton, combiner, and diagnostics controls.");
            AddSeperator();

            AddText("<b>After installing or updating UMA</b>");
            AddText("Rebuild the Global Library so new and moved slots, overlays, races, recipes, and other indexed assets are available.");
            LogLine rebuildLine = AddText("Rebuild the Global Library now");
            rebuildLine.ButtonAction = line => DoLibraryRebuild(rebuildLine);
            AddSeperator();

            AddText("<b>Learn and troubleshoot</b>");
            AddText("Open the <b>Documentation Browser</b> to browse the Markdown guides included with this installation.");
            AddText("Use <b>Scan Scene</b> or <b>Scan Project</b> for common setup and asset problems, and the UMA diagnostics tools for character-build or RenderTexture investigation.");
            AddText("The <b>Links</b> page includes the UMA Discord, Wiki, forum, GitHub repository, Asset Store page, and video channel.");
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
            try
            {
                scenes = AssetDatabase.LoadAssetAtPath<UMAWelcomeScenes>(
                    UMAPathUtility.WelcomeScenesPath);
                if (scenes == null)
                    scenes = UMAPathUtility.LoadInstallAsset<UMAWelcomeScenes>(
                        "InternalDataStore/Editor/Resources/UMAWelcomeScenes.asset");
                if (scenes == null)
                    scenes = Resources.Load<UMAWelcomeScenes>(
                        "UMAWelcomeScenesProject");
                if (scenes == null)
                    scenes = Resources.Load<UMAWelcomeScenes>(
                        "UMAWelcomeScenes");
            }
            catch { /* ignore */ }

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
            string scenePath = UMAPathUtility.ResolveLegacyInstallAssetPath(
                scene.scenePath);
            bool canOpen = !string.IsNullOrEmpty(scenePath) &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null;
            using (new EditorGUI.DisabledScope(!canOpen))
            {
                if (GUI.Button(textureRect, preview) && canOpen)
                {
                    try
                    {
                        EditorSceneManager.OpenScene(scenePath);
                    }
                    catch (Exception ex)
                    {
                        AddText($"Failed to open scene '{scene.sceneName}' " +
                            $"at '{scenePath}': {ex.Message}", LogType.Error);
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
