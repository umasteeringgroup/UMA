using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UMA;
using System.Threading;
using System.Diagnostics;
using System;
using System.IO;

namespace UMA
{

    public class UMASettings : ScriptableObject
    {
        public const string DefaultIgnoreTag = "UMAIgnore";

#if UNITY_EDITOR
        private const string DefaultSettingsRelativePath =
            "InternalDataStore/InGame/Resources/UMASettings.asset";
        private const string DefaultGeneratorRelativePath =
            "Core/Defaults/UMA_GLIB.prefab";
        private const string DefaultCharacterRelativePath =
            "Core/Defaults/UMADynamicCharacterAvatar.prefab";
        private const string DefaultTextureMergeRelativePath =
            "Core/StandardAssets/UMA/Atlas/TextureMerge.asset";
        private const string BuiltInFallbackVersion = "UMA NextGen 3.0f4";
        private static UMASettings transientSettings;
#endif

        [SerializeField]
        [Tooltip("UMA ignores objects with this tag when rebuilding the skeleton.")]
        public string IgnoreTag = DefaultIgnoreTag;

        // Runtime toggle for MeshAPI combiner (Unity 2022.2+)
        [Tooltip("Enable the MeshData API based combiner on Unity 2022.2+. Falls back to legacy combiner when disabled or on older Unity.")]
        public bool useMeshAPICombiner = false;
		[Tooltip("Enable detailed UMA memory usage debug logs.")]
		public bool DebugMemoryUsage = false;
		[Tooltip("Enable decal callbacks on UMA characters.")]
		public bool DisableDecalCallbacks = false;

#if UNITY_EDITOR
        //public const string customSettingsPath = "Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset";

        [Multiline(7)]
        public string WarningMessage = "Warning: Please do not modify these\n settings using the inspector.\n Use the project settings instead.\n Modifying settings that need compiler\n directives set will NOT work if you\n edit them in the inspector!";
        public bool Initialized = false;

        [SerializeField]
        public string UMAVersion = BuiltInFallbackVersion;
        [SerializeField]
        public string KeepTag = "UMAKeepChain";
        public string[] tagLookupValues = new string[] { "Head", "Hair", "Torso", "Legs", "Feet", "Hands", "Smooshable", "Unsmooshable", "KeepChain", "Ignore" };
        public string[] groupNames = new string[] { "Head", "Body", "Arms", "Legs", "Feet", "Hands"};
        public bool cleanRegenOnSave = true;
        public bool autoRepairIndex = false;
        public bool showIndexedTypes = true;
        public bool showUnindexedTypes = false;
        public bool postProcessAllAssets = false;

        public bool useBurstCompiler = false;
        public bool useAddressables = false;
        public bool enableGLTFExport = false;
        public bool alwaysGetAddressables = true;
        public bool ignoreBackupFolders = false;

        public bool addDNAOnRaceChange = true;
        public bool addrUseSharedGroup = true;
        public string addrSharedGroupName = "UMAShared";
        public string addrDefaultLabel = "UMA_Default";
        public bool addrStripMaterials = true; //VES fixed missing r in addStripMaterials
        public bool addrStripTextures = false;
        public bool addrIncludeRecipes = false;
        public bool addrIncludeOther = false;
        public bool addrStripUVAttachedShaders = false; 

        public bool showWelcomeToUMA = true;
        [Tooltip("Show the UMA Toolbar overlay in the Scene view.")]
        public bool showToolbar = true;
#endif
        public GameObject generatorPrefab;
		public static UMASettings instance;
#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeOnLoad()
        {
            instance = GetSettings();
        }
#endif        
#if UNITY_EDITOR
        public GameObject characterPrefab;
        public TextureMerge textureMerge;

        [Header("Links")]
        public string DiscordInvite;
        public string DiscordURL;
        public string WikiURL;
        public string ForumURL;
        public string AssetStoreURL;
        public string GithubURL;
        public string YoutubeURL;
        [Header("Shader Folder")]
        [Tooltip("The UMA-relative folder containing the shader packages used to refresh UMA shaders.")]
        public string ShaderFolder = UMAPathUtility.ShaderPackagesRelativePath;
        [Header("Default UMA Folder")]
        [Tooltip("Resolved UMA installation asset path. UMA may be below Assets or installed as a UPM package.")]
        public string UMAFolder = "Assets/UMA";
        [Header("Overlay Painter")]
        [Tooltip("Open Overlay Painter in a dedicated floating workspace with Layers and Brush " +
            "docked together beside the Scene and 2D views. Disable this to open the three " +
            "Overlay Painter panels as ordinary independent dockable windows.")]
        public bool texturePaintCompactView = true;
        [Tooltip("Project folder used for the temporary Overlay Painter recovery asset and its data files. " +
            "This folder must be below Assets and can be excluded from source control.")]
        public string texturePaintRecoveryFolder = UMAPathUtility.OverlayPainterRecoveryRoot;
        [Tooltip("Periodically save recoverable Overlay Painter changes in the background. This also " +
            "autosaves modified permanent TexturePaintDocuments. Manual Save and close protection remain available when disabled.")]
        public bool texturePaintAutomaticRecovery = true;
        [Tooltip("Seconds Overlay Painter waits after the most recent edit before starting a background save.")]
        [Min(15f)] public float texturePaintRecoveryIdleDelaySeconds = 120f;
        [Tooltip("Minimum seconds between completed Overlay Painter background saves.")]
        [Min(0f)] public float texturePaintRecoveryMinimumIntervalSeconds = 300f;

        [Header("Welcome page textures")]
        public Texture2D Overlays;
        public Texture2D Slots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeOnLoad()
        {
            instance = GetSettings();
        }


        [MenuItem("Assets/Create/UMA/Core/UMASettings")]
        public static void CreateUMASettingsMenuItem()
        {
            var settings = CustomAssetUtility.CreateAsset<UMASettings>("", true, "UMASettings", true);
            settings.showWelcomeToUMA = true;
            settings.showToolbar = true;
            settings.generatorPrefab = null;
            settings.characterPrefab = null;
            settings.DiscordInvite = "https://discord.gg/KdteVKd";
            settings.DiscordURL = "https://discord.com/channels/459433092554162193/537991320636096523";
            settings.WikiURL = "https://github.com/umasteeringgroup/UMA/wiki";
            settings.ForumURL = "https://discussions.unity.com/t/uma-unity-multipurpose-avatar-on-the-asset-store-part-2/1487160";
            settings.AssetStoreURL = "https://assetstore.unity.com/packages/3d/characters/uma-2-35611";
            settings.ShaderFolder = UMAPathUtility.ShaderPackagesRelativePath;
            // Default to legacy combiner to avoid surprises
            settings.useMeshAPICombiner = false;
            UpdateAlwaysOverrides(settings); //VES added
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }



        public static string FindUMAFullPath()
        {
            return UMAPathUtility.InstallAssetRoot;
        }
#endif

		public static UMASettings GetSettings() {
			if (HasResolvedSettingsCache())
				return instance;
#if UNITY_EDITOR
			return GetOrCreateSettings();
#else
			var settings = LoadPreferredSettings();
			UpdateAlwaysOverrides(settings); //VES added
			if (settings != null) instance = settings;
			return settings;
#endif
		}

        private static bool HasResolvedSettingsCache()
        {
#if UNITY_EDITOR
            return instance != null && instance != transientSettings;
#else
            return instance != null;
#endif
        }

        private static UMASettings LoadPreferredSettings()
        {
            UMASettings settings = Resources.Load<UMASettings>("UMAProjectSettings");
            if (settings != null) return settings;
#if UNITY_EDITOR
            settings = LoadInstallDefaultSettings();
            if (settings != null) return settings;
#endif
            return Resources.Load<UMASettings>("UMASettings");
        }

#if UNITY_EDITOR
        private static UMASettings LoadInstallDefaultSettings()
        {
            UMASettings settings = AssetDatabase.LoadAssetAtPath<UMASettings>(
                UMAPathUtility.ResolveInstallAssetPath(
                    DefaultSettingsRelativePath));
            if (settings != null) return settings;

            // Package import callbacks can run before PackageInfo and the
            // assembly-definition path have settled. Find the shipped asset by
            // its installation-relative suffix instead of creating a permanent
            // project asset from the C# field defaults.
            string suffix = "/" + DefaultSettingsRelativePath;
            string[] guids = AssetDatabase.FindAssets("t:UMASettings");
            Array.Sort(guids, StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UMAPathUtility.Normalize(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (!path.EndsWith(suffix,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                settings = AssetDatabase.LoadAssetAtPath<UMASettings>(path);
                if (settings != null) return settings;
            }
            return null;
        }

        private static string GetInstallRoot(UMASettings installDefault)
        {
            string path = UMAPathUtility.Normalize(
                AssetDatabase.GetAssetPath(installDefault));
            string suffix = "/" + DefaultSettingsRelativePath;
            return path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - suffix.Length)
                : UMAPathUtility.InstallAssetRoot;
        }

        private static bool IsBuiltInFallbackCopy(
            UMASettings settings,
            UMASettings installDefault)
        {
            return settings != null && installDefault != null &&
                settings != installDefault &&
                (string.IsNullOrWhiteSpace(settings.UMAVersion) ||
                 string.Equals(settings.UMAVersion,
                     BuiltInFallbackVersion,
                     StringComparison.Ordinal)) &&
                !string.Equals(installDefault.UMAVersion,
                    BuiltInFallbackVersion,
                    StringComparison.Ordinal);
        }

        private static bool SynchronizeInstallMetadata(
            UMASettings settings,
            UMASettings installDefault)
        {
            if (settings == null || installDefault == null ||
                settings == installDefault)
                return false;

            bool changed = false;
            changed |= CopyIfDifferent(ref settings.UMAVersion,
                installDefault.UMAVersion);
            changed |= CopyIfDifferent(ref settings.WarningMessage,
                installDefault.WarningMessage);
            changed |= CopyIfDifferent(ref settings.DiscordInvite,
                installDefault.DiscordInvite);
            changed |= CopyIfDifferent(ref settings.DiscordURL,
                installDefault.DiscordURL);
            changed |= CopyIfDifferent(ref settings.WikiURL,
                installDefault.WikiURL);
            changed |= CopyIfDifferent(ref settings.ForumURL,
                installDefault.ForumURL);
            changed |= CopyIfDifferent(ref settings.AssetStoreURL,
                installDefault.AssetStoreURL);
            changed |= CopyIfDifferent(ref settings.GithubURL,
                installDefault.GithubURL);
            changed |= CopyIfDifferent(ref settings.YoutubeURL,
                installDefault.YoutubeURL);
            if (settings.Overlays != installDefault.Overlays)
            {
                settings.Overlays = installDefault.Overlays;
                changed = true;
            }
            if (settings.Slots != installDefault.Slots)
            {
                settings.Slots = installDefault.Slots;
                changed = true;
            }
            return changed;
        }

        private static bool CopyIfDifferent(ref string target, string source)
        {
            if (string.Equals(target, source, StringComparison.Ordinal))
                return false;
            target = source;
            return true;
        }

        private static bool UpgradeLegacyShaderFolder(UMASettings settings)
        {
            if (settings == null) return false;

            string shaderFolder = UMAPathUtility.Normalize(
                settings.ShaderFolder).Trim('/');
            bool usesLegacyPath = string.IsNullOrEmpty(shaderFolder) ||
                shaderFolder.Equals("Core/ShaderPackages",
                    StringComparison.OrdinalIgnoreCase) ||
                shaderFolder.Equals("UMA/Core/ShaderPackages",
                    StringComparison.OrdinalIgnoreCase) ||
                shaderFolder.Equals(
                    UMAPathUtility.LegacyInstallRoot + "/Core/ShaderPackages",
                    StringComparison.OrdinalIgnoreCase);
            if (!usesLegacyPath) return false;

            settings.ShaderFolder = UMAPathUtility.ShaderPackagesRelativePath;
            return true;
        }

        private static UMASettings GetTransientSettings()
        {
            if (transientSettings == null)
            {
                transientSettings = CreateInstance<UMASettings>();
                transientSettings.name = "UMASettings (Importing)";
                transientSettings.hideFlags = HideFlags.HideAndDontSave;
                transientSettings.useMeshAPICombiner = false;
            }
            instance = transientSettings;
            return transientSettings;
        }

        public static void InvalidateSettingsCache()
        {
            instance = null;
        }

        public static bool IsSettingsAssetPath(string assetPath)
        {
            string path = UMAPathUtility.Normalize(assetPath);
            return path.Equals(UMAPathUtility.ProjectSettingsPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/" + DefaultSettingsRelativePath,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void ReleaseTransientSettings()
        {
            if (transientSettings == null) return;
            UMASettings transient = transientSettings;
            transientSettings = null;
            if (transient != null) DestroyImmediate(transient);
        }

        private static T LoadInstallDefaultAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            return UMAPathUtility.LoadInstallAsset<T>(relativePath);
        }

        private static bool RestoreMissingDefaultReferences(
            UMASettings settings,
            UMASettings installDefault)
        {
            if (settings == null) return false;
            bool changed = false;

            if (settings.generatorPrefab == null)
            {
                settings.generatorPrefab = installDefault != null
                    ? installDefault.generatorPrefab : null;
                if (settings.generatorPrefab == null)
                    settings.generatorPrefab =
                        LoadInstallDefaultAsset<GameObject>(
                            DefaultGeneratorRelativePath);
                changed |= settings.generatorPrefab != null;
            }
            if (settings.characterPrefab == null)
            {
                settings.characterPrefab = installDefault != null
                    ? installDefault.characterPrefab : null;
                if (settings.characterPrefab == null)
                    settings.characterPrefab =
                        LoadInstallDefaultAsset<GameObject>(
                            DefaultCharacterRelativePath);
                changed |= settings.characterPrefab != null;
            }
            if (settings.textureMerge == null)
            {
                settings.textureMerge = installDefault != null
                    ? installDefault.textureMerge : null;
                if (settings.textureMerge == null)
                    settings.textureMerge =
                        LoadInstallDefaultAsset<TextureMerge>(
                            DefaultTextureMergeRelativePath);
                changed |= settings.textureMerge != null;
            }
            if (settings.Overlays == null && installDefault != null &&
                installDefault.Overlays != null)
            {
                settings.Overlays = installDefault.Overlays;
                changed = true;
            }
            if (settings.Slots == null && installDefault != null &&
                installDefault.Slots != null)
            {
                settings.Slots = installDefault.Slots;
                changed = true;
            }

            return changed;
        }
#endif

        /// <summary>
        /// Returns the configured ignore tag, falling back to UMA's default when the
        /// settings asset is unavailable or contains a value that would ignore every
        /// untagged transform.
        /// </summary>
        public static string GetIgnoreTag()
        {
            if (instance == null)
            {
                instance = GetSettings();
            }

            string tag = instance != null ? instance.IgnoreTag : null;
            if (string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "Untagged", StringComparison.Ordinal))
            {
                return DefaultIgnoreTag;
            }

            return tag.Trim();
        }

        /// <summary>
        /// Resolves the configured ignore tag and verifies that it exists in Unity's
        /// tag list. Returns null and reports a useful error instead of allowing
        /// CompareTag or GameObject.tag to throw a UnityException.
        /// </summary>
        public static string GetValidatedIgnoreTag(GameObject probe)
        {
            string tag = GetIgnoreTag();
            if (probe == null)
            {
                return tag;
            }

            try
            {
                probe.CompareTag(tag);
                return tag;
            }
            catch (UnityException exception)
            {
                UnityEngine.Debug.LogError(
                    $"[UMA] The configured IgnoreTag '{tag}' is not defined in Unity's Tags and Layers settings. " +
                    $"Ignore-tag processing has been disabled for this operation. {exception.Message}",
                    probe);
                return null;
            }
        }

        public static bool TryAssignIgnoreTag(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            string tag = GetValidatedIgnoreTag(target);
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            target.tag = tag;
            return true;
        }



        public static UMASettings GetOrCreateSettings()
        {
#if UNITY_EDITOR
            if (HasResolvedSettingsCache())
            {
                UpdateAlwaysOverrides(instance);
                return instance;
            }

            UMASettings settings = AssetDatabase.LoadAssetAtPath<UMASettings>(
                UMAPathUtility.ProjectSettingsPath);
            UMASettings installDefault = LoadInstallDefaultSettings();
            string installRoot = installDefault != null
                ? GetInstallRoot(installDefault)
                : UMAPathUtility.InstallAssetRoot;
            bool packageInstallation = installRoot.StartsWith(
                "Packages/", StringComparison.OrdinalIgnoreCase);
            if (settings == null && !packageInstallation)
                settings = installDefault;
            if (settings == null)
            {
                if (installDefault == null)
                    return GetTransientSettings();

                settings = Instantiate(installDefault);
                settings.name = "UMAProjectSettings";
                settings.UMAFolder = installRoot;
                settings.texturePaintRecoveryFolder =
                    UMAPathUtility.OverlayPainterRecoveryRoot;
                UpdateAlwaysOverrides(settings); //VES added
                UMAPathUtility.EnsureAssetFolder(
                    UMAPathUtility.ProjectResourcesRoot);
                AssetDatabase.CreateAsset(settings,
                    UMAPathUtility.ProjectSettingsPath);
                AssetDatabase.SaveAssets();
            }

            bool changed = false;
            if (IsBuiltInFallbackCopy(settings, installDefault))
            {
                EditorUtility.CopySerialized(installDefault, settings);
                settings.name = "UMAProjectSettings";
                changed = true;
            }
            changed |= SynchronizeInstallMetadata(settings, installDefault);
            changed |= RestoreMissingDefaultReferences(
                settings, installDefault);
            changed |= UpgradeLegacyShaderFolder(settings);
            if (packageInstallation)
            {
                if (!string.Equals(settings.UMAFolder,
                    installRoot,
                    StringComparison.OrdinalIgnoreCase))
                {
                    settings.UMAFolder = installRoot;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(
                        settings.texturePaintRecoveryFolder) ||
                    settings.texturePaintRecoveryFolder.StartsWith(
                        UMAPathUtility.LegacyInstallRoot + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    settings.texturePaintRecoveryFolder =
                        UMAPathUtility.OverlayPainterRecoveryRoot;
                    changed = true;
                }
                if (!string.IsNullOrEmpty(settings.ShaderFolder) &&
                    settings.ShaderFolder.StartsWith(
                        UMAPathUtility.LegacyInstallRoot + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    settings.ShaderFolder = settings.ShaderFolder.Substring(
                        UMAPathUtility.LegacyInstallRoot.Length + 1);
                    changed = true;
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            UpdateAlwaysOverrides(settings); //VES added
            ReleaseTransientSettings();
            instance = settings;
            return settings;
#else
            if (instance != null)
            {
                return instance;
            }
            UMASettings resourceSettings = LoadPreferredSettings();
            if (resourceSettings != null)
            {
                UpdateAlwaysOverrides(resourceSettings);
                instance = resourceSettings;
                return resourceSettings;
            }
            var settings = ScriptableObject.CreateInstance<UMASettings>();
            // settings.cities = new List<string>();
            settings.useMeshAPICombiner = false;
            UpdateAlwaysOverrides(settings); //VES added
			instance = settings;
			return settings;
#endif
        }


        public static UMASettings GetSettingsFromResources()
        {
#if UNITY_EDITOR
            UMASettings settings = GetOrCreateSettings();
#else
            UMASettings settings = LoadPreferredSettings();
#endif
            UpdateAlwaysOverrides(settings); //VES added
            return settings;
        }

        // Runtime accessor for the toggle
        public static bool UseMeshAPICombiner
        {
            get
            {
                var s = GetSettingsFromResources();
                return s != null && s.useMeshAPICombiner;
            }
        }

        static void UpdateAlwaysOverrides(UMASettings settings) { //VES added
            if (settings == null) return;
#if UNITY_EDITOR
#if UMA_ALWAYS_STRIP_MATERIALS
            settings.addrStripMaterials = true;
#endif
#if UMA_ALWAYS_INCLUDE_RECIPES
            settings.addrIncludeRecipes = true;
#endif
#endif
        }

		public static bool DisplayDebugMemoryUsage {
			get {
				var settings = GetOrCreateSettings();
				return settings.DebugMemoryUsage;
			}
		}

#if UNITY_EDITOR
        public static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }

        public static bool CleanRegenOnSave
        {
            get
            {
                var settings = GetOrCreateSettings();
                return settings.cleanRegenOnSave;
            }
        }

        public static bool ShowToolbar
        {
            get
            {
                var settings = GetOrCreateSettings();
                return settings == null || settings.showToolbar;
            }
        }

        public static event Action<bool> ToolbarVisibilityChanged;
        public static event Action ProjectWindowTypeDisplayChanged;

        public static void NotifyToolbarVisibilityChanged(bool show)
        {
            ToolbarVisibilityChanged?.Invoke(show);
        }

        public static void NotifyProjectWindowTypeDisplayChanged()
        {
            ProjectWindowTypeDisplayChanged?.Invoke();
        }

        public static bool IgnoreBackupFolders
        {
            get
            {
                var settings = GetOrCreateSettings();
                return settings.ignoreBackupFolders;
            }
        }
        public static string TexturePaintRecoveryFolder
        {
            get
            {
                var settings = GetOrCreateSettings();
                string configured = settings != null ? settings.texturePaintRecoveryFolder : null;
                if (string.IsNullOrWhiteSpace(configured)) return UMAPathUtility.OverlayPainterRecoveryRoot;
                configured = configured.Trim().Replace('\\', '/').TrimEnd('/');
                string[] parts = configured.Split('/');
                if (!configured.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || parts.Length < 2)
                    return UMAPathUtility.OverlayPainterRecoveryRoot;
                for (int i = 0; i < parts.Length; i++)
                    if (string.IsNullOrWhiteSpace(parts[i]) || parts[i] == "." || parts[i] == "..")
                        return UMAPathUtility.OverlayPainterRecoveryRoot;
                return configured;
            }
        }
        public static bool TexturePaintCompactView
        {
            get
            {
                var settings = GetOrCreateSettings();
                return settings == null || settings.texturePaintCompactView;
            }
        }
        public static bool TexturePaintAutomaticRecovery
        {
            get
            {
                var settings = GetOrCreateSettings();
                return settings == null || settings.texturePaintAutomaticRecovery;
            }
        }
        public static double TexturePaintRecoveryIdleDelaySeconds
        {
            get
            {
                var settings = GetOrCreateSettings();
                return Math.Max(15d, settings != null
                    ? settings.texturePaintRecoveryIdleDelaySeconds : 120d);
            }
        }
        public static double TexturePaintRecoveryMinimumIntervalSeconds
        {
            get
            {
                var settings = GetOrCreateSettings();
                return Math.Max(0d, settings != null
                    ? settings.texturePaintRecoveryMinimumIntervalSeconds : 300d);
            }
        }
        public static bool AutoRepairIndex { get { var settings = GetOrCreateSettings(); return settings.autoRepairIndex; } }
        public static bool ShowIndexedTypes { get { var settings = GetOrCreateSettings(); return settings.showIndexedTypes; } }
        public static bool ShowUnindexedTypes { get { var settings = GetOrCreateSettings(); return settings.showUnindexedTypes; } }
        public static bool PostProcessAllAssets { get { var settings = GetOrCreateSettings(); return settings.postProcessAllAssets; } }
        public static bool UseBurstCompiler { get { var settings = GetOrCreateSettings(); return settings.useBurstCompiler; } }
        public static bool UseAddressables { get { var settings = GetOrCreateSettings(); return settings.useAddressables; } }
        public static bool EnableGLTFExport { get { var settings = GetOrCreateSettings(); return settings.enableGLTFExport; } }
        public static bool AlwaysGetAddressables { get { var settings = GetOrCreateSettings(); return settings.alwaysGetAddressables; } }
        public static bool AddrUseSharedGroup { get { var settings = GetOrCreateSettings(); return settings.addrUseSharedGroup; } }
        public static string AddrSharedGroupName { get { var settings = GetOrCreateSettings(); return settings.addrSharedGroupName; } }
        public static string AddrDefaultLabel { get { var settings = GetOrCreateSettings(); return settings.addrDefaultLabel; } }
        public static bool AddrStripMaterials { get { var settings = GetOrCreateSettings(); return settings.addrStripMaterials; } } //VES fixed missing r in AddStripMaterials
        public static bool AddrStripTextures { get { var settings = GetOrCreateSettings(); return settings.addrStripTextures; } }
        public static bool AddrStripUVAttachedShaders { get { var settings = GetOrCreateSettings(); return settings.addrStripUVAttachedShaders; } }
        public static bool AddrIncludeRecipes { get { var settings = GetOrCreateSettings(); return settings.addrIncludeRecipes; } }
        public static bool AddrIncludeOther { get { var settings = GetOrCreateSettings(); return settings.addrIncludeOther; } }
#endif
    }
}
