using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class BrushEditor : EditorWindow
    {
        private BrushLibrary library;
        private int selection = -1;
        private Vector2 scroll;

        public static void Open(BrushLibrary initialLibrary = null)
        {
            BrushEditor window = GetWindow<BrushEditor>(true, "Overlay Painter Brush Library");
            if (initialLibrary != null) window.library = initialLibrary;
            if (window.library != null)
                TexturePaintStageWindow.ActiveStage?.SetCurrentBrushLibrary(window.library);
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            BrushLibrary nextLibrary = (BrushLibrary)EditorGUILayout.ObjectField(
                "Library", library, typeof(BrushLibrary), false);
            if (nextLibrary != library)
            {
                library = nextLibrary;
                selection = -1;
                TexturePaintStageWindow.ActiveStage?.SetCurrentBrushLibrary(library);
            }
            if (library == null)
            {
                EditorGUILayout.HelpBox("Assign a BrushLibrary asset, or create one.", MessageType.Info);
                if (GUILayout.Button("Create Brush Library")) CreateLibrary();
                return;
            }
            int imported = BrushLibrarySpriteSheetUtility.DrawDropPad(library);
            if (imported > 0) selection = library.Brushes.Count - 1;
            EditorGUILayout.Space(3f);
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
            TexturePaintStageWindow.ActiveStage?.SetCurrentBrushLibrary(library);
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

    internal sealed class BrushNamePromptWindow : EditorWindow
    {
        private string brushName;
        private Action<string> onAccepted;
        private bool focusNameField = true;

        internal static void Show(string initialName, Action<string> onAccepted)
        {
            BrushNamePromptWindow window = CreateInstance<BrushNamePromptWindow>();
            window.titleContent = new GUIContent("Save New Brush");
            window.brushName = initialName ?? string.Empty;
            window.onAccepted = onAccepted;
            window.minSize = window.maxSize = new Vector2(390f, 100f);
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Brush Name", EditorStyles.boldLabel);
            GUI.SetNextControlName("BrushName");
            brushName = EditorGUILayout.TextField(brushName ?? string.Empty);
            if (focusNameField)
            {
                EditorGUI.FocusTextInControl("BrushName");
                focusNameField = false;
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80f))) Close();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(brushName)))
                if (GUILayout.Button("Save", GUILayout.Width(80f))) Accept();
            GUILayout.EndHorizontal();

            Event current = Event.current;
            if (current.type != EventType.KeyDown) return;
            if (current.keyCode == KeyCode.Escape)
            {
                current.Use();
                Close();
            }
            else if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) &&
                !string.IsNullOrWhiteSpace(brushName))
            {
                current.Use();
                Accept();
            }
        }

        private void Accept()
        {
            Action<string> callback = onAccepted;
            onAccepted = null;
            string acceptedName = brushName;
            Close();
            callback?.Invoke(acceptedName);
        }
    }

    internal static class BrushLibrarySpriteSheetUtility
    {
        private const float DropPadHeight = 58f;
        private static string feedback;
        private static MessageType feedbackType = MessageType.Info;

        public static int DrawDropPad(BrushLibrary library)
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, DropPadHeight, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, new GUIContent("Drop Sprite Sheet Here\nCreates one Stamp brush per Sprite",
                "Drop a Texture2D imported as a sprite sheet, or one of its Sprite sub-assets."),
                EditorStyles.helpBox);

            int created = 0;
            Event current = Event.current;
            if (dropArea.Contains(current.mousePosition) &&
                (current.type == EventType.DragUpdated || current.type == EventType.DragPerform))
            {
                UnityEngine.Object sheet = FindSpriteSheet(DragAndDrop.objectReferences);
                DragAndDrop.visualMode = sheet != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (sheet == null)
                    {
                        feedback = "The dropped asset does not contain any Sprites.";
                        feedbackType = MessageType.Warning;
                    }
                    else
                    {
                        List<BrushPreset> presets = CreateBrushesFromSpriteSheet(library, sheet,
                            out int skipped);
                        created = presets.Count;
                        feedback = created > 0
                            ? $"Created {created} brush{(created == 1 ? string.Empty : "es")}" +
                              (skipped > 0 ? $"; skipped {skipped} already in this library." : ".")
                            : skipped > 0
                                ? "Every Sprite in that sheet already has a brush in this library."
                                : "The dropped asset does not contain any Sprites.";
                        feedbackType = created > 0 ? MessageType.Info : MessageType.Warning;
                    }
                    current.Use();
                }
                else current.Use();
            }

            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, feedbackType);
            return created;
        }

        internal static List<BrushPreset> CreateBrushesFromSpriteSheet(BrushLibrary library,
            UnityEngine.Object droppedAsset, out int skipped)
        {
            List<BrushPreset> created = new List<BrushPreset>();
            skipped = 0;
            if (library == null || droppedAsset == null) return created;
            string libraryPath = AssetDatabase.GetAssetPath(library);
            string sheetPath = AssetDatabase.GetAssetPath(droppedAsset);
            if (string.IsNullOrEmpty(libraryPath) || string.IsNullOrEmpty(sheetPath)) return created;

            List<Sprite> sprites = LoadSprites(sheetPath);
            if (sprites.Count == 0) return created;
            string libraryFolder = Path.GetDirectoryName(libraryPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(libraryFolder)) return created;
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(sheetPath);
            string sheetName = mainAsset != null ? mainAsset.name : Path.GetFileNameWithoutExtension(sheetPath);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Brushes From Sprite Sheet");
            Undo.RegisterCompleteObjectUndo(library, "Create Brushes From Sprite Sheet");
            for (int i = 0; i < sprites.Count; i++)
            {
                Sprite sprite = sprites[i];
                if (LibraryContainsSprite(library, sprite))
                {
                    skipped++;
                    continue;
                }

                string displayName = sheetName + " " + (i + 1);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    libraryFolder + "/" + SafeFileName(displayName) + ".asset");
                BrushPreset preset = ScriptableObject.CreateInstance<BrushPreset>();
                preset.name = displayName;
                preset.shape = BrushPreset.Shape.Stamp;
                preset.stampSprite = sprite;
                preset.stampTexture = null;
                AssetDatabase.CreateAsset(preset, assetPath);
                Undo.RegisterCreatedObjectUndo(preset, "Create Brushes From Sprite Sheet");
                library.Add(preset);
                TexturePaintStageWindow.ActiveStage?.RecordBrushLibraryChange(library, preset,
                    library.Brushes.Count - 1, true);
                created.Add(preset);
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
            return created;
        }

        private static UnityEngine.Object FindSpriteSheet(UnityEngine.Object[] objects)
        {
            if (objects == null) return null;
            for (int i = 0; i < objects.Length; i++)
            {
                UnityEngine.Object candidate = objects[i];
                if (candidate is not Texture2D && candidate is not Sprite) continue;
                string path = AssetDatabase.GetAssetPath(candidate);
                if (!string.IsNullOrEmpty(path) && LoadSprites(path).Count > 0) return candidate;
            }
            return null;
        }

        private static List<Sprite> LoadSprites(string assetPath)
        {
            List<Sprite> sprites = new List<Sprite>();
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
                if (assets[i] is Sprite sprite && !sprites.Contains(sprite)) sprites.Add(sprite);
            return sprites;
        }

        private static bool LibraryContainsSprite(BrushLibrary library, Sprite sprite)
        {
            for (int i = 0; i < library.Brushes.Count; i++)
                if (library.Brushes[i] != null && library.Brushes[i].stampSprite == sprite) return true;
            return false;
        }

        private static string SafeFileName(string value)
        {
            char[] result = value.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < result.Length; i++)
                if (System.Array.IndexOf(invalid, result[i]) >= 0 || result[i] == '/' || result[i] == '\\')
                    result[i] = '_';
            return new string(result);
        }
    }

    [CustomEditor(typeof(BrushLibrary))]
    public sealed class BrushLibraryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(6f);
            BrushLibrarySpriteSheetUtility.DrawDropPad((BrushLibrary)target);
        }
    }

    internal static class BrushPresetInspectorUtility
    {
        public static void DrawStampSource(BrushPreset preset)
        {
            Texture2D nextTexture = (Texture2D)EditorGUILayout.ObjectField(
                "Stamp Texture", preset.stampTexture, typeof(Texture2D), false);
            if (nextTexture != preset.stampTexture)
            {
                preset.stampTexture = nextTexture;
                if (nextTexture != null) preset.stampSprite = null;
            }
            Sprite nextSprite = (Sprite)EditorGUILayout.ObjectField(
                "Stamp Sprite", preset.stampSprite, typeof(Sprite), false);
            if (nextSprite != preset.stampSprite)
            {
                preset.stampSprite = nextSprite;
                if (nextSprite != null) preset.stampTexture = null;
            }
        }

        public static void DrawRandomization(BrushPreset preset)
        {
            using (new EditorGUI.DisabledScope(preset.alignToStroke))
                preset.randomRotation = EditorGUILayout.Toggle(new GUIContent("Random Rotation",
                    "Applies a new random 0-360 degree rotation to every stamp. Follow Stroke controls rotation instead."),
                    preset.randomRotation);
            if (preset.alignToStroke)
                EditorGUILayout.LabelField("Random Rotation is unavailable while Follow Stroke is enabled.",
                    EditorStyles.wordWrappedMiniLabel);

            preset.randomSizeVariation = EditorGUILayout.Toggle(
                new GUIContent("Random Size Variation",
                    "Randomly changes the complete world-space brush size for each paint stamp."),
                preset.randomSizeVariation);
            using (new EditorGUI.DisabledScope(!preset.randomSizeVariation))
            {
                preset.randomSizeShrink = EditorGUILayout.Slider("Shrink (%)",
                    Mathf.Clamp01(preset.randomSizeShrink) * 100f, 0f, 100f) * 0.01f;
                preset.randomSizeGrow = EditorGUILayout.Slider("Grow (%)",
                    Mathf.Clamp01(preset.randomSizeGrow) * 100f, 0f, 100f) * 0.01f;
            }

            preset.splatter = EditorGUILayout.Toggle(new GUIContent("Splatter",
                "Randomly offsets each paint stamp around the stroke center."), preset.splatter);
            using (new EditorGUI.DisabledScope(!preset.splatter))
            {
                preset.splatterDistance = EditorGUILayout.Slider(new GUIContent("Splatter Distance (%)",
                        "Maximum random offset as a percentage of the stamp's effective world-space size."),
                    Mathf.Clamp(preset.splatterDistance, 0.01f, 2f) * 100f, 1f, 200f) * 0.01f;
                preset.randomStrength = EditorGUILayout.Toggle(new GUIContent("Random Strength",
                    "Randomly varies each splatter stamp from zero to the current paint strength."),
                    preset.randomStrength);
            }
        }

        public static void DrawStrokeEvolution(BrushPreset preset)
        {
            preset.fade = EditorGUILayout.Toggle(new GUIContent("Fade",
                "Reduces stamp alpha from full strength to zero over World Length, or over the complete stroke when Auto Fade is enabled. Tablet pressure still multiplies flow when Pressure Affects Flow is enabled."),
                preset.fade);
            using (new EditorGUI.DisabledScope(!preset.fade))
                preset.autoFade = EditorGUILayout.Toggle(new GUIContent("Auto Fade",
                    "Draws at full flow while the pointer is down, then redraws the completed stroke with flow fading from full strength at the start to zero at the end."),
                    preset.autoFade);
            preset.taper = EditorGUILayout.Toggle(new GUIContent("Taper",
                "Reduces stamp size from full size to zero over World Length, or over the complete stroke when Auto Taper is enabled. Tablet pressure still multiplies size when Pressure Affects Size is enabled."),
                preset.taper);
            using (new EditorGUI.DisabledScope(!preset.taper))
                preset.autoTaper = EditorGUILayout.Toggle(new GUIContent("Auto Taper",
                    "Draws at full size while the pointer is down, then redraws the completed stroke with size tapering from full size at the start to zero at the end."),
                    preset.autoTaper);
            bool usesManualLength = (preset.fade && !preset.autoFade) ||
                (preset.taper && !preset.autoTaper);
            using (new EditorGUI.DisabledScope(!usesManualLength))
            {
                EditorGUI.BeginChangeCheck();
                float length = EditorGUILayout.FloatField(new GUIContent("World Length",
                    "World-space length used by non-auto Fade and Taper. Auto modes use the completed stroke length instead. The untouched default tracks three times the brush size."),
                    preset.ResolvedFadeTaperLength);
                if (EditorGUI.EndChangeCheck()) preset.fadeTaperLength = Mathf.Max(0.0001f, length);
            }
        }
    }

    [CustomEditor(typeof(BrushPreset))]
    public sealed class BrushPresetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            BrushPreset preset = (BrushPreset)target;
            Undo.RecordObject(preset, "Edit Overlay Painter Brush Preset");
            EditorGUI.BeginChangeCheck();
            preset.shape = (BrushPreset.Shape)EditorGUILayout.EnumPopup("Shape", preset.shape);
            if (preset.shape == BrushPreset.Shape.Stamp)
                BrushPresetInspectorUtility.DrawStampSource(preset);
            preset.size = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Size", preset.size));
            preset.hardness = EditorGUILayout.Slider("Hardness", preset.hardness, 0f, 1f);
            preset.flow = EditorGUILayout.Slider("Flow", preset.flow, 0f, 1f);
            preset.spacing = EditorGUILayout.Slider("Spacing", preset.spacing, 0.01f, 10f);
            preset.rotation = EditorGUILayout.Slider("Rotation", preset.rotation, -180f, 180f);
            preset.blendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                "Blend Mode", preset.blendMode);
            preset.mirrorStroke = EditorGUILayout.Toggle("Mirror Stroke", preset.mirrorStroke);
            preset.alignToStroke = EditorGUILayout.Toggle("Follow Stroke", preset.alignToStroke);
            BrushPresetInspectorUtility.DrawRandomization(preset);
            BrushPresetInspectorUtility.DrawStrokeEvolution(preset);
            preset.tags = EditorGUILayout.TextField("Tags", preset.tags);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(preset);
        }
    }
}
