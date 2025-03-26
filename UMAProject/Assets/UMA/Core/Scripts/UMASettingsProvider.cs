#if UNITY_EDITOR
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.Linq;
using UMA;
using UnityEditor.Build;

namespace UMA
{

    class UMASettingsProvider : SettingsProvider
    {
        public const string DefineSymbol_32BitBuffers = "UMA_32BITBUFFERS";
        public const string DefineSymbol_Addressables = "UMA_ADDRESSABLES";
        public const string DefineSymbol_BurstCompile = "UMA_BURSTCOMPILE";
        public const string DefineSymbol_UMAAlwaysGetAddressableItems = "UMA_ALWAYSGETADDR_NO_PROD";
        public const string DefineSymbol_GLTFExport = "UMA_GLTF";

        //private const string DefineSymbol_AsmDef = "UMA_ASMDEF";
        public const string ConfigToggle_LeanMeanSceneFiles = "UMA_CLEANUP_GENERATED_DATA_ON_SAVE";
        public const string ConfigToggle_UseSharedGroup = "UMA_ADDRESSABLES_USE_SHARED_GROUP";
        public const string ConfigToggle_ArchiveGroups = "UMA_ADDRESSABLES_ARCHIVE_ASSETBUNDLE_GROUPS";

        public const string ConfigToggle_AddCollectionLabels = "UMA_SHAREDGROUP_ADDCOLLECTIONLABELS";
        public const string ConfigToggle_IncludeRecipes = "UMA_SHAREDGROUP_INCLUDERECIPES";
        public const string ConfigToggle_IncludeOther = "UMA_SHAREDGROUP_INCLUDEOTHERINDEXED";
        public const string ConfigToggle_StripUmaMaterials = "UMA_SHAREDGROUP_STRIPUMAMATERIALS";
        public const string ConfigToggle_PostProcessAllAssets = "UMA_POSTPROCESS_ALL_ASSETS";
        public const string ConfigToggle_IndexAutoRepair = "UMA_INDEX_AUTOREPAIR";

        private string dots = "";


        private SerializedObject m_CustomSettings;

        public UMASettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            m_CustomSettings = UMASettings.GetSerializedSettings();
        }

        public static NamedBuildTarget CurrentNamedBuildTarget
        {
            get
            {
#if UNITY_SERVER
                    return NamedBuildTarget.Server;
#else
                BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
                BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
                NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
                return namedBuildTarget;
#endif
            }
        }

        public static void BeginVerticalPadded(float padding, Color backgroundColor, GUIStyle theStyle = null)
        {
            if (theStyle == null)
            {
                theStyle = EditorStyles.textField;
            }

            GUI.color = backgroundColor;
            GUILayout.BeginHorizontal(theStyle);
            GUI.color = Color.white;

            GUILayout.Space(padding);
            GUILayout.BeginVertical();
            GUILayout.Space(padding);
        }

        public static void EndVerticalPadded(float padding)
        {
            GUILayout.Space(padding);
            GUILayout.EndVertical();
            GUILayout.Space(padding);
            GUILayout.EndHorizontal();
        }

        protected internal static void DrawPropertiesExcluding(SerializedObject obj, params string[] propertyToExclude)
        {
            SerializedProperty iterator = obj.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (!propertyToExclude.Contains(iterator.name))
                {
                    //Debug.Log("Drawing property " + iterator.name);
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }
        }

        protected internal static void DrawPropertiesIncluding(SerializedObject obj, params string[] propertyToInclude)
        {
            SerializedProperty iterator = obj.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (propertyToInclude.Contains(iterator.name))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }
        }


        public void DrawBoolConfigToggle(string propertyName, string label, string tooltip, string defineSymbol, HashSet<string> defineSymbols, bool burst = false)
        {
            SerializedProperty prop = m_CustomSettings.FindProperty(propertyName);
            EditorGUI.BeginChangeCheck();
            prop.boolValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), prop.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                Debug.Log($"{label} changed to {prop.boolValue} burst = {burst}");
                if (burst)
                {
                    if (prop.boolValue)
                    {
                        string datapath = Application.dataPath;
                        string sourceFile = Path.Combine(datapath, "uma", "core", "uma_core_burst.dat");
                        string destFile = Path.Combine(datapath, "uma", "core", "uma_core.asmdef");
                        Debug.Log($"Burst changed to {prop.boolValue}-Copying from {sourceFile} to {destFile}");
                        File.Copy(sourceFile, destFile, true);
                        AssetDatabase.Refresh();
                        Debug.Log("File copied");
                    }
                    else
                    {
                        string datapath = Application.dataPath;
                        string sourceFile = Path.Combine(datapath, "uma", "core", "uma_core_noburst.dat");
                        string destFile = Path.Combine(datapath, "uma", "core", "uma_core.asmdef");
                        Debug.Log($"Burst changed to {prop.boolValue}-Copying from {sourceFile} to {destFile}");
                        File.Copy(sourceFile, destFile, true);
                        AssetDatabase.Refresh();
                        Debug.Log("File copied");
                    }
                }
                m_CustomSettings.ApplyModifiedProperties();
                if (prop.boolValue)
                {
                    if (!defineSymbols.Contains(defineSymbol))
                    {
                        Debug.Log("Adding define symbol " + defineSymbol);
                        defineSymbols.Add(defineSymbol);
                        PlayerSettings.SetScriptingDefineSymbols(CurrentNamedBuildTarget, string.Join(";", defineSymbols));
                        AssetDatabase.SaveAssets();
                    }
                }
                else
                {
                    if (defineSymbols.Contains(defineSymbol))
                    {
                        Debug.Log("Removing define symbol " + defineSymbol);
                        defineSymbols.Remove(defineSymbol);
                        PlayerSettings.SetScriptingDefineSymbols(CurrentNamedBuildTarget, string.Join(";", defineSymbols));
                        AssetDatabase.SaveAssets();
                    }
                }
            }
        }

        public void DrawBoolProperty(string propertyName, string label, string tooltip)
        {
            SerializedProperty prop = m_CustomSettings.FindProperty(propertyName);
            EditorGUI.BeginChangeCheck();
            prop.boolValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), prop.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                m_CustomSettings.ApplyModifiedProperties();
            }
        }

        public void DrawObjectProperty(string propertyName, string label, string tooltip, System.Type type)
        {
            SerializedProperty prop = m_CustomSettings.FindProperty(propertyName);
            EditorGUILayout.ObjectField(prop, type, new GUIContent(label, tooltip));
        }

        public void DrawStringProperty(string propertyName, string label, string toolTip)
        {
            SerializedProperty prop = m_CustomSettings.FindProperty(propertyName);
            EditorGUI.BeginChangeCheck();
            prop.stringValue = EditorGUILayout.TextField(new GUIContent(label, toolTip), prop.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                m_CustomSettings.ApplyModifiedProperties();
            }
        }

        public override void OnGUI(string searchContext)
        {
            if (EditorApplication.isCompiling)
            {
                dots += ".";
                if (dots.Length > 20)
                    dots = "";
                GUILayout.Space(30);
                EditorGUILayout.LabelField("    Compile in progress  " + dots);
                System.Threading.Thread.Sleep(100);
                Repaint();
                return;
            }

            dots = "";

            var defineSymbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup).Split(';'));

            EditorGUILayout.LabelField("UMA Version " + m_CustomSettings.FindProperty("UMAVersion").stringValue, EditorStyles.boldLabel);
            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These tags are used by UMA to identify different types of assets", MessageType.Info);
            //DrawPropertiesExcluding(m_CustomSettings, new string[] { "UMAVersion","m_Script","Use32bitBuffers", "UseBurstCompiler", "UseAddressables", "EnableGLTFExport" ,
            //    "AddrUseSharedGroup", "AddrSharedGroupName", "AddrDefaultLabel", "AddStripMaterials", "AddrIncludeRecipes", "CleanRegenOnSave", "AutoRepairIndex", "ShowIndexedTypes", "ShowUnindexedTypes", "PostProcessAllAssets" 
            //    });

            DrawPropertiesIncluding(m_CustomSettings, new string[] { "IgnoreTag", "KeepTag", "tagLookupValues" });



            DrawPropertiesIncluding(m_CustomSettings, new string[] { "UMAVersion" });
            EndVerticalPadded(10);

            GUILayout.Space(10);

            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Editor Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These settings control the behavior of UMA in the editor", MessageType.Info);
            DrawBoolProperty("cleanRegenOnSave", "Clean Regen On Save", "If true, UMA will destroy all UMAS when saving, then regenerate after save - Saving large amounts of memory in the scene file");
            DrawBoolProperty("postProcessAllAssets", "Post Process All Assets", "If true, UMA will post process all assets in the project on startup");
            DrawBoolProperty("autoRepairIndex", "Index Auto Repair", "If true, UMA will attempt to repair any missing items in the UMA Global Library");
            DrawBoolProperty("showIndexedTypes", "Show Indexed Types", "If true, UMA will show all indexed types in the project window");
            DrawBoolProperty("showUnindexedTypes", "Show Unindexed Types", "If true, UMA will show all unindexed types in the project window");

            DrawBoolProperty("showWelcomeToUMA", "Show Welcome Window", "If true, UMA will show the welcome window when the project is loaded");

            DrawObjectProperty("characterPrefab", "Character Prefab", "The default character prefab used by UMA", typeof(GameObject));
            DrawObjectProperty("generatorPrefab", "Generator Prefab", "The default generator prefab used by UMA", typeof(GameObject));
            DrawObjectProperty("textureMerge", "Texture Merger", "The default texture merger used by UMA", typeof(TextureMerge));

            DrawStringProperty("DiscordInvite", "Discord Invite", "The default discord invite link for UMA");
            DrawStringProperty("DiscordURL", "Discord URL", "The default discord URL for UMA");
            DrawStringProperty("WikiURL", "Wiki URL", "The default wiki URL for UMA");
            DrawStringProperty("ForumURL", "Forum URL", "The default forum URL for UMA");
            DrawStringProperty("AssetStoreURL", "Asset Store URL", "The default asset store URL for UMA");
            DrawStringProperty("ShaderFolder", "Shader Folder", "The folder where the UMA shaders are located, relative to the Assets folder. Usually UMA/Core/ShaderPackages");

            EndVerticalPadded(10);

            GUILayout.Space(10);

            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Project Build Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Modifying these settings will change the UMA define symbols in the project settings, and force a recompile.", MessageType.Info);
            DrawBoolConfigToggle("use32bitBuffers", "Use 32bit Buffers", "If true, UMA will use 32bit buffers for all UMA data", DefineSymbol_32BitBuffers, defineSymbols);
            EditorGUILayout.HelpBox("Using the Burst compiler will speed up certain operations. But will require adding the following packages from the Package Manager: Burst, Jobs (Mathematics, Collections should be pulled in automatically)", MessageType.Warning, true);
            DrawBoolConfigToggle("useBurstCompiler", "Use Burst Compiler", "If true, UMA will use the Burst Compiler to speed up array math. Must install the jobs package first", DefineSymbol_BurstCompile, defineSymbols, true);
            DrawBoolConfigToggle("useAddressables", "Use Addressables", "If true, UMA will use the Addressables system for loading assets", DefineSymbol_Addressables, defineSymbols);
            DrawBoolConfigToggle("alwaysGetAddressables", "Always Get Addressables", "If true, UMA will always load items even if they bundles are not available in the editor. You should test with this off!", DefineSymbol_UMAAlwaysGetAddressableItems, defineSymbols);
            DrawBoolConfigToggle("enableGLTFExport", "Enable GLTF Export", "If true, UMA will enable the GLTF export feature", DefineSymbol_GLTFExport, defineSymbols);
            EndVerticalPadded(10);

            GUILayout.Space(10);

            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("UMA Addressables Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These settings are only used if 'Use Addressables' is enabled", MessageType.Info);
            bool useAddressables = m_CustomSettings.FindProperty("useAddressables").boolValue;
            GUI.enabled = useAddressables;
            DrawPropertiesIncluding(m_CustomSettings, new string[] { "addrUseSharedGroup", "addrSharedGroupName", "addrDefaultLabel", "addStripMaterials", "addrIncludeRecipes", "addrIncludeOther" });
            GUI.enabled = true;
            EndVerticalPadded(10);

            m_CustomSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider()
        {
            return new UMASettingsProvider("Project/UMA", SettingsScope.Project);
        }
    }
}
#endif