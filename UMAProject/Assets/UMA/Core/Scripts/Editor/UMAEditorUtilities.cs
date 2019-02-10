using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UMA.CharacterSystem;
using UnityEditor.Animations;
using System.IO;
using System.Text.RegularExpressions;

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
		private const string umaHotkeyWord = "UMA_HOTKEYS";

        static UMAEditorUtilities()
        {
			EditorApplication.update += RunCallbacks;
		}

		private static void RunCallbacks()
		{
			if (!ranOnce)
			{
				FriendlyNames.Add(typeof(SlotDataAsset), "Slot");
				FriendlyNames.Add(typeof(OverlayDataAsset), "Overlay");
				FriendlyNames.Add(typeof(RaceData), "Race");
				FriendlyNames.Add(typeof(UMATextRecipe), "Text Recipe");
				FriendlyNames.Add(typeof(UMAWardrobeRecipe), "Wardrobe Recipe");
				FriendlyNames.Add(typeof(UMAWardrobeCollection), "Wardrobe Collection");
				FriendlyNames.Add(typeof(AnimatorController), "Animator Controller");
				FriendlyNames.Add(typeof(TextAsset), "Text");
				FriendlyNames.Add(typeof(DynamicUMADnaAsset), "Dynamic DNA");
				icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UMA/InternalDataStore/UmaIndex.png");
				showIndexedTypes = EditorPrefs.GetBool("BoolUMAShowTypes", true);
				showUnindexedTypes = EditorPrefs.GetBool("BoolUMAShowUnindexed", false);

				if (icon == null)
				{
					Debug.Log("Unable to load texture icon");
				}
				UMAAssetIndexer ai = UMAAssetIndexer.Instance;
				if (showIndexedTypes)
				{
					EditorApplication.projectWindowItemOnGUI += DrawItems;
				}
				ranOnce = true;
				return;
			}
		}

		[PreferenceItem("UMA")]
        public static void PreferencesGUI()
        {
            // Preferences GUI
            bool newshowIndexedTypes = EditorGUILayout.Toggle("Show Indexed Types", showIndexedTypes);
            showUnindexedTypes = EditorGUILayout.Toggle("Also Show Unindexed Types", showUnindexedTypes);

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


            if (GUI.changed)
            {
                EditorApplication.RepaintProjectWindow();
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

#if UMA_HOTKEYS
		[MenuItem("UMA/Toggle Hotkeys (enabled)")]
#else
		[MenuItem("UMA/Toggle Hotkeys (disabled)")]
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
		public static void CreateRecipe(string path, SlotDataAsset sd, OverlayDataAsset od, string slotName, bool addToGlobalLibrary)
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
			asset.Save(recipe, UMAContext.Instance);
			asset.DisplayValue = slotName;

			// Write the asset to disk
			AssetDatabase.CreateAsset(asset, path);
			AssetDatabase.SaveAssets();
			if (addToGlobalLibrary)
			{
				// Add it to the global libary
				UMAAssetIndexer.Instance.EvilAddAsset(typeof(CharacterSystem.UMAWardrobeRecipe), asset);
			}
			// Inform the asset database a file has changes
			AssetDatabase.Refresh();
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