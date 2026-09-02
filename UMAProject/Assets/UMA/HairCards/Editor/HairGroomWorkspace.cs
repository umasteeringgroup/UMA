using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed class HairGroomWorkspace : EditorWindow
    {
        private const string QuickStartPath = "Assets/UMA/Docs/Hair Cards - Quick Start.md";

        private static readonly string[] StepNames =
        {
            "Setup", "Growth", "Guides", "Groom", "Cards", "Optimize", "Validate & Bake"
        };

        private Vector2 explorerScroll;
        private Vector2 detailsScroll;
        private HairBakeOutcome lastBake;
        private GameObject sceneHelperCandidate;
        private string visibilitySearch = string.Empty;
        private bool recipeVisibilityExpanded = true;
        private bool udimVisibilityExpanded = true;
        private bool slotVisibilityExpanded = true;

        [MenuItem("UMA/Hair Cards/Hair Groom Workspace", priority = 210)]
        public static void OpenForActiveStage()
        {
            HairGroomWorkspace window = GetWindow<HairGroomWorkspace>();
            window.titleContent = new GUIContent("Hair Groom", EditorGUIUtility.IconContent("Mesh Icon").image);
            window.minSize = new Vector2(760f, 500f);
            window.Show();
        }

        public static void RepaintOpenWindows()
        {
            HairGroomWorkspace[] windows = Resources.FindObjectsOfTypeAll<HairGroomWorkspace>();
            for (int i = 0; i < windows.Length; i++) windows[i].Repaint();
        }

        private void OnGUI()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage == null || stage.Groom == null)
            {
                DrawNoStage();
                return;
            }

            DrawHeader(stage);
            int selectedStep = GUILayout.Toolbar((int)stage.WorkflowStep, StepNames, GUILayout.Height(28f));
            if (selectedStep != (int)stage.WorkflowStep) stage.WorkflowStep = (HairWorkflowStep)selectedStep;
            EditorGUILayout.Space(3f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Clamp(position.width * 0.27f, 205f, 300f))))
                {
                    explorerScroll = EditorGUILayout.BeginScrollView(explorerScroll);
                    DrawExplorer(stage);
                    EditorGUILayout.EndScrollView();
                }
                GUILayout.Box(GUIContent.none, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
                detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll);
                DrawStep(stage);
                EditorGUILayout.EndScrollView();
            }
            DrawStatus(stage);
        }

        private static void DrawNoStage()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Hair Groom Workspace", CenteredTitle());
            EditorGUILayout.LabelField("Open a HairGroomAsset, readable Mesh, or generated DynamicCharacterAvatar to begin.",
                CenteredWrapped());
            GUILayout.Space(12f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open Selected Source", GUILayout.Width(170f), GUILayout.Height(28f)))
                    HairCardMenu.OpenSelectedSource();
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
        }

        private static void DrawHeader(HairCardStage stage)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(stage.Groom.name, EditorStyles.boldLabel);
                GUILayout.Label(stage.Groom.SourceRace, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                HairValidationReport validation = stage.Validation;
                if (validation != null)
                {
                    GUILayout.Label($"{validation.ErrorCount} errors  {validation.WarningCount} warnings",
                        validation.ErrorCount > 0 ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                }
                if (GUILayout.Button(new GUIContent("?", "Open Hair Cards - Quick Start"),
                        EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    OpenQuickStart();
                if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(48f))) stage.FrameGroom();
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(58f))) stage.QueueRebuild(true);
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45f))) stage.SaveNow();
                using (new EditorGUI.DisabledScope(stage.Validation != null && !stage.Validation.CanBake))
                {
                    if (GUILayout.Button("Bake", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                    {
                        stage.WorkflowStep = HairWorkflowStep.ValidateAndBake;
                    }
                }
                if (GUILayout.Button("Exit Stage", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    StageUtility.GoBackToPreviousStage();
            }
        }

        private static void OpenQuickStart()
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

        private void DrawExplorer(HairCardStage stage)
        {
            HairGroomAsset groom = stage.Groom;
            EditorGUILayout.LabelField("Groom Explorer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{groom.Groups.Count} groups  •  {CountGuides(groom)} guides", EditorStyles.miniLabel);
            EditorGUILayout.Space(3f);
            for (int groupIndex = 0; groupIndex < groom.Groups.Count; groupIndex++)
            {
                HairGroup group = groom.Groups[groupIndex];
                if (group == null) continue;
                bool active = group.Id == stage.ActiveGroupId;
                Rect row = EditorGUILayout.GetControlRect(false, 23f);
                if (active) EditorGUI.DrawRect(row, new Color(0.18f, 0.42f, 0.7f, 0.35f));
                Rect visibleRect = new Rect(row.x + 2f, row.y + 3f, 18f, 18f);
                Rect lockRect = new Rect(row.x + 22f, row.y + 3f, 18f, 18f);
                Rect labelRect = new Rect(row.x + 43f, row.y, row.width - 78f, row.height);
                bool visible = GUI.Toggle(visibleRect, group.visible, GUIContent.none);
                bool locked = GUI.Toggle(lockRect, group.locked, EditorGUIUtility.IconContent("LockIcon-On"), GUIStyle.none);
                if (visible != group.visible || locked != group.locked)
                {
                    Undo.RecordObject(groom, "Change Hair Group State");
                    group.visible = visible;
                    group.locked = locked;
                    HairGroomCommands.Commit(groom);
                }
                if (GUI.Button(labelRect, new GUIContent(group.name, $"{group.guides.Count} authored guides"),
                        active ? EditorStyles.boldLabel : EditorStyles.label)) stage.SetActiveGroup(group.Id);
                EditorGUI.LabelField(new Rect(row.xMax - 34f, row.y, 32f, row.height), group.guides.Count.ToString(),
                    EditorStyles.miniLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Group")) ShowAddGroupMenu(stage);
                using (new EditorGUI.DisabledScope(groom.Groups.Count <= 1))
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(62f)) &&
                        HairGroomCommands.RemoveGroup(groom, stage.ActiveGroupId))
                        stage.SetActiveGroup(groom.Groups[0].Id);
                }
            }

            HairGroup activeGroup = stage.ActiveGroup;
            if (activeGroup == null)
            {
                DrawAvatarVisibility(stage);
                return;
            }
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Active Group", EditorStyles.boldLabel);
            string groupName = EditorGUILayout.DelayedTextField("Name", activeGroup.name);
            HairGroupRole role = (HairGroupRole)EditorGUILayout.EnumPopup("Role", activeGroup.role);
            Color color = EditorGUILayout.ColorField("Display Color", activeGroup.color);
            bool enabled = EditorGUILayout.Toggle("Include in Bake", activeGroup.enabled);
            if (groupName != activeGroup.name || role != activeGroup.role || color != activeGroup.color ||
                enabled != activeGroup.enabled)
            {
                Undo.RecordObject(groom, "Edit Hair Group");
                activeGroup.name = groupName;
                activeGroup.role = role;
                activeGroup.color = color;
                activeGroup.enabled = enabled;
                HairGroomCommands.Commit(groom);
            }
            DrawAvatarVisibility(stage);
        }

        private void DrawAvatarVisibility(HairCardStage stage)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Avatar Visibility", EditorStyles.boldLabel);
            if (!stage.HasAvatarVisibility)
            {
                EditorGUILayout.LabelField("Launch from a generated DynamicCharacterAvatar to hide its recipes, " +
                                           "UDIM groups, and slots.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            stage.ShowAvatar = EditorGUILayout.ToggleLeft("Show character preview", stage.ShowAvatar);
            visibilitySearch = EditorGUILayout.TextField(visibilitySearch, EditorStyles.toolbarSearchField);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft)) stage.ShowAllAvatarSlots();
                if (GUILayout.Button("None", EditorStyles.miniButtonMid)) stage.HideAllAvatarSlots();
                if (GUILayout.Button("Invert", EditorStyles.miniButtonRight)) stage.InvertAvatarSlots();
            }

            recipeVisibilityExpanded = DrawVisibilitySection(stage, "By Recipe", recipeVisibilityExpanded,
                stage.RecipeVisibilityGroups);
            udimVisibilityExpanded = DrawVisibilitySection(stage, "By UDIM Group", udimVisibilityExpanded,
                stage.UdimVisibilityGroups);
            slotVisibilityExpanded = DrawVisibilitySection(stage, "By Slot", slotVisibilityExpanded,
                stage.SlotVisibilityGroups);
            EditorGUILayout.LabelField("Visibility is stage-only. Hidden parts are also removed from painting, " +
                                       "selection, and guide-placement raycasts.", EditorStyles.wordWrappedMiniLabel);
        }

        private bool DrawVisibilitySection(HairCardStage stage, string title, bool expanded,
            IReadOnlyList<HairAvatarVisibilityGroup> groups)
        {
            int matched = CountMatchingVisibilityGroups(groups);
            expanded = EditorGUILayout.Foldout(expanded, $"{title} ({matched})", true);
            if (!expanded) return false;
            if (groups == null || groups.Count == 0)
            {
                EditorGUILayout.LabelField("No groups on this character.", EditorStyles.miniLabel);
                return true;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < groups.Count; i++)
            {
                HairAvatarVisibilityGroup group = groups[i];
                if (!MatchesVisibilitySearch(group)) continue;
                HairVisibilityState state = stage.GetVisibilityState(group);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.showMixedValue = state == HairVisibilityState.Mixed;
                    bool currentlyVisible = state == HairVisibilityState.Visible;
                    string tooltip = group.SlotNames.Count == 1
                        ? group.SlotNames[0]
                        : string.Join("\n", group.SlotNames);
                    bool visible = EditorGUILayout.ToggleLeft(new GUIContent(group.DisplayName, tooltip),
                        currentlyVisible);
                    EditorGUI.showMixedValue = false;
                    if (visible != currentlyVisible) stage.SetVisibility(group, visible);
                    if (GUILayout.Button("Only", EditorStyles.miniButton, GUILayout.Width(37f)))
                        stage.IsolateVisibility(group);
                }
            }
            EditorGUI.indentLevel--;
            return true;
        }

        private int CountMatchingVisibilityGroups(IReadOnlyList<HairAvatarVisibilityGroup> groups)
        {
            if (groups == null) return 0;
            int count = 0;
            for (int i = 0; i < groups.Count; i++)
                if (MatchesVisibilitySearch(groups[i])) count++;
            return count;
        }

        private bool MatchesVisibilitySearch(HairAvatarVisibilityGroup group)
        {
            if (group == null) return false;
            if (string.IsNullOrWhiteSpace(visibilitySearch)) return true;
            if (group.DisplayName.IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (int i = 0; i < group.SlotNames.Count; i++)
                if (group.SlotNames[i].IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void DrawStep(HairCardStage stage)
        {
            switch (stage.WorkflowStep)
            {
                case HairWorkflowStep.Setup: DrawSetup(stage); break;
                case HairWorkflowStep.Growth: DrawGrowth(stage); break;
                case HairWorkflowStep.Guides: DrawGuides(stage); break;
                case HairWorkflowStep.Groom: DrawGroom(stage); break;
                case HairWorkflowStep.Cards: DrawCards(stage); break;
                case HairWorkflowStep.Optimize: DrawOptimize(stage); break;
                case HairWorkflowStep.ValidateAndBake: DrawValidateAndBake(stage); break;
            }
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
            bool symmetry = EditorGUILayout.Toggle("Enabled", groom.SymmetryEnabled);
            if (symmetry != groom.SymmetryEnabled)
            {
                Undo.RecordObject(groom, "Change Hair Symmetry");
                groom.SymmetryEnabled = symmetry;
                HairGroomCommands.Commit(groom);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            stage.ShowScalp = EditorGUILayout.Toggle("Show Authoring Surface", stage.ShowScalp);
            if (stage.HasAvatarVisibility)
                stage.ShowAvatar = EditorGUILayout.Toggle("Show Character", stage.ShowAvatar);
            stage.ShowChildren = EditorGUILayout.Toggle("Show Children", stage.ShowChildren);
            stage.ShowHelpers = EditorGUILayout.Toggle("Show Helpers", stage.ShowHelpers);
            stage.ShowControlPoints = EditorGUILayout.Toggle("Control Points", stage.ShowControlPoints);
            stage.PreviewMode = (HairPreviewMode)EditorGUILayout.EnumPopup("Preview Mode", stage.PreviewMode);
            if (GUILayout.Button("Continue to Growth", GUILayout.Height(28f))) stage.WorkflowStep = HairWorkflowStep.Growth;
        }

        private static void DrawGrowth(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("2. Growth", "Paint the surface region and scalar fields that drive guide placement and styling.");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.PaintGrowth, "Paint Growth Area", "Button",
                        GUILayout.Height(30f))) stage.SceneTool = HairSceneTool.PaintGrowth;
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.Select, "Select Vertices", "Button",
                        GUILayout.Height(30f))) stage.SceneTool = HairSceneTool.Select;
                stage.PaintErase = GUILayout.Toggle(stage.PaintErase, "Erase", "Button", GUILayout.Width(60f), GUILayout.Height(30f));
                stage.MirrorPaintX = GUILayout.Toggle(stage.MirrorPaintX, "Mirror X", "Button",
                    GUILayout.Width(76f), GUILayout.Height(30f));
            }
            stage.BrushRadius = EditorGUILayout.Slider("Brush Radius", stage.BrushRadius,
                HairBrushInteractionUtility.MinimumRadius, HairBrushInteractionUtility.MaximumRadius);
            stage.BrushHardness = EditorGUILayout.Slider(new GUIContent("Hardness",
                "Matches Overlay Painter: values inside this fraction of the radius receive full strength, then fall off linearly to zero at the outer ring."),
                stage.BrushHardness, 0f, 1f);
            stage.BrushStrength = EditorGUILayout.Slider("Strength", stage.BrushStrength, 0.01f, 1f);
            stage.PaintValue = EditorGUILayout.FloatField("Paint Value", stage.PaintValue);
            EditorGUILayout.HelpBox(
                "Mirror X paints both sides across the source mesh local X = 0 plane (M toggles it). " +
                "Shift + right-drag: horizontal changes radius, vertical changes hardness. [ and ] adjust radius; Shift + [ and ] adjust hardness.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Growth Maps", EditorStyles.boldLabel);
            for (int i = 0; i < group.maps.Count; i++)
            {
                HairGrowthMap map = group.maps[i];
                if (map == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool active = map.Id == stage.ActiveMapId;
                    bool selected = GUILayout.Toggle(active, map.name, "Button");
                    if (selected && !active) stage.SetActiveMap(map.Id);
                    bool visible = GUILayout.Toggle(map.visible, "V", "Button", GUILayout.Width(25f));
                    bool locked = GUILayout.Toggle(map.locked, "L", "Button", GUILayout.Width(25f));
                    if (visible != map.visible || locked != map.locked)
                    {
                        Undo.RecordObject(stage.Groom, "Change Growth Map State");
                        map.visible = visible;
                        map.locked = locked;
                        HairGroomCommands.Commit(stage.Groom);
                    }
                }
            }
            if (GUILayout.Button("Add Map…")) ShowAddMapMenu(stage);

            HairGrowthMap activeMap = stage.ActiveMap;
            if (activeMap != null)
            {
                EditorGUILayout.Space(7f);
                EditorGUILayout.LabelField(activeMap.name + " Operations", EditorStyles.boldLabel);
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
            if (GUILayout.Button("Continue to Guides", GUILayout.Height(28f))) stage.WorkflowStep = HairWorkflowStep.Guides;
        }

        private static void DrawGuides(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("3. Guides", "Place guides by hand or preview deterministic distribution from the Growth Area.");

            stage.GetGrowthAreaStatistics(out int growthVertices, out int sourceVertices, out float growthMaximum);
            if (growthVertices == 0)
            {
                EditorGUILayout.HelpBox(
                    "No non-zero Growth Area exists for this group. Automatic generation cannot place guides until you paint or initialize the scalp region.",
                    MessageType.Error);
                if (GUILayout.Button("Return to Growth and Paint", GUILayout.Height(28f)))
                    stage.WorkflowStep = HairWorkflowStep.Growth;
            }
            else
            {
                float coverage = sourceVertices > 0 ? growthVertices / (float)sourceVertices : 0f;
                EditorGUILayout.HelpBox(
                    $"Growth Area ready: {growthVertices:N0} source vertices ({coverage:P1}), maximum {growthMaximum:0.###}.",
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
                "1. Preview distributes temporary dashed guides inside the Growth Area. 2. Accept converts that preview into editable authored guides.",
                EditorStyles.wordWrappedLabel);
            HairGuideGenerationSettings settings = stage.GuideGeneration;
            settings.guideCount = EditorGUILayout.IntSlider("Guide Count", settings.guideCount, 1, 1000);
            settings.pointsPerGuide = EditorGUILayout.IntSlider("Points per Guide", settings.pointsPerGuide, 2, 24);
            settings.defaultLength = EditorGUILayout.Slider("Default Length", settings.defaultLength, 0.01f, 1f);
            settings.minimumRootSpacing = EditorGUILayout.Slider("Minimum Spacing", settings.minimumRootSpacing, 0f, 0.2f);
            settings.surfaceFlow = EditorGUILayout.Slider("Follow Surface Flow", settings.surfaceFlow, 0f, 1f);
            settings.lift = EditorGUILayout.Slider("Lift", settings.lift, 0f, 1f);
            settings.seed = EditorGUILayout.IntField("Seed", settings.seed);
            using (new EditorGUI.DisabledScope(growthVertices == 0 || group.locked))
            {
                if (GUILayout.Button($"1. Preview {settings.guideCount:N0} Generated Guides", GUILayout.Height(32f)))
                    stage.GenerateGuidePreview();
            }
            if (group.locked)
                EditorGUILayout.HelpBox("Unlock the active group before accepting or manually adding guides.",
                    MessageType.Warning);

            HairGuideGenerationResult preview = stage.GenerationPreview;
            if (preview != null)
            {
                EditorGUILayout.HelpBox(
                    $"Dashed preview: {preview.guides.Count:N0} guides. {preview.rejectedBySpacing:N0} spacing rejections, {preview.rejectedByMask:N0} mask rejections. The groom is unchanged until you accept.",
                    preview.guides.Count > 0 ? MessageType.Info : MessageType.Error);
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
            EditorGUILayout.LabelField($"Authored Guides ({group.guides.Count})", EditorStyles.boldLabel);
            int listLimit = Mathf.Min(group.guides.Count, 200);
            for (int i = 0; i < listLimit; i++)
            {
                HairGuide guide = group.guides[i];
                if (guide == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool active = guide.Id == stage.ActiveGuideId;
                    bool selected = GUILayout.Toggle(active, guide.name, "Button");
                    if (selected && !active) stage.SetActiveGuide(guide.Id);
                    GUILayout.Label($"{guide.points.Count} pts", EditorStyles.miniLabel, GUILayout.Width(42f));
                    if (GUILayout.Button("×", GUILayout.Width(22f))) HairGroomCommands.DeleteGuide(stage.Groom, guide.Id);
                }
            }
            if (group.guides.Count > listLimit) EditorGUILayout.LabelField($"Showing first {listLimit} guides.", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(group.guides.Count == 0))
            {
                if (GUILayout.Button("Continue to Groom", GUILayout.Height(30f)))
                    stage.WorkflowStep = HairWorkflowStep.Groom;
            }
        }

        private void DrawGroom(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("4. Groom", "Sculpt authored guides on non-destructive layers, then refine with ordered modifiers and helpers.");
            if (group.guides.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "This group has no authored guides to style. Generate and accept guides, or place guides manually, before grooming.",
                    MessageType.Error);
                if (GUILayout.Button("Go to Guides", GUILayout.Height(30f)))
                    stage.WorkflowStep = HairWorkflowStep.Guides;
                return;
            }
            EditorGUILayout.LabelField("Essential Brush Shelf", EditorStyles.boldLabel);
            HairSceneTool[] tools =
            {
                HairSceneTool.Comb, HairSceneTool.Grab, HairSceneTool.Smooth, HairSceneTool.Length,
                HairSceneTool.Cut, HairSceneTool.Width, HairSceneTool.Clump, HairSceneTool.Part, HairSceneTool.Freeze
            };
            for (int row = 0; row < 3; row++)
            {
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
            stage.BrushRadius = EditorGUILayout.Slider("Radius", stage.BrushRadius,
                HairBrushInteractionUtility.MinimumRadius, HairBrushInteractionUtility.MaximumRadius);
            stage.BrushHardness = EditorGUILayout.Slider(new GUIContent("Hardness",
                "Full-strength inner radius followed by a linear falloff to the outer brush ring."),
                stage.BrushHardness, 0f, 1f);
            stage.BrushStrength = EditorGUILayout.Slider("Strength", stage.BrushStrength, 0.01f, 1f);
            stage.PaintErase = EditorGUILayout.Toggle("Reverse / Erase", stage.PaintErase);
            EditorGUILayout.HelpBox(
                "Shift + right-drag: horizontal changes radius, vertical changes hardness. [ and ] adjust radius; Shift + [ and ] adjust hardness.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Sculpt Layers", EditorStyles.boldLabel);
            for (int i = group.sculptLayers.Count - 1; i >= 0; i--)
            {
                HairSculptLayer layer = group.sculptLayers[i];
                if (layer == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool active = layer.Id == stage.ActiveLayerId;
                    bool selected = GUILayout.Toggle(active, layer.name, "Button");
                    if (selected && !active) stage.SetActiveLayer(layer.Id);
                    bool visible = GUILayout.Toggle(layer.visible, "V", "Button", GUILayout.Width(24f));
                    bool locked = GUILayout.Toggle(layer.locked, "L", "Button", GUILayout.Width(24f));
                    if (visible != layer.visible || locked != layer.locked)
                    {
                        Undo.RecordObject(stage.Groom, "Change Hair Sculpt Layer State");
                        layer.visible = visible;
                        layer.locked = locked;
                        HairGroomCommands.Commit(stage.Groom);
                    }
                }
            }
            if (GUILayout.Button("+ Sculpt Layer"))
            {
                HairSculptLayer layer = HairGroomCommands.AddSculptLayer(stage.Groom, group);
                stage.SetActiveLayer(layer.Id);
            }
            HairSculptLayer activeLayer = group.sculptLayers.Find(layer =>
                layer != null && layer.Id == stage.ActiveLayerId);
            if (activeLayer != null)
            {
                float opacity = EditorGUILayout.Slider("Active Layer Opacity", activeLayer.opacity, 0f, 1f);
                HairSculptBlendMode blend = (HairSculptBlendMode)EditorGUILayout.EnumPopup(
                    "Active Layer Blend", activeLayer.blendMode);
                if (!Mathf.Approximately(opacity, activeLayer.opacity) || blend != activeLayer.blendMode)
                {
                    Undo.RecordObject(stage.Groom, "Edit Hair Sculpt Layer");
                    activeLayer.opacity = opacity;
                    activeLayer.blendMode = blend;
                    HairGroomCommands.Commit(stage.Groom);
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Modifier Stack", EditorStyles.boldLabel);
            for (int i = 0; i < group.modifiers.Count; i++) DrawModifier(stage, group.modifiers[i], i);
            if (GUILayout.Button("+ Modifier…")) ShowModifierMenu(stage);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Helpers & Constraints", EditorStyles.boldLabel);
            for (int i = 0; i < stage.Groom.SharedHelpers.Count; i++)
            {
                HairHelper helper = stage.Groom.SharedHelpers[i];
                if (helper == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool active = helper.Id == stage.ActiveHelperId;
                    bool selected = GUILayout.Toggle(active, helper.name, "Button");
                    if (selected && !active)
                        stage.SetActiveHelper(helper.Id);
                    GUILayout.Label(ObjectNames.NicifyVariableName(helper.type.ToString()), EditorStyles.miniLabel);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Curve Rail"))
                    stage.SetActiveHelper(HairGroomCommands.AddHelper(stage.Groom, HairHelperType.CurveRail,
                        stage.Groom.SourceMesh.bounds.center).Id);
                if (GUILayout.Button("+ Collider"))
                    stage.SetActiveHelper(HairGroomCommands.AddHelper(stage.Groom, HairHelperType.Sphere,
                        stage.Groom.SourceMesh.bounds.center).Id);
            }
            sceneHelperCandidate = (GameObject)EditorGUILayout.ObjectField("Scene Helper Object",
                sceneHelperCandidate, typeof(GameObject), true);
            if (GUILayout.Button("Bind Scene Object as Curve Rail"))
            {
                GameObject selected = sceneHelperCandidate != null ? sceneHelperCandidate : Selection.activeGameObject;
                if (selected != null)
                {
                    HairHelper helper = HairGroomCommands.BindSceneHelper(stage.Groom, selected,
                        HairHelperType.CurveRail);
                    if (helper != null) stage.SetActiveHelper(helper.Id);
                }
                else EditorUtility.DisplayDialog("Bind Hair Helper", "Select a scene GameObject first.", "OK");
            }
            HairHelper activeHelper = stage.Groom.FindHelper(stage.ActiveHelperId);
            if (activeHelper != null)
            {
                EditorGUILayout.HelpBox(
                    $"Active helper: {activeHelper.name}. Move it with the Scene gizmo, then add a constraint to make the group follow it.",
                    MessageType.Info);
                if (GUILayout.Button("Constrain Active Group to Helper"))
                    HairGroomCommands.AddConstraint(stage.Groom, group, HairConstraintType.FollowCurve, activeHelper);
            }

            if (group.constraints.Count > 0)
            {
                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField("Active Group Constraints", EditorStyles.boldLabel);
                for (int constraintIndex = 0; constraintIndex < group.constraints.Count; constraintIndex++)
                {
                    HairConstraintSettings constraint = group.constraints[constraintIndex];
                    if (constraint == null) continue;
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            bool enabled = EditorGUILayout.Toggle(constraint.enabled, GUILayout.Width(18f));
                            EditorGUILayout.LabelField(constraint.name, EditorStyles.boldLabel);
                            if (GUILayout.Button("X", GUILayout.Width(22f)))
                            {
                                HairGroomCommands.RemoveConstraint(stage.Groom, group, constraint.Id);
                                break;
                            }
                            if (enabled != constraint.enabled)
                            {
                                Undo.RecordObject(stage.Groom, "Toggle Hair Constraint");
                                constraint.enabled = enabled;
                                HairGroomCommands.Commit(stage.Groom);
                            }
                        }
                        float weight = EditorGUILayout.Slider("Weight", constraint.weight, 0f, 1f);
                        HairConstraintType type = (HairConstraintType)EditorGUILayout.EnumPopup(
                            "Type", constraint.type);
                        if (!Mathf.Approximately(weight, constraint.weight) || type != constraint.type)
                        {
                            Undo.RecordObject(stage.Groom, "Edit Hair Constraint");
                            constraint.weight = weight;
                            constraint.type = type;
                            HairGroomCommands.Commit(stage.Groom);
                        }
                    }
                }
            }

            if (GUILayout.Button("Continue to Cards", GUILayout.Height(28f))) stage.WorkflowStep = HairWorkflowStep.Cards;
        }

        private static void DrawCards(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("5. Cards", "Choose the generated card profile, child population, atlas, UV regions, and preview material.");
            if (group.guides.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Cards require authored guides. Return to Guides, generate a preview, and accept it before configuring card output.",
                    MessageType.Error);
                if (GUILayout.Button("Go to Guides", GUILayout.Height(30f)))
                    stage.WorkflowStep = HairWorkflowStep.Guides;
                return;
            }
            HairCardProfileAsset profile = (HairCardProfileAsset)EditorGUILayout.ObjectField("Card Profile", group.profile,
                typeof(HairCardProfileAsset), false);
            HairAtlasProfileAsset atlas = (HairAtlasProfileAsset)EditorGUILayout.ObjectField("Atlas Profile", group.atlas,
                typeof(HairAtlasProfileAsset), false);
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
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Profile & Meshing", EditorStyles.boldLabel);
                HairCardShape shape = (HairCardShape)EditorGUILayout.EnumPopup("Card Shape", profile.Shape);
                float rootWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Root Width", profile.DefaultWidth));
                float tipWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Tip Width", profile.TipWidth));
                int samples = EditorGUILayout.IntSlider("Samples per Card", profile.SamplesPerCard, 2, 64);
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
                    profile.Configure(shape, rootWidth, tipWidth, samples, sides, doubleSided);
                    EditorUtility.SetDirty(profile);
                    stage.QueueRebuild();
                }
            }

            EditorGUILayout.Space(8f);
            DrawAtlasSettings(stage, group);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Child Cards", EditorStyles.boldLabel);
            HairChildSettings children = group.children;
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
            int estimate = group.guides.Count * (children.childrenPerGuide + (children.includeGuideCard ? 1 : 0));
            EditorGUILayout.HelpBox($"{group.guides.Count} guides × ({children.childrenPerGuide} children + {(children.includeGuideCard ? "1 guide card" : "no guide card")}) ≈ {estimate:N0} cards.", MessageType.Info);
            if (stage.Evaluation != null)
                EditorGUILayout.LabelField($"Current evaluated output: {stage.Evaluation.CardCount:N0} cards " +
                                           $"({stage.Evaluation.guideCurveCount:N0} guide cards + " +
                                           $"{stage.Evaluation.childCurveCount:N0} children).",
                    EditorStyles.wordWrappedMiniLabel);
            stage.PreviewMode = (HairPreviewMode)EditorGUILayout.EnumPopup("Preview", stage.PreviewMode);
            if (GUILayout.Button("Rebuild Card Preview", GUILayout.Height(28f))) stage.QueueRebuild(true);
            if (GUILayout.Button("Continue to Optimize", GUILayout.Height(28f))) stage.WorkflowStep = HairWorkflowStep.Optimize;
        }

        private static void DrawAtlasSettings(HairCardStage stage, HairGroup group)
        {
            EditorGUILayout.LabelField("Atlas & UV Areas", EditorStyles.boldLabel);
            HairAtlasProfileAsset atlas = group.atlas;
            if (atlas == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an Atlas Profile to place card UVs over named areas of a hair texture. Without one, every card uses the full 0-1 UV range.",
                    MessageType.Warning);
                if (GUILayout.Button("Create Atlas Profile"))
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
            Texture2D normal = (Texture2D)EditorGUILayout.ObjectField("Normal Atlas", atlas.normal,
                typeof(Texture2D), false);
            Texture2D mask = (Texture2D)EditorGUILayout.ObjectField("Mask Atlas", atlas.mask,
                typeof(Texture2D), false);
            Material material = (Material)EditorGUILayout.ObjectField("Card Material", atlas.material,
                typeof(Material), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(atlas, "Edit Hair Atlas Textures");
                atlas.albedo = albedo;
                atlas.normal = normal;
                atlas.mask = mask;
                atlas.material = material;
                EditorUtility.SetDirty(atlas);
                stage.QueueRebuild();
            }

            DrawAtlasPreview(atlas, group);
            if (GUILayout.Button("Open UV Area Editor...", GUILayout.Height(28f)))
                HairAtlasRegionEditorWindow.Open(atlas);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Card Area Assignment", EditorStyles.boldLabel);
            int selection = GUILayout.Toolbar((int)group.atlasRegionSelection,
                new[] { "Use All UV Areas", "Use Selected UV Areas" });
            HairAtlasRegionSelectionMode selectionMode = (HairAtlasRegionSelectionMode)selection;
            if (selectionMode != group.atlasRegionSelection)
            {
                Undo.RecordObject(stage.Groom, "Change Hair UV Area Assignment");
                group.atlasRegionSelection = selectionMode;
                HairGroomCommands.Commit(stage.Groom);
            }

            int selectedCount = CountValidSelectedRegions(group, atlas);
            if (group.atlasRegionSelection == HairAtlasRegionSelectionMode.Selected && selectedCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Select at least one UV area below. Cards have no valid atlas area until a selection is made.",
                    MessageType.Error);
            }
            else
            {
                string assignment = group.atlasRegionSelection == HairAtlasRegionSelectionMode.All
                    ? $"Each card deterministically chooses among all {atlas.regions.Count} areas using the area weights."
                    : $"Each card deterministically chooses among the {selectedCount} selected areas using their weights.";
                EditorGUILayout.HelpBox(assignment + " Rebuilding with the same groom and seed keeps the assignments stable.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"UV Areas ({atlas.regions.Count})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Rectangles use normalized atlas UV coordinates. Weight controls how often an eligible area is chosen.",
                EditorStyles.wordWrappedMiniLabel);

            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region == null) continue;
                DrawAtlasRegion(stage, group, atlas, region, regionIndex);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ UV Area"))
                {
                    Undo.RecordObject(atlas, "Add Hair UV Area");
                    atlas.CreateRegion($"Area {atlas.regions.Count + 1}", new Rect(0f, 0f, 1f, 1f));
                    EditorUtility.SetDirty(atlas);
                    stage.QueueRebuild();
                }
                using (new EditorGUI.DisabledScope(atlas.regions.Count == 0))
                {
                    if (GUILayout.Button("Select All for This Group"))
                    {
                        Undo.RecordObject(stage.Groom, "Select All Hair UV Areas");
                        group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                        group.atlasRegionIds.Clear();
                        for (int i = 0; i < atlas.regions.Count; i++)
                        {
                            HairAtlasRegion region = atlas.regions[i];
                            if (region != null) group.atlasRegionIds.Add(region.Id);
                        }
                        HairGroomCommands.Commit(stage.Groom);
                    }
                }
            }
        }

        private static void DrawAtlasRegion(
            HairCardStage stage,
            HairGroup group,
            HairAtlasProfileAsset atlas,
            HairAtlasRegion region,
            int regionIndex)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = group.atlasRegionIds.Contains(region.Id);
                    using (new EditorGUI.DisabledScope(group.atlasRegionSelection == HairAtlasRegionSelectionMode.All))
                    {
                        bool nextSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(18f));
                        if (nextSelected != selected)
                        {
                            Undo.RecordObject(stage.Groom, "Assign Hair UV Area");
                            if (nextSelected) group.atlasRegionIds.Add(region.Id);
                            else group.atlasRegionIds.Remove(region.Id);
                            HairGroomCommands.Commit(stage.Groom);
                        }
                    }
                    EditorGUILayout.LabelField($"Area {regionIndex + 1}", EditorStyles.boldLabel,
                        GUILayout.Width(55f));
                    string regionName = EditorGUILayout.TextField(region.name);
                    if (GUILayout.Button("Remove", GUILayout.Width(62f)))
                    {
                        Undo.RecordObjects(new UnityEngine.Object[] { atlas, stage.Groom }, "Remove Hair UV Area");
                        atlas.regions.RemoveAt(regionIndex);
                        for (int groupIndex = 0; groupIndex < stage.Groom.Groups.Count; groupIndex++)
                            stage.Groom.Groups[groupIndex]?.atlasRegionIds?.Remove(region.Id);
                        EditorUtility.SetDirty(atlas);
                        HairGroomCommands.Commit(stage.Groom);
                        return;
                    }

                    if (regionName != region.name)
                    {
                        Undo.RecordObject(atlas, "Rename Hair UV Area");
                        region.name = string.IsNullOrWhiteSpace(regionName) ? $"Area {regionIndex + 1}" : regionName;
                        EditorUtility.SetDirty(atlas);
                    }
                }

                Rect uvRect = EditorGUILayout.RectField("UV Rectangle", region.uvRect);
                float weight = Mathf.Max(0f, EditorGUILayout.FloatField("Selection Weight", region.weight));
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool flipU = EditorGUILayout.Toggle("Flip U", region.flipU);
                    bool flipV = EditorGUILayout.Toggle("Flip V", region.flipV);
                    if (uvRect != region.uvRect || !Mathf.Approximately(weight, region.weight) ||
                        flipU != region.flipU || flipV != region.flipV)
                    {
                        Undo.RecordObject(atlas, "Edit Hair UV Area");
                        region.uvRect = uvRect;
                        region.weight = weight;
                        region.flipU = flipU;
                        region.flipV = flipV;
                        region.EnsureIntegrity();
                        EditorUtility.SetDirty(atlas);
                        stage.QueueRebuild();
                    }
                }

                string tags = string.Join(", ", region.tags ?? Array.Empty<string>());
                string nextTags = EditorGUILayout.TextField("Tags", tags);
                if (!string.Equals(tags, nextTags, StringComparison.Ordinal))
                {
                    Undo.RecordObject(atlas, "Edit Hair UV Area Tags");
                    region.tags = ParseTags(nextTags);
                    EditorUtility.SetDirty(atlas);
                }
            }
        }

        private static void DrawAtlasPreview(HairAtlasProfileAsset atlas, HairGroup group)
        {
            Texture displayTexture = HairAtlasRegionEditorWindow.ResolveDisplayTexture(atlas);
            float aspect = displayTexture != null && displayTexture.height > 0
                ? displayTexture.width / (float)displayTexture.height
                : 1f;
            Rect preview = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MaxWidth(520f));
            EditorGUI.DrawRect(preview, new Color(0.12f, 0.12f, 0.12f, 1f));
            if (displayTexture != null) GUI.DrawTexture(preview, displayTexture, ScaleMode.StretchToFill, false);

            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region == null) continue;
                Rect uv = region.uvRect;
                Rect outline = new Rect(
                    preview.x + uv.x * preview.width,
                    preview.y + (1f - uv.y - uv.height) * preview.height,
                    uv.width * preview.width,
                    uv.height * preview.height);
                bool eligible = group.atlasRegionSelection == HairAtlasRegionSelectionMode.All ||
                                group.atlasRegionIds.Contains(region.Id);
                Color color = eligible ? new Color(0.1f, 0.9f, 1f, 1f) : new Color(0.45f, 0.45f, 0.45f, 1f);
                DrawOutline(outline, color, 2f);
                GUI.Label(outline, (regionIndex + 1).ToString(), labelStyle);
            }
        }

        private static void DrawOutline(Rect rectangle, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rectangle.x, rectangle.y, rectangle.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rectangle.x, rectangle.yMax - thickness, rectangle.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rectangle.x, rectangle.y, thickness, rectangle.height), color);
            EditorGUI.DrawRect(new Rect(rectangle.xMax - thickness, rectangle.y, thickness, rectangle.height), color);
        }

        private static int CountValidSelectedRegions(HairGroup group, HairAtlasProfileAsset atlas)
        {
            if (group.atlasRegionIds == null || atlas.regions == null) return 0;
            int count = 0;
            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region != null && group.atlasRegionIds.Contains(region.Id)) count++;
            }
            return count;
        }

        private static string[] ParseTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags)) return Array.Empty<string>();
            string[] parts = tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> parsed = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string tag = parts[i].Trim();
                if (!string.IsNullOrEmpty(tag) && !parsed.Contains(tag)) parsed.Add(tag);
            }
            return parsed.ToArray();
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
                    int samples = EditorGUILayout.IntSlider("Samples per Card", lod.samplesPerCard, 2, 32);
                    int tubeSides = EditorGUILayout.IntSlider("Maximum Tube Sides", lod.maximumTubeSides, 3, 12);
                    float screen = EditorGUILayout.Slider("Screen Height", lod.screenRelativeHeight, 0f, 1f);
                    HairLodReductionMode mode = (HairLodReductionMode)EditorGUILayout.EnumPopup("Reduction", lod.reductionMode);
                    if (!Mathf.Approximately(fraction, lod.cardFraction) || samples != lod.samplesPerCard ||
                        tubeSides != lod.maximumTubeSides ||
                        !Mathf.Approximately(screen, lod.screenRelativeHeight) || mode != lod.reductionMode)
                    {
                        Undo.RecordObject(groom, "Edit Hair LOD");
                        lod.cardFraction = fraction;
                        lod.samplesPerCard = samples;
                        lod.maximumTubeSides = tubeSides;
                        lod.screenRelativeHeight = screen;
                        lod.reductionMode = mode;
                        HairGroomCommands.Commit(groom);
                    }
                }
            }
            if (GUILayout.Button("+ LOD")) HairGroomCommands.AddLod(groom);
            EditorGUILayout.HelpBox("Bake transfers the closest available scalp bone weights to every generated card vertex. Inspect deformation on the equipped preview avatar before shipping.", MessageType.Info);
            if (GUILayout.Button("Continue to Validate & Bake", GUILayout.Height(28f)))
                stage.WorkflowStep = HairWorkflowStep.ValidateAndBake;
        }

        private void DrawValidateAndBake(HairCardStage stage)
        {
            HairGroomAsset groom = stage.Groom;
            DrawStepTitle("7. Validate & Bake", "Review release blockers and create Unity Mesh, UMA Slot, Overlay, recipe, and every configured LOD in one transaction.");
            if (GUILayout.Button("Validate All", GUILayout.Height(28f))) stage.QueueRebuild(true);
            HairValidationReport report = stage.Validation;
            if (report != null)
            {
                EditorGUILayout.HelpBox(report.CanBake
                    ? $"Ready to bake: {report.cardCount:N0} cards, {report.vertexCount:N0} vertices, {report.triangleCount:N0} triangles."
                    : $"Resolve {report.ErrorCount} blocking error(s) before baking.",
                    report.CanBake ? MessageType.Info : MessageType.Error);
                for (int i = 0; i < report.issues.Count; i++)
                {
                    HairValidationIssue issue = report.issues[i];
                    MessageType type = issue.severity == HairValidationSeverity.Error ? MessageType.Error :
                        issue.severity == HairValidationSeverity.Warning ? MessageType.Warning : MessageType.Info;
                    EditorGUILayout.HelpBox(issue.message, type);
                }
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
                if (GUILayout.Button("Dry Run", GUILayout.Height(30f))) lastBake = HairBakePipeline.DryRun(groom);
                using (new EditorGUI.DisabledScope(report != null && !report.CanBake))
                {
                    if (GUILayout.Button("Bake", GUILayout.Height(30f)))
                        lastBake = HairBakePipeline.Bake(groom, stage.SourceAvatar);
                }
            }
            if (lastBake != null)
            {
                EditorGUILayout.HelpBox(lastBake.succeeded
                    ? $"Bake completed: {lastBake.assets.Count} asset(s), {lastBake.cardCount:N0} cards, {lastBake.triangleCount:N0} triangles."
                    : "Bake did not commit output assets.", lastBake.succeeded ? MessageType.Info : MessageType.Warning);
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

        private static void DrawModifier(HairCardStage stage, HairModifierSettings modifier, int index)
        {
            if (modifier == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool enabled = EditorGUILayout.Toggle(modifier.enabled, GUILayout.Width(18f));
                    EditorGUILayout.LabelField($"{index + 1}. {modifier.name}", EditorStyles.boldLabel);
                    if (enabled != modifier.enabled)
                    {
                        Undo.RecordObject(stage.Groom, "Toggle Hair Modifier");
                        modifier.enabled = enabled;
                        HairGroomCommands.Commit(stage.Groom);
                    }
                }
                float weight = EditorGUILayout.Slider("Weight", modifier.weight, 0f, 1f);
                float amount = EditorGUILayout.FloatField("Amount", modifier.amount);
                HairModifierDomain domain = (HairModifierDomain)EditorGUILayout.EnumPopup("Domain", modifier.domain);
                Vector3 vector = modifier.vector;
                if (ModifierUsesVector(modifier.type))
                    vector = EditorGUILayout.Vector3Field("Direction / Parameters", modifier.vector);
                int seed = modifier.seed;
                if (modifier.type == HairModifierType.Noise || modifier.type == HairModifierType.Curl ||
                    modifier.type == HairModifierType.Wave)
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
                    int selectedHelper = Mathf.Max(0, helperIds.IndexOf(modifier.helperId));
                    selectedHelper = EditorGUILayout.Popup("Helper", selectedHelper, helperNames.ToArray());
                    helperId = helperIds[selectedHelper];
                }
                if (!Mathf.Approximately(weight, modifier.weight) || !Mathf.Approximately(amount, modifier.amount) ||
                    domain != modifier.domain || vector != modifier.vector || seed != modifier.seed ||
                    helperId != modifier.helperId)
                {
                    Undo.RecordObject(stage.Groom, "Edit Hair Modifier");
                    modifier.weight = weight;
                    modifier.amount = amount;
                    modifier.domain = domain;
                    modifier.vector = vector;
                    modifier.seed = seed;
                    modifier.helperId = helperId;
                    HairGroomCommands.Commit(stage.Groom);
                }
            }
        }

        private static bool ModifierUsesVector(HairModifierType type)
        {
            return type == HairModifierType.FlowAlign || type == HairModifierType.Lift ||
                   type == HairModifierType.Part || type == HairModifierType.Curl ||
                   type == HairModifierType.Wave || type == HairModifierType.Gravity;
        }

        private static bool ModifierUsesHelper(HairModifierType type)
        {
            return type == HairModifierType.HelperFollow || type == HairModifierType.Collision ||
                   type == HairModifierType.PushOut || type == HairModifierType.TrimByMesh;
        }

        private static void DrawStatus(HairCardStage stage)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                HairEvaluationResult evaluation = stage.Evaluation;
                HairCardMeshBuildResult mesh = stage.MeshBuild;
                GUILayout.Label($"{ObjectNames.NicifyVariableName(stage.SceneTool.ToString())}: {stage.ActionStatus}",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Guides {CountGuides(stage.Groom):N0}  Cards {evaluation?.CardCount ?? 0:N0}  Tris {mesh?.triangleCount ?? 0:N0}",
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawStepTitle(string title, string description)
        {
            EditorGUILayout.LabelField(title, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(7f);
        }

        private static void ShowAddGroupMenu(HairCardStage stage)
        {
            GenericMenu menu = new GenericMenu();
            foreach (HairGroupRole role in Enum.GetValues(typeof(HairGroupRole)))
            {
                HairGroupRole captured = role;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(role.ToString())), false, () =>
                {
                    HairGroup group = HairGroomCommands.AddGroup(stage.Groom, captured);
                    stage.SetActiveGroup(group.Id);
                });
            }
            menu.ShowAsContext();
        }

        private static void ShowAddMapMenu(HairCardStage stage)
        {
            GenericMenu menu = new GenericMenu();
            foreach (HairMapKind kind in Enum.GetValues(typeof(HairMapKind)))
            {
                HairMapKind captured = kind;
                bool exists = stage.ActiveGroup.FindMap(captured) != null && captured != HairMapKind.Custom;
                if (exists) menu.AddDisabledItem(new GUIContent(ObjectNames.NicifyVariableName(captured.ToString())));
                else menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(captured.ToString())), false, () =>
                {
                    HairGrowthMap map = HairGroomCommands.EnsureMap(stage.Groom, stage.ActiveGroup, captured);
                    stage.SetActiveMap(map.Id);
                });
            }
            menu.ShowAsContext();
        }

        private static void ShowModifierMenu(HairCardStage stage)
        {
            GenericMenu menu = new GenericMenu();
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                HairModifierType captured = type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.ToString())), false,
                    () => HairGroomCommands.AddModifier(stage.Groom, stage.ActiveGroup, captured));
            }
            menu.ShowAsContext();
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
