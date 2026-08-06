using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class BrushEditor : EditorWindow
    {
        private BrushLibrary library;
        private int selection = -1;
        private Vector2 scroll;

        public static void Open()
        {
            BrushEditor window = GetWindow<BrushEditor>(true, "Overlay Painter Brush Library");
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            library = (BrushLibrary)EditorGUILayout.ObjectField("Library", library, typeof(BrushLibrary), false);
            if (library == null)
            {
                EditorGUILayout.HelpBox("Assign a BrushLibrary asset, or create one.", MessageType.Info);
                if (GUILayout.Button("Create Brush Library")) CreateLibrary();
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < library.Brushes.Count; i++)
            {
                BrushPreset preset = library.Brushes[i];
                if (GUILayout.Toggle(selection == i, preset != null ? preset.name : "Missing", "Button")) selection = i;
            }
            EditorGUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add New")) AddPreset();
            using (new EditorGUI.DisabledScope(selection < 0 || selection >= library.Brushes.Count))
            {
                if (GUILayout.Button("Remove")) RemovePreset();
                if (GUILayout.Button("Export JSON")) ExportPreset();
            }
            if (GUILayout.Button("Import JSON")) ImportPreset();
            GUILayout.EndHorizontal();
            if (selection >= 0 && selection < library.Brushes.Count && library.Brushes[selection] != null)
            {
                EditorGUILayout.Space();
                UnityEditor.Editor presetEditor = UnityEditor.Editor.CreateEditor(library.Brushes[selection]);
                presetEditor.OnInspectorGUI();
                DestroyImmediate(presetEditor);
            }
        }

        private void CreateLibrary()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Brush Library", "Overlay Painter Brush Library", "asset", string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            library = CreateInstance<BrushLibrary>(); AssetDatabase.CreateAsset(library, path); AssetDatabase.SaveAssets();
        }

        private void AddPreset()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Brush", "Overlay Painter Brush", "asset", string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            BrushPreset preset = CreateInstance<BrushPreset>(); AssetDatabase.CreateAsset(preset, path);
            library.Add(preset);
            TexturePaintStageWindow.ActiveStage?.RecordBrushLibraryChange(library, preset,
                library.Brushes.Count - 1, true);
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            selection = library.Brushes.Count - 1;
        }

        private void RemovePreset()
        {
            BrushPreset preset = library.Brushes[selection];
            int removedIndex = selection;
            library.Remove(preset);
            TexturePaintStageWindow.ActiveStage?.RecordBrushLibraryChange(library, preset, removedIndex, false);
            EditorUtility.SetDirty(library);
            selection = Mathf.Min(selection, library.Brushes.Count - 1);
        }

        private void ExportPreset()
        {
            BrushPreset preset = library.Brushes[selection];
            string path = EditorUtility.SaveFilePanel("Export Brush", string.Empty, preset.name + ".json", "json");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, EditorJsonUtility.ToJson(preset, true));
        }

        private void ImportPreset()
        {
            string source = EditorUtility.OpenFilePanel("Import Brush", string.Empty, "json");
            if (string.IsNullOrEmpty(source)) return;
            string path = EditorUtility.SaveFilePanelInProject("Save Imported Brush", Path.GetFileNameWithoutExtension(source), "asset", string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            BrushPreset preset = CreateInstance<BrushPreset>(); EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(source), preset);
            AssetDatabase.CreateAsset(preset, path); library.Add(preset);
            TexturePaintStageWindow.ActiveStage?.RecordBrushLibraryChange(library, preset,
                library.Brushes.Count - 1, true);
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            selection = library.Brushes.Count - 1;
        }
    }
}
