using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    internal enum TexturePaintScenePreviewMode
    {
        Material,
        SelectedChannel,
        OriginalMaterial,
        LayerMask,
        WorldPosition,
        WorldNormal,
        SignedCurvature,
        AmbientOcclusion,
        Thickness,
        SurfaceId,
        VertexNormal,
        VertexTangent,
        UVSeams
    }

    public sealed partial class TexturePaintStageWindow
    {
        private const float WorkspaceToolbarHeight = 24f;
        private const float WorkspaceSplitterSize = 4f;
        private const string LayerDragKey = "UMA.TexturePaint.LayerIndex";
        private static string ToolRailIconPath =>
            UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Editor/Icons/TexturePaintIcons.png");
        private const int ToolRailIconCount = 13;
        private const int DocumentPickerControlId = 0x5450444F;
        private const string DocumentAssetFolder = "Assets";
        private const float LayerRowExtendedControlsMinimumWidth = 600f;
        private static Sprite[] toolRailIcons;

        [SerializeField] private float workspaceLeftWidth = 238f;
        [SerializeField] private float workspaceRightWidth = 318f;
        [SerializeField] private float workspaceShelfHeight = 178f;
        // Legacy document field retained so v10-v18 workspace JSON continues to round-trip after
        // the dockable tool rail moved into a native Scene-view toolbar overlay.
        [SerializeField] private bool workspaceShowToolRail = true;
        [SerializeField] private bool workspaceShowTargets = true;
        [SerializeField] private bool workspaceShowLayers = true;
        [SerializeField] private bool workspaceShowProperties = true;
        [SerializeField] private bool workspaceShowAssetShelf = true;
        [SerializeField] private bool workspaceShowUV = true;
        [SerializeField] private int workspacePanelTab = 1;
        [SerializeField] private int workspaceLeftTab;
        [SerializeField] private int workspaceRightTab;
        [SerializeField] private Vector2 workspaceUVPan;
        [SerializeField] private float workspaceUVZoom = 1f;
        [SerializeField] private bool channelSolo;
        [SerializeField] private bool layerMaskMode;
        [SerializeField] private bool soloLayerMask;
        [SerializeField, Range(0f, 1f)] private float layerMaskPaintValue = 1f;
        [SerializeField] private bool previewBefore;
        [SerializeField] private TexturePaintScenePreviewMode scenePreviewMode;
        [SerializeField] private bool uvPreviewBefore;
        [SerializeField] private bool uvChannelAdjustmentsExpanded = true;
        [SerializeField] private bool isolateSelectedSlots;
        // Retained under its original serialized name for recipe compatibility; this now
        // controls only the UV wireframe in the dockable 2D canvas.
        [SerializeField] private bool wireframe;
        [SerializeField] private string assetShelfSearch;
        [SerializeField] private string assetShelfFolder;
        [SerializeField] private bool assetShelfFavoritesOnly;
        [SerializeField] private bool assetShelfRecentOnly;
        [SerializeField] private List<string> favoriteBrushGuids = new List<string>();
        [SerializeField] private List<string> recentBrushGuids = new List<string>();
        [SerializeField] private List<string> brushOrderGuids = new List<string>();
        [SerializeField] private List<string> workspaceCollapsedLayerGroupIds = new List<string>();
        [SerializeField] private List<string> workspaceCollapsedPropertySectionIds = new List<string>();
        [SerializeField] private List<string> workspaceExpandedLayerChannelIds = new List<string>();

        [NonSerialized] private Vector2 workspaceTargetScroll;
        [NonSerialized] private Vector2 workspaceLayerScroll;
        [NonSerialized] private Vector2 workspacePropertyScroll;
        [NonSerialized] private Vector2 workspaceBrushScroll;
        [NonSerialized] private Vector2 workspaceShelfScroll;
        [NonSerialized] private string workspaceTargetSearch;
        [NonSerialized] private string workspaceRenameLayerId;
        [NonSerialized] private string workspaceRenameBuffer;
        [NonSerialized] private string workspaceRenameBrush;
        [NonSerialized] private TexturePaintChannel workspaceAddLayerChannel = TexturePaintChannel.Albedo;
        [NonSerialized] private int uvPreferredTriangle = -1;
        [NonSerialized] private bool uvStrokeActive;
        [NonSerialized] private bool directUVStroke;
        [NonSerialized] private bool uvPanning;
        [NonSerialized] private int uvDraggingSplinePoint = -1;
        [NonSerialized] private UVSplineHandleKind uvDraggingSplineHandle;
        [NonSerialized] private bool uvDraggingSplineIncoming;
        [NonSerialized] private bool uvSplineHandleUndoStarted;
        [NonSerialized] private Vector2 uvPanStartMouse;
        [NonSerialized] private Vector2 uvPanStart;
        [NonSerialized] private bool modifierBrushDrag;
        [NonSerialized] private int modifierBrushHotControl;
        [NonSerialized] private Vector2 modifierBrushStartMouse;
        [NonSerialized] private float modifierBrushStartSize;
        [NonSerialized] private float modifierBrushStartHardness;
        [NonSerialized] private TextureSet modifierPathEditSet;
        [NonSerialized] private bool modifierPathUndoStarted;
        [NonSerialized] private bool documentPickerOpen;
        [NonSerialized] private bool uvColorSamplerArmed;
        [NonSerialized] private int geometryFillMode;
        [NonSerialized] private bool workspacePreviewBeforeApplied;
        [NonSerialized] private string workspaceStatus;
        [NonSerialized] private double workspaceStatusUntil;
        [NonSerialized] private int splitterDrag;
        [NonSerialized] private Vector2 splitterStartMouse;
        [NonSerialized] private float splitterStartValue;
        [NonSerialized] private readonly Dictionary<ReconstructedSurface, Material> workspaceDebugMaterials =
            new Dictionary<ReconstructedSurface, Material>();
        [NonSerialized] private readonly Dictionary<string, Texture2D> workspaceInternalPreviewTextures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        [NonSerialized] private readonly Dictionary<Mesh, Vector2[]> workspaceUVEdges =
            new Dictionary<Mesh, Vector2[]>();
        [NonSerialized] private readonly List<BrushShelfItem> workspaceBrushes = new List<BrushShelfItem>();
        [NonSerialized] private bool workspaceBrushesDirty = true;
        [NonSerialized] private bool workspaceInitialized;
        [NonSerialized] private Vector3[] workspaceUVLineBuffer;

        private enum UVSplineHandleKind
        {
            None,
            Anchor,
            Incoming,
            Outgoing,
            Width
        }
        [NonSerialized] private readonly Vector3[] workspaceUVCircleCursor = new Vector3[49];
        [NonSerialized] private readonly Vector3[] workspaceUVSquareCursor = new Vector3[5];

        private sealed class BrushShelfItem
        {
            public string guid;
            public string path;
            public string folder;
            public BrushPreset preset;
        }

        internal void DrawWorkspace(Rect windowRect)
        {
            if (controller?.Textures == null || controller.Textures.Sets.Count == 0) return;
            InitializeWorkspaceUI();
            if (closeAfterSave && IsPersistenceActive)
            {
                GUILayout.BeginArea(new Rect(0f, 0f, windowRect.width, windowRect.height));
                GUILayout.FlexibleSpace();
                EditorGUILayout.HelpBox("Saving a recoverable Overlay Painter session. The stage will close automatically when the journal commit is durable.",
                    MessageType.Info);
                Rect progressRect = GUILayoutUtility.GetRect(220f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(progressRect, persistenceProgress,
                    string.IsNullOrEmpty(persistenceStatus) ? "Saving recovery…" : persistenceStatus);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                return;
            }
            Event current = Event.current;
            HandleDocumentPickerEvent(current);
            bool changedBefore = GUI.changed;
            pathEditRecordedThisGUI = false;
            bool hadPathRenderState = TryCapturePathRenderState(out TextureSet pathSetBefore,
                out TexturePaintLayer pathLayerBefore, out TexturePaintSplineSettings pathSettingsBefore,
                out int pathSignatureBefore);
            HandleWorkspaceShortcuts(current);

            Rect toolbar = new Rect(0f, 0f, windowRect.width, WorkspaceToolbarHeight);
            DrawGlobalToolbar(toolbar);

            float bodyTop = WorkspaceToolbarHeight;
            Rect body = new Rect(0f, bodyTop, windowRect.width,
                Mathf.Max(0f, windowRect.height - bodyTop));

            DrawWorkspaceBody(body);
            ApplyWorkspaceDisplay();
            HandlePathRenderParameterChanges(hadPathRenderState, pathSetBefore, pathLayerBefore,
                pathSettingsBefore, pathSignatureBefore);
            CaptureActivePaintLayerSettings();
            if (GUI.changed && !changedBefore)
            {
                MarkDocumentDirty();
                SceneView.RepaintAll();
                TexturePaintUVWindow.RepaintOpenWindows();
                TexturePaintBrushWindow.RepaintOpenWindows();
            }
        }

        internal void DrawBrushWorkspace(Rect windowRect)
        {
            if (controller?.Textures == null || controller.Textures.Sets.Count == 0) return;
            InitializeWorkspaceUI();
            if (closeAfterSave && IsPersistenceActive)
            {
                GUILayout.BeginArea(new Rect(0f, 0f, windowRect.width, windowRect.height));
                GUILayout.FlexibleSpace();
                EditorGUILayout.HelpBox("Finishing the Overlay Painter save. Brush controls will return when the document is ready.",
                    MessageType.Info);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                return;
            }

            Event current = Event.current;
            HandleDocumentPickerEvent(current);
            bool changedBefore = GUI.changed;
            pathEditRecordedThisGUI = false;
            bool hadPathRenderState = TryCapturePathRenderState(out TextureSet pathSetBefore,
                out TexturePaintLayer pathLayerBefore, out TexturePaintSplineSettings pathSettingsBefore,
                out int pathSignatureBefore);
            HandleWorkspaceShortcuts(current);

            Rect toolbar = new Rect(0f, 0f, windowRect.width, WorkspaceToolbarHeight);
            DrawBrushWindowToolbar(toolbar);
            float bodyTop = WorkspaceToolbarHeight;
            float shelfHeight = CalculateBrushAssetShelfHeight(windowRect.height,
                workspaceShelfHeight, workspaceShowAssetShelf);
            Rect content = new Rect(0f, bodyTop, windowRect.width,
                Mathf.Max(0f, windowRect.height - bodyTop - shelfHeight -
                    (workspaceShowAssetShelf ? WorkspaceSplitterSize : 0f)));
            if (workspaceShowAssetShelf)
            {
                Rect shelfSplitter = new Rect(0f, content.yMax, windowRect.width, WorkspaceSplitterSize);
                HandleSplitter(shelfSplitter, 3, ref workspaceShelfHeight, true, 112f,
                    Mathf.Max(112f, windowRect.height * 0.45f));
                Rect shelfRect = new Rect(0f, shelfSplitter.yMax, windowRect.width,
                    Mathf.Max(0f, windowRect.height - shelfSplitter.yMax));
                GUI.Box(shelfRect, GUIContent.none, WorkspaceStyles.Region);
                GUILayout.BeginArea(Shrink(shelfRect, 1f));
                DrawAssetShelf();
                GUILayout.EndArea();
            }

            GUI.Box(content, GUIContent.none, WorkspaceStyles.Region);
            GUILayout.BeginArea(Shrink(content, 1f));
            DrawRegionHeader("BRUSH CONTROLS", "Brush behavior and stroke projection settings.");
            workspaceBrushScroll = GUILayout.BeginScrollView(workspaceBrushScroll);
            TextureSet set = ActiveTextureSet;
            TexturePaintLayer activeLayer = set != null &&
                (uint)set.activeLayerIndex < (uint)set.layers.Count
                    ? set.layers[set.activeLayerIndex] : null;
            bool showPaintControls = IsLayerMaskMode(set) || activeLayer == null ||
                activeLayer.kind == TexturePaintLayerKind.Paint || activeLayer.IsSplineLayer;
            if (showPaintControls)
            {
                DrawPropertySection("BRUSH", DrawBrushProperties);
                DrawPropertySection("STROKE & PROJECTION", DrawStrokeProperties);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Select a Paint or Path layer, or enter Layer Mask mode, to edit brush and projection settings.",
                    MessageType.Info);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            HandlePathRenderParameterChanges(hadPathRenderState, pathSetBefore, pathLayerBefore,
                pathSettingsBefore, pathSignatureBefore);
            CaptureActivePaintLayerSettings();
            if (GUI.changed && !changedBefore)
            {
                ApplyWorkspaceDisplay();
                MarkDocumentDirty();
                SceneView.RepaintAll();
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintUVWindow.RepaintOpenWindows();
                TexturePaintBrushWindow.RepaintOpenWindows();
            }
        }

        private void DrawBrushWindowToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.toolbar);
            GUILayout.BeginHorizontal();
            GUILayout.Label("BRUSH WORKSPACE", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            workspaceShowAssetShelf = GUILayout.Toggle(workspaceShowAssetShelf,
                new GUIContent("Asset Shelf", "Show the brush Asset Shelf in this window (Tab)"),
                EditorStyles.toolbarButton, GUILayout.Width(78f));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        internal static float CalculateBrushAssetShelfHeight(float windowHeight, float requestedHeight,
            bool visible)
        {
            return visible
                ? Mathf.Clamp(requestedHeight, 112f, Mathf.Max(112f, windowHeight * 0.45f))
                : 0f;
        }

        internal void DrawUVWorkspace(Rect windowRect)
        {
            if (controller?.Textures == null || controller.Textures.Sets.Count == 0) return;
            InitializeWorkspaceUI();
            if (closeAfterSave && IsPersistenceActive)
            {
                GUILayout.BeginArea(new Rect(0f, 0f, windowRect.width, windowRect.height));
                GUILayout.FlexibleSpace();
                EditorGUILayout.HelpBox("Finishing the Overlay Painter save. This view will close automatically when the journal commit is durable.",
                    MessageType.Info);
                Rect progressRect = GUILayoutUtility.GetRect(220f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(progressRect, persistenceProgress,
                    string.IsNullOrEmpty(persistenceStatus) ? "Finishing save…" : persistenceStatus);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                return;
            }
            Event current = Event.current;
            HandleDocumentPickerEvent(current);
            bool changedBefore = GUI.changed;
            pathEditRecordedThisGUI = false;
            HandleWorkspaceShortcuts(current, uvWindowInput: true);

            Rect content = new Rect(0f, 0f, windowRect.width, windowRect.height);
            GUI.Box(content, GUIContent.none, WorkspaceStyles.Canvas);
            GUILayout.BeginArea(Shrink(content, 1f));
            DrawViewportRegion();
            GUILayout.EndArea();

            if (GUI.changed && !changedBefore)
            {
                ApplyWorkspaceDisplay();
                MarkDocumentDirty();
                SceneView.RepaintAll();
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintUVWindow.RepaintOpenWindows();
                TexturePaintBrushWindow.RepaintOpenWindows();
            }
        }

        internal void EndUVWindowInteraction()
        {
            uvPanning = false;
            uvColorSamplerArmed = false;
            ReleaseModifierBrushCapture(true);
            if (controller == null)
            {
                uvDraggingSplinePoint = -1;
                uvDraggingSplineHandle = UVSplineHandleKind.None;
                uvSplineHandleUndoStarted = false;
                uvStrokeActive = false;
                return;
            }
            if (uvDraggingSplineHandle != UVSplineHandleKind.None)
            {
                uvDraggingSplinePoint = -1;
                uvDraggingSplineHandle = UVSplineHandleKind.None;
                uvSplineHandleUndoStarted = false;
                ScheduleSplineReapply(true);
            }
            else if (splineReapplyPending) ScheduleSplineReapply(true);
            if (uvStrokeActive) EndUVStroke(true);
        }

        private void DrawWorkspaceBody(Rect body)
        {
            bool showTargets = workspaceShowTargets;
            bool showLayersAndProperties = workspaceShowLayers || workspaceShowProperties;
            if (!showTargets && !showLayersAndProperties) return;
            Rect panel = new Rect(body.x, body.y, body.width, body.height);
            GUI.Box(panel, GUIContent.none, WorkspaceStyles.Region);
            GUILayout.BeginArea(Shrink(panel, 1f));
            DrawWorkspacePanelGroup(panel.size, showTargets, showLayersAndProperties);
            GUILayout.EndArea();
        }

        private void DrawWorkspacePanelGroup(Vector2 size, bool showTargets,
            bool showLayersAndProperties)
        {
            const float tabsHeight = 22f;
            var panelIds = new List<int>(2);
            var panelLabels = new List<string>(2);
            if (showTargets)
            {
                panelIds.Add(0);
                panelLabels.Add("Paint Target");
            }
            if (showLayersAndProperties)
            {
                panelIds.Add(1);
                panelLabels.Add("Layers & Properties");
            }
            int selected = panelIds.IndexOf(Mathf.Clamp(workspacePanelTab, 0, 1));
            if (selected < 0) selected = 0;
            GUILayout.BeginArea(new Rect(0f, 0f, size.x, tabsHeight), EditorStyles.toolbar);
            selected = GUILayout.Toolbar(selected, panelLabels.ToArray(), EditorStyles.toolbarButton);
            GUILayout.EndArea();
            workspacePanelTab = panelIds[Mathf.Clamp(selected, 0, panelIds.Count - 1)];

            Rect content = new Rect(0f, tabsHeight, size.x, Mathf.Max(0f, size.y - tabsHeight));
            GUILayout.BeginArea(content);
            if (workspacePanelTab == 0) DrawTargetRegion();
            else DrawRightRegion(content.size);
            GUILayout.EndArea();
        }

        private void DrawGlobalToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.toolbar);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("File", "Document commands"), EditorStyles.toolbarDropDown,
                GUILayout.Width(42f))) ShowFileMenu(GUILayoutUtility.GetLastRect());
            if (GUILayout.Button(new GUIContent("Edit", "Undo and editing commands"), EditorStyles.toolbarDropDown,
                GUILayout.Width(42f))) ShowEditMenu(GUILayoutUtility.GetLastRect());
            GUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!CanUndoLightweight && !controller.Painting.History.CanUndo && !controller.Plugins.CanUndo))
                if (GUILayout.Button(new GUIContent("Undo", "Undo the latest paint, path, or plugin edit (Ctrl/Cmd+Z)"), EditorStyles.toolbarButton, GUILayout.Width(46f))) PerformWorkspaceUndo();
            using (new EditorGUI.DisabledScope(!CanRedoLightweight && !controller.Painting.History.CanRedo && !controller.Plugins.CanRedo))
                if (GUILayout.Button(new GUIContent("Redo", "Redo the latest paint, path, or plugin edit (Ctrl/Cmd+Shift+Z)"), EditorStyles.toolbarButton, GUILayout.Width(46f))) PerformWorkspaceRedo();
            using (new EditorGUI.DisabledScope(IsPersistenceActive))
                if (GUILayout.Button(new GUIContent(IsDocumentTemporary ? "Save As" : "Save",
                    IsDocumentTemporary ? "Create a project document (Ctrl/Cmd+S)" : "Save the project document (Ctrl/Cmd+S)"),
                    EditorStyles.toolbarButton, GUILayout.Width(IsDocumentTemporary ? 54f : 44f))) SaveWorkspace();
            if (GUILayout.Button(new GUIContent("Export", "Export textures and UMA assets"), EditorStyles.toolbarButton, GUILayout.Width(50f))) OpenExportWindow();
            GUILayout.Space(8f);
            if (GUILayout.Button(new GUIContent("2D", "Open or focus the dockable 2D texture canvas"),
                EditorStyles.toolbarButton, GUILayout.Width(36f))) TexturePaintUVWindow.ShowDockable();
            if (GUILayout.Button(new GUIContent("Brush", "Open or focus the dockable brush controls"),
                EditorStyles.toolbarButton, GUILayout.Width(46f))) TexturePaintBrushWindow.ShowDockable();
            bool compactToolbar = rect.width < 900f;
            if (compactToolbar)
            {
                if (GUILayout.Button(new GUIContent("View", "Preview and workspace display options"),
                    EditorStyles.toolbarDropDown, GUILayout.Width(46f))) ShowViewMenu(GUILayoutUtility.GetLastRect());
            }
            else
            {
                if (IsLayerMaskMode(ActiveTextureSet))
                {
                    bool nextSolo = GUILayout.Toggle(soloLayerMask,
                        new GUIContent("Solo Mask", "Show the active grayscale layer mask on the 3D model"),
                        EditorStyles.toolbarButton, GUILayout.Width(68f));
                    if (nextSolo != soloLayerMask)
                        SetScenePreviewMode(nextSolo ? TexturePaintScenePreviewMode.LayerMask
                            : TexturePaintScenePreviewMode.Material);
                }
                else
                {
                    bool nextSolo = GUILayout.Toggle(channelSolo, new GUIContent("Solo", "Preview only the selected logical channel without material shading"), EditorStyles.toolbarButton, GUILayout.Width(42f));
                    if (nextSolo != channelSolo)
                        SetScenePreviewMode(nextSolo ? TexturePaintScenePreviewMode.SelectedChannel
                            : TexturePaintScenePreviewMode.Material);
                }
                bool nextBefore = GUILayout.Toggle(previewBefore, new GUIContent("Before 3D", "Show the original source textures on the lit 3D character"), EditorStyles.toolbarButton, GUILayout.Width(66f));
                if (nextBefore != previewBefore)
                    SetScenePreviewMode(nextBefore ? TexturePaintScenePreviewMode.OriginalMaterial
                        : TexturePaintScenePreviewMode.Material);
                isolateSelectedSlots = GUILayout.Toggle(isolateSelectedSlots, new GUIContent("Isolate", "Hide surfaces outside the selected slots"), EditorStyles.toolbarButton, GUILayout.Width(52f));
            }
            GUILayout.FlexibleSpace();
            if (IsPersistenceActive)
                GUILayout.Label($"{persistenceStatus} {Mathf.RoundToInt(persistenceProgress * 100f)}%",
                    EditorStyles.miniLabel, GUILayout.MaxWidth(230f));
            else if (!string.IsNullOrEmpty(persistenceError))
                GUILayout.Label(new GUIContent("Save failed", persistenceError), EditorStyles.miniLabel,
                    GUILayout.MaxWidth(90f));
            if (!string.IsNullOrEmpty(workspaceStatus) && EditorApplication.timeSinceStartup < workspaceStatusUntil)
                GUILayout.Label(workspaceStatus, EditorStyles.miniLabel, GUILayout.MaxWidth(260f));
            TextureSet active = ActiveTextureSet;
            string target = active?.surface?.slotNames != null && active.surface.slotNames.Count > 0
                ? string.Join(", ", active.surface.slotNames) : "No slot";
            string documentName = document != null ? document.name : "No Document";
            if (rect.width >= 720f) GUILayout.Label(new GUIContent(
                $"{(documentDirty ? "● " : string.Empty)}{documentName}  ·  {DocumentStateLabel}  ·  {target}  ·  {selectedChannel}",
                IsDocumentTemporary ? "Temporary session backed by the configured recovery asset. Use Save As to create a permanent document."
                    : AssetDatabase.GetAssetPath(document)),
                EditorStyles.miniLabel);
            if (GUILayout.Button(new GUIContent("Layout", "Show, hide, or reset workspace regions"), EditorStyles.toolbarDropDown, GUILayout.Width(58f))) ShowLayoutMenu();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ShowFileMenu(Rect anchor)
        {
            string kb = Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent($"New Document\t{kb}+N"), false, NewWorkspaceDocument);
            menu.AddItem(new GUIContent($"Load Document...\t{kb}+O"), false, OpenWorkspaceDocumentPicker);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent((IsDocumentTemporary ? "Save As" : "Save") + $"\t{kb}+S"), false, SaveWorkspace);
            menu.AddItem(new GUIContent($"Save As...\t{kb}+Shift+S"), false, SaveWorkspaceAs);
            if (!IsDocumentTemporary)
                menu.AddItem(new GUIContent("Revert to Saved"), false, RevertWorkspaceDocument);
            else menu.AddDisabledItem(new GUIContent("Revert to Saved"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Export..."), false, OpenExportWindow);
            TextureSet presetSet = ActiveTextureSet;
            if (presetSet != null && presetSet.layers.Count > 0)
                menu.AddItem(new GUIContent("Material Preset/Save Entire Stack..."), false,
                    () => SaveWholeStackAsMaterialPreset(presetSet));
            else menu.AddDisabledItem(new GUIContent("Material Preset/Save Entire Stack..."));
            if (presetSet != null)
                menu.AddItem(new GUIContent("Material Preset/Apply..."), false,
                    () => OpenMaterialPresetPicker(presetSet));
            else menu.AddDisabledItem(new GUIContent("Material Preset/Apply..."));
            menu.AddItem(new GUIContent("Clear All Overlay Painting..."), false,
                () => ClearAllTexturePaintData(true));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Close Overlay Painter"), false,
                () => RequestCloseStage());
            menu.DropDown(anchor);
        }

        private void ShowEditMenu(Rect anchor)
        {
            string kb = Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

            GenericMenu menu = new GenericMenu();
            bool pathLayerActive = TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer pathLayer);
            bool canUndo = CanUndoLightweight || controller.Painting.History.CanUndo || controller.Plugins.CanUndo;
            bool canRedo = CanRedoLightweight || controller.Painting.History.CanRedo || controller.Plugins.CanRedo;
            string undoLabel = CanUndoLightweight ? "Undo " + LightweightUndoLabel : "Undo";
            string redoLabel = CanRedoLightweight ? "Redo " + LightweightRedoLabel : "Redo";
            if (canUndo) menu.AddItem(new GUIContent(undoLabel + $"\t{kb}+Z"), false, PerformWorkspaceUndo);
            else menu.AddDisabledItem(new GUIContent($"Undo\t{kb}   +Z"));
            if (canRedo) menu.AddItem(new GUIContent(redoLabel + $"\t{kb}+Shift+Z"), false, PerformWorkspaceRedo);
            else menu.AddDisabledItem(new GUIContent($"Redo\t{kb}+Shift+Z"));
            menu.AddSeparator(string.Empty);

            TextureSet set = ActiveTextureSet;
            bool hasLayer = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count;
            if (hasLayer)
            {
                int layerIndex = set.activeLayerIndex;
                TexturePaintLayer activeLayer = set.layers[layerIndex];
                menu.AddItem(new GUIContent("Layer/Duplicate\tCtrl/Cmd+D"), false,
                    () => DuplicateActiveLayer(set));
                menu.AddItem(new GUIContent("Layer/Rename\tF2"), false, () => BeginLayerRename(activeLayer));
                menu.AddItem(new GUIContent("Layer/Delete\tDelete"), false,
                    () => DeleteLayer(set, layerIndex, true));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Layer/Duplicate\tCtrl/Cmd+D"));
                menu.AddDisabledItem(new GUIContent("Layer/Rename\tF2"));
                menu.AddDisabledItem(new GUIContent("Layer/Delete\tDelete"));
            }
            if (set != null)
            {
                menu.AddItem(new GUIContent("Layer/New Paint Layer"), false, () => AddPaintLayer(set));
                menu.AddItem(new GUIContent("Layer/New Fill Layer"), false, () => AddFillLayer(set));
                menu.AddItem(new GUIContent("Layer/New Path Layer"), false, () => CreateSplineLayerWithUndo(set));
                menu.AddItem(new GUIContent("Layer/New Plugin Layer"), false, () => AddPluginLayer(set));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Layer/New Paint Layer"));
                menu.AddDisabledItem(new GUIContent("Layer/New Fill Layer"));
                menu.AddDisabledItem(new GUIContent("Layer/New Path Layer"));
                menu.AddDisabledItem(new GUIContent("Layer/New Plugin Layer"));
            }

            menu.AddSeparator(string.Empty);
            if (pathLayerActive)
            {
                menu.AddItem(new GUIContent($"Path/Select All Points\t{kb}+A"), false,
                    SelectAllActivePathPoints);
                menu.AddItem(new GUIContent($"Path/Copy\t{kb}+C"), false, CopyActivePath);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent($"Path/Select All Points\t{kb}+A"));
                menu.AddDisabledItem(new GUIContent($"Path/Copy\t{kb}+C"));
            }
            if (set != null && !string.IsNullOrEmpty(splineClipboard))
                menu.AddItem(new GUIContent($"Path/Paste as New Layer\t{kb}+V"), false, PastePathAsNewLayer);
            else menu.AddDisabledItem(new GUIContent($"Path/Paste as New Layer\t{kb}+V"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Clear All Overlay Painting..."), false,
                () => ClearAllTexturePaintData(true));
            menu.DropDown(anchor);
        }

        private bool ConfirmDocumentSwitch(string action)
        {
            if (!documentDirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved Overlay Painter Changes",
                $"Save changes to '{document?.name ?? "the current document"}' before {action}?",
                "Save", "Cancel", "Don't Save");
            if (choice == 1) return false;
            if (choice == 0)
            {
                SaveWorkspace();
                return false;
            }
            return true;
        }

        private string ChooseWorkspaceDocumentPath(string title, bool copyName)
        {
            string sourceName = copyName && document != null ? document.name + " Copy" :
                (avatar != null ? avatar.name + " Overlay Painter" : "Overlay Painter Document");
            foreach (char invalid in Path.GetInvalidFileNameChars()) sourceName = sourceName.Replace(invalid, '_');
            string path = EditorUtility.SaveFilePanelInProject(title, sourceName, "asset",
                "Choose where to save the texture-paint document.", DocumentAssetFolder);
            if (string.IsNullOrEmpty(path)) return null;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                EditorUtility.DisplayDialog(title, "An Overlay Painter document already exists at that location. Choose a different name.", "OK");
                return null;
            }
            return path;
        }

        private void NewWorkspaceDocument()
        {
            if (!ConfirmDocumentSwitch("creating a new document")) return;
            TexturePaintDocument previous = document;
            ResetTexturePaintRuntimeState();
            TexturePaintRecoveryStore.Delete(recoveryContextKey);
            document = TexturePaintDocumentStorage.CreateTransient(avatar, launchContext);
            controller.AttachDocument(document);
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            documentRevision = document.revisionId;
            documentDirty = false;
            recoveryDirty = false;
            documentChangeVersion++;
            FinishDocumentChange(previous, "New temporary texture-paint session created");
        }

        private void OpenWorkspaceDocumentPicker()
        {
            documentPickerOpen = true;
            EditorGUIUtility.ShowObjectPicker<TexturePaintDocument>(document, false, string.Empty,
                DocumentPickerControlId);
        }

        private void HandleDocumentPickerEvent(Event current)
        {
            if (materialPresetPickerOpen)
            {
                HandleMaterialPresetPickerEvent(current);
                return;
            }
            if (!documentPickerOpen || current == null) return;
            if (!IsObjectPickerCompletionEvent(current)) return;
            documentPickerOpen = false;
            TexturePaintDocument selected = EditorGUIUtility.GetObjectPickerObject() as TexturePaintDocument;
            if (selected == null) ShowWorkspaceStatus("No Overlay Painter document was selected");
            else if (selected == document) ShowWorkspaceStatus(selected.name + " is already loaded");
            else LoadWorkspaceDocument(selected);
            current.Use();
        }

        internal static bool IsObjectPickerCompletionEvent(Event current)
        {
            if (current == null || current.type != EventType.ExecuteCommand) return false;
            return current.commandName == "ObjectSelectorClosed" ||
                   current.commandName == "ObjectSelectorSelectionDone";
        }

        private void LoadWorkspaceDocument(TexturePaintDocument selected)
        {
            if (selected == null || selected == document || !ValidateDocumentLaunchContext(selected) ||
                !ConfirmDocumentSwitch("loading another document")) return;
            TexturePaintDocument previous = document;
            ResetTexturePaintRuntimeState();
            document = selected;
            controller.AttachDocument(document);
            document.Migrate();
            TexturePaintDocumentStorage.RestoreReport restore =
                TexturePaintDocumentStorage.Restore(document, controller.Textures);
            RestoreState(LoadDocumentEditorState(), false);
            documentRevision = document.revisionId;
            documentDirty = false;
            recoveryDirty = false;
            persistenceError = null;
            TexturePaintRecoveryStore.Delete(recoveryContextKey);
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            ScheduleAutosaveAfterChange();
            string status = restore.HasUnboundLayers
                ? $"Loaded {document.name}; {restore.unboundLayers} saved layer member" +
                  (restore.unboundLayers == 1 ? string.Empty : "s") + " could not be rebound · see Console"
                : "Loaded " + document.name;
            FinishDocumentChange(previous, status);
        }

        private void SaveWorkspaceAs()
        {
            SaveWorkspaceAs(false);
        }

        private void SaveWorkspaceAs(bool closeWhenComplete)
        {
            if (IsPersistenceActive) return;
            string path = ChooseWorkspaceDocumentPath("Save Overlay Painter Document As", !IsDocumentTemporary);
            if (string.IsNullOrEmpty(path))
            {
                closeAfterSave = false;
                return;
            }
            BeginPersistence(PersistenceIntent.ProjectSave, path, closeWhenComplete);
            ShowWorkspaceStatus("Saving project document…");
        }

        private void RevertWorkspaceDocument()
        {
            if (IsDocumentTemporary || !EditorUtility.DisplayDialog("Revert Overlay Painter Document",
                $"Discard current changes and reload '{document.name}' from disk? This cannot be undone.",
                "Revert", "Cancel")) return;
            TexturePaintDocument currentDocument = document;
            ResetTexturePaintRuntimeState();
            controller.AttachDocument(currentDocument);
            TexturePaintDocumentStorage.RestoreReport restore =
                TexturePaintDocumentStorage.Restore(currentDocument, controller.Textures);
            documentRevision = currentDocument.revisionId;
            documentDirty = false;
            recoveryDirty = false;
            TexturePaintRecoveryStore.Delete(recoveryContextKey);
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            ScheduleAutosaveAfterChange();
            string status = restore.HasUnboundLayers
                ? $"Reverted {currentDocument.name}; {restore.unboundLayers} saved layer member" +
                  (restore.unboundLayers == 1 ? string.Empty : "s") + " could not be rebound · see Console"
                : "Reverted " + currentDocument.name;
            FinishDocumentChange(null, status);
        }

        private void FinishDocumentChange(TexturePaintDocument previous, string status)
        {
            ClearLightweightHistory();
            selectedSurface = Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1);
            if (controller.Textures.Sets.Count > 0)
                SyncActiveLayerSelection(controller.Textures.Sets[selectedSurface]);
            if (previous != null && previous != document && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(previous)))
                DestroyImmediate(previous);
            ApplyWorkspaceDisplay();
            ShowWorkspaceStatus(status);
            RepaintAll();
        }

        private void SelectAllActivePathPoints()
        {
            if (!TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer) || layer.spline == null) return;
            spline = layer.spline;
            selectedSplinePoints ??= new HashSet<int>();
            selectedSplinePoints.Clear();
            for (int i = 0; i < spline.PointCount; i++) selectedSplinePoints.Add(i);
            selectedSplinePoint = spline.PointCount > 0 ? 0 : -1;
            RepaintAll();
        }

        private void CopyActivePath()
        {
            if (!TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer) || layer.spline == null) return;
            splineClipboard = JsonUtility.ToJson(layer.spline);
            ShowWorkspaceStatus("Path copied");
        }

        private void PastePathAsNewLayer()
        {
            TextureSet set = ActiveTextureSet;
            if (set == null || string.IsNullOrEmpty(splineClipboard)) return;
            SetSelectedChannelAndRefreshSource(TexturePaintChannel.Albedo);
            BeginLayerCreationUndo("Paste Texture Path");
            TexturePaintLayer pasted = set.AddSplineLayer("Pasted Path");
            pasted.spline = JsonUtility.FromJson<TexturePaintSpline>(splineClipboard);
            pathMode = TexturePaintPathMode.Ribbon;
            ResetPathEditingDefaults();
            pasted.splineSettings = CreateSplineSettings();
            spline = pasted.spline;
            splineMode = pasted.spline?.worldSpace == true;
            selectedSplinePoint = -1;
            CompleteLayerCreationUndo(pasted);
            SyncActiveLayerSelection(set);
            ShowWorkspaceStatus("Path pasted as a new layer");
        }

        internal static Sprite GetToolRailIcon(int iconIndex)
        {
            if ((uint)iconIndex >= ToolRailIconCount) return null;
            if (toolRailIcons == null || toolRailIcons.Length != ToolRailIconCount)
            {
                toolRailIcons = new Sprite[ToolRailIconCount];
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(ToolRailIconPath);
                for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    if (!(assets[assetIndex] is Sprite sprite)) continue;
                    const string prefix = "TexturePaintIcons_";
                    if (!sprite.name.StartsWith(prefix, StringComparison.Ordinal) ||
                        !int.TryParse(sprite.name.Substring(prefix.Length), out int index) ||
                        (uint)index >= ToolRailIconCount) continue;
                    toolRailIcons[index] = sprite;
                }
            }
            return toolRailIcons[iconIndex];
        }

        private void DrawTargetRegion()
        {
            DrawRegionHeader("PAINT TARGET", "Choose one logical target. UDIM members are expanded automatically.");
            workspaceLeftTab = GUILayout.Toolbar(Mathf.Clamp(workspaceLeftTab, 0, 1), new[] { "Targets", "Texture Sets" });
            GUILayout.BeginHorizontal();
            workspaceTargetSearch = EditorGUILayout.TextField(workspaceTargetSearch ?? string.Empty,
                EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
            if (DrawImportWarningButton(globalImportWarnings, "unmapped import items", GUILayout.Width(22f),
                GUILayout.Height(18f)))
                ShowImportWarningDetails("Import warnings not associated with a reconstructed paint target",
                    globalImportWarnings);
            using (new EditorGUI.DisabledScope(ActiveLogicalTarget == null))
                if (GUILayout.Button(new GUIContent("Frame Target", "Frame all geometry belonging to the current logical paint target in the Scene view"),
                    EditorStyles.miniButton, GUILayout.Width(86f))) FrameActiveTarget();
            GUILayout.EndHorizontal();
            workspaceTargetScroll = GUILayout.BeginScrollView(workspaceTargetScroll);
            if (workspaceLeftTab == 0) DrawSlotNavigator(); else DrawTextureSetNavigator();
            GUILayout.EndScrollView();
        }

        private void DrawSlotNavigator()
        {
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            IReadOnlyList<TexturePaintLogicalTarget> targets = controller.LogicalTargets?.Targets;
            if (targets == null || targets.Count == 0)
            {
                EditorGUILayout.HelpBox("No logical paint targets were reconstructed.", MessageType.Warning);
                return;
            }
            for (int i = 0; i < targets.Count; i++)
            {
                TexturePaintLogicalTarget target = targets[i];
                if (!TargetMatchesSearch(target, workspaceTargetSearch)) continue;
                bool selected = string.Equals(selectedTargetId, target.id, StringComparison.Ordinal);
                List<MeshReconstructionWarning> targetWarnings = GetImportWarnings(target);
                bool hasTargetWarnings = targetWarnings != null && targetWarnings.Count > 0;
                string type = target.isUdim ? $"UDIM · {target.members.Count} tiles" : "Single slot";
                Rect row = GUILayoutUtility.GetRect(10f, target.isUdim && selected ? 28f + target.members.Count * 18f : 42f,
                    GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                    (selected ? WorkspaceStyles.SelectedRow : WorkspaceStyles.Row).Draw(row, false, false, selected, false);
                float warningReserve = hasTargetWarnings ? 27f : 0f;
                GUI.Label(new Rect(row.x + 7f, row.y + 4f, row.width - 14f - warningReserve, 18f),
                    target.displayName, EditorStyles.label);
                GUI.Label(new Rect(row.x + 7f, row.y + 22f, row.width - 14f - warningReserve, 16f),
                    type, EditorStyles.miniLabel);
                Rect selectionRect = new Rect(row.x, row.y, row.width - warningReserve,
                    Mathf.Min(40f, row.height));
                if (GUI.Button(selectionRect, GUIContent.none, GUIStyle.none) && !selected)
                    SelectLogicalTarget(target, sets, true);
                if (hasTargetWarnings)
                {
                    Rect warningRect = new Rect(row.xMax - 23f, row.y + 8f, 18f, 18f);
                    if (DrawImportWarningButton(warningRect, targetWarnings, target.displayName))
                        ShowImportWarningDetails(target.displayName, targetWarnings);
                }
                if (target.isUdim && selected)
                {
                    for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
                    {
                        TexturePaintLogicalTargetMember member = target.members[memberIndex];
                        List<MeshReconstructionWarning> memberWarnings = GetImportWarnings(member);
                        bool hasMemberWarnings = memberWarnings != null && memberWarnings.Count > 0;
                        float memberY = row.y + 40f + memberIndex * 18f;
                        GUI.Label(new Rect(row.x + 14f, memberY,
                                row.width - 21f - (hasMemberWarnings ? 25f : 0f), 16f),
                            $"{member.udimTileNumber}  {member.slotName}", EditorStyles.miniLabel);
                        if (hasMemberWarnings && DrawImportWarningButton(
                                new Rect(row.xMax - 22f, memberY, 17f, 17f), memberWarnings, member.slotName))
                            ShowImportWarningDetails(member.slotName, memberWarnings);
                    }
                }
            }
        }

        private static bool TargetMatchesSearch(TexturePaintLogicalTarget target, string search)
        {
            if (target == null) return false;
            if (MatchesSearch(target.displayName, search)) return true;
            for (int i = 0; i < target.members.Count; i++)
                if (MatchesSearch(target.members[i].slotName, search) ||
                    MatchesSearch(target.members[i].udimTileNumber.ToString(), search)) return true;
            return false;
        }

        private void DrawTextureSetNavigator()
        {
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            TexturePaintLogicalTarget target = ActiveLogicalTarget;
            for (int i = 0; i < sets.Count; i++)
            {
                TextureSet set = sets[i];
                if (target == null || !IsSurfaceSelected(set.surface)) continue;
                string slots = set.surface.slotNames.Count > 0 ? string.Join(", ", set.surface.slotNames) : $"Surface {i + 1}";
                if (!MatchesSearch(slots, workspaceTargetSearch)) continue;
                Rect row = GUILayoutUtility.GetRect(10f, 48f, GUILayout.ExpandWidth(true));
                bool selected = selectedSurface == i;
                List<MeshReconstructionWarning> setWarnings = GetImportWarnings(set);
                bool hasSetWarnings = setWarnings != null && setWarnings.Count > 0;
                if (Event.current.type == EventType.Repaint)
                    (selected ? WorkspaceStyles.SelectedRow : WorkspaceStyles.Row).Draw(row, false, false, selected, false);
                Rect thumbnail = new Rect(row.x + 5f, row.y + 5f, 38f, 38f);
                DrawTextureThumbnail(thumbnail, set.GetVisibleTexture(selectedChannel), TexturePaintStoreFallback(selectedChannel));
                float warningReserve = hasSetWarnings ? 27f : 0f;
                GUI.Label(new Rect(thumbnail.xMax + 7f, row.y + 6f,
                    row.width - thumbnail.width - 16f - warningReserve, 20f), slots, EditorStyles.label);
                GUI.Label(new Rect(thumbnail.xMax + 7f, row.y + 25f,
                    row.width - thumbnail.width - 16f - warningReserve, 16f),
                    $"Texture set {i + 1} · {set.channels.Count} channels", EditorStyles.miniLabel);
                Rect selectionRect = new Rect(row.x, row.y, row.width - warningReserve, row.height);
                if (GUI.Button(selectionRect, GUIContent.none, GUIStyle.none))
                {
                    selectedSurface = i;
                    SyncActiveLayerSelection(set);
                    ApplyWorkspaceDisplay();
                }
                if (hasSetWarnings && DrawImportWarningButton(
                        new Rect(row.xMax - 23f, row.y + 15f, 18f, 18f), setWarnings, slots))
                    ShowImportWarningDetails(slots, setWarnings);
            }
        }

        private void DrawViewportRegion()
        {
            TextureSet set = ActiveTextureSet;
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool compact = EditorGUIUtility.currentViewWidth < 600f;
            if (!compact)
            {
                if (GUILayout.Button(new GUIContent("Frame 3D", "Frame the active target in the Scene view"), EditorStyles.toolbarButton, GUILayout.Width(62f))) FrameActiveTarget();
                GUILayout.Label(new GUIContent("2D UV", "Interactive synchronized UV canvas"), EditorStyles.miniLabel, GUILayout.Width(38f));
                GUILayout.Space(5f);
                DrawChannelToolbar(set);
            }
            else DrawCompactChannelToolbar(set);
            using (new EditorGUI.DisabledScope(IsLayerMaskMode(set)))
                uvPreviewBefore = GUILayout.Toggle(uvPreviewBefore,
                    new GUIContent("Before", "Show the original source texture in this 2D canvas only"),
                    EditorStyles.toolbarButton, GUILayout.Width(52f));
            if (IsLayerMaskMode(set))
            {
                bool nextSoloMask = GUILayout.Toggle(soloLayerMask,
                    new GUIContent("Solo Mask", "Show the active grayscale layer mask on the 3D model"),
                    EditorStyles.toolbarButton, GUILayout.Width(70f));
                if (nextSoloMask != soloLayerMask)
                    SetScenePreviewMode(nextSoloMask ? TexturePaintScenePreviewMode.LayerMask
                        : TexturePaintScenePreviewMode.Material);
            }
            wireframe = GUILayout.Toggle(wireframe,
                new GUIContent("Wire", "Show the UV wireframe in this 2D canvas only"),
                EditorStyles.toolbarButton, GUILayout.Width(42f));
            bool pathLayerActive = TryGetActivePathLayer(set, out _);
            if (pathLayerActive) uvColorSamplerArmed = false;
            using (new EditorGUI.DisabledScope(pathLayerActive))
                uvColorSamplerArmed = GUILayout.Toggle(uvColorSamplerArmed,
                    new GUIContent("Pick", "Sample the displayed 2D texture color (I)"),
                    EditorStyles.toolbarButton, GUILayout.Width(38f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("1:1", "Reset UV pan and zoom"), EditorStyles.toolbarButton, GUILayout.Width(34f)))
            { workspaceUVZoom = 1f; workspaceUVPan = Vector2.zero; }
            GUILayout.Label($"{workspaceUVZoom * 100f:0}%", EditorStyles.miniLabel, GUILayout.Width(42f));
            GUILayout.EndHorizontal();

            if (set != null && !IsLayerMaskMode(set)) DrawUVChannelAdjustments(set);

            Rect canvas = GUILayoutUtility.GetRect(100f, 10000f, 100f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (set != null) DrawUVCanvas(canvas, set);
            else Draw3DCompanionMessage(canvas);
        }

        private void DrawUVChannelAdjustments(TextureSet set)
        {
            TextureChannelTarget target = set?.GetChannel(selectedChannel);
            if (target == null) return;
            target.adjustments ??= new TexturePaintChannelAdjustments();
            bool normalChannel = selectedChannel == TexturePaintChannel.Normal;
            bool grayscale = TexturePaintChannelUtility.IsGrayscale(selectedChannel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            uvChannelAdjustmentsExpanded = EditorGUILayout.Foldout(uvChannelAdjustmentsExpanded,
                new GUIContent("Channel Adjustments · " +
                    TexturePaintChannelUtility.DisplayName(selectedChannel),
                    "Non-destructive adjustments applied only to the currently displayed channel."), true);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(target.adjustments.IsNeutral && !grayscale))
            {
                if (GUILayout.Button(new GUIContent("Reset", "Restore neutral values for this channel."),
                        EditorStyles.miniButton, GUILayout.Width(48f)))
                {
                    TexturePaintChannelAdjustments reset = target.adjustments.Clone();
                    reset.Reset();
                    ChangeChannelAdjustments(set, selectedChannel, reset);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (uvChannelAdjustmentsExpanded)
            {
                if (normalChannel)
                {
                    EditorGUILayout.HelpBox(
                        "Image adjustments are disabled for encoded normal vectors. Use Normal Touch-up or Normal Control instead.",
                        MessageType.Info);
                }
                else
                {
                    if (uvPreviewBefore)
                        EditorGUILayout.LabelField(
                            "The Before view shows the unadjusted source. Turn Before off to see these values.",
                            EditorStyles.wordWrappedMiniLabel);
                    TexturePaintChannelAdjustments next = target.adjustments.Clone();
                    EditorGUI.BeginChangeCheck();
                    next.brightness = EditorGUILayout.Slider(new GUIContent("Brightness (%)",
                        "Adds or removes brightness from this channel."), next.brightness * 100f,
                        -100f, 100f) * 0.01f;
                    next.contrast = EditorGUILayout.Slider(new GUIContent("Contrast (%)",
                        "Adjusts contrast around the midpoint of this channel."), next.contrast * 100f,
                        -100f, 100f) * 0.01f;
                    if (grayscale)
                    {
                        next.grayscaleAdjustmentCurve = EditorGUILayout.CurveField(new GUIContent(
                                "Brightness / Contrast Curve",
                                "X is the original grayscale value; Y is the amount of Brightness and Contrast applied. " +
                                "The default diagonal protects black and progressively affects brighter values."),
                            next.grayscaleAdjustmentCurve, Color.white, new Rect(0f, 0f, 1f, 1f),
                            GUILayout.Height(44f));
                        EditorGUILayout.LabelField(
                            "Black (0) is unaffected when the curve starts at 0. Raise or lower the curve to control how strongly each value range is adjusted.",
                            EditorStyles.wordWrappedMiniLabel);
                    }
                    using (new EditorGUI.DisabledScope(grayscale))
                    {
                        next.hue = EditorGUILayout.Slider(new GUIContent("Hue (degrees)",
                            "Rotates this channel around the color wheel."), next.hue, -180f, 180f);
                        next.vibrance = EditorGUILayout.Slider(new GUIContent("Vibrance (%)",
                            "Adjusts muted colors more strongly than colors that are already saturated."),
                            next.vibrance * 100f, -100f, 100f) * 0.01f;
                        next.saturation = EditorGUILayout.Slider(new GUIContent("Saturation (%)",
                            "Color saturation; 100% leaves this channel unchanged."),
                            next.saturation * 100f, 0f, 200f) * 0.01f;
                        EditorGUILayout.LabelField(new GUIContent("Color Balance",
                            "Adjust opposing color pairs for this channel."), EditorStyles.miniBoldLabel);
                        EditorGUI.indentLevel++;
                        next.colorBalance.x = EditorGUILayout.Slider("Cyan / Red (%)",
                            next.colorBalance.x * 100f, -100f, 100f) * 0.01f;
                        next.colorBalance.y = EditorGUILayout.Slider("Magenta / Green (%)",
                            next.colorBalance.y * 100f, -100f, 100f) * 0.01f;
                        next.colorBalance.z = EditorGUILayout.Slider("Yellow / Blue (%)",
                            next.colorBalance.z * 100f, -100f, 100f) * 0.01f;
                        EditorGUI.indentLevel--;
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        next.Normalize();
                        ChangeChannelAdjustments(set, selectedChannel, next);
                    }
                    if (grayscale)
                        EditorGUILayout.LabelField(
                            "Hue, Vibrance, Saturation, and Color Balance do not affect grayscale data channels.",
                            EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawChannelToolbar(TextureSet set)
        {
            TexturePaintChannel[] channels = (TexturePaintChannel[])Enum.GetValues(typeof(TexturePaintChannel));
            var available = new List<TexturePaintChannel>(channels.Length);
            for (int i = 0; i < channels.Length; i++)
                if (set?.GetChannel(channels[i]) != null) available.Add(channels[i]);
            if (available.Count > 7)
            {
                DrawCompactChannelToolbar(set);
                return;
            }
            for (int i = 0; i < available.Count; i++)
            {
                TexturePaintChannel channel = available[i];
                bool next = GUILayout.Toggle(selectedChannel == channel,
                    new GUIContent(ChannelShortName(channel), TexturePaintChannelUtility.DisplayName(channel)),
                    EditorStyles.toolbarButton, GUILayout.Width(channel == TexturePaintChannel.Albedo ? 38f : 42f));
                if (next && selectedChannel != channel)
                {
                    SetSelectedChannelAndRefreshSource(channel);
                    ApplyWorkspaceDisplay();
                }
            }
        }

        private static string ChannelShortName(TexturePaintChannel channel)
        {
            switch (channel)
            {
                case TexturePaintChannel.Albedo: return "Base";
                case TexturePaintChannel.Normal: return "Nrm";
                case TexturePaintChannel.Metallic: return "Met";
                case TexturePaintChannel.Roughness: return "Rgh";
                case TexturePaintChannel.AmbientOcclusion: return "AO";
                case TexturePaintChannel.Emission: return "Em";
                case TexturePaintChannel.Custom: return "C";
                case TexturePaintChannel.SkinColorMask: return "Skin";
                case TexturePaintChannel.Thickness: return "Thick";
                case TexturePaintChannel.DetailMask: return "Detail";
                case TexturePaintChannel.NormalControl: return "NC";
                default: return channel.ToString();
            }
        }

        private void DrawCompactChannelToolbar(TextureSet set)
        {
            TexturePaintChannel[] channels = (TexturePaintChannel[])Enum.GetValues(typeof(TexturePaintChannel));
            var available = new List<TexturePaintChannel>(channels.Length);
            var labels = new List<string>(channels.Length);
            int selected = -1;
            for (int i = 0; i < channels.Length; i++)
            {
                TexturePaintChannel channel = channels[i];
                if (set?.GetChannel(channel) == null) continue;
                if (channel == selectedChannel) selected = available.Count;
                available.Add(channel);
                labels.Add(channel == TexturePaintChannel.Albedo ? "Base" :
                    TexturePaintChannelUtility.DisplayName(channel));
            }
            if (available.Count == 0)
            {
                GUILayout.Label("No channel", EditorStyles.miniLabel, GUILayout.Width(72f));
                return;
            }
            if (selected < 0)
            {
                selected = 0;
                SetSelectedChannelAndRefreshSource(available[0]);
            }
            int next = EditorGUILayout.Popup(selected, labels.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(78f));
            TexturePaintChannel nextChannel = available[Mathf.Clamp(next, 0, available.Count - 1)];
            if (nextChannel == selectedChannel) return;
            SetSelectedChannelAndRefreshSource(nextChannel);
            ApplyWorkspaceDisplay();
        }

        private void DrawUVCanvas(Rect canvas, TextureSet set)
        {
            EditorGUI.DrawRect(canvas, WorkspaceStyles.CanvasColor);
            DrawCheckerboard(canvas, 18f);
            float baseSize = Mathf.Max(32f, Mathf.Min(canvas.width, canvas.height) - 28f);
            float size = baseSize * Mathf.Clamp(workspaceUVZoom, 0.2f, 8f);
            Vector2 center = canvas.center + workspaceUVPan;
            Rect textureRect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            bool maskMode = IsLayerMaskMode(set);
            TexturePaintLayer activeLayer = maskMode ? set.layers[set.activeLayerIndex] : null;
            Texture texture = maskMode ? set.GetLayerMaskPreview(activeLayer) :
                GetWorkspacePreviewTexture(set, uvPreviewBefore, true);
            if (texture != null) GUI.DrawTexture(textureRect, texture, ScaleMode.StretchToFill, false);
            else EditorGUI.DrawRect(textureRect, TexturePaintStoreFallback(selectedChannel));

            GUI.BeginClip(canvas);
            Rect localTexture = new Rect(textureRect.x - canvas.x, textureRect.y - canvas.y, textureRect.width, textureRect.height);
            DrawUVWireframe(set.surface.mesh, localTexture);
            if (!maskMode) DrawUVPaths(set, localTexture);
            DrawUVBrushCursor(set, localTexture);
            GUI.EndClip();
            HandleUVCanvasInput(canvas, textureRect, set);

            Rect badge = new Rect(canvas.x + 8f, canvas.y + 8f, 230f, 22f);
            bool groupPreview = !uvPreviewBefore && (uint)set.activeLayerIndex < (uint)set.layers.Count &&
                set.layers[set.activeLayerIndex]?.kind == TexturePaintLayerKind.Group;
            GUI.Label(badge, maskMode ? "LAYER MASK · grayscale paint target" : uvPreviewBefore ? "SOURCE · before painting" : groupPreview
                ? $"SELECTED GROUP · {selectedChannel}"
                : $"DESTINATION · {selectedChannel}", WorkspaceStyles.CanvasBadge);
            Rect help = new Rect(canvas.x + 8f, canvas.yMax - 24f, canvas.width - 16f, 18f);
            bool activePath = TryGetActivePathLayer(set, out TexturePaintLayer helpPathLayer);
            string inputHelp = maskMode
                ? "LAYER MASK · LMB paint grayscale · MMB/RMB pan · Wheel zoom · Erase restores mask background"
                : activePath && !helpPathLayer.visible
                ? "PATH LAYER DISABLED · Enable the layer to display or edit its spline"
                : activePath && helpPathLayer.spline?.worldSpace == false
                ? "Shift+LMB add · Ctrl+LMB insert · LMB drag point/green curve/blue width handles · MMB/RMB pan · Wheel zoom"
                : activePath
                ? "3D SPLINE - Edit points in the Scene view - MMB/RMB pan - Wheel zoom"
                : CanStartFreehandPaint(set)
                    ? "LMB paint · Shift-drag constrain axis · MMB/RMB pan · Wheel zoom · Ctrl-click clone source · Shift+RMB size/hardness"
                    : "Select or create a Paint layer to use freehand tools · MMB/RMB pan · Wheel zoom";
            GUI.Label(new Rect(help.x + 1.5f, help.y + 1.5f, help.width, help.height), inputHelp,
                WorkspaceStyles.CanvasHintShadow);
            GUI.Label(help, inputHelp, WorkspaceStyles.CanvasHint);
        }

        private void Draw3DCompanionMessage(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, WorkspaceStyles.CanvasColor);
            GUI.Label(new Rect(canvas.x + 20f, canvas.center.y - 32f, canvas.width - 40f, 64f),
                "3D painting remains synchronized in the Scene view.\nEnable 2D UV to inspect or paint the same target and active layer.",
                WorkspaceStyles.CenterMessage);
        }

        private void HandleUVCanvasInput(Rect canvas, Rect textureRect, TextureSet set)
        {
            Event current = Event.current;
            TexturePaintLayer uvSplineLayer = null;
            bool activeSplineLayer = !IsLayerMaskMode(set) &&
                TryGetActivePathLayer(set, out uvSplineLayer);
            bool visibleSplineLayer = activeSplineLayer &&
                IsActiveSplineAuthoringLayer(set, set.activeLayerIndex);
            bool authoringSplineLayer = visibleSplineLayer && uvSplineLayer.spline?.worldSpace == false;
            if (authoringSplineLayer) spline = uvSplineLayer.spline;
            if (modifierBrushDrag && HandleBrushModifierDrag(current)) return;
            if (!canvas.Contains(current.mousePosition))
            {
                if (current.type == EventType.MouseLeaveWindow)
                {
                    EndUVWindowInteraction();
                    return;
                }
                if (current.rawType == EventType.MouseUp && current.button == 0)
                {
                    if (uvDraggingSplineHandle != UVSplineHandleKind.None)
                    {
                        uvDraggingSplinePoint = -1;
                        uvDraggingSplineHandle = UVSplineHandleKind.None;
                        uvSplineHandleUndoStarted = false;
                        ScheduleSplineReapply(true);
                    }
                    else if (splineReapplyPending) ScheduleSplineReapply(true);
                    if (uvStrokeActive) EndUVStroke(true);
                }
                return;
            }
            if (HandleBrushModifierDrag(current)) return;
            if (current.type == EventType.ScrollWheel)
            {
                float oldZoom = workspaceUVZoom;
                workspaceUVZoom = Mathf.Clamp(workspaceUVZoom * Mathf.Exp(-current.delta.y * 0.08f), 0.2f, 8f);
                Vector2 fromCenter = current.mousePosition - canvas.center - workspaceUVPan;
                workspaceUVPan -= fromCenter * (workspaceUVZoom / oldZoom - 1f);
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDown && (current.button == 2 || current.button == 1))
            {
                if (current.button == 1 && authoringSplineLayer)
                {
                    int contextPoint = FindSplinePointAt(textureRect, current.mousePosition);
                    if (contextPoint >= 0)
                    {
                        SelectSingleSplinePoint(contextPoint);
                        ShowSplinePointContextMenu(set, contextPoint);
                        current.Use();
                        return;
                    }
                }
                uvPanning = true; uvPanStartMouse = current.mousePosition; uvPanStart = workspaceUVPan;
                current.Use(); return;
            }
            if (uvPanning && current.type == EventType.MouseDrag)
            {
                workspaceUVPan = uvPanStart + current.mousePosition - uvPanStartMouse;
                current.Use(); return;
            }
            if (uvPanning && (current.rawType == EventType.MouseUp || current.type == EventType.MouseLeaveWindow))
            {
                uvPanning = false; current.Use(); return;
            }
            if (current.rawType == EventType.MouseUp && current.button == 0)
            {
                if (uvDraggingSplineHandle != UVSplineHandleKind.None)
                {
                    uvDraggingSplinePoint = -1;
                    uvDraggingSplineHandle = UVSplineHandleKind.None;
                    uvSplineHandleUndoStarted = false;
                    ScheduleSplineReapply(true);
                    current.Use();
                    return;
                }
                if (splineReapplyPending)
                {
                    ScheduleSplineReapply(true);
                    current.Use();
                    return;
                }
                if (uvStrokeActive)
                {
                    EndUVStroke(true);
                    current.Use();
                    return;
                }
            }
            if (authoringSplineLayer && current.type == EventType.MouseDown && current.button == 0)
            {
                UVSplineHandleKind selectedHandle = PathEditModeAllowsAdjust(pathEditMode)
                    ? FindSelectedUVSplineHandleAt(uvSplineLayer.spline, textureRect,
                        current.mousePosition)
                    : UVSplineHandleKind.None;
                if (selectedHandle != UVSplineHandleKind.None)
                {
                    uvDraggingSplinePoint = selectedSplinePoint;
                    uvDraggingSplineHandle = selectedHandle;
                    uvDraggingSplineIncoming = selectedHandle == UVSplineHandleKind.Incoming;
                    uvSplineHandleUndoStarted = false;
                    current.Use();
                    return;
                }
                int point = FindSplinePointAt(textureRect, current.mousePosition);
                if (point >= 0)
                {
                    selectedSplinePoint = point;
                    selectedSplinePoints?.Clear();
                    selectedSplinePoints?.Add(point);
                    uvDraggingSplinePoint = PathEditModeAllowsMove(pathEditMode) ? point : -1;
                    uvDraggingSplineHandle = PathEditModeAllowsMove(pathEditMode)
                        ? UVSplineHandleKind.Anchor : UVSplineHandleKind.None;
                    uvSplineHandleUndoStarted = false;
                }
                else if (PathEditModeAllowsTopology(pathEditMode) &&
                    (current.control || current.command))
                {
                    TryInsertUVSplinePoint(set, textureRect, current.mousePosition);
                }
                else if (PathEditModeAllowsTopology(pathEditMode) && current.shift &&
                    TryCanvasUV(current.mousePosition, textureRect, out Vector2 addUV))
                {
                    AddUVSplinePoint(set, MakeDirectUVSample(set, addUV));
                }
                else if (!PathEditModeAllowsTopology(pathEditMode) &&
                    (current.shift || current.control || current.command))
                    ShowWorkspaceStatus(pathEditMode + " mode does not add or insert points");
                current.Use();
                return;
            }
            if (visibleSplineLayer && !authoringSplineLayer && current.button == 0 &&
                (current.type == EventType.MouseDown || current.type == EventType.MouseDrag))
            {
                if (current.type == EventType.MouseDown)
                    ShowWorkspaceStatus("This is a 3D spline. Edit it in the Scene view.");
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 0 && authoringSplineLayer &&
                uvDraggingSplineHandle != UVSplineHandleKind.None && uvDraggingSplinePoint >= 0)
            {
                if (!uvSplineHandleUndoStarted)
                {
                    string label = uvDraggingSplineHandle == UVSplineHandleKind.Width
                        ? "Adjust UV Path Point Width"
                        : uvDraggingSplineHandle == UVSplineHandleKind.Anchor
                            ? "Move UV Path Point" : "Adjust UV Path Curve";
                    BeginLightweightPathUndo(set, label);
                    uvSplineHandleUndoStarted = true;
                }
                if (uvDraggingSplineHandle == UVSplineHandleKind.Width)
                    MoveUVSplineWidth(set, uvDraggingSplinePoint, textureRect, current.mousePosition);
                else
                {
                    Vector2 dragUV = CanvasPointToUV(current.mousePosition, textureRect);
                    if (uvDraggingSplineHandle == UVSplineHandleKind.Anchor)
                        MoveUVSplinePoint(set, uvDraggingSplinePoint, dragUV);
                    else
                        MoveUVSplineControl(set, uvDraggingSplinePoint, uvDraggingSplineIncoming, dragUV);
                }
                current.Use();
                return;
            }

            // MouseMove is generated continuously by the standalone 2D window. Resolving the UV
            // back onto a dense combined mesh here did no input work and duplicated the cursor's
            // repaint-only lookup, making hover alone as expensive as painting.
            if (current.type != EventType.MouseDown && current.type != EventType.MouseDrag) return;

            Vector2 paintInputPoint = uvStrokeActive
                ? ResolveStrokeViewPoint(current.mousePosition)
                : current.mousePosition;
            if (!TryCanvasUV(paintInputPoint, textureRect, out Vector2 uv)) return;

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (!authoringSplineLayer && geometryFillMode != 0 && CanStartFreehandPaint(set))
                {
                    if (TryMakeUVSample(set, uv, out StrokeSample geometrySample))
                        ApplyGeometryFill(set, geometrySample);
                }
                else if (!authoringSplineLayer && uvColorSamplerArmed)
                {
                    SampleSurfaceColor(set, uv, uvPreviewBefore); uvColorSamplerArmed = false;
                }
                else if (!authoringSplineLayer && CanStartFreehandPaint(set) &&
                    tool == TexturePaintTool.Clone && current.control)
                {
                    cloneSourceUV = uv; ShowWorkspaceStatus("Clone source sampled");
                }
                else if (CanStartFreehandPaint(set))
                {
                    BeginPaintAt(set, MakeDirectUVSample(set, uv), true, true,
                        current.mousePosition, current.shift);
                    uvStrokeActive = strokeActive;
                }
                else ShowPaintLayerRequiredStatus(set);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && uvStrokeActive)
            {
                using (controller.Painting.Performance.StrokeDiagnostics.MeasureInputEvent())
                    ContinuePaintAt(MakeDirectUVSample(set, uv));
                current.Use();
            }
        }

        private StrokeSample MakeDirectUVSample(TextureSet set, Vector2 uv)
        {
            // The 2D canvas owns texture coordinates directly. Treat normalized UV as its stroke
            // plane so spacing, stabilization, fade, taper, and splatter never need mesh projection.
            float pressure = Event.current != null && Event.current.pressure > 0f
                ? Event.current.pressure : 1f;
            return CreateDirectUVSample(set, uv, pressure);
        }

        internal static StrokeSample CreateDirectUVSample(TextureSet set, Vector2 uv, float pressure = 1f)
        {
            Vector3 planePoint = new Vector3(uv.x, uv.y, 0f);
            return new StrokeSample(planePoint, Vector3.forward, uv, set.surface.index, -1)
            {
                surfaceId = set.persistentId,
                projectionDirection = Vector3.back,
                pressure = Mathf.Clamp01(pressure),
                uvIsland = -1,
                slotName = string.Empty
            };
        }

        private bool TryMakeUVSample(TextureSet set, Vector2 uv, out StrokeSample sample)
        {
            sample = default;
            ReconstructedSurface surface = set?.surface;
            if (surface == null || !surface.TryUVToWorld(uv, uvPreferredTriangle, out Vector3 world,
                out Vector3 normal, out int triangle, out Vector3 barycentric) || !IsSelectedSlotHit(surface, triangle)) return false;
            uvPreferredTriangle = triangle;
            float pressure = Event.current != null && Event.current.pressure > 0f ? Event.current.pressure : 1f;
            sample = new StrokeSample(world, normal, uv, surface.index, triangle)
            {
                surfaceId = set.persistentId,
                barycentric = barycentric,
                projectionDirection = -normal,
                pressure = Mathf.Clamp01(pressure),
                uvIsland = surface.triangleIslands != null && (uint)triangle < (uint)surface.triangleIslands.Length
                    ? surface.triangleIslands[triangle] : -1,
                slotName = surface.GetTriangleSlotName(triangle)
            };
            return true;
        }

        private void EndUVStroke(bool commit)
        {
            if (strokeActive)
            {
                if (commit) EndPaint();
                else
                {
                    controller.Painting.Performance.StrokeDiagnostics.RecordWorkingSet(
                        completedStrokePoints.Count, previousContactSamples.Count);
                    controller.Painting.EndStroke(false);
                    strokeActive = false;
                    previousContactSamples.Clear();
                    sampledStrokePoints.Clear();
                    strokeTextureSets.Clear();
                }
            }
            uvStrokeActive = false;
            directUVStroke = false;
            paintGestureActive = false;
            ResetStrokeViewConstraint();
        }

        private void DrawUVWireframe(Mesh mesh, Rect textureRect)
        {
            if (!wireframe || mesh == null || Event.current.type != EventType.Repaint) return;
            if (!workspaceUVEdges.TryGetValue(mesh, out Vector2[] edges))
            {
                int[] triangles = mesh.triangles;
                Vector2[] uv = mesh.uv;
                if (uv == null || uv.Length != mesh.vertexCount) return;
                edges = new Vector2[triangles.Length * 2];
                int write = 0;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector2 a = uv[triangles[i]], b = uv[triangles[i + 1]], c = uv[triangles[i + 2]];
                    edges[write++] = a; edges[write++] = b;
                    edges[write++] = b; edges[write++] = c;
                    edges[write++] = c; edges[write++] = a;
                }
                workspaceUVEdges[mesh] = edges;
            }
            if (workspaceUVLineBuffer == null || workspaceUVLineBuffer.Length != edges.Length)
                workspaceUVLineBuffer = new Vector3[edges.Length];
            for (int i = 0; i < edges.Length; i++)
                workspaceUVLineBuffer[i] = new Vector3(textureRect.x + edges[i].x * textureRect.width,
                    textureRect.y + (1f - edges[i].y) * textureRect.height, 0f);
            Handles.color = new Color(1f, 0.62f, 0.12f, 0.9f);
            Handles.DrawLines(workspaceUVLineBuffer);
        }

        private void DrawUVBrushCursor(TextureSet set, Rect textureRect)
        {
            if (Event.current.type != EventType.Repaint || !CanStartFreehandPaint(set) ||
                TryGetActivePathLayer(set, out _)) return;
            Event current = Event.current;
            Vector2 mouse = current.mousePosition;
            if (!TryCanvasUV(mouse, textureRect, out Vector2 uv)) return;
            Vector2 point = new Vector2(textureRect.x + uv.x * textureRect.width,
                textureRect.y + (1f - uv.y) * textureRect.height);
            float radius = Mathf.Max(2f, ActiveBrush.size * textureRect.width);
            Handles.color = new Color(paintColor.r, paintColor.g, paintColor.b, 0.95f);
            DrawUVBrushOutline(point, radius, 1f);
            Handles.color = new Color(1f, 1f, 1f, 0.65f);
            DrawUVBrushOutline(point, radius, ActiveBrush.hardness);
            float radians = ActiveBrush.rotation * Mathf.Deg2Rad;
            Handles.DrawLine(point, point + new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)) * radius);
        }

        private void DrawUVBrushOutline(Vector2 center, float radius, float scale)
        {
            const int circleSegments = 48;
            bool square = ActiveBrush.shape == BrushPreset.Shape.Square;
            int segments = square ? 4 : circleSegments;
            Vector3[] points = square ? workspaceUVSquareCursor : workspaceUVCircleCursor;
            float rotation = ActiveBrush.rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rotation), sin = Mathf.Sin(rotation);
            for (int i = 0; i <= segments; i++)
            {
                float x;
                float y;
                if (square)
                {
                    int corner = i & 3;
                    x = corner == 0 || corner == 3 ? -scale : scale;
                    y = corner < 2 ? -scale : scale;
                }
                else
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    x = Mathf.Cos(angle) * scale;
                    y = Mathf.Sin(angle) * scale;
                }
                Vector2 brushPoint = new Vector2(x * cos - y * sin, x * sin + y * cos);
                points[i] = center + new Vector2(brushPoint.x, -brushPoint.y) * radius;
            }
            Handles.DrawAAPolyLine(2f, points);
        }

        private void DrawUVPaths(TextureSet set, Rect textureRect)
        {
            if (set == null || Event.current.type != EventType.Repaint) return;
            int layerIndex = set.activeLayerIndex;
            if (!IsActiveSplineAuthoringLayer(set, layerIndex)) return;
            TexturePaintSpline path = set.layers[layerIndex].spline;
            if (path == null || path.worldSpace || path.PointCount == 0) return;
            path.EnsureControlPoints();
            var strips = new List<List<Vector3>> { new List<Vector3>() };
            if (path.PointCount == 1)
            {
                Vector2 uv = path.uvPoints[0];
                strips[0].Add(new Vector3(textureRect.x + uv.x * textureRect.width,
                    textureRect.y + (1f - uv.y) * textureRect.height));
            }
            else if (path.worldSpace && set.surface != null)
            {
                IReadOnlyList<TextureSet> projectionSets = GetSplineProjectionSets(set.surface);
                int previousIsland = -1;
                Vector2 previousUV = path.uvPoints[0];
                bool hasPrevious = false;
                for (int segment = 0; segment < path.SegmentCount; segment++)
                {
                    int next = (segment + 1) % path.PointCount;
                    int preferredSurface = segment < path.surfaceIndices.Count
                        ? path.surfaceIndices[segment] : set.surface.index;
                    int preferredTriangle = segment < path.triangleIndices.Count
                        ? path.triangleIndices[segment] : -1;
                    for (int step = 0; step <= 32; step++)
                    {
                        float t = step / 32f;
                        path.EvaluateSegment(segment, next, t, out Vector3 world, out _);
                        Vector3 hint = Vector3.Slerp(path.worldNormals[segment], path.worldNormals[next], t);
                        bool found = TryProjectWorldPathPoint(projectionSets, world, hint,
                            preferredSurface, preferredTriangle, out TextureSet projectedSet,
                            out _, out _, out Vector2 uv, out int triangle, out _);
                        if (!found) continue;
                        ReconstructedSurface displaySurface = projectedSet.surface;
                        preferredSurface = displaySurface.index;
                        preferredTriangle = triangle;
                        if (!ReferenceEquals(projectedSet, set))
                        {
                            if (strips[strips.Count - 1].Count > 0) strips.Add(new List<Vector3>());
                            hasPrevious = false;
                            continue;
                        }
                        int island = displaySurface.triangleIslands != null &&
                            (uint)triangle < (uint)displaySurface.triangleIslands.Length
                                ? displaySurface.triangleIslands[triangle] : -1;
                        bool discontinuity = hasPrevious &&
                            ((previousIsland >= 0 && island >= 0 && previousIsland != island) ||
                             Vector2.Distance(previousUV, uv) > 0.2f);
                        if (discontinuity && strips[strips.Count - 1].Count > 0)
                            strips.Add(new List<Vector3>());
                        strips[strips.Count - 1].Add(new Vector3(
                            textureRect.x + uv.x * textureRect.width,
                            textureRect.y + (1f - uv.y) * textureRect.height));
                        previousIsland = island; previousUV = uv; hasPrevious = true;
                    }
                }
            }
            else
            {
                for (int segment = 0; segment < path.SegmentCount; segment++)
                {
                    int next = (segment + 1) % path.PointCount;
                    for (int step = 0; step <= 24; step++)
                    {
                        path.EvaluateSegment(segment, next, step / 24f, out _, out Vector2 uv);
                        strips[0].Add(new Vector3(textureRect.x + uv.x * textureRect.width,
                            textureRect.y + (1f - uv.y) * textureRect.height));
                    }
                }
            }
            Handles.color = new Color(1f, 0.72f, 0.12f, 1f);
            for (int strip = 0; strip < strips.Count; strip++)
                if (strips[strip].Count > 1) Handles.DrawAAPolyLine(3f, strips[strip].ToArray());
            if (selectedSplinePoint >= 0 && selectedSplinePoint < path.PointCount)
            {
                if (PathEditModeAllowsAdjust(pathEditMode))
                    DrawSelectedUVSplineHandles(path, textureRect, selectedSplinePoint);
            }
            for (int pointIndex = 0; pointIndex < path.PointCount; pointIndex++)
            {
                if (path.worldSpace && pointIndex < path.surfaceIndices.Count &&
                    path.surfaceIndices[pointIndex] != set.surface.index) continue;
                Vector2 uv = path.uvPoints[pointIndex];
                Vector2 point = new Vector2(textureRect.x + uv.x * textureRect.width,
                    textureRect.y + (1f - uv.y) * textureRect.height);
                Handles.color = pointIndex == selectedSplinePoint
                    ? new Color(1f, 0.42f, 0.04f, 1f) : new Color(0.92f, 0.05f, 0.04f, 1f);
                Handles.DrawSolidDisc(point, Vector3.forward, pointIndex == selectedSplinePoint ? 5f : 3.5f);
            }
        }

        private void DrawSelectedUVSplineHandles(TexturePaintSpline path, Rect textureRect,
            int pointIndex)
        {
            path.EnsureControlPoints();
            Vector2 anchor = UVToCanvasPoint(textureRect, path.uvPoints[pointIndex]);
            if (path.useBezier && path.showControls)
            {
                Vector2 incoming = UVToCanvasPoint(textureRect, path.uvInControls[pointIndex]);
                Vector2 outgoing = UVToCanvasPoint(textureRect, path.uvOutControls[pointIndex]);
                Color green = new Color(0.2f, 1f, 0.32f, 1f);
                Handles.color = new Color(green.r, green.g, green.b, 0.72f);
                Handles.DrawLine(anchor, incoming, 2f);
                Handles.DrawLine(anchor, outgoing, 2f);
                Handles.color = green;
                if (Vector2.Distance(anchor, incoming) > 2f)
                    Handles.DrawSolidDisc(incoming, Vector3.forward, 4.5f);
                if (Vector2.Distance(anchor, outgoing) > 2f)
                    Handles.DrawSolidDisc(outgoing, Vector3.forward, 4.5f);
            }

            if (!TryGetUVSplineWidthHandle(path, textureRect, pointIndex,
                    out Vector2 widthCenter, out _, out _, out Vector2 widthHandle)) return;
            Color blue = new Color(0.12f, 0.55f, 1f, 1f);
            Handles.color = new Color(blue.r, blue.g, blue.b, 0.75f);
            Handles.DrawLine(widthCenter, widthHandle, 2.5f);
            Handles.color = blue;
            Handles.DrawSolidDisc(widthHandle, Vector3.forward, 5.5f);
        }

        private UVSplineHandleKind FindSelectedUVSplineHandleAt(TexturePaintSpline path,
            Rect textureRect, Vector2 mouse)
        {
            if (path == null || path.worldSpace || selectedSplinePoint < 0 ||
                selectedSplinePoint >= path.PointCount ||
                !PathEditModeAllowsAdjust(pathEditMode)) return UVSplineHandleKind.None;
            path.EnsureControlPoints();
            if (TryGetUVSplineWidthHandle(path, textureRect, selectedSplinePoint,
                    out _, out _, out _, out Vector2 widthHandle) &&
                Vector2.Distance(mouse, widthHandle) <= 10f) return UVSplineHandleKind.Width;
            if (!path.useBezier || !path.showControls) return UVSplineHandleKind.None;

            Vector2 anchor = UVToCanvasPoint(textureRect, path.uvPoints[selectedSplinePoint]);
            Vector2 incoming = UVToCanvasPoint(textureRect, path.uvInControls[selectedSplinePoint]);
            Vector2 outgoing = UVToCanvasPoint(textureRect, path.uvOutControls[selectedSplinePoint]);
            if (Vector2.Distance(anchor, incoming) > 2f && Vector2.Distance(mouse, incoming) <= 10f)
                return UVSplineHandleKind.Incoming;
            if (Vector2.Distance(anchor, outgoing) > 2f && Vector2.Distance(mouse, outgoing) <= 10f)
                return UVSplineHandleKind.Outgoing;
            return UVSplineHandleKind.None;
        }

        private bool TryGetUVSplineWidthHandle(TexturePaintSpline path, Rect textureRect,
            int pointIndex, out Vector2 center, out Vector2 normal, out float displayScale,
            out Vector2 handle)
        {
            center = normal = handle = default;
            displayScale = 0f;
            if (path == null || (uint)pointIndex >= (uint)path.PointCount) return false;
            path.EnsureControlPoints();
            center = UVToCanvasPoint(textureRect, path.uvPoints[pointIndex]);
            Vector2 incoming = UVToCanvasPoint(textureRect, path.uvInControls[pointIndex]);
            Vector2 outgoing = UVToCanvasPoint(textureRect, path.uvOutControls[pointIndex]);
            Vector2 tangent = outgoing - incoming;
            if (tangent.sqrMagnitude < 4f)
            {
                int previous = pointIndex > 0 ? pointIndex - 1 : path.closed && path.PointCount > 1
                    ? path.PointCount - 1 : pointIndex;
                int next = pointIndex + 1 < path.PointCount ? pointIndex + 1 :
                    path.closed && path.PointCount > 1 ? 0 : pointIndex;
                tangent = UVToCanvasPoint(textureRect, path.uvPoints[next]) -
                    UVToCanvasPoint(textureRect, path.uvPoints[previous]);
            }
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector2.right;
            tangent.Normalize();
            normal = new Vector2(-tangent.y, tangent.x);

            TexturePaintSplineSettings settings = null;
            TextureSet set = ActiveTextureSet;
            if (TryGetActivePathLayer(set, out TexturePaintLayer layer) && layer.spline == path)
                settings = layer.splineSettings;
            float baseRadius = Mathf.Max(0.0001f, settings?.brushSize ?? ActiveBrush.size) *
                textureRect.width;
            displayScale = Mathf.Clamp(baseRadius, 18f, Mathf.Max(18f, textureRect.width * 0.12f));
            handle = center + normal * displayScale * Mathf.Clamp(path.widths[pointIndex], 0.05f, 4f);
            return true;
        }

        private int FindSplinePointAt(Rect textureRect, Vector2 mouse)
        {
            TextureSet set = ActiveTextureSet;
            if (spline == null || set == null || !IsActiveSplineAuthoringLayer(set, set.activeLayerIndex) ||
                set.layers[set.activeLayerIndex].spline != spline || spline.worldSpace) return -1;
            int best = -1; float bestDistance = 9f;
            for (int i = 0; i < spline.PointCount; i++)
            {
                if (spline.worldSpace && i < spline.surfaceIndices.Count &&
                    spline.surfaceIndices[i] != set.surface.index) continue;
                Vector2 uv = spline.uvPoints[i];
                Vector2 point = new Vector2(textureRect.x + uv.x * textureRect.width,
                    textureRect.y + (1f - uv.y) * textureRect.height);
                float distance = Vector2.Distance(point, mouse);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        private void AddUVSplinePoint(TextureSet set, StrokeSample sample)
        {
            TextureSet activeSet = ActivateSurfaceForSpline(set.surface);
            if (activeSet == null) return;
            EnsureSplineLayer(activeSet);
            if (spline?.worldSpace != false)
            {
                ShowWorkspaceStatus("This is a 3D spline. Edit it in the Scene view or change Spline Space in Properties.");
                return;
            }
            BeginLightweightPathUndo(activeSet, "Add UV Path Point");
            spline.AddPoint(sample.worldPosition, sample.uv, sample.surfaceIndex, sample.triangleIndex, sample.worldNormal);
            selectedSplinePoint = spline.PointCount - 1;
            NormalizeTwoDimensionalSplinePoint(activeSet, spline, selectedSplinePoint);
            if (selectedSplinePoint > 0)
                NormalizeTwoDimensionalSplinePoint(activeSet, spline, selectedSplinePoint - 1);
            CompleteLightweightPathEdit(activeSet, true);
            SceneView.RepaintAll();
        }

        private bool TryInsertUVSplinePoint(TextureSet set, Rect textureRect, Vector2 mouse)
        {
            if (!TryGetActivePathLayer(set, out TexturePaintLayer layer) || layer.spline == null ||
                layer.spline.worldSpace || layer.spline.SegmentCount == 0 ||
                !TryFindNearestUVSplineSegment(set, layer.spline, textureRect, mouse,
                    out int segment, out float segmentT)) return false;

            spline = layer.spline;
            BeginLightweightPathUndo(set, "Insert UV Path Point");
            int inserted = spline.InsertPointAfter(segment, segmentT);
            if (inserted < 0) return false;
            selectedSplinePoint = inserted;
            selectedSplinePoints?.Clear();
            selectedSplinePoints?.Add(inserted);
            NormalizeTwoDimensionalSplinePoint(set, spline, inserted);
            CompleteLightweightPathEdit(set, true);
            SceneView.RepaintAll();
            return true;
        }

        private bool TryFindNearestUVSplineSegment(TextureSet set, TexturePaintSpline targetSpline,
            Rect textureRect, Vector2 mouse, out int bestSegment, out float bestT)
        {
            bestSegment = -1;
            bestT = 0f;
            if (targetSpline?.worldSpace != false) return false;
            float bestDistanceSquared = SplineInsertTolerancePixels * SplineInsertTolerancePixels;
            targetSpline.EnsureControlPoints();
            if (!targetSpline.worldSpace)
            {
                const int subdivisions = 32;
                for (int segment = 0; segment < targetSpline.SegmentCount; segment++)
                {
                    int next = (segment + 1) % targetSpline.PointCount;
                    targetSpline.EvaluateSegment(segment, next, 0f, out _, out Vector2 previousUV);
                    Vector2 previousScreen = UVToCanvasPoint(textureRect, previousUV);
                    float previousT = 0f;
                    for (int step = 1; step <= subdivisions; step++)
                    {
                        float t = step / (float)subdivisions;
                        targetSpline.EvaluateSegment(segment, next, t, out _, out Vector2 uv);
                        Vector2 screen = UVToCanvasPoint(textureRect, uv);
                        AccumulateSplineInsertionCandidate(mouse, previousScreen, screen, segment,
                            previousT, t, ref bestSegment, ref bestT, ref bestDistanceSquared);
                        previousScreen = screen;
                        previousT = t;
                    }
                }
                return bestSegment >= 0;
            }

            IReadOnlyList<TextureSet> projectionSets = GetSplineProjectionSets(set.surface);
            const int worldSubdivisions = 32;
            for (int segment = 0; segment < targetSpline.SegmentCount; segment++)
            {
                int next = (segment + 1) % targetSpline.PointCount;
                int preferredSurface = targetSpline.surfaceIndices[segment];
                int preferredTriangle = targetSpline.triangleIndices[segment];
                bool hasPrevious = false;
                Vector2 previousScreen = default;
                Vector2 previousUV = default;
                int previousIsland = -1;
                float previousT = 0f;
                for (int step = 0; step <= worldSubdivisions; step++)
                {
                    float t = step / (float)worldSubdivisions;
                    targetSpline.EvaluateSegment(segment, next, t, out Vector3 world, out _);
                    Vector3 hint = Vector3.Slerp(targetSpline.worldNormals[segment],
                        targetSpline.worldNormals[next], t);
                    bool found = TryProjectWorldPathPoint(projectionSets, world, hint,
                        preferredSurface, preferredTriangle, out TextureSet projectedSet,
                        out _, out _, out Vector2 uv, out int triangle, out _);
                    if (!found || !ReferenceEquals(projectedSet, set))
                    {
                        hasPrevious = false;
                        continue;
                    }
                    preferredSurface = projectedSet.surface.index;
                    preferredTriangle = triangle;
                    int island = projectedSet.surface.triangleIslands != null &&
                        (uint)triangle < (uint)projectedSet.surface.triangleIslands.Length
                            ? projectedSet.surface.triangleIslands[triangle] : -1;
                    bool discontinuity = hasPrevious &&
                        ((previousIsland >= 0 && island >= 0 && previousIsland != island) ||
                         Vector2.Distance(previousUV, uv) > 0.2f);
                    Vector2 screen = UVToCanvasPoint(textureRect, uv);
                    if (hasPrevious && !discontinuity)
                        AccumulateSplineInsertionCandidate(mouse, previousScreen, screen, segment,
                            previousT, t, ref bestSegment, ref bestT, ref bestDistanceSquared);
                    previousScreen = screen;
                    previousUV = uv;
                    previousIsland = island;
                    previousT = t;
                    hasPrevious = true;
                }
            }
            return bestSegment >= 0;
        }

        private static Vector2 UVToCanvasPoint(Rect textureRect, Vector2 uv)
        {
            return new Vector2(textureRect.x + uv.x * textureRect.width,
                textureRect.y + (1f - uv.y) * textureRect.height);
        }

        private static Vector2 CanvasPointToUV(Vector2 point, Rect textureRect)
        {
            return new Vector2((point.x - textureRect.x) / Mathf.Max(1f, textureRect.width),
                1f - (point.y - textureRect.y) / Mathf.Max(1f, textureRect.height));
        }

        private void MoveUVSplinePoint(TextureSet set, int point, Vector2 uv)
        {
            if (spline == null || (uint)point >= (uint)spline.PointCount) return;
            if (spline.worldSpace) return;
            Vector2 delta = uv - spline.uvPoints[point];
            spline.uvPoints[point] = uv;
            spline.EnsureControlPoints();
            spline.uvInControls[point] += delta; spline.uvOutControls[point] += delta;
            NormalizeTwoDimensionalSplinePoint(set, spline, point);
            spline.RefreshStraightTangents();
            CompleteLightweightPathEdit(set, true);
            SceneView.RepaintAll();
        }

        private void MoveUVSplineControl(TextureSet set, int point, bool incoming, Vector2 uv)
        {
            if (spline == null || spline.worldSpace || (uint)point >= (uint)spline.PointCount) return;
            spline.SetWorldControl(point, incoming, new Vector3(uv.x, uv.y, 0f), uv);
            NormalizeTwoDimensionalSplinePoint(set, spline, point);
            CompleteLightweightPathEdit(set, true);
            SceneView.RepaintAll();
        }

        private void MoveUVSplineWidth(TextureSet set, int point, Rect textureRect, Vector2 mouse)
        {
            if (spline == null || spline.worldSpace || (uint)point >= (uint)spline.PointCount ||
                !TryGetUVSplineWidthHandle(spline, textureRect, point, out Vector2 center,
                    out Vector2 normal, out float displayScale, out _)) return;
            float width = Vector2.Dot(mouse - center, normal) / Mathf.Max(1f, displayScale);
            spline.widths[point] = Mathf.Clamp(width, 0.05f, 4f);
            CompleteLightweightPathEdit(set, true);
            SceneView.RepaintAll();
        }

        private static bool TryCanvasUV(Vector2 point, Rect textureRect, out Vector2 uv)
        {
            uv = CanvasPointToUV(point, textureRect);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        private void DrawRightRegion(Vector2 size)
        {
            TextureSet set = ActiveTextureSet;
            float headerHeight = 46f;
            float available = Mathf.Max(0f, size.y - headerHeight);
            float stackHeight = workspaceShowLayers && workspaceShowProperties ? Mathf.Max(155f, available * 0.48f) : available;

            GUILayout.BeginArea(new Rect(0f, 0f, size.x, headerHeight));
            TexturePaintLogicalTarget logicalTarget = ActiveLogicalTarget;
            string stackTitle = logicalTarget != null ? "LAYER / PATH · " + logicalTarget.displayName : "LAYER / PATH";
            string stackSubtitle = logicalTarget?.isUdim == true
                ? $"One logical stack backed by {logicalTarget.members.Count} UDIM tiles."
                : "One logical stack backed by the selected slot.";
            DrawRegionHeader(stackTitle, stackSubtitle);
            workspaceRightTab = GUILayout.Toolbar(Mathf.Clamp(workspaceRightTab, 0, 1), new[] { "Layers", "Paths" });
            GUILayout.EndArea();

            float y = headerHeight;
            if (workspaceShowLayers)
            {
                Rect stack = new Rect(0f, y, size.x, stackHeight);
                GUILayout.BeginArea(stack);
                DrawLayerStack(set, workspaceRightTab == 1);
                GUILayout.EndArea();
                y += stackHeight;
            }
            if (workspaceShowProperties)
            {
                if (workspaceShowLayers)
                {
                    Rect separator = new Rect(0f, y, size.x, 2f);
                    EditorGUI.DrawRect(separator, WorkspaceStyles.BorderColor);
                    y += 2f;
                }
                Rect properties = new Rect(0f, y, size.x, Mathf.Max(0f, size.y - y));
                GUILayout.BeginArea(properties);
                DrawPropertiesRegion(set);
                GUILayout.EndArea();
            }
        }

        private void DrawLayerStack(TextureSet set, bool pathsOnly)
        {
            if (set == null) return;
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawSplitLayerButton(new GUIContent("+ Paint", "Add an empty Paint layer"),
                () => AddPaintLayer(set), () => ShowAddPaintLayerMenu(set), 72f);
            DrawSplitLayerButton(new GUIContent("+ Fill", "Add a Fill layer using the current source"),
                () => AddFillLayer(set), () => ShowAddFillLayerMenu(set), 66f);
            DrawSplitLayerButton(new GUIContent("+ Path", "Add an empty spline/path layer"),
                () => CreateSplineLayerWithUndo(set), () => ShowAddPathLayerMenu(set), 72f);
            if (!pathsOnly && GUILayout.Button(new GUIContent("+ Plugin",
                    "Add a procedural generator/filter layer"), EditorStyles.toolbarButton))
                AddPluginLayer(set);
            if (!pathsOnly && GUILayout.Button(new GUIContent("+ Group", "Add layer folder/group"), EditorStyles.toolbarButton))
            {
                BeginLayerCreationUndo("Add Layer Group");
                TexturePaintLayer created = set.AddGroup("Group " + (set.layers.Count + 1));
                CompleteLayerCreationUndo(created);
                SyncActiveLayerSelection(set);
            }
            GUILayout.EndHorizontal();

            DrawLayerStackDiagnostic(set, pathsOnly);

            workspaceLayerScroll = GUILayout.BeginScrollView(workspaceLayerScroll);
            int deleteIndex = -1;
            for (int i = set.layers.Count - 1; i >= 0; i--)
            {
                TexturePaintLayer layer = set.layers[i];
                if (pathsOnly && !layer.IsSplineLayer) continue;
                if (!pathsOnly && IsLayerHiddenByCollapsedGroup(set, layer)) continue;
                Rect row = GUILayoutUtility.GetRect(10f, 46f, GUILayout.ExpandWidth(true));
                DrawLayerRow(set, layer, i, row, ref deleteIndex);
            }
            GUILayout.EndScrollView();
            if (deleteIndex >= 0) DeleteLayer(set, deleteIndex, true);

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool hasLayer = (uint)set.activeLayerIndex < (uint)set.layers.Count;
            using (new EditorGUI.DisabledScope(!hasLayer))
            {
                if (GUILayout.Button(new GUIContent("Duplicate", "Duplicate active layer (Ctrl/Cmd+D)"), EditorStyles.toolbarButton)) DuplicateActiveLayer(set);
                using (new EditorGUI.DisabledScope(set.activeLayerIndex <= 0))
                    if (GUILayout.Button(new GUIContent("Merge", "Merge active layer down"), EditorStyles.toolbarButton)) MergeActiveLayer(set);
                if (GUILayout.Button(new GUIContent("Delete", "Delete active layer (Delete)"), EditorStyles.toolbarButton)) DeleteLayer(set, set.activeLayerIndex, true);
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawSplitLayerButton(GUIContent content, Action primaryAction,
            Action menuAction, float width)
        {
            const float arrowWidth = 18f;
            Rect combined = GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight,
                GUILayout.Width(width));
            Rect primary = new Rect(combined.x, combined.y,
                Mathf.Max(0f, combined.width - arrowWidth), combined.height);
            Rect arrow = new Rect(primary.xMax, combined.y, arrowWidth, combined.height);
            if (GUI.Button(primary, content, EditorStyles.toolbarButton)) primaryAction?.Invoke();
            if (GUI.Button(arrow, new GUIContent("▼", "Choose how to create this layer"),
                    EditorStyles.toolbarButton))
                menuAction?.Invoke();
        }

        private void ShowAddPaintLayerMenu(TextureSet set)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Empty Paint Layer"), false, () => AddPaintLayer(set));
            menu.AddItem(new GUIContent("Add Paint Layer from Sprite Set..."), false,
                () => ShowAddPaintLayerFromSpriteSet(set));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Apply Material Preset..."), false,
                () => OpenMaterialPresetPicker(set));
            menu.ShowAsContext();
        }

        private void ShowAddFillLayerMenu(TextureSet set)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Empty Fill Layer"), false, () => AddFillLayer(set));
            menu.AddItem(new GUIContent("Add from Overlay..."), false,
                () => OverlayPainterOverlayPickerWindow.Show(overlay => AddOverlayFillLayer(set, overlay)));
            menu.AddItem(new GUIContent("Add from Sprite Set..."), false,
                () => ShowAddFillLayerFromSpriteSet(set));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Apply Material Preset..."), false,
                () => OpenMaterialPresetPicker(set));
            menu.ShowAsContext();
        }

        private void ShowAddPathLayerMenu(TextureSet set)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Empty Path Layer"), false,
                () => CreateSplineLayerWithUndo(set));
            menu.AddItem(new GUIContent("Add from OverlayDataAsset..."), false,
                () => OverlayPainterOverlayPickerWindow.Show(overlay => AddOverlayPathLayer(set, overlay)));
            menu.AddItem(new GUIContent("Add from Sprite Set..."), false,
                () => ShowAddPathLayerFromSpriteSet(set));
            menu.ShowAsContext();
        }

        private void DrawLayerRow(TextureSet set, TexturePaintLayer layer, int index, Rect row, ref int deleteIndex)
        {
            bool selected = set.activeLayerIndex == index;
            if (Event.current.type == EventType.Repaint)
                (selected ? WorkspaceStyles.SelectedRow : WorkspaceStyles.Row).Draw(row, false, false, selected, false);
            Rect drag = new Rect(row.x + 2f, row.y + 4f, 13f, row.height - 8f);
            GUI.Label(drag, "≡", WorkspaceStyles.DragHandle);
            Rect eye = new Rect(drag.xMax, row.y + 11f, 34f, 24f);
            bool visible = GUI.Toggle(eye, layer.visible, new GUIContent(layer.visible ? "ON" : "OFF",
                layer.visible ? "Layer is visible; click to hide it" : "Layer is hidden; click to show it"),
                EditorStyles.miniButton);
            if (visible != layer.visible)
            {
                ChangeLayerVisibility(set, layer, visible);
            }
            int hierarchyDepth = LayerHierarchyDepth(set, layer);
            float hierarchyIndent = Mathf.Min(hierarchyDepth, 4) * 14f;
            Rect thumb = new Rect(eye.xMax + 4f + hierarchyIndent, row.y + 5f, 36f, 36f);
            bool folderDropHover = false;
            if (layer.kind == TexturePaintLayerKind.Group && thumb.Contains(Event.current.mousePosition) &&
                DragAndDrop.GetGenericData(LayerDragKey) is int folderHoverIndex &&
                folderHoverIndex != index && (uint)folderHoverIndex < (uint)set.layers.Count)
                folderDropHover = set.layers[folderHoverIndex]?.kind != TexturePaintLayerKind.Group;
            bool groupExpanded = layer.kind != TexturePaintLayerKind.Group || IsGroupExpanded(layer);
            if (layer.kind == TexturePaintLayerKind.Group)
                DrawLayerFolderIcon(thumb, groupExpanded, folderDropHover);
            else
            {
                Texture thumbnail = ResolveLayerThumbnail(layer, selectedChannel);
                Color thumbnailFallback = layer.kind == TexturePaintLayerKind.Fill &&
                    layer.fillSettings?.source == TexturePaintBrushSource.Color
                        ? layer.fillSettings.color
                        : Color.clear;
                DrawTextureThumbnail(thumb, thumbnail,
                    thumbnailFallback);
            }
            Rect maskThumb = layer.layerMask?.target?.Front != null
                ? new Rect(thumb.xMax + 3f, row.y + 8f, 30f, 30f) : default;
            if (maskThumb.width > 0f)
            {
                DrawTextureThumbnail(maskThumb, set.GetLayerMaskPreview(layer), Color.white);
                if (layerMaskMode && selected)
                    Handles.DrawSolidRectangleWithOutline(maskThumb, Color.clear,
                        new Color(0.25f, 0.75f, 1f, 1f));
                GUI.Label(maskThumb, new GUIContent(string.Empty,
                    "Layer mask. Click to enter Mask Mode and paint this grayscale texture."));
            }
            Rect delete = new Rect(row.xMax - 27f, row.y + 11f, 23f, 23f);
            Rect menu = new Rect(delete.x - 27f, row.y + 11f, 23f, 23f);
            Rect effectsButton = new Rect(menu.x - 29f, row.y + 11f, 25f, 23f);
            bool showExtendedControls = ShouldShowLayerRowExtendedControls(row.width);
            Rect extendedControls = default;
            float textRight = effectsButton.x - 6f;
            if (showExtendedControls)
            {
                float controlsWidth = Mathf.Clamp(row.width * 0.38f, 255f, 330f);
                extendedControls = new Rect(effectsButton.x - controlsWidth - 6f, row.y + 2f,
                    controlsWidth, row.height - 4f);
                textRight = extendedControls.x - 7f;
            }

            float thumbnailRight = maskThumb.width > 0f ? maskThumb.xMax : thumb.xMax;
            Rect text = new Rect(thumbnailRight + 7f, row.y + 4f,
                Mathf.Max(0f, textRight - thumbnailRight - 7f), 21f);
            if (workspaceRenameLayerId == layer.id)
            {
                GUI.SetNextControlName("TexturePaintLayerRename");
                workspaceRenameBuffer = GUI.TextField(text, workspaceRenameBuffer ?? layer.name);
                if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    CommitLayerRename(layer); Event.current.Use();
                }
            }
            else GUI.Label(text, LayerDisplayName(layer), selected ? EditorStyles.boldLabel : EditorStyles.label);
            GUI.Label(new Rect(text.x, row.y + 25f, text.width, 16f), LayerSubtitle(set, layer),
                EditorStyles.miniLabel);

            if (showExtendedControls)
            {
                const float labelWidth = 51f;
                Rect opacityLabel = new Rect(extendedControls.x, row.y + 3f, labelWidth, 18f);
                Rect opacityField = new Rect(opacityLabel.xMax, opacityLabel.y,
                    extendedControls.width - labelWidth, 18f);
                Rect blendLabel = new Rect(extendedControls.x, row.y + 24f, labelWidth, 18f);
                Rect blendField = new Rect(blendLabel.xMax, blendLabel.y,
                    extendedControls.width - labelWidth, 18f);
                GUI.Label(opacityLabel, "Opacity", EditorStyles.miniLabel);
                GUI.Label(blendLabel, "Blend", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float opacity = EditorGUI.Slider(opacityField, layer.opacity, 0f, 1f);
                TexturePaintBlendMode blend = (TexturePaintBlendMode)EditorGUI.EnumPopup(
                    blendField, layer.blendMode);
                if (EditorGUI.EndChangeCheck())
                    ChangeLayerMetadata(set, layer, layer.name, opacity, blend);
            }
            if (GUI.Button(menu, "⋮", EditorStyles.miniButton)) ShowLayerMenu(set, layer, index);

            layer.effects ??= new TexturePaintLayerEffects();
            layer.effects.Normalize();
            bool hasEffects = layer.effects.HasEnabled || layer.layerMask?.effects?.HasEnabled == true;
            Color previousBackground = GUI.backgroundColor;
            if (hasEffects) GUI.backgroundColor = new Color(0.38f, 0.72f, 1f);
            using (new EditorGUI.DisabledScope(layer.kind == TexturePaintLayerKind.Group && layer.layerMask == null))
                if (GUI.Button(effectsButton, new GUIContent("fx", hasEffects
                        ? "Edit enabled layer effects" : "Add layer effects"), EditorStyles.miniButton))
                    ShowLayerEffectsPopup(effectsButton, set, layer);
            GUI.backgroundColor = previousBackground;

            if (GUI.Button(delete, new GUIContent("X", "Delete layer"), EditorStyles.miniButton))
                deleteIndex = index;

            Rect click = new Rect(eye.xMax, row.y, row.width - (eye.xMax - row.x), row.height);
            bool pointerOverControl = menu.Contains(Event.current.mousePosition) ||
                delete.Contains(Event.current.mousePosition) ||
                effectsButton.Contains(Event.current.mousePosition) ||
                (maskThumb.width > 0f && maskThumb.Contains(Event.current.mousePosition)) ||
                (showExtendedControls && extendedControls.Contains(Event.current.mousePosition));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                maskThumb.width > 0f && maskThumb.Contains(Event.current.mousePosition))
            {
                set.activeLayerIndex = index;
                SyncActiveLayerSelection(set);
                EnterLayerMaskMode(set, layer);
                GUI.FocusControl(null);
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                layer.kind == TexturePaintLayerKind.Group && thumb.Contains(Event.current.mousePosition))
            {
                ExitLayerMaskMode();
                set.activeLayerIndex = index;
                SetGroupExpanded(layer, !groupExpanded);
                SyncActiveLayerSelection(set);
                GUI.FocusControl(null);
                GUI.changed = true;
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                click.Contains(Event.current.mousePosition) && !pointerOverControl)
            {
                ExitLayerMaskMode();
                set.activeLayerIndex = index; SyncActiveLayerSelection(set); GUI.FocusControl(null); Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && drag.Contains(Event.current.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(LayerDragKey, index);
                DragAndDrop.StartDrag(layer.name);
                Event.current.Use();
            }
            if ((Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform) &&
                layer.kind == TexturePaintLayerKind.Group && thumb.Contains(Event.current.mousePosition) &&
                DragAndDrop.GetGenericData(LayerDragKey) is int folderFrom && folderFrom != index)
            {
                TexturePaintLayer draggedLayer = (uint)folderFrom < (uint)set.layers.Count
                    ? set.layers[folderFrom]
                    : null;
                bool canGroup = draggedLayer != null && !ReferenceEquals(draggedLayer, layer) &&
                    (draggedLayer.kind != TexturePaintLayerKind.Group ||
                     !IsDescendantOfGroup(set, layer, new HashSet<string> { draggedLayer.id }));
                DragAndDrop.visualMode = canGroup ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                if (canGroup && Event.current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (MoveLayerIntoGroupWithHistory(set, draggedLayer, layer))
                    {
                        SetGroupExpanded(layer, true);
                        SyncActiveLayerSelection(set);
                        ShowWorkspaceStatus($"Moved '{draggedLayer.name}' into '{layer.name}'");
                    }
                    DragAndDrop.SetGenericData(LayerDragKey, null);
                }
                Event.current.Use();
            }
            if ((Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform) && row.Contains(Event.current.mousePosition) &&
                DragAndDrop.GetGenericData(LayerDragKey) is int from && from != index)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                if (Event.current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (MoveLayerWithHistory(set, from, index)) SyncActiveLayerSelection(set);
                    DragAndDrop.SetGenericData(LayerDragKey, null);
                    MarkDocumentDirty();
                }
                Event.current.Use();
            }
            if (Event.current.type == EventType.ContextClick && row.Contains(Event.current.mousePosition) &&
                !pointerOverControl)
            { ShowLayerMenu(set, layer, index); Event.current.Use(); }
        }

        internal static bool ShouldShowLayerRowExtendedControls(float rowWidth)
        {
            return rowWidth >= LayerRowExtendedControlsMinimumWidth;
        }

        internal static Texture ResolveLayerThumbnail(TexturePaintLayer layer,
            TexturePaintChannel selectedChannel)
        {
            if (layer == null) return null;
            TexturePaintChannel thumbnailChannel = PreferredLayerThumbnailChannel(layer);
            if (layer.channels.TryGetValue(thumbnailChannel, out EditableTextureTarget target) &&
                target != null)
                return target.Front;

            // The layer may have authored pixels from an earlier channel even when its current
            // paint settings point somewhere else. A row thumbnail represents the layer itself,
            // so fall back to a stable authored channel rather than the globally selected one.
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (layer.channels.TryGetValue(channel, out target) && target != null)
                {
                    thumbnailChannel = channel;
                    return target.Front;
                }

            // A restored fill can briefly be drawn before its generated target is reconstructed.
            // Show its actual direct source during that frame instead of the legacy white fill
            // fallback. OverlayData sources are resolved by reconstruction and have no single
            // source texture that can be selected reliably here.
            TexturePaintFillSettings settings = layer.kind == TexturePaintLayerKind.Fill
                ? layer.fillSettings
                : null;
            if (settings?.source == TexturePaintBrushSource.Texture)
                return TexturePaintSpriteSource.Resolve(settings.sourceTexture, settings.sourceSprite,
                    thumbnailChannel, settings.normalConvention, settings.invert);
            return null;
        }

        private static TexturePaintChannel PreferredLayerThumbnailChannel(TexturePaintLayer layer)
        {
            if (layer.kind == TexturePaintLayerKind.Fill) return layer.fillChannel;
            if (layer.IsSplineLayer && layer.splineSettings != null) return layer.splineSettings.channel;
            if (layer.paintSettings != null) return layer.paintSettings.channel;
            return TexturePaintChannel.Albedo;
        }

        private static void DrawLayerFolderIcon(Rect rect, bool expanded, bool dropHover)
        {
            Color previous = GUI.backgroundColor;
            if (dropHover) GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.backgroundColor = previous;
            GUIContent icon = EditorGUIUtility.IconContent(expanded ? "FolderOpened Icon" : "Folder Icon");
            if (icon?.image == null) icon = EditorGUIUtility.IconContent("Folder Icon");
            if (icon?.image != null)
                GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f),
                    icon.image, ScaleMode.ScaleToFit, true);
            GUI.Label(rect, new GUIContent(string.Empty,
                (expanded ? "Collapse" : "Expand") +
                " layer group. Drop a Paint, Fill, or Path layer on this folder to add it."));
        }

        private bool IsGroupExpanded(TexturePaintLayer group)
        {
            if (group == null || group.kind != TexturePaintLayerKind.Group) return true;
            workspaceCollapsedLayerGroupIds ??= new List<string>();
            return !workspaceCollapsedLayerGroupIds.Contains(LayerGroupStateKey(group));
        }

        private void SetGroupExpanded(TexturePaintLayer group, bool expanded)
        {
            if (group == null || group.kind != TexturePaintLayerKind.Group) return;
            workspaceCollapsedLayerGroupIds ??= new List<string>();
            string key = LayerGroupStateKey(group);
            bool changed = expanded
                ? workspaceCollapsedLayerGroupIds.Remove(key)
                : !workspaceCollapsedLayerGroupIds.Contains(key);
            if (!expanded && changed) workspaceCollapsedLayerGroupIds.Add(key);
            if (changed) TexturePaintDockWindow.RepaintOpenWindows();
        }

        private static string LayerGroupStateKey(TexturePaintLayer group)
        {
            return !string.IsNullOrEmpty(group?.logicalLayerId) ? group.logicalLayerId : group?.id;
        }

        private bool IsLayerHiddenByCollapsedGroup(TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer == null) return false;
            string parentId = layer.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < set.layers.Count)
            {
                TexturePaintLayer parent = FindLayerById(set, parentId);
                if (parent == null) break;
                if (parent.kind == TexturePaintLayerKind.Group && !IsGroupExpanded(parent)) return true;
                parentId = parent.parentId;
            }
            return false;
        }

        private static int LayerHierarchyDepth(TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer == null) return 0;
            int depth = 0;
            string parentId = layer.parentId;
            while (!string.IsNullOrEmpty(parentId) && depth < set.layers.Count)
            {
                TexturePaintLayer parent = FindLayerById(set, parentId);
                if (parent == null) break;
                depth++;
                parentId = parent.parentId;
            }
            return depth;
        }

        private static TexturePaintLayer FindLayerById(TextureSet set, string layerId)
        {
            if (set == null || string.IsNullOrEmpty(layerId)) return null;
            for (int i = 0; i < set.layers.Count; i++)
                if (string.Equals(set.layers[i]?.id, layerId, StringComparison.Ordinal))
                    return set.layers[i];
            return null;
        }

        private void ShowLayerEffectsPopup(Rect anchor, TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer == null ||
                layer.kind == TexturePaintLayerKind.Group && layer.layerMask == null) return;
            int index = set.layers.IndexOf(layer);
            if (index >= 0)
            {
                set.activeLayerIndex = index;
                SyncActiveLayerSelection(set);
            }
            UnityEditor.PopupWindow.Show(anchor, new LayerEffectsPopup(this, set, layer, selectedChannel));
        }

        private sealed class LayerEffectsPopup : PopupWindowContent
        {
            private readonly TexturePaintStageWindow owner;
            private readonly TextureSet set;
            private readonly TexturePaintLayer layer;
            private readonly TexturePaintChannel defaultChannel;
            private readonly TexturePaintLayerEffects effects;
            private readonly TexturePaintLayerMaskEffects maskEffects;
            private readonly bool ribbonLayer;
            private Vector2 scroll;
            private TexturePaintLayerEffectKind addEffectKind = TexturePaintLayerEffectKind.Stroke;
            private int requestedRemove = -1;
            private int requestedMoveFrom = -1;
            private int requestedMoveTo = -1;

            public LayerEffectsPopup(TexturePaintStageWindow owner, TextureSet set,
                TexturePaintLayer layer, TexturePaintChannel defaultChannel)
            {
                this.owner = owner;
                this.set = set;
                this.layer = layer;
                this.defaultChannel = defaultChannel;
                effects = layer.effects?.Clone() ?? new TexturePaintLayerEffects();
                effects.Normalize();
                maskEffects = layer.layerMask?.effects?.Clone() ?? new TexturePaintLayerMaskEffects();
                maskEffects.Normalize();
                ribbonLayer = layer.IsSplineLayer &&
                    layer.splineSettings?.pathMode == TexturePaintPathMode.Ribbon;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(410f, 720f);
            }

            public override void OnGUI(Rect rect)
            {
                if (owner == null || set == null || layer == null || !set.layers.Contains(layer))
                {
                    EditorGUILayout.HelpBox("This layer is no longer available.", MessageType.Info);
                    return;
                }

                GUILayout.Label("Layer Effects · " + layer.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Effects are evaluated non-destructively during layer compositing.",
                    EditorStyles.wordWrappedMiniLabel);
                if (!set.LayerEffectsAvailable)
                    EditorGUILayout.HelpBox(
                        "Layer effects require compute shaders with RGFloat and RFloat render-texture support.",
                        MessageType.Warning);
                EditorGUILayout.Space(4f);
                EditorGUI.BeginChangeCheck();
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (layer.kind != TexturePaintLayerKind.Group)
                {
                    EditorGUILayout.LabelField("Ordered Effect Stack", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        "Effects are evaluated from top to bottom. Multiple instances are supported.",
                        EditorStyles.wordWrappedMiniLabel);
                    requestedRemove = requestedMoveFrom = requestedMoveTo = -1;
                    for (int i = 0; i < effects.Stack.Count; i++)
                    {
                        TexturePaintLayerEffectSettings effect = effects.Stack[i];
                        if (effect == null || !CanUseEffectKind(effect.kind)) continue;
                        DrawEffect(effect, EffectTitle(effect.kind), i);
                    }
                    if (requestedRemove >= 0) { effects.Stack.RemoveAt(requestedRemove); GUI.changed = true; }
                    else if (requestedMoveFrom >= 0 && requestedMoveTo >= 0)
                    { effects.Move(requestedMoveFrom, requestedMoveTo); GUI.changed = true; }

                    EditorGUILayout.BeginHorizontal();
                    addEffectKind = (TexturePaintLayerEffectKind)EditorGUILayout.EnumPopup(
                        "New Effect", addEffectKind);
                    using (new EditorGUI.DisabledScope(!CanUseEffectKind(addEffectKind)))
                        if (GUILayout.Button("Add", GUILayout.Width(62f)))
                        {
                            TexturePaintLayerEffectSettings added = effects.Add(addEffectKind);
                            added.enabled = true;
                            added.channel = FirstLayerChannel(defaultChannel);
                            GUI.changed = true;
                        }
                    EditorGUILayout.EndHorizontal();
                    if (!CanUseEffectKind(addEffectKind))
                        EditorGUILayout.HelpBox("Ribbon edge effects can only be added to Path layers.",
                            MessageType.Info);
                    if (layer.IsSplineLayer && layer.splineSettings?.pathMode != TexturePaintPathMode.Ribbon)
                        EditorGUILayout.HelpBox("Ribbon-local effects are evaluated when this path's Apply Mode is Ribbon.",
                            MessageType.Info);
                }
                if (layer.layerMask != null) DrawMaskEffects();
                EditorGUILayout.EndScrollView();
                if (!EditorGUI.EndChangeCheck()) return;

                effects.Normalize();
                maskEffects.Normalize();
                if (layer.kind != TexturePaintLayerKind.Group &&
                    JsonUtility.ToJson(layer.effects) != JsonUtility.ToJson(effects))
                    owner.ChangeLayerEffects(set, layer, effects);
                if (layer.layerMask != null &&
                    JsonUtility.ToJson(layer.layerMask.effects) != JsonUtility.ToJson(maskEffects))
                    owner.ChangeLayerMaskEffects(set, layer, maskEffects);
                SceneView.RepaintAll();
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintUVWindow.RepaintOpenWindows();
                editorWindow?.Repaint();
            }

            private void DrawMaskEffects()
            {
                EditorGUILayout.Space(6f);
                GUILayout.Label("Layer Mask Effects", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Evaluated in order: editable mask, noise, then texture overlay.",
                    EditorStyles.wordWrappedMiniLabel);

                TexturePaintLayerMaskNoiseSettings noise = maskEffects.noise;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                noise.enabled = EditorGUILayout.ToggleLeft("Layer Mask Noise", noise.enabled,
                    EditorStyles.boldLabel);
                if (noise.enabled)
                {
                    EditorGUI.indentLevel++;
                    noise.seed = EditorGUILayout.IntField("Seed", noise.seed);
                    noise.tiling = EditorGUILayout.Vector2Field("Tiling (X / Y)", noise.tiling);
                    noise.offset = EditorGUILayout.Vector2Field("Offset (X / Y)", noise.offset);
                    noise.octaves = EditorGUILayout.IntSlider("Detail", noise.octaves, 1, 8);
                    noise.balance = EditorGUILayout.Slider("Balance", noise.balance, 0f, 1f);
                    noise.contrast = EditorGUILayout.Slider("Contrast", noise.contrast, 0.01f, 8f);
                    noise.invert = EditorGUILayout.Toggle("Invert", noise.invert);
                    noise.combine = (TexturePaintBlendMode)EditorGUILayout.EnumPopup("Combine", noise.combine);
                    noise.opacity = EditorGUILayout.Slider("Opacity", noise.opacity, 0f, 1f);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                TexturePaintLayerMaskTextureOverlaySettings overlay = maskEffects.textureOverlay;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                overlay.enabled = EditorGUILayout.ToggleLeft("Layer Mask Texture Overlay", overlay.enabled,
                    EditorStyles.boldLabel);
                if (overlay.enabled)
                {
                    EditorGUI.indentLevel++;
                    overlay.texture = (Texture2D)EditorGUILayout.ObjectField("Texture", overlay.texture,
                        typeof(Texture2D), false);
                    overlay.sourceChannel = (TexturePaintLayerMaskTextureChannel)EditorGUILayout.EnumPopup(
                        "Grayscale Source", overlay.sourceChannel);
                    overlay.tiling = EditorGUILayout.Vector2Field("Tiling (X / Y)", overlay.tiling);
                    overlay.offset = EditorGUILayout.Vector2Field("Offset (X / Y)", overlay.offset);
                    overlay.rotation = EditorGUILayout.FloatField("Rotation", overlay.rotation);
                    overlay.invert = EditorGUILayout.Toggle("Invert", overlay.invert);
                    overlay.combine = (TexturePaintBlendMode)EditorGUILayout.EnumPopup("Combine", overlay.combine);
                    overlay.opacity = EditorGUILayout.Slider("Opacity", overlay.opacity, 0f, 1f);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }

            private bool CanUseEffectKind(TexturePaintLayerEffectKind kind)
            {
                return kind != TexturePaintLayerEffectKind.EdgeFade &&
                    kind != TexturePaintLayerEffectKind.BevelEdge &&
                    kind != TexturePaintLayerEffectKind.ProceduralStitch || layer.IsSplineLayer;
            }

            private static string EffectTitle(TexturePaintLayerEffectKind kind)
            {
                return kind switch
                {
                    TexturePaintLayerEffectKind.InnerShadow => "Inner Shadow",
                    TexturePaintLayerEffectKind.OuterShadow => "Outer Shadow",
                    TexturePaintLayerEffectKind.InnerGlow => "Inner Glow",
                    TexturePaintLayerEffectKind.OuterGlow => "Outer Glow",
                    TexturePaintLayerEffectKind.ColorOverlay => "Color Overlay",
                    TexturePaintLayerEffectKind.EdgeFade => "Edge Fade",
                    TexturePaintLayerEffectKind.BevelEdge => "Bevel Edge",
                    TexturePaintLayerEffectKind.ProceduralStitch => "Procedural Stitch",
                    TexturePaintLayerEffectKind.TextureOverlay => "Texture Overlay",
                    TexturePaintLayerEffectKind.ImageAdjustments => "Image Adjustments",
                    _ => kind.ToString()
                };
            }

            private void DrawEffect(TexturePaintLayerEffectSettings effect, string title, int stackIndex)
            {
                if (effect == null) return;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool wasEnabled = effect.enabled;
                effect.enabled = EditorGUILayout.ToggleLeft(title, effect.enabled, EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(stackIndex <= 0))
                    if (GUILayout.Button("▲", GUILayout.Width(25f)))
                    { requestedMoveFrom = stackIndex; requestedMoveTo = stackIndex - 1; }
                using (new EditorGUI.DisabledScope(stackIndex >= effects.Stack.Count - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(25f)))
                    { requestedMoveFrom = stackIndex; requestedMoveTo = stackIndex + 1; }
                if (GUILayout.Button("×", GUILayout.Width(25f))) requestedRemove = stackIndex;
                EditorGUILayout.EndHorizontal();
                if (!wasEnabled && effect.enabled) effect.channel = FirstLayerChannel(defaultChannel);
                if (effect.enabled)
                {
                    EditorGUI.indentLevel++;
                    if (effect.kind == TexturePaintLayerEffectKind.EdgeFade)
                    {
                        float startPercent = EditorGUILayout.Slider(
                            new GUIContent("Fade Begins (%)",
                                "Distance from the ribbon centerline as a percentage of its half-width. 0 starts at the center; 100 starts at the side edge."),
                            effect.edgeFadeStart * 100f, 0f, 100f);
                        float sizePercent = EditorGUILayout.Slider(
                            new GUIContent("Fade Size (%)",
                                "Percentage of the remaining center-to-edge distance used by the fade. 0 is an immediate cutout; 100 reaches transparency at the side edge."),
                            effect.edgeFadeSize * 100f, 0f, 100f);
                        effect.edgeFadeStart = startPercent * 0.01f;
                        effect.edgeFadeSize = sizePercent * 0.01f;
                        EditorGUILayout.LabelField(
                            "Uses the ribbon cross-section, so texture rotation, mesh UV orientation, seams, and UDIM tiles do not rotate the fade.",
                            EditorStyles.wordWrappedMiniLabel);
                    }
                    else
                    {
                        effect.channel = DrawEffectChannel(effect.channel);
                        if (effect.kind == TexturePaintLayerEffectKind.BevelEdge)
                        {
                            effect.ribbonSide = (TexturePaintRibbonSide)EditorGUILayout.EnumPopup(
                                "Ribbon Edge", effect.ribbonSide);
                            effect.color = EditorGUILayout.ColorField("Light Color", effect.color);
                            effect.secondaryColor = EditorGUILayout.ColorField("Dark Color", effect.secondaryColor);
                            effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                            effect.smoothness = EditorGUILayout.Slider("Smooth", effect.smoothness, 0f, 1f);
                            if (effect.ribbonSide != TexturePaintRibbonSide.Right)
                            {
                                effect.ribbonLeftTone = (TexturePaintRibbonBevelTone)EditorGUILayout.EnumPopup(
                                    "Left Tone", effect.ribbonLeftTone);
                                effect.ribbonLeftOffset = EditorGUILayout.Slider("Left Offset (px)",
                                    effect.ribbonLeftOffset, -128f, 128f);
                            }
                            if (effect.ribbonSide != TexturePaintRibbonSide.Left)
                            {
                                effect.ribbonRightTone = (TexturePaintRibbonBevelTone)EditorGUILayout.EnumPopup(
                                    "Right Tone", effect.ribbonRightTone);
                                effect.ribbonRightOffset = EditorGUILayout.Slider("Right Offset (px)",
                                    effect.ribbonRightOffset, -128f, 128f);
                            }
                        }
                        else if (effect.kind == TexturePaintLayerEffectKind.ProceduralStitch)
                        {
                            effect.ribbonSide = (TexturePaintRibbonSide)EditorGUILayout.EnumPopup(
                                "Ribbon Edge", effect.ribbonSide);
                            effect.color = EditorGUILayout.ColorField("Thread Color", effect.color);
                            effect.stitchRows = (TexturePaintRibbonStitchRows)EditorGUILayout.EnumPopup(
                                "Rows Per Side", effect.stitchRows);
                            effect.stitchThreadSize = EditorGUILayout.Slider(
                                new GUIContent("Thread Size (%)", "Thread width as a percentage of ribbon width."),
                                effect.stitchThreadSize * 100f, 0.1f, 25f) * 0.01f;
                            effect.stitchLength = EditorGUILayout.Slider(
                                new GUIContent("Stitch Length (%)", "One stitch length as a percentage of a complete source tile. Gaps use the same length."),
                                effect.stitchLength * 100f, 1f, 100f) * 0.01f;
                            effect.stitchInset = EditorGUILayout.Slider(
                                new GUIContent("Edge Inset (%)", "Distance of the first stitch row from its ribbon edge."),
                                effect.stitchInset * 100f, 0f, 45f) * 0.01f;
                        }
                        else if (effect.kind == TexturePaintLayerEffectKind.TextureOverlay)
                        {
                            DrawTextureOverlaySource(effect, 1);
                            DrawTextureOverlaySource(effect, 2);
                            EditorGUILayout.LabelField(
                                "Textures repeat in destination UV space and are combined in Texture 1 then Texture 2 order. Their alpha, opacity, and color alpha all affect coverage.",
                                EditorStyles.wordWrappedMiniLabel);
                        }
                        else if (effect.kind == TexturePaintLayerEffectKind.ImageAdjustments)
                        {
                            bool grayscale = TexturePaintChannelUtility.IsGrayscale(effect.channel);
                            using (new EditorGUI.DisabledScope(grayscale))
                            {
                                effect.saturation = EditorGUILayout.Slider(
                                    new GUIContent("Saturation (%)", "Color saturation; 100% leaves the channel unchanged."),
                                    effect.saturation * 100f, 0f, 200f) * 0.01f;
                                effect.hue = EditorGUILayout.Slider(
                                    new GUIContent("Hue (degrees)", "Rotates hue around the color wheel."),
                                    effect.hue, -180f, 180f);
                            }
                            effect.brightness = EditorGUILayout.Slider(
                                new GUIContent("Brightness (%)", "Adds or removes brightness from the selected channel."),
                                effect.brightness * 100f, -100f, 100f) * 0.01f;
                            effect.contrast = EditorGUILayout.Slider(
                                new GUIContent("Contrast (%)", "Adjusts contrast around the channel midpoint."),
                                effect.contrast * 100f, -100f, 100f) * 0.01f;
                            if (grayscale)
                                EditorGUILayout.LabelField(
                                    "Hue and Saturation do not affect grayscale channels.",
                                    EditorStyles.wordWrappedMiniLabel);
                        }
                        else effect.color = EditorGUILayout.ColorField("Color", effect.color);
                        switch (effect.kind)
                        {
                            case TexturePaintLayerEffectKind.Stroke:
                                effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                                effect.smoothness = EditorGUILayout.Slider("Smooth", effect.smoothness, 0f, 1f);
                                break;
                            case TexturePaintLayerEffectKind.InnerShadow:
                            case TexturePaintLayerEffectKind.OuterShadow:
                                effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                                if (ribbonLayer)
                                {
                                    effect.ribbonSide = (TexturePaintRibbonSide)EditorGUILayout.EnumPopup(
                                        "Ribbon Edge", effect.ribbonSide);
                                    effect.offset.x = EditorGUILayout.Slider("Edge Offset (px)",
                                        effect.offset.x, -128f, 128f);
                                }
                                else
                                {
                                    effect.offset = EditorGUILayout.Vector2Field("Offset (px)", effect.offset);
                                    effect.offset.x = Mathf.Clamp(effect.offset.x, -256f, 256f);
                                    effect.offset.y = Mathf.Clamp(effect.offset.y, -256f, 256f);
                                }
                                effect.curve = EditorGUILayout.CurveField("Curve", effect.curve,
                                    Color.white, new Rect(0f, 0f, 1f, 1f), GUILayout.Height(34f));
                                break;
                            case TexturePaintLayerEffectKind.InnerGlow:
                            case TexturePaintLayerEffectKind.OuterGlow:
                                effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                                if (ribbonLayer)
                                    effect.ribbonSide = (TexturePaintRibbonSide)EditorGUILayout.EnumPopup(
                                        "Ribbon Edge", effect.ribbonSide);
                                effect.curve = EditorGUILayout.CurveField("Curve", effect.curve,
                                    Color.white, new Rect(0f, 0f, 1f, 1f), GUILayout.Height(34f));
                                break;
                            case TexturePaintLayerEffectKind.ColorOverlay:
                                effect.blendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                                    "Blend", effect.blendMode);
                                break;
                        }
                        string levelLabel = effect.kind == TexturePaintLayerEffectKind.ImageAdjustments
                            ? "Amount" : "Level";
                        effect.level = EditorGUILayout.Slider(
                            new GUIContent(levelLabel, effect.kind == TexturePaintLayerEffectKind.ImageAdjustments
                                ? "Blends between the unadjusted and adjusted image."
                                : "Effect strength, independent of the selected color's alpha."),
                            effect.level, 0f, 1f);
                        effect.color = TexturePaintChannelUtility.ConstrainColor(effect.channel, effect.color);
                        effect.secondaryColor = TexturePaintChannelUtility.ConstrainColor(
                            effect.channel, effect.secondaryColor);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }

            private TexturePaintChannel DrawEffectChannel(TexturePaintChannel current)
            {
                var authored = new List<TexturePaintChannel>();
                foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                    if (layer.channels.ContainsKey(channel)) authored.Add(channel);
                if (authored.Count == 0)
                {
                    EditorGUILayout.HelpBox("Add a layer channel before enabling this effect.",
                        MessageType.Warning);
                    return current;
                }
                int selected = authored.IndexOf(current);
                if (selected < 0) selected = 0;
                string[] names = new string[authored.Count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = TexturePaintChannelUtility.DisplayName(authored[i]);
                selected = EditorGUILayout.Popup(new GUIContent("Channel",
                    "Only channels authored by this layer can receive its effects."), selected, names);
                return authored[Mathf.Clamp(selected, 0, authored.Count - 1)];
            }

            private TexturePaintChannel FirstLayerChannel(TexturePaintChannel preferred)
            {
                if (layer.channels.ContainsKey(preferred)) return preferred;
                foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                    if (layer.channels.ContainsKey(channel)) return channel;
                return preferred;
            }

            private static void DrawTextureOverlaySource(TexturePaintLayerEffectSettings effect, int index)
            {
                bool first = index == 1;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Texture " + index, EditorStyles.miniBoldLabel);
                if (first)
                {
                    effect.texture1 = (Texture2D)EditorGUILayout.ObjectField(
                        "Texture", effect.texture1, typeof(Texture2D), false);
                    effect.textureTiling1 = EditorGUILayout.Vector2Field(
                        "Tiling (X / Y)", effect.textureTiling1);
                    effect.textureOffset1 = EditorGUILayout.Vector2Field(
                        "Offset (X / Y)", effect.textureOffset1);
                    effect.textureRotation1 = EditorGUILayout.FloatField(
                        "Rotation", effect.textureRotation1);
                    effect.blendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                        "Combine", effect.blendMode);
                    effect.textureOpacity1 = EditorGUILayout.Slider(
                        "Opacity", effect.textureOpacity1, 0f, 1f);
                    effect.color = EditorGUILayout.ColorField("Color Multiplier", effect.color);
                }
                else
                {
                    effect.texture2 = (Texture2D)EditorGUILayout.ObjectField(
                        "Texture", effect.texture2, typeof(Texture2D), false);
                    effect.textureTiling2 = EditorGUILayout.Vector2Field(
                        "Tiling (X / Y)", effect.textureTiling2);
                    effect.textureOffset2 = EditorGUILayout.Vector2Field(
                        "Offset (X / Y)", effect.textureOffset2);
                    effect.textureRotation2 = EditorGUILayout.FloatField(
                        "Rotation", effect.textureRotation2);
                    effect.secondaryBlendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                        "Combine", effect.secondaryBlendMode);
                    effect.textureOpacity2 = EditorGUILayout.Slider(
                        "Opacity", effect.textureOpacity2, 0f, 1f);
                    effect.secondaryColor = EditorGUILayout.ColorField(
                        "Color Multiplier", effect.secondaryColor);
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawPropertiesRegion(TextureSet set)
        {
            DrawRegionHeader("PROPERTIES", "Source, Destination, Target, and Channels are kept explicit.");
            workspacePropertyScroll = GUILayout.BeginScrollView(workspacePropertyScroll);
            TexturePaintLayer activeLayer = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count
                ? set.layers[set.activeLayerIndex] : null;
            bool isPaint = activeLayer?.kind == TexturePaintLayerKind.Paint;
            bool isFill = activeLayer?.kind == TexturePaintLayerKind.Fill;
            bool isPath = activeLayer?.IsSplineLayer == true;
            bool isGroup = activeLayer?.kind == TexturePaintLayerKind.Group;
            bool isPlugin = activeLayer?.kind == TexturePaintLayerKind.Plugin;
            bool maskMode = IsLayerMaskMode(set);
            bool showPaintControls = maskMode || activeLayer == null || isPaint || isPath;
            if (maskMode)
                EditorGUILayout.HelpBox(
                    "LAYER MASK mode is active. Paint white to reveal the layer, black to hide it. Erase restores the mask's original black or white value.",
                    MessageType.Info);
            else if (isPath)
                EditorGUILayout.HelpBox(
                    activeLayer.spline?.worldSpace == true
                        ? "3D path active: edit it only in the Scene view. Shift+Click adds a point; Ctrl+Click inserts near the path."
                        : "2D path active: edit it only in the 2D view. Shift+Click adds a point; Ctrl+Click inserts near the path. Model geometry is not consulted.",
                    MessageType.Info);
            else if (isFill || isGroup || isPlugin)
                EditorGUILayout.HelpBox(
                    isPlugin
                        ? "Plugin layers generate cached, multi-channel content from the composite below them. Paint their mask to art-direct the result."
                        : "Freehand tools require an active Paint layer. Fill and Group layers cannot receive brush strokes.",
                    MessageType.Info);
            if (showPaintControls) DrawPropertySection("DESTINATION", () =>
            {
                if (maskMode)
                {
                    sourceMode = TexturePaintSourceMode.SourceOverlay;
                    using (new EditorGUI.DisabledScope(true))
                        GUILayout.Toolbar(1, new[] { "Material Channel", "Layer Mask" });
                    EditorGUILayout.HelpBox($"Strokes edit the grayscale mask owned by '{activeLayer.name}'.",
                        MessageType.None);
                }
                else if (isPaint || isPath)
                {
                    sourceMode = TexturePaintSourceMode.SourceOverlay;
                    using (new EditorGUI.DisabledScope(true))
                        GUILayout.Toolbar(1, new[] { "Base Texture", "Active Layer" });
                    EditorGUILayout.HelpBox($"Strokes are owned by '{activeLayer.name}' and disappear when that layer is deleted.",
                        MessageType.None);
                }
                else
                {
                    int destination = GUILayout.Toolbar(sourceMode == TexturePaintSourceMode.SourceTexture ? 0 : 1,
                        new[] { "Base Texture", "Active Layer" });
                    sourceMode = destination == 0 ? TexturePaintSourceMode.SourceTexture : TexturePaintSourceMode.SourceOverlay;
                    if (destination == 0)
                        EditorGUILayout.HelpBox("Base Texture writes are direct. Active Layer is recommended for non-destructive work.", MessageType.Warning);
                    else
                        EditorGUILayout.HelpBox("Create or select a destination layer.", MessageType.Warning);
                }
            });

            if (activeLayer != null)
                DrawPropertySection("ACTIVE LAYER", () => DrawActiveLayerProperties(set, activeLayer));
            if (showPaintControls)
            {
                // Direct base-texture painting still needs a channel selector when no layer exists.
                // Active layers own their selector and channel details in the section above.
                if (!maskMode && activeLayer == null)
                    DrawPropertySection("CHANNELS", () => DrawChannelProperties(set));
            }
            if (isPath && !maskMode)
                DrawPropertySection("PATH", () => DrawPathProperties(set));
            if (!isFill && !isGroup && !isPlugin) DrawPropertySection("EXTENSIONS", () =>
            {
                if (GUILayout.Button("Plugins…")) PluginManagerWindow.Open(controller);
            });
            DrawPropertySection("DOCUMENT", () =>
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save")) SaveWorkspace();
                if (GUILayout.Button("Export…")) OpenExportWindow();
                GUILayout.EndHorizontal();
                if (GUILayout.Button(new GUIContent("Clear All...",
                    "Restore every slot to its source textures and remove all layers, paths, masks, and paint history.")))
                    ClearAllTexturePaintData(true);
                if (DrawPropertySubsectionFoldout("properties.document.performance-memory",
                        "Performance & Memory"))
                    DrawPerformanceProperties();
            });
            GUILayout.EndScrollView();
        }

        private void DrawSourceProperties(TextureSet set)
        {
            TexturePaintLayer fillLayer = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count &&
                set.layers[set.activeLayerIndex].kind == TexturePaintLayerKind.Fill
                    ? set.layers[set.activeLayerIndex] : null;
            TexturePaintChannelSourceSettings selectedFillSource =
                fillLayer?.GetChannelSettings(selectedChannel, false)?.sourceSettings;
            TexturePaintFillSettings fillSettings = selectedFillSource != null
                ? FillSettingsFromChannelSource(selectedFillSource)
                : fillLayer?.fillSettings;
            EditorGUI.BeginChangeCheck();
            if (TexturePaintChannelUtility.IsAuxiliary(selectedChannel))
            {
                int sourceIndex = paintSource == TexturePaintBrushSource.Texture ? 0 : 1;
                sourceIndex = GUILayout.Toolbar(sourceIndex, new[] { "Texture", "Color" });
                paintSource = sourceIndex == 0 ? TexturePaintBrushSource.Texture :
                    TexturePaintBrushSource.Color;
            }
            else paintSource = (TexturePaintBrushSource)GUILayout.Toolbar((int)paintSource,
                new[] { "Texture", "Overlay", "Color" });
            bool sourceReady = true;
            switch (paintSource)
            {
                case TexturePaintBrushSource.Texture:
                    DrawTextureOrSpriteSourceFields();
                    sourceReady = paintSourceTexture != null;
                    if (!sourceReady)
                        EditorGUILayout.HelpBox(fillLayer != null
                            ? "Select a source texture or sprite to apply this source to the Fill layer."
                            : "Select a source texture or sprite before painting.", MessageType.Info);
                    break;
                case TexturePaintBrushSource.Overlay:
                    DrawOverlayDataSelector(controller.Textures.Sets);
                    sourceReady = paintSourceOverlay != null;
                    if (!sourceReady)
                        EditorGUILayout.HelpBox(fillLayer != null
                            ? "Select an OverlayData source to apply this source to the Fill layer."
                            : "Select an OverlayData source before painting.", MessageType.Info);
                    EditorGUILayout.HelpBox("Overlay textures route to logical channels through UMA material keywords.", MessageType.None);
                    break;
                default:
                    if (TexturePaintChannelUtility.IsGrayscale(selectedChannel))
                    {
                        float value = EditorGUILayout.Slider("Source Value",
                            TexturePaintChannelUtility.ScalarValue(paintColor), 0f, 1f);
                        paintColor = new Color(value, value, value, paintColor.a);
                    }
                    else paintColor = EditorGUILayout.ColorField("Source Color", paintColor);
                    break;
            }
            if (TexturePaintChannelUtility.IsAuxiliary(selectedChannel))
                EditorGUILayout.HelpBox("Normal Control is painter-owned and has no OverlayData material source. Use a texture, sprite, or grayscale value.", MessageType.None);
            Vector2 tiling = fillSettings?.tiling ?? Vector2.one;
            Vector2 offset = fillSettings?.offset ?? Vector2.zero;
            float rotation = fillSettings?.rotation ?? 0f;
            bool invert = fillSettings?.invert ?? false;
            if (fillLayer != null && paintSource != TexturePaintBrushSource.Color)
            {
                invert = EditorGUILayout.Toggle(new GUIContent("Invert",
                    "Use one minus each RGB source channel. Alpha coverage is preserved."),
                    invert);
                tiling = EditorGUILayout.Vector2Field(new GUIContent("Tiling X / Y",
                    "Independent horizontal and vertical repetition for the generated Fill texture"), tiling);
                tiling.x = Mathf.Clamp(tiling.x, 0.01f, 1000f);
                tiling.y = Mathf.Clamp(tiling.y, 0.01f, 1000f);
                offset = EditorGUILayout.Vector2Field("Offset X / Y", offset);
                rotation = EditorGUILayout.FloatField("Rotation", rotation);
            }
            bool changed = EditorGUI.EndChangeCheck();
            // Keep an incomplete source choice in the UI. Committing it immediately would fail
            // Fill generation and snap the toolbar back before the user can assign its asset.
            if (changed && fillLayer != null && sourceReady)
            {
                TexturePaintFillSettings updated = (fillSettings ?? new TexturePaintFillSettings()).Clone();
                updated.source = paintSource;
                updated.sourceTexture = paintSourceSprite == null ? paintSourceTexture : null;
                updated.sourceSprite = paintSourceSprite;
                updated.sourceOverlay = paintSourceOverlay;
                updated.color = paintColor;
                updated.normalConvention = normalConvention;
                updated.invert = invert;
                updated.tiling = tiling;
                updated.offset = offset;
                updated.rotation = rotation;
                updated.useFirstChannelTransform = fillLayer.fillSettings?.useFirstChannelTransform == true;
                TexturePaintChannel channel = fillLayer.channels.ContainsKey(selectedChannel)
                    ? selectedChannel : fillLayer.fillChannel;
                ChangeFillLayer(set, fillLayer, channel, updated);
            }
        }

        private void DrawTextureOrSpriteSourceFields()
        {
            Texture2D directTexture = paintSourceSprite == null ? paintSourceTexture : null;
            EditorGUI.BeginChangeCheck();
            directTexture = (Texture2D)EditorGUILayout.ObjectField("Source Texture", directTexture,
                typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                paintSourceSprite = null;
                paintSourceTexture = directTexture;
            }

            EditorGUI.BeginChangeCheck();
            Sprite sprite = (Sprite)EditorGUILayout.ObjectField("Source Sprite", paintSourceSprite,
                typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck()) SetPaintSourceSprite(sprite);
            if (paintSourceSprite != null)
                EditorGUILayout.HelpBox("The selected sprite-sheet region is cached as a temporary texture for painting.",
                    MessageType.None);
            if (selectedChannel == TexturePaintChannel.Normal &&
                (paintSourceSprite != null || directTexture != null))
                EditorGUILayout.HelpBox("Normal sources are converted to linear tangent-space vectors. " +
                    "Convention describes the source image; Overlay Painter converts DirectX sources " +
                    "to its internal OpenGL representation before blending.", MessageType.None);
        }

        private void SetPaintSourceSprite(Sprite sprite)
        {
            paintSourceSprite = sprite;
            paintSourceTexture = TexturePaintSpriteSource.Resolve(null, sprite, selectedChannel,
                normalConvention);
        }

        private void RestorePaintSource(Texture2D texture, Sprite sprite)
        {
            paintSourceSprite = sprite;
            paintSourceTexture = TexturePaintSpriteSource.Resolve(texture, sprite, selectedChannel,
                normalConvention);
        }

        private void RefreshPaintSourceForChannel()
        {
            if (paintSourceSprite != null)
                paintSourceTexture = TexturePaintSpriteSource.Resolve(null, paintSourceSprite,
                    selectedChannel, normalConvention);
        }

        private void SetSelectedChannelAndRefreshSource(TexturePaintChannel channel)
        {
            selectedChannel = channel;
            paintColor = TexturePaintChannelUtility.ConstrainColor(channel, paintColor);
            RefreshPaintSourceForChannel();
        }

        private void DrawChannelProperties(TextureSet set, TexturePaintLayer layer = null)
        {
            EditorGUI.BeginChangeCheck();
            TexturePaintChannel nextChannel = DrawAvailableChannelPopup(set,
                new GUIContent("Channel",
                    "The channel used by the brush, path controls, 2D canvas, and channel-solo preview."),
                selectedChannel, layer);
            bool channelChanged = nextChannel != selectedChannel;
            if (channelChanged)
                SetSelectedChannelAndRefreshSource(nextChannel);
            if (selectedChannel == TexturePaintChannel.Normal)
                normalConvention = (TexturePaintNormalConvention)EditorGUILayout.EnumPopup("Convention", normalConvention);
            if (EditorGUI.EndChangeCheck() && !channelChanged)
                RefreshPaintSourceForChannel();
            TextureChannelTarget target = set?.GetChannel(selectedChannel);
            if (target == null) EditorGUILayout.HelpBox("The active target has no matching logical channel.", MessageType.Warning);
            if (selectedChannel == TexturePaintChannel.NormalControl && set != null)
            {
                EditorGUILayout.HelpBox("Neutral gray (0.5) leaves the normal unchanged. Darker values recess the surface; lighter values raise it. The generated normal is used for 3D preview and export.", MessageType.None);
                EditorGUI.BeginChangeCheck();
                int radius = EditorGUILayout.IntSlider(new GUIContent("Sample Radius (px)",
                    "Shared neighbor distance used to calculate gradients for this target."),
                    set.normalControlRadius, 1, 16);
                bool invertHeight = EditorGUILayout.Toggle(new GUIContent("Invert Height",
                    "Shared raised/recessed interpretation for this target."), set.normalControlInvert);
                if (EditorGUI.EndChangeCheck())
                    ChangeNormalControlSettings(set, set.normalControlStrength, radius, invertHeight);
            }
            bool nextSolo = EditorGUILayout.Toggle(new GUIContent("Solo in 3D", "Preview this logical channel without material shading"), channelSolo);
            if (nextSolo != channelSolo) { channelSolo = nextSolo; if (channelSolo) previewBefore = false; }
            bool nextBefore = EditorGUILayout.Toggle(new GUIContent("Before in 3D",
                "Show the original source textures while preserving the character material and lighting"), previewBefore);
            if (nextBefore != previewBefore) { previewBefore = nextBefore; if (previewBefore) channelSolo = false; }
        }

        private static TexturePaintChannel DrawAvailableChannelPopup(TextureSet set, GUIContent label,
            TexturePaintChannel current, TexturePaintLayer layer = null)
        {
            List<TexturePaintChannel> channels = GetSelectableChannels(set, layer);
            if (channels.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup(label, 0, new[]
                    {
                        layer == null ? "No material channels" : "No created layer channels"
                    });
                return current;
            }
            if (channels.Count == 1)
            {
                EditorGUILayout.LabelField(label,
                    new GUIContent(TexturePaintChannelUtility.DisplayName(channels[0])));
                return channels[0];
            }
            int selected = channels.IndexOf(current);
            if (selected < 0) selected = 0;
            string[] names = new string[channels.Count];
            for (int i = 0; i < channels.Count; i++)
                names[i] = TexturePaintChannelUtility.DisplayName(channels[i]);
            selected = EditorGUILayout.Popup(label, selected, names);
            return channels[Mathf.Clamp(selected, 0, channels.Count - 1)];
        }

        internal static List<TexturePaintChannel> GetSelectableChannels(TextureSet set,
            TexturePaintLayer layer = null)
        {
            var channels = new List<TexturePaintChannel>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (set?.GetChannel(channel) == null) continue;
                if (layer != null && !layer.channels.ContainsKey(channel)) continue;
                channels.Add(channel);
            }
            return channels;
        }

        private void DrawBrushProperties()
        {
            BrushPreset selectedPreset = (BrushPreset)EditorGUILayout.ObjectField(
                "Preset", brush, typeof(BrushPreset), false);
            if (selectedPreset != brush) SelectBrushPreset(selectedPreset);
            BrushPreset active = ActiveBrush;
            EditorGUI.BeginChangeCheck();
            tool = (TexturePaintTool)EditorGUILayout.EnumPopup("Tool", tool);
            if (IsLayerMaskMode(ActiveTextureSet) && tool == TexturePaintTool.NormalTouchup)
                tool = TexturePaintTool.Paint;
            active.shape = (BrushPreset.Shape)EditorGUILayout.EnumPopup("Shape", active.shape);
            active.size = EditorGUILayout.Slider(new GUIContent("Brush Size",
                "World-space radius in the 3D view; normalized-UV radius in the 2D view."),
                active.size, 0.001f, 0.5f);
            active.hardness = EditorGUILayout.Slider("Hardness", active.hardness, 0f, 1f);
            active.flow = EditorGUILayout.Slider("Flow", active.flow, 0f, 1f);
            active.spacing = EditorGUILayout.Slider(new GUIContent("Stroke Spacing",
                "Center-to-center stamp spacing measured in brush diameters."), active.spacing, 0.01f, 10f);
            active.rotation = DrawBrushRotation(active.rotation);
            active.blendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                "Brush Blend", active.blendMode);
            active.mirrorStroke = EditorGUILayout.Toggle("Mirror Stroke", active.mirrorStroke);
            active.alignToStroke = EditorGUILayout.Toggle("Follow Stroke", active.alignToStroke);
            if (active.shape == BrushPreset.Shape.Stamp)
                BrushPresetInspectorUtility.DrawStampSource(active);
            if (!splineMode || IsLayerMaskMode(ActiveTextureSet))
            {
                BrushPresetInspectorUtility.DrawRandomization(active);
                BrushPresetInspectorUtility.DrawStrokeEvolution(active);
            }
            strength = EditorGUILayout.Slider("Strength", strength, 0f, 1f);
            limitStrokeCoverage = EditorGUILayout.Toggle(new GUIContent("Cap Per Stroke", "Accumulate coverage up to one complete replacement per stroke."), limitStrokeCoverage);
            mirrorX = EditorGUILayout.Toggle("Mirror Global X", mirrorX);
            EditorGUI.EndChangeCheck();
            DrawBrushAssetActions();
            if (tool == TexturePaintTool.Plugin && controller.Plugins.Brushes.Count > 0)
            {
                string[] names = new string[controller.Plugins.Brushes.Count];
                for (int i = 0; i < names.Length; i++) names[i] = controller.Plugins.Brushes[i].Descriptor.displayName;
                selectedBrushPlugin = EditorGUILayout.Popup("Brush Extension", Mathf.Clamp(selectedBrushPlugin, 0, names.Length - 1), names);
                ITexturePaintBrushV2 plugin = controller.Plugins.Brushes[Mathf.Clamp(selectedBrushPlugin, 0, names.Length - 1)];
                PluginManagerWindow.DrawParameters(plugin.Descriptor, controller.Plugins.GetParameters(plugin));
            }
            EditorGUILayout.HelpBox("Shift + right-drag: horizontal changes size, vertical changes hardness. [ and ] adjust size.", MessageType.None);
        }

        private void DrawActiveLayerProperties(TextureSet set, TexturePaintLayer layer)
        {
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("Name", layer.name);
            float opacity = EditorGUILayout.Slider("Opacity", layer.opacity, 0f, 1f);
            TexturePaintBlendMode blend = (TexturePaintBlendMode)EditorGUILayout.EnumPopup("Blend", layer.blendMode);
            if (EditorGUI.EndChangeCheck())
            {
                ChangeLayerMetadata(set, layer, name, opacity, blend);
            }
            if (layer.kind == TexturePaintLayerKind.Fill && !IsLayerMaskMode(set))
                DrawFillLayerProperties(set, layer);
            if (layer.kind == TexturePaintLayerKind.Plugin && !IsLayerMaskMode(set))
                DrawPluginLayerProperties(set, layer);
            if (IsLayerMaskMode(set) && layer.layerMask != null)
            {
                DrawLayerMaskSource(set, layer);
                DrawLayerMaskPluginProperties(set, layer);
            }
            EditorGUILayout.LabelField("Type", layer.kind.ToString());
            DrawMaterialPresetProvenance(layer);
            if (!string.IsNullOrEmpty(layer.pluginId) && layer.kind != TexturePaintLayerKind.Plugin)
                EditorGUILayout.LabelField("Extension", layer.pluginId + " " + layer.pluginVersion);
            if (layer.kind != TexturePaintLayerKind.Group && !IsLayerMaskMode(set))
                DrawLayerChannelProperties(set, layer, layer.kind != TexturePaintLayerKind.Plugin);
        }

        private void DrawPluginLayerProperties(TextureSet set, TexturePaintLayer layer)
        {
            IReadOnlyList<ITexturePaintCommandExtensionV2> available = controller?.Plugins?.Commands;
            available ??= Array.Empty<ITexturePaintCommandExtensionV2>();
            ITexturePaintCommandExtensionV2 selectedPlugin = controller?.Plugins?.FindCommand(layer.pluginId);
            bool missing = !string.IsNullOrEmpty(layer.pluginId) && selectedPlugin == null;
            var labels = new List<string>();
            var choices = new List<ITexturePaintCommandExtensionV2>();
            if (missing)
            {
                labels.Add("Missing · " + layer.pluginId);
                choices.Add(null);
            }
            labels.Add("None");
            choices.Add(null);
            int selectedIndex = missing ? 0 : 0;
            for (int i = 0; i < available.Count; i++)
            {
                ITexturePaintCommandExtensionV2 plugin = available[i];
                string kind = plugin is ITexturePaintGeneratorV2 ? "Generator" : "Filter";
                labels.Add(plugin.Descriptor.displayName + "  (" + kind + ")");
                choices.Add(plugin);
                if (ReferenceEquals(plugin, selectedPlugin)) selectedIndex = labels.Count - 1;
            }

            string layerKey = !string.IsNullOrEmpty(layer.logicalLayerId) ? layer.logicalLayerId : layer.id;
            bool running = pluginLayerCancellation != null;
            using (new EditorGUI.DisabledScope(running))
            {
                int next = EditorGUILayout.Popup("Plugin", selectedIndex, labels.ToArray());
                if (next != selectedIndex)
                {
                    ITexturePaintCommandExtensionV2 choice = choices[next];
                    ChangePluginLayerDefinition(set, layer, choice);
                    selectedPlugin = choice;
                    missing = false;
                }
            }

            if (missing)
                EditorGUILayout.HelpBox(
                    "The selected plugin is not installed or failed registration. The last cached output remains visible and can still be masked, blended, or exported.",
                    MessageType.Warning);
            if (selectedPlugin == null)
            {
                if (!missing)
                    EditorGUILayout.HelpBox("Choose a generator or filter. Filters read the composite below this layer; generators may also use that input.",
                        MessageType.Info);
                return;
            }

            TexturePaintPluginDescriptor descriptor = selectedPlugin.Descriptor;
            bool versionMismatch = !string.IsNullOrEmpty(layer.pluginVersion) &&
                !string.Equals(layer.pluginVersion, descriptor.pluginVersion, StringComparison.Ordinal);
            EditorGUILayout.LabelField(descriptor.description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Version", descriptor.pluginVersion);
            EditorGUILayout.LabelField("Reads", descriptor.ResolvedReadChannels.ToString());
            EditorGUILayout.LabelField("Writes", descriptor.declaredChannels.ToString());
            if (descriptor.ResolvedMeshMaps != TexturePaintMeshMapMask.None)
                EditorGUILayout.LabelField("Mesh Maps", descriptor.ResolvedMeshMaps.ToString());

            TexturePaintPluginParameterSet parameters =
                controller.Plugins.GetLayerParameters(layer, selectedPlugin);
            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUI.BeginChangeCheck();
                PluginManagerWindow.DrawParameters(descriptor, parameters);
                if (EditorGUI.EndChangeCheck())
                    ChangePluginLayerParameters(set, layer, selectedPlugin, parameters);
            }

            if (versionMismatch)
                EditorGUILayout.HelpBox("Cached output was generated with plugin version " +
                    layer.pluginVersion + "; installed version is " + descriptor.pluginVersion +
                    ". Regenerate when ready.", MessageType.Warning);
            if (!string.IsNullOrEmpty(layer.pluginLastError))
                EditorGUILayout.HelpBox(layer.pluginLastError, MessageType.Error);
            else if (layer.pluginStale)
                EditorGUILayout.HelpBox(layer.channels.Count > 0
                    ? "Parameters or lower layers changed. The previous cached result remains visible until regeneration succeeds."
                    : "This Plugin layer has not been generated yet.", MessageType.Warning);
            else EditorGUILayout.HelpBox("Cached output is current.", MessageType.None);

            if (running && string.Equals(runningPluginLayerId, layerKey, StringComparison.Ordinal))
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, 18f);
                EditorGUI.ProgressBar(progressRect, Mathf.Clamp01(pluginLayerProgress), "Regenerating");
                if (GUILayout.Button("Cancel")) pluginLayerCancellation.Cancel();
            }
            else
            {
                using (new EditorGUI.DisabledScope(running))
                    if (GUILayout.Button(layer.channels.Count > 0 ? "Regenerate" : "Generate"))
                        RegeneratePluginLayer(set, layer, selectedPlugin);
            }
        }

        private async void RegeneratePluginLayer(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin)
        {
            if (set == null || layer == null || plugin == null || pluginLayerCancellation != null)
                return;
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var destinations = new Dictionary<TextureSet, TexturePaintLayer>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                if (peer.layer.kind != TexturePaintLayerKind.Plugin)
                { ShowWorkspaceStatus("The logical Plugin layer is inconsistent across target members."); return; }
                destinations[peer.textureSet] = peer.layer;
            }

            pluginLayerCancellation = new System.Threading.CancellationTokenSource();
            runningPluginLayerId = !string.IsNullOrEmpty(layer.logicalLayerId)
                ? layer.logicalLayerId : layer.id;
            pluginLayerProgress = 0f;
            RepaintAll();
            try
            {
                TexturePaintPluginParameterSet parameters =
                    controller.Plugins.GetLayerParameters(layer, plugin);
                await controller.Plugins.ExecutePluginLayerAsync(plugin, controller.Textures,
                    parameters, destinations, new Progress<float>(value =>
                    {
                        pluginLayerProgress = value;
                        TexturePaintDockWindow.RepaintOpenWindows();
                    }), pluginLayerCancellation.Token);
                SyncActiveLayerSelection(ActiveTextureSet);
                ShowWorkspaceStatus(plugin.Descriptor.displayName + " generated successfully");
            }
            catch (OperationCanceledException)
            {
                ShowWorkspaceStatus("Plugin generation cancelled; cached output retained");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowWorkspaceStatus("Plugin generation failed; cached output retained");
            }
            finally
            {
                pluginLayerCancellation?.Dispose();
                pluginLayerCancellation = null;
                runningPluginLayerId = null;
                pluginLayerProgress = 0f;
                MarkDocumentDirty();
                RepaintAll();
            }
        }

        private void DrawLayerMaskSource(TextureSet set, TexturePaintLayer layer)
        {
            TexturePaintLayerMask mask = layer?.layerMask;
            if (mask == null) return;
            mask.NormalizePaintSource();
            EditorGUILayout.Space(4f);
            if (!DrawPropertySubsectionFoldout("properties.active-layer.mask-paint-source",
                    "Mask Painting")) return;
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(new GUIContent("Mask Value",
                "0 paints black (hidden); 1 paints white (visible)."), mask.PaintValue, 0f, 1f);
            if (!EditorGUI.EndChangeCheck()) return;
            TexturePaintChannelSourceSettings source = TexturePaintLayerMask.DefaultSourceSettings();
            source.color = new Color(value, value, value, 1f);
            ChangeLayerMaskSource(set, layer, source, TexturePaintChannel.Albedo);
            layerMaskPaintValue = value;
            paintSource = TexturePaintBrushSource.Color;
            paintSourceTexture = null;
            paintSourceSprite = null;
            paintSourceOverlay = null;
        }

        private void DrawLayerMaskPluginProperties(TextureSet set, TexturePaintLayer layer)
        {
            TexturePaintLayerMask mask = layer?.layerMask;
            if (mask == null || !DrawPropertySubsectionFoldout(
                    "properties.active-layer.mask-plugin", "Mask Filter / Generator")) return;
            IReadOnlyList<ITexturePaintCommandExtensionV2> available =
                controller?.Plugins?.Commands ?? Array.Empty<ITexturePaintCommandExtensionV2>();
            ITexturePaintCommandExtensionV2 selected = controller?.Plugins?.FindCommand(mask.pluginId);
            var choices = new List<ITexturePaintCommandExtensionV2> { null };
            var labels = new List<string> { "None" };
            int selectedIndex = 0;
            for (int i = 0; i < available.Count; i++)
            {
                ITexturePaintCommandExtensionV2 candidate = available[i];
                if ((candidate.Descriptor.supportedTargets & TexturePaintPluginTarget.LayerMask) == 0)
                    continue;
                choices.Add(candidate);
                labels.Add(candidate.Descriptor.displayName + (candidate is ITexturePaintGeneratorV2
                    ? "  (Generator)" : "  (Filter)"));
                if (ReferenceEquals(candidate, selected)) selectedIndex = choices.Count - 1;
            }
            bool missing = !string.IsNullOrEmpty(mask.pluginId) && selected == null;
            if (missing)
            {
                labels.Insert(0, "Missing · " + mask.pluginId);
                choices.Insert(0, null);
                selectedIndex = 0;
            }
            bool running = pluginLayerCancellation != null;
            using (new EditorGUI.DisabledScope(running))
            {
                int next = EditorGUILayout.Popup("Plugin", selectedIndex, labels.ToArray());
                if (next != selectedIndex)
                {
                    selected = choices[next];
                    ChangeLayerMaskPluginDefinition(set, layer, selected);
                    missing = false;
                }
            }
            if (missing)
            {
                EditorGUILayout.HelpBox("The mask plugin is unavailable. The saved mask remains editable.",
                    MessageType.Warning);
                return;
            }
            if (selected == null)
            {
                EditorGUILayout.HelpBox(
                    "Choose a compatible filter or generator. Its result is written directly to this grayscale mask and can then be painted by hand.",
                    MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField(selected.Descriptor.description, EditorStyles.wordWrappedMiniLabel);
            TexturePaintPluginParameterSet parameters =
                string.Equals(mask.pluginId, selected.Descriptor.id, StringComparison.Ordinal) &&
                mask.pluginParameters != null
                    ? mask.pluginParameters.Clone()
                    : controller.Plugins.CreateParameters(selected);
            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUI.BeginChangeCheck();
                PluginManagerWindow.DrawParameters(selected.Descriptor, parameters,
                    id => id == "sourceChannel" || id == "destinationChannel");
                if (EditorGUI.EndChangeCheck())
                    ChangeLayerMaskPluginParameters(set, layer, selected, parameters);
            }
            if (!string.IsNullOrEmpty(mask.pluginLastError))
                EditorGUILayout.HelpBox(mask.pluginLastError, MessageType.Error);
            else if (mask.pluginStale)
                EditorGUILayout.HelpBox("Parameters changed. Generate to update the mask.",
                    MessageType.Warning);
            if (!running)
            {
                if (GUILayout.Button("Generate Mask"))
                    RegenerateLayerMaskPlugin(set, layer, selected);
            }
            else if (GUILayout.Button("Cancel Mask Generation")) pluginLayerCancellation.Cancel();
        }

        private async void RegenerateLayerMaskPlugin(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin)
        {
            if (set == null || layer?.layerMask == null || plugin == null ||
                pluginLayerCancellation != null) return;
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var destinations = new Dictionary<TextureSet, TexturePaintLayer>();
            for (int i = 0; i < peers.Count; i++)
            {
                if (peers[i].layer.layerMask == null)
                { ShowWorkspaceStatus("Every logical layer member needs a mask."); return; }
                destinations[peers[i].textureSet] = peers[i].layer;
            }
            pluginLayerCancellation = new System.Threading.CancellationTokenSource();
            runningPluginLayerId = "mask:" + (!string.IsNullOrEmpty(layer.logicalLayerId)
                ? layer.logicalLayerId : layer.id);
            try
            {
                TexturePaintPluginParameterSet parameters = layer.layerMask.pluginParameters?.Clone() ??
                    controller.Plugins.CreateParameters(plugin);
                await controller.Plugins.ExecuteLayerMaskAsync(plugin, controller.Textures, parameters,
                    destinations, new Progress<float>(value =>
                    {
                        pluginLayerProgress = value;
                        TexturePaintDockWindow.RepaintOpenWindows();
                    }), pluginLayerCancellation.Token);
                ShowWorkspaceStatus(plugin.Descriptor.displayName + " generated the layer mask");
            }
            catch (OperationCanceledException)
            { ShowWorkspaceStatus("Mask generation cancelled; previous mask retained"); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowWorkspaceStatus("Mask generation failed; previous mask retained");
            }
            finally
            {
                pluginLayerCancellation?.Dispose(); pluginLayerCancellation = null;
                runningPluginLayerId = null; pluginLayerProgress = 0f;
                MarkDocumentDirty(); RepaintAll();
            }
        }

        private void DrawLayerChannelProperties(TextureSet set, TexturePaintLayer layer,
            bool includeAddControls)
        {
            if (set == null || layer == null || layer.kind == TexturePaintLayerKind.Group) return;
            EditorGUILayout.Space(3f);
            GUILayout.Label("Layer Channels & Settings", EditorStyles.boldLabel);
            // if (!DrawPropertySubsectionFoldout("properties.active-layer.layer-channels",
            //         "Channel Details")) return;
            var authored = new List<TexturePaintChannel>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (layer.channels.ContainsKey(channel)) authored.Add(channel);

            if (authored.Count == 0)
                EditorGUILayout.HelpBox("This layer has no authored channel texture yet.", MessageType.Info);
            for (int i = 0; i < authored.Count; i++)
            {
                TexturePaintChannel channel = authored[i];
                string foldoutId = LayerChannelFoldoutId(layer, channel);
                bool expanded = IsLayerChannelExpanded(foldoutId);
                string header = LayerChannelHeader(channel);
                GUIContent tooltip = new GUIContent(string.Empty,
                    $"{TexturePaintChannelUtility.DisplayName(channel)} is a " +
                    $"{LayerChannelTypeName(channel).ToLowerInvariant()} channel. " +
                    "Expand to edit its source, painting, opacity, blend, and preview settings.");
                bool nextExpanded = GUIHelper.FoldOutBar(expanded, header, tooltip);
                if (nextExpanded != expanded)
                {
                    SetLayerChannelExpanded(foldoutId, nextExpanded);
                    if (nextExpanded) SelectLayerChannelForEditing(layer, channel);
                }
                if (!nextExpanded) continue;

                GUIHelper.BeginVerticalPadded(10, new Color(0.55f, 0.25f, 0.25f));
                bool removed = DrawLayerChannelEntry(set, layer, channel);
                GUIHelper.EndVerticalPadded(10);
                if (removed) break;
            }

            if (!includeAddControls) return;
            var available = new List<TexturePaintChannel>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (set.GetChannel(channel) != null && !layer.channels.ContainsKey(channel))
                    available.Add(channel);
            if (available.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup("New Channel", 0, new[] { "All supported channels added" });
                    GUILayout.Button("Add Channel");
                }
                return;
            }

            int selected = available.IndexOf(workspaceAddLayerChannel);
            if (selected < 0) selected = 0;
            string[] names = new string[available.Count];
            for (int i = 0; i < available.Count; i++)
                names[i] = TexturePaintChannelUtility.DisplayName(available[i]);
            selected = EditorGUILayout.Popup("New Channel", selected, names);
            workspaceAddLayerChannel = available[Mathf.Clamp(selected, 0, available.Count - 1)];
            if (GUILayout.Button(new GUIContent("Add Channel",
                "Add an empty editable channel texture to every member of this logical layer.")) &&
                AddLayerChannelWithHistory(set, layer, workspaceAddLayerChannel))
            {
                SetSelectedChannelAndRefreshSource(workspaceAddLayerChannel);
            }
        }

        private bool DrawLayerChannelEntry(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannel channel)
        {
            TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel);
            bool pluginOutput = layer.kind == TexturePaintLayerKind.Plugin;
            if (!pluginOutput)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                bool remove = GUILayout.Button(new GUIContent("Remove Channel",
                    "Remove this channel texture and its settings."), GUILayout.Width(108f));
                GUILayout.EndHorizontal();
                if (remove && EditorUtility.DisplayDialog("Remove Layer Channel",
                        $"Remove the {channel} channel from '{layer.name}'?\n\n" +
                        "Its painted pixels will be discarded. Undo remains available.",
                        "Remove Channel", "Cancel"))
                {
                    bool wasSelected = selectedChannel == channel;
                    if (RemoveLayerChannelWithHistory(set, layer, channel))
                    {
                        SetLayerChannelExpanded(LayerChannelFoldoutId(layer, channel), false);
                        if (wasSelected)
                        {
                            TexturePaintChannel next = TexturePaintChannel.Albedo;
                            foreach (TexturePaintChannel candidate in Enum.GetValues(typeof(TexturePaintChannel)))
                                if (layer.channels.ContainsKey(candidate)) { next = candidate; break; }
                            SetSelectedChannelAndRefreshSource(next);
                        }
                        return true;
                    }
                }
            }

            if (set.GetChannel(channel) == null)
                EditorGUILayout.HelpBox("The active target has no matching logical channel.",
                    MessageType.Warning);

            if (selectedChannel != channel)
            {
                if (GUILayout.Button(new GUIContent("Use for Painting and Preview",
                        "Make this the active brush, path, 2D canvas, and channel-preview target.")))
                    SelectLayerChannelForEditing(layer, channel);
            }
            else EditorGUILayout.LabelField("Active Painting / Preview Channel",
                EditorStyles.miniBoldLabel);

            if (!pluginOutput) DrawLayerChannelSource(set, layer, channel, settings);

            if (channel == TexturePaintChannel.NormalControl)
            {
                EditorGUI.BeginChangeCheck();
                float heightStrength = EditorGUILayout.Slider(new GUIContent("Height Strength",
                    "Slope intensity generated only by this layer's grayscale height field."),
                    set.ResolveNormalControlStrength(settings), 0f, 16f);
                if (EditorGUI.EndChangeCheck())
                    ChangeLayerNormalControlStrength(set, layer, heightStrength);

                EditorGUI.BeginChangeCheck();
                int radius = EditorGUILayout.IntSlider(new GUIContent("Sample Radius (px)",
                    "Shared neighbor distance used to calculate gradients for this target."),
                    set.normalControlRadius, 1, 16);
                bool invertHeight = EditorGUILayout.Toggle(new GUIContent("Invert Height",
                    "Shared raised/recessed interpretation for this target."),
                    set.normalControlInvert);
                if (EditorGUI.EndChangeCheck())
                    ChangeNormalControlSettings(set, set.normalControlStrength, radius, invertHeight);
            }

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("Enabled", settings.enabled);
            bool locked = pluginOutput ? true : EditorGUILayout.Toggle("Lock Painting", settings.locked);
            float contribution = settings.contribution;
            if (layer.kind != TexturePaintLayerKind.Fill && !pluginOutput)
                contribution = EditorGUILayout.Slider("Channel Paint Strength", contribution, 0f, 1f);
            float opacity = EditorGUILayout.Slider("Channel Opacity", settings.opacity, 0f, 1f);
            TexturePaintBlendMode blend = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                "Channel Blend", settings.blendMode);
            if (EditorGUI.EndChangeCheck())
                ChangeLayerChannel(set, layer, channel, enabled, locked, contribution, opacity, blend);

            if (selectedChannel == channel)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("3D Preview", EditorStyles.boldLabel);
                bool nextSolo = EditorGUILayout.Toggle(new GUIContent("Solo Channel",
                    "Preview this logical channel without material shading."), channelSolo);
                if (nextSolo != channelSolo)
                {
                    channelSolo = nextSolo;
                    if (channelSolo) previewBefore = false;
                }
                bool nextBefore = EditorGUILayout.Toggle(new GUIContent("Before Painting",
                    "Show original source textures while preserving the character material and lighting."),
                    previewBefore);
                if (nextBefore != previewBefore)
                {
                    previewBefore = nextBefore;
                    if (previewBefore) channelSolo = false;
                }
            }
            return false;
        }

        internal static string LayerChannelHeader(TexturePaintChannel channel) =>
            TexturePaintChannelUtility.DisplayName(channel) + " - " + LayerChannelTypeName(channel);

        internal static string LayerChannelTypeName(TexturePaintChannel channel)
        {
            if (channel == TexturePaintChannel.Normal) return "Normal Map";
            if (channel == TexturePaintChannel.NormalControl) return "Height / Grayscale";
            if (TexturePaintChannelUtility.IsColor(channel)) return "Color";
            if (TexturePaintChannelUtility.IsGrayscale(channel)) return "Grayscale";
            if (channel == TexturePaintChannel.Custom) return "Custom";
            return "Texture";
        }

        private static string LayerChannelFoldoutId(TexturePaintLayer layer,
            TexturePaintChannel channel) => (layer?.id ?? string.Empty) + ":" + channel;

        private bool IsLayerChannelExpanded(string foldoutId)
        {
            workspaceExpandedLayerChannelIds ??= new List<string>();
            return workspaceExpandedLayerChannelIds.Contains(foldoutId);
        }

        private void SetLayerChannelExpanded(string foldoutId, bool expanded)
        {
            workspaceExpandedLayerChannelIds ??= new List<string>();
            bool changed = expanded
                ? !workspaceExpandedLayerChannelIds.Contains(foldoutId)
                : workspaceExpandedLayerChannelIds.Remove(foldoutId);
            if (expanded && changed) workspaceExpandedLayerChannelIds.Add(foldoutId);
            if (!changed) return;
            TexturePaintDockWindow.RepaintOpenWindows();
            TexturePaintBrushWindow.RepaintOpenWindows();
        }

        private void DrawLayerChannelSource(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannel channel, TexturePaintLayerChannelSettings channelSettings)
        {
            TexturePaintChannelSourceSettings source = channelSettings.sourceSettings?.Clone() ??
                DefaultLayerChannelSource(layer, channel);
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            bool hasFirstChannel = layer.TryGetFirstAuthoredChannel(out TexturePaintChannel firstChannel);
            bool isFirstChannel = hasFirstChannel && channel == firstChannel;
            bool shareFillTransform = layer.kind == TexturePaintLayerKind.Fill &&
                layer.fillSettings?.useFirstChannelTransform == true;
            if (layer.kind == TexturePaintLayerKind.Fill && isFirstChannel)
            {
                EditorGUI.BeginChangeCheck();
                bool nextShareFillTransform = EditorGUILayout.Toggle(
                    new GUIContent("Use Transform For All Channels",
                        "Use this first channel's tiling, offset, and rotation for every channel in the Fill layer."),
                    shareFillTransform);
                if (EditorGUI.EndChangeCheck())
                {
                    TexturePaintFillSettings updated = FillSettingsFromChannelSource(source);
                    updated.useFirstChannelTransform = nextShareFillTransform;
                    ChangeFillLayer(set, layer, firstChannel, updated);
                    return;
                }
            }
            EditorGUI.BeginChangeCheck();
            if (TexturePaintChannelUtility.IsAuxiliary(channel))
            {
                int sourceIndex = source.source == TexturePaintBrushSource.Texture ? 0 : 1;
                sourceIndex = EditorGUILayout.Popup("Type", sourceIndex,
                    new[] { "Texture", "Color" });
                source.source = sourceIndex == 0 ? TexturePaintBrushSource.Texture :
                    TexturePaintBrushSource.Color;
                source.sourceOverlay = null;
            }
            else source.source = (TexturePaintBrushSource)EditorGUILayout.EnumPopup("Type", source.source);
            switch (source.source)
            {
                case TexturePaintBrushSource.Texture:
                    Texture2D texture = source.sourceSprite == null ? source.sourceTexture : null;
                    texture = (Texture2D)EditorGUILayout.ObjectField("Texture", texture,
                        typeof(Texture2D), false);
                    Sprite sprite = (Sprite)EditorGUILayout.ObjectField("Sprite", source.sourceSprite,
                        typeof(Sprite), false);
                    if (sprite != source.sourceSprite)
                    {
                        source.sourceSprite = sprite;
                        if (sprite != null) source.sourceTexture = null;
                    }
                    else if (texture != (source.sourceSprite == null ? source.sourceTexture : null))
                    {
                        source.sourceTexture = texture;
                        source.sourceSprite = null;
                    }
                    source.invert = EditorGUILayout.Toggle("Invert", source.invert);
                    if (channel == TexturePaintChannel.Normal)
                        source.normalConvention = (TexturePaintNormalConvention)EditorGUILayout.EnumPopup(
                            "Convention", source.normalConvention);
                    break;
                case TexturePaintBrushSource.Overlay:
                    source.sourceOverlay = (OverlayDataAsset)EditorGUILayout.ObjectField("Overlay",
                        source.sourceOverlay, typeof(OverlayDataAsset), false);
                    source.invert = EditorGUILayout.Toggle("Invert", source.invert);
                    if (channel == TexturePaintChannel.Normal)
                        source.normalConvention = (TexturePaintNormalConvention)EditorGUILayout.EnumPopup(
                            "Convention", source.normalConvention);
                    break;
                default:
                    if (TexturePaintChannelUtility.IsGrayscale(channel))
                    {
                        float value = EditorGUILayout.Slider("Value",
                            TexturePaintChannelUtility.ScalarValue(source.color), 0f, 1f);
                        source.color = new Color(value, value, value, source.color.a);
                    }
                    else source.color = EditorGUILayout.ColorField("Color", source.color);
                    break;
            }
            if (TexturePaintChannelUtility.IsAuxiliary(channel))
                EditorGUILayout.HelpBox("Normal Control accepts texture, sprite, or grayscale value sources; it is not present in OverlayData materials.", MessageType.None);
            if (layer.kind == TexturePaintLayerKind.Fill)
            {
                bool transformDrivenByFirst = shareFillTransform && !isFirstChannel;
                using (new EditorGUI.DisabledScope(transformDrivenByFirst))
                {
                    source.tiling = EditorGUILayout.Vector2Field("Tiling X / Y", source.tiling);
                    source.tiling.x = Mathf.Clamp(source.tiling.x, 0.01f, 1000f);
                    source.tiling.y = Mathf.Clamp(source.tiling.y, 0.01f, 1000f);
                    source.offset = EditorGUILayout.Vector2Field("Offset X / Y", source.offset);
                    source.rotation = EditorGUILayout.FloatField("Rotation", source.rotation);
                }
                if (transformDrivenByFirst)
                    EditorGUILayout.HelpBox($"Transform is driven by the {firstChannel} channel.",
                        MessageType.None);
            }
            if (!EditorGUI.EndChangeCheck()) return;

            var assignment = new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings>
                { [channel] = source };
            if (!ChangeLayerChannelSources(set, layer, assignment)) return;
            if (selectedChannel == channel) SelectLayerChannelForEditing(layer, channel);
            if (layer.IsSplineLayer && layer.spline?.PointCount > 0)
                RequestSplineReapply(set, false);
        }

        private TexturePaintChannelSourceSettings DefaultLayerChannelSource(TexturePaintLayer layer,
            TexturePaintChannel channel)
        {
            if (layer?.kind == TexturePaintLayerKind.Fill && channel == layer.fillChannel &&
                layer.fillSettings != null)
                return ChannelSourceFromFillSettings(layer.fillSettings);
            if (layer?.kind == TexturePaintLayerKind.Paint && layer.paintSettings != null &&
                layer.paintSettings.channel == channel)
                return ChannelSourceFromPaintSettings(layer.paintSettings);
            if (layer?.IsSplineLayer == true && layer.splineSettings != null &&
                layer.splineSettings.channel == channel)
                return ChannelSourceFromSplineSettings(layer.splineSettings);
            return new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Color,
                color = DefaultChannelSourceColor(channel),
                normalConvention = normalConvention
            };
        }

        private static Color DefaultChannelSourceColor(TexturePaintChannel channel)
        {
            switch (channel)
            {
                case TexturePaintChannel.Albedo: return Color.white;
                case TexturePaintChannel.Normal: return new Color(0.5f, 0.5f, 1f, 1f);
                case TexturePaintChannel.NormalControl: return new Color(0.5f, 0.5f, 0.5f, 1f);
                case TexturePaintChannel.Roughness:
                case TexturePaintChannel.AmbientOcclusion:
                case TexturePaintChannel.DetailMask: return Color.white;
                case TexturePaintChannel.SkinColorMask: return Color.clear;
                default: return Color.black;
            }
        }

        private void SelectLayerChannelForEditing(TexturePaintLayer layer, TexturePaintChannel channel)
        {
            SetSelectedChannelAndRefreshSource(channel);
            TexturePaintChannelSourceSettings source = layer?.GetChannelSettings(channel, false)?.sourceSettings;
            if (source == null) return;
            paintSource = source.source;
            paintSourceTexture = source.sourceTexture;
            paintSourceSprite = source.sourceSprite;
            paintSourceOverlay = source.sourceOverlay;
            paintColor = source.color;
            normalConvention = source.normalConvention;
            RefreshPaintSourceForChannel();
        }

        private static TexturePaintFillSettings FillSettingsFromChannelSource(
            TexturePaintChannelSourceSettings source)
        {
            return new TexturePaintFillSettings
            {
                source = source.source,
                sourceTexture = source.sourceTexture,
                sourceSprite = source.sourceSprite,
                sourceOverlay = source.sourceOverlay,
                color = source.color,
                normalConvention = source.normalConvention,
                invert = source.invert,
                tiling = source.tiling,
                offset = source.offset,
                rotation = source.rotation,
                projection = source.projection,
                triplanarBlend = source.triplanarBlend,
                blendOffset = source.blendOffset,
                blendSharpness = source.blendSharpness,
                ignoreSourceAlpha = source.ignoreSourceAlpha
            };
        }

        private static TexturePaintChannelSourceSettings ChannelSourceFromFillSettings(
            TexturePaintFillSettings settings)
        {
            return new TexturePaintChannelSourceSettings
            {
                source = settings.source,
                sourceTexture = settings.sourceTexture,
                sourceSprite = settings.sourceSprite,
                sourceOverlay = settings.sourceOverlay,
                color = settings.color,
                normalConvention = settings.normalConvention,
                invert = settings.invert,
                tiling = settings.tiling,
                offset = settings.offset,
                rotation = settings.rotation,
                projection = settings.projection,
                triplanarBlend = settings.triplanarBlend,
                blendOffset = settings.blendOffset,
                blendSharpness = settings.blendSharpness,
                ignoreSourceAlpha = settings.ignoreSourceAlpha
            };
        }

        private static TexturePaintChannelSourceSettings ChannelSourceFromPaintSettings(
            TexturePaintLayerSettings settings)
        {
            return new TexturePaintChannelSourceSettings
            {
                source = settings.source,
                sourceTexture = settings.sourceTexture,
                sourceSprite = settings.sourceSprite,
                sourceOverlay = settings.sourceOverlay,
                color = settings.color,
                normalConvention = settings.normalConvention
            };
        }

        private static TexturePaintChannelSourceSettings ChannelSourceFromSplineSettings(
            TexturePaintSplineSettings settings)
        {
            return new TexturePaintChannelSourceSettings
            {
                source = settings.source,
                sourceTexture = settings.sourceTexture,
                sourceSprite = settings.sourceSprite,
                sourceOverlay = settings.sourceOverlay,
                color = settings.color,
                normalConvention = settings.normalConvention
            };
        }

        private void DrawFillLayerProperties(TextureSet set, TexturePaintLayer layer)
        {
            layer.NormalizeKindPayload();
            TexturePaintChannel editingChannel = layer.channels.ContainsKey(selectedChannel)
                ? selectedChannel : layer.fillChannel;
            TexturePaintChannelSourceSettings source =
                layer.GetChannelSettings(editingChannel, false)?.sourceSettings;
            TexturePaintFillSettings current = source != null
                ? FillSettingsFromChannelSource(source)
                : layer.fillSettings;
            EditorGUI.BeginChangeCheck();
            var authoredChannels = new List<TexturePaintChannel>();
            foreach (TexturePaintChannel candidate in Enum.GetValues(typeof(TexturePaintChannel)))
                if (layer.channels.ContainsKey(candidate)) authoredChannels.Add(candidate);
            int fillChannelIndex = authoredChannels.IndexOf(editingChannel);
            if (fillChannelIndex < 0) fillChannelIndex = 0;
            string[] fillChannelNames = new string[authoredChannels.Count];
            for (int i = 0; i < authoredChannels.Count; i++)
                fillChannelNames[i] = TexturePaintChannelUtility.DisplayName(authoredChannels[i]);
            int nextFillChannelIndex = authoredChannels.Count > 0
                ? EditorGUILayout.Popup("Fill Channel", fillChannelIndex, fillChannelNames)
                : 0;
            TexturePaintChannel channel = authoredChannels.Count > 0
                ? authoredChannels[Mathf.Clamp(nextFillChannelIndex, 0, authoredChannels.Count - 1)]
                : editingChannel;
            if (EditorGUI.EndChangeCheck() && channel != editingChannel)
            {
                SelectLayerChannelForEditing(layer, channel);
                return;
            }
            EditorGUI.BeginChangeCheck();
            TexturePaintFillProjection projection = (TexturePaintFillProjection)EditorGUILayout.EnumPopup(
                new GUIContent("Fill Type", "Flat follows UV space; Triplanar projects continuously in world space"),
                current.projection);
            TexturePaintTriplanarBlend triplanarBlend = current.triplanarBlend;
            float blendOffset = current.blendOffset;
            float blendSharpness = current.blendSharpness;
            if (projection == TexturePaintFillProjection.Triplanar)
            {
                triplanarBlend = (TexturePaintTriplanarBlend)EditorGUILayout.EnumPopup(
                    new GUIContent("Edge Blend", "Hard selects the dominant axis; Cross Fade blends the three projections"),
                    triplanarBlend);
                if (triplanarBlend == TexturePaintTriplanarBlend.CrossFade)
                {
                    blendOffset = EditorGUILayout.Slider(new GUIContent("Blend Offset",
                        "Suppress minor projection axes before blending"), blendOffset, 0f, 0.49f);
                    blendSharpness = EditorGUILayout.Slider(new GUIContent("Blend Sharpness",
                        "Higher values tighten transitions between projection axes"), blendSharpness, 0.5f, 32f);
                }
            }
            if (!EditorGUI.EndChangeCheck()) return;

            TexturePaintFillSettings updated = current.Clone();
            updated.normalConvention = normalConvention;
            updated.useFirstChannelTransform = layer.fillSettings?.useFirstChannelTransform == true;
            updated.projection = projection;
            updated.triplanarBlend = triplanarBlend;
            updated.blendOffset = blendOffset;
            updated.blendSharpness = blendSharpness;
            ChangeFillLayer(set, layer, editingChannel, updated);
            SetSelectedChannelAndRefreshSource(editingChannel);
        }

        private void DrawStrokeProperties()
        {
            GUILayout.BeginHorizontal();
            bool polygon = GUILayout.Toggle(geometryFillMode == 1,
                new GUIContent("Fill Polygon", "Click a mesh polygon to fill it with the current paint color or mask value."),
                EditorStyles.miniButtonLeft);
            bool island = GUILayout.Toggle(geometryFillMode == 2,
                new GUIContent("Fill UV Island", "Click a mesh polygon to fill its complete UV island."),
                EditorStyles.miniButtonRight);
            GUILayout.EndHorizontal();
            int nextFillMode = polygon ? 1 : island ? 2 : 0;
            if (nextFillMode != geometryFillMode)
            {
                geometryFillMode = nextFillMode;
                if (geometryFillMode != 0)
                    ShowWorkspaceStatus(geometryFillMode == 1
                        ? "Polygon Fill armed: click a polygon; Esc cancels"
                        : "UV Island Fill armed: click an island; Esc cancels");
                SceneView.RepaintAll();
            }
            strokeStabilization = EditorGUILayout.Slider("Stabilization", strokeStabilization, 0f, 1f);
            directionSmoothing = EditorGUILayout.Slider("Direction Smoothing", directionSmoothing, 0f, 1f);
            pressureAffectsFlow = EditorGUILayout.Toggle("Pressure → Flow", pressureAffectsFlow);
            pressureAffectsSize = EditorGUILayout.Toggle("Pressure → Size", pressureAffectsSize);
            projectionDepth = EditorGUILayout.Slider("Projection Depth", projectionDepth, 0.05f, 2f);
            normalAngleLimit = EditorGUILayout.Slider("Normal Angle", normalAngleLimit, 0f, 180f);
            paintBackfaces = EditorGUILayout.Toggle("Paint Backfaces", paintBackfaces);
        }

        private void DrawPathProperties(TextureSet set)
        {
            if (set == null) return;
            if (spline == null)
            {
                if (GUILayout.Button("Create Path Layer")) CreateSplineLayerWithUndo(set);
                return;
            }
            splineMode = TryGetActivePathLayer(set, out TexturePaintLayer propertiesPathLayer) &&
                propertiesPathLayer.spline?.worldSpace == true;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle("Path Authoring", TryGetActivePathLayer(set, out _));
            DrawSplineSpaceProperty(set);
            EditorGUILayout.HelpBox(spline.worldSpace
                ? "3D-only: edit points and controls in the Scene view."
                : "2D-only: edit points in the 2D view; rasterization does not use model geometry.",
                MessageType.None);
            EditorGUI.BeginChangeCheck();
            bool useBezier = EditorGUILayout.Toggle("Bezier Curves", spline.useBezier);
            bool showControls = EditorGUILayout.Toggle("Control Handles", spline.showControls);
            TexturePaintPathMode nextPathMode = (TexturePaintPathMode)EditorGUILayout.EnumPopup("Apply Mode", pathMode);
            TexturePaintPathOrientation nextOrientation;
            using (new EditorGUI.DisabledScope(nextPathMode == TexturePaintPathMode.Ribbon))
                nextOrientation = (TexturePaintPathOrientation)EditorGUILayout.EnumPopup("Orientation",
                    nextPathMode == TexturePaintPathMode.Ribbon
                        ? TexturePaintPathOrientation.FollowPath : pathOrientation);
            if (nextPathMode == TexturePaintPathMode.Ribbon)
                nextOrientation = TexturePaintPathOrientation.FollowPath;
            TexturePaintPathCap nextStartCap = pathStartCap;
            TexturePaintPathCap nextEndCap = pathEndCap;
            using (new EditorGUI.DisabledScope(nextPathMode == TexturePaintPathMode.Ribbon))
            {
                nextStartCap = (TexturePaintPathCap)EditorGUILayout.EnumPopup("Start Cap", pathStartCap);
                nextEndCap = (TexturePaintPathCap)EditorGUILayout.EnumPopup("End Cap", pathEndCap);
            }
            int nextRadialSymmetry = EditorGUILayout.IntSlider("Radial Copies", radialSymmetry, 1, 16);
            if (nextPathMode == TexturePaintPathMode.Ribbon)
            {
                EditorGUILayout.Space(3f);
                if (DrawPropertySubsectionFoldout("properties.path.ribbon-endpoint-tiles",
                        "Ribbon Endpoint Tiles"))
                {
                    DrawRibbonEndpointSource("Beginning", ref ribbonBeginningTexture, ref ribbonBeginningSprite);
                    DrawRibbonEndpointSource("End", ref ribbonEndTexture, ref ribbonEndSprite);
                    EditorGUILayout.LabelField(
                        "Optional endpoint images replace the first and final complete ribbon tiles. " +
                        "They use the same orientation as the repeating source.",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                BeginLightweightPathUndo(set, "Edit Path Parameters");
                spline.useBezier = useBezier; spline.showControls = showControls;
                pathMode = nextPathMode; pathOrientation = nextOrientation;
                pathStartCap = nextStartCap; pathEndCap = nextEndCap; radialSymmetry = nextRadialSymmetry;
                CompleteLightweightPathEdit(set, false);
            }
            DrawPathStampSpacingControl();
            EditorGUILayout.LabelField("Editor Mode", pathEditMode.ToString());
            EditorGUILayout.LabelField("Auto Update", pathAutoUpdate ? "On" : "Off");
            EditorGUILayout.HelpBox(pathAutoUpdate
                ? "Path editing modes and topology actions are in the Overlay Painter Path toolbar in the Scene view."
                : "The path preview is frozen. Use Update in the Overlay Painter Path toolbar when ready.",
                pathAutoUpdate ? MessageType.None : MessageType.Info);
            if (selectedSplinePoint >= 0 && selectedSplinePoint < spline.PointCount)
            {
                spline.EnsureControlPoints();
                using (new EditorGUI.DisabledScope(!PathEditModeAllowsAdjust(pathEditMode)))
                {
                    EditorGUI.BeginChangeCheck();
                    TexturePaintTangentMode tangent = (TexturePaintTangentMode)EditorGUILayout.EnumPopup("Point Tangent", spline.tangentModes[selectedSplinePoint]);
                    float pressure = EditorGUILayout.Slider("Point Pressure", spline.pressures[selectedSplinePoint], 0f, 1f);
                    float widthPercent = EditorGUILayout.Slider(new GUIContent("Point Width (%)", "Brush width at this point as a percentage of the path width"),
                        spline.widths[selectedSplinePoint] * 100f, 5f, 400f);
                    float width = widthPercent * 0.01f;
                    float flow = EditorGUILayout.Slider("Point Flow", spline.flows[selectedSplinePoint], 0f, 2f);
                    float roll = EditorGUILayout.Slider("Point Roll", spline.rolls[selectedSplinePoint], -180f, 180f);
                    float offset = spline.offsets[selectedSplinePoint];
                    using (new EditorGUI.DisabledScope(!spline.worldSpace))
                        offset = EditorGUILayout.Slider("Surface Offset", offset, -0.1f, 0.1f);
                    Color color = EditorGUILayout.ColorField("Point Color", spline.colors[selectedSplinePoint]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        IReadOnlyCollection<int> selection = selectedSplinePoints != null && selectedSplinePoints.Count > 0
                            ? selectedSplinePoints : new[] { selectedSplinePoint };
                        BeginLightweightPathUndo(set, "Edit Path Point Dynamics");
                        foreach (int point in selection)
                        {
                            if ((uint)point >= (uint)spline.PointCount) continue;
                            spline.SetTangentMode(point, tangent); spline.pressures[point] = pressure;
                            spline.widths[point] = width; spline.flows[point] = flow; spline.rolls[point] = roll;
                            spline.offsets[point] = offset; spline.colors[point] = color;
                        }
                        CompleteLightweightPathEdit(set, false);
                    }
                }
            }
        }

        private static void DrawRibbonEndpointSource(string label, ref Texture2D texture, ref Sprite sprite)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label + " Tile", EditorStyles.miniBoldLabel);
            Texture2D directTexture = sprite == null ? texture : null;
            EditorGUI.BeginChangeCheck();
            directTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", directTexture,
                typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                texture = directTexture;
                sprite = null;
            }
            EditorGUI.BeginChangeCheck();
            Sprite nextSprite = (Sprite)EditorGUILayout.ObjectField("Sprite", sprite, typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck())
            {
                sprite = nextSprite;
                if (sprite != null) texture = null;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawPerformanceProperties()
        {
            historyBudgetMB = EditorGUILayout.IntSlider("Undo Budget MB", historyBudgetMB, 16, 1024);
            coverageBudgetMB = EditorGUILayout.IntSlider("Stroke Budget MB", coverageBudgetMB, 16, 512);
            controller.Painting.History.MemoryBudgetBytes = historyBudgetMB * 1024L * 1024L;
            controller.Painting.CoverageMemoryBudgetBytes = coverageBudgetMB * 1024L * 1024L;
            TexturePaintPerformanceMetrics metrics = controller.Painting.Performance;
            EditorGUILayout.LabelField("Preview p95", metrics.PreviewP95Milliseconds.ToString("0.00") + " ms");
            EditorGUILayout.LabelField("Undo Memory", EditorUtility.FormatBytes(controller.Painting.History.EstimatedMemoryBytes));
            EditorGUILayout.LabelField("Stroke Memory", EditorUtility.FormatBytes(controller.Painting.ActiveCoverageMemoryBytes));
            EditorGUILayout.LabelField("Geometry Masks", metrics.geometryMaskBuilds.ToString());
            DrawStrokeDiagnostics(metrics);
        }

        private void DrawAssetShelf()
        {
            RefreshBrushShelfIfNeeded();
            DrawRegionHeader("ASSET SHELF", "Searchable presets with folders, tags, favorites, recents, thumbnails, and drag reorder.");
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            assetShelfSearch = GUILayout.TextField(assetShelfSearch ?? string.Empty, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
            List<string> folders = GetBrushFolders();
            int folderIndex = Mathf.Max(0, folders.IndexOf(assetShelfFolder ?? "All"));
            int nextFolder = EditorGUILayout.Popup(folderIndex, folders.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(140f));
            assetShelfFolder = folders[Mathf.Clamp(nextFolder, 0, folders.Count - 1)];
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            assetShelfFavoritesOnly = GUILayout.Toggle(assetShelfFavoritesOnly, new GUIContent("★ Favorites", "Show favorite brushes"), EditorStyles.toolbarButton, GUILayout.Width(78f));
            assetShelfRecentOnly = GUILayout.Toggle(assetShelfRecentOnly, new GUIContent("Recent", "Show recently used brushes"), EditorStyles.toolbarButton, GUILayout.Width(54f));
            if (GUILayout.Button(new GUIContent("New Folder", "Create a brush folder under the current Assets folder"), EditorStyles.toolbarButton, GUILayout.Width(72f))) CreateBrushFolder();
            if (GUILayout.Button(new GUIContent("Library…", "Open the full brush library editor"), EditorStyles.toolbarButton, GUILayout.Width(58f))) BrushEditor.Open(currentBrushLibrary);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            List<BrushShelfItem> visible = GetVisibleBrushes();
            Rect shelf = GUILayoutUtility.GetRect(100f, 10000f, 70f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float tileWidth = 112f, tileHeight = 94f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((shelf.width - 10f) / tileWidth));
            int rows = Mathf.CeilToInt(visible.Count / (float)columns);
            Rect content = new Rect(0f, 0f, Mathf.Max(shelf.width - 16f, columns * tileWidth), Mathf.Max(shelf.height, rows * tileHeight + 8f));
            workspaceShelfScroll = GUI.BeginScrollView(shelf, workspaceShelfScroll, content);
            for (int i = 0; i < visible.Count; i++)
            {
                int column = i % columns, row = i / columns;
                Rect tile = new Rect(column * tileWidth + 4f, row * tileHeight + 4f, tileWidth - 6f, tileHeight - 6f);
                DrawBrushTile(visible[i], tile);
            }
            HandleShelfDrop(content);
            GUI.EndScrollView();

            if (brush != null)
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label("Selected:", EditorStyles.miniLabel, GUILayout.Width(50f));
                workspaceRenameBrush ??= brush.name;
                workspaceRenameBrush = GUILayout.TextField(workspaceRenameBrush,
                    EditorStyles.toolbarTextField, GUILayout.MinWidth(60f), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Rename", EditorStyles.toolbarButton, GUILayout.Width(52f))) RenameBrushAsset(brush, workspaceRenameBrush);
                if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton, GUILayout.Width(60f))) DuplicateBrushAsset(brush);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label("Tags:", EditorStyles.miniLabel, GUILayout.Width(34f));
                EditorGUI.BeginChangeCheck();
                string tags = GUILayout.TextField(brush.tags ?? string.Empty, EditorStyles.toolbarTextField, GUILayout.MinWidth(100f));
                if (EditorGUI.EndChangeCheck())
                {
                    string previousTags = brush.tags;
                    brush.tags = tags;
                    PushLightweightCommand("Edit Brush Tags",
                        () => { brush.tags = previousTags; EditorUtility.SetDirty(brush); },
                        () => { brush.tags = tags; EditorUtility.SetDirty(brush); });
                    EditorUtility.SetDirty(brush);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawBrushTile(BrushShelfItem item, Rect tile)
        {
            bool selected = brush == item.preset;
            if (Event.current.type == EventType.Repaint)
                WorkspaceStyles.Asset.Draw(tile, false, false, false, false);
            Rect preview = new Rect(tile.x + 7f, tile.y + 5f, tile.width - 14f, 55f);
            Texture2D stamp = item.preset.ResolvedStampTexture;
            Texture thumbnail = stamp != null
                ? AssetPreview.GetAssetPreview(stamp) ?? AssetPreview.GetMiniThumbnail(stamp)
                : null;
            DrawBrushThumbnail(preview, item.preset, thumbnail);
            Rect star = new Rect(tile.xMax - 23f, tile.y + 4f, 19f, 19f);
            bool favorite = favoriteBrushGuids.Contains(item.guid);
            if (GUI.Button(star, favorite ? "★" : "☆", WorkspaceStyles.Star)) ToggleFavorite(item.guid);
            Rect nameRect = new Rect(tile.x + 5f, preview.yMax + 3f, tile.width - 10f, 18f);
            if (selected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(nameRect, new Color(WorkspaceStyles.AssetSelectionColor.r,
                    WorkspaceStyles.AssetSelectionColor.g, WorkspaceStyles.AssetSelectionColor.b, 0.28f));
            GUI.Label(nameRect, item.preset.name, WorkspaceStyles.AssetName);
            if (selected && Event.current.type == EventType.Repaint)
                DrawSelectionOutline(tile, WorkspaceStyles.AssetSelectionColor, 2f);
            Rect click = new Rect(tile.x, tile.y, tile.width, tile.height);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && click.Contains(Event.current.mousePosition) && !star.Contains(Event.current.mousePosition))
            {
                UseBrush(item); Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDrag && click.Contains(Event.current.mousePosition))
            {
                DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new UnityEngine.Object[] { item.preset };
                DragAndDrop.SetGenericData("UMA.TexturePaint.BrushGuid", item.guid); DragAndDrop.StartDrag(item.preset.name); Event.current.Use();
            }
            if ((Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform) && click.Contains(Event.current.mousePosition) &&
                DragAndDrop.GetGenericData("UMA.TexturePaint.BrushGuid") is string fromGuid && fromGuid != item.guid)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                if (Event.current.type == EventType.DragPerform)
                {
                    ReorderBrush(fromGuid, item.guid); DragAndDrop.AcceptDrag();
                    DragAndDrop.SetGenericData("UMA.TexturePaint.BrushGuid", null);
                }
                Event.current.Use();
            }
            if (Event.current.type == EventType.ContextClick && click.Contains(Event.current.mousePosition))
            { ShowBrushMenu(item); Event.current.Use(); }
        }

        private void RefreshBrushShelfIfNeeded()
        {
            if (!workspaceBrushesDirty) return;
            workspaceBrushesDirty = false;
            workspaceBrushes.Clear();
            string[] guids = AssetDatabase.FindAssets("t:BrushPreset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BrushPreset preset = AssetDatabase.LoadAssetAtPath<BrushPreset>(path);
                if (preset == null) continue;
                workspaceBrushes.Add(new BrushShelfItem
                {
                    guid = guids[i], path = path, folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets", preset = preset
                });
                if (!brushOrderGuids.Contains(guids[i])) brushOrderGuids.Add(guids[i]);
            }
            brushOrderGuids.RemoveAll(guid => workspaceBrushes.Find(item => item.guid == guid) == null);
            workspaceBrushes.Sort((a, b) =>
            {
                int ai = brushOrderGuids.IndexOf(a.guid), bi = brushOrderGuids.IndexOf(b.guid);
                if (ai != bi) return ai.CompareTo(bi);
                return string.Compare(a.preset.name, b.preset.name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private List<BrushShelfItem> GetVisibleBrushes()
        {
            var result = new List<BrushShelfItem>();
            for (int i = 0; i < workspaceBrushes.Count; i++)
            {
                BrushShelfItem item = workspaceBrushes[i];
                if (!string.IsNullOrEmpty(assetShelfFolder) && assetShelfFolder != "All" && item.folder != assetShelfFolder) continue;
                if (assetShelfFavoritesOnly && !favoriteBrushGuids.Contains(item.guid)) continue;
                if (assetShelfRecentOnly && !recentBrushGuids.Contains(item.guid)) continue;
                string searchable = item.preset.name + " " + item.preset.tags + " " + item.folder;
                if (!MatchesSearch(searchable, assetShelfSearch)) continue;
                result.Add(item);
            }
            if (assetShelfRecentOnly)
                result.Sort((a, b) => recentBrushGuids.IndexOf(a.guid).CompareTo(recentBrushGuids.IndexOf(b.guid)));
            return result;
        }

        private List<string> GetBrushFolders()
        {
            var folders = new List<string> { "All" };
            for (int i = 0; i < workspaceBrushes.Count; i++)
                if (!folders.Contains(workspaceBrushes[i].folder)) folders.Add(workspaceBrushes[i].folder);
            if (!string.IsNullOrEmpty(assetShelfFolder) && assetShelfFolder != "All" &&
                AssetDatabase.IsValidFolder(assetShelfFolder) && !folders.Contains(assetShelfFolder)) folders.Add(assetShelfFolder);
            folders.Sort(1, folders.Count - 1, StringComparer.OrdinalIgnoreCase);
            if (!folders.Contains(assetShelfFolder ?? "All")) assetShelfFolder = "All";
            return folders;
        }

        private void UseBrush(BrushShelfItem item)
        {
            SelectBrushPreset(item.preset);
            workspaceRenameBrush = brush.name;
            recentBrushGuids.Remove(item.guid);
            recentBrushGuids.Insert(0, item.guid);
            while (recentBrushGuids.Count > 16) recentBrushGuids.RemoveAt(recentBrushGuids.Count - 1);
            ShowWorkspaceStatus("Brush: " + brush.name);
        }

        private void ToggleFavorite(string guid)
        {
            if (!favoriteBrushGuids.Remove(guid)) favoriteBrushGuids.Add(guid);
        }

        private void ReorderBrush(string fromGuid, string targetGuid)
        {
            int from = brushOrderGuids.IndexOf(fromGuid), target = brushOrderGuids.IndexOf(targetGuid);
            if (from < 0 || target < 0 || from == target) return;
            brushOrderGuids.RemoveAt(from);
            if (from < target) target--;
            brushOrderGuids.Insert(Mathf.Clamp(target, 0, brushOrderGuids.Count), fromGuid);
            workspaceBrushesDirty = true;
            MarkDocumentDirty();
        }

        private void HandleShelfDrop(Rect shelf)
        {
            Event current = Event.current;
            if (!shelf.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform) ||
                DragAndDrop.GetGenericData("UMA.TexturePaint.BrushGuid") != null) return;
            bool supported = false;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                if (DragAndDrop.objectReferences[i] is BrushPreset ||
                    DragAndDrop.objectReferences[i] is Texture2D ||
                    DragAndDrop.objectReferences[i] is Sprite) { supported = true; break; }
            if (!supported) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                {
                    if (DragAndDrop.objectReferences[i] is BrushPreset preset)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(preset));
                        BrushShelfItem item = workspaceBrushes.Find(candidate => candidate.guid == guid);
                        if (item != null) UseBrush(item);
                    }
                    else if (DragAndDrop.objectReferences[i] is Texture2D stamp)
                    {
                        brush = null;
                        transientBrush.shape = BrushPreset.Shape.Stamp;
                        transientBrush.stampTexture = stamp;
                        transientBrush.stampSprite = null;
                        ShowWorkspaceStatus("Session stamp: " + stamp.name);
                    }
                    else if (DragAndDrop.objectReferences[i] is Sprite stampSprite)
                    {
                        brush = null;
                        transientBrush.shape = BrushPreset.Shape.Stamp;
                        transientBrush.stampSprite = stampSprite;
                        transientBrush.stampTexture = null;
                        ShowWorkspaceStatus("Session stamp: " + stampSprite.name);
                    }
                }
            }
            current.Use();
        }

        private void CreateBrushFolder()
        {
            string parent = !string.IsNullOrEmpty(assetShelfFolder) && assetShelfFolder != "All" ? assetShelfFolder : "Assets";
            if (!AssetDatabase.IsValidFolder(parent)) parent = "Assets";
            string unique = AssetDatabase.GenerateUniqueAssetPath(parent + "/New Brush Folder");
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(unique));
            if (!string.IsNullOrEmpty(guid))
            {
                assetShelfFolder = AssetDatabase.GUIDToAssetPath(guid);
                workspaceBrushesDirty = true;
                ShowWorkspaceStatus("Created " + assetShelfFolder);
            }
        }

        private static void DrawSelectionOutline(Rect rect, Color color, float width)
        {
            width = Mathf.Max(1f, width);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + width, width,
                Mathf.Max(0f, rect.height - width * 2f)), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y + width, width,
                Mathf.Max(0f, rect.height - width * 2f)), color);
        }

        private void RenameBrushAsset(BrushPreset preset, string newName)
        {
            string path = AssetDatabase.GetAssetPath(preset);
            if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(newName)) return;
            string error = AssetDatabase.RenameAsset(path, newName.Trim());
            if (!string.IsNullOrEmpty(error)) EditorUtility.DisplayDialog("Rename Brush", error, "OK");
            else { workspaceBrushesDirty = true; workspaceRenameBrush = newName.Trim(); AssetDatabase.SaveAssets(); }
        }

        private void DuplicateBrushAsset(BrushPreset preset)
        {
            string source = AssetDatabase.GetAssetPath(preset);
            if (string.IsNullOrEmpty(source)) return;
            string destination = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(Path.GetDirectoryName(source) ?? "Assets",
                preset.name + " Copy.asset").Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(source, destination)) return;
            AssetDatabase.SaveAssets(); workspaceBrushesDirty = true;
            BrushPreset copy = AssetDatabase.LoadAssetAtPath<BrushPreset>(destination);
            if (copy != null)
            {
                string guid = AssetDatabase.AssetPathToGUID(destination);
                brushOrderGuids.Add(guid); SelectBrushPreset(copy); workspaceRenameBrush = copy.name;
            }
        }

        private void ShowBrushMenu(BrushShelfItem item)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Use Brush"), false, () => UseBrush(item));
            menu.AddItem(new GUIContent(favoriteBrushGuids.Contains(item.guid) ? "Remove Favorite" : "Add Favorite"), false, () => ToggleFavorite(item.guid));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateBrushAsset(item.preset));
            menu.AddItem(new GUIContent("Ping in Project"), false, () => EditorGUIUtility.PingObject(item.preset));
            menu.ShowAsContext();
        }

        private void AddPaintLayer(TextureSet set)
        {
            SetSelectedChannelAndRefreshSource(TexturePaintChannel.Albedo);
            BeginLayerCreationUndo("Add Paint Layer");
            TexturePaintLayer created = set.AddLayer("Paint Layer " + (set.layers.Count + 1));
            created.visible = true;
            sourceMode = TexturePaintSourceMode.SourceOverlay;
            created.paintSettings = CreatePaintLayerSettings();
            CompleteLayerCreationUndo(created);
            SyncActiveLayerSelection(set);
        }

        private void ShowAddPaintLayerFromSpriteSet(TextureSet set)
        {
            OverlayPainterSpriteSetPickerWindow.Show((spriteSet, spriteIndex, tiling) =>
            {
                if (set == null || controller?.Textures == null) return;
                int previousCount = set.layers.Count;
                AddPaintLayer(set);
                if (set.layers.Count <= previousCount || (uint)set.activeLayerIndex >=
                    (uint)set.layers.Count) return;
                TexturePaintLayer layer = set.layers[set.activeLayerIndex];
                if (layer?.kind != TexturePaintLayerKind.Paint) return;
                AddFromSpriteSet(set, layer, spriteSet, spriteIndex, tiling);
            });
        }

        private void ShowAddFillLayerFromSpriteSet(TextureSet set)
        {
            OverlayPainterSpriteSetPickerWindow.ShowForFill(
                (spriteSet, spriteIndex, tiling, projection) =>
                    AddFillLayerFromSpriteSet(set, spriteSet, spriteIndex, tiling, projection));
        }

        private void AddFillLayerFromSpriteSet(TextureSet set, OverlayPainterSpriteSet spriteSet,
            int spriteIndex, Vector2 tiling, TexturePaintFillProjection projection)
        {
            if (set == null || spriteSet?.spriteSheets == null || spriteSet.spriteSheets.Count == 0)
            {
                ShowWorkspaceStatus("The selected Sprite Set has no channel sheets.");
                return;
            }

            var sources = new List<KeyValuePair<OverlayPainterSpriteSheet, Sprite>>(
                spriteSet.spriteSheets.Count);
            var errors = new List<string>();
            var assignedChannels = new HashSet<TexturePaintChannel>();
            for (int i = 0; i < spriteSet.spriteSheets.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = spriteSet.spriteSheets[i];
                if (sheet == null || sheet.spriteSheet == null)
                {
                    errors.Add($"sheet {i + 1} has no texture");
                    continue;
                }
                if (set.GetChannel(sheet.channel) == null)
                {
                    errors.Add($"{sheet.channel} is not available on this material");
                    continue;
                }
                if (!assignedChannels.Add(sheet.channel))
                {
                    errors.Add($"duplicate {sheet.channel} sheet");
                    continue;
                }
                if (!OverlayPainterSpriteSetEditorUtility.TryGetSprite(sheet, spriteIndex,
                        out Sprite sprite))
                {
                    errors.Add($"{sheet.SheetName} has no sprite {spriteIndex + 1}");
                    continue;
                }
                sources.Add(new KeyValuePair<OverlayPainterSpriteSheet, Sprite>(sheet, sprite));
            }
            if (sources.Count == 0)
            {
                ShowWorkspaceStatus(errors.Count == 0
                    ? "No channels could be assigned from that Sprite Set."
                    : "Sprite Set: " + string.Join("; ", errors));
                return;
            }

            string spriteName = spriteSet.GetSpriteName(spriteIndex, sources[0].Value.name);
            OverlayPainterSpriteSheet firstSheet = sources[0].Key;
            TexturePaintFillSettings firstSettings = CreateSpriteSetFillSettings(sources[0].Value,
                firstSheet.inverted, tiling, projection, normalConvention);
            firstSettings.useFirstChannelTransform = true;
            SetSelectedChannelAndRefreshSource(firstSheet.channel);
            BeginLayerCreationUndo("Add Fill Layer from Sprite Set");
            TexturePaintLayer created = set.AddFillLayer(spriteName + " Fill", firstSheet.channel,
                firstSettings);
            if (created == null)
            {
                pendingLayerCreationLabel = null;
                ShowWorkspaceStatus($"'{spriteName}' could not be generated for the active material.");
                return;
            }

            for (int i = 1; i < sources.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = sources[i].Key;
                TexturePaintFillSettings settings = CreateSpriteSetFillSettings(sources[i].Value,
                    sheet.inverted, tiling, projection, normalConvention);
                if (!set.UpdateFillLayer(created, sheet.channel, settings))
                    errors.Add($"{sheet.channel} could not be added");
            }
            created.fillChannel = firstSheet.channel;
            created.fillSettings = firstSettings.Clone();
            created.fillSettings.useFirstChannelTransform = true;
            created.fillColor = Color.white;
            set.RegenerateFillLayer(created);
            created.visible = true;
            CompleteLayerCreationUndo(created);
            if (!set.layers.Contains(created)) return;
            SyncActiveLayerSelection(set);
            SelectLayerChannelForEditing(created, firstSheet.channel);
            string status = $"Added '{spriteName}' as a {created.channels.Count}-channel " +
                $"{projection} Fill layer with {firstSettings.tiling.x:0.###} × " +
                $"{firstSettings.tiling.y:0.###} tiling.";
            if (errors.Count > 0) status += " Skipped: " + string.Join("; ", errors) + ".";
            ShowWorkspaceStatus(status);
        }

        internal static TexturePaintFillSettings CreateSpriteSetFillSettings(Sprite sprite,
            bool inverted, Vector2 tiling, TexturePaintFillProjection projection,
            TexturePaintNormalConvention convention)
        {
            tiling.x = Mathf.Clamp(tiling.x, 0.01f, 1000f);
            tiling.y = Mathf.Clamp(tiling.y, 0.01f, 1000f);
            return new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceSprite = sprite,
                invert = inverted,
                color = Color.white,
                normalConvention = convention,
                projection = projection,
                tiling = tiling,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendSharpness = 4f
            };
        }

        private void ShowAddPathLayerFromSpriteSet(TextureSet set)
        {
            OverlayPainterSpriteSetPickerWindow.ShowForPath((spriteSet, spriteIndex) =>
                AddPathLayerFromSpriteSet(set, spriteSet, spriteIndex));
        }

        private void AddPathLayerFromSpriteSet(TextureSet set, OverlayPainterSpriteSet spriteSet,
            int spriteIndex)
        {
            if (set == null || spriteSet?.spriteSheets == null || spriteSet.spriteSheets.Count == 0)
            {
                ShowWorkspaceStatus("The selected Sprite Set has no channel sheets.");
                return;
            }

            var sources = new List<KeyValuePair<OverlayPainterSpriteSheet, Sprite>>(
                spriteSet.spriteSheets.Count);
            var errors = new List<string>();
            var channels = new HashSet<TexturePaintChannel>();
            for (int i = 0; i < spriteSet.spriteSheets.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = spriteSet.spriteSheets[i];
                if (sheet == null || sheet.spriteSheet == null)
                {
                    errors.Add($"sheet {i + 1} has no texture");
                    continue;
                }
                if (set.GetChannel(sheet.channel) == null)
                {
                    errors.Add($"{sheet.channel} is not available on this material");
                    continue;
                }
                if (!OverlayPainterSpriteSetEditorUtility.TryGetSprite(sheet, spriteIndex,
                        out Sprite sprite))
                {
                    errors.Add($"{sheet.SheetName} has no sprite {spriteIndex + 1}");
                    continue;
                }
                if (!channels.Add(sheet.channel))
                {
                    errors.Add($"duplicate {sheet.channel} sheet");
                    continue;
                }
                sources.Add(new KeyValuePair<OverlayPainterSpriteSheet, Sprite>(sheet, sprite));
            }
            if (sources.Count == 0)
            {
                ShowWorkspaceStatus(errors.Count == 0
                    ? "No compatible channels could be assigned from that Sprite Set."
                    : "Sprite Set: " + string.Join("; ", errors));
                return;
            }

            string spriteName = spriteSet.GetSpriteName(spriteIndex, sources[0].Value.name);
            BeginLayerCreationUndo("Add Path Layer from Sprite Set");
            TexturePaintLayer created = CreateSplineLayer(set);
            created.name = spriteName + " Path";
            if (created.spline != null) created.spline.name = created.name;

            TexturePaintChannel firstChannel = sources[0].Key.channel;
            int assignedCount = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = sources[i].Key;
                TexturePaintChannelSourceSettings settings = CreateSpriteSetPathSourceSettings(
                    sources[i].Value, sheet.inverted, normalConvention);
                if (!TryAddPathSourceChannel(set, created, sheet.channel, settings))
                {
                    errors.Add($"{sheet.channel} could not be added");
                    continue;
                }
                if (assignedCount++ == 0) firstChannel = sheet.channel;
            }
            if (assignedCount == 0)
            {
                pendingLayerCreationLabel = null;
                set.layers.Remove(created);
                created.Dispose();
                set.activeLayerIndex = Mathf.Clamp(set.activeLayerIndex, -1, set.layers.Count - 1);
                set.BindPreviewTextures();
                ShowWorkspaceStatus("No compatible Sprite Set channels could be added to the Path layer.");
                return;
            }

            tool = TexturePaintTool.Paint;
            SelectLayerChannelForEditing(created, firstChannel);
            ResetPathEditingDefaults();
            created.splineSettings = CreateSplineSettings();
            set.BindPreviewTextures();
            CompleteLayerCreationUndo(created);
            if (!set.layers.Contains(created)) return;
            SyncActiveLayerSelection(set);
            SelectLayerChannelForEditing(created, firstChannel);
            string status = $"Added '{spriteName}' as a {assignedCount}-channel Ribbon Path. " +
                "Place path points in the Scene or 2D view.";
            if (errors.Count > 0) status += " Skipped: " + string.Join("; ", errors) + ".";
            ShowWorkspaceStatus(status);
        }

        internal static TexturePaintChannelSourceSettings CreateSpriteSetPathSourceSettings(
            Sprite sprite, bool inverted, TexturePaintNormalConvention convention)
        {
            return new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceSprite = sprite,
                invert = inverted,
                color = Color.white,
                normalConvention = convention,
                tiling = Vector2.one,
                projection = TexturePaintFillProjection.Flat,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendSharpness = 4f
            };
        }

        private void AddFillLayer(TextureSet set)
        {
            SetSelectedChannelAndRefreshSource(TexturePaintChannel.Albedo);
            BeginLayerCreationUndo("Add Fill Layer");
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = paintSource,
                sourceTexture = paintSourceSprite == null ? paintSourceTexture : null,
                sourceSprite = paintSourceSprite,
                sourceOverlay = paintSourceOverlay,
                color = paintColor,
                normalConvention = normalConvention,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendSharpness = 4f
            };
            TexturePaintLayer created = set.AddFillLayer("Fill Layer " + (set.layers.Count + 1),
                TexturePaintChannel.Albedo, settings);
            if (created == null)
            {
                pendingLayerCreationLabel = null;
                ShowWorkspaceStatus(paintSource == TexturePaintBrushSource.Texture && paintSourceTexture == null
                    ? "Select a source texture or sprite before adding a Fill layer."
                    : paintSource == TexturePaintBrushSource.Overlay && paintSourceOverlay == null
                        ? "Select an OverlayData source before adding a Fill layer."
                        : "The Fill source could not be generated for the selected channel.");
                return;
            }
            created.visible = true;
            CompleteLayerCreationUndo(created);
            SyncActiveLayerSelection(set);
        }

        private void AddOverlayFillLayer(TextureSet set, OverlayDataAsset overlay)
        {
            if (set == null || overlay == null) return;
            var channels = new List<KeyValuePair<TexturePaintChannel, int>>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (TexturePaintChannelUtility.IsAuxiliary(channel)) continue;
                if (set.TryResolveOverlaySource(overlay, channel, normalConvention, false,
                        out _, out int overlayIndex))
                    channels.Add(new KeyValuePair<TexturePaintChannel, int>(channel, overlayIndex));
            }
            if (channels.Count == 0)
            {
                ShowWorkspaceStatus($"'{overlay.overlayName}' has no channels compatible with the active material.");
                return;
            }

            Texture2D coverage = overlay.alphaMask as Texture2D;
            if (coverage == null && overlay.textureList != null && overlay.textureList.Length > 0)
                coverage = overlay.textureList[0] as Texture2D;
            bool hasSharedCoverage = coverage != null;
            TexturePaintFillSettings CreateSettings() => new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Overlay,
                sourceOverlay = overlay,
                color = Color.white,
                normalConvention = normalConvention,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendSharpness = 4f,
                ignoreSourceAlpha = hasSharedCoverage,
                useFirstChannelTransform = true
            };

            BeginLayerCreationUndo("Add Fill Layer from Overlay");
            TexturePaintChannel firstChannel = channels[0].Key;
            TexturePaintLayer created = set.AddFillLayer(overlay.overlayName, firstChannel,
                CreateSettings());
            if (created == null)
            {
                pendingLayerCreationLabel = null;
                ShowWorkspaceStatus($"'{overlay.overlayName}' could not be generated for the active material.");
                return;
            }

            var unsupportedBlends = new HashSet<OverlayDataAsset.OverlayBlend>();
            for (int i = 0; i < channels.Count; i++)
            {
                TexturePaintChannel channel = channels[i].Key;
                if (i > 0 && !set.UpdateFillLayer(created, channel, CreateSettings())) continue;
                OverlayDataAsset.OverlayBlend overlayBlend = overlay.overlayBlend != null &&
                    (uint)channels[i].Value < (uint)overlay.overlayBlend.Length
                        ? overlay.overlayBlend[channels[i].Value]
                        : OverlayDataAsset.OverlayBlend.Normal;
                TexturePaintLayerChannelSettings channelSettings = created.GetChannelSettings(channel);
                if (TryMapOverlayBlend(overlayBlend, out TexturePaintBlendMode paintBlend))
                    channelSettings.blendMode = paintBlend;
                else
                {
                    channelSettings.blendMode = TexturePaintBlendMode.Normal;
                    unsupportedBlends.Add(overlayBlend);
                }
            }
            created.fillChannel = firstChannel;
            created.fillSettings = CreateSettings();
            created.fillColor = Color.white;
            if (created.channelSettings.TryGetValue(firstChannel,
                    out TexturePaintLayerChannelSettings firstSettings))
                created.blendMode = firstSettings.blendMode;

            if (hasSharedCoverage)
            {
                TexturePaintLayerMask mask = set.AddLayerMask(created, 1f);
                if (mask != null)
                {
                    mask.effects.textureOverlay.enabled = true;
                    mask.effects.textureOverlay.texture = coverage;
                    mask.effects.textureOverlay.sourceChannel =
                        TexturePaintLayerMaskTextureChannel.Alpha;
                    mask.effects.textureOverlay.combine = TexturePaintBlendMode.Multiply;
                    mask.effects.textureOverlay.opacity = 1f;
                }
            }
            set.RegenerateFillLayer(created);
            created.visible = true;
            CompleteLayerCreationUndo(created);
            if (!set.layers.Contains(created)) return;
            SyncActiveLayerSelection(set);
            SelectLayerChannelForEditing(created, firstChannel);
            string status = $"Added '{overlay.overlayName}' as a {created.channels.Count}-channel Fill layer.";
            if (unsupportedBlends.Count > 0)
                status += " Unsupported UMA blend modes use Normal: " +
                    string.Join(", ", unsupportedBlends) + ".";
            ShowWorkspaceStatus(status);
        }

        private void AddOverlayPathLayer(TextureSet set, OverlayDataAsset overlay)
        {
            if (set == null || overlay == null) return;
            var channels = new List<KeyValuePair<TexturePaintChannel, int>>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (TexturePaintChannelUtility.IsAuxiliary(channel)) continue;
                if (set.TryResolveOverlaySource(overlay, channel, normalConvention, false,
                        out _, out int overlayIndex))
                    channels.Add(new KeyValuePair<TexturePaintChannel, int>(channel, overlayIndex));
            }
            if (channels.Count == 0)
            {
                ShowWorkspaceStatus($"'{overlay.overlayName}' has no channels compatible with the active material.");
                return;
            }

            BeginLayerCreationUndo("Add Path Layer from OverlayDataAsset");
            TexturePaintLayer created = CreateSplineLayer(set);
            string overlayName = string.IsNullOrWhiteSpace(overlay.overlayName) ? overlay.name : overlay.overlayName;
            created.name = overlayName + " Path";
            if (created.spline != null) created.spline.name = created.name;

            var unsupportedBlends = new HashSet<OverlayDataAsset.OverlayBlend>();
            TexturePaintChannel firstChannel = channels[0].Key;
            int assignedCount = 0;
            for (int i = 0; i < channels.Count; i++)
            {
                TexturePaintChannel channel = channels[i].Key;
                TexturePaintChannelSourceSettings source = CreateOverlayPathSourceSettings(overlay,
                    normalConvention);
                if (!TryAddPathSourceChannel(set, created, channel, source)) continue;

                OverlayDataAsset.OverlayBlend overlayBlend = overlay.overlayBlend != null &&
                    (uint)channels[i].Value < (uint)overlay.overlayBlend.Length
                        ? overlay.overlayBlend[channels[i].Value]
                        : OverlayDataAsset.OverlayBlend.Normal;
                TexturePaintLayerChannelSettings channelSettings = created.GetChannelSettings(channel);
                if (TryMapOverlayBlend(overlayBlend, out TexturePaintBlendMode paintBlend))
                    channelSettings.blendMode = paintBlend;
                else
                {
                    channelSettings.blendMode = TexturePaintBlendMode.Normal;
                    unsupportedBlends.Add(overlayBlend);
                }
                if (assignedCount++ == 0)
                {
                    firstChannel = channel;
                    created.blendMode = channelSettings.blendMode;
                }
            }
            if (assignedCount == 0)
            {
                pendingLayerCreationLabel = null;
                set.layers.Remove(created);
                created.Dispose();
                set.activeLayerIndex = Mathf.Clamp(set.activeLayerIndex, -1, set.layers.Count - 1);
                set.BindPreviewTextures();
                ShowWorkspaceStatus($"'{overlayName}' could not be assigned to a Path layer.");
                return;
            }

            tool = TexturePaintTool.Paint;
            SelectLayerChannelForEditing(created, firstChannel);
            ResetPathEditingDefaults();
            created.splineSettings = CreateSplineSettings();
            set.BindPreviewTextures();
            CompleteLayerCreationUndo(created);
            if (!set.layers.Contains(created)) return;
            SyncActiveLayerSelection(set);
            SelectLayerChannelForEditing(created, firstChannel);
            string status = $"Added '{overlayName}' as a {assignedCount}-channel Ribbon Path. " +
                "Place path points in the Scene or 2D view.";
            if (unsupportedBlends.Count > 0)
                status += " Unsupported UMA blend modes use Normal: " +
                    string.Join(", ", unsupportedBlends) + ".";
            ShowWorkspaceStatus(status);
        }

        internal static TexturePaintChannelSourceSettings CreateOverlayPathSourceSettings(
            OverlayDataAsset overlay, TexturePaintNormalConvention convention)
        {
            return new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Overlay,
                sourceOverlay = overlay,
                color = Color.white,
                normalConvention = convention,
                tiling = Vector2.one,
                projection = TexturePaintFillProjection.Flat,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendSharpness = 4f
            };
        }

        private static bool TryAddPathSourceChannel(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannel channel, TexturePaintChannelSourceSettings source)
        {
            if (set == null || layer?.IsSplineLayer != true || source == null ||
                set.GetChannel(channel) == null) return false;
            EditableTextureTarget target = set.GetPaintTarget(channel,
                TexturePaintSourceMode.SourceOverlay);
            if (target == null) return false;
            layer.GetChannelSettings(channel).sourceSettings = source.Clone();
            return true;
        }

        private static bool TryMapOverlayBlend(OverlayDataAsset.OverlayBlend source,
            out TexturePaintBlendMode destination)
        {
            switch (source)
            {
                case OverlayDataAsset.OverlayBlend.Normal:
                    destination = TexturePaintBlendMode.Normal;
                    return true;
                case OverlayDataAsset.OverlayBlend.Multiply:
                    destination = TexturePaintBlendMode.Multiply;
                    return true;
                case OverlayDataAsset.OverlayBlend.Overlay:
                    destination = TexturePaintBlendMode.Overlay;
                    return true;
                case OverlayDataAsset.OverlayBlend.Screen:
                    destination = TexturePaintBlendMode.Screen;
                    return true;
                case OverlayDataAsset.OverlayBlend.Subtract:
                    destination = TexturePaintBlendMode.Subtract;
                    return true;
                default:
                    destination = TexturePaintBlendMode.Normal;
                    return false;
            }
        }

        private void AddPluginLayer(TextureSet set)
        {
            if (set == null) return;
            BeginLayerCreationUndo("Add Plugin Layer");
            TexturePaintLayer created = set.AddPluginLayer(
                "Plugin Layer " + (set.layers.Count + 1));
            created.visible = true;
            CompleteLayerCreationUndo(created);
            SyncActiveLayerSelection(set);
            ShowWorkspaceStatus(controller?.Plugins?.Commands.Count > 0
                ? "Plugin layer created. Choose a generator or filter in its properties."
                : "Plugin layer created, but no generator/filter plugins are currently installed.");
        }

        private void AddFromSpriteSet(TextureSet set, TexturePaintLayer layer,
            OverlayPainterSpriteSet spriteSet, int spriteIndex, Vector2 tiling)
        {
            if (set == null || layer == null || layer.kind == TexturePaintLayerKind.Group ||
                layer.kind == TexturePaintLayerKind.Plugin ||
                !set.layers.Contains(layer))
            {
                ShowWorkspaceStatus("The target Paint, Fill, or Path layer is no longer available.");
                return;
            }
            if (spriteSet?.spriteSheets == null || spriteSet.spriteSheets.Count == 0)
            {
                ShowWorkspaceStatus("The selected Sprite Set has no channel sheets.");
                return;
            }

            var sources = new List<KeyValuePair<OverlayPainterSpriteSheet, Sprite>>(
                spriteSet.spriteSheets.Count);
            var errors = new List<string>();
            for (int i = 0; i < spriteSet.spriteSheets.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = spriteSet.spriteSheets[i];
                if (sheet == null || sheet.spriteSheet == null)
                {
                    errors.Add($"sheet {i + 1} has no texture");
                    continue;
                }
                if (set.GetChannel(sheet.channel) == null)
                {
                    errors.Add($"{sheet.channel} is not available on this material");
                    continue;
                }
                if (!OverlayPainterSpriteSetEditorUtility.TryGetSprite(sheet, spriteIndex,
                    out Sprite sprite))
                {
                    errors.Add($"{sheet.SheetName} has no sprite {spriteIndex + 1}");
                    continue;
                }
                sources.Add(new KeyValuePair<OverlayPainterSpriteSheet, Sprite>(sheet, sprite));
            }
            if (sources.Count == 0)
            {
                ShowWorkspaceStatus(errors.Count == 0
                    ? "No channels could be assigned from that Sprite Set."
                    : "Sprite Set: " + string.Join("; ", errors));
                return;
            }

            string spriteName = spriteSet.GetSpriteName(spriteIndex, sources[0].Value.name);
            tiling.x = Mathf.Clamp(tiling.x, 0.01f, 1000f);
            tiling.y = Mathf.Clamp(tiling.y, 0.01f, 1000f);
            var assignments = new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings>();
            for (int i = 0; i < sources.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = sources[i].Key;
                if (assignments.ContainsKey(sheet.channel))
                {
                    errors.Add($"duplicate {sheet.channel} sheet");
                    continue;
                }
                if (!layer.channels.ContainsKey(sheet.channel) &&
                    !AddLayerChannelWithHistory(set, layer, sheet.channel))
                {
                    errors.Add($"{sheet.channel} could not be added");
                    continue;
                }
                assignments[sheet.channel] = new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Texture,
                    sourceSprite = sources[i].Value,
                    invert = sheet.inverted,
                    color = Color.white,
                    normalConvention = normalConvention,
                    tiling = tiling,
                    projection = TexturePaintFillProjection.Flat,
                    triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                    blendSharpness = 4f
                };
            }
            if (assignments.Count == 0 || !ChangeLayerChannelSources(set, layer, assignments)) return;

            TexturePaintChannel activeChannel = sources[0].Key.channel;
            if (!assignments.ContainsKey(activeChannel))
                foreach (TexturePaintChannel assigned in assignments.Keys) { activeChannel = assigned; break; }
            if (layer.kind == TexturePaintLayerKind.Fill) set.RegenerateFillLayer(layer);
            set.BindPreviewTextures();
            SyncActiveLayerSelection(set);
            SelectLayerChannelForEditing(layer, activeChannel);
            if (layer.IsSplineLayer && layer.spline?.PointCount > 0)
                RequestSplineReapply(set, false);
            string status = $"Assigned {spriteName} to {assignments.Count} channel" +
                (assignments.Count == 1 ? string.Empty : "s") + $" on '{layer.name}'.";
            if (errors.Count > 0) status += " Skipped: " + string.Join("; ", errors) + ".";
            ShowWorkspaceStatus(status);
        }

        private void DuplicateActiveLayer(TextureSet set)
        {
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return;
            DuplicateLayerWithHistory(set, set.activeLayerIndex);
            SyncActiveLayerSelection(set); MarkDocumentDirty();
        }

        private void MergeActiveLayer(TextureSet set)
        {
            if (set == null || set.activeLayerIndex <= 0) return;
            bool wasMaskMode = IsLayerMaskMode(set);
            if (MergeLayerWithHistory(set, set.activeLayerIndex) && wasMaskMode)
                ExitLayerMaskMode();
            SyncActiveLayerSelection(set); MarkDocumentDirty();
        }

        private void DeleteLayer(TextureSet set, int index, bool confirm)
        {
            if (set == null || (uint)index >= (uint)set.layers.Count) return;
            TexturePaintLayer layer = set.layers[index];
            if (confirm && !EditorUtility.DisplayDialog("Delete Texture Layer",
                GetLayerDeletionConfirmation(set, layer), "Delete", "Cancel")) return;
            bool wasMaskMode = IsLayerMaskMode(set) && set.activeLayerIndex == index;
            DeleteLayerWithHistory(set, index);
            if (wasMaskMode) ExitLayerMaskMode();
            SyncActiveLayerSelection(set); MarkDocumentDirty();
        }

        private void ShowLayerMenu(TextureSet set, TexturePaintLayer layer, int index)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent(layer.kind == TexturePaintLayerKind.Group
                    ? "Preset/Save Group as Material Preset..."
                    : "Preset/Save Layer as Material Preset..."), false,
                () => SaveLayerAsMaterialPreset(set, layer));
            menu.AddItem(new GUIContent("Preset/Save Entire Stack as Material Preset..."), false,
                () => SaveWholeStackAsMaterialPreset(set));
            menu.AddItem(new GUIContent("Preset/Apply Material Preset..."), false,
                () => OpenMaterialPresetPicker(set));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Rename\tF2"), false, () => BeginLayerRename(layer));
            menu.AddItem(new GUIContent("Duplicate\tCtrl+D"), false, () =>
            { set.activeLayerIndex = index; DuplicateActiveLayer(set); });
            if (!string.IsNullOrEmpty(layer.parentId))
                menu.AddItem(new GUIContent("Remove from Group"), false, () =>
                {
                    if (RemoveLayerFromGroupWithHistory(set, layer))
                    {
                        set.activeLayerIndex = set.layers.IndexOf(layer);
                        SyncActiveLayerSelection(set);
                        ShowWorkspaceStatus($"Removed '{layer.name}' from its group");
                    }
                });
            else menu.AddDisabledItem(new GUIContent("Remove from Group"));
            if (layer.kind == TexturePaintLayerKind.Fill)
                menu.AddItem(new GUIContent("Rasterize to Paint Layer"), false, () =>
                {
                    set.activeLayerIndex = index;
                    if (RasterizeFillLayerWithHistory(set, layer))
                        SyncActiveLayerSelection(set);
                });
            menu.AddSeparator(string.Empty);
            if (layer.layerMask == null)
            {
                menu.AddItem(new GUIContent("Mask/Add Black Mask"), false, () =>
                {
                    set.activeLayerIndex = index;
                    SyncActiveLayerSelection(set);
                    if (AddLayerMaskWithHistory(set, layer, 0f))
                    { layerMaskPaintValue = 1f; EnterLayerMaskMode(set, layer); }
                });
                menu.AddItem(new GUIContent("Mask/Add White Mask"), false, () =>
                {
                    set.activeLayerIndex = index;
                    SyncActiveLayerSelection(set);
                    if (AddLayerMaskWithHistory(set, layer, 1f))
                    { layerMaskPaintValue = 0f; EnterLayerMaskMode(set, layer); }
                });
                menu.AddDisabledItem(new GUIContent("Mask/Remove Mask"));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Mask/Add Black Mask"));
                menu.AddDisabledItem(new GUIContent("Mask/Add White Mask"));
                menu.AddItem(new GUIContent("Mask/Remove Mask"), false, () =>
                {
                    if (RemoveLayerMaskWithHistory(set, layer)) ExitLayerMaskMode();
                });
            }
            menu.AddSeparator("Mask/");
            if (layer.layerMask?.target?.Front != null)
                menu.AddItem(new GUIContent("Mask/Copy Mask"), false,
                    () => CopyLayerMaskToClipboard(set, layer));
            else menu.AddDisabledItem(new GUIContent("Mask/Copy Mask"));
            if (HasLayerMaskClipboard)
                menu.AddItem(new GUIContent("Mask/Paste Mask"), false, () =>
                {
                    bool keepMaskMode = IsLayerMaskMode(set) && set.activeLayerIndex == index;
                    set.activeLayerIndex = index;
                    SyncActiveLayerSelection(set);
                    if (!PasteLayerMaskFromClipboardWithHistory(set, layer)) return;
                    if (keepMaskMode) EnterLayerMaskMode(set, layer);
                    else if (layerMaskMode) ExitLayerMaskMode();
                });
            else menu.AddDisabledItem(new GUIContent("Mask/Paste Mask"));
            menu.AddSeparator(string.Empty);
            if (set.CanMergeLayerDown(index, out string mergeReason)) menu.AddItem(new GUIContent("Merge Down"), false, () =>
            { set.activeLayerIndex = index; MergeActiveLayer(set); });
            else menu.AddDisabledItem(new GUIContent("Merge Down", mergeReason));
            menu.ShowAsContext();
        }

        private void EnterLayerMaskMode(TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer?.layerMask?.target == null) return;
            layerMaskMode = true;
            uvPreviewBefore = false;
            channelSolo = false;
            previewBefore = false;
            if (tool == TexturePaintTool.NormalTouchup) tool = TexturePaintTool.Paint;
            layer.layerMask.NormalizePaintSource();
            layerMaskPaintValue = layer.layerMask.PaintValue;
            paintSource = TexturePaintBrushSource.Color;
            paintSourceTexture = null;
            paintSourceSprite = null;
            paintSourceOverlay = null;
            ShowWorkspaceStatus("Layer Mask mode: paint white to reveal and black to hide");
            ApplyWorkspaceDisplay();
            RepaintAll();
        }

        private void ExitLayerMaskMode()
        {
            if (!layerMaskMode && !soloLayerMask) return;
            layerMaskMode = false;
            soloLayerMask = false;
            ApplyWorkspaceDisplay();
            RepaintAll();
        }

        internal static bool ShouldExitLayerMaskMode(Event current, bool maskMode,
            int activeGeometryFillMode)
        {
            return maskMode && activeGeometryFillMode == 0 && current != null &&
                current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape &&
                !current.control && !current.command && !current.alt;
        }

        private bool TryExitLayerMaskModeFromShortcut(Event current)
        {
            if (!ShouldExitLayerMaskMode(current, layerMaskMode, geometryFillMode)) return false;

            // Preserve the portion already painted if Escape arrives before MouseUp, and release
            // Scene-view capture so the following mouse event cannot continue the old mask stroke.
            if (uvStrokeActive) EndUVStroke(true);
            else if (strokeActive) EndPaint();
            paintGestureActive = false;
            uvColorSamplerArmed = false;
            ReleasePaintControl();

            ExitLayerMaskMode();
            ShowWorkspaceStatus("Exited Layer Mask mode");
            current.Use();
            return true;
        }

        private bool IsLayerMaskMode(TextureSet set)
        {
            return layerMaskMode && set != null &&
                (uint)set.activeLayerIndex < (uint)set.layers.Count &&
                set.layers[set.activeLayerIndex]?.layerMask?.target != null;
        }

        private void BeginLayerRename(TexturePaintLayer layer)
        {
            if (layer == null) return;
            workspaceRenameLayerId = layer.id; workspaceRenameBuffer = layer.name;
            EditorApplication.delayCall += () => { GUI.FocusControl("TexturePaintLayerRename"); TexturePaintDockWindow.RepaintOpenWindows(); };
        }

        private void CommitLayerRename(TexturePaintLayer layer)
        {
            if (layer == null) return;
            if (!string.IsNullOrWhiteSpace(workspaceRenameBuffer) && workspaceRenameBuffer != layer.name)
            {
                RenameLayerWithHistory(FindContainingSet(layer), layer, workspaceRenameBuffer);
            }
            workspaceRenameLayerId = null; workspaceRenameBuffer = null; GUI.FocusControl(null);
        }

        private static string LayerDisplayName(TexturePaintLayer layer)
        {
            string display = layer.kind == TexturePaintLayerKind.Spline ? "Path · " + layer.name :
                layer.kind == TexturePaintLayerKind.Fill ? "Fill · " + layer.name :
                layer.kind == TexturePaintLayerKind.Plugin ? "Plugin · " + layer.name : layer.name;
            string channels = LayerChannelSummary(layer);
            return string.IsNullOrEmpty(channels) ? display : display + ": " + channels;
        }

        private static string LayerSubtitle(TextureSet set, TexturePaintLayer layer)
        {
            TexturePaintLayer parent = FindLayerById(set, layer.parentId);
            string visibility = !layer.visible ? "HIDDEN · " : parent?.visible == false ? "GROUP HIDDEN · " : string.Empty;
            if (layer.kind == TexturePaintLayerKind.Group)
            {
                int children = 0;
                for (int i = 0; set != null && i < set.layers.Count; i++)
                    if (string.Equals(set.layers[i]?.parentId, layer.id, StringComparison.Ordinal)) children++;
                return visibility + $"{children} {(children == 1 ? "layer" : "layers")} · Drop layers on folder";
            }
            string prefix = parent != null ? parent.name + " · " : string.Empty;
            if (layer.kind == TexturePaintLayerKind.Plugin)
            {
                string plugin = string.IsNullOrEmpty(layer.pluginId) ? "Choose plugin" : layer.pluginId;
                string status = layer.pluginStale ? "STALE" : "CACHED";
                if (!string.IsNullOrEmpty(layer.pluginLastError)) status = "ERROR";
                return visibility + prefix + status + " · " + plugin;
            }
            if (!string.IsNullOrEmpty(layer.pluginId))
                return visibility + prefix + layer.pluginId + " · " + layer.pluginVersion;
            if (layer.IsSplineLayer)
                return visibility + prefix + layer.spline.PointCount + " points · " + layer.blendMode;
            return visibility + prefix + Mathf.RoundToInt(layer.opacity * 100f) + "% · " + layer.blendMode;
        }

        private void DrawLayerStackDiagnostic(TextureSet set, bool pathsOnly)
        {
            int authored = 0;
            int effectivelyVisible = 0;
            int selectedContributors = 0;
            int selectedVisibleContributors = 0;
            var authoredChannels = new HashSet<TexturePaintChannel>();
            for (int i = 0; set != null && i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || layer.kind == TexturePaintLayerKind.Group ||
                    (pathsOnly && !layer.IsSplineLayer) || layer.channels.Count == 0) continue;
                authored++;
                bool layerVisible = IsLayerEffectivelyVisible(set, layer);
                if (layerVisible) effectivelyVisible++;
                foreach (var pair in layer.channels)
                {
                    TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(pair.Key, false);
                    if (settings != null && !settings.enabled) continue;
                    authoredChannels.Add(pair.Key);
                    if (pair.Key != selectedChannel) continue;
                    selectedContributors++;
                    if (layerVisible && (settings == null || settings.opacity > 0f))
                        selectedVisibleContributors++;
                }
            }
            if (authored == 0) return;
            if (effectivelyVisible == 0)
            {
                EditorGUILayout.HelpBox("All authored layers are HIDDEN. Change their visibility from OFF to ON to include them in the 2D and 3D composites.",
                    MessageType.Warning);
                return;
            }
            if (selectedContributors > 0 && selectedVisibleContributors == 0)
            {
                EditorGUILayout.HelpBox($"All {selectedChannel} contributors are hidden or have zero opacity.",
                    MessageType.Warning);
                return;
            }
            if (selectedContributors == 0 && authoredChannels.Count > 0)
            {
                EditorGUILayout.HelpBox($"No layer contributes to {selectedChannel}. Existing layer data: {ChannelSetSummary(authoredChannels)}.",
                    MessageType.Info);
                return;
            }
            if (selectedChannel != TexturePaintChannel.Albedo && selectedContributors > 0 &&
                !authoredChannels.Contains(TexturePaintChannel.Albedo))
                EditorGUILayout.HelpBox($"These layers contribute {selectedChannel} material data, not surface color. " +
                    "Use Solo to inspect that channel directly, or author an Albedo layer to change the lit 3D color.",
                    MessageType.Info);
        }

        private static bool IsLayerEffectivelyVisible(TextureSet set, TexturePaintLayer layer)
        {
            if (layer?.visible != true || layer.opacity <= 0f) return false;
            string parentId = layer.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < (set?.layers.Count ?? 0))
            {
                TexturePaintLayer parent = FindLayerById(set, parentId);
                if (parent == null) break;
                if (!parent.visible || parent.opacity <= 0f) return false;
                parentId = parent.parentId;
            }
            return true;
        }

        private static string LayerChannelSummary(TexturePaintLayer layer)
        {
            if (layer == null || layer.kind == TexturePaintLayerKind.Group) return string.Empty;
            var channels = new HashSet<TexturePaintChannel>();
            foreach (var pair in layer.channels)
                channels.Add(pair.Key);
            if (channels.Count == 0)
            {
                if (layer.kind == TexturePaintLayerKind.Fill) channels.Add(layer.fillChannel);
                else if (layer.IsSplineLayer && layer.splineSettings != null)
                    channels.Add(layer.splineSettings.channel);
                else if (layer.paintSettings != null) channels.Add(layer.paintSettings.channel);
            }
            return ChannelSetSummary(channels);
        }

        private static string ChannelSetSummary(HashSet<TexturePaintChannel> channels)
        {
            if (channels == null || channels.Count == 0) return string.Empty;
            var names = new List<string>(channels.Count);
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (channels.Contains(channel)) names.Add(TexturePaintChannelUtility.DisplayName(channel));
            return string.Join(" + ", names);
        }

        private void InitializeWorkspaceUI()
        {
            if (workspaceInitialized) return;
            workspaceInitialized = true;
            workspaceBrushesDirty = true;
            favoriteBrushGuids ??= new List<string>(); recentBrushGuids ??= new List<string>(); brushOrderGuids ??= new List<string>();
            workspaceCollapsedLayerGroupIds ??= new List<string>();
            workspaceCollapsedPropertySectionIds ??= new List<string>();
            workspaceExpandedLayerChannelIds ??= new List<string>();
            assetShelfFolder = string.IsNullOrEmpty(assetShelfFolder) ? "All" : assetShelfFolder;
            EditorApplication.projectChanged += OnWorkspaceProjectChanged;
            ApplyWorkspaceDisplay();
        }

        private void DisposeWorkspaceUI()
        {
            if (!workspaceInitialized) return;
            workspaceInitialized = false;
            EditorApplication.projectChanged -= OnWorkspaceProjectChanged;
            if (controller?.Textures != null)
            {
                for (int i = 0; i < controller.Textures.Sets.Count; i++)
                {
                    TextureSet set = controller.Textures.Sets[i];
                    if (set.surface?.gameObject != null)
                    {
                        set.surface.gameObject.SetActive(true);
                        MeshRenderer renderer = set.surface.gameObject.GetComponent<MeshRenderer>();
                        if (renderer != null) renderer.sharedMaterial = set.previewMaterial;
                    }
                }
            }
            foreach (Material material in workspaceDebugMaterials.Values) if (material != null) DestroyImmediate(material);
            workspaceDebugMaterials.Clear();
            ClearInternalPreviewTextures();
            workspaceUVEdges.Clear(); workspaceUVLineBuffer = null;
        }

        private void OnWorkspaceProjectChanged() => workspaceBrushesDirty = true;

        private void ApplyWorkspaceDisplay()
        {
            if (controller?.Textures == null) return;
            TexturePaintScenePreviewMode previewMode = SceneToolbarPreviewMode;
            bool maskDiagnostic = previewMode == TexturePaintScenePreviewMode.LayerMask &&
                IsLayerMaskMode(ActiveTextureSet);
            bool diagnostic = previewMode == TexturePaintScenePreviewMode.SelectedChannel ||
                maskDiagnostic || IsInternalScenePreview(previewMode);
            bool restorePaintedTextures = !previewBefore && workspacePreviewBeforeApplied;
            Shader shader = diagnostic ? Shader.Find("Unlit/Texture") : null;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
            {
                TextureSet set = controller.Textures.Sets[i];
                if (previewBefore) BindSourceMaterialTextures(set);
                else if (restorePaintedTextures) set.BindPreviewTextures(false);
                ReconstructedSurface surface = set.surface;
                if (surface?.gameObject == null) continue;
                surface.gameObject.SetActive(!isolateSelectedSlots || IsSurfaceSelected(surface));
                MeshRenderer renderer = surface.gameObject.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                if (!diagnostic || shader == null)
                {
                    renderer.sharedMaterial = set.previewMaterial;
                    continue;
                }
                if (!workspaceDebugMaterials.TryGetValue(surface, out Material debug) || debug == null || debug.shader != shader)
                {
                    if (debug != null) DestroyImmediate(debug);
                    debug = new Material(shader) { name = "Overlay Painter Channel Preview", hideFlags = HideFlags.HideAndDontSave };
                    workspaceDebugMaterials[surface] = debug;
                }
                Texture texture;
                if (maskDiagnostic && IsLayerMaskMode(set))
                    texture = set.GetLayerMaskPreview(set.layers[set.activeLayerIndex]);
                else if (IsInternalScenePreview(previewMode))
                    texture = GetInternalScenePreviewTexture(set, previewMode);
                else texture = GetWorkspacePreviewTexture(set, previewBefore);
                debug.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
                debug.color = Color.white;
                renderer.sharedMaterial = debug;
            }
            workspacePreviewBeforeApplied = previewBefore;
        }

        private static bool IsInternalScenePreview(TexturePaintScenePreviewMode mode)
            => mode >= TexturePaintScenePreviewMode.WorldPosition;

        private Texture GetInternalScenePreviewTexture(TextureSet set, TexturePaintScenePreviewMode mode)
        {
            if (set == null) return null;
            switch (mode)
            {
                case TexturePaintScenePreviewMode.WorldNormal:
                    return set.GetProceduralMeshMaps(1024)?.worldNormal;
                case TexturePaintScenePreviewMode.AmbientOcclusion:
                    return set.GetProceduralMeshMaps(1024)?.ambientOcclusion;
                case TexturePaintScenePreviewMode.VertexNormal:
                    return set.tangentSpaceMaps?.vertexNormals;
                case TexturePaintScenePreviewMode.VertexTangent:
                    return set.tangentSpaceMaps?.tangents;
            }

            string key = (string.IsNullOrEmpty(set.persistentId)
                ? "surface-" + set.surface.index : set.persistentId) + ":" + mode;
            if (workspaceInternalPreviewTextures.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;

            Texture2D source;
            switch (mode)
            {
                case TexturePaintScenePreviewMode.WorldPosition:
                    source = set.GetProceduralMeshMaps(1024)?.position;
                    break;
                case TexturePaintScenePreviewMode.SignedCurvature:
                    source = set.GetProceduralMeshMaps(1024)?.curvature;
                    break;
                case TexturePaintScenePreviewMode.Thickness:
                    source = set.GetProceduralMeshMaps(1024)?.thickness;
                    break;
                case TexturePaintScenePreviewMode.SurfaceId:
                    source = set.GetProceduralMeshMaps(1024)?.id;
                    break;
                case TexturePaintScenePreviewMode.UVSeams:
                    source = set.tangentSpaceMaps?.seamLookup;
                    break;
                default:
                    return null;
            }
            if (source == null) return null;

            Color[] sourcePixels = source.GetPixels();
            Color[] previewPixels = new Color[sourcePixels.Length];
            Bounds bounds = set.surface?.collider != null
                ? set.surface.collider.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            Vector3 size = bounds.size;
            size.x = Mathf.Max(size.x, 0.00001f);
            size.y = Mathf.Max(size.y, 0.00001f);
            size.z = Mathf.Max(size.z, 0.00001f);
            float maximumThickness = 0f;
            if (mode == TexturePaintScenePreviewMode.Thickness)
                for (int i = 0; i < sourcePixels.Length; i++)
                    if (sourcePixels[i].a > 0f) maximumThickness = Mathf.Max(maximumThickness, sourcePixels[i].r);
            maximumThickness = Mathf.Max(maximumThickness, 0.00001f);

            for (int i = 0; i < sourcePixels.Length; i++)
                previewPixels[i] = VisualizeInternalPixel(mode, sourcePixels[i], bounds.min, size,
                    maximumThickness);

            Texture2D preview = new Texture2D(source.width, source.height, TextureFormat.RGBA32,
                false, true)
            {
                name = "Overlay Painter " + ScenePreviewLabel(mode) + " Preview",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            preview.SetPixels(previewPixels);
            preview.Apply(false, false);
            workspaceInternalPreviewTextures[key] = preview;
            return preview;
        }

        internal static Color VisualizeInternalPixel(TexturePaintScenePreviewMode mode, Color source,
            Vector3 boundsMinimum, Vector3 boundsSize, float maximumThickness)
        {
            if (source.a <= 0f) return Color.black;
            switch (mode)
            {
                case TexturePaintScenePreviewMode.WorldPosition:
                    return new Color(
                        Mathf.Clamp01((source.r - boundsMinimum.x) / boundsSize.x),
                        Mathf.Clamp01((source.g - boundsMinimum.y) / boundsSize.y),
                        Mathf.Clamp01((source.b - boundsMinimum.z) / boundsSize.z), 1f);
                case TexturePaintScenePreviewMode.SignedCurvature:
                {
                    float signed = Mathf.Clamp(source.r * 2f - 1f, -1f, 1f);
                    Color flat = new Color(0.08f, 0.08f, 0.08f, 1f);
                    return signed < 0f
                        ? Color.Lerp(flat, new Color(1f, 0.16f, 0.06f, 1f), -signed)
                        : Color.Lerp(flat, new Color(0.05f, 0.7f, 1f, 1f), signed);
                }
                case TexturePaintScenePreviewMode.Thickness:
                {
                    float value = Mathf.Clamp01(source.r / Mathf.Max(0.00001f, maximumThickness));
                    return Color.Lerp(new Color(0.02f, 0.02f, 0.08f, 1f),
                        new Color(1f, 0.7f, 0.05f, 1f), Mathf.Sqrt(value));
                }
                case TexturePaintScenePreviewMode.SurfaceId:
                {
                    int hash = unchecked(Mathf.RoundToInt(source.r) * 73856093 ^
                        Mathf.RoundToInt(source.g) * 19349663 ^ Mathf.RoundToInt(source.b) * 83492791);
                    float hue = (hash & 0xFFFF) / 65535f;
                    Color color = Color.HSVToRGB(hue, 0.72f, 0.95f);
                    color.a = 1f;
                    return color;
                }
                case TexturePaintScenePreviewMode.UVSeams:
                    return new Color(1f, 0.65f, 0.05f, 1f);
                default:
                    return source;
            }
        }

        private void ClearInternalPreviewTextures()
        {
            foreach (Texture2D texture in workspaceInternalPreviewTextures.Values)
                if (texture != null) DestroyImmediate(texture);
            workspaceInternalPreviewTextures.Clear();
        }

        private static void BindSourceMaterialTextures(TextureSet set)
        {
            Material material = set?.previewMaterial;
            if (material == null) return;
            foreach (TextureChannelTarget target in set.channels.Values)
            {
                if (target == null || !string.IsNullOrEmpty(target.physicalProperty) ||
                    string.IsNullOrEmpty(target.materialProperty) || !material.HasProperty(target.materialProperty)) continue;
                material.SetTexture(target.materialProperty, target.sourceTexture);
            }
            foreach (TexturePhysicalChannelGroup group in set.physicalChannelGroups.Values)
                if (group != null && !string.IsNullOrEmpty(group.materialProperty) && material.HasProperty(group.materialProperty))
                    material.SetTexture(group.materialProperty, group.source);
        }

        private Texture GetWorkspacePreviewTexture(TextureSet set, bool before,
            bool isolateSelectedGroup = false)
        {
            TextureChannelTarget target = set?.GetChannel(selectedChannel);
            if (target == null) return null;
            if (!before && isolateSelectedGroup)
            {
                RenderTexture groupPreview = set.GetSelectedGroupPreview(selectedChannel);
                if (groupPreview != null) return groupPreview;
            }
            return before ? target.sourceTexture : set.GetVisibleTexture(selectedChannel);
        }

        private void ApplySceneViewDisplay(SceneView sceneView)
        {
            if (sceneView == null) return;
            ApplyWorkspaceDisplay();
        }

        private void SampleSurfaceColor(TextureSet set, Vector2 uv, bool before)
        {
            Texture texture = GetWorkspacePreviewTexture(set, before, true);
            if (set == null || texture == null) return;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
                if (ReferenceEquals(controller.Textures.Sets[i], set)) { selectedSurface = i; break; }
            paintColor = ReadPixel(texture, uv);
            paintSource = TexturePaintBrushSource.Color;
            ShowWorkspaceStatus($"Sampled {selectedChannel} · {paintColor}");
            ApplyWorkspaceDisplay();
        }

        private static Color ReadPixel(Texture source, Vector2 uv)
        {
            if (source is RenderTexture renderTexture) return ReadPixel(renderTexture, uv);
            RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(source, temporary);
                return ReadPixel(temporary, uv);
            }
            finally { RenderTexture.ReleaseTemporary(temporary); }
        }

        private static Color ReadPixel(RenderTexture source, Vector2 uv)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D pixel = null;
            try
            {
                RenderTexture.active = source;
                pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * source.width), 0, source.width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * source.height), 0, source.height - 1);
                pixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false); pixel.Apply(false, false);
                return pixel.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixel != null) DestroyImmediate(pixel);
            }
        }

        private bool HandleWorkspaceShortcuts(Event current, bool sceneViewInput = false, bool uvWindowInput = false)
        {
            if (current == null || current.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return false;
            if (TryExitLayerMaskModeFromShortcut(current)) return true;
            bool hadPathRenderState = TryCapturePathRenderState(out TextureSet pathSetBefore,
                out TexturePaintLayer pathLayerBefore, out TexturePaintSplineSettings pathSettingsBefore,
                out int pathSignatureBefore);
            bool action = current.control || current.command;
            // Scene-view flythrough/orbit/pan owns keyboard input while its view tool is active.
            // Never consume those keys as painter tool changes.
            if (sceneViewInput && !action && (Tools.viewToolActive || current.alt)) return false;
            if (action && current.keyCode == KeyCode.N)
            {
                NewWorkspaceDocument(); current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.O)
            {
                OpenWorkspaceDocumentPicker(); current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.S)
            {
                if (current.shift) SaveWorkspaceAs(); else SaveWorkspace();
                current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.Z)
            {
                if (current.shift) PerformWorkspaceRedo(); else PerformWorkspaceUndo();
                current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.Y)
            {
                PerformWorkspaceRedo(); current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.D)
            {
                DuplicateActiveLayer(ActiveTextureSet); current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.A && TryGetActivePathLayer(ActiveTextureSet, out _))
            {
                SelectAllActivePathPoints(); current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.C && TryGetActivePathLayer(ActiveTextureSet, out _))
            {
                CopyActivePath(); current.Use(); return true;
            }
            if (action && current.keyCode == KeyCode.V && ActiveTextureSet != null &&
                !string.IsNullOrEmpty(splineClipboard))
            {
                PastePathAsNewLayer(); current.Use(); return true;
            }
            if (action) return false;
            if (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace)
            {
                TextureSet set = ActiveTextureSet;
                if (set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count) DeleteLayer(set, set.activeLayerIndex, true);
                current.Use(); return true;
            }
            if (current.keyCode == KeyCode.F2)
            {
                TextureSet set = ActiveTextureSet;
                if (set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count) BeginLayerRename(set.layers[set.activeLayerIndex]);
                current.Use(); return true;
            }
            if (current.keyCode == KeyCode.LeftBracket || current.keyCode == KeyCode.RightBracket)
            {
                float direction = current.keyCode == KeyCode.RightBracket ? 1f : -1f;
                if (current.shift) ActiveBrush.hardness = Mathf.Clamp01(ActiveBrush.hardness + direction * 0.05f);
                else ActiveBrush.size = Mathf.Clamp(ActiveBrush.size * Mathf.Pow(1.12f, direction), 0.001f, 0.5f);
                ShowWorkspaceStatus(current.shift ? $"Hardness {ActiveBrush.hardness:0.00}" : $"Size {ActiveBrush.size:0.000}");
                CommitPathRenderParameterChange(hadPathRenderState, pathSetBefore, pathLayerBefore,
                    pathSettingsBefore, pathSignatureBefore, "Adjust Path Brush");
                current.Use(); return true;
            }
            if (current.keyCode >= KeyCode.Alpha1 && current.keyCode <= KeyCode.Alpha7)
            {
                int channel = (int)current.keyCode - (int)KeyCode.Alpha1;
                SetSelectedChannelAndRefreshSource(
                    (TexturePaintChannel)Mathf.Clamp(channel, 0, 6));
                CommitPathRenderParameterChange(hadPathRenderState, pathSetBefore, pathLayerBefore,
                    pathSettingsBefore, pathSignatureBefore, "Change Path Channel");
                ApplyWorkspaceDisplay(); current.Use(); return true;
            }
            bool freehandToolShortcut = current.keyCode == KeyCode.B || current.keyCode == KeyCode.E ||
                current.keyCode == KeyCode.U || current.keyCode == KeyCode.K || current.keyCode == KeyCode.C ||
                current.keyCode == KeyCode.O || current.keyCode == KeyCode.N || current.keyCode == KeyCode.P;
            if (freehandToolShortcut && !CanStartFreehandPaint(ActiveTextureSet))
            {
                ShowPaintLayerRequiredStatus(ActiveTextureSet);
                current.Use();
                return true;
            }
            switch (current.keyCode)
            {
                case KeyCode.B: tool = TexturePaintTool.Paint; break;
                case KeyCode.E: tool = TexturePaintTool.Erase; break;
                case KeyCode.U: tool = TexturePaintTool.Blur; break;
                case KeyCode.K: tool = TexturePaintTool.Smear; break;
                case KeyCode.C: tool = TexturePaintTool.Clone; break;
                case KeyCode.O: tool = current.shift ? TexturePaintTool.Burn : TexturePaintTool.Dodge; break;
                case KeyCode.N:
                    if (IsLayerMaskMode(ActiveTextureSet))
                    { ShowWorkspaceStatus("Normal touchup is unavailable in Layer Mask mode"); break; }
                    tool = TexturePaintTool.NormalTouchup;
                    SetSelectedChannelAndRefreshSource(TexturePaintChannel.Normal);
                    break;
                case KeyCode.P: tool = TexturePaintTool.Plugin; break;
                case KeyCode.I:
                    if (!uvWindowInput) return false;
                    if (TryGetActivePathLayer(ActiveTextureSet, out _))
                        ShowWorkspaceStatus("Color sampling is disabled while authoring a spline layer");
                    else uvColorSamplerArmed = !uvColorSamplerArmed;
                    break;
                case KeyCode.M: mirrorX = !mirrorX; break;
                case KeyCode.Tab: workspaceShowAssetShelf = !workspaceShowAssetShelf; break;
                case KeyCode.Escape:
                    if (geometryFillMode != 0)
                    {
                        geometryFillMode = 0;
                        ShowWorkspaceStatus("Geometry fill cancelled");
                        break;
                    }
                    if (uvWindowInput) uvColorSamplerArmed = false;
                    if (uvStrokeActive) EndUVStroke(false);
                    else return false;
                    break;
                default: return false;
            }
            CommitPathRenderParameterChange(hadPathRenderState, pathSetBefore, pathLayerBefore,
                pathSettingsBefore, pathSignatureBefore, "Edit Path Parameters");
            ShowWorkspaceStatus(current.keyCode + (tool == TexturePaintTool.Plugin ? " · Plugin Brush" : " · " + tool));
            ApplyWorkspaceDisplay(); current.Use(); return true;
        }

        private bool HandleBrushModifierDrag(Event current, bool sceneViewInput = false)
        {
            if (current == null) return false;
            if (sceneViewInput && ShouldYieldToSceneNavigation(current))
            {
                ReleaseModifierBrushCapture(true);
                return false;
            }
            if (modifierBrushHotControl != 0 && GUIUtility.hotControl != modifierBrushHotControl)
            {
                ReleaseModifierBrushCapture(true);
                return false;
            }
            if (current.type == EventType.MouseDown && current.button == 1 && current.shift && !current.alt)
            {
                modifierBrushDrag = true; modifierBrushStartMouse = current.mousePosition;
                modifierBrushStartSize = ActiveBrush.size; modifierBrushStartHardness = ActiveBrush.hardness;
                modifierPathEditSet = null;
                modifierPathUndoStarted = false;
                TextureSet activeSet = ActiveTextureSet;
                if (TryGetActivePathLayer(activeSet, out _)) modifierPathEditSet = activeSet;
                modifierBrushHotControl = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = modifierBrushHotControl;
                current.Use(); return true;
            }
            if (modifierBrushDrag && current.type == EventType.MouseDrag)
            {
                if (modifierPathEditSet != null && !modifierPathUndoStarted)
                {
                    BeginLightweightPathUndo(modifierPathEditSet, "Adjust Path Brush");
                    modifierPathUndoStarted = true;
                }
                Vector2 delta = current.mousePosition - modifierBrushStartMouse;
                ActiveBrush.size = Mathf.Clamp(modifierBrushStartSize * Mathf.Exp(delta.x * 0.012f), 0.001f, 0.5f);
                ActiveBrush.hardness = Mathf.Clamp01(modifierBrushStartHardness - delta.y / 180f);
                if (modifierPathEditSet != null) CompleteLightweightPathEdit(modifierPathEditSet, true);
                ShowWorkspaceStatus($"Size {ActiveBrush.size:0.000} · Hardness {ActiveBrush.hardness:0.00}");
                current.Use(); return true;
            }
            if (modifierBrushDrag && (current.rawType == EventType.MouseUp || current.type == EventType.MouseLeaveWindow))
            {
                ReleaseModifierBrushCapture(true);
                current.Use(); return true;
            }
            return false;
        }

        private void ReleaseModifierBrushCapture(bool completePathEdit)
        {
            int ownedControl = modifierBrushHotControl;
            modifierBrushHotControl = 0;
            if (ownedControl != 0 && GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            if (completePathEdit && modifierPathEditSet != null && modifierPathUndoStarted)
            {
                if (pendingPathEdit != null && pendingPathEdit.deferred) CommitPendingPathEdit();
                if (splineReapplyPending) ScheduleSplineReapply(true);
            }
            modifierBrushDrag = false;
            modifierPathEditSet = null;
            modifierPathUndoStarted = false;
        }

        private void PerformWorkspaceUndo()
        {
            if (CanUndoLightweight) UndoLightweight();
            else if (controller.Painting.History.CanUndo) controller.Painting.Undo();
            else if (controller.Plugins.CanUndo) controller.Plugins.Undo();
            ApplyWorkspaceDisplay(); RepaintAll();
        }

        internal void PerformUndoFromExternalWindow() => PerformWorkspaceUndo();

        private void PerformWorkspaceRedo()
        {
            if (CanRedoLightweight) RedoLightweight();
            else if (controller.Painting.History.CanRedo) controller.Painting.Redo();
            else if (controller.Plugins.CanRedo) controller.Plugins.Redo();
            ApplyWorkspaceDisplay(); RepaintAll();
        }

        internal void PerformRedoFromExternalWindow() => PerformWorkspaceRedo();

        private void SaveWorkspace()
        {
            if (IsPersistenceActive) return;
            if (IsDocumentTemporary) SaveWorkspaceAs();
            else
            {
                BeginPersistence(PersistenceIntent.ProjectSave, AssetDatabase.GetAssetPath(document));
                ShowWorkspaceStatus("Saving project document…");
            }
        }

        private void OpenExportWindow()
        {
            TextureSet set = ActiveTextureSet;
            if (set == null) return;
            TexturePaintExportWindow.Open(controller, avatar, set, BuildState(), document);
        }

        private void FrameActiveTarget()
        {
            TexturePaintLogicalTarget target = ActiveLogicalTarget;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (target == null || sceneView == null) return;

            bool hasBounds = false;
            Bounds targetBounds = default;
            var visited = new HashSet<ReconstructedSurface>();
            for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
            {
                TexturePaintLogicalTargetMember member = target.members[memberIndex];
                for (int surfaceIndex = 0; surfaceIndex < member.surfaces.Count; surfaceIndex++)
                {
                    ReconstructedSurface surface = member.surfaces[surfaceIndex];
                    if (surface == null || !visited.Add(surface)) continue;
                    Renderer renderer = surface.gameObject != null ? surface.gameObject.GetComponent<Renderer>() : null;
                    Bounds surfaceBounds;
                    if (renderer != null) surfaceBounds = renderer.bounds;
                    else if (surface.collider != null) surfaceBounds = surface.collider.bounds;
                    else continue;
                    if (!hasBounds) { targetBounds = surfaceBounds; hasBounds = true; }
                    else targetBounds.Encapsulate(surfaceBounds);
                }
            }
            if (!hasBounds) return;
            sceneView.Frame(targetBounds, false);
            sceneView.Repaint();
        }

        private void ShowViewMenu(Rect anchor)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Solo Selected Channel"), channelSolo, () =>
            {
                SetScenePreviewMode(channelSolo
                    ? TexturePaintScenePreviewMode.Material
                    : TexturePaintScenePreviewMode.SelectedChannel);
            });
            menu.AddItem(new GUIContent("Before in 3D"), previewBefore, () =>
            {
                SetScenePreviewMode(previewBefore
                    ? TexturePaintScenePreviewMode.Material
                    : TexturePaintScenePreviewMode.OriginalMaterial);
            });
            menu.AddItem(new GUIContent("Isolate Selected Slots"), isolateSelectedSlots, () =>
            {
                isolateSelectedSlots = !isolateSelectedSlots;
                RefreshWorkspaceView();
            });
            menu.DropDown(anchor);
        }

        internal TexturePaintScenePreviewMode SceneToolbarPreviewMode
        {
            get
            {
                if (IsInternalScenePreview(scenePreviewMode)) return scenePreviewMode;
                if (previewBefore) return TexturePaintScenePreviewMode.OriginalMaterial;
                if (channelSolo) return TexturePaintScenePreviewMode.SelectedChannel;
                if (soloLayerMask && IsLayerMaskMode(ActiveTextureSet))
                    return TexturePaintScenePreviewMode.LayerMask;
                return TexturePaintScenePreviewMode.Material;
            }
        }

        internal static string ScenePreviewLabel(TexturePaintScenePreviewMode mode)
        {
            switch (mode)
            {
                case TexturePaintScenePreviewMode.SelectedChannel: return "Selected Channel";
                case TexturePaintScenePreviewMode.OriginalMaterial: return "Original Material";
                case TexturePaintScenePreviewMode.LayerMask: return "Layer Mask";
                case TexturePaintScenePreviewMode.WorldPosition: return "World Position";
                case TexturePaintScenePreviewMode.WorldNormal: return "World Normal";
                case TexturePaintScenePreviewMode.SignedCurvature: return "Signed Curvature";
                case TexturePaintScenePreviewMode.AmbientOcclusion: return "Ambient Occlusion";
                case TexturePaintScenePreviewMode.Thickness: return "Thickness";
                case TexturePaintScenePreviewMode.SurfaceId: return "Surface / Island ID";
                case TexturePaintScenePreviewMode.VertexNormal: return "Vertex Normal";
                case TexturePaintScenePreviewMode.VertexTangent: return "Vertex Tangent";
                case TexturePaintScenePreviewMode.UVSeams: return "UV Seams";
                default: return "Material";
            }
        }

        internal static string ScenePreviewTooltip(TexturePaintScenePreviewMode mode)
        {
            switch (mode)
            {
                case TexturePaintScenePreviewMode.WorldPosition:
                    return "World position normalized to each target's bounds (RGB = XYZ).";
                case TexturePaintScenePreviewMode.WorldNormal:
                    return "World-space normal map used by mesh-aware plugins.";
                case TexturePaintScenePreviewMode.SignedCurvature:
                    return "Signed curvature used by edge and cavity effects (red = concave, blue = convex).";
                case TexturePaintScenePreviewMode.AmbientOcclusion:
                    return "Ambient accessibility map used by dirt and cavity effects.";
                case TexturePaintScenePreviewMode.Thickness:
                    return "Mesh thickness in Unity world units, normalized per target for display.";
                case TexturePaintScenePreviewMode.SurfaceId:
                    return "Deterministic colors for mesh triangles, surfaces, and UV islands.";
                case TexturePaintScenePreviewMode.VertexNormal:
                    return "UV-space interpolated vertex normals used by the paint system.";
                case TexturePaintScenePreviewMode.VertexTangent:
                    return "UV-space interpolated vertex tangents used by the paint system.";
                case TexturePaintScenePreviewMode.UVSeams:
                    return "UV seam pixels used for cross-island stroke continuity.";
                case TexturePaintScenePreviewMode.LayerMask:
                    return "The active layer's mask.";
                case TexturePaintScenePreviewMode.SelectedChannel:
                    return "Only the currently selected material channel.";
                case TexturePaintScenePreviewMode.OriginalMaterial:
                    return "Source material textures before Overlay Painter edits.";
                default:
                    return "Composited material preview.";
            }
        }

        internal bool SceneToolbarHasLayerMask => IsLayerMaskMode(ActiveTextureSet);
        internal bool SceneToolbarReady => controller?.Textures != null && controller.Textures.Sets.Count > 0;
        internal bool SceneToolbarToolsEnabled => SceneToolbarReady && !IsPersistenceActive;
        internal bool SceneToolbarCanUseGeometryFill => SceneToolbarToolsEnabled &&
            CanStartFreehandPaint(ActiveTextureSet);
        internal bool SceneToolbarCanActivatePath => SceneToolbarToolsEnabled && ActiveTextureSet != null;
        internal bool SceneToolbarPathActive
        {
            get
            {
                TextureSet set = ActiveTextureSet;
                return SceneToolbarReady && set != null && !IsLayerMaskMode(set) &&
                    IsActiveSplineAuthoringLayer(set, set.activeLayerIndex);
            }
        }
        internal TexturePaintPathEditMode SceneToolbarPathEditMode => pathEditMode;
        internal bool SceneToolbarPathAutoUpdate => pathAutoUpdate;
        internal bool SceneToolbarPathClosed => SceneToolbarPathActive &&
            TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer closedLayer) &&
            closedLayer.spline.closed;
        internal bool SceneToolbarPathHasSelection => SceneToolbarPathActive &&
            TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer selectionLayer) &&
            selectedSplinePoint >= 0 && selectedSplinePoint < selectionLayer.spline.PointCount;
        // Edit modes constrain direct viewport gestures, not explicit toolbar commands. A user
        // must be able to insert/delete/reverse/close or straighten a path without temporarily
        // switching away from Move or Adjust mode.
        internal bool SceneToolbarPathCanTopology => SceneToolbarPathActive;
        internal bool SceneToolbarPathCanAdjust => SceneToolbarPathActive;
        internal bool SceneToolbarMirrorX { get => mirrorX; set { mirrorX = value; RepaintAll(); } }
        internal bool SceneToolbarPaintBackfaces { get => paintBackfaces; set { paintBackfaces = value; RepaintAll(); } }
        internal bool SceneToolbarIsolate
        {
            get => isolateSelectedSlots;
            set { isolateSelectedSlots = value; RefreshWorkspaceView(); }
        }
        internal TexturePaintTool SceneToolbarTool => tool;
        internal int SceneToolbarGeometryFillMode => geometryFillMode;
        internal TexturePaintChannel SceneToolbarChannel => selectedChannel;
        internal string SceneToolbarTargetName => ActiveLogicalTarget?.displayName ?? "Target";
        internal bool SceneToolbarCanUndo => SceneToolbarReady &&
            (CanUndoLightweight || controller.Painting.History.CanUndo || controller.Plugins.CanUndo);
        internal bool SceneToolbarCanRedo => SceneToolbarReady &&
            (CanRedoLightweight || controller.Painting.History.CanRedo || controller.Plugins.CanRedo);

        internal static void RequestSceneToolbarShutdown()
        {
            TexturePaintStageWindow stage = ActiveStage ??
                StageUtility.GetCurrentStage() as TexturePaintStageWindow;
            if (stage == null)
            {
                TexturePaintSceneOverlayVisibility.HideAll();
                return;
            }
            stage.RequestCloseStage();
        }

        internal List<TexturePaintChannel> GetSceneToolbarChannels()
        {
            var result = new List<TexturePaintChannel>();
            TextureSet set = ActiveTextureSet;
            if (set == null) return result;
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (set.channels.ContainsKey(channel)) result.Add(channel);
            return result;
        }

        internal IReadOnlyList<TexturePaintLogicalTarget> GetSceneToolbarTargets()
            => controller?.LogicalTargets?.Targets;

        internal bool IsSceneToolbarTargetSelected(TexturePaintLogicalTarget target)
            => target != null && string.Equals(selectedTargetId, target.id, StringComparison.Ordinal);

        internal void SetSceneToolbarTarget(TexturePaintLogicalTarget target)
        {
            if (target == null) return;
            SelectLogicalTarget(target, controller.Textures.Sets, true);
            if (ActiveTextureSet?.GetChannel(selectedChannel) == null)
            {
                List<TexturePaintChannel> channels = GetSceneToolbarChannels();
                if (channels.Count > 0) SetSelectedChannelAndRefreshSource(channels[0]);
            }
            RepaintAll();
        }

        internal bool CanSelectSceneToolbarTool(TexturePaintTool value)
            => SceneToolbarToolsEnabled && CanStartFreehandPaint(ActiveTextureSet) &&
                (value != TexturePaintTool.Plugin || controller.Plugins.Brushes.Count > 0) &&
                (value != TexturePaintTool.NormalTouchup ||
                    (!IsLayerMaskMode(ActiveTextureSet) &&
                     ActiveTextureSet.GetChannel(TexturePaintChannel.Normal) != null));

        internal void SetSceneToolbarTool(TexturePaintTool value)
        {
            if (!CanSelectSceneToolbarTool(value))
            {
                ShowPaintLayerRequiredStatus(ActiveTextureSet);
                RepaintAll();
                return;
            }
            geometryFillMode = 0;
            tool = value;
            if (tool == TexturePaintTool.NormalTouchup)
                SetSelectedChannelAndRefreshSource(TexturePaintChannel.Normal);
            ShowWorkspaceStatus("3D tool: " + value);
            RepaintAll();
        }

        internal void SetSceneToolbarGeometryFillMode(int mode)
        {
            mode = Mathf.Clamp(mode, 0, 2);
            if (mode != 0 && (!SceneToolbarToolsEnabled || !CanStartFreehandPaint(ActiveTextureSet)))
            {
                ShowPaintLayerRequiredStatus(ActiveTextureSet);
                return;
            }
            geometryFillMode = mode;
            if (geometryFillMode == 1)
                ShowWorkspaceStatus("Polygon Fill armed: click a polygon; Esc cancels");
            else if (geometryFillMode == 2)
                ShowWorkspaceStatus("UV Island Fill armed: click an island; Esc cancels");
            else ShowWorkspaceStatus("Geometry fill canceled");
            SceneView.RepaintAll();
        }

        internal void ActivateSceneToolbarPathTool()
        {
            if (!SceneToolbarCanActivatePath) return;
            if (!TryGetActivePathLayer(ActiveTextureSet, out _))
                CreateSplineLayerWithUndo(ActiveTextureSet);
            splineMode = TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer) &&
                layer.spline?.worldSpace == true;
            ShowWorkspaceStatus(splineMode ? "3D path editing active" : "Select a 3D path layer to edit it");
            RepaintAll();
        }

        internal void SetSceneToolbarPathEditMode(TexturePaintPathEditMode mode)
        {
            if (!SceneToolbarPathActive || !Enum.IsDefined(typeof(TexturePaintPathEditMode), mode)) return;
            ReleaseSplineHandleCapture(true, false);
            EndUVWindowInteraction();
            pathEditMode = mode;
            CaptureSplineSettings(ActiveTextureSet.layers[ActiveTextureSet.activeLayerIndex]);
            MarkDocumentDirty();
            ShowWorkspaceStatus("Path edit mode: " + mode);
            RepaintAll();
        }

        internal void SetSceneToolbarPathAutoUpdate(bool enabled)
        {
            if (!SceneToolbarPathActive || pathAutoUpdate == enabled) return;
            pathAutoUpdate = enabled;
            TextureSet set = ActiveTextureSet;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            CaptureSplineSettings(layer);
            if (enabled) RequestSplineReapply(set, false);
            else if (ReferenceEquals(pendingSplineSet, set) && ReferenceEquals(pendingSplineLayer, layer))
                CancelScheduledSplineReapply();
            MarkDocumentDirty();
            ShowWorkspaceStatus(enabled
                ? "Path Auto Update enabled" : "Path Auto Update disabled · press Update to rebuild");
            RepaintAll();
        }

        internal void SceneToolbarUpdatePath()
        {
            if (!SceneToolbarPathActive ||
                !TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer)) return;
            if (pendingPathEdit != null && pendingPathEdit.deferred) CommitPendingPathEdit();
            spline = layer.spline;
            ApplySpline();
            ShowWorkspaceStatus("Path updated");
            RepaintAll();
        }

        internal void SceneToolbarSetPathClosed(bool closed)
        {
            if (!SceneToolbarPathCanTopology ||
                !TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer) ||
                layer.spline.closed == closed) return;
            TextureSet set = ActiveTextureSet;
            spline = layer.spline;
            BeginLightweightPathUndo(set, closed ? "Close Path" : "Open Path");
            spline.closed = closed;
            CompleteLightweightPathEdit(set, false);
        }

        internal void SceneToolbarInsertPathPoint()
        {
            if (!SceneToolbarPathCanTopology || !SceneToolbarPathHasSelection ||
                !TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer)) return;
            TextureSet set = ActiveTextureSet;
            spline = layer.spline;
            BeginLightweightPathUndo(set, "Insert Spline Point");
            selectedSplinePoint = spline.InsertPointAfter(selectedSplinePoint);
            UpdateSplineAnchorFromCurrentDomain(set, spline, selectedSplinePoint);
            SelectSingleSplinePoint(selectedSplinePoint);
            CompleteLightweightPathEdit(set, false);
        }

        internal void SceneToolbarDeletePathPoints()
        {
            if (SceneToolbarPathCanTopology && SceneToolbarPathHasSelection)
                DeleteSelectedSplinePoints(ActiveTextureSet);
        }

        internal void SceneToolbarReversePath()
        {
            if (!SceneToolbarPathCanTopology ||
                !TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer) ||
                layer.spline.PointCount < 2) return;
            TextureSet set = ActiveTextureSet;
            spline = layer.spline;
            BeginLightweightPathUndo(set, "Reverse Spline");
            spline.Reverse();
            CompleteLightweightPathEdit(set, false);
        }

        internal void SceneToolbarStraightenPathHandles()
        {
            if (SceneToolbarPathCanAdjust && SceneToolbarPathHasSelection)
                StraightenSelectedSplinePoints(ActiveTextureSet);
        }

        internal void SceneToolbarSelectAllPathPoints()
        {
            if (SceneToolbarPathActive) SelectAllActivePathPoints();
        }

        internal void SceneToolbarCopyPath()
        {
            if (SceneToolbarPathActive) CopyActivePath();
        }

        internal void SceneToolbarPastePath()
        {
            if (ActiveTextureSet != null && !string.IsNullOrEmpty(splineClipboard))
                PastePathAsNewLayer();
        }

        internal bool SceneToolbarCanPastePath => ActiveTextureSet != null &&
            !string.IsNullOrEmpty(splineClipboard);

        internal void SceneToolbarClearPath()
        {
            if (!SceneToolbarPathCanTopology ||
                !TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer layer) ||
                layer.spline.PointCount == 0) return;
            TextureSet set = ActiveTextureSet;
            spline = layer.spline;
            BeginLightweightPathUndo(set, "Clear Path");
            spline.Clear();
            SelectSingleSplinePoint(-1);
            CompleteLightweightPathEdit(set, false);
        }

        internal void SetSceneToolbarChannel(TexturePaintChannel channel)
        {
            if (ActiveTextureSet?.GetChannel(channel) == null) return;
            if (tool == TexturePaintTool.NormalTouchup && channel != TexturePaintChannel.Normal)
                tool = TexturePaintTool.Paint;
            SetSelectedChannelAndRefreshSource(channel);
            RefreshWorkspaceView();
        }

        internal void SetScenePreviewMode(TexturePaintScenePreviewMode mode)
        {
            if (mode == TexturePaintScenePreviewMode.LayerMask && !SceneToolbarHasLayerMask) return;
            if (IsInternalScenePreview(mode))
            {
                try
                {
                    for (int i = 0; i < controller.Textures.Sets.Count; i++)
                        GetInternalScenePreviewTexture(controller.Textures.Sets[i], mode);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    ShowWorkspaceStatus("Could not build " + ScenePreviewLabel(mode) + " preview");
                    return;
                }
            }
            scenePreviewMode = mode;
            channelSolo = mode == TexturePaintScenePreviewMode.SelectedChannel;
            previewBefore = mode == TexturePaintScenePreviewMode.OriginalMaterial;
            soloLayerMask = mode == TexturePaintScenePreviewMode.LayerMask;
            RefreshWorkspaceView();
        }

        internal void RebuildSceneToolbarMeshMaps()
        {
            if (!SceneToolbarReady) return;
            ClearInternalPreviewTextures();
            try
            {
                for (int i = 0; i < controller.Textures.Sets.Count; i++)
                {
                    TextureSet set = controller.Textures.Sets[i];
                    set.proceduralMeshMaps?.Dispose();
                    set.proceduralMeshMaps = null;
                }
                for (int i = 0; i < controller.Textures.Sets.Count; i++)
                    controller.Textures.Sets[i].GetProceduralMeshMaps(1024);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowWorkspaceStatus("Plugin mesh-map rebuild failed");
                RefreshWorkspaceView();
                return;
            }
            RefreshWorkspaceView();
            ShowWorkspaceStatus("Plugin mesh maps rebuilt at working resolution");
        }

        internal void SceneToolbarUndo() => PerformWorkspaceUndo();
        internal void SceneToolbarRedo() => PerformWorkspaceRedo();
        internal void SceneToolbarFrameTarget() => FrameActiveTarget();
        internal void SceneToolbarShowShortcutHelp() => ShowShortcutHelp();

        private void RefreshWorkspaceView()
        {
            ApplyWorkspaceDisplay();
            RepaintAll();
        }

        private void ShowLayoutMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Targets"), workspaceShowTargets, () =>
            { workspaceShowTargets = !workspaceShowTargets; RepaintAll(); });
            menu.AddItem(new GUIContent("Layers / Paths"), workspaceShowLayers, () =>
            { workspaceShowLayers = !workspaceShowLayers; RepaintAll(); });
            menu.AddItem(new GUIContent("Properties"), workspaceShowProperties, () =>
            { workspaceShowProperties = !workspaceShowProperties; RepaintAll(); });
            menu.AddItem(new GUIContent("Open 2D Canvas"), false, TexturePaintUVWindow.ShowDockable);
            menu.AddItem(new GUIContent("Open Brush Controls"), false,
                TexturePaintBrushWindow.ShowDockable);
            menu.AddSeparator(string.Empty);
            if (UMASettings.TexturePaintCompactView)
                menu.AddItem(new GUIContent("Reset Compact View"), false,
                    TexturePaintWorkspaceLayout.ResetCompactView);
            else
                menu.AddDisabledItem(new GUIContent("Reset Compact View"));
            menu.AddItem(new GUIContent("Reset Workspace"), false, () =>
            {
                workspaceLeftWidth = 238f; workspaceRightWidth = 318f; workspaceShelfHeight = 178f;
                workspaceShowTargets = workspaceShowLayers = workspaceShowProperties = workspaceShowAssetShelf = true;
                workspacePanelTab = 1; workspaceUVPan = Vector2.zero; workspaceUVZoom = 1f; RepaintAll();
            });
            menu.ShowAsContext();
        }

        private void ShowShortcutHelp()
        {
            EditorUtility.DisplayDialog("Overlay Painter Shortcuts",
                "B Paint    E Erase    U Blur    K Smear    C Clone\n" +
                "O Dodge    Shift+O Burn    N Normal Touchup    P Plugin\n" +
                "I Sample Color in the 2D canvas    M Mirror\n" +
                "1–7 Channels    [ ] Size    Shift+[ ] Hardness\n" +
                "Shift+Right Drag Size/Hardness    Tab Asset Shelf\n" +
                "Escape Exit Layer Mask mode (or cancel Geometry Fill)\n" +
                "Scene navigation always keeps its keyboard input\n" +
                "Ctrl/Cmd+Z Undo    Ctrl/Cmd+Shift+Z Redo\n" +
                "Ctrl/Cmd+D Duplicate Layer    F2 Rename    Delete Remove\n" +
                "Ctrl/Cmd+S Save", "Close");
        }

        private void ShowWorkspaceStatus(string message)
        {
            workspaceStatus = message; workspaceStatusUntil = EditorApplication.timeSinceStartup + 3d;
            TexturePaintDockWindow.RepaintOpenWindows();
            TexturePaintUVWindow.RepaintOpenWindows();
            TexturePaintBrushWindow.RepaintOpenWindows();
        }

        private void HandleSplitter(Rect rect, int id, ref float value, bool invertY, float minimum, float maximum, bool invertX = false)
        {
            EditorGUIUtility.AddCursorRect(rect, invertY ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                splitterDrag = id; splitterStartMouse = current.mousePosition; splitterStartValue = value;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); current.Use();
            }
            if (splitterDrag == id && current.type == EventType.MouseDrag)
            {
                float delta = invertY ? splitterStartMouse.y - current.mousePosition.y : current.mousePosition.x - splitterStartMouse.x;
                if (invertX) delta = -delta;
                value = Mathf.Clamp(splitterStartValue + delta, minimum, maximum); current.Use();
            }
            if (splitterDrag == id && current.rawType == EventType.MouseUp)
            {
                splitterDrag = 0; GUIUtility.hotControl = 0; current.Use();
            }
        }

        private void DrawPropertySection(string title, Action body)
        {
            GUILayout.Space(3f);
            string sectionId = "properties." + title.ToLowerInvariant().Replace(' ', '-');
            bool expanded = IsPropertySectionExpanded(sectionId);
            bool nextExpanded = EditorGUILayout.Foldout(expanded, title, true,
                WorkspaceStyles.SectionFoldout);
            if (nextExpanded != expanded) SetPropertySectionExpanded(sectionId, nextExpanded);
            if (!nextExpanded) return;
            GUILayout.BeginVertical(WorkspaceStyles.PropertyBox);
            body?.Invoke();
            GUILayout.EndVertical();
        }

        private bool DrawPropertySubsectionFoldout(string sectionId, string title)
        {
            bool expanded = IsPropertySectionExpanded(sectionId);
            bool nextExpanded = EditorGUILayout.Foldout(expanded, title, true,
                WorkspaceStyles.SubsectionFoldout);
            if (nextExpanded != expanded) SetPropertySectionExpanded(sectionId, nextExpanded);
            return nextExpanded;
        }

        private bool IsPropertySectionExpanded(string sectionId)
        {
            workspaceCollapsedPropertySectionIds ??= new List<string>();
            return !workspaceCollapsedPropertySectionIds.Contains(sectionId);
        }

        private void SetPropertySectionExpanded(string sectionId, bool expanded)
        {
            workspaceCollapsedPropertySectionIds ??= new List<string>();
            bool changed = expanded
                ? workspaceCollapsedPropertySectionIds.Remove(sectionId)
                : !workspaceCollapsedPropertySectionIds.Contains(sectionId);
            if (!expanded && changed) workspaceCollapsedPropertySectionIds.Add(sectionId);
            if (changed)
            {
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintBrushWindow.RepaintOpenWindows();
            }
        }

        private static void DrawRegionHeader(string title, string tooltip)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(new GUIContent(title, tooltip), WorkspaceStyles.RegionHeader);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static void DrawTextureThumbnail(Rect rect, Texture texture, Color fallback)
        {
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.09f, 0.09f, 1f));
            if (texture != null) GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), texture, ScaleMode.ScaleToFit, false);
            else EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), fallback);
        }

        private static void DrawBrushThumbnail(Rect rect, BrushPreset preset, Texture texture)
        {
            DrawCheckerboard(rect, 9f);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            else
            {
                Color color = new Color(0.72f, 0.72f, 0.72f, 0.9f);
                if (preset.shape == BrushPreset.Shape.Circle)
                {
                    Handles.color = color; Handles.DrawSolidDisc(rect.center, Vector3.forward, Mathf.Min(rect.width, rect.height) * 0.31f);
                }
                else EditorGUI.DrawRect(new Rect(rect.center.x - 18f, rect.center.y - 18f, 36f, 36f), color);
            }
        }

        private static void DrawCheckerboard(Rect rect, float cell)
        {
            if (Event.current.type != EventType.Repaint) return;
            Texture2D checker = WorkspaceStyles.Checker;
            GUI.DrawTextureWithTexCoords(rect, checker,
                new Rect(0f, 0f, rect.width / Mathf.Max(2f, cell * 2f), rect.height / Mathf.Max(2f, cell * 2f)));
        }

        private static bool MatchesSearch(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string[] terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < terms.Length; i++)
                if (value?.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private static Color TexturePaintStoreFallback(TexturePaintChannel channel)
        {
            switch (channel)
            {
                case TexturePaintChannel.Normal: return new Color(0.5f, 0.5f, 1f, 1f);
                case TexturePaintChannel.NormalControl: return new Color(0.5f, 0.5f, 0.5f, 1f);
                case TexturePaintChannel.Albedo: return Color.white;
                case TexturePaintChannel.AmbientOcclusion: return Color.white;
                case TexturePaintChannel.Roughness: return Color.white;
                case TexturePaintChannel.DetailMask: return Color.white;
                case TexturePaintChannel.SkinColorMask: return Color.clear;
                default: return Color.black;
            }
        }

        private TextureSet ActiveTextureSet => controller?.Textures != null && controller.Textures.Sets.Count > 0
            ? controller.Textures.Sets[Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1)] : null;

        private static Rect Shrink(Rect rect, float amount) => new Rect(rect.x + amount, rect.y + amount,
            Mathf.Max(0f, rect.width - amount * 2f), Mathf.Max(0f, rect.height - amount * 2f));

        // UMA.Editors.GUIHelper cannot be referenced here because UMA_Core_Editor already
        // references this editor assembly. Keep the CharacterBaseEditor drawing contract
        // local so the channel UI can share its appearance without an assembly cycle.
        private static class GUIHelper
        {
            public static bool FoldOutBar(bool foldout, string content, GUIContent tooltip)
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
                GUILayout.Space(10f);
                GUIContent label = tooltip == null
                    ? new GUIContent(content)
                    : new GUIContent(content, tooltip.tooltip);
                bool nextFoldout = EditorGUILayout.Foldout(foldout, label, true);
                GUILayout.EndHorizontal();
                return nextFoldout;
            }

            public static void BeginVerticalPadded(float padding, Color backgroundColor,
                GUIStyle style = null)
            {
                style ??= EditorStyles.textField;
                Color previousColor = GUI.color;
                GUI.color = backgroundColor;
                GUILayout.BeginHorizontal(style);
                GUI.color = previousColor;
                GUILayout.Space(padding);
                GUILayout.BeginVertical();
                GUILayout.Space(padding);
            }

            public static void EndVerticalPadded(float padding)
            {
                GUILayout.Space(padding);
                GUILayout.EndVertical();
                GUILayout.Space(padding);
                GUILayout.EndHorizontal();
            }
        }

        private static class WorkspaceStyles
        {
            private static Texture2D checker;
            public static Texture2D Checker
            {
                get
                {
                    if (checker != null) return checker;
                    checker = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                    {
                        name = "Overlay Painter Checker", hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point
                    };
                    Color one = new Color(0.16f, 0.16f, 0.16f, 1f), two = new Color(0.22f, 0.22f, 0.22f, 1f);
                    checker.SetPixels(new[] { one, two, two, one }); checker.Apply(false, true);
                    return checker;
                }
            }
            public static readonly Color BorderColor = EditorGUIUtility.isProSkin
                ? new Color(0.08f, 0.08f, 0.08f, 1f) : new Color(0.55f, 0.55f, 0.55f, 1f);
            public static readonly Color CanvasColor = EditorGUIUtility.isProSkin
                ? new Color(0.075f, 0.075f, 0.085f, 1f) : new Color(0.32f, 0.32f, 0.34f, 1f);
            public static readonly GUIStyle Region = new GUIStyle("ProjectBrowserPreviewBg") { border = new RectOffset(1, 1, 1, 1) };
            public static readonly GUIStyle Canvas = new GUIStyle(Region);
            public static readonly GUIStyle RegionHeader = new GUIStyle(EditorStyles.miniBoldLabel)
            { alignment = TextAnchor.MiddleLeft };
            public static readonly GUIStyle SectionFoldout = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold, fontSize = EditorStyles.miniBoldLabel.fontSize,
                margin = new RectOffset(5, 3, 3, 2),
                normal = { textColor = new Color(0.55f, 0.75f, 1f) },
                onNormal = { textColor = new Color(0.55f, 0.75f, 1f) },
                focused = { textColor = new Color(0.55f, 0.75f, 1f) },
                onFocused = { textColor = new Color(0.55f, 0.75f, 1f) }
            };
            public static readonly GUIStyle SubsectionFoldout = new GUIStyle(EditorStyles.foldout)
            { fontStyle = FontStyle.Bold };
            public static readonly GUIStyle PropertyBox = new GUIStyle(EditorStyles.helpBox)
            { padding = new RectOffset(7, 7, 5, 6), margin = new RectOffset(4, 4, 0, 3) };
            public static readonly GUIStyle Row = new GUIStyle("RL Background") { padding = new RectOffset(3, 3, 2, 2) };
            public static readonly GUIStyle SelectedRow = new GUIStyle("SelectionRect") { padding = new RectOffset(3, 3, 2, 2) };
            public static readonly GUIStyle DragHandle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            public static readonly GUIStyle Asset = new GUIStyle("ProjectBrowserGridLabel")
            { alignment = TextAnchor.LowerCenter, padding = new RectOffset(3, 3, 3, 3) };
            public static readonly GUIStyle AssetSelected = new GUIStyle("ProjectBrowserGridLabel")
            { alignment = TextAnchor.LowerCenter, padding = new RectOffset(3, 3, 3, 3), normal = { background = Texture2D.whiteTexture } };
            public static readonly GUIStyle AssetName = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            public static readonly Color AssetSelectionColor = new Color(0.24f, 0.56f, 0.92f, 1f);
            public static readonly GUIStyle Star = new GUIStyle(EditorStyles.miniButton)
            { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 13 };
            public static readonly GUIStyle CanvasBadge = new GUIStyle(EditorStyles.toolbarButton)
            { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            public static readonly GUIStyle CanvasHint = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.72f) } };
            public static readonly GUIStyle CanvasHintShadow = new GUIStyle(CanvasHint)
            { normal = { textColor = new Color(0f, 0f, 0f, 0.9f) } };
            public static readonly GUIStyle CenterMessage = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 12 };
        }
    }

    [Overlay(typeof(SceneView), TexturePaintSceneToolPaletteOverlay.Title,
        TexturePaintSceneOverlayVisibility.DefaultDisplay)]
    public sealed class TexturePaintSceneToolPaletteOverlay : ToolbarOverlay
    {
        internal const string Title = "Overlay Painter Toolbar";
        internal static readonly string[] ElementIds =
        {
            TexturePaintScenePaintToolToggle.Id,
            TexturePaintSceneEraseToolToggle.Id,
            TexturePaintSceneBlurToolToggle.Id,
            TexturePaintSceneSmearToolToggle.Id,
            TexturePaintSceneCloneToolToggle.Id,
            TexturePaintSceneDodgeToolToggle.Id,
            TexturePaintSceneBurnToolToggle.Id,
            TexturePaintSceneNormalToolToggle.Id,
            TexturePaintScenePluginToolToggle.Id,
            TexturePaintScenePolygonFillToggle.Id,
            TexturePaintSceneIslandFillToggle.Id,
            TexturePaintScenePathToolToggle.Id,
            TexturePaintSceneToolHelpButton.Id
        };

        private static readonly HashSet<TexturePaintSceneToolPaletteOverlay> Instances =
            new HashSet<TexturePaintSceneToolPaletteOverlay>();
        private bool contextWasActive;

        public TexturePaintSceneToolPaletteOverlay() : base(ElementIds)
        {
        }

        public override void OnCreated()
        {
            base.OnCreated();
            Instances.Add(this);
            contextWasActive = IsContextActive();
            displayed = contextWasActive;
            EditorApplication.update += UpdateContextVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateContextVisibility;
            Instances.Remove(this);
            base.OnWillBeDestroyed();
        }

        internal static void HideAllInstances()
        {
            foreach (TexturePaintSceneToolPaletteOverlay overlay in Instances)
            {
                if (overlay == null) continue;
                overlay.contextWasActive = false;
                overlay.displayed = false;
            }
        }

        internal static void RefreshAllInstances()
        {
            foreach (TexturePaintSceneToolPaletteOverlay overlay in Instances)
                overlay?.UpdateContextVisibility();
        }

        internal static bool AddToolIcon(VisualElement element, int iconIndex)
        {
            Sprite sprite = TexturePaintStageWindow.GetToolRailIcon(iconIndex);
            if (element == null || sprite == null) return false;
            var image = new UnityEngine.UIElements.Image
            {
                sprite = sprite,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            image.style.width = 18f;
            image.style.height = 18f;
            image.style.alignSelf = Align.Center;
            image.style.flexShrink = 0f;
            element.style.minWidth = 26f;
            element.style.justifyContent = Justify.Center;
            element.Add(image);
            return true;
        }

        private void UpdateContextVisibility()
        {
            bool active = IsContextActive();
            if (!TexturePaintSceneOverlayVisibility.NeedsDisplayRefresh(
                    active, contextWasActive, displayed)) return;
            contextWasActive = active;
            displayed = active;
        }

        private bool IsContextActive()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            return stage != null && stage.SceneToolbarReady &&
                ReferenceEquals(StageUtility.GetCurrentStage(), stage) &&
                TexturePaintSceneOverlayVisibility.ShouldDisplayOn(containerWindow);
        }
    }

    public abstract class TexturePaintSceneBrushToolToggle : EditorToolbarToggle
    {
        private readonly TexturePaintTool tool;

        protected TexturePaintSceneBrushToolToggle(TexturePaintTool tool, int iconIndex,
            string fallbackText, string toolTip)
        {
            this.tool = tool;
            text = fallbackText;
            tooltip = toolTip;
            if (TexturePaintSceneToolPaletteOverlay.AddToolIcon(this, iconIndex)) text = null;
            RegisterCallback<ChangeEvent<bool>>(OnChanged);
            schedule.Execute(UpdateDisplay).Every(150L);
            UpdateDisplay();
        }

        private void OnChanged(ChangeEvent<bool> change)
        {
            if (!change.newValue) return;
            TexturePaintStageWindow.ActiveStage?.SetSceneToolbarTool(tool);
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.CanSelectSceneToolbarTool(tool) == true);
            SetValueWithoutNotify(stage != null && stage.SceneToolbarGeometryFillMode == 0 &&
                stage.SceneToolbarTool == tool);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintScenePaintToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Paint";
        public TexturePaintScenePaintToolToggle() : base(TexturePaintTool.Paint, 0, "P", "Paint (B)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneEraseToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Erase";
        public TexturePaintSceneEraseToolToggle() : base(TexturePaintTool.Erase, 1, "E", "Erase (E)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneBlurToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Blur";
        public TexturePaintSceneBlurToolToggle() : base(TexturePaintTool.Blur, 2, "U", "Blur (U)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneSmearToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Smear";
        public TexturePaintSceneSmearToolToggle() : base(TexturePaintTool.Smear, 3, "S", "Smear (K)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneCloneToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Clone";
        public TexturePaintSceneCloneToolToggle() : base(TexturePaintTool.Clone, 4, "C",
            "Clone (C); Ctrl-click sets the source") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneDodgeToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Dodge";
        public TexturePaintSceneDodgeToolToggle() : base(TexturePaintTool.Dodge, 5, "D", "Dodge (O)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneBurnToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Burn";
        public TexturePaintSceneBurnToolToggle() : base(TexturePaintTool.Burn, 6, "B", "Burn (Shift+O)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneNormalToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/NormalTouchup";
        public TexturePaintSceneNormalToolToggle() : base(TexturePaintTool.NormalTouchup, 7, "N",
            "Normal touch-up (N); unavailable in Layer Mask mode") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintScenePluginToolToggle : TexturePaintSceneBrushToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Plugin";
        public TexturePaintScenePluginToolToggle() : base(TexturePaintTool.Plugin, 8, "Plug",
            "Plugin brush (P)") { }
    }

    public abstract class TexturePaintSceneGeometryToolToggle : EditorToolbarToggle
    {
        private readonly int mode;

        protected TexturePaintSceneGeometryToolToggle(int mode, int iconIndex, string fallbackText,
            string toolTip)
        {
            this.mode = mode;
            text = fallbackText;
            tooltip = toolTip;
            if (TexturePaintSceneToolPaletteOverlay.AddToolIcon(this, iconIndex)) text = null;
            RegisterCallback<ChangeEvent<bool>>(OnChanged);
            schedule.Execute(UpdateDisplay).Every(150L);
            UpdateDisplay();
        }

        private void OnChanged(ChangeEvent<bool> change)
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            if (stage == null) return;
            if (change.newValue) stage.SetSceneToolbarGeometryFillMode(mode);
            else if (stage.SceneToolbarGeometryFillMode == mode)
                stage.SetSceneToolbarGeometryFillMode(0);
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarCanUseGeometryFill == true);
            SetValueWithoutNotify(stage?.SceneToolbarGeometryFillMode == mode);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintScenePolygonFillToggle : TexturePaintSceneGeometryToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/PolygonFill";
        public TexturePaintScenePolygonFillToggle() : base(1, 12, "Poly",
            "Polygon Fill: click a mesh polygon to fill it with the current paint color or mask value") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneIslandFillToggle : TexturePaintSceneGeometryToolToggle
    {
        public const string Id = "UMA/OverlayPainterTools/IslandFill";
        public TexturePaintSceneIslandFillToggle() : base(2, 11, "Island",
            "UV Island Fill: click a polygon to fill its complete UV island") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintScenePathToolToggle : EditorToolbarToggle
    {
        public const string Id = "UMA/OverlayPainterTools/Path";

        public TexturePaintScenePathToolToggle()
        {
            text = "Path";
            tooltip = "Create or edit a spline/path layer";
            if (TexturePaintSceneToolPaletteOverlay.AddToolIcon(this, 9)) text = null;
            RegisterCallback<ChangeEvent<bool>>(change =>
            {
                if (change.newValue) TexturePaintStageWindow.ActiveStage?.ActivateSceneToolbarPathTool();
            });
            schedule.Execute(UpdateDisplay).Every(150L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarCanActivatePath == true);
            SetValueWithoutNotify(stage?.SceneToolbarPathActive == true);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneToolHelpButton : EditorToolbarButton
    {
        public const string Id = "UMA/OverlayPainterTools/Help";

        public TexturePaintSceneToolHelpButton()
        {
            text = "?";
            tooltip = "Shortcut and painting-workflow reference";
            if (TexturePaintSceneToolPaletteOverlay.AddToolIcon(this, 10)) text = null;
            clicked += () => TexturePaintStageWindow.ActiveStage?.SceneToolbarShowShortcutHelp();
            schedule.Execute(() => SetEnabled(TexturePaintStageWindow.ActiveStage != null)).Every(250L);
        }
    }

    [Overlay(typeof(SceneView), "Overlay Painter Path",
        TexturePaintSceneOverlayVisibility.DefaultDisplay)]
    public sealed class TexturePaintPathToolbarOverlay : ToolbarOverlay
    {
        private static readonly HashSet<TexturePaintPathToolbarOverlay> Instances =
            new HashSet<TexturePaintPathToolbarOverlay>();
        private bool contextWasActive;

        public TexturePaintPathToolbarOverlay() : base(
            TexturePaintPathModeDropdown.Id,
            TexturePaintPathAutoUpdateToggle.Id,
            TexturePaintPathUpdateButton.Id,
            TexturePaintPathClosedToggle.Id,
            TexturePaintPathActionsDropdown.Id)
        {
        }

        public override void OnCreated()
        {
            base.OnCreated();
            Instances.Add(this);
            contextWasActive = IsContextActive();
            displayed = contextWasActive;
            EditorApplication.update += UpdateContextVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateContextVisibility;
            Instances.Remove(this);
            base.OnWillBeDestroyed();
        }

        internal static void HideAllInstances()
        {
            foreach (TexturePaintPathToolbarOverlay overlay in Instances)
            {
                if (overlay == null) continue;
                overlay.contextWasActive = false;
                overlay.displayed = false;
            }
        }

        internal static void RefreshAllInstances()
        {
            foreach (TexturePaintPathToolbarOverlay overlay in Instances)
                overlay?.UpdateContextVisibility();
        }

        private void UpdateContextVisibility()
        {
            bool active = IsContextActive();
            if (!TexturePaintSceneOverlayVisibility.NeedsDisplayRefresh(
                    active, contextWasActive, displayed)) return;
            contextWasActive = active;
            displayed = active;
        }

        private bool IsContextActive()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            return stage?.SceneToolbarPathActive == true &&
                ReferenceEquals(StageUtility.GetCurrentStage(), stage) &&
                TexturePaintSceneOverlayVisibility.ShouldDisplayOn(containerWindow);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintPathModeDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/OverlayPainterPath/EditMode";

        public TexturePaintPathModeDropdown()
        {
            clicked += ShowMenu;
            schedule.Execute(UpdateDisplay).Every(150L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarPathActive == true);
            text = stage?.SceneToolbarPathEditMode.ToString() ?? "Standard";
            tooltip = stage?.SceneToolbarPathEditMode switch
            {
                TexturePaintPathEditMode.Move => "Move path anchors only; curve and width handles are hidden.",
                TexturePaintPathEditMode.Adjust => "Adjust curve and width handles without moving anchors.",
                _ => "Add, remove, move, and adjust path points."
            };
        }

        private void ShowMenu()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            var menu = new GenericMenu();
            AddMode(TexturePaintPathEditMode.Standard,
                "Standard · Add, Move & Adjust");
            AddMode(TexturePaintPathEditMode.Move,
                "Move · Anchors Only");
            AddMode(TexturePaintPathEditMode.Adjust,
                "Adjust · Curves & Width");
            menu.DropDown(worldBound);

            void AddMode(TexturePaintPathEditMode mode, string label)
            {
                if (stage?.SceneToolbarPathActive == true)
                    menu.AddItem(new GUIContent(label), stage.SceneToolbarPathEditMode == mode,
                        () => stage.SetSceneToolbarPathEditMode(mode));
                else menu.AddDisabledItem(new GUIContent(label));
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintPathAutoUpdateToggle : EditorToolbarToggle
    {
        public const string Id = "UMA/OverlayPainterPath/AutoUpdate";

        public TexturePaintPathAutoUpdateToggle()
        {
            text = "Auto";
            tooltip = "Automatically rebuild the path after edits. Disable for manual Update.";
            RegisterCallback<ChangeEvent<bool>>(change =>
            {
                TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
                if (stage?.SceneToolbarPathActive == true)
                    stage.SetSceneToolbarPathAutoUpdate(change.newValue);
            });
            schedule.Execute(UpdateDisplay).Every(150L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarPathActive == true);
            SetValueWithoutNotify(stage?.SceneToolbarPathAutoUpdate == true);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintPathUpdateButton : EditorToolbarButton
    {
        public const string Id = "UMA/OverlayPainterPath/Update";

        public TexturePaintPathUpdateButton()
        {
            text = "Update";
            tooltip = "Rebuild the path and all affected channels now.";
            clicked += () => TexturePaintStageWindow.ActiveStage?.SceneToolbarUpdatePath();
            schedule.Execute(() => SetEnabled(
                TexturePaintStageWindow.ActiveStage?.SceneToolbarPathActive == true)).Every(150L);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintPathClosedToggle : EditorToolbarToggle
    {
        public const string Id = "UMA/OverlayPainterPath/Closed";

        public TexturePaintPathClosedToggle()
        {
            text = "Closed";
            tooltip = "Connect the last path point to the first in any path edit mode.";
            RegisterCallback<ChangeEvent<bool>>(change =>
                TexturePaintStageWindow.ActiveStage?.SceneToolbarSetPathClosed(change.newValue));
            schedule.Execute(UpdateDisplay).Every(150L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarPathCanTopology == true);
            SetValueWithoutNotify(stage?.SceneToolbarPathClosed == true);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintPathActionsDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/OverlayPainterPath/Actions";

        public TexturePaintPathActionsDropdown()
        {
            text = "Actions";
            tooltip = "Path point and topology actions.";
            clicked += ShowMenu;
            schedule.Execute(() => SetEnabled(
                TexturePaintStageWindow.ActiveStage?.SceneToolbarPathActive == true)).Every(150L);
        }

        private void ShowMenu()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            var menu = new GenericMenu();
            bool topology = stage?.SceneToolbarPathCanTopology == true;
            bool selection = stage?.SceneToolbarPathHasSelection == true;
            bool adjust = stage?.SceneToolbarPathCanAdjust == true;
            Add("Insert Point After Selected", topology && selection,
                () => stage.SceneToolbarInsertPathPoint());
            Add("Delete Selected Points", topology && selection,
                () => stage.SceneToolbarDeletePathPoints());
            Add("Reverse Path", topology, () => stage.SceneToolbarReversePath());
            Add("Clear Path", topology, () => stage.SceneToolbarClearPath());
            menu.AddSeparator(string.Empty);
            Add("Straighten Selected Handles", adjust && selection,
                () => stage.SceneToolbarStraightenPathHandles());
            Add("Select All Points", stage?.SceneToolbarPathActive == true,
                () => stage.SceneToolbarSelectAllPathPoints());
            menu.AddSeparator(string.Empty);
            Add("Copy Path", stage?.SceneToolbarPathActive == true,
                () => stage.SceneToolbarCopyPath());
            Add("Paste as New Path Layer", stage?.SceneToolbarCanPastePath == true,
                () => stage.SceneToolbarPastePath());
            menu.DropDown(worldBound);

            void Add(string label, bool enabled, GenericMenu.MenuFunction action)
            {
                if (enabled) menu.AddItem(new GUIContent(label), false, action);
                else menu.AddDisabledItem(new GUIContent(label));
            }
        }
    }

    [Overlay(typeof(SceneView), "Overlay Painter 3D",
        TexturePaintSceneOverlayVisibility.DefaultDisplay)]
    public sealed class TexturePaintSceneToolbarOverlay : ToolbarOverlay
    {
        private static readonly HashSet<TexturePaintSceneToolbarOverlay> Instances =
            new HashSet<TexturePaintSceneToolbarOverlay>();
        private bool contextWasActive;

        public TexturePaintSceneToolbarOverlay() : base(
            TexturePaintSceneToolDropdown.Id,
            TexturePaintSceneTargetDropdown.Id,
            TexturePaintSceneChannelDropdown.Id,
            TexturePaintScenePreviewDropdown.Id,
            TexturePaintSceneMirrorToggle.Id,
            TexturePaintSceneBackfaceToggle.Id,
            TexturePaintSceneIsolateToggle.Id,
            TexturePaintSceneFrameButton.Id,
            TexturePaintSceneUndoButton.Id,
            TexturePaintSceneRedoButton.Id,
            TexturePaintSceneShutdownButton.Id)
        {
        }

        public override void OnCreated()
        {
            base.OnCreated();
            Instances.Add(this);
            contextWasActive = IsContextActive();
            displayed = contextWasActive;
            EditorApplication.update += UpdateContextVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateContextVisibility;
            Instances.Remove(this);
            base.OnWillBeDestroyed();
        }

        internal static void HideAllInstances()
        {
            foreach (TexturePaintSceneToolbarOverlay overlay in Instances)
            {
                if (overlay == null) continue;
                overlay.contextWasActive = false;
                overlay.displayed = false;
            }
        }

        internal static void RefreshAllInstances()
        {
            foreach (TexturePaintSceneToolbarOverlay overlay in Instances)
                overlay?.UpdateContextVisibility();
        }

        private void UpdateContextVisibility()
        {
            bool active = IsContextActive();
            if (!TexturePaintSceneOverlayVisibility.NeedsDisplayRefresh(
                    active, contextWasActive, displayed)) return;
            contextWasActive = active;
            displayed = active;
        }

        private bool IsContextActive()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            return stage != null && stage.SceneToolbarReady &&
                ReferenceEquals(StageUtility.GetCurrentStage(), stage) &&
                TexturePaintSceneOverlayVisibility.ShouldDisplayOn(containerWindow);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneTargetDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/OverlayPainter3D/Target";

        public TexturePaintSceneTargetDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_Avatar Icon").image as Texture2D;
            clicked += ShowMenu;
            schedule.Execute(UpdateDisplay).Every(250L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarReady == true);
            text = stage?.SceneToolbarTargetName ?? "Target";
            tooltip = "Select and frame a logical slot or UDIM paint target.";
        }

        private void ShowMenu()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            var menu = new GenericMenu();
            IReadOnlyList<TexturePaintLogicalTarget> targets = stage?.GetSceneToolbarTargets();
            if (targets == null || targets.Count == 0)
                menu.AddDisabledItem(new GUIContent("No reconstructed paint targets"));
            else
                for (int i = 0; i < targets.Count; i++)
                {
                    TexturePaintLogicalTarget target = targets[i];
                    string suffix = target.isUdim ? $" ({target.members.Count} UDIM tiles)" : string.Empty;
                    menu.AddItem(new GUIContent(target.displayName + suffix),
                        stage.IsSceneToolbarTargetSelected(target), () => stage.SetSceneToolbarTarget(target));
                }
            menu.DropDown(worldBound);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneToolDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/OverlayPainter3D/Tool";

        public TexturePaintSceneToolDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_EditCollider").image as Texture2D;
            clicked += ShowMenu;
            schedule.Execute(UpdateDisplay).Every(250L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarReady == true);
            if (stage == null) { text = "Tool"; return; }
            text = stage.SceneToolbarGeometryFillMode == 1 ? "Polygon Fill" :
                stage.SceneToolbarGeometryFillMode == 2 ? "Island Fill" : stage.SceneToolbarTool.ToString();
            tooltip = "Select the active 3D paint, geometry-fill, or path tool.";
        }

        private void ShowMenu()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            var menu = new GenericMenu();
            if (stage?.SceneToolbarReady != true)
            {
                menu.AddDisabledItem(new GUIContent("Open Overlay Painter"));
                menu.DropDown(worldBound);
                return;
            }
            foreach (TexturePaintTool value in Enum.GetValues(typeof(TexturePaintTool)))
            {
                TexturePaintTool captured = value;
                GUIContent content = new GUIContent("Brushes/" + ToolLabel(value));
                if (stage.CanSelectSceneToolbarTool(value))
                    menu.AddItem(content, stage.SceneToolbarGeometryFillMode == 0 && stage.SceneToolbarTool == value,
                        () => stage.SetSceneToolbarTool(captured));
                else menu.AddDisabledItem(content, false);
            }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Geometry/Fill Polygon"), stage.SceneToolbarGeometryFillMode == 1,
                () => stage.SetSceneToolbarGeometryFillMode(1));
            menu.AddItem(new GUIContent("Geometry/Fill UV Island"), stage.SceneToolbarGeometryFillMode == 2,
                () => stage.SetSceneToolbarGeometryFillMode(2));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Path/Create or Edit 3D Path"), false,
                stage.ActivateSceneToolbarPathTool);
            menu.DropDown(worldBound);
        }

        private static string ToolLabel(TexturePaintTool tool)
        {
            switch (tool)
            {
                case TexturePaintTool.NormalTouchup: return "Normal Touch-up";
                case TexturePaintTool.Plugin: return "Plugin Brush";
                default: return tool.ToString();
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneChannelDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/OverlayPainter3D/Channel";

        public TexturePaintSceneChannelDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_Texture Icon").image as Texture2D;
            clicked += ShowMenu;
            schedule.Execute(UpdateDisplay).Every(250L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarReady == true);
            text = stage == null ? "Channel" : TexturePaintChannelUtility.DisplayName(stage.SceneToolbarChannel);
            tooltip = "Select an existing channel on the active paint target.";
        }

        private void ShowMenu()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            var menu = new GenericMenu();
            List<TexturePaintChannel> channels = stage?.GetSceneToolbarChannels();
            if (channels == null || channels.Count == 0)
                menu.AddDisabledItem(new GUIContent("No channels on active target"));
            else
                for (int i = 0; i < channels.Count; i++)
                {
                    TexturePaintChannel channel = channels[i];
                    menu.AddItem(new GUIContent(TexturePaintChannelUtility.DisplayName(channel)),
                        stage.SceneToolbarChannel == channel, () => stage.SetSceneToolbarChannel(channel));
                }
            menu.DropDown(worldBound);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintScenePreviewDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/OverlayPainter3D/Preview";

        public TexturePaintScenePreviewDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_SceneViewVisibility").image as Texture2D;
            clicked += ShowMenu;
            schedule.Execute(UpdateDisplay).Every(250L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarReady == true);
            TexturePaintScenePreviewMode mode = stage?.SceneToolbarPreviewMode ??
                TexturePaintScenePreviewMode.Material;
            text = TexturePaintStageWindow.ScenePreviewLabel(mode);
            tooltip = TexturePaintStageWindow.ScenePreviewTooltip(mode);
        }

        private void ShowMenu()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            var menu = new GenericMenu();
            if (stage?.SceneToolbarReady != true)
            {
                menu.AddDisabledItem(new GUIContent("Open Overlay Painter"));
                menu.DropDown(worldBound);
                return;
            }
            AddPreview(menu, stage, "Material/Composited Material", TexturePaintScenePreviewMode.Material);
            AddPreview(menu, stage, "Material/Selected Channel", TexturePaintScenePreviewMode.SelectedChannel);
            AddPreview(menu, stage, "Material/Original Material", TexturePaintScenePreviewMode.OriginalMaterial);
            if (stage.SceneToolbarHasLayerMask)
                AddPreview(menu, stage, "Material/Active Layer Mask", TexturePaintScenePreviewMode.LayerMask);
            else menu.AddDisabledItem(new GUIContent("Material/Active Layer Mask"));
            menu.AddSeparator(string.Empty);
            AddPreview(menu, stage, "Plugin Mesh Maps/World Position", TexturePaintScenePreviewMode.WorldPosition);
            AddPreview(menu, stage, "Plugin Mesh Maps/World Normal", TexturePaintScenePreviewMode.WorldNormal);
            AddPreview(menu, stage, "Plugin Mesh Maps/Signed Curvature", TexturePaintScenePreviewMode.SignedCurvature);
            AddPreview(menu, stage, "Plugin Mesh Maps/Ambient Occlusion", TexturePaintScenePreviewMode.AmbientOcclusion);
            AddPreview(menu, stage, "Plugin Mesh Maps/Thickness", TexturePaintScenePreviewMode.Thickness);
            AddPreview(menu, stage, "Plugin Mesh Maps/Surface - Island ID", TexturePaintScenePreviewMode.SurfaceId);
            menu.AddSeparator("Plugin Mesh Maps/");
            menu.AddItem(new GUIContent("Plugin Mesh Maps/Rebuild Cached Maps"), false,
                stage.RebuildSceneToolbarMeshMaps);
            menu.AddSeparator(string.Empty);
            AddPreview(menu, stage, "Paint Internals/Vertex Normal", TexturePaintScenePreviewMode.VertexNormal);
            AddPreview(menu, stage, "Paint Internals/Vertex Tangent", TexturePaintScenePreviewMode.VertexTangent);
            AddPreview(menu, stage, "Paint Internals/UV Seams", TexturePaintScenePreviewMode.UVSeams);
            menu.DropDown(worldBound);
        }

        private static void AddPreview(GenericMenu menu, TexturePaintStageWindow stage, string label,
            TexturePaintScenePreviewMode mode)
        {
            menu.AddItem(new GUIContent(label, TexturePaintStageWindow.ScenePreviewTooltip(mode)),
                stage.SceneToolbarPreviewMode == mode, () => stage.SetScenePreviewMode(mode));
        }
    }

    public abstract class TexturePaintSceneSyncedToggle : EditorToolbarToggle
    {
        protected TexturePaintSceneSyncedToggle(string iconName, string toolTip, string label = null)
        {
            if (!string.IsNullOrEmpty(iconName))
                icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
            text = label;
            tooltip = toolTip;
            RegisterCallback<ChangeEvent<bool>>(OnChanged);
            schedule.Execute(UpdateDisplay).Every(250L);
            UpdateDisplay();
        }

        protected abstract bool Read(TexturePaintStageWindow stage);
        protected abstract void Write(TexturePaintStageWindow stage, bool next);

        private void OnChanged(ChangeEvent<bool> change)
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            if (stage?.SceneToolbarReady == true) Write(stage, change.newValue);
        }

        private void UpdateDisplay()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarReady == true);
            SetValueWithoutNotify(stage != null && Read(stage));
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneMirrorToggle : TexturePaintSceneSyncedToggle
    {
        public const string Id = "UMA/OverlayPainter3D/Mirror";
        public TexturePaintSceneMirrorToggle() : base("d_Mirror", "Mirror strokes across global X") { }
        protected override bool Read(TexturePaintStageWindow stage) => stage.SceneToolbarMirrorX;
        protected override void Write(TexturePaintStageWindow stage, bool next) => stage.SceneToolbarMirrorX = next;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneBackfaceToggle : TexturePaintSceneSyncedToggle
    {
        public const string Id = "UMA/OverlayPainter3D/Backfaces";
        public TexturePaintSceneBackfaceToggle() : base(null, "Paint back-facing polygons", "Back") { }
        protected override bool Read(TexturePaintStageWindow stage) => stage.SceneToolbarPaintBackfaces;
        protected override void Write(TexturePaintStageWindow stage, bool next) => stage.SceneToolbarPaintBackfaces = next;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneIsolateToggle : TexturePaintSceneSyncedToggle
    {
        public const string Id = "UMA/OverlayPainter3D/Isolate";
        public TexturePaintSceneIsolateToggle() : base("d_SceneViewVisibility", "Isolate the selected paint target") { }
        protected override bool Read(TexturePaintStageWindow stage) => stage.SceneToolbarIsolate;
        protected override void Write(TexturePaintStageWindow stage, bool next) => stage.SceneToolbarIsolate = next;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneFrameButton : EditorToolbarButton
    {
        public const string Id = "UMA/OverlayPainter3D/Frame";
        public TexturePaintSceneFrameButton()
        {
            icon = EditorGUIUtility.IconContent("d_ViewToolZoom").image as Texture2D;
            tooltip = "Frame the active paint target";
            clicked += () => TexturePaintStageWindow.ActiveStage?.SceneToolbarFrameTarget();
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneUndoButton : EditorToolbarButton
    {
        public const string Id = "UMA/OverlayPainter3D/Undo";
        public TexturePaintSceneUndoButton()
        {
            text = "↶";
            tooltip = "Undo the last Overlay Painter operation";
            clicked += () => TexturePaintStageWindow.ActiveStage?.SceneToolbarUndo();
            schedule.Execute(() => SetEnabled(TexturePaintStageWindow.ActiveStage?.SceneToolbarCanUndo == true)).Every(250L);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneRedoButton : EditorToolbarButton
    {
        public const string Id = "UMA/OverlayPainter3D/Redo";
        public TexturePaintSceneRedoButton()
        {
            text = "↷";
            tooltip = "Redo the last Overlay Painter operation";
            clicked += () => TexturePaintStageWindow.ActiveStage?.SceneToolbarRedo();
            schedule.Execute(() => SetEnabled(TexturePaintStageWindow.ActiveStage?.SceneToolbarCanRedo == true)).Every(250L);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneShutdownButton : EditorToolbarButton
    {
        public const string Id = "UMA/OverlayPainter3D/Shutdown";
        public const string Label = "Shutdown Overlay Painter";

        public TexturePaintSceneShutdownButton()
        {
            text = Label;
            tooltip = "Save, discard, or cancel as needed, then leave the Overlay Painter stage.";
            style.minWidth = 170f;
            style.flexShrink = 0f;
            clicked += TexturePaintStageWindow.RequestSceneToolbarShutdown;
            schedule.Execute(() => SetEnabled(TexturePaintStageWindow.ActiveStage != null)).Every(250L);
        }
    }

    internal static class TexturePaintSceneOverlayVisibility
    {
        internal const bool DefaultDisplay = false;

        internal static bool NeedsDisplayRefresh(bool active, bool contextWasActive,
            bool displayed)
        {
            return active != contextWasActive || displayed != active;
        }

        internal static bool ShouldDisplayOn(EditorWindow window)
        {
            return TexturePaintWorkspaceLayout.ShouldShowSceneOverlays(window);
        }

        internal static void RefreshAll()
        {
            TexturePaintSceneToolPaletteOverlay.RefreshAllInstances();
            TexturePaintPathToolbarOverlay.RefreshAllInstances();
            TexturePaintSceneToolbarOverlay.RefreshAllInstances();
            SceneView.RepaintAll();
        }

        internal static void HideAll()
        {
            TexturePaintSceneToolPaletteOverlay.HideAllInstances();
            TexturePaintPathToolbarOverlay.HideAllInstances();
            TexturePaintSceneToolbarOverlay.HideAllInstances();
            SceneView.RepaintAll();
        }
    }
}
