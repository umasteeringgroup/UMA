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
        // Runtime toggle for MeshAPI combiner (Unity 2022.2+)
        [Tooltip("Enable the MeshData API based combiner on Unity 2022.2+. Falls back to legacy combiner when disabled or on older Unity.")]
        public bool useMeshAPICombiner = false;

#if UNITY_EDITOR
        //public const string customSettingsPath = "Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset";

        [Multiline(7)]
        public string WarningMessage = "Warning: Please do not modify these\n settings using the inspector.\n Use the project settings instead.\n Modifying settings that need compiler\n directives set will NOT work if you\n edit them in the inspector!";
        public bool Initialized = false;

        [SerializeField]
        public string UMAVersion = "UMA 2.13.f3";
        [SerializeField]
        public string IgnoreTag = "UMAIgnore";
        [SerializeField]
        public string KeepTag = "UMAKeepChain";
        public string[] tagLookupValues = new string[] { "Head", "Hair", "Torso", "Legs", "Feet", "Hands", "Smooshable", "Unsmooshable", "KeepChain", "Ignore" };

        public bool cleanRegenOnSave = true;
        public bool autoRepairIndex = false;
        public bool showIndexedTypes = true;
        public bool showUnindexedTypes = false;
        public bool postProcessAllAssets = false;

        public bool useBurstCompiler = false;
        public bool useAddressables = false;
        public bool enableGLTFExport = false;
        public bool alwaysGetAddressables = true;


        public bool addrUseSharedGroup = true;
        public string addrSharedGroupName = "UMAShared";
        public string addrDefaultLabel = "UMA_Default";
        public bool addrStripMaterials = true; //VES fixed missing r in addStripMaterials
        public bool addrStripTextures = false;
        public bool addrIncludeRecipes = false;
        public bool addrIncludeOther = false;

        public bool showWelcomeToUMA = true;
#endif
        public GameObject generatorPrefab;
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
        [Tooltip("The folder where the UMA shaders are located, relative to the Assets folder. Usually UMA/Core/ShaderPackages")]
        public string ShaderFolder;
        [Header("Default UMA Folder")]
        [Tooltip("The UMA folder, relative to the Assets folder. Usually Assets/UMA")]
        public string UMAFolder = "Assets/UMA";

        [Header("Welcome page textures")]
        public Texture2D Overlays;
        public Texture2D Slots;
		public static UMASettings instance;


        [MenuItem("Assets/Create/UMA/Core/UMASettings")]
        public static void CreateUMASettingsMenuItem()
        {
            var settings = CustomAssetUtility.CreateAsset<UMASettings>("", true, "UMASettings", true);
            settings.showWelcomeToUMA = true;
            settings.generatorPrefab = null;
            settings.characterPrefab = null;
            settings.DiscordInvite = "https://discord.gg/KdteVKd";
            settings.DiscordURL = "https://discord.com/channels/459433092554162193/537991320636096523";
            settings.WikiURL = "https://github.com/umasteeringgroup/UMA/wiki";
            settings.ForumURL = "https://discussions.unity.com/t/uma-unity-multipurpose-avatar-on-the-asset-store-part-2/1487160";
            settings.AssetStoreURL = "https://assetstore.unity.com/packages/3d/characters/uma-2-35611";
            settings.ShaderFolder = "UMA/Core/ShaderPackages";
            // Default to legacy combiner to avoid surprises
            settings.useMeshAPICombiner = false;
            UpdateAlwaysOverrides(settings); //VES added
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }


        public static UMASettings GetSettings()
        {
            var settings = Resources.Load<UMASettings>("UMASettings");
            UpdateAlwaysOverrides(settings); //VES added
            return settings;
        }

        public static string FindUMAFullPath()
        {
            string folder = "/UMA";
            string OSFolderName = Path.Combine(Application.dataPath, "UMA");
            if (Directory.Exists(OSFolderName))
            {
                // If the UMA folder exists in the Assets directory, return its path
                return "Assets/UMA";
            }

            // Not in the default location, so we need to search for it
            // search the project for the UMA folder
            string[] folders = AssetDatabase.FindAssets("UMA t:Folder");
            if (folders != null && folders.Length > 0)
            {
                foreach (string guid in folders)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        // If we find a folder path that ends with "/UMA", return its path
                        return path;
                    }
                }
            }

            // if we didn't find it, return the default path. Let the chips fall where they may.
            return "Assets/UMA";
        }

        public static UMASettings GetOrCreateSettings()
        {
            if (instance != null)
            {
                return instance;
            }
            string path = FindUMAFullPath() + "/InternalDataStore/InGame/Resources/UMASettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<UMASettings>(path);
#if UNITY_EDITOR
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<UMASettings>();
                // settings.cities = new List<string>();
                settings.useMeshAPICombiner = false;
                UpdateAlwaysOverrides(settings); //VES added
                AssetDatabase.CreateAsset(settings, path);
                AssetDatabase.SaveAssets();
            }
#endif
            UpdateAlwaysOverrides(settings); //VES added
            instance = settings;
            return settings;
        }
#endif

        public static UMASettings GetSettingsFromResources()
        {
            UMASettings settings = Resources.Load<UMASettings>("UMASettings");
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
#if UNITY_EDITOR
#if UMA_ALWAYS_STRIP_MATERIALS
            settings.addrStripMaterials = true;
#endif
#if UMA_ALWAYS_INCLUDE_RECIPES
            settings.addrIncludeRecipes = true;
#endif
#endif
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
        public static bool AddrIncludeRecipes { get { var settings = GetOrCreateSettings(); return settings.addrIncludeRecipes; } }
        public static bool AddrIncludeOther { get { var settings = GetOrCreateSettings(); return settings.addrIncludeOther; } }
#endif
    }
}
