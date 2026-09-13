using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairGroomWorkspace : EditorWindow
    {
        private const string QuickStartPath = "Assets/UMA/Docs/Hair Cards - Quick Start.md";
        private const string MapClipboardKey = "UMA.HairCards.GrowthMapClipboard.v1";
        private static HairGrowthMapClipboard mapClipboard;
        private static string mapClipboardStatus;

        private static readonly string[] StepNames =
        {
            "Setup", "Growth", "Guides", "Groom", "Cards", "Optimize", "Validate & Bake"
        };

        [SerializeField] private Vector2 explorerScroll;
        [SerializeField] private Vector2 detailsScroll;
        private HairBakeOutcome lastBake;
        [SerializeField] private HairAtlasEditorPanel atlasEditor = new HairAtlasEditorPanel();
        [SerializeField] private bool cardGeometryExpanded;
        [SerializeField] private bool childSettingsExpanded;
        [SerializeField] private bool atlasMapsExpanded;
        [SerializeField] private string guideSearch = string.Empty;
        [SerializeField] private int guidePage;
        [SerializeField] private bool guideLibraryExpanded;
        [SerializeField] private bool authoredGuidesExpanded;
        private double nextPreferencesSave;
        private string preferenceContext;
        private HairGroomAsset preferenceGroom;

        [MenuItem("UMA/Hair Cards/Hair Groom Workspace", priority = 210)]
        public static void OpenForActiveStage()
        {
            HairGroomNodeWindow.Open();
            HairGroomPreviewWindow.Open();
            OpenProperties();
        }

        [MenuItem("UMA/Hair Cards/Hair Properties", priority = 212)]
        public static void OpenProperties()
        {
            HairGroomWorkspace window = GetWindow<HairGroomWorkspace>();
            window.titleContent = new GUIContent("Hair Properties", EditorGUIUtility.IconContent("SettingsIcon").image);
            window.minSize = new Vector2(480f, 400f);
            if (!window.initialPlacementDone) { window.position = new Rect(420f, 80f, 640f, 740f); window.initialPlacementDone = true; }
            window.Show();
        }

        public static void RepaintOpenWindows()
        {
            HairGroomWorkspace[] windows = Resources.FindObjectsOfTypeAll<HairGroomWorkspace>();
            for (int i = 0; i < windows.Length; i++) windows[i].Repaint();
            HairGroomNodeWindow.RepaintOpenWindows();
            HairGroomPreviewWindow.RepaintOpenWindows();
        }

        internal static void SaveOpenPreferences()
        {
            if (HairEditorPreferences.Suspended) return;
            foreach (HairGroomWorkspace window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>())
            {
                HairEditorPreferences.instance.Remember(window, "workspace", window.preferenceContext);
                window.atlasEditor?.SavePreferences();
            }
            HairGroomNodeWindow.SaveOpenPreferences();
            HairGroomPreviewWindow.SaveOpenPreferences();
        }

        internal static void ResetOpenPreferences()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>())
            {
                bool placed = window.initialPlacementDone;
                HairPreferenceCodec.Reset(window);
                window.initialPlacementDone = placed;
                window.atlasEditor?.ResetPreferences();
                window.Repaint();
            }
        }

        internal static void CancelOpenAtlasInteractions()
        { foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>()) window.atlasEditor?.CancelInteraction(); }

        internal static void SelectUvSet(HairAtlasProfileAsset atlas, string id)
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage?.ActiveGroup != null && stage.ActiveGroup.atlas == atlas)
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Atlas, stage.ActiveGroup.Id));
            foreach (HairGroomWorkspace window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>())
            { window.atlasEditor?.SelectSet(atlas, id); window.Repaint(); }
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Hair Properties");
            minSize = new Vector2(480f, 400f);
            if (HairEditorPreferences.Suspended) return;
            preferenceGroom = HairCardStage.ActiveStage?.Groom;
            preferenceContext = HairEditorPreferences.Context(preferenceGroom);
            HairEditorPreferences.instance.Restore(this, "workspace", preferenceContext);
            EditorApplication.update += SaveIdlePreferences;
            Undo.undoRedoPerformed += OnAtlasUndoRedo;
        }

        private void SaveIdlePreferences()
        {
            if (HairEditorPreferences.Suspended || HairCardStage.ActiveStage?.IsEditing == true ||
                EditorApplication.timeSinceStartup < nextPreferencesSave) return;
            nextPreferencesSave = EditorApplication.timeSinceStartup + 2d;
            HairEditorPreferences.instance.Remember(this, "workspace", preferenceContext);
        }

        private void OnAtlasUndoRedo()
        {
            atlasEditor?.CancelInteraction();
            Repaint();
        }

        private void OnDisable()
        {
            EditorApplication.update -= SaveIdlePreferences;
            if (!HairEditorPreferences.Suspended) HairEditorPreferences.instance.Remember(this, "workspace", preferenceContext);
            Undo.undoRedoPerformed -= OnAtlasUndoRedo;
            atlasEditor?.Dispose();
            if (!HairEditorPreferences.Suspended)
            {
                HairCardStage.ActiveStage?.SetGravityHeld(false);
                HairCardStage.ActiveStage?.SetWorkspaceInteraction(false);
            }
        }

        private void OnLostFocus()
        {
            atlasEditor?.CancelInteraction();
            HairCardStage.ActiveStage?.SetGravityHeld(false);
            HairCardStage.ActiveStage?.SetWorkspaceInteraction(false);
        }

        private void OnGUI()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            HairGroomAsset nextGroom = stage?.Groom;
            if (nextGroom != preferenceGroom)
            {
                HairEditorPreferences.instance.Remember(this, "workspace", preferenceContext);
                preferenceGroom = nextGroom;
                preferenceContext = HairEditorPreferences.Context(nextGroom);
                HairEditorPreferences.instance.Restore(this, "workspace", preferenceContext);
                atlasEditor?.CancelInteraction();
            }
            if (stage == null || stage.WorkflowStep != HairWorkflowStep.Cards)
                atlasEditor?.CancelInteraction();
            if (stage == null || stage.Groom == null)
            {
                DrawNoStage();
                return;
            }

            DrawNodeInspector(stage);
            Event current = Event.current;
            stage.SetWorkspaceInteraction(GUIUtility.hotControl != 0 || EditorGUIUtility.editingTextField ||
                atlasEditor?.IsInteracting == true, current != null &&
                (current.rawType == EventType.MouseDown || current.rawType == EventType.MouseDrag || current.rawType == EventType.KeyDown));
        }

        private static void DrawNoStage()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Hair Properties", CenteredTitle());
            EditorGUILayout.LabelField("Open a groom using Hair Nodes, then select a node in its tree to edit properties here.",
                CenteredWrapped());
            GUILayout.FlexibleSpace();
        }


        internal static void OpenQuickStart()
        {
            TextAsset guide = AssetDatabase.LoadAssetAtPath<TextAsset>(QuickStartPath);
            if (guide != null)
            {
                AssetDatabase.OpenAsset(guide);
                return;
            }
            EditorUtility.DisplayDialog("Hair Cards Quick Start",
                $"The quick-start guide was not found at '{QuickStartPath}'.", "OK");
        }


        private static void DrawSetup(HairCardStage stage)
        {
            HairGroomAsset groom = stage.Groom;
            DrawStepTitle("1. Setup", "Bind the groom to a source scalp and establish preview behavior.");
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Source Mesh", groom.SourceMesh, typeof(Mesh), false);
            EditorGUILayout.TextField("Stable Source ID", groom.SourceMeshId);
            EditorGUILayout.TextField("Topology Signature", groom.SourceTopologySignature);
            EditorGUILayout.TextField("Race", groom.SourceRace);
            EditorGUILayout.TextField("Slot", groom.SourceSlot);
            EditorGUI.EndDisabledGroup();
            MessageType topologyType = groom.SourceTopologyMatches() ? MessageType.Info : MessageType.Error;
            EditorGUILayout.HelpBox(groom.SourceTopologyMatches()
                ? "Surface binding topology matches. Roots can be evaluated exactly."
                : "Source topology changed. Rebind or restore the source mesh before baking.", topologyType);
            if (GUILayout.Button("Reproject All Guide Roots"))
            {
                int repaired = HairGroomCommands.ReprojectAllRoots(groom);
                Debug.Log($"[UMA Hair Cards] Reprojected {repaired} guide roots for '{groom.name}'.");
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Symmetry", EditorStyles.boldLabel);
            stage.MirrorPaintX = EditorGUILayout.Toggle("Mirror growth paint X", stage.MirrorPaintX);
            stage.MirrorCutX = EditorGUILayout.Toggle("Mirror slice cut X", stage.MirrorCutX);
            EditorGUILayout.HelpBox("These are the active, stage-only mirror controls across source-local X = 0. " +
                "Comb/Grab do not have a global symmetry switch. Use a Mirror modifier for procedural guide mirroring.", MessageType.Info);

        }

        private void DrawGrowth(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("2. Growth", "Paint one Growth / Density map: 0 = no growth, 0.5 = half density, 1 = full density.");
            DrawGrowthMapRow(stage, stage.ActiveMap);
            if (HasPaintedDensityMultiplier(group))
                EditorGUILayout.HelpBox("The optional Density Multiplier also reduces growth. Select its node under Optional Maps to edit or reset it.", MessageType.Info);
            if (stage.ActiveMap?.kind == HairMapKind.Density)
                using (new EditorGUI.DisabledScope(stage.ActiveMap.locked))
                    if (GUILayout.Button("Reset Density Multiplier to 1"))
                        HairGroomCommands.FillMap(stage.Groom, stage.ActiveMap, 1f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.PaintGrowth, "Paint Active Map", "Button",
                        GUILayout.Height(30f))) stage.SceneTool = HairSceneTool.PaintGrowth;
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.Select, "Select Vertices", "Button",
                        GUILayout.Height(30f))) stage.SceneTool = HairSceneTool.Select;
                bool erase = GUILayout.Toggle(stage.EffectivePaintErase, new GUIContent("Erase",
                    "Select Erase, or hold Shift in the Scene view to erase temporarily."), "Button", GUILayout.Width(60f), GUILayout.Height(30f));
                if (erase != stage.EffectivePaintErase) stage.PaintErase = erase;
                stage.MirrorPaintX = GUILayout.Toggle(stage.MirrorPaintX, "Mirror X", "Button",
                    GUILayout.Width(76f), GUILayout.Height(30f));
            }
            stage.BrushRadius = EditorGUILayout.Slider("Brush Radius", stage.BrushRadius,
                HairBrushInteractionUtility.MinimumRadius, HairBrushInteractionUtility.MaximumRadius);
            stage.BrushHardness = EditorGUILayout.Slider(new GUIContent("Hardness",
                "Matches Overlay Painter: values inside this fraction of the radius receive full strength, then fall off linearly to zero at the outer ring."),
                stage.BrushHardness, 0f, 1f);
            stage.BrushStrength = EditorGUILayout.Slider("Strength", stage.BrushStrength, 0.01f, 1f);
            bool paintingDensity = stage.ActiveMap?.kind == HairMapKind.GrowthArea || stage.ActiveMap?.kind == HairMapKind.Density;
            stage.PaintValue = paintingDensity ? EditorGUILayout.Slider("Paint Value", stage.PaintValue, 0f, 1f)
                : EditorGUILayout.FloatField("Paint Value", stage.PaintValue);
            EditorGUILayout.HelpBox(
                "Mirror X paints both sides across the source mesh local X = 0 plane (M toggles it). " +
                "Hold Shift while painting to erase temporarily; release it to restore the selected mode. " +
                "Shift + right-drag: horizontal changes radius, vertical changes hardness. [ and ] adjust radius; Shift + [ and ] adjust hardness.",
                MessageType.None);

            HairGrowthMap activeMap = stage.ActiveMap;
            if (activeMap != null)
            {
                EditorGUILayout.Space(7f);
                EditorGUILayout.LabelField(activeMap.DisplayName + " Operations", EditorStyles.boldLabel);
                DrawMapClipboard(stage, group, activeMap);
                using (new EditorGUI.DisabledScope(activeMap.locked))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Visible 0")) stage.FillVisibleActiveMap(activeMap.valueRange.x);
                    if (GUILayout.Button("Visible 1")) stage.FillVisibleActiveMap(activeMap.valueRange.y);
                    if (GUILayout.Button("Invert")) HairGroomCommands.InvertMap(stage.Groom, activeMap);
                    if (GUILayout.Button("Smooth")) HairGroomCommands.SmoothMap(stage.Groom, activeMap, 2);
                }
                EditorGUILayout.LabelField(
                    "Visible operations affect only slots currently shown in Avatar Visibility. This is the safe way to initialize a scalp on a combined character mesh.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(activeMap.locked))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Fill Entire Source 0"))
                        HairGroomCommands.FillMap(stage.Groom, activeMap, activeMap.valueRange.x);
                    if (GUILayout.Button("Fill Entire Source 1"))
                        HairGroomCommands.FillMap(stage.Groom, activeMap, activeMap.valueRange.y);
                }
                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField($"Vertex Selection ({stage.SelectedVertexCount:N0})", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Click a triangle to replace, Shift-drag to add, Ctrl/Cmd-drag to subtract.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Map → Selection")) stage.SelectFromActiveMap();
                    if (GUILayout.Button("Selection → Map")) stage.ApplySelectionToActiveMap(activeMap.valueRange.y);
                    if (GUILayout.Button("Erase Selected")) stage.ApplySelectionToActiveMap(activeMap.valueRange.x);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Grow")) stage.GrowVertexSelection(false);
                    if (GUILayout.Button("Shrink")) stage.GrowVertexSelection(true);
                    if (GUILayout.Button("Invert")) stage.InvertVertexSelection();
                    if (GUILayout.Button("Clear")) stage.ClearVertexSelection();
                }
            }
            if (stage.PaintableTriangleCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "There are no paintable triangles. Show at least one source slot in Avatar Visibility, then validate the groom source topology.",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Paint in the Scene view ({stage.PaintableTriangleCount:N0} paintable triangles): move over the visible surface until the cyan brush ring appears, then left-drag to paint. Alt-drag continues to orbit the camera. The blue-to-orange overlay shows strength; orange is stronger.",
                    MessageType.Info);
            }
        }

        internal static bool HasPaintedDensityMultiplier(HairGroup group)
        {
            HairGrowthMap multiplier = group?.FindMap(HairMapKind.Density);
            if (multiplier == null) return false;
            if (multiplier.values == null || multiplier.values.Length == 0) return multiplier.defaultValue != 1f;
            foreach (float value in multiplier.values) if (value != 1f) return true;
            return false;
        }

        private static void DrawGrowthMapRow(HairCardStage stage, HairGrowthMap map)
        {
            if (map == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(map.DisplayName, EditorStyles.boldLabel);
                bool visible = GUILayout.Toggle(map.visible, new GUIContent("V", "Show this map's paint overlay; does not disable its effect"), "Button", GUILayout.Width(25f));
                bool locked = GUILayout.Toggle(map.locked, new GUIContent("L", "Lock painting of this map"), "Button", GUILayout.Width(25f));
                if (visible != map.visible || locked != map.locked)
                {
                    Undo.RecordObject(stage.Groom, "Change Growth Map State");
                    map.visible = visible; map.locked = locked;
                    HairGroomCommands.Commit(stage.Groom);
                }
            }
        }

        private void DrawGuides(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("3. Guides", "Place guides by hand or preview density-controlled generation from Growth / Density.");

            stage.GetGrowthAreaStatistics(out int growthVertices, out int sourceVertices, out float growthMaximum);
            if (growthVertices == 0)
            {
                EditorGUILayout.HelpBox(
                    "No non-zero Growth / Density exists for this group. Select Growth / Density in Hair Nodes to paint or initialize the scalp region first.",
                    MessageType.Error);
            }
            else
            {
                float coverage = sourceVertices > 0 ? growthVertices / (float)sourceVertices : 0f;
                EditorGUILayout.HelpBox(
                    $"Painted region ready: {growthVertices:N0} source vertices ({coverage:P1}), maximum density {growthMaximum:0.###}.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField("Manual Guide Tools", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.PlaceGuide, "Place Guide", "Button", GUILayout.Height(30f)))
                    stage.SceneTool = HairSceneTool.PlaceGuide;
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.DrawGuide, "Draw Guide", "Button", GUILayout.Height(30f)))
                    stage.SceneTool = HairSceneTool.DrawGuide;
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.Select, "Select / Edit", "Button", GUILayout.Height(30f)))
                    stage.SceneTool = HairSceneTool.Select;
            }

            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField("Automatic Guide Generation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "1. Preview creates temporary dashed guides using painted density. 2. Accept creates editable guides; Replace Generated Only refreshes previous generated guides instead of adding more.",
                EditorStyles.wordWrappedLabel);
            HairGuideGenerationSettings settings = stage.GuideGeneration;
            EditorGUI.BeginChangeCheck();
            settings.guideCount = EditorGUILayout.IntSlider(new GUIContent("Guides at Full Density",
                "Budget if the current painted footprint were density 1. The surface-area-weighted average of Growth / Density × the optional multiplier reduces this count. Unpainted body triangles do not dilute it."), settings.guideCount, 1, 1000);
            settings.pointsPerGuide = EditorGUILayout.IntSlider("Points per Guide", settings.pointsPerGuide, 2, 24);
            settings.defaultLength = EditorGUILayout.Slider("Default Length", settings.defaultLength, 0.01f, 1f);
            settings.minimumRootSpacing = EditorGUILayout.Slider("Minimum Spacing", settings.minimumRootSpacing, 0f, 0.2f);
            settings.rootUniformity = EditorGUILayout.Slider(new GUIContent("Root Uniformity",
                "0: original random placement. 1: compare more candidates to fill gaps and spread roots evenly. " +
                "Respects Growth / Density, its optional multiplier, and Minimum Spacing; higher values take longer to preview."), settings.rootUniformity, 0f, 1f);
            settings.surfaceFlow = EditorGUILayout.Slider("Follow Surface Flow", settings.surfaceFlow, 0f, 1f);
            settings.lift = EditorGUILayout.Slider("Lift", settings.lift, 0f, 1f);
            settings.seed = EditorGUILayout.IntField("Seed", settings.seed);
            if (EditorGUI.EndChangeCheck() && stage.GenerationPreview != null) stage.CancelGuidePreview();
            EditorGUILayout.LabelField("For even coverage, start with Root Uniformity 0.75–1 and low Minimum Spacing. " +
                "Preview again after changing settings; accepted guides are not moved automatically.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox("Example: a budget of 100 with uniform paint at 0.5 targets 50 guides; at 1 it targets 100. Soft edges lower the average too. The budget covers the current painted footprint, not a fixed number per square meter. Minimum Spacing can reduce the result further.", MessageType.None);
            using (new EditorGUI.DisabledScope(growthVertices == 0 || group.locked))
            {
                if (GUILayout.Button("1. Preview Density-Adjusted Guides", GUILayout.Height(32f)))
                    stage.GenerateGuidePreview();
            }
            if (group.locked)
                EditorGUILayout.HelpBox("Unlock the active group before accepting or manually adding guides.",
                    MessageType.Warning);

            HairGuideGenerationResult preview = stage.GenerationPreview;
            if (preview != null)
            {
                EditorGUILayout.HelpBox(
                    $"Full-density budget {preview.fullDensityGuideCount:N0} × average painted density {preview.averagePaintedDensity:P1} → target {preview.densityAdjustedGuideCount:N0}.\n" +
                    $"Dashed preview: {preview.guides.Count:N0} placed. {preview.rejectedBySpacing:N0} spacing rejections, {preview.rejectedByMask:N0} mask rejections. Authored guides are unchanged until you accept.",
                    preview.guides.Count > 0 ? MessageType.Info : MessageType.Warning);
                for (int i = 0; i < preview.warnings.Count; i++) EditorGUILayout.HelpBox(preview.warnings[i], MessageType.Warning);
                using (new EditorGUI.DisabledScope(preview.guides.Count == 0 || group.locked))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"2. Accept {preview.guides.Count:N0} as Guides", GUILayout.Height(30f)))
                        stage.AcceptGuidePreview();
                    if (GUILayout.Button("Replace Generated Only", GUILayout.Height(30f)))
                        stage.AcceptGuidePreview(true);
                }
                if (GUILayout.Button("Cancel Preview")) stage.CancelGuidePreview();
            }

            EditorGUILayout.Space(8f);
            DrawGuideLibrary(stage, true);
        }

        private void DrawGuideLibrary(HairCardStage stage, bool collapsible = false)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            string heading = $"Authored Guides · {stage.SelectedGuideIds.Count:N0} selected";
            if (collapsible)
            {
                authoredGuidesExpanded = EditorGUILayout.Foldout(authoredGuidesExpanded, heading, true, EditorStyles.foldoutHeader);
                if (!authoredGuidesExpanded) return;
            }
            else EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            DrawGuideRemovalActions(stage);
            string search = EditorGUILayout.TextField("Search guides", guideSearch, EditorStyles.toolbarSearchField);
            if (search != guideSearch) { guideSearch = search; guidePage = 0; }
            List<HairGuide> matches = group.guides.FindAll(guide => guide != null &&
                (string.IsNullOrWhiteSpace(guideSearch) || guide.name.IndexOf(guideSearch, StringComparison.OrdinalIgnoreCase) >= 0));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select matching")) stage.SetGuideSelection(matches.ConvertAll(guide => guide.Id));
                if (GUILayout.Button("Clear selection")) stage.SetGuideSelection(null);
                stage.IsolateSelectedGuides = GUILayout.Toggle(stage.IsolateSelectedGuides, "Isolate", "Button");
            }
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(matches.Count / 48f));
            guidePage = Mathf.Clamp(guidePage, 0, pageCount - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(guidePage == 0)) if (GUILayout.Button("‹", GUILayout.Width(30f))) guidePage--;
                GUILayout.Label($"{matches.Count:N0} matches · Page {guidePage + 1} / {pageCount}", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(guidePage + 1 >= pageCount)) if (GUILayout.Button("›", GUILayout.Width(30f))) guidePage++;
            }
            for (int i = guidePage * 48; i < Mathf.Min(matches.Count, (guidePage + 1) * 48); i++)
            {
                HairGuide guide = matches[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = stage.IsGuideSelected(guide.Id);
                    if (EditorGUILayout.Toggle(selected, GUILayout.Width(18f)) != selected) stage.ToggleGuideSelection(guide.Id);
                    if (GUILayout.Button(guide.name + (guide.enabled ? "" : " (disabled)"),
                        guide.Id == stage.ActiveGuideId ? EditorStyles.boldLabel : EditorStyles.label))
                    {
                        if (Event.current.shift || Event.current.control || Event.current.command) stage.ToggleGuideSelection(guide.Id);
                        else stage.SetActiveGuide(guide.Id);
                    }
                    GUILayout.Label($"{guide.points.Count} pts", EditorStyles.miniLabel, GUILayout.Width(42f));
                }
            }
            using (new EditorGUI.DisabledScope(group.locked || stage.SelectedGuideIds.Count == 0))
            {
                HairGuide active = group.guides.Find(guide => guide != null && guide.Id == stage.ActiveGuideId);
                if (active != null)
                {
                    string name = EditorGUILayout.DelayedTextField("Active guide name", active.name);
                    if (name != active.name) { Undo.RecordObject(stage.Groom, "Rename Hair Guide"); active.name = name; HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Display); }
                }
                using (new EditorGUILayout.HorizontalScope())
                    foreach (HairGuideBatchAction action in new[] { HairGuideBatchAction.Enable, HairGuideBatchAction.Disable, HairGuideBatchAction.Freeze, HairGuideBatchAction.Unfreeze })
                        if (GUILayout.Button(action.ToString())) HairGroomCommands.ApplyGuideBatch(stage.Groom, group, stage.SelectedGuideIds, action);
                if (GUILayout.Button("Duplicate selected")) stage.SetGuideSelection(HairGroomCommands.ApplyGuideBatch(stage.Groom, group, stage.SelectedGuideIds, HairGuideBatchAction.Duplicate));
            }
            if (stage.IsolateSelectedGuides && stage.SelectedGuideIds.Count == 0)
                EditorGUILayout.HelpBox("Isolation is empty. Select matching guides or turn off Isolate to see the groom.", MessageType.Info);
        }

        private static void DrawGuideRemovalActions(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(group.locked || stage.SelectedGuideIds.Count == 0))
                    if (GUILayout.Button(new GUIContent("Delete selected", "Immediately delete selected guides and their sculpt deltas. Undo restores them.")))
                        stage.DeleteGuides();
                using (new EditorGUI.DisabledScope(group.locked || group.guides.Count == 0))
                    if (GUILayout.Button(new GUIContent("Remove all…", "Remove every authored guide in the current group, including disabled guides and guides hidden by search or isolation. Requires confirmation; Undo supported.")))
                        ConfirmRemoveAllGuides(stage, EditorUtility.DisplayDialog);
            }
            if (group.locked) EditorGUILayout.LabelField("Unlock the active group to remove guides.", EditorStyles.wordWrappedMiniLabel);
        }

        internal static int ConfirmRemoveAllGuides(HairCardStage stage, Func<string, string, string, string, bool> confirm)
        {
            HairGroup group = stage?.ActiveGroup;
            if (group == null || group.locked || group.guides.Count == 0 || confirm == null) return 0;
            HairGroomAsset targetGroom = stage.Groom;
            if (!confirm("Remove All Hair Guides",
                $"Remove all {group.guides.Count:N0} guides from '{group.name}'?\n\nThis includes disabled guides and guides hidden by search or isolation, their sculpt deltas, and generated child cards. Other groups, paint maps, layers, modifiers, and card settings are kept.\n\nUndo restores the removed guides and sculpting.",
                "Remove all guides", "Cancel")) return 0;
            // Never apply a modal confirmation to a different group if the context changed while it was open.
            return stage.Groom == targetGroom && stage.ActiveGroup == group ? stage.DeleteGuides(true) : 0;
        }

        private static void DrawMapClipboard(HairCardStage stage, HairGroup group, HairGrowthMap map)
        {
            mapClipboard ??= new HairGrowthMapClipboard(SessionState.GetString(MapClipboardKey, string.Empty));
            using (new EditorGUILayout.HorizontalScope())
            {
                bool canCopy = mapClipboard.CanCopy(stage.Groom, group, map, false, out string copyReason);
                using (new EditorGUI.DisabledScope(!canCopy))
                    if (GUILayout.Button(new GUIContent("Copy", copyReason ?? "Copy all vertex values from this map, including hidden slots.")))
                        if (mapClipboard.TryCopy(stage.Groom, group, map, false, out mapClipboardStatus))
                            SessionState.SetString(MapClipboardKey, mapClipboard.Serialize());
                bool canCut = mapClipboard.CanCopy(stage.Groom, group, map, true, out string cutReason);
                using (new EditorGUI.DisabledScope(!canCut))
                    if (GUILayout.Button(new GUIContent("Cut", cutReason ?? "Copy all values, then reset this map to its default value. Undo restores the source.")))
                        if (mapClipboard.TryCopy(stage.Groom, group, map, true, out mapClipboardStatus))
                            SessionState.SetString(MapClipboardKey, mapClipboard.Serialize());
                bool canPaste = mapClipboard.CanPaste(stage.Groom, group, map, out string pasteReason);
                using (new EditorGUI.DisabledScope(!canPaste))
                    if (GUILayout.Button(new GUIContent("Paste", pasteReason ?? "Replace this map's values with the clipboard, clamped to this map's range. Undo supported.")))
                        mapClipboard.TryPaste(stage.Groom, group, map, out mapClipboardStatus);
            }
            EditorGUILayout.LabelField(mapClipboard.Description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Copy/Cut/Paste affect the entire map, including hidden slots. Paste keeps the destination name and type.", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(mapClipboardStatus))
                EditorGUILayout.LabelField(mapClipboardStatus, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawGroom(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("4. Groom", "Sculpt authored guides on non-destructive layers, then refine with ordered modifiers and helpers.");
            EditorGUILayout.LabelField(
                "After generating cards, Groom shows the updated card preview after each stroke or Gravity hold. " +
                "During a stroke, guides stay visible for editing. Use Hair Preview & Settings for a guide-only view.",
                EditorStyles.wordWrappedMiniLabel);
            if (group.guides.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "This group has no authored guides to style. Select Guides in Hair Nodes to generate and accept guides, or place them manually.",
                    MessageType.Error);
                return;
            }
            bool sculptWritable = stage.CanEditLayer && (stage.IsLayerEditing || !stage.HasDownstreamSculptOperations);
            if (!sculptWritable && !(stage.SceneTool == HairSceneTool.Erase && stage.CanEraseGuides))
                EditorGUILayout.HelpBox("Sculpting is paused at the final preview. In Hair Nodes, choose Edit This Layer to change its input, or Add Finishing Sculpt Layer to refine the visible result. Locked/inactive layers cannot be sculpted.", MessageType.Info);
            EditorGUILayout.LabelField("Essential Brush Shelf", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Editing: {group.name} / {group.sculptLayers.Find(layer => layer != null && layer.Id == stage.ActiveLayerId)?.name ?? "New sculpt layer"}", EditorStyles.wordWrappedLabel);
            stage.BrushScope = (HairBrushScope)EditorGUILayout.EnumPopup("Edit scope", stage.BrushScope);
            EditorGUILayout.LabelField($"{stage.AffectedGuideCount:N0} editable guides under brush · {stage.SelectedGuideIds.Count:N0} selected", EditorStyles.miniLabel);
            if (stage.BrushScope == HairBrushScope.VisibleHair)
                EditorGUILayout.LabelField("Visible hair excludes points behind the visible source surface; guide display depth is independent.", EditorStyles.wordWrappedMiniLabel);
            guideLibraryExpanded = EditorGUILayout.Foldout(guideLibraryExpanded, "Guide selection & isolation", true);
            if (guideLibraryExpanded) DrawGuideLibrary(stage);
            HairSceneTool[] tools =
            {
                HairSceneTool.Comb, HairSceneTool.Grab, HairSceneTool.Smooth, HairSceneTool.Length,
                HairSceneTool.Cut, HairSceneTool.Width, HairSceneTool.Clump, HairSceneTool.Part, HairSceneTool.Freeze
            };
            for (int row = 0; row < 3; row++)
            {
                using var toolLock = new EditorGUI.DisabledScope(!sculptWritable);
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < 3; column++)
                    {
                        int index = row * 3 + column;
                        HairSceneTool tool = tools[index];
                        if (GUILayout.Toggle(stage.SceneTool == tool, ObjectNames.NicifyVariableName(tool.ToString()),
                                "Button", GUILayout.Height(27f))) stage.SceneTool = tool;
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!stage.CanEraseGuides))
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.Erase, new GUIContent("Erase",
                    "Delete whole guides touched by the brush, including their children and sculpt data. Frozen guides are protected. Undo restores the stroke."),
                    "Button", GUILayout.Height(27f))) stage.SceneTool = HairSceneTool.Erase;
            if (stage.SceneTool == HairSceneTool.Cut)
            {
                stage.MirrorCutX = EditorGUILayout.ToggleLeft(new GUIContent("Mirror Slice Across X",
                    "Applies the same slice to the opposite side across the source mesh local X = 0 plane."),
                    stage.MirrorCutX);
                EditorGUILayout.HelpBox(
                    "Drag a line across the Scene view. The camera and drag line form a finite slice plane; " +
                    "each intersected guide is cut at its first root-to-tip crossing and everything beyond it is removed. " +
                    "Press M to toggle mirroring.", MessageType.Info);
            }
            else if (stage.SceneTool == HairSceneTool.Erase)
            {
                stage.BrushRadius = EditorGUILayout.Slider("Radius", stage.BrushRadius,
                    HairBrushInteractionUtility.MinimumRadius, HairBrushInteractionUtility.MaximumRadius);
                EditorGUILayout.HelpBox("Erase deletes whole guides touched anywhere along their displayed curves, plus their children and sculpt data. " +
                    "This changes the group's authored guides, not just this sculpt layer. Any frozen point protects its entire guide. " +
                    "Radius and Edit scope control deletion; Hardness, Strength and Reverse do not apply. Undo restores the entire stroke.", MessageType.Warning);
                EditorGUILayout.LabelField("Left-drag: erase · [ / ] or Shift + right-drag: radius · Ctrl/Cmd + Z: undo stroke", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                stage.BrushRadius = EditorGUILayout.Slider("Radius", stage.BrushRadius,
                    HairBrushInteractionUtility.MinimumRadius, HairBrushInteractionUtility.MaximumRadius);
                stage.BrushHardness = EditorGUILayout.Slider(new GUIContent("Hardness",
                    "Full-strength inner radius followed by a linear falloff to the outer brush ring."),
                    stage.BrushHardness, 0f, 1f);
                stage.BrushStrength = EditorGUILayout.Slider("Strength", stage.BrushStrength, 0.01f, 1f);
                stage.PaintErase = EditorGUILayout.Toggle(stage.SceneTool == HairSceneTool.Length ?
                    "Shorten (instead of lengthen)" : stage.SceneTool == HairSceneTool.Freeze ? "Unfreeze" : "Reverse / Erase", stage.PaintErase);
                if (stage.SceneTool == HairSceneTool.Length)
                    EditorGUILayout.HelpBox("Length intentionally changes segment lengths. Roots and fully frozen points stay fixed; " +
                        "turn on Shorten to reduce length. These edits are stored on the sculpt layer.", MessageType.Info);
                if (stage.SceneTool == HairSceneTool.Freeze || stage.ShowFreezeMask)
                    EditorGUILayout.LabelField("Freeze mask: cyan = editable, pink = frozen. Roots are always anchored.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.HelpBox(
                    "Comb, Grab, Smooth, Clump, and Part preserve every guide segment. Length and Cut intentionally change length. " +
                    "Through Depth reaches overlapping projected guides. Visible Hair and Selected Guides Only narrow the scope. " +
                    "Shift + right-drag adjusts radius/hardness; [ and ] adjust radius; Shift + [ and ] adjust hardness.",
                    MessageType.None);
            }
            if (stage.SceneTool != HairSceneTool.Erase)
            {
                stage.BrushRootInfluence = EditorGUILayout.Slider(new GUIContent("Root Influence",
                    "Bending multiplier near the base for Comb, Grab, Smooth, Clump, Part and Gravity. 0 adds root protection; 1 keeps the tool's normal response. Fades to 1 at the tip. The attachment stays pinned. Length, Cut, Width and Freeze are unaffected."),
                    stage.BrushRootInfluence, 0f, 1f);
                EditorGUILayout.LabelField("Root stays fixed. Lower values protect the base; 1 keeps the normal tool response. Tips are unaffected.", EditorStyles.wordWrappedMiniLabel);
            }
            if (GUILayout.Button(new GUIContent("Repair Existing Stretch (New Layer)",
                    "Restores authored segment lengths while keeping current directions. Intentional Length-tool changes are also normalized. The correction is written to a new undoable layer."),
                GUILayout.Height(26f)))
                stage.RepairActiveGroupLengths();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Gravity Settle", EditorStyles.boldLabel);
            stage.GravityStrength = EditorGUILayout.Slider(new GUIContent("Strength",
                "How quickly guide segments rotate toward gravity while the button is held."),
                stage.GravityStrength, 0.1f, 8f);
            stage.GravitySeparation = EditorGUILayout.Slider(new GUIContent("Card Separation",
                "Fans guides along their scalp normal with a small stable per-guide variation so cards do not collapse into one sheet."),
                stage.GravitySeparation, 0f, 1f);
            stage.GravityCollision = EditorGUILayout.ToggleLeft("Collide with scalp / body", stage.GravityCollision);
            if (stage.GravityCollision)
            {
                stage.GravityClearance = EditorGUILayout.Slider("Surface clearance", stage.GravityClearance, 0f, 0.05f);
                EditorGUILayout.LabelField("Uses the full source body in its authoring pose, including hidden slots. Requires outward-facing surface normals. Roots and frozen anchors take priority over clearance.", EditorStyles.wordWrappedMiniLabel);
            }
            Color oldBackground = GUI.backgroundColor;
            if (stage.GravitySimulationActive) GUI.backgroundColor = new Color(0.45f, 0.85f, 1f);
            bool gravityHeld;
            using (new EditorGUI.DisabledScope(!sculptWritable))
            gravityHeld = GUILayout.RepeatButton(new GUIContent(
                    stage.GravitySimulationActive ? "Settling… release to stop" : "Hold to Apply Gravity",
                    "Press and hold to relax the active group's guides under gravity. Roots and guide lengths remain locked."),
                GUILayout.Height(36f));
            GUI.backgroundColor = oldBackground;
            if (gravityHeld)
            {
                stage.SetGravityHeld(true);
                Repaint();
            }
            Event current = Event.current;
            if (stage.GravitySimulationActive && !gravityHeld && current != null &&
                (current.rawType == EventType.MouseUp || current.type == EventType.MouseLeaveWindow ||
                 (current.type == EventType.Repaint && GUIUtility.hotControl == 0)))
                stage.SetGravityHeld(false);
            EditorGUILayout.HelpBox(
                "Gravity is non-destructive on the active sculpt layer. Hold briefly to relax tips; hold longer for a full settle. " +
                "Undo restores the complete hold as one operation.", MessageType.Info);

            EditorGUILayout.Space(8f);
        }

        private void DrawCards(HairCardStage stage, HairGroomNodeKind section = HairGroomNodeKind.Cards)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("5. Cards", "Assign a texture and material, define UV sets directly below, then preview the generated cards.");
            if (group.guides.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "You can configure card geometry, textures, and UV sets now. Add authored guides to see generated cards in the Scene view.",
                    MessageType.Info);
            }
            HairCardProfileAsset profile = (HairCardProfileAsset)EditorGUILayout.ObjectField("Card Profile", group.profile,
                typeof(HairCardProfileAsset), false);
            HairAtlasProfileAsset atlas = (HairAtlasProfileAsset)EditorGUILayout.ObjectField("Atlas Profile", group.atlas,
                typeof(HairAtlasProfileAsset), false);
            using (new EditorGUI.DisabledScope(group.locked))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(group.profile == null))
                    if (GUILayout.Button(new GUIContent("Make profile unique", "Save a private copy next to this groom; other groups retain the original profile."))) { HairGroomCommands.MakeResourcesUnique(stage.Groom, group, false); profile = group.profile; }
                using (new EditorGUI.DisabledScope(group.atlas == null))
                    if (GUILayout.Button(new GUIContent("Make atlas unique", "Copy UV sets and texture references for this group. Textures and material remain shared."))) { HairGroomCommands.MakeResourcesUnique(stage.Groom, group, true); atlas = group.atlas; }
            }
            if (profile != group.profile || atlas != group.atlas)
            {
                Undo.RecordObject(stage.Groom, "Assign Hair Card Resources");
                group.profile = profile;
                if (atlas != group.atlas)
                {
                    group.atlasRegionSelection = HairAtlasRegionSelectionMode.All;
                    group.atlasRegionIds.Clear();
                }
                group.atlas = atlas;
                HairGroomCommands.Commit(stage.Groom);
            }
            if (section == HairGroomNodeKind.Atlas) { DrawAtlasSettings(stage, group); return; }
            if (section == HairGroomNodeKind.Cards)
            {
                if (profile == null)
                {
                    EditorGUILayout.HelpBox("A Card Profile is required. Create a ribbon profile or assign an existing profile above.", MessageType.Warning);
                    if (GUILayout.Button("Create Default Ribbon Profile"))
                    { group.profile = HairCardMenu.CreateDefaultProfileNear(stage.Groom); HairGroomCommands.Commit(stage.Groom); }
                }
                if (GUILayout.Button("Build / Refresh Card Preview", GUILayout.Height(30f)))
                { stage.PreviewMode = HairPreviewMode.Cards; stage.QueueRebuild(true); }
                EditorGUILayout.HelpBox("Configure shape in Geometry, then materials and UV sets in Materials & UVs. Resource edits are shared unless you make the profile or atlas unique. Card previews update after grooming strokes.", MessageType.Info);
                return;
            }
            if (profile == null)
            {
                EditorGUILayout.HelpBox("A Card Profile is required for a release bake.", MessageType.Error);
                if (GUILayout.Button("Create Default Ribbon Profile"))
                {
                    group.profile = HairCardMenu.CreateDefaultProfileNear(stage.Groom);
                    HairGroomCommands.Commit(stage.Groom);
                }
            }
            else
            {
                HairCardShape shape = (HairCardShape)EditorGUILayout.EnumPopup("Card Shape", profile.Shape);
                float rootWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Root Width", profile.DefaultWidth));
                float tipWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Tip Width", profile.TipWidth));
                using (new EditorGUI.DisabledScope(group.locked))
                {
                    float embedMm = EditorGUILayout.Slider(new GUIContent("Root Embed (mm)",
                        "Active group only. Tucks guide and child cards inward along their root surface normal, fading over the first 20% of card length. 0 disables; try 1–3 mm. Does not move guides or change their length. Included in preview and bake."),
                        group.rootEmbedDepth * 1000f, 0f, 20f);
                    if (!Mathf.Approximately(embedMm, group.rootEmbedDepth * 1000f))
                    {
                        Undo.RecordObject(stage.Groom, "Embed Hair Card Roots");
                        group.rootEmbedDepth = embedMm * 0.001f;
                        HairGroomCommands.Commit(stage.Groom);
                    }
                }
                EditorGUILayout.LabelField("Width blends along the whole card. Equal root/tip widths make a ribbon; width sculpting and variation apply on top.",
                    EditorStyles.wordWrappedMiniLabel);
                HairLodSettings activeLod = stage.Groom.Lods.Find(item => item.level == stage.LodLevel) ?? stage.Groom.Lods[0];
                bool profileSampling = EditorGUILayout.Toggle(new GUIContent("Use profile sampling",
                    "For the current LOD, use each group's Card Profile sampling instead of a shared LOD override."), activeLod.useProfileSamples);
                if (profileSampling != activeLod.useProfileSamples)
                {
                    Undo.RecordObject(stage.Groom, "Change Hair Sampling Source");
                    activeLod.useProfileSamples = profileSampling;
                    HairGroomCommands.Commit(stage.Groom);
                }
                int samples = profile.SamplesPerCard;
                using (new EditorGUI.DisabledScope(!profileSampling))
                    samples = EditorGUILayout.IntSlider("Profile samples", profile.SamplesPerCard, 2, 64);
                if (!profileSampling)
                {
                    int lodSamples = EditorGUILayout.IntSlider($"LOD {activeLod.level} samples", activeLod.samplesPerCard, 2, 64);
                    if (lodSamples != activeLod.samplesPerCard)
                    {
                        Undo.RecordObject(stage.Groom, "Edit Hair LOD Sampling");
                        activeLod.samplesPerCard = lodSamples;
                        HairGroomCommands.Commit(stage.Groom);
                    }
                }
                EditorGUILayout.LabelField($"Effective sampling: {activeLod.ResolveSampleCount(profile)} points/card (LOD {activeLod.level}).",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Card Profile edits are shared by all groups using this profile; Save includes these edits.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                bool vertexGradient = EditorGUILayout.Toggle(new GUIContent("Root-to-tip vertex colors",
                    "Writes RGB and alpha to the generated mesh's COLOR channel, including children and baked LODs. The material shader must read vertex colors."), profile.UseVertexColorGradient);
                Color rootColor = profile.RootVertexColor, tipColor = profile.TipVertexColor;
                int holdSegments = profile.RootColorSegments;
                using (new EditorGUI.DisabledScope(!vertexGradient))
                {
                    rootColor = EditorGUILayout.ColorField(new GUIContent("Root Vertex Color (RGBA)"), rootColor, true, true, false);
                    tipColor = EditorGUILayout.ColorField(new GUIContent("Tip Vertex Color (RGBA)"), tipColor, true, true, false);
                    holdSegments = EditorGUILayout.IntSlider(new GUIContent("Solid Root Segments",
                        "0 fades immediately. 2 holds rows 0, 1 and 2 at the root RGBA, then fades linearly to the tip. Lower-resolution LODs clamp this to leave at least one fade segment."), holdSegments, 0, 63);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(profile, "Edit Hair Vertex Colors");
                    profile.ConfigureVertexColors(vertexGradient, rootColor, tipColor, holdSegments);
                    EditorUtility.SetDirty(profile); stage.TrackResourceEdit(profile);
                    stage.QueuePreviewChange(HairPreviewChange.Geometry);
                }
                if (vertexGradient)
                {
                    int effectiveSamples = activeLod.ResolveSampleCount(profile);
                    if (stage.PreviewQuality == HairPreviewQuality.Draft) effectiveSamples = Mathf.Min(effectiveSamples, 8);
                    int effectiveHold = Mathf.Min(holdSegments, Mathf.Max(0, effectiveSamples - 2));
                    EditorGUILayout.LabelField($"Current preview: {effectiveHold} solid root segments, then {effectiveSamples - 1 - effectiveHold} fade segments. Alpha is mesh data; shader behavior controls animation/transparency.", EditorStyles.wordWrappedMiniLabel);
                }
                int sides = profile.TubeSides;
                if (shape == HairCardShape.TaperedTube)
                    sides = EditorGUILayout.IntSlider("Tube Sides", profile.TubeSides, 3, 12);
                bool doubleSided = profile.DoubleSided;
                if (shape == HairCardShape.Ribbon)
                    doubleSided = EditorGUILayout.Toggle("Generate Backfaces", profile.DoubleSided);
                if (shape != profile.Shape || !Mathf.Approximately(rootWidth, profile.DefaultWidth) ||
                    !Mathf.Approximately(tipWidth, profile.TipWidth) || samples != profile.SamplesPerCard ||
                    sides != profile.TubeSides || doubleSided != profile.DoubleSided)
                {
                    Undo.RecordObject(profile, "Edit Hair Card Profile");
                    bool samplingChanged = samples != profile.SamplesPerCard;
                    profile.Configure(shape, rootWidth, tipWidth, samples, sides, doubleSided);
                    EditorUtility.SetDirty(profile);
                    stage.TrackResourceEdit(profile);
                    stage.QueuePreviewChange(samplingChanged ? HairPreviewChange.Evaluation : HairPreviewChange.Geometry);
                }
            }

        }

        private void DrawChildren(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            HairChildSettings children = group.children;
            EditorGUILayout.LabelField("Child population & variation", EditorStyles.boldLabel);
            {
                int childCount = EditorGUILayout.IntSlider("Children per Guide", children.childrenPerGuide, 0, 64);
                bool guideCard = EditorGUILayout.Toggle("Include Guide Card", children.includeGuideCard);
                float spread = EditorGUILayout.Slider("Root Spread", children.rootSpread, 0f, 0.2f);
                float clump = EditorGUILayout.Slider("Clump", children.clump, 0f, 1f);
                float lengthVariation = EditorGUILayout.Slider("Length Variation", children.lengthVariation, 0f, 1f);
                float widthVariation = EditorGUILayout.Slider("Width Variation", children.widthVariation, 0f, 1f);
                float rollVariation = EditorGUILayout.Slider("Roll Variation", children.rollVariation, 0f, 1f);
                HairGuideInterpolationMode interpolation = (HairGuideInterpolationMode)EditorGUILayout.EnumPopup(
                    "Interpolation", children.interpolation);
                int seed = EditorGUILayout.IntField("Seed", children.seed);
                if (childCount != children.childrenPerGuide || guideCard != children.includeGuideCard ||
                    !Mathf.Approximately(spread, children.rootSpread) || !Mathf.Approximately(clump, children.clump) ||
                    !Mathf.Approximately(lengthVariation, children.lengthVariation) ||
                    !Mathf.Approximately(widthVariation, children.widthVariation) ||
                    !Mathf.Approximately(rollVariation, children.rollVariation) ||
                    interpolation != children.interpolation || seed != children.seed)
                {
                    Undo.RecordObject(stage.Groom, "Edit Child Hair Settings");
                    children.childrenPerGuide = childCount;
                    children.includeGuideCard = guideCard;
                    children.rootSpread = spread;
                    children.clump = clump;
                    children.lengthVariation = lengthVariation;
                    children.widthVariation = widthVariation;
                    children.rollVariation = rollVariation;
                    children.interpolation = interpolation;
                    children.seed = seed;
                    HairGroomCommands.Commit(stage.Groom);
                }
            }
            int estimate = group.guides.Count * (children.childrenPerGuide + (children.includeGuideCard ? 1 : 0));
            EditorGUILayout.HelpBox($"{group.guides.Count} guides × ({children.childrenPerGuide} children + {(children.includeGuideCard ? "1 guide card" : "no guide card")}) ≈ {estimate:N0} cards.", MessageType.Info);
            if (stage.Evaluation != null)
                EditorGUILayout.LabelField($"Current evaluated output: {stage.Evaluation.CardCount:N0} cards " +
                                           $"({stage.Evaluation.guideCurveCount:N0} guide cards + " +
                                           $"{stage.Evaluation.childCurveCount:N0} children).",
                    EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawAtlasSettings(HairCardStage stage, HairGroup group)
        {
            EditorGUILayout.LabelField("Texture & UV Setup", EditorStyles.boldLabel);
            HairAtlasProfileAsset atlas = group.atlas;
            if (atlas == null)
            {
                EditorGUILayout.HelpBox("Create an Atlas Profile to save named UV sets and their texture/material assignments.",
                    MessageType.Info);
                if (GUILayout.Button("Create Atlas Profile", GUILayout.Height(28f)))
                {
                    Undo.RecordObject(stage.Groom, "Create and Assign Hair Atlas");
                    group.atlas = HairCardMenu.CreateDefaultAtlasNear(stage.Groom);
                    group.atlasRegionSelection = HairAtlasRegionSelectionMode.All;
                    group.atlasRegionIds.Clear();
                    HairGroomCommands.Commit(stage.Groom);
                }
                return;
            }

            atlas.EnsureIntegrity();
            EditorGUI.BeginChangeCheck();
            Texture2D albedo = (Texture2D)EditorGUILayout.ObjectField("Albedo Atlas", atlas.albedo,
                typeof(Texture2D), false);
            Material material = (Material)EditorGUILayout.ObjectField("First Pass Material", atlas.material,
                typeof(Material), false);
            Material secondPassMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Second Pass Material",
                "Optional: draw the same hair geometry again using this material."), atlas.secondPassMaterial, typeof(Material), false);
            EditorGUILayout.LabelField("Second pass is optional. Both passes share the atlas and vertex colors. Shader depth/blend settings and render queues control compositing; use a later queue for the second pass.", EditorStyles.wordWrappedMiniLabel);
            if (material != null && secondPassMaterial != null && secondPassMaterial.renderQueue <= material.renderQueue)
                EditorGUILayout.HelpBox("The second pass's render queue is not later than the first pass. Adjust the material queues to guarantee first-then-second ordering, as with UMAMaterial.", MessageType.Warning);
            SharedColorTable table = (SharedColorTable)EditorGUILayout.ObjectField("Shared Color Table",
                atlas.sharedColorTable as SharedColorTable, typeof(SharedColorTable), false);
            int colorIndex = table != atlas.sharedColorTable ? -1 : atlas.sharedColorIndex;
            if (table != null)
            {
                int count = table.colors?.Length ?? 0;
                string[] choices = new string[count + 1]; choices[0] = "None (do not apply)";
                for (int i = 0; i < count; i++)
                    choices[i + 1] = table.colors[i] == null ? $"{i + 1}. (empty)" :
                        $"{i + 1}. {(string.IsNullOrWhiteSpace(table.colors[i].name) ? "Unnamed color" : table.colors[i].name)}";
                colorIndex = EditorGUILayout.Popup("Shared Color", Mathf.Clamp(colorIndex + 1, 0, count), choices) - 1;
                if (colorIndex >= 0 && table.colors[colorIndex] != null)
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ColorField(new GUIContent("Color swatch"), table.colors[colorIndex].color, true, true, false);
                if (count == 0) EditorGUILayout.HelpBox("This shared color table has no colors.", MessageType.Info);
            }
            else colorIndex = -1;
            Texture2D normal = atlas.normal;
            Texture2D mask = atlas.mask;
            atlasMapsExpanded = EditorGUILayout.Foldout(atlasMapsExpanded, "Additional texture maps", true);
            if (atlasMapsExpanded)
            {
                normal = (Texture2D)EditorGUILayout.ObjectField("Normal Atlas", normal, typeof(Texture2D), false);
                mask = (Texture2D)EditorGUILayout.ObjectField("Mask Atlas", mask, typeof(Texture2D), false);
            }
            if (EditorGUI.EndChangeCheck() &&
                (albedo != atlas.albedo || normal != atlas.normal || mask != atlas.mask || material != atlas.material ||
                 secondPassMaterial != atlas.secondPassMaterial || table != atlas.sharedColorTable || colorIndex != atlas.sharedColorIndex))
            {
                Undo.RecordObject(atlas, "Edit Hair Atlas Textures");
                atlas.albedo = albedo;
                atlas.normal = normal;
                atlas.mask = mask;
                bool materialChanged = atlas.material != material || atlas.secondPassMaterial != secondPassMaterial;
                bool sharedColorChanged = table != atlas.sharedColorTable || colorIndex != atlas.sharedColorIndex;
                atlas.material = material;
                atlas.secondPassMaterial = secondPassMaterial;
                atlas.sharedColorTable = table; atlas.sharedColorIndex = colorIndex;
                EditorUtility.SetDirty(atlas);
                stage.TrackResourceEdit(atlas);
                stage.QueuePreviewChange(materialChanged ? HairPreviewChange.Geometry : HairPreviewChange.Materials | HairPreviewChange.Validation);
                if (sharedColorChanged || materialChanged) HairSharedColorUtility.Apply(atlas, stage);
            }
            if (HairSharedColorUtility.SelectedColor(atlas) != null)
            {
                int parameters = HairSharedColorUtility.ParameterCount(atlas);
                using (new EditorGUI.DisabledScope(parameters == 0))
                    if (GUILayout.Button($"Apply Shared Color Shader Parameters ({parameters})")) HairSharedColorUtility.Apply(atlas, stage);
                EditorGUILayout.HelpBox(parameters > 0
                    ? "Selecting a color copies matching shader parameters to both assigned pass materials (Undo supported). Other users of those materials also change. Reapply after editing the table. Clearing the selection does not restore old material values."
                    : "No matching shader parameters to apply. Assign a material and choose a color with shader properties supported by that shader. The swatch/channel tint alone is not a shader parameter.", MessageType.Info);
            }
            if (atlas.albedo == null && HairAtlasRegionEditorWindow.ResolveDisplayTexture(atlas) != null)
                EditorGUILayout.LabelField("Previewing the Card Material's base texture.", EditorStyles.wordWrappedMiniLabel);
            else if (atlas.albedo != null && atlas.material != null)
                EditorGUILayout.LabelField("Scene and card previews use this atlas with the material's alpha settings. The shared material is unchanged.",
                    EditorStyles.wordWrappedMiniLabel);
            if (atlas.material == null)
                EditorGUILayout.HelpBox("Assign a Card Material for the Scene preview and baked cards. Canvas alpha settings only affect the UV preview.",
                    MessageType.Info);

            atlasEditor ??= new HairAtlasEditorPanel();
            float editorWidth = NodeContentWidth(position.width);
            atlasEditor.Draw(atlas, stage.Groom, group, editorWidth, Repaint);
        }

        private static void DrawOptimize(HairCardStage stage)
        {
            HairGroomAsset groom = stage.Groom;
            DrawStepTitle("6. Optimize", "Author deterministic LODs and keep card, vertex, triangle, and skinning budgets visible.");
            HairCardMeshBuildResult mesh = stage.MeshBuild;
            HairEvaluationResult evaluation = stage.Evaluation;
            EditorGUILayout.LabelField("Live Budget", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Guides", CountGuides(groom).ToString("N0"));
            EditorGUILayout.LabelField("Cards", (evaluation?.CardCount ?? 0).ToString("N0"));
            EditorGUILayout.LabelField("Vertices", (mesh?.vertexCount ?? 0).ToString("N0"));
            EditorGUILayout.LabelField("Triangles", (mesh?.triangleCount ?? 0).ToString("N0"));
            int triangleBudget = EditorGUILayout.IntField("Triangle Budget", groom.BakeSettings.triangleBudget);
            int cardBudget = EditorGUILayout.IntField("Card Budget", groom.BakeSettings.cardBudget);
            if (triangleBudget != groom.BakeSettings.triangleBudget || cardBudget != groom.BakeSettings.cardBudget)
            {
                Undo.RecordObject(groom, "Edit Hair Budgets");
                groom.BakeSettings.triangleBudget = Mathf.Max(1, triangleBudget);
                groom.BakeSettings.cardBudget = Mathf.Max(1, cardBudget);
                HairGroomCommands.Commit(groom);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("LOD Profiles", EditorStyles.boldLabel);
            for (int i = 0; i < groom.Lods.Count; i++)
            {
                HairLodSettings lod = groom.Lods[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Toggle(stage.LodLevel == lod.level, lod.name, "Button")) stage.LodLevel = lod.level;
                        GUILayout.Label($"{lod.cardFraction:P0} cards", EditorStyles.miniLabel);
                    }
                    float fraction = EditorGUILayout.Slider("Card Fraction", lod.cardFraction, 0f, 1f);
                    bool profileSamples = EditorGUILayout.Toggle("Use profile sampling", lod.useProfileSamples);
                    int samples = lod.samplesPerCard;
                    using (new EditorGUI.DisabledScope(profileSamples))
                        samples = EditorGUILayout.IntSlider("LOD sample override", lod.samplesPerCard, 2, 64);
                    int tubeSides = EditorGUILayout.IntSlider("Maximum Tube Sides", lod.maximumTubeSides, 3, 12);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Slider("Screen Height (inactive)", lod.screenRelativeHeight, 0f, 1f);
                        EditorGUILayout.EnumPopup("Reduction (reserved)", lod.reductionMode);
                    }
                    EditorGUILayout.LabelField("Current reduction: deterministic importance thinning. Automatic screen-height switching, " +
                        "group merging, and impostors are not implemented; the reserved settings above have no effect.", EditorStyles.wordWrappedMiniLabel);
                    if (!Mathf.Approximately(fraction, lod.cardFraction) || samples != lod.samplesPerCard ||
                        tubeSides != lod.maximumTubeSides || profileSamples != lod.useProfileSamples)
                    {
                        Undo.RecordObject(groom, "Edit Hair LOD");
                        lod.cardFraction = fraction;
                        lod.samplesPerCard = samples;
                        lod.maximumTubeSides = tubeSides;
                        lod.useProfileSamples = profileSamples;
                        HairGroomCommands.Commit(groom);
                    }
                }
            }
            if (GUILayout.Button("+ LOD")) HairGroomCommands.AddLod(groom);
            EditorGUILayout.HelpBox("Bake transfers the closest available scalp bone weights to every generated card vertex. Inspect deformation on the equipped preview avatar before shipping.", MessageType.Info);
        }

        private void DrawValidateAndBake(HairCardStage stage)
        {
            HairGroomAsset groom = stage.Groom;
            DrawStepTitle("7. Validate & Bake", "Review release blockers and create Unity Mesh, UMA Slot, Overlay, recipe, and every configured LOD in one transaction.");
            if (GUILayout.Button("Validate All Exported LODs", GUILayout.Height(28f))) stage.ValidateReleaseNow();
            HairValidationReport report = stage.ReleaseValidation;
            if (!stage.ReleaseValidationIsCurrent)
                EditorGUILayout.HelpBox("Release validation is missing or outdated. Validate to refresh it. " +
                    "Bake always revalidates the full output, regardless of preview visibility or LOD.", MessageType.Info);
            if (report != null)
            {
                EditorGUILayout.HelpBox(report.CanBake
                    ? $"Last release check passed. LOD 0: {report.cardCount:N0} cards, {report.vertexCount:N0} vertices, {report.triangleCount:N0} triangles."
                    : $"Resolve {report.ErrorCount} blocking error(s) before baking.",
                    report.CanBake ? MessageType.Info : MessageType.Error);
                foreach (HairLodValidationSummary lod in report.lods)
                    EditorGUILayout.LabelField($"LOD {lod.level}: {lod.cardCount:N0} cards · {lod.vertexCount:N0} vertices · {lod.triangleCount:N0} triangles",
                        EditorStyles.wordWrappedMiniLabel);
                DrawValidationIssues(stage, report);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Bake Transaction", EditorStyles.boldLabel);
            HairBakeSettings settings = groom.BakeSettings;
            string output = EditorGUILayout.TextField("Output Folder", settings.outputFolder);
            string assetName = EditorGUILayout.TextField("Asset Name", settings.assetName);
            bool createMesh = EditorGUILayout.Toggle("Unity Mesh + LODs", settings.createMesh);
            bool createSlot = EditorGUILayout.Toggle("UMA SlotDataAsset", settings.createSlot);
            bool createOverlay = EditorGUILayout.Toggle("OverlayDataAsset", settings.createOverlay);
            bool createRecipe = EditorGUILayout.Toggle("Wardrobe Recipe", settings.createWardrobeRecipe);
            bool updateIndex = EditorGUILayout.Toggle("Update Global Library", settings.updateGlobalLibrary);
            bool overwrite = EditorGUILayout.Toggle("Update Existing Assets", settings.overwriteExisting);
            bool requireAtlas = EditorGUILayout.Toggle("Require Atlas", settings.requireAtlas);
            UMAMaterial umaMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMA Material", settings.umaMaterial,
                typeof(UMAMaterial), false);
            OverlayDataAsset overlay = (OverlayDataAsset)EditorGUILayout.ObjectField("Existing Overlay",
                settings.overlayTemplate, typeof(OverlayDataAsset), false);
            RaceData race = (RaceData)EditorGUILayout.ObjectField("Compatible Race", settings.raceData,
                typeof(RaceData), false);
            string wardrobeSlot = EditorGUILayout.TextField("Wardrobe Slot", settings.wardrobeSlot);
            if (output != settings.outputFolder || assetName != settings.assetName || createMesh != settings.createMesh ||
                createSlot != settings.createSlot || createOverlay != settings.createOverlay ||
                createRecipe != settings.createWardrobeRecipe || updateIndex != settings.updateGlobalLibrary ||
                overwrite != settings.overwriteExisting || requireAtlas != settings.requireAtlas ||
                umaMaterial != settings.umaMaterial ||
                overlay != settings.overlayTemplate || race != settings.raceData || wardrobeSlot != settings.wardrobeSlot)
            {
                Undo.RecordObject(groom, "Edit Hair Bake Settings");
                settings.outputFolder = output;
                settings.assetName = assetName;
                settings.createMesh = createMesh;
                settings.createSlot = createSlot;
                settings.createOverlay = createOverlay;
                settings.createWardrobeRecipe = createRecipe;
                settings.updateGlobalLibrary = updateIndex;
                settings.overwriteExisting = overwrite;
                settings.requireAtlas = requireAtlas;
                settings.umaMaterial = umaMaterial;
                settings.overlayTemplate = overlay;
                settings.raceData = race;
                settings.wardrobeSlot = wardrobeSlot;
                HairGroomCommands.Commit(groom);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Dry Run (all output)", GUILayout.Height(30f)))
                {
                    stage.ValidateReleaseNow();
                    if (stage.ReleaseValidation != null)
                    {
                        HairValidationReport result = stage.ReleaseValidation;
                        lastBake = new HairBakeOutcome { validation = result, isDryRun = true, succeeded = result.CanBake,
                            cardCount = result.cardCount, vertexCount = result.vertexCount, triangleCount = result.triangleCount };
                    }
                }
                using (new EditorGUI.DisabledScope(stage.ReleaseValidationIsCurrent && report != null && !report.CanBake))
                {
                    if (GUILayout.Button("Bake", GUILayout.Height(30f)))
                    {
                        lastBake = HairBakePipeline.Bake(groom, stage.SourceAvatar);
                        stage.SetReleaseValidation(lastBake.validation);
                    }
                }
            }
            if (lastBake != null)
            {
                EditorGUILayout.HelpBox(lastBake.succeeded
                    ? lastBake.isDryRun ? "Dry run passed for all exported LODs. No output assets were written." :
                        $"Bake completed: {lastBake.assets.Count} asset(s), {lastBake.cardCount:N0} cards, {lastBake.triangleCount:N0} triangles."
                    : "Validation/bake did not commit output assets. Review the release issues above.", lastBake.succeeded ? MessageType.Info : MessageType.Warning);
                for (int i = 0; i < lastBake.warnings.Count; i++)
                    EditorGUILayout.HelpBox(lastBake.warnings[i], MessageType.Warning);
                for (int i = 0; i < lastBake.assets.Count; i++)
                {
                    UnityEngine.Object asset = lastBake.assets[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(asset, asset.GetType(), false);
                        if (GUILayout.Button("Ping", GUILayout.Width(42f))) EditorGUIUtility.PingObject(asset);
                    }
                }
            }
        }

        private static void DrawValidationIssues(HairCardStage stage, HairValidationReport report)
        {
            foreach (HairValidationIssue issue in report.issues)
            {
                MessageType type = issue.severity == HairValidationSeverity.Error ? MessageType.Error :
                    issue.severity == HairValidationSeverity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(issue.message, type);
                EditorGUILayout.LabelField("Use Issues in Hair Nodes to locate this setting or guide.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    HairGroup group = stage.Groom.FindGroup(issue.groupId);
                    using (new EditorGUI.DisabledScope(group == null || group.locked))
                    {
                        string repair = issue.fixId == "assign-profile" ? "Create profile" :
                            issue.fixId == "assign-atlas" ? "Create atlas" : null;
                        if (repair != null && GUILayout.Button(repair))
                        {
                            stage.FixMissingResource(issue);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private static void DrawModifier(HairCardStage stage, HairModifierSettings modifier)
        {
            if (modifier == null) return;
            using (new EditorGUI.DisabledScope(stage.ActiveGroup.locked))
            {
                bool enabled = EditorGUILayout.Toggle(new GUIContent("Active",
                    "Apply this modifier to preview and baked hair. Turning it off preserves all settings."), modifier.enabled);
                if (enabled != modifier.enabled)
                    HairGroomCommands.SetModifierEnabled(stage.Groom, stage.ActiveGroup, stage.ActiveLayer, modifier, enabled);
                if (!modifier.enabled)
                    EditorGUILayout.LabelField("Bypassed. Settings are preserved; turn Active on to apply this modifier.", EditorStyles.wordWrappedMiniLabel);
                string name = EditorGUILayout.DelayedTextField("Name", modifier.name);
                float weight = EditorGUILayout.Slider("Blend weight", modifier.weight, 0f, 1f);
                string amountLabel = modifier.type switch
                {
                    HairModifierType.Length or HairModifierType.Width => "Scale multiplier",
                    HairModifierType.Curl or HairModifierType.Wave or HairModifierType.Noise => "Amplitude (units)",
                    HairModifierType.Twist => "Roll (degrees)",
                    HairModifierType.Resample or HairModifierType.Simplify => "Control point count",
                    HairModifierType.Gravity => "Settle duration (seconds)",
                    HairModifierType.Lift => "Lift distance (source units)",
                    HairModifierType.HelperFollow => "Follow strength (0–1)",
                    HairModifierType.TrimByMesh => "Cut offset (units)",
                    HairModifierType.LodReduction => "Samples per card",
                    HairModifierType.Smooth or HairModifierType.Clump => "Strength (0–1)",
                    HairModifierType.SurfaceProjection or HairModifierType.Collision or HairModifierType.PushOut => "Surface offset",
                    _ => "Distance / amount"
                };
                float amount = modifier.amount;
                if (modifier.type == HairModifierType.FlowAlign)
                    amount = EditorGUILayout.Slider(new GUIContent("Alignment (0–1)",
                        "Rotates segment directions toward the source-local direction. 0 leaves the hair unchanged; 1 fully aligns it at full blend/influence. Roots and segment lengths stay fixed."), amount, 0f, 1f);
                else if (modifier.type == HairModifierType.Gravity)
                    amount = EditorGUILayout.Slider(amountLabel, amount, 0f, 5f);
                else if (modifier.type != HairModifierType.Mirror && modifier.type != HairModifierType.SplineFlow)
                    amount = EditorGUILayout.FloatField(amountLabel, amount);
                bool usesRamp = modifier.type != HairModifierType.Length && modifier.type != HairModifierType.Resample && modifier.type != HairModifierType.Simplify && modifier.type != HairModifierType.LodReduction;
                float rootInfluence = modifier.rootInfluence;
                if (ModifierUsesRootInfluence(modifier.type))
                    rootInfluence = EditorGUILayout.Slider(new GUIContent("Root Influence", "The root stays anchored. Lower values protect bending near the base; tips keep full influence."), rootInfluence, 0f, 1f);
                float gravityStrength = modifier.gravityStrength, separation = modifier.gravitySeparation, clearance = modifier.gravityClearance;
                bool collision = modifier.gravityCollision;
                bool useWorldGravity = modifier.useWorldGravity;
                Vector3 gravityDirection = modifier.gravityDirection;
                HairLiftNormalMode liftNormalMode = modifier.liftNormalMode;
                if (modifier.type == HairModifierType.Lift)
                {
                    liftNormalMode = (HairLiftNormalMode)EditorGUILayout.EnumPopup("Lift normal", liftNormalMode);
                    EditorGUILayout.HelpBox("Positive Lift bends hair away from the scalp; negative values lower it. Root Normal follows each strand's attachment normal. " +
                        "Closest Surface Normal follows the nearest source-mesh triangle at each point (requires outward-facing triangles). " +
                        "Normals account for object scale and the preview pose. Roots and segment lengths stay fixed; Root Influence and frozen points are respected. " +
                        "Distance is a target displacement in source units, not a guaranteed clearance.", MessageType.Info);
                }
                if (modifier.type == HairModifierType.Gravity)
                {
                    gravityStrength = EditorGUILayout.Slider("Gravity strength", gravityStrength, 0f, 10f);
                    separation = EditorGUILayout.Slider("Card separation", separation, 0f, 1f);
                    useWorldGravity = EditorGUILayout.Toggle("Use world gravity", useWorldGravity);
                    using (new EditorGUI.DisabledScope(useWorldGravity)) gravityDirection = EditorGUILayout.Vector3Field("Source-local gravity override", gravityDirection);
                    collision = EditorGUILayout.Toggle("Collide with source mesh", collision);
                    using (new EditorGUI.DisabledScope(!collision)) clearance = EditorGUILayout.Slider("Surface clearance", clearance, 0f, 0.05f);
                    EditorGUILayout.HelpBox("Uses the hold button's settling, stiffness, freeze and length-constraint rules. World gravity accounts for object rotation/scale and the preview's per-guide pose. Disable it for an intentional local-direction override. Duration is simulated from the incoming shape on every rebuild; it never accumulates. New modifiers copy the hold-button settings.", MessageType.Info);
                }
                if (modifier.type == HairModifierType.TrimByMesh)
                    EditorGUILayout.HelpBox("Cuts at the first intersection with the groom's source mesh, keeping the root-side curve. Blend weight softens the amount removed. Frozen tips are protected.", MessageType.Info);
                if (modifier.type == HairModifierType.Clump)
                    EditorGUILayout.LabelField("Bends strands toward the group's shared tip center, or the selected helper position. Roots and segment lengths stay fixed.", EditorStyles.wordWrappedMiniLabel);
                if (modifier.type == HairModifierType.Curl || modifier.type == HairModifierType.Wave)
                    EditorGUILayout.LabelField("For a smooth curl/wave, add Resample before this modifier and allow at least four control points per cycle.", EditorStyles.wordWrappedMiniLabel);
                if (modifier.type == HairModifierType.SurfaceProjection)
                    EditorGUILayout.LabelField("Projects non-frozen points onto the source's nearest-vertex surface approximation. Root is pinned; projection intentionally changes length.", EditorStyles.wordWrappedMiniLabel);
                if (modifier.type == HairModifierType.Collision || modifier.type == HairModifierType.PushOut)
                    EditorGUILayout.LabelField("Surface offset is clearance, not strength. Blend and root-to-tip influence control response. Anchors and segment lengths take priority when clearance conflicts.", EditorStyles.wordWrappedMiniLabel);
                if (modifier.type == HairModifierType.Mirror)
                    EditorGUILayout.LabelField("Reflects the entire strand, including its root and root normal, across the groom symmetry plane. Does not duplicate hair. Partial blending passes through the plane.", EditorStyles.wordWrappedMiniLabel);
                if (modifier.type == HairModifierType.LodReduction)
                    EditorGUILayout.LabelField("Legacy per-card sample reduction. Use Optimize for density and distance-based LODs.", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                AnimationCurve ramp = usesRamp ? EditorGUILayout.CurveField("Root → tip influence", modifier.rootToTip) : modifier.rootToTip;
                bool rampChanged = EditorGUI.EndChangeCheck();
                HairModifierDomain domain = (HairModifierDomain)EditorGUILayout.EnumPopup("Domain", modifier.domain);
                EditorGUILayout.LabelField("Guides And Children applies once to guides; children inherit the result. Children affects generated children only.", EditorStyles.wordWrappedMiniLabel);
                Vector3 vector = modifier.vector;
                if (modifier.type == HairModifierType.Curl || modifier.type == HairModifierType.Wave)
                { vector.x = Mathf.Max(0.01f, EditorGUILayout.FloatField("Cycles along guide", vector.x)); vector.y = EditorGUILayout.FloatField("Phase (radians)", vector.y); }
                else if (ModifierUsesVector(modifier.type) && modifier.type != HairModifierType.Gravity)
                    vector = EditorGUILayout.Vector3Field(modifier.type == HairModifierType.Part ? "Source-local plane normal" : "Source-local direction", modifier.vector);
                if (modifier.type == HairModifierType.FlowAlign)
                {
                    EditorGUILayout.LabelField("Roots stay anchored; segment lengths are preserved. Guides And Children aligns the guides once; children inherit that flow. Children aligns only generated children. Direction is in the source mesh's local space.", EditorStyles.wordWrappedMiniLabel);
                    if (!float.IsFinite(vector.sqrMagnitude) || vector.sqrMagnitude <= 1e-12f)
                        EditorGUILayout.HelpBox("Enter a finite, non-zero direction to align the hair.", MessageType.Info);
                }
                int seed = modifier.seed;
                if (modifier.type == HairModifierType.Noise)
                    seed = EditorGUILayout.IntField("Seed", modifier.seed);
                string helperId = modifier.helperId;
                if (ModifierUsesHelper(modifier.type))
                {
                    List<string> helperIds = new List<string> { string.Empty };
                    List<string> helperNames = new List<string> { "None" };
                    for (int helperIndex = 0; helperIndex < stage.Groom.SharedHelpers.Count; helperIndex++)
                    {
                        HairHelper helper = stage.Groom.SharedHelpers[helperIndex];
                        if (helper == null) continue;
                        helperIds.Add(helper.Id);
                        helperNames.Add(helper.name);
                    }
                    if (!string.IsNullOrEmpty(modifier.helperId) && !helperIds.Contains(modifier.helperId))
                    { helperIds.Add(modifier.helperId); helperNames.Add("Missing helper (choose a replacement)"); }
                    int selectedHelper = Mathf.Max(0, helperIds.IndexOf(modifier.helperId));
                    selectedHelper = EditorGUILayout.Popup("Helper", selectedHelper, helperNames.ToArray());
                    helperId = helperIds[selectedHelper];
                    HairHelper selected = stage.Groom.FindHelper(helperId);
                    if (modifier.type == HairModifierType.HelperFollow && (selected?.points == null || selected.points.Count < 2))
                        EditorGUILayout.HelpBox("Choose a curve helper with at least two points to enable following.", MessageType.Warning);
                    if ((modifier.type == HairModifierType.Collision || modifier.type == HairModifierType.PushOut) &&
                        (selected == null || (selected.type != HairHelperType.Sphere && selected.type != HairHelperType.Box &&
                         selected.type != HairHelperType.Capsule && selected.type != HairHelperType.Plane && selected.type != HairHelperType.Repulsor &&
                         selected.type != HairHelperType.VolumeTarget && selected.type != HairHelperType.SculptCage)))
                        EditorGUILayout.HelpBox("Choose a sphere, box, capsule or plane collider helper. Curve helpers are not collision volumes.", MessageType.Warning);
                }
                if (name != modifier.name || rampChanged || !Mathf.Approximately(weight, modifier.weight) || !Mathf.Approximately(amount, modifier.amount) ||
                    domain != modifier.domain || vector != modifier.vector || seed != modifier.seed ||
                    helperId != modifier.helperId || rootInfluence != modifier.rootInfluence || liftNormalMode != modifier.liftNormalMode ||
                    gravityStrength != modifier.gravityStrength || separation != modifier.gravitySeparation ||
                    collision != modifier.gravityCollision || clearance != modifier.gravityClearance || gravityDirection != modifier.gravityDirection || useWorldGravity != modifier.useWorldGravity)
                {
                    Undo.RecordObject(stage.Groom, "Edit Hair Modifier");
                    modifier.name = name;
                    modifier.rootToTip = ramp;
                    modifier.weight = weight;
                    modifier.amount = amount;
                    modifier.domain = domain;
                    modifier.vector = vector;
                    modifier.seed = seed;
                    modifier.helperId = helperId;
                    modifier.rootInfluence = rootInfluence;
                    modifier.liftNormalMode = liftNormalMode;
                    modifier.gravityStrength = gravityStrength; modifier.gravitySeparation = separation;
                    modifier.gravityCollision = collision; modifier.gravityClearance = clearance;
                    modifier.gravityDirection = gravityDirection;
                    modifier.useWorldGravity = useWorldGravity;
                    HairGroomCommands.Commit(stage.Groom);
                }
                if (modifier.type == HairModifierType.SplineFlow) HairSplineFlowInspector.Draw(stage, modifier);
            }
        }

        private static bool ModifierUsesVector(HairModifierType type)
        {
            return type == HairModifierType.FlowAlign ||
                   type == HairModifierType.Part || type == HairModifierType.Curl ||
                   type == HairModifierType.Wave || type == HairModifierType.Gravity;
        }

        private static bool ModifierUsesRootInfluence(HairModifierType type) => type == HairModifierType.Gravity ||
            type == HairModifierType.Smooth || type == HairModifierType.FlowAlign || type == HairModifierType.SplineFlow || type == HairModifierType.Lift ||
            type == HairModifierType.Clump || type == HairModifierType.Part || type == HairModifierType.Curl ||
            type == HairModifierType.Wave || type == HairModifierType.Noise || type == HairModifierType.HelperFollow ||
            type == HairModifierType.Collision || type == HairModifierType.PushOut;

        private static bool ModifierUsesHelper(HairModifierType type)
        {
            return type == HairModifierType.HelperFollow || type == HairModifierType.Collision ||
                   type == HairModifierType.PushOut || type == HairModifierType.Clump;
        }

        internal static void DrawStatus(HairCardStage stage)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                HairEvaluationResult evaluation = stage.Evaluation;
                HairCardMeshBuildResult mesh = stage.MeshBuild;
                GUILayout.Label($"{ObjectNames.NicifyVariableName(stage.SceneTool.ToString())}: {stage.ActionStatus}",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label($"Guides {CountGuides(stage.Groom):N0}  Cards {evaluation?.CardCount ?? 0:N0}  Tris {mesh?.triangleCount ?? 0:N0}",
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawStepTitle(string title, string description)
        {
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(7f);
        }

        private static int CountGuides(HairGroomAsset groom)
        {
            int count = 0;
            if (groom?.Groups == null) return count;
            for (int i = 0; i < groom.Groups.Count; i++) count += groom.Groups[i]?.guides?.Count ?? 0;
            return count;
        }

        private static GUIStyle CenteredTitle()
        {
            return new GUIStyle(EditorStyles.largeLabel) { alignment = TextAnchor.MiddleCenter };
        }

        private static GUIStyle CenteredWrapped()
        {
            return new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
