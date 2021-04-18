using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UMA.CharacterSystem;
using UnityEditor.Animations;
using System.IO;
using System.Text.RegularExpressions;
using UMA.PoseTools;

namespace UMA
{
    [InitializeOnLoad]
    public static class UMAEditorUtilities
    {
        public static Dictionary<Type, string> FriendlyNames = new Dictionary<Type, string>();
        private static Texture2D icon;
		private static bool ranOnce = false;
        private static bool showIndexedTypes = false;
        private static bool showUnindexedTypes = true;
		public  static string umaDefaultLabel = "UMA_Default";

		private const string umaDefaultLabelKey = "UMA_DEFAULTLABEL";
		private const string umaHotkeyWord = "UMA_HOTKEYS";
		private const string umaLocation = "RelativeUMA";
		private const string DefineSymbol_32BitBuffers = "UMA_32BITBUFFERS";
		private const string DefineSymbol_Addressables = "UMA_ADDRESSABLES";
		//private const string DefineSymbol_AsmDef = "UMA_ASMDEF";
		public const string ConfigToggle_LeanMeanSceneFiles = "UMA_CLEANUP_GENERATED_DATA_ON_SAVE";
		public const string ConfigToggle_UseSharedGroup = "UMA_ADDRESSABLES_USE_SHARED_GROUP";
		public const string ConfigToggle_ArchiveGroups = "UMA_ADDRESSABLES_ARCHIVE_ASSETBUNDLE_GROUPS";

		public const string ConfigToggle_AddCollectionLabels = "UMA_SHAREDGROUP_ADDCOLLECTIONLABELS";
		public const string ConfigToggle_IncludeRecipes = "UMA_SHAREDGROUP_INCLUDERECIPES";
		public const string ConfigToggle_IncludeOther = "UMA_SHAREDGROUP_INCLUDEOTHERINDEXED";
		public const string ConfigToggle_StripUmaMaterials = "UMA_SHAREDGROUP_STRIPUMAMATERIALS";
		public const string ConfigToggle_PostProcessAllAssets = "UMA_POSTPROCESS_ALL_ASSETS";
		private static string DNALocation = "UMA/";

        static UMAEditorUtilities()
        {
			EditorApplication.update += RunCallbacks;
		}

		private static void RunCallbacks()
		{
			if (!ranOnce)
			{
				FriendlyNames = new Dictionary<Type, string>();
				FriendlyNames.Add(typeof(SlotDataAsset), "Slot");
				FriendlyNames.Add(typeof(OverlayDataAsset), "Overlay");
				FriendlyNames.Add(typeof(RaceData), "Race");
				FriendlyNames.Add(typeof(UMATextRecipe), "Text Recipe");
				FriendlyNames.Add(typeof(UMAWardrobeRecipe), "Wardrobe Recipe");
				FriendlyNames.Add(typeof(UMAWardrobeCollection), "Wardrobe Collection");
				FriendlyNames.Add(typeof(AnimatorController), "Animator Controller");
				FriendlyNames.Add(typeof(TextAsset), "Text");
				FriendlyNames.Add(typeof(DynamicUMADnaAsset), "Dynamic DNA");

				string[] iconTextures = AssetDatabase.FindAssets("t:texture UmaIndex");
				if (iconTextures != null && iconTextures.Length > 0)
				{
					icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(iconTextures[0]));
				}
				else
				{
					if(Debug.isDebugBuild)
						Debug.LogWarning("Unable to load texture icon");
				}

				showIndexedTypes = EditorPrefs.GetBool("BoolUMAShowTypes", true);
				showUnindexedTypes = EditorPrefs.GetBool("BoolUMAShowUnindexed", false);

				UMAAssetIndexer ai = UMAAssetIndexer.Instance;
				if (showIndexedTypes && ai != null)
				{
					EditorApplication.projectWindowItemOnGUI += DrawItems;
				}
				ranOnce = true;
				return;
			}
		}





		private class MyPrefSettingsProvider : SettingsProvider
		{
			public MyPrefSettingsProvider(string path, SettingsScope scopes = SettingsScope.User)
			: base(path, scopes)
			{ }

			public override void OnGUI(string searchContext)
			{
				PreferencesGUI();
			}
		}

		[SettingsProvider]
		static SettingsProvider MyNewPrefCode()
		{
			return new MyPrefSettingsProvider("Preferences/UMA");
		}
 
		public static void PreferencesGUI()
        {
            // Preferences GUI
            bool newshowIndexedTypes = EditorGUILayout.Toggle("Show Indexed Types", showIndexedTypes);
            showUnindexedTypes = EditorGUILayout.Toggle("Show Unindexed Types", showUnindexedTypes);

			if (!PlayerPrefs.HasKey(umaLocation))
			{
				PlayerPrefs.SetString(umaLocation, DNALocation);
			}
			string umaloc = PlayerPrefs.GetString(umaLocation);
			string newUmaLoc = EditorGUILayout.DelayedTextField("Relative UMA Location", umaloc);
			if (umaloc != newUmaLoc)
			{
				PlayerPrefs.SetString(umaLocation, newUmaLoc);
			}

            // Save the preferences
            if (newshowIndexedTypes != showIndexedTypes)
            {
                showIndexedTypes = newshowIndexedTypes;
                EditorPrefs.SetBool("BoolUMAShowTypes", showIndexedTypes);
                if (showIndexedTypes)
                    EditorApplication.projectWindowItemOnGUI += DrawItems;
                else
                    EditorApplication.projectWindowItemOnGUI -= DrawItems;
            }

			ConfigToggle(ConfigToggle_PostProcessAllAssets, "Postprocess All Assets", "When assets in unity are moved, this will fix their paths in the index. This can be very slow.", false);
			ConfigToggle(ConfigToggle_LeanMeanSceneFiles, "Clean/Regen on Save", "When using edit-time UMA's the geometry is stored in scene files. Enabling this cleans them up before saving, and regenerates after saving, making your scene files squeaky clean.", true);

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.Space();
			GUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Toggling build options will cause a recompile", EditorStyles.miniLabel);
			GUILayout.EndHorizontal();

			EditorGUILayout.Space();

			bool prevAddressables = IsAddressable();

			var defineSymbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup).Split(';'));


			DefineSymbolToggle(defineSymbols, DefineSymbol_32BitBuffers, "Use 32bit buffers", "This allows meshes bigger than 64k vertices");
			DefineSymbolToggle(defineSymbols, DefineSymbol_Addressables, "Use Addressables", "This activates the code that loads from asset bundles using addressables.");

			/* bool prevuseAsmDef = IsAsmdef(defineSymbols, DefineSymbol_AsmDef);
			bool useAsmDef = DefineSymbolToggle(defineSymbols, DefineSymbol_AsmDef, "Use Asmdef", "This activates the internal ASMDEF for UMA.");
			if (prevuseAsmDef != useAsmDef)
			{
				if (useAsmDef)
				{
					EnableAsmdef();
				}
				else
				{
					DisableAsmDef();
				}
			}*/





#if !UMA_ADDRESSABLES

			GUILayout.Label("Addressables package MUST be installed before enabling this option!",EditorStyles.boldLabel);
#endif
			if (EditorGUI.EndChangeCheck())
			{
				PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join(";", defineSymbols));
			}


			GUI.enabled =
#if UMA_ADDRESSABLES
				true;
#else
				false;
#endif

#if UMA_ADDRESSABLES

			if (IsAddressable() == false && prevAddressables == true)
            {
				UMAAddressablesSupport.Instance.CleanupAddressables(false, true);
            }

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Addressables Options",EditorStyles.boldLabel);
			EditorGUILayout.Space();

			// ask here for the 
#else
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Addressables Options (Not Enabled)", EditorStyles.boldLabel);
			EditorGUILayout.Space();
#endif
			ConfigToggle(ConfigToggle_UseSharedGroup, "Use Shared Group", "Add all Addressables to the same Shared Group.", true);
			// This is managed by the addressables system
			//ConfigToggle(ConfigToggle_ArchiveGroups, "Archive Groups", "For now just copies the assetbundles into folders with the group name.", false);
			
			GUILayout.Label("Shared Group Generation");
			GUILayout.Label("By default, Slots and Overlays (with their Texture references) are included.",EditorStyles.miniLabel);

			string currentLabel = PlayerPrefs.GetString(umaDefaultLabelKey, umaDefaultLabel);
			string newUmaLabel = EditorGUILayout.DelayedTextField("Default UMA Label", currentLabel);
			if (newUmaLabel != umaDefaultLabel)
			{
				PlayerPrefs.SetString(umaDefaultLabelKey, newUmaLabel);
			}
			GUILayout.Label("Note: If you include recipes or other items, you will need to manually load them using LoadLabelList!", EditorStyles.miniLabel);
			ConfigToggle(ConfigToggle_StripUmaMaterials, "Strip UMAMaterials", "In some versions of Unity, using an SRP can cause each bundle to include the compiled shaders. This will stop that from happening.", false);
			ConfigToggle(ConfigToggle_IncludeRecipes, "Include Recipes", "Include recipes in shared group generation", false);
			ConfigToggle(ConfigToggle_IncludeOther, "Include all other types", "Include all other types in index in shared group generation", false);

			GUI.enabled = true;
            if (GUI.changed)
            {
                EditorApplication.RepaintProjectWindow();
            }
        }

		public static string GetDefaultAddressableLabel()
		{
			return PlayerPrefs.GetString(umaDefaultLabelKey,umaDefaultLabel);
		}

		public static bool LeanMeanSceneFiles()
		{
			return GetConfigValue(ConfigToggle_LeanMeanSceneFiles, true);
		}

		public static bool UseSharedGroupConfigured()
		{
			return GetConfigValue(ConfigToggle_UseSharedGroup, true);
		}

		public static bool StripUMAMaterials()
        {
			return GetConfigValue(ConfigToggle_StripUmaMaterials, false);
        }
		public static bool PostProcessAllAssets()
		{
			return GetConfigValue(ConfigToggle_PostProcessAllAssets, false);
		}

		public static bool IsAddressable()
		{
			return GetConfigValue(DefineSymbol_Addressables, false);
		}

		public static bool IsAsmdef(HashSet<string> defineSymbols, string Symbol)
        {
			return (defineSymbols.Contains(Symbol));
		}

		private static void ConfigToggle(string toggleId, string text, string tooltip, bool defaultValue)
		{
			var toggle = GetConfigValue(toggleId, defaultValue);
			if (EditorGUILayout.Toggle(new GUIContent(text, tooltip), toggle) != toggle)
			{
				SetConfigValue(toggleId, !toggle);
			}
		}

		private static void SetConfigValue(string toggleId, bool value)
		{
			//TODO: obviously not the right place!
			EditorPrefs.SetBool(toggleId, value);
		}

		public static bool GetConfigValue(string toggleId, bool defaultValue)
		{
			//TODO: obviously not the right place!
			return EditorPrefs.GetBool(toggleId, defaultValue);
		}

		private static bool DefineSymbolToggle(HashSet<string> defineSymbols, string defineSymbol, string text, string tooltip)
		{
#if UMA_ALWAYSADDRESSABLE
			if(defineSymbol == DefineSymbol_Addressables) {
				if(!defineSymbols.Contains(defineSymbol)) {
					defineSymbols.Add(defineSymbol);
				}
				EditorGUILayout.Toggle(new GUIContent(text, tooltip), true);
				return true;
			}
#endif
			if (EditorGUILayout.Toggle(new GUIContent(text, tooltip), defineSymbols.Contains(defineSymbol)))
			{
				defineSymbols.Add(defineSymbol);
				return true;
			}
			else
			{
				defineSymbols.Remove(defineSymbol);
				return false;
			}
		}

        private static void DrawItems(string guid, Rect selectionRect)
        {
            if (!showIndexedTypes) return;
            if (UMAAssetIndexer.Instance == null) return;

            AssetItem ai = UMAAssetIndexer.Instance.FromGuid(guid);
            if (ai != null)
            {
                if (FriendlyNames.ContainsKey(ai._Type))
                {
                    string FriendlyType = FriendlyNames[ai._Type];
                    // Draw the friendly type
                    ShowAsset(selectionRect, FriendlyType, icon);
                }
            }
            else
            {
                if (showUnindexedTypes == false)
                {
                    return;
                }
                if (String.IsNullOrEmpty(guid))
                {
                    return;
                }
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(path))
                {
                    Debug.Log("Unable to get path for asset: " + guid);
                    return;
                }
                UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(path);
                if (o == null)
                {
                    Debug.Log("Unable to get asset: " + path);
                    return;
                }
                Type t = o.GetType();
                if (FriendlyNames.ContainsKey(t))
                {
                    string FriendlyType = FriendlyNames[t];
                    ShowAsset(selectionRect, FriendlyType);
                }
            }
        }

        private static void ShowAsset(Rect selectionRect, string FriendlyType, Texture2D icon)
        {
            if (selectionRect.height <= 22 && selectionRect.width > 200)
            {
                GUIStyle labelstyle = EditorStyles.miniLabel;
                Color col = EditorGUIUtility.isProSkin
                    ? (Color)new Color32(56, 56, 56, 255)
                    : (Color)new Color32(194, 194, 194, 255);

                Rect newRect = selectionRect;
                Vector2 labelSize = labelstyle.CalcSize(new GUIContent(FriendlyType));
                // Display Label
                newRect.x = ((newRect.width + selectionRect.x) - labelSize.x) - 20;
                newRect.width = labelSize.x + 1;
                EditorGUI.DrawRect(newRect, col);
                GUI.Label(newRect, FriendlyType, labelstyle);
                // Display Icon
                newRect.x = newRect.x + newRect.width;
                newRect.width = 16;
                GUI.DrawTexture(newRect, icon);
            }
        }

        private static void ShowAsset(Rect selectionRect, string FriendlyType)
        {
            if (selectionRect.height <= 22 && selectionRect.width > 200)
            {
                GUIStyle labelstyle = EditorStyles.miniLabel;
                Color col = EditorGUIUtility.isProSkin
                    ? (Color)new Color32(56, 56, 56, 255)
                    : (Color)new Color32(194, 194, 194, 255);

                Rect newRect = selectionRect;
                Vector2 labelSize = labelstyle.CalcSize(new GUIContent(FriendlyType));
                newRect.x = ((newRect.width + selectionRect.x) - labelSize.x) - 4;
                newRect.width = labelSize.x + 1;
                EditorGUI.DrawRect(newRect, col);
                GUI.Label(newRect, FriendlyType, labelstyle);
            }
        }

#if UNITY_2018_4_OR_NEWER || UNITY_2019_1_OR_NEWER
		public static void EnableAsmdef()
		{
			RenameFiles(".asmdefTemp", ".asmdef", "Asmdef files are in place.", "Unable to find asmdefTemp files. Have you already ran this?");
		}

		public static void DisableAsmDef()
		{
			RenameFiles(".asmdef", ".asmdefTemp", "Asmdef files are removed.", "Unable to find asmdef files. Have you already ran this?");
		}

		public static void RenameFiles(string oldpattern,string newpattern, string completeMessage, string notFoundMessage)
		{
			string assetPath = Path.Combine(Application.dataPath, "UMA");
			string[] files = Directory.GetFiles(assetPath, "*"+oldpattern, SearchOption.AllDirectories);

			if (files.Length == 0)
			{
				EditorUtility.DisplayDialog("Warning", notFoundMessage , "Guess so");
				return;
			}
			foreach (string s in files)
			{
				string newFile = s.Replace(oldpattern, newpattern);
				if (newFile == s)
                {
					// 
					newFile = s.ToLower().Replace(oldpattern.ToLower(), newpattern.ToLower());
                }
				File.Move(s, newFile);
			}
			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Complete",completeMessage , "OK");
		}
#endif


		[MenuItem("UMA/SRP/Convert to URP (LWRP)")]
		static void ConvertToURP()
		{
			if (EditorUtility.DisplayDialog("Convert?", "Convert UMA Materials from Standard to URP. You should run the Unity option to convert your project to URP/LWRP in addition to running this option. Continue?", "OK", "Cancel"))
			{
				if (ConvertUMAMaterials("_MainTex", "_BaseMap"))
				{
					EditorUtility.DisplayDialog("Convert",
						"UMAMaterials converted. You will need to run the unity URP (LWRP) conversion utility to convert your materials if you have not already done this.", "OK");
				}
				else
				{
					EditorUtility.DisplayDialog("Convert", "No UMAMaterials needed to be converted.", "OK");
				}

			}
		}

		[MenuItem("UMA/SRP/Convert to Standard from URP (LWRP)")]
		static void ConvertToStandard()
		{
			if (EditorUtility.DisplayDialog("Convert?", "Convert UMAMaterials to Standard from URP. You will need to manually fix the template materials. Continue?", "OK", "Cancel"))
			{
				if (ConvertUMAMaterials("_BaseMap", "_MainTex"))
				{
					EditorUtility.DisplayDialog("Convert", "UMAMaterials converted. You will need to manually fix the template materials by changing them to use the correct shaders if you modified them.", "OK");
				}
				else
				{
					EditorUtility.DisplayDialog("Convert", "No UMAMaterials needed to be converted.", "OK");
				}
			}
		}

		/// <summary>
		/// Convertes all UMAMaterial channel Material Property names if they match.
		/// </summary>
		/// <param name="From"></param>
		/// <param name="To"></param>
		/// <returns></returns>
		static bool ConvertUMAMaterials(string From, string To)
		{
			string[] guids = AssetDatabase.FindAssets("t:UMAMaterial");

			int dirtycount = 0;
			foreach (string guid in guids)
			{
				bool matModified = false;
				string path = AssetDatabase.GUIDToAssetPath(guid);
				UMAMaterial umat = AssetDatabase.LoadAssetAtPath<UMAMaterial>(path);
				if (umat.material.shader.name.ToLower().StartsWith("standard") || umat.material.shader.name.ToLower().Contains("lit"))
				{
					for (int i = 0; i < umat.channels.Length; i++)
					{
						if (umat.channels[i].materialPropertyName == From)
						{
							umat.channels[i].materialPropertyName = To;
							matModified = true;
						}
					}
				}
				if (umat.material.shader.name.ToLower().Contains("hair fade"))
                {
					umat.material.shader = Shader.Find("Universal Render Pipeline/Nature/SpeedTree8");
					if (umat.material.name.ToLower().Contains("single"))
                    {
						umat.material.SetInt("_TwoSided", 2);
                    }
					else
                    {
						umat.material.SetInt("_TwoSided", 0);
					}
					matModified = true;
                }
				if (matModified)
				{
					dirtycount++;
					EditorUtility.SetDirty(umat);
				}
			}
			if (dirtycount > 0)
			{
				AssetDatabase.SaveAssets();
				return true;
			}
			return false;
		}

#if UMA_HOTKEYS
		[MenuItem("UMA/Toggle Hotkeys (enabled)",priority =30)]
#else
		[MenuItem("UMA/Toggle Hotkeys (disabled)", priority = 30)]
#endif
		public static void ToggleUMAHotkeys()
		{
			string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup ( EditorUserBuildSettings.selectedBuildTargetGroup );
            List<string> allDefines = new List<string>();
            allDefines.AddRange(definesString.Split(';'));

			if (allDefines.Contains(umaHotkeyWord))
				allDefines.Remove(umaHotkeyWord);
			else
				allDefines.Add(umaHotkeyWord);

			PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join( ";", allDefines.ToArray()));
		}
      
		/// <summary>
		/// Create a Wardrobe Recipe from the slot (and optionally overlay)
		/// </summary>
		/// <param name="path"></param>
		/// <param name="sd"></param>
		/// <param name="od"></param>
		/// <param name="slotName"></param>
		/// <param name="addToGlobalLibrary"></param>
		public static UMAWardrobeRecipe CreateRecipe(string path, SlotDataAsset sd, OverlayDataAsset od, string slotName, bool addToGlobalLibrary)
		{
			// Generate an asset in memory
			UMAWardrobeRecipe asset = ScriptableObject.CreateInstance<CharacterSystem.UMAWardrobeRecipe>();
			UMAData.UMARecipe recipe = new UMAData.UMARecipe();
			recipe.ClearDna();
			SlotData mySlot = new SlotData(sd);
			if (od != null)
			{
				OverlayData myOverlay = new OverlayData(od);
				mySlot.AddOverlay(myOverlay);
			}
			recipe.SetSlot(0, mySlot);
			asset.Save(recipe, UMAContextBase.Instance);
			asset.DisplayValue = slotName;

			// Write the asset to disk
			AssetDatabase.CreateAsset(asset, path);
			AssetDatabase.SaveAssets();
			if (addToGlobalLibrary)
			{
				// Add it to the global libary
				UMAAssetIndexer.Instance.EvilAddAsset(typeof(CharacterSystem.UMAWardrobeRecipe), asset);
				EditorUtility.SetDirty(UMAAssetIndexer.Instance);
			}
			// Inform the asset database a file has changes
			AssetDatabase.Refresh();
			return asset;
		}

		[MenuItem("UMA/Create Wardrobe Recipe from selected slot and overlay")]
		public static void SaveAsRecipe()
		{
			SlotDataAsset sd = null;
			OverlayDataAsset od = null;

			foreach (UnityEngine.Object obj in Selection.objects)
			{
				// Make sure it's in the project, not the hierarchy.
				// Not sure how we would ever have Slots and Overlays in the hierarchy though.
				if (AssetDatabase.Contains(obj))
				{
					if (obj is SlotDataAsset)
					{
						sd = obj as SlotDataAsset;
					}
					if (obj is OverlayDataAsset)
					{
						od = obj as OverlayDataAsset;
					}
				}
			}

			if (sd == null)
			{
				EditorUtility.DisplayDialog("Notice", "A SlotDataAsset must be selected in the project view", "Got it");
				return;
			}

			string assetPath = AssetDatabase.GetAssetPath(sd.GetInstanceID());
			string path = Path.GetDirectoryName(assetPath);
			string AssetName = Path.GetFileNameWithoutExtension(assetPath);
			if (AssetName.ToLower().Contains("_slot"))
			{
				AssetName = Regex.Replace(AssetName, "_slot", "_Recipe", RegexOptions.IgnoreCase);
			}
			else
			{
				AssetName += "_Recipe";
			}
			assetPath = Path.Combine(path, AssetName + ".asset");

			bool doCreate = false;
			if (File.Exists(assetPath))
			{
				if (EditorUtility.DisplayDialog("File Already Exists!", "An asset at that location already exists! Overwrite it?", "Yes", "Cancel"))
					doCreate = true;
			}
			else
				doCreate = true;

			if(doCreate)
			{
				CreateRecipe(assetPath, sd, od, sd.name, true);
				Debug.Log("Recipe created at: " + assetPath);
			}
		}
	}

	public static class UMAExtensions
    {
		public static void Fill(this bool[] array, bool value, int count = 0, int threshold = 32)
		{
			if (threshold <= 0)
				throw new ArgumentException("threshold");

			if (count == 0) count = array.Length;

			int current_size = 0, keep_looping_up_to = Math.Min(count, threshold);

			while (current_size < keep_looping_up_to)
				array[current_size++] = value;

			for (int at_least_half = (count + 1) >> 1; current_size < at_least_half; current_size <<= 1)
				Array.Copy(array, 0, array, current_size, current_size);

			Array.Copy(array, 0, array, current_size, count - current_size);
		}
		public static System.Type[] GetAllDerivedTypes(this System.AppDomain aAppDomain, System.Type aType)
        {
            var result = new List<System.Type>();
            var assemblies = aAppDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.IsSubclassOf(aType))
                        result.Add(type);
                }
            }
            return result.ToArray();
        }

        public static Rect GetEditorMainWindowPos()
        {
            Resolution r = Screen.currentResolution;
            return new Rect(0, 0, r.width, r.height);
        }

        public static void CenterOnMainWin(this UnityEditor.EditorWindow aWin)
        {
            var main = GetEditorMainWindowPos();
            var pos = aWin.position;
            float w = (main.width - pos.width) * 0.5f;
            float h = (main.height - pos.height) * 0.5f;
            pos.x = main.x + w;
            pos.y = main.y + h;
            aWin.position = pos;
        }
    }
}