using System;
using System.IO;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public static class UMAPresetAssetUtility
    {
        private static string FolderKey => "UMA.Preset.LastSaveFolder." + Application.dataPath;

        public static string LastSaveFolder
        {
            get
            {
                string folder = EditorPrefs.GetString(FolderKey, "Assets");
                return (folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)) && AssetDatabase.IsValidFolder(folder) ? folder : "Assets";
            }
        }

        public static void RememberSaveFolder(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && (folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)) && AssetDatabase.IsValidFolder(folder))
                EditorPrefs.SetString(FolderKey, folder);
        }

        [MenuItem("UMA/Presets/Import Legacy Preset...")]
        private static void ImportNew() => ImportLegacy();

        public static bool ImportLegacy(UMAPreset destination = null)
        {
            string sourcePath = EditorUtility.OpenFilePanel("Import legacy UMA preset", LastSaveFolder, "umapreset,json");
            if (string.IsNullOrEmpty(sourcePath)) return false;
            UMAPreset created = null;
            try
            {
                var definition = UMAPreset.ConvertLegacyJson(File.ReadAllText(sourcePath), destination != null ? destination.Definition.RaceName : null);
                var references = definition.Wardrobe.Select(ResolveRecipe).ToArray();
                string path = destination != null ? AssetDatabase.GetAssetPath(destination) :
                    EditorUtility.SaveFilePanelInProject("Save converted UMA preset", Path.GetFileNameWithoutExtension(sourcePath), "asset", "Save the converted preset asset.", LastSaveFolder);
                if (string.IsNullOrEmpty(path)) return false;
                if (destination != null && !EditorUtility.DisplayDialog("Import legacy preset", "Replace this preset's DNA, colors and wardrobe? Its race and icon will be preserved.", "Import", "Cancel")) return false;
                if (destination == null)
                {
                    path = AssetDatabase.GenerateUniqueAssetPath(path);
                    created = ScriptableObject.CreateInstance<UMAPreset>();
                }
                var preset = destination != null ? destination : created;
                if (destination != null) Undo.RecordObject(preset, "Import legacy UMA preset");
                preset.Definition = definition;
                preset.WardrobeRecipes = references;
                if (destination == null) AssetDatabase.CreateAsset(preset, path);
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssetIfDirty(preset);
                RememberSaveFolder(path);
                Selection.activeObject = preset;
                EditorGUIUtility.PingObject(preset);
                return true;
            }
            catch (Exception ex)
            {
                if (created != null && !AssetDatabase.Contains(created)) UnityEngine.Object.DestroyImmediate(created);
                EditorUtility.DisplayDialog("Unable to import UMA preset", ex.Message, "OK");
                return false;
            }
        }

        private static UMATextRecipe ResolveRecipe(string name)
        {
            var index = UMAAssetIndexer.Instance;
            if (index != null && index.HasRecipe(name))
            {
                var indexed = index.GetRecipe(name, false);
                if (indexed != null) return indexed;
            }
            var matches = AssetDatabase.FindAssets("t:UMATextRecipe")
                .Select(guid => AssetDatabase.LoadAssetAtPath<UMATextRecipe>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(recipe => recipe != null && recipe.name == name).ToArray();
            if (matches.Length == 1) return matches[0];
            throw new InvalidOperationException(matches.Length == 0 ? "Missing wardrobe recipe: " + name : "Multiple wardrobe recipes named: " + name + ". Resolve the duplicate names before importing.");
        }
    }
}
