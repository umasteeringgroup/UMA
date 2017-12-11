using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UMA;
using UMA.CharacterSystem;
using UnityEditor.Animations;

namespace Tenebrous.EditorEnhancements
{
    [InitializeOnLoad]
    public static class UMAEditorUtilities
    {
        public static Dictionary<Type, string> FriendlyNames = new Dictionary<Type, string>();
        private static Texture2D icon;
        private static bool showIndexedTypes = false;
        private static bool showUnindexedTypes = true;

        static UMAEditorUtilities()
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
            showUnindexedTypes = EditorPrefs.GetBool("BoolUMAShowUnindexed", true);

            if (icon == null)
            {
                Debug.Log("Unable to load texture icon");
            }
            UMAAssetIndexer ai = UMAAssetIndexer.Instance;
            if (showIndexedTypes)
            {
                EditorApplication.projectWindowItemOnGUI += DrawItems;
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
            if (selectionRect.height <= 22)
            {
                GUIStyle labelstyle = EditorStyles.miniLabel;

                Rect newRect = selectionRect;
                Vector2 labelSize = labelstyle.CalcSize(new GUIContent(FriendlyType));
                newRect.width += selectionRect.x;
                newRect.x = 0;
                newRect.x = newRect.width - labelSize.x;
                newRect.x -= 4;
                newRect.width = labelSize.x + 1;
                newRect.x -= 16;
                GUI.Label(newRect, FriendlyType, labelstyle);
                newRect.x = newRect.x + newRect.width;
                newRect.width = 16;
                GUI.DrawTexture(newRect, icon);
            }
        }
        private static void ShowAsset(Rect selectionRect, string FriendlyType)
        {
            if (selectionRect.height <= 22)
            {
                GUIStyle labelstyle = EditorStyles.miniLabel;

                Rect newRect = selectionRect;
                Vector2 labelSize = labelstyle.CalcSize(new GUIContent(FriendlyType));
                newRect.width += selectionRect.x;
                newRect.x = 0;
                newRect.x = newRect.width - labelSize.x;
                newRect.x -= 4;
                newRect.width = labelSize.x + 1;
                GUI.Label(newRect, FriendlyType, labelstyle);
            }
        }
    }
}