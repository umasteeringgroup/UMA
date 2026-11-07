#if UNITY_EDITOR
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
        private string dots = "";
        public string BasePath => FindUMAFullPath();

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


        private void DrawBoolConfigToggle(string label, string tooltip, string defineSymbol, HashSet<string> defineSymbols)
        {

            EditorGUI.BeginChangeCheck();
            var boolValue = defineSymbols.Contains(defineSymbol);
            boolValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), boolValue);
            if (EditorGUI.EndChangeCheck())
            {
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
                Repaint();
                return;
            } 

            dots = "";

            var defineSymbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(CurrentNamedBuildTarget).Split(';'));

            EditorGUILayout.LabelField("UMA Version " + m_CustomSettings.FindProperty("UMAVersion").stringValue, EditorStyles.boldLabel);
            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These tags are used by UMA to identify different types of assets", MessageType.Info);

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
            EndVerticalPadded(10);

            GUILayout.Space(10);
			BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
			EditorGUILayout.LabelField("Mesh Generation", EditorStyles.boldLabel);

			DrawBoolProperty(
				"useMeshAPICombiner",
				"Use MeshAPI Combiner",
				"Use the MeshData API based combiner. When disabled, UMA uses the managed combiner."
			);

			


			EndVerticalPadded(10);
			GUILayout.Space(10);

			BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Editor Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These settings control the behavior of UMA in the editor", MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent("UMA Folder",
                    "Automatically detected UMA installation path."), UMAPathUtility.InstallAssetRoot);
            }
            DrawBoolProperty("cleanRegenOnSave", "Clean Regen On Save", "If true, UMA will destroy all UMAS when saving, then regenerate after save - Saving large amounts of memory in the scene file");
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

            EndVerticalPadded(10);

            GUILayout.Space(10);

            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Project Build Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Modifying these settings will change the UMA define symbols in the project settings, and force a recompile.", MessageType.Info);
            EditorGUILayout.HelpBox("Burst acceleration requires the Burst, Collections and Mathematics packages. Addressables support requires the Addressables package.", MessageType.Info);
            DrawBoolConfigToggle("Use Burst Compiler", "Enable Burst acceleration for supported UMA operations.", DefineSymbol_BurstCompile, defineSymbols);
            DrawBoolConfigToggle("Use Addressables", "Enable Addressables support for loading UMA assets.", DefineSymbol_Addressables, defineSymbols);
            DrawBoolConfigToggle("Always Get Addressables", "Allow editor Play Mode to load indexed Addressable assets directly from the Asset Database. Disable this when testing bundle loading.", DefineSymbol_UMAAlwaysGetAddressableItems, defineSymbols);
            EndVerticalPadded(10);

            GUILayout.Space(10);

            BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("UMA Addressables Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These settings are only used if 'Use Addressables' is enabled. Note: Stripping Textures *requires* that you are indexing Texture2D type!", MessageType.Info); 
            DrawPropertiesIncluding(m_CustomSettings, new string[] { "addrUseSharedGroup", "addrDefaultLabel", "addrStripMaterials", "addrStripTextures", "addrStripUVAttachedShaders", "addrIncludeRecipes", "addrIncludeOther"});
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
