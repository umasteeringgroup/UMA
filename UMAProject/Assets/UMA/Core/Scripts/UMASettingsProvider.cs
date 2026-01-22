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
using System;

namespace UMA
{ 

    public class UMASettingsProvider : SettingsProvider
    {
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
        public const string ConfigToggle_StripUmaTextures = "UMA_SHAREDGROUP_STRIPUMATEXTURES";
        public const string ConfigToggle_PostProcessAllAssets = "UMA_POSTPROCESS_ALL_ASSETS";
        public const string ConfigToggle_IndexAutoRepair = "UMA_INDEX_AUTOREPAIR";

        private string dots = "";
        private string UMABasePath = "";
        public string BasePath
        {
            get
            {
                if (string.IsNullOrEmpty(UMABasePath))
                {
                    UMABasePath = FindUMAFullPath();
                }
                return UMABasePath;
            }
        }

        public static string FindUMAFolder()
        {
            string basePath = FindUMAFullPath();
            // return the path relative to the Assets folder
            string folder = basePath.Replace(Application.dataPath, "Assets");
            return folder;
        }

        public static string FindUMAFullPath()
        {
            // Use the configured UMAFolder setting if available
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            try
            {
                var settings = UMASettings.GetOrCreateSettings();
                if (settings != null)
                {
                    string configured = settings.UMAFolder;
                    if (string.IsNullOrEmpty(configured))
                    {
                        configured = "Assets/UMA"; // default
                    }

                    // Normalize path to start with Assets/
                    if (!configured.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                    {
                        configured = Path.Combine("Assets", configured).Replace('\\', '/');
                    }

                    string full = Path.Combine(projectRoot, configured).Replace('\\', '/');
                    if (Directory.Exists(full))
                    {
                        return full;
                    }
                }
            }
            catch
            {
                // Swallow any errors (e.g. during domain reload) and fall back to search
            }

            // Fallback search (original behaviour) if configured path missing
            string folder = "UMA";
            string[] folders = AssetDatabase.FindAssets("UMA t:Folder");
            if (folders != null && folders.Length > 0)
            {
                foreach (string guid in folders)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        // convert to full path
                        string fullPath = Path.Combine(projectRoot, path).Replace('\\', '/');
                        if (Directory.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                }
            }
            // default full path
            return Path.Combine(Application.dataPath, folder).Replace('\\', '/');
        }

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

            EditorGUI.BeginChangeCheck();
            var boolValue = defineSymbols.Contains(defineSymbol);
            boolValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                Debug.Log($"{label} changed to {boolValue} burst = {burst}");
                if (burst)
                {
                    if (boolValue)
                    {
                        string sourceFile = Path.Combine(BasePath,  "core", "uma_core_burst.dat");
                        string destFile = Path.Combine(BasePath,"core", "uma_core.asmdef");
                        Debug.Log($"Burst changed to {boolValue}-Copying from {sourceFile} to {destFile}");
                        File.Copy(sourceFile, destFile, true);
                        AssetDatabase.Refresh();
                        Debug.Log("File copied");
                    }
                    else
                    {
                        string sourceFile = Path.Combine(BasePath,"core", "uma_core_noburst.dat");
                        string destFile = Path.Combine(BasePath,"core", "uma_core.asmdef");
                        Debug.Log($"Burst changed to {boolValue}-Copying from {sourceFile} to {destFile}");
                        File.Copy(sourceFile, destFile, true);
                        AssetDatabase.Refresh();
                        Debug.Log("File copied");
                    }
                }
                m_CustomSettings.ApplyModifiedProperties();
                if (boolValue)
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

        private void ForceRepaint()
        {
            // Schedule a repaint of the focused window to update UI immediately after folder selection
            EditorApplication.delayCall += () =>
            {
                var wnd = EditorWindow.focusedWindow;
                if (wnd != null) wnd.Repaint();
            };
        }

        private void DrawFolderSetting(SerializedProperty prop, string label, string tooltip, bool mustStartWithAssets, Action onChanged = null)
        {
            string current = prop.stringValue;
            // Validation
            string relPath = current;
            if (string.IsNullOrEmpty(relPath))
            {
                relPath = label.Contains("UMA") ? "Assets/UMA" : "UMA/Core/ShaderPackages";
            }

            if (!relPath.StartsWith("Assets"))
            {
                if (mustStartWithAssets)
                {
                    relPath = Path.Combine("Assets", relPath).Replace('\\', '/');
                }
                else
                {
                    // For shader folder we allow omission of Assets prefix but add for validation
                    relPath = Path.Combine("Assets", relPath).Replace('\\', '/');
                }
            }

            bool exists = AssetDatabase.IsValidFolder(relPath);
            if (!exists)
            {
                EditorGUILayout.HelpBox($"{label} path '{prop.stringValue}' does not exist. Please set a valid folder.", MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newVal = EditorGUILayout.TextField(new GUIContent(label, tooltip), prop.stringValue);
            if (GUILayout.Button("Select", GUILayout.MaxWidth(60)))
            {
                string startPath = Application.dataPath;
                if (!string.IsNullOrEmpty(prop.stringValue))
                {
                    string attempt = prop.stringValue;
                    if (!attempt.StartsWith("Assets")) attempt = Path.Combine("Assets", attempt).Replace('\\', '/');
                    string fullAttempt = Path.Combine(Directory.GetParent(Application.dataPath).FullName, attempt).Replace('\\', '/');
                    if (Directory.Exists(fullAttempt)) startPath = fullAttempt;
                }
                string folderPicked = EditorUtility.OpenFolderPanel($"Select {label}", startPath, "");
                if (!string.IsNullOrEmpty(folderPicked))
                {
                    folderPicked = folderPicked.Replace('\\', '/');
                    // Convert to relative under project
                    string proj = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
                    if (folderPicked.StartsWith(proj))
                    {
                        string rel = folderPicked.Substring(proj.Length + 1); // remove trailing '/'
                        if (!rel.StartsWith("Assets")) rel = Path.Combine("Assets", rel).Replace('\\', '/');
                        newVal = rel;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                prop.stringValue = newVal;
                m_CustomSettings.ApplyModifiedProperties();
                onChanged?.Invoke();
                ForceRepaint();
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
                EditorGUILayout.LabelField(" Adding support for selected options... Compile in progress  " + dots);
                System.Threading.Thread.Sleep(100);
                Repaint();
                return;
            } 

            dots = "";

            var defineSymbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(CurrentNamedBuildTarget).Split(';'));

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
			EditorGUILayout.LabelField("Debug Settings", EditorStyles.boldLabel);

			DrawBoolProperty(
				"useMeshAPICombiner",
				"Use MeshAPI Combiner (Unity 2022.2+)",
				"Enable the MeshData API based combiner at runtime on Unity 2022.2+. When disabled or on older Unity versions, UMA uses the legacy combiner."
			);

			DrawBoolProperty("DebugMemoryUsage", "Debug Memory Usage", "If true, UMA will log memory usage information to the console");
			DrawBoolProperty("DisableDecalCallbacks","Disable Decal Callbacks", "If true, UMA will disable decal callbacks to improve performance when decals are not used.");
			


			EndVerticalPadded(10);
			GUILayout.Space(10);

			BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Editor Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These settings control the behavior of UMA in the editor", MessageType.Info);

            // Folder settings (UMAFolder & ShaderFolder) directly after the help box as requested
            SerializedProperty umaFolderProp = m_CustomSettings.FindProperty("UMAFolder");
            SerializedProperty shaderFolderProp = m_CustomSettings.FindProperty("ShaderFolder");
            if (umaFolderProp != null)
            {
                DrawFolderSetting(umaFolderProp, "UMA Folder", "The UMA folder, relative to the Assets folder.", true, () => { UMABasePath = ""; });
            }
            if (shaderFolderProp != null)
            {
                DrawFolderSetting(shaderFolderProp, "Shader Folder", "The folder where the UMA shaders are located, relative to the Assets folder.", false, null);
            }

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
            EditorGUILayout.HelpBox("Using the Burst compiler will speed up certain operations. But will require adding the following packages from the Package Manager: Collections, Jobs (Mathematics, Burst should be pulled in automatically. If not, please add these packages first.)", MessageType.Warning, true);
            DrawBoolConfigToggle("useBurstCompiler", "Use Burst Compiler", "If true, UMA will use the Burst Compiler to speed up array math. Must install the appropriate packages first", DefineSymbol_BurstCompile, defineSymbols, true);
            DrawBoolConfigToggle("useAddressables", "Use Addressables", "If true, UMA will use the Addressables system for loading assets", DefineSymbol_Addressables, defineSymbols);
            DrawBoolConfigToggle("alwaysGetAddressables", "Always Get Addressables", "If true, UMA will always load items even if they bundles are not available in the editor. You should test with this off!", DefineSymbol_UMAAlwaysGetAddressableItems, defineSymbols);
            DrawBoolConfigToggle("enableGLTFExport", "Enable GLTF Export", "If true, UMA will enable the GLTF export feature", DefineSymbol_GLTFExport, defineSymbols);
            EndVerticalPadded(10);

            GUILayout.Space(10);

            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("UMA Addressables Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These settings are only used if 'Use Addressables' is enabled. Note: Stripping Textures *requires* that you are indexing Texture2D type!", MessageType.Info); 
            DrawPropertiesIncluding(m_CustomSettings, new string[] { "addrUseSharedGroup", "addrSharedGroupName", "addrDefaultLabel", "addStripMaterials", "addrStripTextures", "addrStripUVAttachedShaders", "addrIncludeRecipes", "addrIncludeOther"});
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