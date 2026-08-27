#if UNITY_EDITOR
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
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
            return UMAPathUtility.InstallAssetRoot;
        }

        protected internal static bool ContainsPropertyName(string[] propertyNames, string propertyName)
        {
            if (propertyNames == null)
            {
                return false;
            }

            for (int propertyIndex = 0; propertyIndex < propertyNames.Length; propertyIndex++)
            {
                if (propertyNames[propertyIndex] == propertyName)
                {
                    return true;
                }
            }

            return false;
        }

        public static string FindUMAFullPath()
        {
            return UMAPathUtility.ResolveAbsolutePath(UMAPathUtility.InstallAssetRoot);
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
                if (!ContainsPropertyName(propertyToExclude, iterator.name))
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
                if (ContainsPropertyName(propertyToInclude, iterator.name))
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
                // Package content may be immutable. Burst is controlled solely by the
                // project define and package dependency; never rewrite UMA_Core.asmdef.
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

        public void DrawBoolProperty(string propertyName, string label, string tooltip, Action<bool> onChanged = null)
        {
            SerializedProperty prop = m_CustomSettings.FindProperty(propertyName);
            EditorGUI.BeginChangeCheck();
            prop.boolValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), prop.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                m_CustomSettings.ApplyModifiedProperties();
                onChanged?.Invoke(prop.boolValue);
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

        private void DrawFolderSetting(SerializedProperty prop, string label, string tooltip, bool mustStartWithAssets,
            Action onChanged = null, bool allowMissing = false, string emptyFallback = null)
        {
            string current = prop.stringValue;
            // Validation
            string relPath = current;
            if (string.IsNullOrEmpty(relPath))
            {
                relPath = !string.IsNullOrEmpty(emptyFallback) ? emptyFallback :
                    label.Contains("UMA") ? UMAPathUtility.InstallAssetRoot :
                    UMAPathUtility.ShaderPackagesRelativePath;
            }

            bool isAssetPath = relPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                               relPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
            if (!isAssetPath)
            {
                if (mustStartWithAssets)
                {
                    relPath = Path.Combine("Assets", relPath).Replace('\\', '/');
                }
                else
                {
                    relPath = relPath.StartsWith("SRP/",
                        StringComparison.OrdinalIgnoreCase)
                        ? UMAPathUtility.ResolveSrpAssetPath(
                            relPath.Substring("SRP/".Length))
                        : UMAPathUtility.ResolveInstallAssetPath(relPath);
                }
            }

            bool exists = AssetDatabase.IsValidFolder(relPath);
            if (!exists && !allowMissing)
            {
                EditorGUILayout.HelpBox($"{label} path '{prop.stringValue}' does not exist. Please set a valid folder.", MessageType.Error);
            }
            else if (!exists)
            {
                EditorGUILayout.HelpBox($"{label} path '{prop.stringValue}' will be created when recovery is first saved.",
                    MessageType.Info);
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
                    if (!attempt.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                        !attempt.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                        attempt = mustStartWithAssets
                            ? Path.Combine("Assets", attempt).Replace('\\', '/')
                            : attempt.StartsWith("SRP/",
                                StringComparison.OrdinalIgnoreCase)
                                ? UMAPathUtility.ResolveSrpAssetPath(
                                    attempt.Substring("SRP/".Length))
                                : UMAPathUtility.ResolveInstallAssetPath(attempt);
                    string fullAttempt = UMAPathUtility.ResolveAbsolutePath(attempt);
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
                        bool valid = rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                                     (!mustStartWithAssets && rel.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase));
                        if (valid) newVal = rel;
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
            UMASettings currentSettings = UMASettings.GetOrCreateSettings();
            if (currentSettings != null &&
                (m_CustomSettings == null ||
                 m_CustomSettings.targetObject != currentSettings))
            {
                m_CustomSettings = new SerializedObject(currentSettings);
            }
            else
            {
                m_CustomSettings?.UpdateIfRequiredOrScript();
            }

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

            string configuredIgnoreTag = m_CustomSettings.FindProperty("IgnoreTag").stringValue;
            string effectiveIgnoreTag = string.IsNullOrWhiteSpace(configuredIgnoreTag) ||
                string.Equals(configuredIgnoreTag, "Untagged", StringComparison.Ordinal)
                ? UMASettings.DefaultIgnoreTag
                : configuredIgnoreTag.Trim();
            if (Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, effectiveIgnoreTag) < 0)
            {
                EditorGUILayout.HelpBox(
                    $"Ignore Tag '{effectiveIgnoreTag}' is not defined in Project Settings > Tags and Layers. " +
                    "UMA will skip ignore-tag processing until the tag is added.",
                    MessageType.Error);
            }

            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These groups are used by UMA to identify slots with the same UV layout for decals", MessageType.Info);
            DrawPropertiesIncluding(m_CustomSettings, new string[] { "groupNames" });
            DrawPropertiesIncluding(m_CustomSettings, new string[] { "addDNAOnRaceChange" });
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
            SerializedProperty texturePaintRecoveryFolderProp =
                m_CustomSettings.FindProperty("texturePaintRecoveryFolder");
            if (umaFolderProp != null)
            {
                DrawFolderSetting(umaFolderProp, "UMA Folder",
                    "Resolved UMA installation path below Assets or Packages.", false,
                    () => { UMABasePath = ""; UMAPathUtility.InvalidateInstallPathCache(); });
            }
            if (shaderFolderProp != null)
            {
                DrawFolderSetting(shaderFolderProp, "Shader Folder",
                    "UMA-relative folder containing the packages used to refresh UMA shaders.",
                    false, null, false, UMAPathUtility.ShaderPackagesRelativePath);
            }
            if (texturePaintRecoveryFolderProp != null)
            {
                DrawFolderSetting(texturePaintRecoveryFolderProp, "Overlay Painter Recovery Folder",
                    "Folder below Assets for painter_recovery.asset and its data files. This folder can be ignored by source control.",
                    true, null, true, UMAPathUtility.OverlayPainterRecoveryRoot);
                string recoveryFolder = texturePaintRecoveryFolderProp.stringValue?.Replace('\\', '/').TrimEnd('/');
                string[] recoveryParts = string.IsNullOrEmpty(recoveryFolder)
                    ? Array.Empty<string>() : recoveryFolder.Split('/');
                bool belowAssets = !string.IsNullOrEmpty(recoveryFolder) &&
                    recoveryFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                    recoveryParts.Length > 1;
                for (int i = 0; belowAssets && i < recoveryParts.Length; i++)
                    if (string.IsNullOrWhiteSpace(recoveryParts[i]) || recoveryParts[i] == "." ||
                        recoveryParts[i] == "..")
                        belowAssets = false;
                if (!belowAssets)
                    EditorGUILayout.HelpBox("Overlay Painter Recovery Folder must be below Assets. " +
                        $"Overlay Painter will use {UMAPathUtility.OverlayPainterRecoveryRoot} until this value is corrected.",
                        MessageType.Error);
                else
                    EditorGUILayout.HelpBox("Recovery creates painter_recovery.asset and a sibling data folder here. " +
                        "Exclude this folder from source control if temporary recovery should remain local.",
                        MessageType.None);
            }

            DrawBoolProperty("cleanRegenOnSave", "Clean Regen On Save", "If true, UMA will destroy all UMAS when saving, then regenerate after save - Saving large amounts of memory in the scene file");
            DrawBoolProperty("postProcessAllAssets", "Post Process All Assets", "If true, UMA will post process all assets in the project on startup");
            DrawBoolProperty("autoRepairIndex", "Index Auto Repair", "If true, UMA will attempt to repair any missing items in the UMA Global Library");
            DrawBoolProperty(
                "showIndexedTypes",
                "Show Indexed Types",
                "If true, UMA will show all indexed types in the project window",
                _ => UMASettings.NotifyProjectWindowTypeDisplayChanged());
            DrawBoolProperty(
                "showUnindexedTypes",
                "Show Unindexed Types",
                "If true, UMA will show all unindexed types in the project window",
                _ => UMASettings.NotifyProjectWindowTypeDisplayChanged());

            DrawBoolProperty("ignoreBackupFolders", "Ignore Backup Folders", "If true, UMA will ignore any folders named 'Backup' when indexing assets. This can help prevent issues with automatic backup systems.");
            DrawBoolProperty("showWelcomeToUMA", "Show Welcome Window", "If true, UMA will show the welcome window when the project is loaded");
            DrawBoolProperty(
                "showToolbar",
                "Show UMA Toolbar",
                "If true, UMA will show the UMA Toolbar overlay in the Scene view.",
                UMASettings.NotifyToolbarVisibilityChanged);



            DrawObjectProperty("characterPrefab", "Character Prefab", "The default character prefab used by UMA", typeof(GameObject));
            DrawObjectProperty("generatorPrefab", "Generator Prefab", "The default generator prefab used by UMA", typeof(GameObject));
            DrawObjectProperty("textureMerge", "Texture Merger", "The default texture merger used by UMA", typeof(TextureMerge));

            DrawStringProperty("DiscordInvite", "Discord Invite", "The default discord invite link for UMA");
            DrawStringProperty("DiscordURL", "Discord URL", "The default discord URL for UMA");
            DrawStringProperty("WikiURL", "Wiki URL", "The default wiki URL for UMA");
            DrawStringProperty("ForumURL", "Forum URL", "The default forum URL for UMA");
            DrawStringProperty("AssetStoreURL", "Asset Store URL", "The default asset store URL for UMA");
            DrawStringProperty("ShaderFolder", "Shader Folder",
                "The UMA-relative shader package folder. Usually SRP/ShaderPackages");

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
