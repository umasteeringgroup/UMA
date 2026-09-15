using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    /// <summary>Stage-wide preview and saved-settings controls, independent of the selected node.</summary>
    public sealed class HairGroomPreviewWindow : EditorWindow
    {
        [SerializeField] private int previewPage;
        [SerializeField] private Vector2 previewScroll;
        [SerializeField] private Vector2 settingsScroll;
        [SerializeField] private string visibilitySearch = string.Empty;
        [SerializeField] private bool recipeVisibilityExpanded = true;
        [SerializeField] private bool udimVisibilityExpanded = true;
        [SerializeField] private bool slotVisibilityExpanded = true;
        [SerializeField] private bool initialPlacementDone;
        private HairGroomAsset preferenceGroom;
        private string preferenceContext;
        private double nextPreferencesSave;

        internal int Page { get => previewPage; set => previewPage = Mathf.Clamp(value, 0, 1); }

        [MenuItem("UMA/Hair Cards/Hair Preview & Settings", priority = 213)]
        public static void Open()
        {
            var window = GetWindow<HairGroomPreviewWindow>();
            if (!window.initialPlacementDone)
            { window.position = new Rect(1080f, 80f, 380f, 740f); window.initialPlacementDone = true; }
            window.Show();
        }

        internal static void OpenVisibility()
        {
            Open();
            GetWindow<HairGroomPreviewWindow>().Page = 0;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Hair Preview & Settings");
            minSize = new Vector2(360f, 400f);
            if (HairEditorPreferences.Suspended) return;
            RestoreContext(HairCardStage.ActiveStage?.Groom);
            EditorApplication.update += SaveIdlePreferences;
            Undo.undoRedoPerformed += Repaint;
        }

        private void RestoreContext(HairGroomAsset groom)
        {
            preferenceGroom = groom;
            preferenceContext = HairEditorPreferences.Context(groom);
            visibilitySearch = string.Empty;
            recipeVisibilityExpanded = udimVisibilityExpanded = slotVisibilityExpanded = true;
            previewScroll = settingsScroll = Vector2.zero;
            previewPage = 0;
            // Migrate just the former visibility fields, without importing the old window placement.
            HairEditorPreferences.instance.RestorePreviewVisibility(this, preferenceContext);
            HairEditorPreferences.instance.Restore(this, "preview", preferenceContext);
            Page = previewPage;
        }

        private void SaveIdlePreferences()
        {
            if (HairEditorPreferences.Suspended || HairCardStage.ActiveStage?.IsEditing == true ||
                EditorApplication.timeSinceStartup < nextPreferencesSave) return;
            nextPreferencesSave = EditorApplication.timeSinceStartup + 2d;
            HairEditorPreferences.instance.Remember(this, "preview", preferenceContext);
        }

        private void OnDisable()
        {
            EditorApplication.update -= SaveIdlePreferences;
            Undo.undoRedoPerformed -= Repaint;
            if (!HairEditorPreferences.Suspended)
                HairEditorPreferences.instance.Remember(this, "preview", preferenceContext);
        }

        internal static void RepaintOpenWindows()
        { foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomPreviewWindow>()) window.Repaint(); }

        internal static void SaveOpenPreferences()
        {
            if (HairEditorPreferences.Suspended) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomPreviewWindow>())
                HairEditorPreferences.instance.Remember(window, "preview", window.preferenceContext);
        }

        internal static void ResetOpenPreferences()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomPreviewWindow>())
            {
                bool placed = window.initialPlacementDone;
                HairPreferenceCodec.Reset(window);
                window.initialPlacementDone = placed;
                window.Repaint();
            }
        }

        private void OnGUI()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage?.Groom != preferenceGroom && !HairEditorPreferences.Suspended)
            {
                HairEditorPreferences.instance.Remember(this, "preview", preferenceContext);
                RestoreContext(stage?.Groom);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Nodes", EditorStyles.toolbarButton)) HairGroomNodeWindow.Open();
                if (GUILayout.Button("Properties", EditorStyles.toolbarButton)) HairGroomWorkspace.OpenProperties();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(stage?.Groom == null))
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44f))) stage.SaveNow();
                if (GUILayout.Button("Help", EditorStyles.toolbarButton, GUILayout.Width(44f))) HairGroomWorkspace.OpenQuickStart();
            }
            Page = GUILayout.Toolbar(Page, new[] { "Preview & Visibility", "Settings" }, GUILayout.Height(25f));
            if (stage?.Groom == null)
            {
                EditorGUILayout.HelpBox("Open a Hair Groom asset or select a source character/mesh to use preview controls and saved settings. This window docks independently of Hair Nodes and Hair Properties.", MessageType.Info);
                if (GUILayout.Button("Open Selected Source", GUILayout.Height(30f))) HairCardMenu.OpenSelectedSource();
                return;
            }
            EditorGUILayout.LabelField(stage.Groom.name, EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(Page == 0 ? previewScroll : settingsScroll))
            {
                if (Page == 0)
                {
                    previewScroll = scroll.scrollPosition;
                    DrawPreviewDisplay(stage);
                    if (stage.IsolateSelectedGuides || !string.IsNullOrEmpty(stage.SoloLayerId))
                        EditorGUILayout.HelpBox("Isolation / solo affects preview only. Enabled guides and visible sculpt passes remain in release output.", MessageType.Info);
                    DrawAvatarVisibility(stage);
                }
                else
                {
                    settingsScroll = scroll.scrollPosition;
                    DrawSettings(stage);
                }
            }
            EditorGUILayout.LabelField(stage.SaveStatus, EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawSettings(HairCardStage stage)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Settings saved automatically", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Each groom keeps its own settings. New grooms inherit the last-used setup. These resets are separate; they do not delete painted maps or authored guides.", MessageType.Info);
            EditorGUILayout.LabelField("Editor options", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Reset remembered visibility, brushes, generation, and window/UV display options. Card setup and authored hair are kept.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Reset editor options", GUILayout.Height(26f)))
            {
                stage.ResetEditorPreferences();
                HairGroomWorkspace.ResetOpenPreferences();
                HairAtlasRegionEditorWindow.ResetOpenPreferences();
                HairGroomNodeWindow.ResetOpenPreferences();
                ResetOpenPreferences();
                foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomPreviewWindow>()) window.Page = 1;
            }
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Card setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Active group: " + (stage.ActiveGroup?.name ?? "None"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Reset this group's card resources and this groom's LOD/bake/symmetry settings. Other groups and old resources are kept. Undo restores the previous setup.", EditorStyles.wordWrappedLabel);
            bool canReset = stage.ActiveGroup != null && !stage.ActiveGroup.locked && EditorUtility.IsPersistent(stage.Groom);
            using (new EditorGUI.DisabledScope(!canReset))
                if (GUILayout.Button("Reset card setup...", GUILayout.Height(26f)) && EditorUtility.DisplayDialog("Reset Hair Card Setup",
                    "Reset the active group's profile, textures/material assignments, UV sets, children and root inset, plus this groom's LOD/bake/symmetry options? New private resources are created; old assets, other groups, painted maps, guides and sculpt layers are kept. Undo restores the old assignments.",
                    "Reset setup", "Cancel"))
                {
                    HairGroomWorkspace.CancelOpenAtlasInteractions();
                    HairEditorPreferences.instance.ResetCurrentSetup(stage.Groom, stage.ActiveGroup);
                    stage.SaveNow();
                }
            if (!canReset)
                EditorGUILayout.LabelField("Select an unlocked group in a saved groom to reset its card setup.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("New-groom defaults", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Stop reusing the last card setup for new grooms. Existing grooms are unchanged.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Forget new-groom defaults", GUILayout.Height(26f)))
            { stage.SaveNow(false); HairEditorPreferences.instance.ClearSetupDefaults(); }
        }

        private static void DrawPreviewDisplay(HairCardStage stage)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Preview & Display", EditorStyles.boldLabel);
            // Stacked fields stay readable in narrow docks.
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
                    "Shows the body slots selected under Avatar Visibility below. Requires a generated character or a saved character binding."), stage.ShowAvatar);
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
                    "Frames the current group's painted Growth / Density region and its influencing bones in the authoring pose. " +
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
                EditorGUILayout.LabelField("Select Source & Setup → Bind Character / Race to attach body slots to an existing groom, or launch from a generated character.", EditorStyles.wordWrappedMiniLabel);
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
            EditorGUILayout.LabelField(stage.HasBoundCharacter
                ? "Visibility is stage-only. Bound body slots are a separate preview; the original authoring surface and its paint remain unchanged."
                : "Visibility is stage-only. Hidden parts are also removed from painting, selection, and guide-placement raycasts.", EditorStyles.wordWrappedMiniLabel);
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

    }
}
