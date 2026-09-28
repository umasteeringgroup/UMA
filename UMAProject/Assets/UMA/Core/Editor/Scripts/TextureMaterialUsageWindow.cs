using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Finds project materials that reference a selected Texture2D and presents
    /// the matches without replacing the user's current Project selection.
    /// </summary>
    public sealed class TextureMaterialUsageWindow : EditorWindow
    {
        private const string MenuPath = "Assets/Find Usage in Material";
        private const float WindowWidth = 720f;
        private const float RowHeight = 42f;

        [SerializeField] private Texture2D texture;
        [SerializeField] private List<Material> materials = new List<Material>();
        [SerializeField] private bool searchCompleted;
        [SerializeField] private bool searchCanceled;

        private Vector2 scrollPosition;

        [MenuItem(MenuPath, false, 2011)]
        private static void FindUsageForSelectedTexture()
        {
            Texture2D selectedTexture = GetSelectedProjectTexture();
            if (selectedTexture == null)
            {
                return;
            }

            TextureMaterialUsageWindow window = GetWindow<TextureMaterialUsageWindow>(
                true, "Texture Material Usage", true);
            window.minSize = new Vector2(560f, 220f);
            window.texture = selectedTexture;
            window.RunSearch(true);
            window.CenterOverMainWindow();
            window.ShowUtility();
            window.Focus();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateFindUsageForSelectedTexture()
        {
            return GetSelectedProjectTexture() != null;
        }

        private static Texture2D GetSelectedProjectTexture()
        {
            if (Selection.objects == null || Selection.objects.Length != 1)
            {
                return null;
            }

            Texture2D selectedTexture = Selection.activeObject as Texture2D;
            if (selectedTexture == null || !AssetDatabase.Contains(selectedTexture))
            {
                return null;
            }

            return string.IsNullOrEmpty(AssetDatabase.GetAssetPath(selectedTexture))
                ? null
                : selectedTexture;
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (texture == null)
            {
                EditorGUILayout.HelpBox(
                    "The texture used for this search is no longer available.",
                    MessageType.Warning);
                return;
            }

            DrawTextureSummary();
            EditorGUILayout.Space(4f);
            DrawResults();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Find Usage in Material", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(texture == null))
                {
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    {
                        RunSearch(true);
                    }
                }
            }
        }

        private void DrawTextureSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Texture", texture, typeof(Texture2D), false);
                }

                string texturePath = AssetDatabase.GetAssetPath(texture);
                EditorGUILayout.LabelField(texturePath, EditorStyles.miniLabel);

                if (searchCompleted)
                {
                    string suffix = materials.Count == 1 ? "material" : "materials";
                    EditorGUILayout.LabelField(
                        $"Found {materials.Count} {suffix} using this texture.",
                        EditorStyles.boldLabel);
                }
            }
        }

        private void DrawResults()
        {
            if (!searchCompleted)
            {
                EditorGUILayout.HelpBox("The material search has not run yet.", MessageType.Info);
                return;
            }

            if (materials.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    searchCanceled
                        ? "The search was canceled before any matching materials were found."
                        : "No material assets use this texture.",
                    searchCanceled ? MessageType.Warning : MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int index = 0; index < materials.Count; index++)
            {
                DrawMaterialRow(materials[index]);
            }
            EditorGUILayout.EndScrollView();

            if (searchCanceled)
            {
                EditorGUILayout.HelpBox(
                    "The search was canceled. The list contains only the matches found before cancellation.",
                    MessageType.Warning);
            }
        }

        private static void DrawMaterialRow(Material material)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox, GUILayout.Height(RowHeight)))
            {
                if (material == null)
                {
                    GUILayout.Label("Missing material", EditorStyles.miniLabel);
                    return;
                }

                string path = AssetDatabase.GetAssetPath(material);
                Texture icon = AssetDatabase.GetCachedIcon(path);
                GUILayout.Label(icon, GUILayout.Width(24f), GUILayout.Height(24f));

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Label(new GUIContent(material.name, path), EditorStyles.boldLabel);
                    GUILayout.Label(new GUIContent(path, path), EditorStyles.miniLabel);
                }

                if (GUILayout.Button("Ping", GUILayout.Width(54f), GUILayout.Height(24f)))
                {
                    EditorGUIUtility.PingObject(material);
                }

                if (GUILayout.Button("Inspect", GUILayout.Width(62f), GUILayout.Height(24f)))
                {
                    QueuePopupInspector(material);
                }
            }
        }

        private static void QueuePopupInspector(Material material)
        {
            // Opening an InspectorWindow while processing an IMGUI event can leave
            // the current view with an invalid GUILayout state. Defer it until the
            // current event is complete.
            EditorApplication.delayCall += () =>
            {
                if (material != null)
                {
                    InspectorUtlity.InspectTarget(material);
                }
            };
        }

        private void RunSearch(bool showProgress)
        {
            materials = FindMaterialsUsingTexture(texture, showProgress, out searchCanceled);
            searchCompleted = true;
            scrollPosition = Vector2.zero;
            Repaint();
        }

        /// <summary>
        /// Returns every project material or material sub-asset that uses the
        /// supplied texture. Material variants are checked through their resolved
        /// properties, while saved serialized references catch hidden or obsolete
        /// shader properties.
        /// </summary>
        public static List<Material> FindMaterialsUsingTexture(Texture2D targetTexture)
        {
            return FindMaterialsUsingTexture(targetTexture, false, out _);
        }

        private static List<Material> FindMaterialsUsingTexture(
            Texture2D targetTexture,
            bool showProgress,
            out bool canceled)
        {
            List<Material> matches = new List<Material>();
            canceled = false;
            if (targetTexture == null)
            {
                return matches;
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material");
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> materialPaths = new List<string>(materialGuids.Length);
            for (int index = 0; index < materialGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[index]);
                if (!string.IsNullOrEmpty(path) && uniquePaths.Add(path))
                {
                    materialPaths.Add(path);
                }
            }
            materialPaths.Sort(StringComparer.OrdinalIgnoreCase);

            try
            {
                for (int pathIndex = 0; pathIndex < materialPaths.Count; pathIndex++)
                {
                    string path = materialPaths[pathIndex];
                    if (showProgress && (pathIndex & 15) == 0 &&
                        EditorUtility.DisplayCancelableProgressBar(
                            "Find Usage in Material",
                            path,
                            materialPaths.Count == 0 ? 1f : pathIndex / (float)materialPaths.Count))
                    {
                        canceled = true;
                        break;
                    }

                    UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                    {
                        Material material = assets[assetIndex] as Material;
                        if (material != null && MaterialUsesTexture(material, targetTexture))
                        {
                            matches.Add(material);
                        }
                    }
                }
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            matches.Sort(CompareMaterials);
            return matches;
        }

        /// <summary>
        /// Tests both resolved shader properties and direct serialized references.
        /// </summary>
        public static bool MaterialUsesTexture(Material material, Texture2D targetTexture)
        {
            if (material == null || targetTexture == null)
            {
                return false;
            }

            string[] texturePropertyNames = material.GetTexturePropertyNames();
            for (int index = 0; index < texturePropertyNames.Length; index++)
            {
                if (material.GetTexture(texturePropertyNames[index]) == targetTexture)
                {
                    return true;
                }
            }

            using (SerializedObject serializedMaterial = new SerializedObject(material))
            {
                SerializedProperty property = serializedMaterial.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue == targetTexture)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CompareMaterials(Material left, Material right)
        {
            if (left == right)
            {
                return 0;
            }
            if (left == null)
            {
                return 1;
            }
            if (right == null)
            {
                return -1;
            }

            int pathComparison = string.Compare(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right),
                StringComparison.OrdinalIgnoreCase);
            return pathComparison != 0
                ? pathComparison
                : string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        }

        private void CenterOverMainWindow()
        {
            float requestedHeight = Mathf.Clamp(150f + materials.Count * (RowHeight + 4f), 220f, 680f);
            Rect mainWindow = EditorGUIUtility.GetMainWindowPosition();
            position = new Rect(
                mainWindow.center.x - WindowWidth * 0.5f,
                mainWindow.center.y - requestedHeight * 0.5f,
                WindowWidth,
                requestedHeight);
        }
    }
}
