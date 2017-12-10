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
//        public static Type[] TrackTypes = { typeof(SlotDataAsset), typeof(OverlayDataAsset), typeof(UMAWardrobeRecipe), typeof(UMATextRecipe), typeof(SharedColorTable) };
//        public static string[] TrackNames = { "Slot", "Overlay", "Wardrobe Item", "Text Recipe", "Color Table" };
        public static Dictionary<Type, string> FriendlyNames = new Dictionary<Type, string>();
        private static Texture2D icon;

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

            if (icon == null)
            {
                Debug.Log("Unable to load texture icon");
            }
            UMAAssetIndexer ai = UMAAssetIndexer.Instance;
            EditorApplication.projectWindowItemOnGUI += DrawItems;
        }



        private static void DrawItems(string guid, Rect selectionRect)
        {
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