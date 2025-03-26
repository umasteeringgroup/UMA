using System.Collections.Generic;
using UMA;
using UnityEditor;
using UnityEngine;
using System.Linq;
using UMA.Editors;

namespace UMA
{
    public class AssetIndexerFilterEditor : EditorWindow
    {
        [MenuItem("UMA/Global Library Filters", priority = 99)]
        public static AssetIndexerFilterEditor GetWindow()
        {
            var window = GetWindow<AssetIndexerFilterEditor>();

            Texture icon = AssetDatabase.LoadAssetAtPath<Texture>("Assets/UMA/InternalDataStore/UMA32.png");
            window.titleContent = new GUIContent(UmaAboutWindow.umaVersion + " Global Library Fiilters", icon);
            window.minSize = new Vector2(800, 420);
            window.maxSize = new Vector2(800, 420);

            window.Focus();
            return window;
        }

        private string[] typeStrings = new string[0];
        private int currentPopupType = 0;
        private string currentFilter = "<Enter Filter Here>";
        private Vector2 currentFocus = Vector2.zero;

        private void OnEnable()
        {
            var keys = UMAAssetIndexer.TypeFromString.Keys;

            List<string> typekeys = new List<string>(keys.Distinct());

            typekeys.Remove("AnimatorController");
            typekeys.Remove("AnimatorOverrideController");

            typeStrings = typekeys.ToArray();
        }


        private void OnGUI()
        {
            // Display instructions.
            GUILayout.Space(8f);
            EditorGUILayout.HelpBox("Select type and enter a filter below. If any filters are added for a type, only paths that contain the filter strings will be used to search for assets of that type.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            currentPopupType = EditorGUILayout.Popup(currentPopupType, typeStrings, GUILayout.Width(100));
            currentFilter = EditorGUILayout.TextField(currentFilter, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string newFilter = EditorUtility.OpenFolderPanel("Browse for folder", currentFilter, "");
                var index = newFilter.ToLower().IndexOf("/assets/");
                if (index > 0)
                {
                    currentFilter = newFilter.Substring(index + 1);
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "You can only select folders in your current projects asset folder", "OK");
                }
            }

            if (GUILayout.Button("Add", GUILayout.Width(80)))
            {
                string typeName = typeStrings[currentPopupType];
                UMAAssetIndexer.Instance.AddSearchFolder(typeName, currentFilter);
                UMAAssetIndexer.Instance.ForceSave();
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUIHelper.BeginVerticalPadded();
            currentFocus = EditorGUILayout.BeginScrollView(currentFocus, GUILayout.Width(position.width - 20), GUILayout.Height(position.height - 120)); // Scroll //

            string removeKey = "";
            string removePath = "";
            foreach (var filter in UMAAssetIndexer.Instance.TypeFolderSearch)
            {
                foreach (string s in filter.Value)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(filter.Key, EditorStyles.textField, GUILayout.Width(100));
                    EditorGUILayout.LabelField(s, EditorStyles.textField, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Remove", GUILayout.Width(80)))
                    {
                        removeKey = filter.Key;
                        removePath = s;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
            GUIHelper.EndVerticalPadded();
            if (!string.IsNullOrEmpty(removeKey))
            {
                UMAAssetIndexer.Instance.RemoveSearchFolder(removeKey, removePath);
                UMAAssetIndexer.Instance.ForceSave();
            }
        }
    }
}