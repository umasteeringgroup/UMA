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

        [MenuItem("UMA/Favorites")]
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
            var favoritelists = AssetDatabase.FindAssets("t:UMAFavoriteList");
            foreach (var favoritelist in favoritelists)
            {
                var path = AssetDatabase.GUIDToAssetPath(favoritelist);
                var list = AssetDatabase.LoadAssetAtPath<UMAFavoriteList>(path);
                if (list != null)
                {
                    if (!UMAFavoriteWindow.favoritelists.Contains(list))
                    {
                        UMAFavoriteWindow.favoritelists.Add(list);
                    }
                }
            }
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

            foreach(var fl in favoritelists)
            {
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
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckInspectors;
        }

        private List<UnityEngine.Object> InspectMe = new List<UnityEngine.Object>();

        private void CheckInspectors()
        {
            if (InspectMe.Count > 0)
            {
                for (int i = 0; i < InspectMe.Count; i++)
                {
                    InspectorUtlity.InspectTarget(InspectMe[i]);
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
            // search bar, refresh button
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var fl in favoritelists)
            {
                if (DrawFavoriteList(fl))
                {
                    deletedList = fl;
                }
            }
            EditorGUILayout.EndScrollView();

            if (deletedList != null)
            {
                favoritelists.Remove(deletedList);
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(deletedList));
            }
        }

        private void RemoveFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            var favoriteList = favorite.favoriteList;
            favoriteList.RemoveAsset(favorite);
            EditorUtility.SetDirty(favoriteList);
            AssetDatabase.SaveAssetIfDirty(favoriteList);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(favoriteList));
            Repaint();
        }

        private void OpenFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            AssetDatabase.OpenAsset(favorite.asset); 
        }

        private void PingFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            Selection.activeObject = favorite.asset;
            EditorGUIUtility.PingObject(favorite.asset);
        }

        private void InspectFavorite(object oFavorite)
        {
            var favorite = oFavorite as UMAFavorite;
            InspectMe.Add(favorite.asset);
            //InspectorUtlity.InspectTarget(favorite.asset); // this causes GUI errors in Unity 2022+ 
            // Selection.activeObject = favorite.asset;
        }

        private bool DrawFavoriteList(UMAFavoriteList fl)
        {
            UMAFavorite deletedFavorite = null;
            bool pingPressed = false;
            bool deletePressed = false;
            GUIContent pingButton = new GUIContent("", "Ping");
            GUIContent inspectButton = new GUIContent("", "Inspect");
            GUIContent deleteButton = new GUIContent("", "Remove");
            //GUIContent openButton = new GUIContent("", "Open");
            pingButton.image = EditorGUIUtility.IconContent("d_scenepicking_pickable_hover@2x").image;
            inspectButton.image = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow@2x").image;
            deleteButton.image = EditorGUIUtility.IconContent("d_winbtn_win_close_h@2x").image;
            // openButton.image = EditorGUIUtility.IconContent("Customized@2x").image;

            GUIHelper.FoldoutBarButton(ref fl.exPanded, fl.name, "Ping", out pingPressed, out deletePressed);
            if (fl.exPanded)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                foreach (var o in fl.Favorites)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(new GUIContent(AssetDatabase.GetCachedIcon(AssetDatabase.GetAssetPath(o.asset))), GUILayout.Width(20), GUILayout.Height(22));
                    //Type t = o.asset.GetType();
                    // EditorGUILayout.LabelField($"{o.name}", GUILayout.ExpandWidth(true), GUILayout.MinWidth(120), GUILayout.Height(22));
                    if (GUILayout.Button($"{o.name}", GUILayout.ExpandWidth(true), GUILayout.MinWidth(120), GUILayout.Height(22)))
                    {
                        OpenFavorite(o);
                    }
//                    if (GUILayout.Button(openButton,GUILayout.Width(22), GUILayout.Height(22)))
//                    {
//                        OpenFavorite(o);
//                    }
                    if (GUILayout.Button(pingButton, GUILayout.Width(22),GUILayout.Height(22)))
                    {
                        PingFavorite(o);
                    }
                    if (GUILayout.Button(inspectButton, GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        InspectFavorite(o);
                    }
                    if (GUILayout.Button(deleteButton, GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        deletedFavorite = o;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                GUIHelper.EndVerticalPadded(10);
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
