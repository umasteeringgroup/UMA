using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UMA.CharacterSystem;
using UMA.Editors;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    [InitializeOnLoad]
    public class WelcomeToUMA : EditorWindow
    {
        private const string WhatsNewDocumentPath = "Docs/!WhatsNewInUMA3.md";
        private const string UrpPackageName = "com.unity.render-pipelines.universal";
        private const string HdrpPackageName = "com.unity.render-pipelines.high-definition";
        private const string UmaUrpPackagePath = "SRP/UMAURP.unitypackage";
        private const string UmaHdrpPackagePath = "SRP/UMAHDRP.unitypackage";
        private const string LegacySrpRoot = UMAPathUtility.ProjectSrpRoot;
        private const string UrpInstalledMarker = "UMAURPInstalled.json";
        private const string HdrpInstalledMarker = "UMAHDRPInstalled.json";
        private const string UrpContentManifest = "UMAURPManifest.json";
        private const string HdrpContentManifest = "UMAHDRPManifest.json";
        private const string PendingSrpImportKey = "UMA.PendingSrpImport";
        private const string StartupCheckCompleteKey = "UMA.WelcomeToUMA.StartupCheckComplete";
        private const string DismissedAutomaticPromptKey =
            "UMA.WelcomeToUMA.DismissedAutomaticPrompt";
        private const double RequiredSrpCheckInterval = 2d;
        private const double PendingImportRecoverySeconds = 300d;
        private static double nextRequiredSrpCheck;
        private static double nextPendingImportCheck;
        private static bool isAssemblyReloadingOrQuitting;

        private enum SrpSupport
        {
            None,
            Urp,
            Hdrp,
            Both
        }

        [Serializable]
        private sealed class SrpInstallMarker
        {
            public string pipeline;
            public string sourceHash;
            public string umaVersion;
            public string installedUtc;
        }

        [Serializable]
        private sealed class PendingSrpImport
        {
            public string pipeline;
            public string sourceHash;
            public string backupFolder;
            public string archiveFileName;
            public string expectedPackageName;
            public string startedUtc;
            public string[] sharedPaths;
            public bool hadPreviousSrp;
            public bool restoreInstallerArchives;
        }

        private sealed class ArchiveHashCache
        {
            public long length;
            public long lastWriteUtcTicks;
            public string hash;
        }

        private static readonly Dictionary<string, ArchiveHashCache>
            ArchiveHashes = new Dictionary<string, ArchiveHashCache>(
                StringComparer.OrdinalIgnoreCase);

        public static WelcomeToUMA Instance
        {
            get; set;
        }

        static WelcomeToUMA()
        {
            AssetDatabase.importPackageCompleted += OnSrpPackageImportCompleted;
            AssetDatabase.importPackageCancelled += OnSrpPackageImportCancelled;
            AssetDatabase.importPackageFailed += OnSrpPackageImportFailed;
            EditorApplication.delayCall += ResumePendingSrpImport;
            EditorApplication.delayCall += DelayedCall;
            EditorApplication.update += EnforceRequiredSrpSelection;
            EditorApplication.projectChanged += SrpProjectChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnBeforeAssemblyReload()
        {
            isAssemblyReloadingOrQuitting = true;
        }

        private static void OnEditorQuitting()
        {
            isAssemblyReloadingOrQuitting = true;
        }

        private static void SrpProjectChanged()
        {
            nextRequiredSrpCheck = 0d;
            EditorApplication.update -= EnforceRequiredSrpSelection;
            EditorApplication.update += EnforceRequiredSrpSelection;
        }

        static void DelayedCall()
        {
            EditorApplication.update += Update;
        }

        public static void Update()
        {
            bool isStartupCheck = !SessionState.GetBool(StartupCheckCompleteKey, false);
            if (isStartupCheck)
                SessionState.SetBool(StartupCheckCompleteKey, true);

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
            SrpSupport installedSrp = GetInstalledSrpSupport();
            SrpSupport activeSrp = GetActiveSrpSupport();
            bool requiresUma3Content = RequiresUma3ContentInstallation();
            bool requiresSrpSelection = RequiresSrpSelection(installedSrp);
            bool hasInstalledSrpUpdate = IsInstalledSrpUpdateAvailable();
            string promptSignature = CreateAutomaticPromptSignature(installedSrp,
                activeSrp, requiresUma3Content, requiresSrpSelection,
                hasInstalledSrpUpdate);
            if (ShouldOpenAutomatically(settings.showWelcomeToUMA, isStartupCheck,
                requiresUma3Content, requiresSrpSelection, hasInstalledSrpUpdate,
                SessionState.GetString(DismissedAutomaticPromptKey, string.Empty),
                promptSignature))
            {
                OpenWindow();
            }
            EditorApplication.update -= Update;
        }

        private static bool ShouldShowAutomatically(bool showAtStartup,
            bool isStartupCheck, bool requiresUma3Content, bool requiresSrpSelection,
            bool hasInstalledSrpUpdate)
        {
            return (showAtStartup && isStartupCheck) || requiresUma3Content ||
                requiresSrpSelection || hasInstalledSrpUpdate;
        }

        private static bool ShouldOpenAutomatically(bool showAtStartup,
            bool isStartupCheck, bool requiresUma3Content,
            bool requiresSrpSelection, bool hasInstalledSrpUpdate,
            string dismissedPromptSignature, string currentPromptSignature)
        {
            return ShouldShowAutomatically(showAtStartup, isStartupCheck,
                       requiresUma3Content, requiresSrpSelection,
                       hasInstalledSrpUpdate) &&
                   !IsAutomaticPromptDismissed(dismissedPromptSignature,
                       currentPromptSignature);
        }

        private static bool IsAutomaticPromptDismissed(string dismissedSignature,
            string currentSignature)
        {
            return !string.IsNullOrEmpty(currentSignature) &&
                   string.Equals(dismissedSignature, currentSignature,
                       StringComparison.Ordinal);
        }

        private static string CreateAutomaticPromptSignature(SrpSupport installedSrp,
            SrpSupport activeSrp, bool requiresUma3Content,
            bool requiresSrpSelection, bool hasInstalledSrpUpdate)
        {
            return ((int)installedSrp).ToString() + ":" +
                   ((int)activeSrp).ToString() + ":" +
                   (requiresUma3Content ? "1" : "0") + ":" +
                   (requiresSrpSelection ? "1" : "0") + ":" +
                   (hasInstalledSrpUpdate ? "1" : "0");
        }

        private static string GetCurrentAutomaticPromptSignature()
        {
            SrpSupport installedSrp = GetInstalledSrpSupport();
            return CreateAutomaticPromptSignature(installedSrp,
                GetActiveSrpSupport(), RequiresUma3ContentInstallation(),
                RequiresSrpSelection(installedSrp),
                IsInstalledSrpUpdateAvailable());
        }

        private static void EnforceRequiredSrpSelection()
        {
            if (Application.isBatchMode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextRequiredSrpCheck)
                return;

            nextRequiredSrpCheck = EditorApplication.timeSinceStartup + RequiredSrpCheckInterval;
            SrpSupport installedSrp = GetInstalledSrpSupport();
            if (!RequiresSrpSelection(installedSrp))
                return;
            if (Instance != null)
                return;
            string promptSignature = CreateAutomaticPromptSignature(installedSrp,
                GetActiveSrpSupport(), RequiresUma3ContentInstallation(), true,
                IsInstalledSrpUpdateAvailable());
            if (IsAutomaticPromptDismissed(
                    SessionState.GetString(DismissedAutomaticPromptKey, string.Empty),
                    promptSignature))
                return;

            OpenWindow();
            EditorApplication.delayCall += () =>
            {
                if (Instance != null && Instance.initialized)
                {
                    Instance.DoContentPackagesPage();
                    Instance.Repaint();
                }
            };
        }

        [MenuItem("UMA/Welcome to UMA", false, 0)]
        public static void ShowWindow()
        {
            SessionState.SetString(DismissedAutomaticPromptKey, string.Empty);
            OpenWindow();
        }

        private static void OpenWindow()
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
        private GUIStyle informationButtonStyle;

        public Rect HeaderRect;
        public Rect NavigationRect;
        public Rect ContentRect;

        public int currentButton;
        private Vector2 scrollPosition;
        private bool projectScanHasRun;
        private bool pageInitialized;
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
            pageInitialized = false;
        }

        public void OnDisable()
        {
            Instance = null;
            if (Application.isBatchMode || isAssemblyReloadingOrQuitting)
                return;

            try
            {
                SessionState.SetString(DismissedAutomaticPromptKey,
                    GetCurrentAutomaticPromptSignature());
            }
            catch
            {
                // Closing the Welcome window must never be blocked by a setup probe.
            }
        }

        public void Awake()
        {
            EditorApplication.delayCall += DelayAwake;
        }

        public void DelayAwake()
        {
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
            pageInitialized = false;
            initialized = true;
            Repaint();
        }

        private void EnsureStyles()
        {
            if (ActiveLargeStyle != null && ErrorFound != null && Warning != null &&
                InfoStyle != null && Hyperlink != null && DescriptionStyle != null &&
                SceneTitleStyle != null && informationButtonStyle != null)
                return;

            // EditorStyles is only reliable while Unity is inside an IMGUI event.
            // Initializing these from EditorApplication.delayCall can leave every
            // style null in a newly created project and make the window throw on
            // each repaint.
            GUIStyle labelStyle = EditorStyles.label ?? GUI.skin?.label ?? new GUIStyle();
            GUIStyle largeLabelStyle = EditorStyles.largeLabel ?? labelStyle;

            ActiveLargeStyle = new GUIStyle(largeLabelStyle)
            {
                richText = true,
                wordWrap = true,
                fontSize = 32,
                alignment = TextAnchor.MiddleCenter
            };

            Hyperlink = new GUIStyle(labelStyle)
            {
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.MiddleLeft
            };
            Hyperlink.hover.textColor = Color.cyan;
            Hyperlink.active.textColor = Color.white;

            ErrorFound = new GUIStyle(labelStyle)
            {
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.MiddleLeft
            };
            ErrorFound.normal.textColor = new Color(0.3f, 0, 0, 1);

            Warning = new GUIStyle(labelStyle)
            {
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.MiddleLeft
            };
            Warning.normal.textColor = Color.yellow;

            InfoStyle = new GUIStyle(labelStyle)
            {
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.UpperLeft
            };

            DescriptionStyle = new GUIStyle(labelStyle)
            {
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.UpperLeft
            };

            SceneTitleStyle = new GUIStyle(labelStyle)
            {
                wordWrap = false,
                richText = true,
                alignment = TextAnchor.UpperLeft
            };

            GUIStyle buttonStyle = GUI.skin?.button ?? new GUIStyle(labelStyle);
            informationButtonStyle = new GUIStyle(buttonStyle)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
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
            EnsureStyles();
            if (!pageInitialized)
            {
                pageInitialized = true;
                currentButton = 0;
                if (RequiresSrpSelection(GetInstalledSrpSupport()) ||
                    RequiresUma3ContentInstallation())
                    DoContentPackagesPage();
                else
                    DoWelcome();
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
            GUIStyle headerStyle = ActiveLargeStyle ?? EditorStyles.largeLabel ??
                EditorStyles.label ?? GUI.skin?.label ?? new GUIStyle();
            EditorGUILayout.LabelField($"Welcome to {version}", headerStyle);
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
            SrpSupport installedSrp = GetInstalledSrpSupport();
            string srpButton = RequiresSrpSelection(installedSrp)
                ? "Install Render Pipeline Support (Required)"
                : IsInstalledSrpUpdateAvailable()
                    ? "Update Render Pipeline Support"
                    : "Render Pipeline Support";
            if (GUILayout.Button(srpButton, GUILayout.Height(40)))
            {
                DoSrpSupportPage();
            }
            UMAContentInstallationState uma3InstallationState =
                UMAContentPackageInstaller.GetState(UMAContentKind.Uma3);
            bool uma3Ready = uma3InstallationState ==
                             UMAContentInstallationState.Installed ||
                             (!UMAPathUtility.IsPackageInstallation &&
                              UMAPathUtility.IsUma3ContentInstalled);
            string contentButton = uma3Ready
                ? "Install / Update UMA Packages"
                : "Install UMA Packages (Required)";
            if (GUILayout.Button(contentButton, GUILayout.Height(40)))
            {
                DoContentPackagesPage();
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

            using (new EditorGUI.DisabledScope(
                       RequiresSrpSelection(installedSrp) || !uma3Ready))
            {
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
            }
            if (GUILayout.Button("Links", GUILayout.Height(40)))
            {
                ClearLog();
                currentButton = 5;
            }
            if (initialSettings != null && initialSettings.showWelcomeToUMA &&
                !RequiresSrpSelection(GetInstalledSrpSupport()))
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
                    : configuredPath.StartsWith("SRP/",
                        StringComparison.OrdinalIgnoreCase)
                        ? UMAPathUtility.ResolveSrpAssetPath(
                            configuredPath.Substring("SRP/".Length))
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
                scrollPosition = GUILayout.BeginScrollView(scrollPosition,
                    false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                    GUILayout.ExpandWidth(true));
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

                float reservedWidth = item.logType == LogType.Error ||
                                      item.logType == LogType.Warning
                    ? 64f
                    : 0f;
                float rowWidth = GetInformationRowWidth(reservedWidth);

                if (item.Image != null)
                {
                    GUILayout.BeginHorizontal();
                    if (!string.IsNullOrEmpty(item.Message))
                    {
                        GUILayout.Label(item.Message, InfoStyle,
                            GUILayout.ExpandWidth(true),
                            GUILayout.MaxWidth(rowWidth));
                    }
                    float imageWidth = Mathf.Min(600f, rowWidth);
                    GUILayout.Label(item.Image, GUILayout.Width(imageWidth));
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
                        float buttonWidth = Mathf.Max(1f, rowWidth - 100f);
                        if (GUILayout.Button(item.Message ?? string.Empty,
                                informationButtonStyle,
                                GUILayout.ExpandWidth(true),
                                GUILayout.MaxWidth(buttonWidth),
                                GUILayout.MinHeight(EditorGUIUtility.singleLineHeight + 6f)))
                        {
                            ButtonAction = item.ButtonAction;
                            ButtonActionLine = item;
                        }
                        if (GUILayout.Button("Ping", GUILayout.Width(96)))
                        {
                            PingLine = item;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(item.Message ?? string.Empty,
                                informationButtonStyle,
                                GUILayout.ExpandWidth(true),
                                GUILayout.MaxWidth(rowWidth),
                                GUILayout.MinHeight(EditorGUIUtility.singleLineHeight + 6f)))
                        {
                            ButtonAction = item.ButtonAction;
                            ButtonActionLine = item;
                        }
                    }
                }
                else
                {
                    GUIStyle style = item.Style ?? InfoStyle;
                    GUILayout.Label(item.Message ?? string.Empty, style,
                        GUILayout.ExpandWidth(true), GUILayout.MaxWidth(rowWidth));
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

        private float GetInformationRowWidth(float reservedWidth = 0f)
        {
            // Account for the inset-area borders, layout spacing, and vertical
            // scrollbar so wrapped controls never enlarge the scroll view.
            return Mathf.Max(1f, ContentRect.width - 32f - reservedWidth);
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
                avatars = UMAObjectUtility.FindObjectsByType<DynamicCharacterAvatar>(
                    FindObjectsInactive.Include);
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

            SrpSupport installedSrp = GetInstalledSrpSupport();
            if (RequiresSrpSelection(installedSrp))
            {
                AddText(installedSrp == SrpSupport.Both
                    ? "<b>A single UMA render pipeline must be selected.</b> Replace the combined SRP content with either UMA URP or UMA HDRP support before continuing."
                    : "<b>Render pipeline support is required.</b> Install either UMA URP or UMA HDRP support before continuing.",
                    LogType.Warning);
                LogLine installSrpLine = AddText("Choose UMA URP or HDRP Support");
                installSrpLine.ButtonAction = line => DoContentPackagesPage();
                AddSeperator();
            }
            else if (IsInstalledSrpUpdateAvailable())
            {
                AddText("<b>An updated UMA render-pipeline support package is available.</b>", LogType.Warning);
                LogLine updateSrpLine = AddText(
                    "Update UMA Render Pipeline Support");
                updateSrpLine.ButtonAction = line => DoContentPackagesPage();
                AddSeperator();
            }

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

        private static SrpSupport GetInstalledSrpSupport()
        {
            bool urpMarker = AssetPathExists(
                LegacySrpRoot + "/" + UrpInstalledMarker);
            bool hdrpMarker = AssetPathExists(
                LegacySrpRoot + "/" + HdrpInstalledMarker);
            bool urpManifest = AssetPathExists(
                LegacySrpRoot + "/" + UrpContentManifest);
            bool hdrpManifest = AssetPathExists(
                LegacySrpRoot + "/" + HdrpContentManifest);
            bool urpContent = IsSrpContentValid(SrpSupport.Urp);
            bool hdrpContent = IsSrpContentValid(SrpSupport.Hdrp);

            // Installer markers and packaged content manifests identify one
            // authoritative pipeline even when shared assets contain fallback
            // material references. Pre-manifest copied folders fall back to
            // the legacy multi-file content checks.
            bool hasAuthoritativeIdentity = urpMarker || hdrpMarker ||
                                            urpManifest || hdrpManifest;
            bool hasUrp = hasAuthoritativeIdentity
                ? (urpMarker || urpManifest) && urpContent
                : urpContent;
            bool hasHdrp = hasAuthoritativeIdentity
                ? (hdrpMarker || hdrpManifest) && hdrpContent
                : hdrpContent;

            if (hasUrp && hasHdrp) return SrpSupport.Both;
            if (hasUrp) return SrpSupport.Urp;
            if (hasHdrp) return SrpSupport.Hdrp;
#if !UMA_PACKAGE_MANAGER
            // Asset-based UMA distributions ship configured for URP. The
            // bundled pipeline archives are optional switches, not evidence
            // that render-pipeline support is missing.
            return SrpSupport.Urp;
#else
            return SrpSupport.None;
#endif
        }

        private static bool RequiresSrpSelection(SrpSupport support)
        {
            if (support == SrpSupport.None || support == SrpSupport.Both)
                return true;

            SrpSupport active = GetActiveSrpSupport();
            return (active == SrpSupport.Urp || active == SrpSupport.Hdrp) &&
                   active != support;
        }

        private static bool RequiresUma3ContentInstallation()
        {
            if (!UMAPathUtility.IsPackageInstallation)
                return !UMAPathUtility.IsUma3ContentInstalled;
            return UMAContentPackageInstaller.GetState(UMAContentKind.Uma3) !=
                   UMAContentInstallationState.Installed;
        }

        private static SrpSupport GetActiveSrpSupport()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                return SrpSupport.None;

            Type type = pipeline.GetType();
            string identity = (type.FullName ?? type.Name) + " " +
                              (type.Assembly.GetName().Name ?? string.Empty);
            if (identity.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0)
                return SrpSupport.Hdrp;
            if (identity.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                return SrpSupport.Urp;
            return SrpSupport.None;
        }

        private static bool IsSrpContentValid(SrpSupport support)
        {
            string manifestName = support == SrpSupport.Urp
                ? UrpContentManifest
                : support == SrpSupport.Hdrp
                    ? HdrpContentManifest
                    : null;
            if (string.IsNullOrEmpty(manifestName))
                return false;

            string manifestAssetPath = LegacySrpRoot + "/" + manifestName;
            if (AssetPathExists(manifestAssetPath))
            {
                string expected = support == SrpSupport.Urp ? "URP" : "HDRP";
                return UMASrpPackageArchiveValidator.TryValidateInstalledSupport(
                    expected, out _);
            }

            // Backward-compatible validation for manually copied pre-manifest
            // folders. Multiple independent paths prevent a partial copy from
            // being accepted as installed support.
            string[] required = support == SrpSupport.Urp
                ? new[]
                {
                    "Textures/ReallyWhite.png",
                    "ShaderGraphs/Graphs/UMA3_SkinShader_URP.shadergraph",
                    "ShaderGraphs/Materials/UMA3_SkinShader_URP.asset"
                }
                : new[]
                {
                    "HDRPSetup/UMAHDRPSetup.cs",
                    "HDRPSetup/UMAHDRPSetup.prefab",
                    "DiffusionProfiles/UMAEye.asset",
                    "ShaderGraphs/Graphs/UMA3_SkinShader_HDRP.shadergraph"
                };
            for (int i = 0; i < required.Length; i++)
            {
                if (!AssetPathExists(LegacySrpRoot + "/" + required[i]))
                    return false;
            }
            return true;
        }

        private static bool AssetPathExists(string assetPath)
        {
            try
            {
                return File.Exists(UMAPathUtility.ResolveAbsolutePath(assetPath));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInstalledSrpUpdateAvailable()
        {
            SrpSupport installed = GetInstalledSrpSupport();
            if (installed != SrpSupport.Urp && installed != SrpSupport.Hdrp)
                return false;
            if (!TryReadSrpMarker(installed, out SrpInstallMarker marker) ||
                string.IsNullOrEmpty(marker.sourceHash))
                return false;

            string archivePath = GetBundledArchiveAbsolutePath(installed);
            string sourceHash = ComputeArchiveHash(archivePath);
            return !string.IsNullOrEmpty(sourceHash) &&
                !string.Equals(sourceHash, marker.sourceHash,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadSrpMarker(SrpSupport support,
            out SrpInstallMarker marker)
        {
            marker = null;
            string markerName = support == SrpSupport.Urp
                ? UrpInstalledMarker
                : support == SrpSupport.Hdrp
                    ? HdrpInstalledMarker
                    : null;
            if (string.IsNullOrEmpty(markerName)) return false;

            try
            {
                string path = UMAPathUtility.ResolveAbsolutePath(
                    LegacySrpRoot + "/" + markerName);
                if (!File.Exists(path)) return false;
                marker = JsonUtility.FromJson<SrpInstallMarker>(
                    File.ReadAllText(path));
                return marker != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetBundledArchiveAbsolutePath(
            SrpSupport support)
        {
            string relativePath = support == SrpSupport.Urp
                ? UmaUrpPackagePath
                : support == SrpSupport.Hdrp
                    ? UmaHdrpPackagePath
                    : null;
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;
            return UMAPathUtility.ResolveAbsolutePath(
                UMAPathUtility.ResolveInstallAssetPath(relativePath));
        }

        private static string ComputeArchiveHash(string archivePath)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                return string.Empty;

            try
            {
                FileInfo info = new FileInfo(archivePath);
                if (ArchiveHashes.TryGetValue(archivePath,
                        out ArchiveHashCache cached) &&
                    cached.length == info.Length &&
                    cached.lastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
                    return cached.hash;

                string hash;
                using (FileStream stream = File.OpenRead(archivePath))
                using (SHA256 sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(stream))
                        .Replace("-", string.Empty).ToLowerInvariant();

                ArchiveHashes[archivePath] = new ArchiveHashCache
                {
                    length = info.Length,
                    lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                    hash = hash
                };
                return hash;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UMA] Could not hash SRP installer archive: " +
                    ex.Message);
                return string.Empty;
            }
        }

        private void DoSrpSupportPage()
        {
            ClearLog();
            scrollPosition = Vector2.zero;
            currentButton = 0;
            AddLargeText("UMA Render Pipeline Support");
#if UMA_PACKAGE_MANAGER
            AddSrpSupportControls();
#else
            AddText("UMA is configured for <b>URP support by default</b> in this installation.");
            AddText("Use one of the bundled packages in <b>Assets/UMA/SRP</b> to import or switch the UMA render-pipeline assets.");
            AddSeperator();

            LogLine urpLine = AddText("Import UMA URP Package...");
            urpLine.ButtonAction = line => ImportBundledSrpPackage(
                SrpSupport.Urp, "URP");
            LogLine hdrpLine = AddText("Import UMA HDRP Package...");
            hdrpLine.ButtonAction = line => ImportBundledSrpPackage(
                SrpSupport.Hdrp, "HDRP");
#endif
        }

#if !UMA_PACKAGE_MANAGER
        private void ImportBundledSrpPackage(SrpSupport support,
            string displayName)
        {
            string archiveAbsolutePath = GetBundledArchiveAbsolutePath(support);
            if (string.IsNullOrEmpty(archiveAbsolutePath) ||
                !File.Exists(archiveAbsolutePath))
            {
                AddText("UMA could not find the bundled " + displayName +
                    " package in Assets/UMA/SRP.", LogType.Error);
                return;
            }

            AssetDatabase.ImportPackage(archiveAbsolutePath, true);
        }
#endif

        private void AddSrpSupportControls()
        {
            SrpSupport installed = GetInstalledSrpSupport();
            SrpSupport active = GetActiveSrpSupport();
            AddText(active == SrpSupport.Urp || active == SrpSupport.Hdrp
                ? "Active project pipeline: <b>" + GetSrpDisplayName(active) + "</b>."
                : "No active URP or HDRP Render Pipeline Asset was detected.");
            if (installed == SrpSupport.None)
            {
                AddText("<b>No UMA URP or HDRP support is installed.</b>", LogType.Warning);
                AddText("Choose URP or HDRP below. UMA requires one render-pipeline support folder.");
            }
            else if (installed == SrpSupport.Both)
            {
                AddText("Both UMA URP and HDRP content were found. UMA requires one selected pipeline; install one package below to replace the combined SRP folder.", LogType.Warning);
            }
            else
            {
                AddText("Installed UMA support: <b>" +
                    GetSrpDisplayName(installed) + "</b>.");
                if ((active == SrpSupport.Urp || active == SrpSupport.Hdrp) &&
                    active != installed)
                    AddText("The installed UMA support does not match the active " +
                        GetSrpDisplayName(active) + " pipeline. Install matching support before using UMA.",
                        LogType.Error);
                if (!TryReadSrpMarker(installed, out _))
                    AddText("This support folder was copied or installed manually. " +
                        "Use the matching action below once to enable automatic update detection.");
                else if (IsInstalledSrpUpdateAvailable())
                    AddText("A newer bundled UMA " +
                        (installed == SrpSupport.Urp ? "URP" : "HDRP") +
                        " package is available.", LogType.Warning);
            }

            AddSeperator();
            LogLine urpLine = AddText(GetSrpActionLabel(
                SrpSupport.Urp, installed, "URP"));
            urpLine.ButtonAction = line => InstallSrpSupport(
                SrpSupport.Urp, UrpPackageName, "URP");
            LogLine hdrpLine = AddText(GetSrpActionLabel(
                SrpSupport.Hdrp, installed, "HDRP"));
            hdrpLine.ButtonAction = line => InstallSrpSupport(
                SrpSupport.Hdrp, HdrpPackageName, "HDRP");
        }

        private void DoContentPackagesPage()
        {
            ClearLog();
            scrollPosition = Vector2.zero;
            currentButton = 0;
            AddLargeText("UMA Editable Content Packages");
            AddText("UMA character content is installed below <b>Assets/UMA</b> so " +
                "materials, textures, recipes, races, and wardrobe assets remain editable. " +
                "Core code and tools can remain in the read-only UPM package.");
            AddSeperator();

            AddText("<b>1. Render Pipeline Support</b>");
            AddText("Choose exactly one pipeline. These buttons install the bundled UMA " +
                "URP or HDRP package into the editable Assets/UMA/SRP folder.");
#if UMA_PACKAGE_MANAGER
            AddSrpSupportControls();
            AddSeperator();

            AddText("<b>2. UMA 3 Content</b>");
            UMAContentInstallationState uma3State =
                UMAContentPackageInstaller.GetState(UMAContentKind.Uma3);
            string uma3Version = UMAContentPackageInstaller.GetInstalledVersion(
                UMAContentKind.Uma3);
            AddText(ContentStatusText(UMAContentKind.Uma3, uma3State, uma3Version),
                uma3State != UMAContentInstallationState.Installed
                    ? LogType.Warning
                    : LogType.Info);
            LogLine uma3Install = AddText(
                uma3State == UMAContentInstallationState.Missing
                    ? "Install UMA 3 Content..."
                    : "Update, or Reinstall UMA 3 Content...");
            uma3Install.ButtonAction = line =>
                UMAContentPackageInstaller.InstallFromFile(UMAContentKind.Uma3);

            AddSeperator();
            AddText("<b>3. Optional UMA 2 Legacy Content</b>");
            UMAContentInstallationState uma2State =
                UMAContentPackageInstaller.GetState(UMAContentKind.Uma2);
            string uma2Version = UMAContentPackageInstaller.GetInstalledVersion(
                UMAContentKind.Uma2);
            AddText(ContentStatusText(UMAContentKind.Uma2, uma2State, uma2Version),
                uma2State == UMAContentInstallationState.Installed
                    ? LogType.Info
                    : LogType.Warning);
            LogLine uma2Install = AddText(
                uma2State == UMAContentInstallationState.Missing
                    ? "Install Optional UMA 2 Legacy Content..."
                    : "Update, or Reinstall UMA 2 Legacy Content...");
            uma2Install.ButtonAction = line =>
                UMAContentPackageInstaller.InstallFromFile(UMAContentKind.Uma2);
            AddText("Content updates compare the installed manifest with project files. " +
                "Locally edited files are never replaced without an explicit backup-and-replace decision.");
#else             
        AddSeperator();
        // Not using the Package Manager. Manual installation is required.
        AddText("Manual installation of UMA content is required when not using the Package Manager.");
        AddText("In the UMA/SRP folder, double-click the appropriate SRP package to install it.");  
#endif
        }

        private static string ContentStatusText(UMAContentKind kind,
            UMAContentInstallationState state, string version)
        {
            string name = UMAContentCatalog.DisplayName(kind);
            switch (state)
            {
                case UMAContentInstallationState.Installed:
                    return "<b>" + name + " " + version + " is installed</b> at " +
                           UMAContentCatalog.Root(kind) + ".";
                case UMAContentInstallationState.Unmanaged:
                    return "<b>" + name + " is present but not currently validated</b> at " +
                           UMAContentCatalog.Root(kind) +
                           ". It may be unmanaged, incompatible with this Core version, or " +
                           "missing a dependency. Select the matching archive to validate " +
                           "and adopt or update it without silently replacing files.";
                case UMAContentInstallationState.Installing:
                    return "<b>" + name + " is currently being installed.</b>";
                default:
                    return "<b>" + name + " is not installed.</b> Expected destination: " +
                           UMAContentCatalog.Root(kind) + ".";
            }
        }

        private static string GetSrpDisplayName(SrpSupport support)
        {
            return support == SrpSupport.Urp ? "URP" :
                support == SrpSupport.Hdrp ? "HDRP" : "Unknown";
        }

        private static string GetSrpActionLabel(SrpSupport target,
            SrpSupport installed, string displayName)
        {
            if (installed == target)
                return (IsInstalledSrpUpdateAvailable() ? "Update" : "Reinstall") +
                    " UMA " + displayName + " Support";
            return (installed == SrpSupport.None ? "Install" : "Switch to") +
                " UMA " + displayName + " Support";
        }

        private void InstallSrpSupport(SrpSupport support,
            string pipelinePackageName, string displayName)
        {
            SrpSupport active = GetActiveSrpSupport();
            if ((active == SrpSupport.Urp || active == SrpSupport.Hdrp) &&
                active != support)
            {
                AddText("UMA " + displayName + " support cannot be installed while the active " +
                    "project pipeline is " + GetSrpDisplayName(active) + ". Change the Render " +
                    "Pipeline Asset in Project Settings, then try again.", LogType.Error);
                LogLine graphicsSettingsLine = AddText("Open Graphics Settings");
                graphicsSettingsLine.ButtonAction = line =>
                    SettingsService.OpenProjectSettings("Project/Graphics");
                return;
            }

            UMAPackageDependencyStatus.Invalidate();
            if (!UMAPackageDependencyStatus.IsInstalled(pipelinePackageName))
            {
                AddText("The Unity " + displayName + " package is not installed. Install it, then try again.", LogType.Warning);
                LogLine packageManagerLine = AddText("Open UMA Package Dependencies");
                packageManagerLine.ButtonAction = line =>
                    UMAPackageDependencyWindow.OpenAndSelect(pipelinePackageName);
                return;
            }

            string archiveAbsolutePath = GetBundledArchiveAbsolutePath(support);
            if (!File.Exists(archiveAbsolutePath))
            {
                AddText("UMA could not find the bundled " + displayName +
                    " package in its SRP folder.", LogType.Error);
                return;
            }

            string urpArchive = GetBundledArchiveAbsolutePath(SrpSupport.Urp);
            string hdrpArchive = GetBundledArchiveAbsolutePath(SrpSupport.Hdrp);
            if (!UMASrpPackageArchiveValidator.TryValidatePair(urpArchive,
                    hdrpArchive, out string validationError))
            {
                AddText("UMA's bundled render-pipeline installers are invalid: " +
                    validationError, LogType.Error);
                return;
            }

            string destinationDescription = UMAPathUtility.IsPackageInstallation
                ? "This imports UMA's " + displayName +
                    " support into the project-owned Assets/UMA/SRP override."
                : "This replaces Assets/UMA/SRP with UMA's " + displayName +
                    " content.";
            if (!EditorUtility.DisplayDialog("Install UMA " + displayName + " Support?",
                    destinationDescription +
                    " Existing SRP content is backed up under Library/UMA before replacement. " +
                    "Both bundled installer archives remain available so you can switch later.",
                    "Install " + displayName, "Cancel"))
                return;

            BeginSrpPackageImport(support, archiveAbsolutePath);
        }

        private static void BeginSrpPackageImport(SrpSupport support,
            string selectedArchivePath)
        {
            PendingSrpImport existing = LoadPendingSrpImport();
            if (existing != null && !RollbackPendingSrpImport(existing,
                    "Starting a new SRP support installation.", false))
                return;
            if (existing == null && File.Exists(GetPendingSrpImportPath()))
            {
                Debug.LogError("[UMA] The saved SRP transaction record is unreadable. " +
                    "Its backup was left under Library/UMA/SrpInstaller; recover or " +
                    "remove that transaction before starting another installation.");
                return;
            }

            string backupFolder = GetCurrentSrpBackupFolder();
            try
            {
                DeleteDirectoryIfPresent(backupFolder);
                Directory.CreateDirectory(backupFolder);

                string srpAbsolutePath = UMAPathUtility.ResolveAbsolutePath(
                    LegacySrpRoot);
                bool hadPreviousSrp = Directory.Exists(srpAbsolutePath);
                ThrowIfReparsePoint(srpAbsolutePath,
                    "UMA SRP destination");
                if (hadPreviousSrp)
                {
                    if (!File.Exists(srpAbsolutePath + ".meta"))
                        throw new InvalidDataException(
                            "The existing SRP root has no recoverable folder metadata: " +
                            srpAbsolutePath + ".meta");
                    CopyDirectory(srpAbsolutePath,
                        Path.Combine(backupFolder, "SRP"));
                    File.Copy(srpAbsolutePath + ".meta",
                        Path.Combine(backupFolder, "SRP.meta"), true);
                }

                string selectedArchiveName = Path.GetFileName(selectedArchivePath);
                string selectedBackupPath = Path.Combine(backupFolder,
                    selectedArchiveName);
                File.Copy(selectedArchivePath, selectedBackupPath, true);
                string pipeline = support == SrpSupport.Urp ? "URP" : "HDRP";
                if (!UMASrpPackageArchiveValidator.TryValidate(selectedBackupPath,
                        pipeline, out UMASrpPackageArchiveInfo selectedArchive,
                        out string copiedArchiveError))
                    throw new InvalidDataException(
                        "The copied SRP installer failed validation: " + copiedArchiveError);

                PendingSrpImport pending = new PendingSrpImport
                {
                    pipeline = pipeline,
                    sourceHash = ComputeArchiveHash(selectedBackupPath),
                    backupFolder = backupFolder,
                    archiveFileName = selectedArchiveName,
                    expectedPackageName = Path.GetFileNameWithoutExtension(
                        selectedArchiveName),
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    sharedPaths = selectedArchive.SharedPaths.ToArray(),
                    hadPreviousSrp = hadPreviousSrp,
                    restoreInstallerArchives = !UMAPathUtility.IsPackageInstallation
                };
                if (string.IsNullOrEmpty(pending.sourceHash))
                    throw new InvalidDataException(
                        "The copied SRP installer could not be hashed.");
                SavePendingSrpImport(pending);
                DeleteProjectSrpRoot();
                RestorePendingSrpRootIdentity(pending);
                RestorePendingSharedContent(pending);
                AssetDatabase.ImportPackage(selectedBackupPath, false);
            }
            catch (Exception ex)
            {
                Debug.LogError("[UMA] Could not install SRP support: " + ex.Message);
                PendingSrpImport pending = LoadPendingSrpImport();
                if (pending != null)
                    RollbackPendingSrpImport(pending, ex.Message, false);
                else if (!File.Exists(GetPendingSrpImportPath()))
                    DeleteDirectoryIfPresent(backupFolder);
            }
        }

        private static void OnSrpPackageImportCompleted(string packageName)
        {
            PendingSrpImport pending = LoadPendingSrpImport();
            if (IsPendingPackageEvent(pending, packageName))
                EditorApplication.delayCall += CompletePendingSrpImport;
        }

        private static void OnSrpPackageImportCancelled(string packageName)
        {
            PendingSrpImport pending = LoadPendingSrpImport();
            if (IsPendingPackageEvent(pending, packageName))
                RollbackPendingSrpImport(pending,
                    "The UMA SRP package import was cancelled.", true);
        }

        private static void OnSrpPackageImportFailed(string packageName,
            string errorMessage)
        {
            PendingSrpImport pending = LoadPendingSrpImport();
            if (IsPendingPackageEvent(pending, packageName))
                RollbackPendingSrpImport(pending,
                    "The UMA SRP package import failed: " + errorMessage, true);
        }

        private static void ResumePendingSrpImport()
        {
            if (EditorApplication.timeSinceStartup < nextPendingImportCheck)
                return;
            nextPendingImportCheck = EditorApplication.timeSinceStartup + 0.5d;

            PendingSrpImport pending = LoadPendingSrpImport();
            if (pending == null)
            {
                EditorApplication.update -= ResumePendingSrpImport;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.update -= ResumePendingSrpImport;
                EditorApplication.update += ResumePendingSrpImport;
                return;
            }
            if (PendingContentExists(pending))
            {
                EditorApplication.update -= ResumePendingSrpImport;
                CompletePendingSrpImport();
                return;
            }

            if (IsPendingImportWithinRecoveryWindow(pending))
            {
                EditorApplication.update -= ResumePendingSrpImport;
                EditorApplication.update += ResumePendingSrpImport;
                return;
            }

            EditorApplication.update -= ResumePendingSrpImport;
            RollbackPendingSrpImport(pending,
                "Recovered an interrupted UMA SRP import after the editor restarted.", true);
        }

        private static bool IsPendingImportWithinRecoveryWindow(
            PendingSrpImport pending)
        {
            return DateTime.TryParse(pending.startedUtc, out DateTime startedUtc) &&
                   (DateTime.UtcNow - startedUtc.ToUniversalTime()).TotalSeconds <
                   PendingImportRecoverySeconds;
        }

        private static void CompletePendingSrpImport()
        {
            PendingSrpImport pending = LoadPendingSrpImport();
            if (pending == null) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.update -= ResumePendingSrpImport;
                EditorApplication.update += ResumePendingSrpImport;
                return;
            }
            if (!PendingContentExists(pending))
            {
                // The package-completed callback can run while Unity is still
                // importing dependent assets or scheduling a script reload.
                // Keep the transaction and retry instead of destroying the
                // just-imported folder on a transient validation result.
                EditorApplication.update -= ResumePendingSrpImport;
                EditorApplication.update += ResumePendingSrpImport;
                return;
            }

            try
            {
                string destinationFolder = UMAPathUtility.ResolveAbsolutePath(
                    LegacySrpRoot);
                if (pending.restoreInstallerArchives)
                    RestoreInstallerArchives(pending.backupFolder,
                        destinationFolder);

                WriteInstalledSrpMarker(pending, destinationFolder);
                AssetDatabase.Refresh();
                PreservePreviousSrpBackup(pending);
                ErasePendingSrpImport();
                Debug.Log("[UMA] Installed UMA " + pending.pipeline +
                    " render-pipeline support in " + LegacySrpRoot + ".");
                EditorApplication.delayCall += () =>
                {
                    if (Instance != null)
                    {
                        Instance.DoSrpSupportPage();
                        Instance.Repaint();
                    }
                };
            }
            catch (Exception ex)
            {
                RollbackPendingSrpImport(pending,
                    "Could not finish the UMA SRP installation: " + ex.Message,
                    true);
            }
        }

        private static bool PendingContentExists(PendingSrpImport pending)
        {
            string archiveName = !string.IsNullOrEmpty(pending.archiveFileName)
                ? pending.archiveFileName
                : pending.expectedPackageName + ".unitypackage";
            string archivePath = Path.Combine(pending.backupFolder, archiveName);
            if (!File.Exists(archivePath) ||
                !string.Equals(ComputeFileHashUncached(archivePath),
                    pending.sourceHash, StringComparison.OrdinalIgnoreCase) ||
                !UMASrpPackageArchiveValidator.TryValidate(archivePath,
                    pending.pipeline, out UMASrpPackageArchiveInfo archive, out _) ||
                !UMASrpPackageArchiveValidator.TryValidateInstalledFiles(
                    pending.pipeline, archive, out _))
                return false;
            if (!pending.hadPreviousSrp)
                return true;
            string backupMeta = Path.Combine(pending.backupFolder, "SRP.meta");
            string installedMeta = UMAPathUtility.ResolveAbsolutePath(LegacySrpRoot) +
                                   ".meta";
            return File.Exists(backupMeta) && File.Exists(installedMeta) &&
                   File.ReadAllBytes(backupMeta).SequenceEqual(
                       File.ReadAllBytes(installedMeta));
        }

        private static void RestorePendingSrpRootIdentity(PendingSrpImport pending)
        {
            if (!pending.hadPreviousSrp)
                return;
            string backupMeta = Path.Combine(pending.backupFolder, "SRP.meta");
            if (!File.Exists(backupMeta))
                throw new InvalidDataException(
                    "The existing SRP root has no recoverable folder metadata.");
            string destination = UMAPathUtility.ResolveAbsolutePath(LegacySrpRoot);
            Directory.CreateDirectory(destination);
            File.Copy(backupMeta, destination + ".meta", true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void RestorePendingSharedContent(PendingSrpImport pending)
        {
            string[] sharedPaths = pending.sharedPaths ?? Array.Empty<string>();
            if (sharedPaths.Length == 0)
                return;

            string sourceRoot = pending.hadPreviousSrp
                ? Path.Combine(pending.backupFolder, "SRP")
                : UMAPathUtility.ResolveAbsolutePath(
                    UMAPathUtility.ResolveInstallAssetPath("SRP"));
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException(
                    "UMA's shared SRP source folder is missing: " + sourceRoot);

            string destinationRoot = UMAPathUtility.ResolveAbsolutePath(
                LegacySrpRoot);
            Directory.CreateDirectory(destinationRoot);
            foreach (string sharedPath in sharedPaths.OrderBy(path =>
                         path.Count(character => character == '/')))
            {
                string relative = sharedPath.Substring(
                    (LegacySrpRoot + "/").Length).Replace('/',
                    Path.DirectorySeparatorChar);
                string source = Path.GetFullPath(Path.Combine(sourceRoot, relative));
                string destination = Path.GetFullPath(Path.Combine(
                    destinationRoot, relative));
                if (!IsAtOrBelow(source, sourceRoot) ||
                    !IsAtOrBelow(destination, destinationRoot))
                {
                    throw new InvalidDataException(
                        "Unsafe shared UMA SRP path: " + sharedPath);
                }

                if (Directory.Exists(source))
                {
                    Directory.CreateDirectory(destination);
                }
                else if (File.Exists(source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(source, destination, true);
                }
                else
                {
                    throw new FileNotFoundException(
                        "Shared UMA SRP content is missing.", source);
                }

                if (!File.Exists(source + ".meta"))
                    throw new FileNotFoundException(
                        "Shared UMA SRP importer metadata is missing.",
                        source + ".meta");
                File.Copy(source + ".meta", destination + ".meta", true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool IsAtOrBelow(string candidate, string root)
        {
            string fullCandidate = Path.GetFullPath(candidate).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullCandidate, fullRoot,
                       StringComparison.OrdinalIgnoreCase) ||
                   fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPendingPackageEvent(PendingSrpImport pending,
            string packageName)
        {
            if (pending == null)
                return false;
            if (string.IsNullOrEmpty(pending.expectedPackageName))
                return true;
            string eventName = Path.GetFileNameWithoutExtension(
                (packageName ?? string.Empty).Replace('\\', '/'));
            return string.Equals(eventName, pending.expectedPackageName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteInstalledSrpMarker(PendingSrpImport pending,
            string destinationFolder)
        {
            string markerName = string.Equals(pending.pipeline, "URP",
                StringComparison.OrdinalIgnoreCase)
                ? UrpInstalledMarker
                : HdrpInstalledMarker;
            string otherMarkerName = markerName == UrpInstalledMarker
                ? HdrpInstalledMarker
                : UrpInstalledMarker;
            string otherMarkerPath = Path.Combine(destinationFolder,
                otherMarkerName);
            if (File.Exists(otherMarkerPath)) File.Delete(otherMarkerPath);
            if (File.Exists(otherMarkerPath + ".meta"))
                File.Delete(otherMarkerPath + ".meta");

            string umaVersion = string.Empty;
            try
            {
                UMASettings settings = UMASettings.GetOrCreateSettings();
                if (settings != null) umaVersion = settings.UMAVersion;
            }
            catch
            {
                // The archive hash remains sufficient for upgrade detection.
            }

            SrpInstallMarker marker = new SrpInstallMarker
            {
                pipeline = pending.pipeline,
                sourceHash = pending.sourceHash,
                umaVersion = umaVersion,
                installedUtc = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(Path.Combine(destinationFolder, markerName),
                JsonUtility.ToJson(marker, true));
        }

        private static void RestoreInstallerArchives(string backupFolder,
            string destinationFolder)
        {
            string backupSrp = Path.Combine(backupFolder, "SRP");
            if (!Directory.Exists(backupSrp)) return;
            foreach (string archive in Directory.GetFiles(backupSrp,
                         "*.unitypackage", SearchOption.TopDirectoryOnly))
            {
                string destination = Path.Combine(destinationFolder,
                    Path.GetFileName(archive));
                File.Copy(archive, destination, true);
                if (File.Exists(archive + ".meta"))
                    File.Copy(archive + ".meta", destination + ".meta", true);
            }
        }

        private static bool RollbackPendingSrpImport(PendingSrpImport pending,
            string reason, bool logError)
        {
            if (!TryValidatePendingSrpImport(pending, out string pendingError))
            {
                Debug.LogError("[UMA] The SRP transaction is unsafe or incomplete. " +
                    "Its backup was left untouched. " + pendingError);
                return false;
            }
            bool restored = false;
            try
            {
                string backupSrp = Path.Combine(pending.backupFolder, "SRP");
                string backupMeta = Path.Combine(pending.backupFolder, "SRP.meta");
                if (pending.hadPreviousSrp &&
                    (!Directory.Exists(backupSrp) || !File.Exists(backupMeta)))
                    throw new InvalidDataException(
                        "The saved UMA SRP backup or its root metadata is missing.");
                DeleteProjectSrpRoot();
                if (pending.hadPreviousSrp)
                {
                    string destination = UMAPathUtility.ResolveAbsolutePath(
                        LegacySrpRoot);
                    CopyDirectory(backupSrp, destination);
                    File.Copy(backupMeta, destination + ".meta", true);
                }
                restored = true;
            }
            catch (Exception ex)
            {
                reason += " Rollback also failed: " + ex.Message;
            }
            finally
            {
                if (restored)
                {
                    ErasePendingSrpImport();
                    DeleteDirectoryIfPresent(pending.backupFolder);
                }
                AssetDatabase.Refresh();
            }

            if (logError) Debug.LogError("[UMA] " + reason);
            else Debug.LogWarning("[UMA] " + reason);
            return restored;
        }

        private static PendingSrpImport LoadPendingSrpImport()
        {
            string json = string.Empty;
            string persistentPath = GetPendingSrpImportPath();
            if (File.Exists(persistentPath))
            {
                try
                {
                    json = File.ReadAllText(persistentPath);
                }
                catch
                {
                    return null;
                }
            }
            if (string.IsNullOrEmpty(json))
                json = SessionState.GetString(PendingSrpImportKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                PendingSrpImport pending = JsonUtility.FromJson<PendingSrpImport>(json);
                return TryValidatePendingSrpImport(pending, out _) ? pending : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SavePendingSrpImport(PendingSrpImport pending)
        {
            if (!TryValidatePendingSrpImport(pending, out string error))
                throw new InvalidDataException(error);
            string json = JsonUtility.ToJson(pending, true);
            string persistentPath = GetPendingSrpImportPath();
            Directory.CreateDirectory(Path.GetDirectoryName(persistentPath));
            if (File.Exists(persistentPath))
                throw new IOException("An UMA SRP transaction record already exists.");
            string temporary = persistentPath + ".new-" +
                               Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, persistentPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            SessionState.SetString(PendingSrpImportKey, json);
        }

        private static bool TryValidatePendingSrpImport(PendingSrpImport pending,
            out string error)
        {
            error = string.Empty;
            if (pending == null ||
                (!string.Equals(pending.pipeline, "URP",
                     StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(pending.pipeline, "HDRP",
                     StringComparison.OrdinalIgnoreCase)) ||
                !SameFullPath(pending.backupFolder, GetCurrentSrpBackupFolder()) ||
                string.IsNullOrWhiteSpace(pending.archiveFileName) ||
                !string.Equals(Path.GetFileName(pending.archiveFileName),
                    pending.archiveFileName, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(pending.archiveFileName),
                    ".unitypackage", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileNameWithoutExtension(
                        pending.archiveFileName), pending.expectedPackageName,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsHex(pending.sourceHash, 64) ||
                !DateTime.TryParse(pending.startedUtc, out _))
            {
                error = "The SRP transaction contains unsafe paths or invalid " +
                        "integrity metadata.";
                return false;
            }

            var sharedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sharedPath in pending.sharedPaths ?? Array.Empty<string>())
            {
                if (!IsSafeSharedSrpPath(sharedPath) ||
                    !sharedPaths.Add(sharedPath))
                {
                    error = "The SRP transaction contains an unsafe or duplicate " +
                            "shared path.";
                    return false;
                }
            }
            return true;
        }

        private static bool IsSafeSharedSrpPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
                path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 ||
                path.Any(char.IsControl) ||
                path.EndsWith(".unitypackage",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string textureRoot = LegacySrpRoot + "/Textures";
            string shaderRoot = LegacySrpRoot + "/ShaderPackages";
            bool supportedRoot = path.Equals(textureRoot,
                                     StringComparison.OrdinalIgnoreCase) ||
                                 path.StartsWith(textureRoot + "/",
                                     StringComparison.OrdinalIgnoreCase) ||
                                 path.Equals(shaderRoot,
                                     StringComparison.OrdinalIgnoreCase) ||
                                 path.StartsWith(shaderRoot + "/",
                                     StringComparison.OrdinalIgnoreCase);
            return supportedRoot && path.Split('/').All(segment =>
                !string.IsNullOrEmpty(segment) && segment != "." && segment != "..");
        }

        private static bool SameFullPath(string left, string right)
        {
            try
            {
                return string.Equals(Path.GetFullPath(left ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsHex(string value, int length)
        {
            return !string.IsNullOrEmpty(value) && value.Length == length &&
                   value.All(Uri.IsHexDigit);
        }

        private static string ComputeFileHashUncached(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void ErasePendingSrpImport()
        {
            SessionState.EraseString(PendingSrpImportKey);
            string persistentPath = GetPendingSrpImportPath();
            if (File.Exists(persistentPath))
                File.Delete(persistentPath);
        }

        private static string GetSrpInstallerStateRoot()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Unable to resolve the Unity project root.");
            return Path.Combine(projectRoot, "Library", "UMA", "SrpInstaller");
        }

        private static string GetPendingSrpImportPath()
        {
            return Path.Combine(GetSrpInstallerStateRoot(), "PendingImport.json");
        }

        private static string GetCurrentSrpBackupFolder()
        {
            return Path.Combine(GetSrpInstallerStateRoot(), "CurrentBackup");
        }

        private static string GetPreviousSrpBackupFolder()
        {
            return Path.Combine(GetSrpInstallerStateRoot(), "PreviousBackup");
        }

        private static void PreservePreviousSrpBackup(PendingSrpImport pending)
        {
            if (!pending.hadPreviousSrp || !Directory.Exists(pending.backupFolder))
            {
                try
                {
                    DeleteDirectoryIfPresent(pending.backupFolder);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UMA] SRP support installed, but its temporary " +
                        "transaction folder could not be removed: " + ex.Message);
                }
                return;
            }

            string backupSrp = Path.Combine(pending.backupFolder, "SRP");
            string backupMeta = Path.Combine(pending.backupFolder, "SRP.meta");
            if (!Directory.Exists(backupSrp) || !File.Exists(backupMeta))
                throw new InvalidDataException(
                    "The current SRP backup is incomplete; the transaction record " +
                    "was retained.");

            string previousFolder = GetPreviousSrpBackupFolder();
            string retiredFolder = previousFolder + ".retired-" +
                                   Guid.NewGuid().ToString("N");
            bool retired = false;
            bool promoted = false;
            try
            {
                if (Directory.Exists(previousFolder))
                {
                    ThrowIfTreeContainsReparsePoint(previousFolder,
                        "Previous UMA SRP backup");
                    Directory.Move(previousFolder, retiredFolder);
                    retired = true;
                }
                Directory.Move(pending.backupFolder, previousFolder);
                promoted = true;
                Debug.Log("[UMA] The previous SRP folder is recoverable from " +
                    previousFolder + ".");
            }
            catch
            {
                if (promoted && Directory.Exists(previousFolder) &&
                    !Directory.Exists(pending.backupFolder))
                    Directory.Move(previousFolder, pending.backupFolder);
                if (retired && Directory.Exists(retiredFolder) &&
                    !Directory.Exists(previousFolder))
                    Directory.Move(retiredFolder, previousFolder);
                throw;
            }

            try
            {
                DeleteDirectoryIfPresent(retiredFolder);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UMA] The new SRP backup was retained, but an older " +
                    "retired backup could not be removed: " + ex.Message);
            }
        }

        private static void DeleteProjectSrpRoot()
        {
            string absolutePath = UMAPathUtility.ResolveAbsolutePath(
                LegacySrpRoot);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 string.Empty;
            string expectedPath = Path.GetFullPath(Path.Combine(projectRoot,
                LegacySrpRoot));
            if (!string.Equals(Path.GetFullPath(absolutePath), expectedPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Unsafe UMA SRP delete target: " + absolutePath);
            ThrowIfReparsePoint(absolutePath, "UMA SRP delete target");
            ThrowIfTreeContainsReparsePoint(absolutePath,
                "UMA SRP delete target");
            if (AssetDatabase.IsValidFolder(LegacySrpRoot))
            {
                if (!AssetDatabase.DeleteAsset(LegacySrpRoot))
                    throw new InvalidOperationException(
                        "Unable to remove " + LegacySrpRoot + ".");
                return;
            }

            if (Directory.Exists(absolutePath))
                Directory.Delete(absolutePath, true);
            if (File.Exists(absolutePath + ".meta"))
                File.Delete(absolutePath + ".meta");
        }

        private static void CopyDirectory(string source, string destination)
        {
            ThrowIfReparsePoint(source, "UMA SRP backup source");
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                ThrowIfReparsePoint(file, "UMA SRP backup source");
                File.Copy(file, Path.Combine(destination,
                    Path.GetFileName(file)), true);
            }
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination,
                    Path.GetFileName(directory)));
        }

        private static void ThrowIfReparsePoint(string path, string description)
        {
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(description +
                    " cannot be a symbolic link or junction: " + path);
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            ThrowIfTreeContainsReparsePoint(path,
                "UMA SRP installer working directory");
            Directory.Delete(path, true);
        }

        private static void ThrowIfTreeContainsReparsePoint(string path,
            string description)
        {
            if (!Directory.Exists(path)) return;
            ThrowIfReparsePoint(path, description);
            foreach (string file in Directory.GetFiles(path))
                ThrowIfReparsePoint(file, description);
            foreach (string directory in Directory.GetDirectories(path))
            {
                ThrowIfReparsePoint(directory, description);
                ThrowIfTreeContainsReparsePoint(directory, description);
            }
        }

        #region LinksButton
        private void ShowLink(string label, string text, string URL)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label ?? "Link", EditorStyles.boldLabel, GUILayout.Width(96));
            if (!string.IsNullOrEmpty(URL))
            {
                if (GUILayout.Button(text ?? "(open)", Hyperlink,
                        GUILayout.ExpandWidth(true)))
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
