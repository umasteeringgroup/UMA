#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA;
using System.Threading;
using System.Diagnostics;

namespace UMA
{

    public class UMASettings : ScriptableObject
    {
        public const string customSettingsPath = "Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset";

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
        public bool use32bitBuffers = true;
        public bool useAddressables = false;
        public bool enableGLTFExport = false;
        public bool alwaysGetAddressables = true;


        public bool addrUseSharedGroup = true;
        public string addrSharedGroupName = "UMAShared";
        public string addrDefaultLabel = "UMA_Default";
        public bool addStripMaterials = true;
        public bool addrIncludeRecipes = false;
        public bool addrIncludeOther = false;

        public bool showWelcomeToUMA = true;
        public GameObject generatorPrefab;
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

        [Header("Welcome page textures")]
        public Texture2D Overlays;
        public Texture2D Slots;


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
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            TestLoad();
        }

        internal static void TestLoad()
        {
            Stopwatch stopwatch = new Stopwatch();


            stopwatch.Start();
            var settings = AssetDatabase.LoadAssetAtPath<UMASettings>(customSettingsPath);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"LoadAssetAtPath {settings.GetInstanceID()} loaded in " + stopwatch.ElapsedTicks + " ticks");

            stopwatch.Restart();
            var settings2 = AssetDatabase.LoadAssetAtPath<UMASettings>(customSettingsPath);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"LoadAssetAtPath {settings2.GetInstanceID()} loaded in " + stopwatch.ElapsedTicks + " ticks");

            stopwatch.Restart();
            var settings3 = AssetDatabase.LoadAssetAtPath<UMASettings>(customSettingsPath);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"LoadAssetAtPath {settings3.GetInstanceID()} loaded in " + stopwatch.ElapsedTicks + " ticks");

            stopwatch.Restart();
            var settings4 = AssetDatabase.LoadAssetAtPath<UMASettings>(customSettingsPath);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"LoadAssetAtPath {settings4.GetInstanceID()} loaded in " + stopwatch.ElapsedTicks + " ticks");
        }

        public static UMASettings GetSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UMASettings>(customSettingsPath);
            return settings;
        }

        public static UMASettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UMASettings>(customSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<UMASettings>();
                // settings.cities = new List<string>();
                AssetDatabase.CreateAsset(settings, customSettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

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
        public static bool Use32bitBuffers { get { var settings = GetOrCreateSettings(); return settings.use32bitBuffers; } }
        public static bool UseAddressables { get { var settings = GetOrCreateSettings(); return settings.useAddressables; } }
        public static bool EnableGLTFExport { get { var settings = GetOrCreateSettings(); return settings.enableGLTFExport; } }
        public static bool AlwaysGetAddressables { get { var settings = GetOrCreateSettings(); return settings.alwaysGetAddressables; } }
        public static bool AddrUseSharedGroup { get { var settings = GetOrCreateSettings(); return settings.addrUseSharedGroup; } }
        public static string AddrSharedGroupName { get { var settings = GetOrCreateSettings(); return settings.addrSharedGroupName; } }
        public static string AddrDefaultLabel { get { var settings = GetOrCreateSettings(); return settings.addrDefaultLabel; } }
        public static bool AddStripMaterials { get { var settings = GetOrCreateSettings(); return settings.addStripMaterials; } }
        public static bool AddrIncludeRecipes { get { var settings = GetOrCreateSettings(); return settings.addrIncludeRecipes; } }
        public static bool AddrIncludeOther { get { var settings = GetOrCreateSettings(); return settings.addrIncludeOther; } }
    }
}
#endif
