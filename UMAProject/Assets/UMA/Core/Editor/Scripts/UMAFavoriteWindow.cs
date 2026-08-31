using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{

    public class UMAFavoriteWindow : EditorWindow
    {
        public static UMAFavoriteWindow instance;
        private static List<UMAFavoriteList> favoritelists = new List<UMAFavoriteList>();
        private static bool initialSearchCompleted = false;
        private static Vector2 scrollPosition = Vector2.zero;

        [MenuItem("UMA/Asset Management/Favorites")]
        public static void ShowWindow()
        {
            RefreshFavoriteListCategories();
            var window = EditorWindow.GetWindow(typeof(UMAFavoriteWindow));
            window.titleContent.text = "UMA Favorites";
            instance = window as UMAFavoriteWindow;
        }

        public static void RefreshFavoriteListCategories()
        {
            initialSearchCompleted = true;
            string[] favoriteListGuids = AssetDatabase.FindAssets("t:UMAFavoriteList");
            var refreshedLists = new List<UMAFavoriteList>(favoriteListGuids.Length);
            foreach (string favoriteListGuid in favoriteListGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(favoriteListGuid);
                var list = AssetDatabase.LoadAssetAtPath<UMAFavoriteList>(path);
                if (list != null && !refreshedLists.Contains(list)) refreshedLists.Add(list);
            }
            favoritelists = refreshedLists;
        }

        public static void AddNewFavoriteType()
        {
            string Path = EditorUtility.SaveFilePanelInProject("Create New Favorite Category", "CategoryName", "asset", "Create a new favorite category");
            if (Path != "")
            {
                string CategoryName = System.IO.Path.GetFileNameWithoutExtension(Path);
                var asset =  CustomAssetUtility.CreateAsset<UMAFavoriteList>(Path,false,CategoryName,false);
                UMAFavoriteWindow.favoritelists.Add(asset);
                AddFavorite(asset);
            }
            if (instance != null)
            {
                instance.Repaint();
            }
        }


        public static void AddFavorite(object oFavoriteList)
        {
            var favoriteList = oFavoriteList as UMAFavoriteList;
            if (favoriteList == null) return;
            foreach (var o in Selection.objects)
            {
                favoriteList.AddAsset(o);
            }
            EditorUtility.SetDirty(favoriteList);
            AssetDatabase.SaveAssetIfDirty(favoriteList);
            if (instance != null)
            {
                instance.Repaint();
            }
        }

        [UnityEditor.MenuItem("Assets/Add Selected Assets to UMA Favorites")]
        public static void AddSelectedToFavorites()
        {
            if (!initialSearchCompleted)
            {
                RefreshFavoriteListCategories();
            }
            List<UMAGenericPopupChoice> choices = new List<UMAGenericPopupChoice>();

            favoritelists.RemoveAll(fl => fl == null);
            foreach(var fl in favoritelists)
            {
                if (fl == null) continue;
                UMAGenericPopupChoice choice = new UMAGenericPopupChoice(new GUIContent(fl.name), () => { AddFavorite(fl); });
                choices.Add(choice);
            }

            if (choices.Count > 0)
            {
                // Add seperator
                choices.Add(new UMAGenericPopupChoice());
            }

            choices.Add(new UMAGenericPopupChoice(new GUIContent("Add New Favorite Category"), AddNewFavoriteType));
            UMAGenericPopupSelection.ShowWindow("Add to Favorites", choices);
        }

        void OnEnable()
        {
            instance = this;
            EditorApplication.update += CheckInspectors;
            EditorApplication.projectChanged += HandleProjectChanged;
            RefreshFavoriteListCategories();
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckInspectors;
            EditorApplication.projectChanged -= HandleProjectChanged;
            if (instance == this) instance = null;
        }

        private void HandleProjectChanged()
        {
            RefreshFavoriteListCategories();
            Repaint();
        }

        private List<UnityEngine.Object> InspectMe = new List<UnityEngine.Object>();

        private void CheckInspectors()
        {
            if (InspectMe.Count > 0)
            {
                for (int i = 0; i < InspectMe.Count; i++)
                {
                    if (InspectMe[i] != null) InspectorUtlity.InspectTarget(InspectMe[i]);
                }
                InspectMe.Clear();
            }
        }

        private void OnGUI()
        {
            instance = this;
            UMAFavoriteList deletedList = null;

            if (!initialSearchCompleted)
            {
                RefreshFavoriteListCategories();
            }
            favoritelists.RemoveAll(fl => fl == null);
            // search bar, refresh button
            using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                scrollPosition = scrollView.scrollPosition;
                foreach (UMAFavoriteList fl in favoritelists)
                {
                    if (fl != null && DrawFavoriteList(fl)) deletedList = fl;
                }
            }

            if (deletedList != null)
            {
                string path = AssetDatabase.GetAssetPath(deletedList);
                favoritelists.Remove(deletedList);
                if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        private void RemoveFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            var favoriteList = favorite?.favoriteList;
            if (favoriteList == null) return;
            favoriteList.RemoveAsset(favorite);
            EditorUtility.SetDirty(favoriteList);
            AssetDatabase.SaveAssetIfDirty(favoriteList);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(favoriteList));
            Repaint();
        }

        private void OpenFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            if (favorite?.asset != null) AssetDatabase.OpenAsset(favorite.asset);
        }

        private void PingFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            if (favorite?.asset == null) return;
            Selection.activeObject = favorite.asset;
            EditorGUIUtility.PingObject(favorite.asset);
        }

        private void InspectFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            if (favorite?.asset != null) InspectMe.Add(favorite.asset);
            //InspectorUtlity.InspectTarget(favorite.asset); // this causes GUI errors in Unity 2022+ 
            // Selection.activeObject = favorite.asset;
        }

        private bool DrawFavoriteList(UMAFavoriteList fl)
        {
            if (fl == null) return false;
            UMAFavorite deletedFavorite = null;
            bool pingPressed = false;
            bool deletePressed = false;
            GUIContent pingButton = new GUIContent("", "Ping");
            GUIContent inspectButton = new GUIContent("", "Inspect");
            GUIContent deleteButton = new GUIContent("", "Remove");
            //GUIContent openButton = new GUIContent("", "Open");
            pingButton.image = EditorGUIUtility.IconContent("d_scenepicking_pickable_hover@2x").image;
            inspectButton.image = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow@2x").image;
            deleteButton.image = EditorGUIUtility.IconContent("d_clear@2x").image;
            // openButton.image = EditorGUIUtility.IconContent("Customized@2x").image;

            GUIHelper.FoldoutBarButton(ref fl.exPanded, fl.name, "Ping", out pingPressed, out deletePressed);
            if (fl.exPanded)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                try
                {
                    if (fl.Favorites != null)
                    {
                        foreach (UMAFavorite favorite in fl.Favorites)
                        {
                            if (favorite == null) continue;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                UnityEngine.Object asset = favorite.asset;
                                string path = asset != null
                                    ? AssetDatabase.GetAssetPath(asset) : null;
                                EditorGUILayout.LabelField(
                                    new GUIContent(!string.IsNullOrEmpty(path)
                                        ? AssetDatabase.GetCachedIcon(path) : null),
                                    GUILayout.Width(20), GUILayout.Height(22));
                                using (new EditorGUI.DisabledScope(asset == null))
                                {
                                    if (GUILayout.Button($"{favorite.name}",
                                        GUILayout.ExpandWidth(true), GUILayout.MinWidth(120),
                                        GUILayout.Height(22)))
                                        OpenFavorite(favorite);
//                    if (GUILayout.Button(openButton,GUILayout.Width(22), GUILayout.Height(22)))
//                    {
//                        OpenFavorite(o);
//                    }
                                    if (GUILayout.Button(pingButton, GUILayout.Width(22),
                                        GUILayout.Height(22))) PingFavorite(favorite);
                                    if (GUILayout.Button(inspectButton, GUILayout.Width(22),
                                        GUILayout.Height(22))) InspectFavorite(favorite);
                                }
                                if (GUILayout.Button(deleteButton, GUILayout.Width(22),
                                    GUILayout.Height(22))) deletedFavorite = favorite;
                            }
                        }
                    }
                }
                finally
                {
                    GUIHelper.EndVerticalPadded(10);
                }
                if (deletedFavorite != null)
                {
                    RemoveFavorite(deletedFavorite);
                }
            }
            if (pingPressed)
            {
                EditorGUIUtility.PingObject(fl);
            }
            return deletePressed;
        }
    }
}
