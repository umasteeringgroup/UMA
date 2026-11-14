using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Recipe folders for the Rig Profile Editor's preview: pick a root folder, and every folder under
    /// it that holds wardrobe recipes is listed by its whole path. A folder's Edit button loads its
    /// recipes into the preview's recipe list, replacing what is there, so each one can then be
    /// switched on or off there.
    /// </summary>
    public sealed partial class UMARigProfileEditorWindow
    {
        private const float FolderRowHeight = 20f;
        private const float FolderListMaxHeight = 260f;

        private sealed class RecipeFolder
        {
            public string Path;    // asset path of the folder
            public string Label;   // path below the root folder, the whole way down
            public readonly List<UMATextRecipe> Recipes = new List<UMATextRecipe>();
        }

        [SerializeField] private DefaultAsset recipeRoot;
        // The folder last loaded with Edit: marked in the list, and where Previous / Next step from.
        [SerializeField] private string loadedRecipeFolder = string.Empty;
        [SerializeField] private bool folderRecipesForRaceOnly = true;
        [SerializeField] private string folderFilter = string.Empty;
        [SerializeField] private Vector2 folderScroll;

        [NonSerialized] private List<RecipeFolder> recipeFolders;
        [NonSerialized] private bool recipeFoldersDirty = true;

        private void OnProjectChangedForRecipeFolders()
        {
            recipeFoldersDirty = true;
            Repaint();
        }

        private string RecipeRootPath()
        {
            string path = recipeRoot != null ? AssetDatabase.GetAssetPath(recipeRoot) : null;
            return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path) ? path : null;
        }

        /// <summary>The race names a listed recipe must name when the list is kept to the race: the race, the races it is cross-compatible with, and the race its Legacy Rig was baked from.</summary>
        private HashSet<string> FolderRaceNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (race == null)
            {
                return names;
            }
            names.Add(race.raceName);
            List<string> cross = race.GetCrossCompatibleRaces();
            if (cross != null)
            {
                foreach (string name in cross) if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            if (race.legacyRig != null && !string.IsNullOrEmpty(race.legacyRig.sourceRaceName))
            {
                names.Add(race.legacyRig.sourceRaceName);
            }
            return names;
        }

        private void EnsureRecipeFolders()
        {
            if (!recipeFoldersDirty && recipeFolders != null)
            {
                return;
            }
            recipeFoldersDirty = false;
            recipeFolders = new List<RecipeFolder>();
            string root = RecipeRootPath();
            if (root == null)
            {
                return;
            }
            HashSet<string> raceNames = folderRecipesForRaceOnly && race != null ? FolderRaceNames() : null;
            var byPath = new Dictionary<string, RecipeFolder>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:UMAWardrobeRecipe", new[] { root }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(assetPath);
                if (recipe == null)
                {
                    continue;
                }
                if (raceNames != null && (recipe.compatibleRaces == null || !recipe.compatibleRaces.Exists(raceNames.Contains)))
                {
                    continue;
                }
                string folderPath = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
                if (!byPath.TryGetValue(folderPath, out RecipeFolder folder))
                {
                    string label = folderPath == root
                        ? System.IO.Path.GetFileName(root) + " (the root folder itself)"
                        : folderPath.Substring(root.Length + 1);
                    folder = new RecipeFolder { Path = folderPath, Label = label };
                    byPath.Add(folderPath, folder);
                    recipeFolders.Add(folder);
                }
                folder.Recipes.Add(recipe);
            }
            // Sorting by the whole path keeps each folder's subfolders together, so the list reads as the tree.
            recipeFolders.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            foreach (RecipeFolder folder in recipeFolders)
            {
                folder.Recipes.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private List<RecipeFolder> VisibleRecipeFolders()
        {
            EnsureRecipeFolders();
            if (string.IsNullOrEmpty(folderFilter))
            {
                return recipeFolders;
            }
            return recipeFolders.FindAll(f => f.Label.IndexOf(folderFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Draws the folder list. Returns true when the saved selection changed; <paramref name="enabledChanged"/>
        /// is true when what the preview wears changed (a folder was loaded into the recipe list).
        /// </summary>
        private bool DrawRecipeFolderSection(out bool enabledChanged)
        {
            enabledChanged = false;
            bool changed = false;
            float indent = EditorGUI.indentLevel * 15f;

            EditorGUILayout.LabelField("Recipe folders", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            var root = (DefaultAsset)EditorGUILayout.ObjectField(new GUIContent("Root folder",
                "Every folder under this one that holds wardrobe recipes is listed below, by its whole path."), recipeRoot, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck() && root != recipeRoot)
            {
                string rootPath = root != null ? AssetDatabase.GetAssetPath(root) : null;
                if (root == null || AssetDatabase.IsValidFolder(rootPath))
                {
                    recipeRoot = root;
                    loadedRecipeFolder = string.Empty;
                    folderFilter = string.Empty;
                    recipeFoldersDirty = true;
                    changed = true;
                }
            }
            if (RecipeRootPath() == null)
            {
                EditorGUILayout.LabelField("Drag a folder here to list the folders of wardrobe recipes under it. Edit loads a folder's recipes into Recipes above.", EditorStyles.wordWrappedMiniLabel);
                return changed;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string raceLabel = race == null ? "the race" : race.legacyRig != null && !string.IsNullOrEmpty(race.legacyRig.sourceRaceName)
                ? $"{race.raceName} or {race.legacyRig.sourceRaceName}" : race.raceName;
            folderRecipesForRaceOnly = EditorGUILayout.ToggleLeft(new GUIContent($"Only recipes for {raceLabel}",
                "List only recipes compatible with the race, a race it is cross-compatible with, or the race its Legacy Rig was baked from."), folderRecipesForRaceOnly);
            if (EditorGUI.EndChangeCheck())
            {
                recipeFoldersDirty = true;
            }
            if (GUILayout.Button(new GUIContent("Rescan", "Look for wardrobe recipes again."), EditorStyles.miniButton, GUILayout.Width(56f)))
            {
                recipeFoldersDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            folderFilter = EditorGUILayout.TextField(new GUIContent("Filter", "Show only folders whose path contains this text."), folderFilter);
            if (GUILayout.Button("x", GUILayout.Width(22f)))
            {
                folderFilter = string.Empty;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            List<RecipeFolder> visible = VisibleRecipeFolders();
            if (recipeFolders.Count == 0)
            {
                EditorGUILayout.LabelField(folderRecipesForRaceOnly
                    ? $"No wardrobe recipes for {raceLabel} under this folder. Untick 'Only recipes for' to list every recipe."
                    : "No wardrobe recipes under this folder.", EditorStyles.wordWrappedMiniLabel);
                return changed;
            }

            // Stepping: load the folder before or after the one loaded last.
            int current = visible.FindIndex(f => f.Path == loadedRecipeFolder);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            using (new EditorGUI.DisabledScope(visible.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("◀ Previous", "Edit the folder above the one loaded last: its recipes replace the list above."), EditorStyles.miniButtonLeft))
                {
                    int index = current < 0 ? visible.Count - 1 : (current - 1 + visible.Count) % visible.Count;
                    changed |= LoadRecipeFolder(visible[index], ref enabledChanged);
                }
                if (GUILayout.Button(new GUIContent("Next ▶", "Edit the folder below the one loaded last: its recipes replace the list above."), EditorStyles.miniButtonRight))
                {
                    int index = current < 0 ? 0 : (current + 1) % visible.Count;
                    changed |= LoadRecipeFolder(visible[index], ref enabledChanged);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"{recipeFolders.Count} folder(s). Edit replaces the recipes above with that folder's recipes.", EditorStyles.wordWrappedMiniLabel);

            float listHeight = Mathf.Min(Mathf.Max(1, visible.Count) * FolderRowHeight + 4f, FolderListMaxHeight);
            folderScroll = EditorGUILayout.BeginScrollView(folderScroll, GUILayout.Height(listHeight));
            foreach (RecipeFolder folder in visible)
            {
                bool loaded = folder.Path == loadedRecipeFolder;
                EditorGUILayout.BeginHorizontal(GUILayout.Height(FolderRowHeight - 2f));
                GUILayout.Space(indent);
                if (GUILayout.Button(new GUIContent("Edit", $"Replace the recipes above with the {folder.Recipes.Count} recipe(s) in this folder."), EditorStyles.miniButton, GUILayout.Width(40f)))
                {
                    changed |= LoadRecipeFolder(folder, ref enabledChanged);
                }
                var content = new GUIContent($"{folder.Label}  ({folder.Recipes.Count})", folder.Path + "\n" + string.Join("\n", folder.Recipes.ConvertAll(r => r.name)));
                GUILayout.Label(content, loaded ? EditorStyles.boldLabel : EditorStyles.label);
                EditorGUILayout.EndHorizontal();
            }
            if (visible.Count == 0)
            {
                EditorGUILayout.LabelField("No folder matches the filter.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
            return changed;
        }

        /// <summary>Replaces the hand-added recipes with a folder's recipes, all switched on.</summary>
        private bool LoadRecipeFolder(RecipeFolder folder, ref bool enabledChanged)
        {
            previewRecipes.Clear();
            disabledPreviewRecipes.Clear();
            previewRecipes.AddRange(folder.Recipes);
            loadedRecipeFolder = folder.Path;
            enabledChanged = true;
            return true;
        }

        // --- remembered with the preview list ---------------------------------------------------------

        private void AppendRecipeFolderParts(List<string> parts)
        {
            string root = RecipeRootPath();
            if (root == null)
            {
                return;
            }
            parts.Add("F:" + AssetDatabase.AssetPathToGUID(root));
            string loaded = !string.IsNullOrEmpty(loadedRecipeFolder) ? AssetDatabase.AssetPathToGUID(loadedRecipeFolder) : null;
            if (!string.IsNullOrEmpty(loaded))
            {
                parts.Add("L:" + loaded);
            }
        }

        /// <summary>Reads one saved part if it belongs to the folder list. Returns true when it did.</summary>
        private bool TryLoadRecipeFolderPart(string part, string path)
        {
            if (part.StartsWith("F:", StringComparison.Ordinal))
            {
                var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                if (folder != null && AssetDatabase.IsValidFolder(path))
                {
                    recipeRoot = folder;
                    recipeFoldersDirty = true;
                }
                return true;
            }
            if (part.StartsWith("L:", StringComparison.Ordinal))
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    loadedRecipeFolder = path;
                }
                return true;
            }
            return false;
        }

        /// <summary>Opens a recipe in a popup Inspector, after the current GUI event.</summary>
        private static void InspectRecipe(UMATextRecipe recipe)
        {
            // Opening an Inspector while an IMGUI event is processed can leave this window's layout broken.
            EditorApplication.delayCall += () =>
            {
                if (recipe != null)
                {
                    InspectorUtlity.InspectTarget(recipe);
                }
            };
        }
    }
}
