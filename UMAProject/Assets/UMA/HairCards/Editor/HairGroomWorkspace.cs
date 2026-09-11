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
        private GameObject sceneHelperCandidate;
        [SerializeField] private string visibilitySearch = string.Empty;
        [SerializeField] private bool recipeVisibilityExpanded = true;
        [SerializeField] private bool udimVisibilityExpanded = true;
        [SerializeField] private bool slotVisibilityExpanded = true;
        [SerializeField] private HairAtlasEditorPanel atlasEditor = new HairAtlasEditorPanel();
        [SerializeField] private bool cardGeometryExpanded;
        [SerializeField] private bool childSettingsExpanded;
        [SerializeField] private bool atlasMapsExpanded;
        [SerializeField] private string guideSearch = string.Empty;
        [SerializeField] private int guidePage;
        [SerializeField] private bool guideLibraryExpanded;
        [SerializeField] private List<string> collapsedLayerIds = new List<string>();
        [SerializeField] private Vector2 layerStackScroll;
        private bool confirmGuideDelete;
        private bool resetOptionsExpanded;
        private bool confirmSetupReset;
        private double nextPreferencesSave;
        private string preferenceContext;
        private HairGroomAsset preferenceGroom;

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

        internal static void SaveOpenPreferences()
        {
            if (HairEditorPreferences.Suspended) return;
            foreach (HairGroomWorkspace window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>())
            {
                HairEditorPreferences.instance.Remember(window, "workspace", window.preferenceContext);
                window.atlasEditor?.SavePreferences();
            }
        }

        internal static void SelectUvSet(HairAtlasProfileAsset atlas, string id)
        {
            foreach (HairGroomWorkspace window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>())
            { window.atlasEditor?.SelectSet(atlas, id); window.Repaint(); }
        }

        private void OnEnable()
        {
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

            DrawHeader(stage);
            DrawResetOptions(stage);
            int selectedStep = GUILayout.Toolbar((int)stage.WorkflowStep, StepNames, GUILayout.Height(28f));
            if (selectedStep != (int)stage.WorkflowStep)
            {
                atlasEditor?.CancelInteraction();
                stage.WorkflowStep = (HairWorkflowStep)selectedStep;
            }
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
            Event current = Event.current;
            stage.SetWorkspaceInteraction(GUIUtility.hotControl != 0 || EditorGUIUtility.editingTextField ||
                atlasEditor?.IsInteracting == true, current != null &&
                (current.rawType == EventType.MouseDown || current.rawType == EventType.MouseDrag || current.rawType == EventType.KeyDown));
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

        private void DrawResetOptions(HairCardStage stage)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Settings saved automatically", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Reset to defaults", "Show separate reset controls for editor preferences and card setup."), GUILayout.Width(130f)))
                    resetOptionsExpanded = !resetOptionsExpanded;
            }
            if (!resetOptionsExpanded) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Settings are remembered automatically. Existing grooms keep their own setup; new grooms inherit the last-used setup.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset editor options"))
                    {
                        stage.ResetEditorPreferences();
                        foreach (HairGroomWorkspace window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>())
                        { HairPreferenceCodec.Reset(window); window.atlasEditor?.ResetPreferences(); window.Repaint(); }
                        HairAtlasRegionEditorWindow.ResetOpenPreferences();
                        confirmSetupReset = false;
                    }
                    using (new EditorGUI.DisabledScope(stage.ActiveGroup == null || stage.ActiveGroup.locked || !EditorUtility.IsPersistent(stage.Groom)))
                        if (GUILayout.Button("Reset card setup")) confirmSetupReset = !confirmSetupReset;
                    if (GUILayout.Button("Forget new-groom defaults"))
                    { stage.SaveNow(false); HairEditorPreferences.instance.ClearSetupDefaults(); }
                }
                EditorGUILayout.LabelField("Editor reset clears remembered visibility, brushes, generation, and workspace/UV display options. Guides and painted maps are never deleted.", EditorStyles.wordWrappedMiniLabel);
                if (confirmSetupReset)
                {
                    EditorGUILayout.HelpBox("Reset the active group's profile, textures/material assignments, UV sets, children and root inset, plus this groom's LOD/bake/symmetry options? New private resources are created; old assets, other groups, painted maps, guides and sculpt layers are kept. Undo restores the old assignments.", MessageType.Warning);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Confirm setup reset"))
                        {
                            atlasEditor?.CancelInteraction();
                            HairEditorPreferences.instance.ResetCurrentSetup(stage.Groom, stage.ActiveGroup);
                            confirmSetupReset = false;
                            stage.SaveNow();
                        }
                        if (GUILayout.Button("Cancel")) confirmSetupReset = false;
                    }
                }
            }
        }

        private void DrawHeader(HairCardStage stage)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(stage.Groom.name, EditorStyles.boldLabel);
                GUILayout.Label(stage.SaveStatus, EditorStyles.miniLabel);
                GUILayout.Label(stage.Groom.SourceRace, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                HairValidationReport validation = stage.ReleaseValidation;
                if (validation != null)
                {
                    GUILayout.Label($"Release: {validation.ErrorCount} errors  {validation.WarningCount} warnings" +
                        (stage.ReleaseValidationIsCurrent ? "" : " (outdated)"),
                        validation.ErrorCount > 0 ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                }
                if (GUILayout.Button(new GUIContent("?", "Open Hair Cards - Quick Start"),
                        EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    OpenQuickStart();
                if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(48f))) stage.FrameGroom();
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    stage.WorkflowStep = HairWorkflowStep.ValidateAndBake;
                    stage.ValidateReleaseNow();
                }
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45f))) stage.SaveNow();
                using (new EditorGUI.DisabledScope(false))
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
                DrawPreviewDisplay(stage);
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
            DrawPreviewDisplay(stage);
            if (stage.IsolateSelectedGuides || !string.IsNullOrEmpty(stage.SoloLayerId))
                EditorGUILayout.HelpBox("Isolation / solo affects preview only. All enabled guides and visible layers are still included in release output.", MessageType.Info);
            DrawAvatarVisibility(stage);
        }

        private static void DrawPreviewDisplay(HairCardStage stage)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Preview & Display", EditorStyles.boldLabel);
            // Stacked fields stay readable in the 205-pixel-wide explorer.
            EditorGUILayout.LabelField("Preview mode", EditorStyles.miniLabel);
            stage.PreviewMode = (HairPreviewMode)EditorGUILayout.EnumPopup(stage.PreviewMode);
            EditorGUILayout.LabelField("Preview quality", EditorStyles.miniLabel);
            stage.PreviewQuality = (HairPreviewQuality)EditorGUILayout.EnumPopup(stage.PreviewQuality);
            stage.ShowChildren = EditorGUILayout.ToggleLeft(new GUIContent("Include children in preview",
                "Evaluates generated children for child-spline and card previews. Turn off for a lighter, guide-only preview. " +
                "Does not change baked output. Use Show child splines below to hide only their lines."), stage.ShowChildren);
            EditorGUILayout.LabelField(stage.PreviewStatus, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Rebuild Card Preview"))
            {
                stage.PreviewMode = HairPreviewMode.Cards;
                stage.QueueRebuild(true);
            }
            EditorGUILayout.Space(5f);
            stage.ShowCardWireframe = EditorGUILayout.ToggleLeft(new GUIContent("Show card wireframe",
                "Shows yellow UV-set card outlines and Unity's selected-card outline. Hide for a clean material preview; card picking still works. Does not affect baking."), stage.ShowCardWireframe);
            EditorGUILayout.LabelField("Guides & Splines", EditorStyles.miniBoldLabel);
            stage.ShowGuideRoots = EditorGUILayout.ToggleLeft(new GUIContent("Show root handles",
                "Shows the clickable handle at each guide root."), stage.ShowGuideRoots);
            using (new EditorGUI.DisabledScope(!stage.ShowGuideRoots))
            {
                stage.RootHandleScale = EditorGUILayout.Slider(new GUIContent("Root Handle Size",
                    "Scales the root selection handles without changing the groom or generated cards."),
                    stage.RootHandleScale, 0.1f, 4f);
            }
            stage.ShowGuideSplines = EditorGUILayout.ToggleLeft(new GUIContent("Show guide splines",
                "Shows the authored/evaluated guide curves in the Scene view."), stage.ShowGuideSplines);
            stage.ShowChildSplines = EditorGUILayout.ToggleLeft(new GUIContent("Show child splines",
                "Shows the faint dotted child curves in Guides And Children mode. Hiding these lines does not remove child cards or rebuild geometry."),
                stage.ShowChildSplines);
            if (stage.ShowChildSplines && (!stage.ShowChildren || stage.PreviewMode != HairPreviewMode.GuidesAndChildren))
                EditorGUILayout.LabelField("Child lines require Guides And Children mode and Include children in preview.",
                    EditorStyles.wordWrappedMiniLabel);
            stage.ShowControlPoints = EditorGUILayout.ToggleLeft(new GUIContent("Show selected control points",
                "Shows editable points for the selected guide."), stage.ShowControlPoints);
            stage.ShowFreezeMask = EditorGUILayout.ToggleLeft(new GUIContent("Show freeze mask",
                "Cyan is editable; pink is frozen. Also shown automatically by the Freeze tool."), stage.ShowFreezeMask);
            stage.DepthTestGuides = EditorGUILayout.ToggleLeft(new GUIContent("Use scene depth (Z-buffer)",
                "Occludes roots, splines, control points, and generated children behind visible geometry. " +
                "Disable for an X-ray view of the complete groom."), stage.DepthTestGuides);
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Scene Objects", EditorStyles.miniBoldLabel);
            stage.ShowScalp = EditorGUILayout.ToggleLeft(new GUIContent("Show authoring surface",
                "Shows the source scalp surface in the authoring stage."), stage.ShowScalp);
            using (new EditorGUI.DisabledScope(!stage.HasAvatarVisibility))
                stage.ShowAvatar = EditorGUILayout.ToggleLeft(new GUIContent("Show character preview",
                    "Shows the avatar parts selected under Avatar Visibility below. Requires a generated character preview."), stage.ShowAvatar);
            stage.ShowHelpers = EditorGUILayout.ToggleLeft(new GUIContent("Show helpers",
                "Shows grooming helper objects in the Scene view."), stage.ShowHelpers);
            EditorGUILayout.LabelField("Display settings are stage-only and do not affect the baked hair.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawBoneFocusButton(HairCardStage stage, HumanBodyBones bone, string label)
        {
            bool available = stage.CanFocusBone(bone);
            using (new EditorGUI.DisabledScope(!available))
                if (GUILayout.Button(new GUIContent(label, available
                    ? $"Focus the {bone} area in the preview pose with the camera 0.45 meters from the orbit pivot, keeping the current viewing angle and projection."
                    : $"No {bone} bone is available. Open the groom from a character with this bone.")))
                    stage.FocusBone(bone);
        }

        private void DrawAvatarVisibility(HairCardStage stage)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Avatar Visibility", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(stage.ActiveGroup == null || stage.Groom?.SourceMesh == null))
            {
                if (GUILayout.Button(new GUIContent("Focus current area",
                    "Frames the current group's painted Growth Area and its influencing bones in the authoring pose. " +
                    "Keeps the orbit pivot on the character's vertical axis and expands symmetrically to fit one-sided paint. " +
                    "Does not change visibility or the groom.")))
                    stage.FocusCurrentArea();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawBoneFocusButton(stage, HumanBodyBones.Head, "Focus Head");
                DrawBoneFocusButton(stage, HumanBodyBones.Neck, "Focus Neck");
            }
            if (!stage.HasAvatarVisibility)
            {
                EditorGUILayout.LabelField("Launch from a generated DynamicCharacterAvatar to hide its recipes, " +
                                           "UDIM groups, and slots.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

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
            stage.MirrorPaintX = EditorGUILayout.Toggle("Mirror growth paint X", stage.MirrorPaintX);
            stage.MirrorCutX = EditorGUILayout.Toggle("Mirror slice cut X", stage.MirrorCutX);
            EditorGUILayout.HelpBox("These are the active, stage-only mirror controls across source-local X = 0. " +
                "Comb/Grab do not have a global symmetry switch. Use a Mirror modifier for procedural guide mirroring.", MessageType.Info);

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
            stage.PaintValue = EditorGUILayout.FloatField("Paint Value", stage.PaintValue);
            EditorGUILayout.HelpBox(
                "Mirror X paints both sides across the source mesh local X = 0 plane (M toggles it). " +
                "Hold Shift while painting to erase temporarily; release it to restore the selected mode. " +
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
            if (GUILayout.Button("Continue to Guides", GUILayout.Height(28f))) stage.WorkflowStep = HairWorkflowStep.Guides;
        }

        private void DrawGuides(HairCardStage stage)
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
            EditorGUI.BeginChangeCheck();
            settings.guideCount = EditorGUILayout.IntSlider("Guide Count", settings.guideCount, 1, 1000);
            settings.pointsPerGuide = EditorGUILayout.IntSlider("Points per Guide", settings.pointsPerGuide, 2, 24);
            settings.defaultLength = EditorGUILayout.Slider("Default Length", settings.defaultLength, 0.01f, 1f);
            settings.minimumRootSpacing = EditorGUILayout.Slider("Minimum Spacing", settings.minimumRootSpacing, 0f, 0.2f);
            settings.rootUniformity = EditorGUILayout.Slider(new GUIContent("Root Uniformity",
                "0: original random placement. 1: compare more candidates to fill gaps and spread roots evenly. " +
                "Respects Growth Area, Density, and Minimum Spacing; higher values take longer to preview."), settings.rootUniformity, 0f, 1f);
            settings.surfaceFlow = EditorGUILayout.Slider("Follow Surface Flow", settings.surfaceFlow, 0f, 1f);
            settings.lift = EditorGUILayout.Slider("Lift", settings.lift, 0f, 1f);
            settings.seed = EditorGUILayout.IntField("Seed", settings.seed);
            if (EditorGUI.EndChangeCheck() && stage.GenerationPreview != null) stage.CancelGuidePreview();
            EditorGUILayout.LabelField("For even coverage, start with Root Uniformity 0.75–1 and low Minimum Spacing. " +
                "Preview again after changing settings; accepted guides are not moved automatically.", EditorStyles.wordWrappedMiniLabel);
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
            DrawGuideLibrary(stage);
            using (new EditorGUI.DisabledScope(group.guides.Count == 0))
            {
                if (GUILayout.Button("Continue to Groom", GUILayout.Height(30f)))
                    stage.WorkflowStep = HairWorkflowStep.Groom;
            }
        }

        private void DrawGuideLibrary(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            EditorGUILayout.LabelField($"Authored guides · {stage.SelectedGuideIds.Count:N0} selected", EditorStyles.boldLabel);
            string search = EditorGUILayout.TextField("Search guides", guideSearch, EditorStyles.toolbarSearchField);
            if (search != guideSearch) { guideSearch = search; guidePage = 0; }
            List<HairGuide> matches = group.guides.FindAll(guide => guide != null &&
                (string.IsNullOrWhiteSpace(guideSearch) || guide.name.IndexOf(guideSearch, StringComparison.OrdinalIgnoreCase) >= 0));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select matching")) stage.SetGuideSelection(matches.ConvertAll(guide => guide.Id));
                if (GUILayout.Button("Clear selection")) { stage.SetGuideSelection(null); confirmGuideDelete = false; }
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
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Duplicate selected")) stage.SetGuideSelection(HairGroomCommands.ApplyGuideBatch(stage.Groom, group, stage.SelectedGuideIds, HairGuideBatchAction.Duplicate));
                    if (GUILayout.Button("Delete selected…")) confirmGuideDelete = true;
                }
                if (confirmGuideDelete)
                {
                    EditorGUILayout.HelpBox($"Delete {stage.SelectedGuideIds.Count:N0} selected guides and their sculpt deltas? Undo can restore them.", MessageType.Warning);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Confirm delete"))
                        { HairGroomCommands.ApplyGuideBatch(stage.Groom, group, stage.SelectedGuideIds, HairGuideBatchAction.Delete); stage.SetGuideSelection(null); confirmGuideDelete = false; }
                        if (GUILayout.Button("Cancel")) confirmGuideDelete = false;
                    }
                }
            }
            if (stage.IsolateSelectedGuides && stage.SelectedGuideIds.Count == 0)
                EditorGUILayout.HelpBox("Isolation is empty. Select matching guides or turn off Isolate to see the groom.", MessageType.Info);
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
                "During a stroke, guides stay visible for editing. Use the Scene Preview menu for a guide-only view.",
                EditorStyles.wordWrappedMiniLabel);
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
            stage.BrushRootInfluence = EditorGUILayout.Slider(new GUIContent("Root Influence",
                "Bending multiplier near the base for Comb, Grab, Smooth, Clump, Part and Gravity. 0 adds root protection; 1 keeps the tool's normal response. Fades to 1 at the tip. The attachment stays pinned. Length, Cut, Width and Freeze are unaffected."),
                stage.BrushRootInfluence, 0f, 1f);
            EditorGUILayout.LabelField("Root stays fixed. Lower values protect the base; 1 keeps the normal tool response. Tips are unaffected.", EditorStyles.wordWrappedMiniLabel);
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
            bool gravityHeld = GUILayout.RepeatButton(new GUIContent(
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
            DrawUnifiedLayerStack(stage);

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

        private void DrawCards(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            if (group == null) return;
            DrawStepTitle("5. Cards", "Assign a texture and material, define UV sets directly below, then preview the generated cards.");
            if (group.guides.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "You can configure card geometry, textures, and UV sets now. Add authored guides to see generated cards in the Scene view.",
                    MessageType.Info);
                if (GUILayout.Button("Go to Guides", GUILayout.Height(30f)))
                    stage.WorkflowStep = HairWorkflowStep.Guides;
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
            if (profile == null)
            {
                EditorGUILayout.HelpBox("A Card Profile is required for a release bake.", MessageType.Error);
                if (GUILayout.Button("Create Default Ribbon Profile"))
                {
                    group.profile = HairCardMenu.CreateDefaultProfileNear(stage.Groom);
                    HairGroomCommands.Commit(stage.Groom);
                }
            }
            else if (cardGeometryExpanded = EditorGUILayout.Foldout(cardGeometryExpanded,
                         "Card geometry", true))
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

            EditorGUILayout.Space(8f);
            DrawAtlasSettings(stage, group);

            EditorGUILayout.Space(8f);
            HairChildSettings children = group.children;
            childSettingsExpanded = EditorGUILayout.Foldout(childSettingsExpanded, "Child population & variation", true);
            if (childSettingsExpanded)
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
            if (GUILayout.Button("Continue to Optimize", GUILayout.Height(28f))) stage.WorkflowStep = HairWorkflowStep.Optimize;
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
            float editorWidth = position.width - Mathf.Clamp(position.width * 0.27f, 205f, 300f) - 42f;
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
            if (GUILayout.Button("Continue to Validate & Bake", GUILayout.Height(28f)))
                stage.WorkflowStep = HairWorkflowStep.ValidateAndBake;
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
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!string.IsNullOrEmpty(issue.guideId) && GUILayout.Button("Select guide"))
                    {
                        stage.NavigateToIssue(issue, true);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Go to setting"))
                    {
                        stage.NavigateToIssue(issue);
                        GUIUtility.ExitGUI();
                    }
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

        private void DrawUnifiedLayerStack(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            collapsedLayerIds ??= new List<string>();
            EditorGUILayout.LabelField("Layer Stack", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Layers evaluate bottom to top. Within each layer: sculpt, then modifiers top to bottom.", EditorStyles.wordWrappedMiniLabel);
            if (group.modifiers.Count > 0)
            {
                EditorGUILayout.HelpBox("This groom has legacy group modifiers. Import them to edit them in the unified stack. The existing result is preserved.", MessageType.Info);
                if (GUILayout.Button("Move legacy modifiers into a layer"))
                    stage.SetActiveLayer(HairGroomCommands.ImportLegacyModifiers(stage.Groom, group)?.Id);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(group.locked))
                    if (GUILayout.Button("+ Sculpt Layer", EditorStyles.toolbarButton))
                        stage.SetActiveLayer(HairGroomCommands.AddSculptLayer(stage.Groom, group)?.Id);
                using (new EditorGUI.DisabledScope(group.locked || stage.ActiveLayer == null || stage.ActiveLayer.locked))
                    if (GUILayout.Button(new GUIContent("+ Modifier", "Add to the selected sculpt layer"), EditorStyles.toolbarButton))
                        ShowModifierMenu(stage);
                GUILayout.FlexibleSpace();
            }
            int rows = group.sculptLayers.Count;
            foreach (HairSculptLayer layer in group.sculptLayers)
                if (layer != null && !collapsedLayerIds.Contains(layer.Id)) rows += layer.modifiers.Count;
            using (var scroll = new EditorGUILayout.ScrollViewScope(layerStackScroll,
                GUILayout.Height(Mathf.Clamp(rows * 24f + 12f, 80f, 270f))))
            {
                layerStackScroll = scroll.scrollPosition;
                for (int i = group.sculptLayers.Count - 1; i >= 0; i--)
                {
                    HairSculptLayer layer = group.sculptLayers[i];
                    if (layer == null) continue;
                    bool expanded = !collapsedLayerIds.Contains(layer.Id);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect foldoutRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
                        bool next = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                        if (next != expanded)
                        {
                            if (next) collapsedLayerIds.Remove(layer.Id);
                            else
                            {
                                collapsedLayerIds.Add(layer.Id);
                                if (stage.ActiveLayerId == layer.Id && stage.ActiveModifier != null) stage.SetActiveLayer(layer.Id);
                            }
                            expanded = next;
                        }
                        bool selected = stage.ActiveLayerId == layer.Id && stage.ActiveModifier == null;
                        if (GUILayout.Toggle(selected, new GUIContent($"{layer.name}  ({layer.modifiers.Count})", "Select layer properties; foldout shows its modifiers"), "Button") && !selected)
                            stage.SetActiveLayer(layer.Id);
                        bool solo = stage.SoloLayerId == layer.Id;
                        if (GUILayout.Toggle(solo, new GUIContent("S", "Solo this layer and its modifiers"), "Button", GUILayout.Width(24f)) != solo) stage.SoloLayer(layer.Id);
                        using (new EditorGUI.DisabledScope(group.locked))
                        {
                            bool visible = GUILayout.Toggle(layer.visible, new GUIContent("V", "Show this layer and its modifiers"), "Button", GUILayout.Width(24f));
                            bool locked = GUILayout.Toggle(layer.locked, new GUIContent("L", "Lock layer sculpting and modifier edits"), "Button", GUILayout.Width(24f));
                            if (visible != layer.visible || locked != layer.locked)
                            {
                                Undo.RecordObject(stage.Groom, "Change Hair Layer State");
                                layer.visible = visible; layer.locked = locked;
                                HairGroomCommands.Commit(stage.Groom);
                            }
                        }
                    }
                    if (!expanded) continue;
                    for (int m = 0; m < layer.modifiers.Count; m++)
                    {
                        HairModifierSettings modifier = layer.modifiers[m];
                        if (modifier == null) continue;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(28f);
                            using (new EditorGUI.DisabledScope(group.locked || layer.locked))
                            {
                                bool enabled = EditorGUILayout.Toggle(new GUIContent("", "Enable this modifier"), modifier.enabled, GUILayout.Width(18f));
                                if (enabled != modifier.enabled)
                                {
                                    Undo.RecordObject(stage.Groom, "Toggle Hair Modifier");
                                    modifier.enabled = enabled;
                                    HairGroomCommands.Commit(stage.Groom);
                                }
                            }
                            bool selected = stage.ActiveModifierId == modifier.Id;
                            if (GUILayout.Toggle(selected, new GUIContent($"{m + 1}. {modifier.name}", ObjectNames.NicifyVariableName(modifier.type.ToString())), "Button") && !selected)
                                stage.SetActiveModifier(layer.Id, modifier.Id);
                        }
                    }
                }
                if (group.sculptLayers.Count == 0) EditorGUILayout.LabelField("Add a sculpt layer to start building the stack.", EditorStyles.wordWrappedMiniLabel);
            }

            HairSculptLayer activeLayer = stage.ActiveLayer;
            HairModifierSettings activeModifier = stage.ActiveModifier;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(activeModifier != null ? "Modifier Properties" : "Layer Properties", EditorStyles.boldLabel);
                if (activeLayer == null)
                {
                    EditorGUILayout.LabelField("Select a layer or one of its modifiers.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }
                EditorGUILayout.LabelField(activeModifier == null ? activeLayer.name : $"{activeLayer.name} / {activeModifier.name}", EditorStyles.wordWrappedMiniLabel);
                if (activeLayer.locked || group.locked) EditorGUILayout.HelpBox("This layer is locked. Unlock it to edit its sculpting or modifiers.", MessageType.Info);
                using (new EditorGUI.DisabledScope(group.locked || activeLayer.locked))
                {
                    bool isLayer = activeModifier == null;
                    string id = activeModifier?.Id ?? activeLayer.Id;
                    int index = isLayer ? group.sculptLayers.IndexOf(activeLayer) : activeLayer.modifiers.IndexOf(activeModifier);
                    int count = isLayer ? group.sculptLayers.Count : activeLayer.modifiers.Count;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(isLayer ? index == count - 1 : index == 0))
                            if (GUILayout.Button("Move up")) HairGroomCommands.EditStack(stage.Groom, group, id, isLayer, isLayer ? 1 : -1);
                        using (new EditorGUI.DisabledScope(isLayer ? index == 0 : index == count - 1))
                            if (GUILayout.Button("Move down")) HairGroomCommands.EditStack(stage.Groom, group, id, isLayer, isLayer ? -1 : 1);
                        if (GUILayout.Button("Duplicate"))
                        {
                            if (isLayer) stage.SetActiveLayer(HairGroomCommands.DuplicateLayer(stage.Groom, group, activeLayer)?.Id);
                            else stage.SetActiveModifier(activeLayer.Id, HairGroomCommands.DuplicateModifier(stage.Groom, group, activeLayer, activeModifier)?.Id);
                        }
                        if (GUILayout.Button(new GUIContent("Remove", isLayer ? "Remove the layer and all its modifiers. Undo restores them." : "Remove this modifier. Undo restores it.")) &&
                            HairGroomCommands.EditStack(stage.Groom, group, id, isLayer, 0, true))
                        {
                            stage.SetActiveLayer(isLayer ? group.sculptLayers.FindLast(layer => layer != null)?.Id : activeLayer.Id);
                            return;
                        }
                    }
                    // Resolve again after duplicate so only the newly selected item's properties render.
                    activeLayer = stage.ActiveLayer;
                    activeModifier = stage.ActiveModifier;
                    if (activeModifier != null) DrawModifier(stage, activeModifier);
                    else if (activeLayer != null)
                    {
                        string name = EditorGUILayout.DelayedTextField("Layer name", activeLayer.name);
                        float opacity = EditorGUILayout.Slider("Opacity", activeLayer.opacity, 0f, 1f);
                        HairSculptBlendMode blend = (HairSculptBlendMode)EditorGUILayout.EnumPopup("Sculpt blend", activeLayer.blendMode);
                        if (name != activeLayer.name || opacity != activeLayer.opacity || blend != activeLayer.blendMode)
                        {
                            Undo.RecordObject(stage.Groom, "Edit Hair Sculpt Layer");
                            activeLayer.name = name; activeLayer.opacity = opacity; activeLayer.blendMode = blend;
                            HairGroomCommands.Commit(stage.Groom);
                        }
                        EditorGUILayout.LabelField("Opacity scales sculpting and modifier weights. Sculpt blend applies to painted offsets. Modifiers deform the accumulated hair at this point in the stack.", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        private static void DrawModifier(HairCardStage stage, HairModifierSettings modifier)
        {
            if (modifier == null) return;
            using (new EditorGUI.DisabledScope(stage.ActiveGroup.locked))
            {
                string name = EditorGUILayout.DelayedTextField("Name", modifier.name);
                float weight = EditorGUILayout.Slider("Blend weight", modifier.weight, 0f, 1f);
                string amountLabel = modifier.type switch
                {
                    HairModifierType.Length or HairModifierType.Width => "Scale multiplier",
                    HairModifierType.Curl or HairModifierType.Wave or HairModifierType.Noise => "Amplitude (units)",
                    HairModifierType.Twist => "Roll (degrees)",
                    HairModifierType.Resample or HairModifierType.Simplify => "Control point count",
                    HairModifierType.Gravity => "Settle duration (seconds)",
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
                else if (modifier.type != HairModifierType.Mirror)
                    amount = EditorGUILayout.FloatField(amountLabel, amount);
                bool usesRamp = modifier.type != HairModifierType.Length && modifier.type != HairModifierType.Resample && modifier.type != HairModifierType.Simplify && modifier.type != HairModifierType.LodReduction;
                float rootInfluence = modifier.rootInfluence;
                if (ModifierUsesRootInfluence(modifier.type))
                    rootInfluence = EditorGUILayout.Slider(new GUIContent("Root Influence", "The root stays anchored. Lower values protect bending near the base; tips keep full influence."), rootInfluence, 0f, 1f);
                float gravityStrength = modifier.gravityStrength, separation = modifier.gravitySeparation, clearance = modifier.gravityClearance;
                bool collision = modifier.gravityCollision;
                bool useWorldGravity = modifier.useWorldGravity;
                Vector3 gravityDirection = modifier.gravityDirection;
                if (modifier.type == HairModifierType.Gravity)
                {
                    gravityStrength = EditorGUILayout.Slider("Gravity strength", gravityStrength, 0f, 10f);
                    separation = EditorGUILayout.Slider("Card separation", separation, 0f, 1f);
                    useWorldGravity = EditorGUILayout.Toggle("Use world gravity", useWorldGravity);
                    using (new EditorGUI.DisabledScope(useWorldGravity)) gravityDirection = EditorGUILayout.Vector3Field("Local gravity override", gravityDirection);
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
                    vector = EditorGUILayout.Vector3Field("Source-local direction", modifier.vector);
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
                    helperId != modifier.helperId || rootInfluence != modifier.rootInfluence ||
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
                    modifier.gravityStrength = gravityStrength; modifier.gravitySeparation = separation;
                    modifier.gravityCollision = collision; modifier.gravityClearance = clearance;
                    modifier.gravityDirection = gravityDirection;
                    modifier.useWorldGravity = useWorldGravity;
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

        private static bool ModifierUsesRootInfluence(HairModifierType type) => type == HairModifierType.Gravity ||
            type == HairModifierType.Smooth || type == HairModifierType.FlowAlign || type == HairModifierType.Lift ||
            type == HairModifierType.Clump || type == HairModifierType.Part || type == HairModifierType.Curl ||
            type == HairModifierType.Wave || type == HairModifierType.Noise || type == HairModifierType.HelperFollow ||
            type == HairModifierType.Collision || type == HairModifierType.PushOut;

        private static bool ModifierUsesHelper(HairModifierType type)
        {
            return type == HairModifierType.HelperFollow || type == HairModifierType.Collision ||
                   type == HairModifierType.PushOut || type == HairModifierType.Clump;
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

        private void ShowModifierMenu(HairCardStage stage)
        {
            HairGroup group = stage.ActiveGroup;
            HairSculptLayer layer = stage.ActiveLayer;
            if (layer == null || layer.locked || group.locked) return;
            GenericMenu menu = new GenericMenu();
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                if (type == HairModifierType.LodReduction) continue;
                HairModifierType captured = type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.ToString())), false,
                    () =>
                    {
                        if (stage == null || stage.ActiveGroup != group) return;
                        HairModifierSettings modifier = HairGroomCommands.AddModifier(stage.Groom, group, captured, layer);
                        if (modifier == null) return;
                        modifier.rootInfluence = stage.BrushRootInfluence;
                        if (captured == HairModifierType.Gravity)
                        {
                            modifier.gravityStrength = stage.GravityStrength;
                            modifier.gravitySeparation = stage.GravitySeparation;
                            modifier.gravityCollision = stage.GravityCollision;
                            modifier.gravityClearance = stage.GravityClearance;
                        }
                        HairGroomCommands.Commit(stage.Groom);
                        collapsedLayerIds.Remove(layer.Id);
                        stage.SetActiveModifier(layer.Id, modifier.Id);
                    });
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
