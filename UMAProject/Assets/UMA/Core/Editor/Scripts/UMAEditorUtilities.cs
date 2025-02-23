using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UMA.CharacterSystem;
using UnityEditor.Animations;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Build;

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
        public const string umaDefaultTags = "Head,Hair,Torso,Legs,Feet,Hands,Smooshable,Unsmooshable";


		private const string umaDefaultLabelKey = "UMA_DEFAULTLABEL";
		private const string umaHotkeyWord = "UMA_HOTKEYS";


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
                    {
                        Debug.LogWarning("Unable to load texture icon");
                    }
                }
				showIndexedTypes = UMASettings.ShowIndexedTypes;
                showUnindexedTypes = UMASettings.ShowUnindexedTypes;

				if (showIndexedTypes)
				{
                    UMAAssetIndexer ai = UMAAssetIndexer.Instance;
					if (ai != null)
					{
						EditorApplication.projectWindowItemOnGUI += DrawItems;
					}
				}
				ranOnce = true;
				return;
			}
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

        public static string[] GetDefaultTags()
        {
			var settings = UMASettings.GetOrCreateSettings();
			return settings.tagLookupValues;
        }

		public static string[] GetDefaultBaseTags()
		{
			string[] strings = GetDefaultTags();
			string[] baseTags = new string[strings.Length];
			// trim everything past the last slash
			for (int i = 0; i < strings.Length; i++)
			{
				string[] split = strings[i].Split('/');
				if (split.Length > 1)
				{
					baseTags[i] = split[split.Length-1];
                }
                else
				{
					baseTags[i] = strings[i];
                }
            }
			return baseTags;
		}

		public static string GetDefaultAddressableLabel()
		{
			return UMASettings.AddrDefaultLabel;
		}

		public static bool LeanMeanSceneFiles()
		{
			return UMASettings.CleanRegenOnSave;
		}

		public static bool UseSharedGroupConfigured()
		{
			return UMASettings.AddrUseSharedGroup;
        }

		public static bool StripUMAMaterials()
        {
			return UMASettings.AddStripMaterials;
        }
		public static bool PostProcessAllAssets()
		{
			return UMASettings.PostProcessAllAssets;
        }

		public static bool IsAddressable()
		{
#if UMA_ALWAYSADDRESSABLE
            return true;
#else
			return UMASettings.UseAddressables;
#endif
		}

		public static bool IsAutoRepairIndex()
		{
			return UMASettings.AutoRepairIndex;
        }

		public static bool IsAsmdef(HashSet<string> defineSymbols, string Symbol)
        {
			return (defineSymbols.Contains(Symbol));
		}


        private static void DrawItems(string guid, Rect selectionRect)
        {
            if (!showIndexedTypes)
            {
                return;
            }

            if (UMAAssetIndexer.Instance == null)
            {
                return;
            }

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
            {
                allDefines.Remove(umaHotkeyWord);
            }
            else
            {
                allDefines.Add(umaHotkeyWord);
            }

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
                {
                    doCreate = true;
                }
            }
			else
            {
                doCreate = true;
            }

            if (doCreate)
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
            {
                throw new ArgumentException("threshold");
            }

            if (count == 0)
            {
                count = array.Length;
            }

            int current_size = 0, keep_looping_up_to = Math.Min(count, threshold);

			while (current_size < keep_looping_up_to)
            {
                array[current_size++] = value;
            }

            for (int at_least_half = (count + 1) >> 1; current_size < at_least_half; current_size <<= 1)
            {
                Array.Copy(array, 0, array, current_size, current_size);
            }

            Array.Copy(array, 0, array, current_size, count - current_size);
		}
		public static System.Type[] GetAllDerivedTypes(this System.AppDomain aAppDomain, System.Type aType)
        {
            var result = new List<System.Type>();
            var assemblies = aAppDomain.GetAssemblies();
			
            foreach (var assembly in assemblies)
            {
				if (assembly.IsDynamic) { continue; }

                var types = assembly.GetExportedTypes();
                foreach (var type in types)
                {
                    if (type.IsSubclassOf(aType))
                    {
                        result.Add(type);
                    }
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