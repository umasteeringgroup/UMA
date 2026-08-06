using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private const float WorkspaceToolbarHeight = 24f;
        private const float WorkspaceToolRailWidth = 48f;
        private const float WorkspaceSplitterSize = 4f;
        private const string LayerDragKey = "UMA.TexturePaint.LayerIndex";
        private const string ToolRailIconPath = "Assets/UMA/OverlayPainter/Editor/Icons/TexturePaintIcons.png";
        private const int ToolRailIconCount = 11;
        private const int DocumentPickerControlId = 0x5450444F;
        private const string DocumentAssetFolder = "Assets";
        private const float LayerRowExtendedControlsMinimumWidth = 600f;
        private static Sprite[] toolRailIcons;

        [SerializeField] private float workspaceLeftWidth = 238f;
        [SerializeField] private float workspaceRightWidth = 318f;
        [SerializeField] private float workspaceShelfHeight = 178f;
        [SerializeField] private bool workspaceShowToolRail = true;
        [SerializeField] private bool workspaceShowTargets = true;
        [SerializeField] private bool workspaceShowLayers = true;
        [SerializeField] private bool workspaceShowProperties = true;
        [SerializeField] private bool workspaceShowAssetShelf = true;
        [SerializeField] private bool workspaceShowUV = true;
        [SerializeField] private int workspaceLeftTab;
        [SerializeField] private int workspaceRightTab;
        [SerializeField] private Vector2 workspaceUVPan;
        [SerializeField] private float workspaceUVZoom = 1f;
        [SerializeField] private bool channelSolo;
        [SerializeField] private bool previewBefore;
        [SerializeField] private bool uvPreviewBefore;
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

        [NonSerialized] private Vector2 workspaceTargetScroll;
        [NonSerialized] private Vector2 workspaceLayerScroll;
        [NonSerialized] private Vector2 workspacePropertyScroll;
        [NonSerialized] private Vector2 workspaceShelfScroll;
        [NonSerialized] private string workspaceTargetSearch;
        [NonSerialized] private string workspaceRenameLayerId;
        [NonSerialized] private string workspaceRenameBuffer;
        [NonSerialized] private string workspaceRenameBrush;
        [NonSerialized] private int uvPreferredTriangle = -1;
        [NonSerialized] private bool uvStrokeActive;
        [NonSerialized] private bool uvPanning;
        [NonSerialized] private int uvDraggingSplinePoint = -1;
        [NonSerialized] private Vector2 uvPanStartMouse;
        [NonSerialized] private Vector2 uvPanStart;
        [NonSerialized] private bool modifierBrushDrag;
        [NonSerialized] private Vector2 modifierBrushStartMouse;
        [NonSerialized] private float modifierBrushStartSize;
        [NonSerialized] private float modifierBrushStartHardness;
        [NonSerialized] private TextureSet modifierPathEditSet;
        [NonSerialized] private bool modifierPathUndoStarted;
        [NonSerialized] private bool documentPickerOpen;
        [NonSerialized] private bool uvColorSamplerArmed;
        [NonSerialized] private bool workspacePreviewBeforeApplied;
        [NonSerialized] private string workspaceStatus;
        [NonSerialized] private double workspaceStatusUntil;
        [NonSerialized] private int splitterDrag;
        [NonSerialized] private Vector2 splitterStartMouse;
        [NonSerialized] private float splitterStartValue;
        [NonSerialized] private readonly Dictionary<ReconstructedSurface, Material> workspaceDebugMaterials =
            new Dictionary<ReconstructedSurface, Material>();
        [NonSerialized] private readonly Dictionary<Mesh, Vector2[]> workspaceUVEdges =
            new Dictionary<Mesh, Vector2[]>();
        [NonSerialized] private readonly List<BrushShelfItem> workspaceBrushes = new List<BrushShelfItem>();
        [NonSerialized] private bool workspaceBrushesDirty = true;
        [NonSerialized] private bool workspaceInitialized;
        [NonSerialized] private Vector3[] workspaceUVLineBuffer;

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
            float shelfHeight = workspaceShowAssetShelf
                ? Mathf.Clamp(workspaceShelfHeight, 112f, Mathf.Max(112f, windowRect.height * 0.45f)) : 0f;
            Rect body = new Rect(0f, bodyTop, windowRect.width,
                Mathf.Max(0f, windowRect.height - bodyTop - shelfHeight - (workspaceShowAssetShelf ? WorkspaceSplitterSize : 0f)));

            if (workspaceShowAssetShelf)
            {
                Rect shelfSplitter = new Rect(0f, body.yMax, windowRect.width, WorkspaceSplitterSize);
                HandleSplitter(shelfSplitter, 3, ref workspaceShelfHeight, true, 112f,
                    Mathf.Max(112f, windowRect.height * 0.45f));
                Rect shelfRect = new Rect(0f, shelfSplitter.yMax, windowRect.width,
                    Mathf.Max(0f, windowRect.height - shelfSplitter.yMax));
                GUI.Box(shelfRect, GUIContent.none, WorkspaceStyles.Region);
                GUILayout.BeginArea(Shrink(shelfRect, 1f));
                DrawAssetShelf();
                GUILayout.EndArea();
            }

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
            }
        }

        internal void DrawUVWorkspace(Rect windowRect)
        {
            if (controller?.Textures == null || controller.Textures.Sets.Count == 0) return;
            InitializeWorkspaceUI();
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

            ApplyWorkspaceDisplay();
            if (GUI.changed && !changedBefore)
            {
                MarkDocumentDirty();
                SceneView.RepaintAll();
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintUVWindow.RepaintOpenWindows();
            }
        }

        internal void EndUVWindowInteraction()
        {
            uvPanning = false;
            uvColorSamplerArmed = false;
            modifierBrushDrag = false;
            modifierPathEditSet = null;
            modifierPathUndoStarted = false;
            if (controller == null)
            {
                uvDraggingSplinePoint = -1;
                uvStrokeActive = false;
                return;
            }
            if (uvDraggingSplinePoint >= 0)
            {
                uvDraggingSplinePoint = -1;
                ReapplyPendingSpline();
            }
            else if (splineReapplyPending) ReapplyPendingSpline();
            if (uvStrokeActive) EndUVStroke(true);
        }

        private void DrawWorkspaceBody(Rect body)
        {
            float railWidth = workspaceShowToolRail ? WorkspaceToolRailWidth : 0f;
            float available = Mathf.Max(0f, body.width - railWidth);
            bool showLeft = workspaceShowTargets;
            bool showRight = workspaceShowLayers || workspaceShowProperties;
            float leftWidth = 0f;
            float rightWidth = 0f;
            if (showLeft && showRight)
            {
                float panelWidth = Mathf.Max(0f, available - WorkspaceSplitterSize);
                float minimumLeft = Mathf.Min(160f, panelWidth * 0.45f);
                float minimumRight = Mathf.Min(210f, Mathf.Max(0f, panelWidth - minimumLeft));
                leftWidth = Mathf.Clamp(workspaceLeftWidth, minimumLeft,
                    Mathf.Max(minimumLeft, panelWidth - minimumRight));
                rightWidth = Mathf.Max(0f, panelWidth - leftWidth);
            }
            else if (showLeft) leftWidth = available;
            else if (showRight) rightWidth = available;

            float x = body.x;
            if (workspaceShowToolRail)
            {
                Rect rail = new Rect(x, body.y, railWidth, body.height);
                GUI.Box(rail, GUIContent.none, WorkspaceStyles.Rail);
                GUILayout.BeginArea(rail);
                DrawToolRail();
                GUILayout.EndArea();
                x += railWidth;
            }

            if (showLeft && leftWidth > 0f)
            {
                Rect left = new Rect(x, body.y, leftWidth, body.height);
                GUI.Box(left, GUIContent.none, WorkspaceStyles.Region);
                GUILayout.BeginArea(Shrink(left, 1f));
                DrawTargetRegion();
                GUILayout.EndArea();
                x += leftWidth;
                if (showRight)
                {
                    Rect splitter = new Rect(x, body.y, WorkspaceSplitterSize, body.height);
                    float maximumLeft = Mathf.Max(160f, available - WorkspaceSplitterSize - 210f);
                    HandleSplitter(splitter, 1, ref workspaceLeftWidth, false, 160f, maximumLeft);
                    x += WorkspaceSplitterSize;
                }
            }

            if (showRight && rightWidth > 0f)
            {
                Rect right = new Rect(x, body.y, Mathf.Max(0f, body.xMax - x), body.height);
                GUI.Box(right, GUIContent.none, WorkspaceStyles.Region);
                GUILayout.BeginArea(Shrink(right, 1f));
                DrawRightRegion(right.size);
                GUILayout.EndArea();
            }
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
            bool compactToolbar = rect.width < 900f;
            if (compactToolbar)
            {
                if (GUILayout.Button(new GUIContent("View", "Preview and workspace display options"),
                    EditorStyles.toolbarDropDown, GUILayout.Width(46f))) ShowViewMenu(GUILayoutUtility.GetLastRect());
            }
            else
            {
                workspaceShowAssetShelf = GUILayout.Toggle(workspaceShowAssetShelf, new GUIContent("Shelf", "Show the brush asset shelf (Tab)"), EditorStyles.toolbarButton, GUILayout.Width(44f));
                bool nextSolo = GUILayout.Toggle(channelSolo, new GUIContent("Solo", "Preview only the selected logical channel without material shading"), EditorStyles.toolbarButton, GUILayout.Width(42f));
                if (nextSolo != channelSolo) { channelSolo = nextSolo; if (channelSolo) previewBefore = false; }
                bool nextBefore = GUILayout.Toggle(previewBefore, new GUIContent("Before 3D", "Show the original source textures on the lit 3D character"), EditorStyles.toolbarButton, GUILayout.Width(66f));
                if (nextBefore != previewBefore) { previewBefore = nextBefore; if (previewBefore) channelSolo = false; }
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
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Document\tCtrl/Cmd+N"), false, NewWorkspaceDocument);
            menu.AddItem(new GUIContent("Load Document...\tCtrl/Cmd+O"), false, OpenWorkspaceDocumentPicker);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent((IsDocumentTemporary ? "Save As" : "Save") + "\tCtrl/Cmd+S"), false, SaveWorkspace);
            menu.AddItem(new GUIContent("Save As...\tCtrl/Cmd+Shift+S"), false, SaveWorkspaceAs);
            if (!IsDocumentTemporary)
                menu.AddItem(new GUIContent("Revert to Saved"), false, RevertWorkspaceDocument);
            else menu.AddDisabledItem(new GUIContent("Revert to Saved"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Export..."), false, OpenExportWindow);
            menu.AddItem(new GUIContent("Clear All Overlay Painting..."), false,
                () => ClearAllTexturePaintData(true));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Close Overlay Painter"), false,
                () => RequestCloseStage());
            menu.DropDown(anchor);
        }

        private void ShowEditMenu(Rect anchor)
        {
            GenericMenu menu = new GenericMenu();
            bool pathLayerActive = TryGetActivePathLayer(ActiveTextureSet, out TexturePaintLayer pathLayer);
            bool canUndo = CanUndoLightweight || controller.Painting.History.CanUndo || controller.Plugins.CanUndo;
            bool canRedo = CanRedoLightweight || controller.Painting.History.CanRedo || controller.Plugins.CanRedo;
            string undoLabel = CanUndoLightweight ? "Undo " + LightweightUndoLabel : "Undo";
            string redoLabel = CanRedoLightweight ? "Redo " + LightweightRedoLabel : "Redo";
            if (canUndo) menu.AddItem(new GUIContent(undoLabel + "\tCtrl/Cmd+Z"), false, PerformWorkspaceUndo);
            else menu.AddDisabledItem(new GUIContent("Undo\tCtrl/Cmd+Z"));
            if (canRedo) menu.AddItem(new GUIContent(redoLabel + "\tCtrl/Cmd+Shift+Z"), false, PerformWorkspaceRedo);
            else menu.AddDisabledItem(new GUIContent("Redo\tCtrl/Cmd+Shift+Z"));
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
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Layer/New Paint Layer"));
                menu.AddDisabledItem(new GUIContent("Layer/New Fill Layer"));
                menu.AddDisabledItem(new GUIContent("Layer/New Path Layer"));
            }

            menu.AddSeparator(string.Empty);
            if (pathLayerActive)
            {
                menu.AddItem(new GUIContent("Path/Select All Points\tCtrl/Cmd+A"), false,
                    SelectAllActivePathPoints);
                menu.AddItem(new GUIContent("Path/Copy\tCtrl/Cmd+C"), false, CopyActivePath);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Path/Select All Points\tCtrl/Cmd+A"));
                menu.AddDisabledItem(new GUIContent("Path/Copy\tCtrl/Cmd+C"));
            }
            if (set != null && !string.IsNullOrEmpty(splineClipboard))
                menu.AddItem(new GUIContent("Path/Paste as New Layer\tCtrl/Cmd+V"), false, PastePathAsNewLayer);
            else menu.AddDisabledItem(new GUIContent("Path/Paste as New Layer\tCtrl/Cmd+V"));
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
            if (!documentPickerOpen || current == null ||
                current.commandName != "ObjectSelectorClosed" ||
                EditorGUIUtility.GetObjectPickerControlID() != DocumentPickerControlId) return;
            documentPickerOpen = false;
            TexturePaintDocument selected = EditorGUIUtility.GetObjectPickerObject() as TexturePaintDocument;
            if (selected != null && selected != document) LoadWorkspaceDocument(selected);
            current.Use();
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
            TexturePaintDocumentStorage.Restore(document, controller.Textures);
            TexturePaintDocumentStorage.RestoreMasks(document, controller.Masks);
            RestoreState(LoadDocumentEditorState(), false);
            documentRevision = document.revisionId;
            documentDirty = false;
            recoveryDirty = false;
            persistenceError = null;
            TexturePaintRecoveryStore.Delete(recoveryContextKey);
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
            FinishDocumentChange(previous, "Loaded " + document.name);
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
            TexturePaintDocumentStorage.Restore(currentDocument, controller.Textures);
            TexturePaintDocumentStorage.RestoreMasks(currentDocument, controller.Masks);
            documentRevision = currentDocument.revisionId;
            documentDirty = false;
            recoveryDirty = false;
            TexturePaintRecoveryStore.Delete(recoveryContextKey);
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
            FinishDocumentChange(null, "Reverted " + currentDocument.name);
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
            BeginLayerCreationUndo("Paste Texture Path");
            TexturePaintLayer pasted = set.AddSplineLayer("Pasted Path");
            pasted.spline = JsonUtility.FromJson<TexturePaintSpline>(splineClipboard);
            pathMode = TexturePaintPathMode.Ribbon;
            pasted.splineSettings = CreateSplineSettings();
            spline = pasted.spline;
            splineMode = true;
            selectedSplinePoint = -1;
            CompleteLayerCreationUndo(pasted);
            SyncActiveLayerSelection(set);
            ShowWorkspaceStatus("Path pasted as a new layer");
        }

        private void DrawToolRail()
        {
            GUILayout.Space(5f);
            TextureSet set = ActiveTextureSet;
            bool canPaint = CanStartFreehandPaint(set);
            using (new EditorGUI.DisabledScope(!canPaint))
            {
                DrawToolButton(TexturePaintTool.Paint, 0, "Paint (B)");
                DrawToolButton(TexturePaintTool.Erase, 1, "Erase (E)");
                DrawToolButton(TexturePaintTool.Blur, 2, "Blur (U)");
                DrawToolButton(TexturePaintTool.Smear, 3, "Smear (K)");
                DrawToolButton(TexturePaintTool.Clone, 4, "Clone (C); Ctrl-click sets source");
                DrawToolButton(TexturePaintTool.Dodge, 5, "Dodge (O)");
                DrawToolButton(TexturePaintTool.Burn, 6, "Burn (Shift+O)");
                DrawToolButton(TexturePaintTool.NormalTouchup, 7, "Normal touchup (N)");
                DrawToolButton(TexturePaintTool.Plugin, 8, "Plugin brush (P)");
            }
            GUILayout.Space(6f);
            Rect separator = GUILayoutUtility.GetRect(30f, 1f);
            EditorGUI.DrawRect(separator, WorkspaceStyles.BorderColor);
            GUILayout.Space(5f);
            bool activeSpline = TryGetActivePathLayer(set, out _);
            bool nextSpline = DrawToolRailIconControl(activeSpline, 9,
                activeSpline ? "Spline authoring is active" : "Create a spline/path layer", 34f);
            if (!activeSpline && nextSpline)
            {
                if (set != null) CreateSplineLayerWithUndo(set);
            }
            splineMode = TryGetActivePathLayer(ActiveTextureSet, out _);
            GUILayout.FlexibleSpace();
            if (DrawToolRailIconButton(10, "Shortcut and workflow reference", 28f)) ShowShortcutHelp();
            GUILayout.Space(4f);
        }

        private void DrawToolButton(TexturePaintTool value, int iconIndex, string tooltip)
        {
            bool selected = CanStartFreehandPaint(ActiveTextureSet) && tool == value;
            bool next = DrawToolRailIconControl(selected, iconIndex, tooltip, 34f);
            if (!next || selected) return;
            tool = value;
            if (tool == TexturePaintTool.NormalTouchup) selectedChannel = TexturePaintChannel.Normal;
            ShowWorkspaceStatus(tooltip);
            SceneView.RepaintAll();
        }

        private static bool DrawToolRailIconControl(bool selected, int iconIndex, string tooltip, float height)
        {
            Rect button = ReserveToolRailButton(height);
            bool next = GUI.Toggle(button, selected, new GUIContent(string.Empty, tooltip), WorkspaceStyles.RailButton);
            DrawToolRailIcon(button, iconIndex);
            return next;
        }

        private static bool DrawToolRailIconButton(int iconIndex, string tooltip, float height)
        {
            Rect button = ReserveToolRailButton(height);
            bool clicked = GUI.Button(button, new GUIContent(string.Empty, tooltip), WorkspaceStyles.RailButton);
            DrawToolRailIcon(button, iconIndex);
            return clicked;
        }

        private static Rect ReserveToolRailButton(float height)
        {
            // The min/max overload avoids EditorStyles.miniButton's inherited one-line
            // preferred height shrinking the layout entry around its empty text content.
            return GUILayoutUtility.GetRect(44f, 44f, height, height, WorkspaceStyles.RailButton,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        }

        private static void DrawToolRailIcon(Rect button, int iconIndex)
        {
            Sprite sprite = GetToolRailIcon(iconIndex);
            if (sprite == null || sprite.texture == null)
            {
                GUI.Label(button, (iconIndex + 1).ToString(), EditorStyles.centeredGreyMiniLabel);
                return;
            }
            GUI.BeginClip(button);
            Rect inner = new Rect(3f, 2f, Mathf.Max(0f, button.width - 6f), Mathf.Max(0f, button.height - 4f));
            float size = Mathf.Min(30f, Mathf.Min(inner.width, inner.height));
            Rect iconRect = new Rect(inner.center.x - size * 0.5f, inner.center.y - size * 0.5f, size, size);
            Rect source = sprite.textureRect;
            Rect uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
                source.width / sprite.texture.width, source.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(iconRect, sprite.texture, uv, true);
            GUI.EndClip();
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
                string type = target.isUdim ? $"UDIM · {target.members.Count} tiles" : "Single slot";
                Rect row = GUILayoutUtility.GetRect(10f, target.isUdim && selected ? 28f + target.members.Count * 18f : 42f,
                    GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                    (selected ? WorkspaceStyles.SelectedRow : WorkspaceStyles.Row).Draw(row, false, false, selected, false);
                GUI.Label(new Rect(row.x + 7f, row.y + 4f, row.width - 14f, 18f), target.displayName, EditorStyles.label);
                GUI.Label(new Rect(row.x + 7f, row.y + 22f, row.width - 14f, 16f), type, EditorStyles.miniLabel);
                if (target.isUdim && selected)
                {
                    for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
                    {
                        TexturePaintLogicalTargetMember member = target.members[memberIndex];
                        GUI.Label(new Rect(row.x + 14f, row.y + 40f + memberIndex * 18f, row.width - 21f, 16f),
                            $"{member.udimTileNumber}  {member.slotName}", EditorStyles.miniLabel);
                    }
                }
                if (GUI.Button(row, GUIContent.none, GUIStyle.none) && !selected)
                    SelectLogicalTarget(target, sets, true);
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
                if (Event.current.type == EventType.Repaint)
                    (selected ? WorkspaceStyles.SelectedRow : WorkspaceStyles.Row).Draw(row, false, false, selected, false);
                Rect thumbnail = new Rect(row.x + 5f, row.y + 5f, 38f, 38f);
                DrawTextureThumbnail(thumbnail, set.GetVisibleTexture(selectedChannel), TexturePaintStoreFallback(selectedChannel));
                GUI.Label(new Rect(thumbnail.xMax + 7f, row.y + 6f, row.width - thumbnail.width - 16f, 20f), slots, EditorStyles.label);
                GUI.Label(new Rect(thumbnail.xMax + 7f, row.y + 25f, row.width - thumbnail.width - 16f, 16f),
                    $"Texture set {i + 1} · {set.channels.Count} channels", EditorStyles.miniLabel);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    selectedSurface = i;
                    SyncActiveLayerSelection(set);
                    ApplyWorkspaceDisplay();
                }
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
            uvPreviewBefore = GUILayout.Toggle(uvPreviewBefore,
                new GUIContent("Before", "Show the original source texture in this 2D canvas only"),
                EditorStyles.toolbarButton, GUILayout.Width(52f));
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

            Rect canvas = GUILayoutUtility.GetRect(100f, 10000f, 100f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (set != null) DrawUVCanvas(canvas, set);
            else Draw3DCompanionMessage(canvas);
        }

        private void DrawChannelToolbar(TextureSet set)
        {
            TexturePaintChannel[] channels = (TexturePaintChannel[])Enum.GetValues(typeof(TexturePaintChannel));
            string[] labels = { "Base", "Nrm", "Met", "Rgh", "AO", "Em", "C" };
            for (int i = 0; i < channels.Length; i++)
            {
                TexturePaintChannel channel = channels[i];
                using (new EditorGUI.DisabledScope(set?.GetChannel(channel) == null))
                {
                    bool next = GUILayout.Toggle(selectedChannel == channel,
                        new GUIContent(labels[i], channel.ToString()), EditorStyles.toolbarButton, GUILayout.Width(i == 0 ? 38f : 34f));
                    if (next && selectedChannel != channel)
                    {
                        selectedChannel = channel;
                        ApplyWorkspaceDisplay();
                    }
                }
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
                labels.Add(channel == TexturePaintChannel.Albedo ? "Base" : channel.ToString());
            }
            if (available.Count == 0)
            {
                GUILayout.Label("No channel", EditorStyles.miniLabel, GUILayout.Width(72f));
                return;
            }
            if (selected < 0)
            {
                selected = 0;
                selectedChannel = available[0];
            }
            int next = EditorGUILayout.Popup(selected, labels.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(78f));
            TexturePaintChannel nextChannel = available[Mathf.Clamp(next, 0, available.Count - 1)];
            if (nextChannel == selectedChannel) return;
            selectedChannel = nextChannel;
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
            Texture texture = GetWorkspacePreviewTexture(set, uvPreviewBefore);
            if (texture != null) GUI.DrawTexture(textureRect, texture, ScaleMode.StretchToFill, false);
            else EditorGUI.DrawRect(textureRect, TexturePaintStoreFallback(selectedChannel));

            GUI.BeginClip(canvas);
            Rect localTexture = new Rect(textureRect.x - canvas.x, textureRect.y - canvas.y, textureRect.width, textureRect.height);
            DrawUVWireframe(set.surface.mesh, localTexture);
            DrawUVPaths(set, localTexture);
            DrawUVBrushCursor(set, localTexture);
            GUI.EndClip();
            HandleUVCanvasInput(canvas, textureRect, set);

            Rect badge = new Rect(canvas.x + 8f, canvas.y + 8f, 230f, 22f);
            GUI.Label(badge, uvPreviewBefore ? "SOURCE · before painting" : $"DESTINATION · {selectedChannel}", WorkspaceStyles.CanvasBadge);
            Rect help = new Rect(canvas.x + 8f, canvas.yMax - 24f, canvas.width - 16f, 18f);
            string inputHelp = TryGetActivePathLayer(set, out _)
                ? "LMB add/select path points · Drag selected point · MMB/RMB pan · Wheel zoom"
                : CanStartFreehandPaint(set)
                    ? "LMB paint · MMB/RMB pan · Wheel zoom · Ctrl-click clone source · Shift+RMB size/hardness"
                    : "Select or create a Paint layer to use freehand tools · MMB/RMB pan · Wheel zoom";
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
            bool authoringSplineLayer = TryGetActivePathLayer(set, out _);
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
                    if (uvDraggingSplinePoint >= 0)
                    {
                        uvDraggingSplinePoint = -1;
                        ReapplyPendingSpline();
                    }
                    else if (splineReapplyPending) ReapplyPendingSpline();
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
                if (uvDraggingSplinePoint >= 0)
                {
                    uvDraggingSplinePoint = -1;
                    ReapplyPendingSpline();
                    current.Use();
                    return;
                }
                if (splineReapplyPending)
                {
                    ReapplyPendingSpline();
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
            if (!TryCanvasUV(current.mousePosition, textureRect, out Vector2 uv) ||
                !TryMakeUVSample(set, uv, out StrokeSample sample)) return;

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (!authoringSplineLayer && uvColorSamplerArmed)
                {
                    SampleSurfaceColor(set.surface, uv, uvPreviewBefore); uvColorSamplerArmed = false;
                }
                else if (!authoringSplineLayer && CanStartFreehandPaint(set) &&
                    tool == TexturePaintTool.Clone && current.control)
                {
                    cloneSourceUV = uv; ShowWorkspaceStatus("Clone source sampled");
                }
                else if (authoringSplineLayer)
                {
                    int point = FindSplinePointAt(textureRect, current.mousePosition);
                    if (point >= 0)
                    {
                        selectedSplinePoint = point; selectedSplinePoints?.Clear(); selectedSplinePoints?.Add(point);
                        BeginLightweightPathUndo(set, "Move UV Path Point");
                        uvDraggingSplinePoint = point;
                    }
                    else AddUVSplinePoint(set, sample);
                }
                else if (CanStartFreehandPaint(set))
                {
                    BeginPaintAt(set, sample); uvStrokeActive = strokeActive;
                }
                else ShowPaintLayerRequiredStatus(set);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && authoringSplineLayer &&
                uvDraggingSplinePoint >= 0 && spline != null && uvDraggingSplinePoint < spline.PointCount)
            {
                MoveUVSplinePoint(set, uvDraggingSplinePoint, uv); current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && uvStrokeActive)
            {
                ContinuePaintAt(sample); current.Use();
            }
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
                    controller.Painting.EndStroke(false);
                    strokeActive = false;
                    previousContactSamples.Clear();
                    sampledStrokePoints.Clear();
                    strokeTextureSets.Clear();
                }
            }
            uvStrokeActive = false;
            paintGestureActive = false;
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
            if (!CanStartFreehandPaint(set) || TryGetActivePathLayer(set, out _)) return;
            Event current = Event.current;
            Vector2 mouse = current.mousePosition;
            if (!TryCanvasUV(mouse, textureRect, out Vector2 uv) ||
                !TryMakeUVSample(set, uv, out StrokeSample sample)) return;
            float radiusUV = set.surface.CalculateUVRadius(sample.triangleIndex, ActiveBrush.size);
            float radius = Mathf.Max(2f, radiusUV * textureRect.width);
            Vector2 point = new Vector2(textureRect.x + uv.x * textureRect.width,
                textureRect.y + (1f - uv.y) * textureRect.height);
            Handles.color = new Color(paintColor.r, paintColor.g, paintColor.b, 0.95f);
            Handles.DrawWireDisc(point, Vector3.forward, radius);
            Handles.color = new Color(1f, 1f, 1f, 0.65f);
            Handles.DrawWireDisc(point, Vector3.forward, radius * ActiveBrush.hardness);
            float radians = ActiveBrush.rotation * Mathf.Deg2Rad;
            Handles.DrawLine(point, point + new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)) * radius);
        }

        private void DrawUVPaths(TextureSet set, Rect textureRect)
        {
            if (set == null || Event.current.type != EventType.Repaint) return;
            int layerIndex = set.activeLayerIndex;
            if (!IsActiveSplineAuthoringLayer(set, layerIndex)) return;
            TexturePaintSpline path = set.layers[layerIndex].spline;
            if (path == null || path.PointCount == 0) return;
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

        private int FindSplinePointAt(Rect textureRect, Vector2 mouse)
        {
            TextureSet set = ActiveTextureSet;
            if (spline == null || set == null || !IsActiveSplineAuthoringLayer(set, set.activeLayerIndex) ||
                set.layers[set.activeLayerIndex].spline != spline) return -1;
            int best = -1; float bestDistance = 9f;
            for (int i = 0; i < spline.PointCount; i++)
            {
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
            BeginLightweightPathUndo(activeSet, "Add UV Path Point");
            spline.worldSpace = false;
            spline.AddPoint(sample.worldPosition, sample.uv, sample.surfaceIndex, sample.triangleIndex, sample.worldNormal);
            selectedSplinePoint = spline.PointCount - 1;
            TexturePaintSurfaceAnchor anchor = spline.anchors[selectedSplinePoint];
            anchor.surfaceId = activeSet.persistentId; anchor.barycentric = sample.barycentric; anchor.normal = sample.worldNormal;
            spline.anchors[selectedSplinePoint] = anchor;
            ProjectSplineControlToSurface(activeSet, spline, selectedSplinePoint, true);
            ProjectSplineControlToSurface(activeSet, spline, selectedSplinePoint, false);
            if (selectedSplinePoint > 0) ProjectSplineControlToSurface(activeSet, spline, selectedSplinePoint - 1, false);
            CompleteLightweightPathEdit(activeSet, true);
            SceneView.RepaintAll();
        }

        private void MoveUVSplinePoint(TextureSet set, int point, Vector2 uv)
        {
            if (spline == null || (uint)point >= (uint)spline.PointCount) return;
            spline.worldSpace = false;
            Vector2 delta = uv - spline.uvPoints[point];
            spline.uvPoints[point] = uv;
            spline.EnsureControlPoints();
            spline.uvInControls[point] += delta; spline.uvOutControls[point] += delta;
            UpdateSplineAnchorFromUV(set, spline, point);
            ProjectSplineControlToSurface(set, spline, point, true);
            ProjectSplineControlToSurface(set, spline, point, false);
            CompleteLightweightPathEdit(set, true);
            SceneView.RepaintAll();
        }

        private static bool TryCanvasUV(Vector2 point, Rect textureRect, out Vector2 uv)
        {
            uv = new Vector2((point.x - textureRect.x) / Mathf.Max(1f, textureRect.width),
                1f - (point.y - textureRect.y) / Mathf.Max(1f, textureRect.height));
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
            if (GUILayout.Button(new GUIContent("+ Paint", "Add paint layer"), EditorStyles.toolbarButton)) AddPaintLayer(set);
            if (GUILayout.Button(new GUIContent("+ Fill", "Add fill layer"), EditorStyles.toolbarButton)) AddFillLayer(set);
            if (GUILayout.Button(new GUIContent("+ Path", "Add spline/path layer"), EditorStyles.toolbarButton))
                CreateSplineLayerWithUndo(set);
            if (!pathsOnly && GUILayout.Button(new GUIContent("+ Group", "Add layer folder/group"), EditorStyles.toolbarButton))
            {
                BeginLayerCreationUndo("Add Layer Group");
                TexturePaintLayer created = set.AddGroup("Group " + (set.layers.Count + 1));
                CompleteLayerCreationUndo(created);
                SyncActiveLayerSelection(set);
            }
            GUILayout.EndHorizontal();

            workspaceLayerScroll = GUILayout.BeginScrollView(workspaceLayerScroll);
            int deleteIndex = -1;
            for (int i = set.layers.Count - 1; i >= 0; i--)
            {
                TexturePaintLayer layer = set.layers[i];
                if (pathsOnly && !layer.IsSplineLayer) continue;
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

        private void DrawLayerRow(TextureSet set, TexturePaintLayer layer, int index, Rect row, ref int deleteIndex)
        {
            bool selected = set.activeLayerIndex == index;
            if (Event.current.type == EventType.Repaint)
                (selected ? WorkspaceStyles.SelectedRow : WorkspaceStyles.Row).Draw(row, false, false, selected, false);
            Rect drag = new Rect(row.x + 2f, row.y + 4f, 13f, row.height - 8f);
            GUI.Label(drag, "≡", WorkspaceStyles.DragHandle);
            Rect eye = new Rect(drag.xMax, row.y + 11f, 25f, 24f);
            bool visible = GUI.Toggle(eye, layer.visible, new GUIContent(layer.visible ? "●" : "○", "Layer visibility"), EditorStyles.miniButton);
            if (visible != layer.visible)
            {
                ChangeLayerVisibility(set, layer, visible);
            }
            Rect thumb = new Rect(eye.xMax + 4f, row.y + 5f, 36f, 36f);
            Texture thumbnail = null;
            if (layer.channels.TryGetValue(selectedChannel, out EditableTextureTarget target)) thumbnail = target.Front;
            DrawTextureThumbnail(thumb, thumbnail, layer.kind == TexturePaintLayerKind.Fill ? layer.fillColor : Color.clear);
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

            Rect text = new Rect(thumb.xMax + 7f, row.y + 4f,
                Mathf.Max(0f, textRight - thumb.xMax - 7f), 21f);
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
            GUI.Label(new Rect(text.x, row.y + 25f, text.width, 16f), LayerSubtitle(layer), EditorStyles.miniLabel);

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
            bool hasEffects = layer.effects.HasEnabled;
            Color previousBackground = GUI.backgroundColor;
            if (hasEffects) GUI.backgroundColor = new Color(0.38f, 0.72f, 1f);
            using (new EditorGUI.DisabledScope(layer.kind == TexturePaintLayerKind.Group))
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
                (showExtendedControls && extendedControls.Contains(Event.current.mousePosition));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                click.Contains(Event.current.mousePosition) && !pointerOverControl)
            {
                set.activeLayerIndex = index; SyncActiveLayerSelection(set); GUI.FocusControl(null); Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && drag.Contains(Event.current.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(LayerDragKey, index);
                DragAndDrop.StartDrag(layer.name);
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

        private void ShowLayerEffectsPopup(Rect anchor, TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer == null || layer.kind == TexturePaintLayerKind.Group) return;
            int index = set.layers.IndexOf(layer);
            if (index >= 0)
            {
                set.activeLayerIndex = index;
                SyncActiveLayerSelection(set);
            }
            PopupWindow.Show(anchor, new LayerEffectsPopup(this, set, layer, selectedChannel));
        }

        private sealed class LayerEffectsPopup : PopupWindowContent
        {
            private readonly TexturePaintStageWindow owner;
            private readonly TextureSet set;
            private readonly TexturePaintLayer layer;
            private readonly TexturePaintChannel defaultChannel;
            private readonly TexturePaintLayerEffects effects;
            private Vector2 scroll;

            public LayerEffectsPopup(TexturePaintStageWindow owner, TextureSet set,
                TexturePaintLayer layer, TexturePaintChannel defaultChannel)
            {
                this.owner = owner;
                this.set = set;
                this.layer = layer;
                this.defaultChannel = defaultChannel;
                effects = layer.effects?.Clone() ?? new TexturePaintLayerEffects();
                effects.Normalize();
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(390f, 610f);
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
                DrawEffect(effects.stroke, "Stroke");
                DrawEffect(effects.innerShadow, "Inner Shadow");
                DrawEffect(effects.outerShadow, "Outer Shadow");
                DrawEffect(effects.innerGlow, "Inner Glow");
                DrawEffect(effects.outerGlow, "Outer Glow");
                DrawEffect(effects.colorOverlay, "Color Overlay");
                EditorGUILayout.EndScrollView();
                if (!EditorGUI.EndChangeCheck()) return;

                effects.Normalize();
                owner.ChangeLayerEffects(set, layer, effects);
                SceneView.RepaintAll();
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintUVWindow.RepaintOpenWindows();
                editorWindow?.Repaint();
            }

            private void DrawEffect(TexturePaintLayerEffectSettings effect, string title)
            {
                if (effect == null) return;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool wasEnabled = effect.enabled;
                effect.enabled = EditorGUILayout.ToggleLeft(title, effect.enabled, EditorStyles.boldLabel);
                if (!wasEnabled && effect.enabled) effect.channel = defaultChannel;
                if (effect.enabled)
                {
                    EditorGUI.indentLevel++;
                    effect.channel = (TexturePaintChannel)EditorGUILayout.EnumPopup(
                        new GUIContent("Channel", "The material channel affected by this layer effect."),
                        effect.channel);
                    effect.color = EditorGUILayout.ColorField("Color", effect.color);
                    switch (effect.kind)
                    {
                        case TexturePaintLayerEffectKind.Stroke:
                            effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                            effect.smoothness = EditorGUILayout.Slider("Smooth", effect.smoothness, 0f, 1f);
                            break;
                        case TexturePaintLayerEffectKind.InnerShadow:
                        case TexturePaintLayerEffectKind.OuterShadow:
                            effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                            effect.offset = EditorGUILayout.Vector2Field("Offset (px)", effect.offset);
                            effect.offset.x = Mathf.Clamp(effect.offset.x, -256f, 256f);
                            effect.offset.y = Mathf.Clamp(effect.offset.y, -256f, 256f);
                            effect.curve = EditorGUILayout.CurveField("Curve", effect.curve,
                                Color.white, new Rect(0f, 0f, 1f, 1f), GUILayout.Height(34f));
                            break;
                        case TexturePaintLayerEffectKind.InnerGlow:
                        case TexturePaintLayerEffectKind.OuterGlow:
                            effect.width = EditorGUILayout.Slider("Width (px)", effect.width, 0.5f, 128f);
                            effect.curve = EditorGUILayout.CurveField("Curve", effect.curve,
                                Color.white, new Rect(0f, 0f, 1f, 1f), GUILayout.Height(34f));
                            break;
                        case TexturePaintLayerEffectKind.ColorOverlay:
                            effect.blendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                                "Blend", effect.blendMode);
                            effect.level = EditorGUILayout.Slider("Level", effect.level, 0f, 1f);
                            break;
                    }
                    EditorGUI.indentLevel--;
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
            bool showPaintControls = activeLayer == null || isPaint || isPath;
            if (isPath)
                EditorGUILayout.HelpBox(
                    "Spline layer active: freehand paint tools are disabled. Click or drag on the model/UV view to add and adjust path points.",
                    MessageType.Info);
            else if (isFill || isGroup)
                EditorGUILayout.HelpBox(
                    "Freehand tools require an active Paint layer. Fill and Group layers cannot receive brush strokes.",
                    MessageType.Info);
            if (showPaintControls) DrawPropertySection("DESTINATION", () =>
            {
                if (isPaint || isPath)
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
            if (isFill)
                DrawPropertySection("SOURCE", () => DrawSourceProperties(set));
            else if (showPaintControls)
            {
                DrawPropertySection("SOURCE", () => DrawSourceProperties(set));
                DrawPropertySection("CHANNELS", () => DrawChannelProperties(set));
                DrawPropertySection("BRUSH", DrawBrushProperties);
            }
            if (isPath)
                DrawPropertySection("PATH", () => DrawPathProperties(set));
            if (showPaintControls)
                DrawPropertySection("STROKE & PROJECTION", DrawStrokeProperties);
            if (!isGroup) DrawPropertySection(isFill ? "MASKS" : "MASKS & EXTENSIONS", () =>
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Masks…")) MaskEditor.Open(controller);
                if (!isFill && GUILayout.Button("Plugins…")) PluginManagerWindow.Open(controller);
                GUILayout.EndHorizontal();
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
                if (GUILayout.Button("Performance & Memory…")) performanceExpanded = !performanceExpanded;
                if (performanceExpanded) DrawPerformanceProperties();
            });
            GUILayout.EndScrollView();
        }

        private void DrawSourceProperties(TextureSet set)
        {
            TexturePaintLayer fillLayer = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count &&
                set.layers[set.activeLayerIndex].kind == TexturePaintLayerKind.Fill
                    ? set.layers[set.activeLayerIndex] : null;
            TexturePaintFillSettings fillSettings = fillLayer?.fillSettings;
            EditorGUI.BeginChangeCheck();
            paintSource = (TexturePaintBrushSource)GUILayout.Toolbar((int)paintSource, new[] { "Texture", "Overlay", "Color" });
            bool sourceReady = true;
            switch (paintSource)
            {
                case TexturePaintBrushSource.Texture:
                    paintSourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source Texture", paintSourceTexture, typeof(Texture2D), false);
                    sourceReady = paintSourceTexture != null;
                    if (!sourceReady)
                        EditorGUILayout.HelpBox(fillLayer != null
                            ? "Select a source texture to apply this source to the Fill layer."
                            : "Select a source texture before painting.", MessageType.Info);
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
                    paintColor = EditorGUILayout.ColorField("Source Color", paintColor);
                    break;
            }
            Vector2 tiling = fillSettings?.tiling ?? Vector2.one;
            if (fillLayer != null && paintSource != TexturePaintBrushSource.Color)
            {
                tiling = EditorGUILayout.Vector2Field(new GUIContent("Tiling X / Y",
                    "Independent horizontal and vertical repetition for the generated Fill texture"), tiling);
                tiling.x = Mathf.Clamp(tiling.x, 0.01f, 1000f);
                tiling.y = Mathf.Clamp(tiling.y, 0.01f, 1000f);
            }
            bool changed = EditorGUI.EndChangeCheck();
            // Keep an incomplete source choice in the UI. Committing it immediately would fail
            // Fill generation and snap the toolbar back before the user can assign its asset.
            if (changed && fillLayer != null && sourceReady)
            {
                TexturePaintFillSettings updated = (fillSettings ?? new TexturePaintFillSettings()).Clone();
                updated.source = paintSource;
                updated.sourceTexture = paintSourceTexture;
                updated.sourceOverlay = paintSourceOverlay;
                updated.color = paintColor;
                updated.tiling = tiling;
                ChangeFillLayer(set, fillLayer, fillLayer.fillChannel, updated);
            }
        }

        private void DrawChannelProperties(TextureSet set)
        {
            selectedChannel = (TexturePaintChannel)EditorGUILayout.EnumPopup("Active Channel", selectedChannel);
            if (selectedChannel == TexturePaintChannel.Normal)
                normalConvention = (TexturePaintNormalConvention)EditorGUILayout.EnumPopup("Convention", normalConvention);
            TextureChannelTarget target = set?.GetChannel(selectedChannel);
            if (target == null) EditorGUILayout.HelpBox("The active target has no matching logical channel.", MessageType.Warning);
            if (set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count)
            {
                TexturePaintLayer layer = set.layers[set.activeLayerIndex];
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(selectedChannel,
                    layer.channels.ContainsKey(selectedChannel));
                if (settings != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool enabled = EditorGUILayout.Toggle("Enabled", settings.enabled);
                    bool locked = EditorGUILayout.Toggle("Lock Painting", settings.locked);
                    float contribution = EditorGUILayout.Slider("Paint Contribution", settings.contribution, 0f, 1f);
                    float opacity = EditorGUILayout.Slider("Channel Opacity", settings.opacity, 0f, 1f);
                    TexturePaintBlendMode blend = (TexturePaintBlendMode)EditorGUILayout.EnumPopup("Channel Blend", settings.blendMode);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ChangeLayerChannel(set, layer, selectedChannel, enabled, locked, contribution, opacity, blend);
                    }
                }
            }
            bool nextSolo = EditorGUILayout.Toggle(new GUIContent("Solo in 3D", "Preview this logical channel without material shading"), channelSolo);
            if (nextSolo != channelSolo) { channelSolo = nextSolo; if (channelSolo) previewBefore = false; }
            bool nextBefore = EditorGUILayout.Toggle(new GUIContent("Before in 3D",
                "Show the original source textures while preserving the character material and lighting"), previewBefore);
            if (nextBefore != previewBefore) { previewBefore = nextBefore; if (previewBefore) channelSolo = false; }
        }

        private void DrawBrushProperties()
        {
            brush = (BrushPreset)EditorGUILayout.ObjectField("Preset", brush, typeof(BrushPreset), false);
            BrushPreset active = ActiveBrush;
            EditorGUI.BeginChangeCheck();
            tool = (TexturePaintTool)EditorGUILayout.EnumPopup("Tool", tool);
            active.shape = (BrushPreset.Shape)EditorGUILayout.EnumPopup("Shape", active.shape);
            active.size = EditorGUILayout.Slider("World Size", active.size, 0.001f, 0.5f);
            active.hardness = EditorGUILayout.Slider("Hardness", active.hardness, 0f, 1f);
            active.flow = EditorGUILayout.Slider("Flow", active.flow, 0f, 1f);
            active.spacing = EditorGUILayout.Slider(new GUIContent("Stroke Spacing",
                "Center-to-center stamp spacing measured in brush diameters."), active.spacing, 0.01f, 10f);
            active.rotation = DrawBrushRotation(active.rotation);
            active.alignToStroke = EditorGUILayout.Toggle("Follow Stroke", active.alignToStroke);
            if (active.shape == BrushPreset.Shape.Stamp)
                active.stampTexture = (Texture2D)EditorGUILayout.ObjectField("Stamp", active.stampTexture, typeof(Texture2D), false);
            strength = EditorGUILayout.Slider("Strength", strength, 0f, 1f);
            limitStrokeCoverage = EditorGUILayout.Toggle(new GUIContent("Cap Per Stroke", "Accumulate coverage up to one complete replacement per stroke."), limitStrokeCoverage);
            mirrorX = EditorGUILayout.Toggle("Mirror Global X", mirrorX);
            if (EditorGUI.EndChangeCheck() && brush != null) EditorUtility.SetDirty(brush);
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
            if (layer.kind == TexturePaintLayerKind.Fill)
                DrawFillLayerProperties(set, layer);
            EditorGUILayout.LabelField("Type", layer.kind.ToString());
            if (!string.IsNullOrEmpty(layer.pluginId))
                EditorGUILayout.LabelField("Extension", layer.pluginId + " " + layer.pluginVersion);
        }

        private void DrawFillLayerProperties(TextureSet set, TexturePaintLayer layer)
        {
            layer.NormalizeKindPayload();
            TexturePaintFillSettings current = layer.fillSettings;
            EditorGUI.BeginChangeCheck();
            TexturePaintChannel channel = (TexturePaintChannel)EditorGUILayout.EnumPopup("Fill Channel", layer.fillChannel);
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
            updated.projection = projection;
            updated.triplanarBlend = triplanarBlend;
            updated.blendOffset = blendOffset;
            updated.blendSharpness = blendSharpness;
            ChangeFillLayer(set, layer, channel, updated);
            selectedChannel = channel;
        }

        private void DrawStrokeProperties()
        {
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
            splineMode = TryGetActivePathLayer(set, out _);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle("Surface Authoring", splineMode);
            EditorGUILayout.LabelField("Path Domain", spline.worldSpace ? "3D Surface" : "2D UV");
            EditorGUI.BeginChangeCheck();
            bool useBezier = EditorGUILayout.Toggle("Bezier Curves", spline.useBezier);
            bool closed = EditorGUILayout.Toggle("Closed Loop", spline.closed);
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
            if (EditorGUI.EndChangeCheck())
            {
                BeginLightweightPathUndo(set, "Edit Path Parameters");
                spline.useBezier = useBezier; spline.closed = closed; spline.showControls = showControls;
                pathMode = nextPathMode; pathOrientation = nextOrientation;
                pathStartCap = nextStartCap; pathEndCap = nextEndCap; radialSymmetry = nextRadialSymmetry;
                CompleteLightweightPathEdit(set, false);
            }
            DrawPathStampSpacingControl();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Along Path")) ApplySpline();
            if (GUILayout.Button("Reverse"))
            {
                BeginLightweightPathUndo(set, "Reverse Spline");
                spline.Reverse();
                CompleteLightweightPathEdit(set, false);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(selectedSplinePoint < 0 || selectedSplinePoint >= spline.PointCount))
            {
                if (GUILayout.Button("Insert Point"))
                {
                    BeginLightweightPathUndo(set, "Insert Spline Point");
                    selectedSplinePoint = spline.InsertPointAfter(selectedSplinePoint);
                    UpdateSplineAnchorFromCurrentDomain(set, spline, selectedSplinePoint);
                    CompleteLightweightPathEdit(set, false);
                }
                if (GUILayout.Button("Delete Point"))
                {
                    BeginLightweightPathUndo(set, "Delete Spline Point");
                    spline.RemovePoint(selectedSplinePoint);
                    selectedSplinePoint = Mathf.Clamp(selectedSplinePoint, -1, spline.PointCount - 1);
                    CompleteLightweightPathEdit(set, false);
                }
            }
            GUILayout.EndHorizontal();
            if (selectedSplinePoint >= 0 && selectedSplinePoint < spline.PointCount)
            {
                spline.EnsureControlPoints();
                EditorGUI.BeginChangeCheck();
                TexturePaintTangentMode tangent = (TexturePaintTangentMode)EditorGUILayout.EnumPopup("Point Tangent", spline.tangentModes[selectedSplinePoint]);
                float pressure = EditorGUILayout.Slider("Point Pressure", spline.pressures[selectedSplinePoint], 0f, 1f);
                float width = EditorGUILayout.Slider("Point Width", spline.widths[selectedSplinePoint], 0.05f, 4f);
                float flow = EditorGUILayout.Slider("Point Flow", spline.flows[selectedSplinePoint], 0f, 2f);
                float roll = EditorGUILayout.Slider("Point Roll", spline.rolls[selectedSplinePoint], -180f, 180f);
                float offset = EditorGUILayout.Slider("Surface Offset", spline.offsets[selectedSplinePoint], -0.1f, 0.1f);
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
        }

        private void DrawAssetShelf()
        {
            RefreshBrushShelfIfNeeded();
            DrawRegionHeader("ASSET SHELF", "Searchable presets with folders, tags, favorites, recents, thumbnails, and drag reorder.");
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            assetShelfSearch = GUILayout.TextField(assetShelfSearch ?? string.Empty, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
            List<string> folders = GetBrushFolders();
            int folderIndex = Mathf.Max(0, folders.IndexOf(assetShelfFolder ?? "All"));
            int nextFolder = EditorGUILayout.Popup(folderIndex, folders.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(170f));
            assetShelfFolder = folders[Mathf.Clamp(nextFolder, 0, folders.Count - 1)];
            assetShelfFavoritesOnly = GUILayout.Toggle(assetShelfFavoritesOnly, new GUIContent("★ Favorites", "Show favorite brushes"), EditorStyles.toolbarButton, GUILayout.Width(78f));
            assetShelfRecentOnly = GUILayout.Toggle(assetShelfRecentOnly, new GUIContent("Recent", "Show recently used brushes"), EditorStyles.toolbarButton, GUILayout.Width(54f));
            if (GUILayout.Button(new GUIContent("New Folder", "Create a brush folder under the current Assets folder"), EditorStyles.toolbarButton, GUILayout.Width(72f))) CreateBrushFolder();
            if (GUILayout.Button(new GUIContent("Library…", "Open the full brush library editor"), EditorStyles.toolbarButton, GUILayout.Width(58f))) BrushEditor.Open();
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
                workspaceRenameBrush = GUILayout.TextField(workspaceRenameBrush, EditorStyles.toolbarTextField, GUILayout.Width(160f));
                if (GUILayout.Button("Rename", EditorStyles.toolbarButton, GUILayout.Width(52f))) RenameBrushAsset(brush, workspaceRenameBrush);
                if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton, GUILayout.Width(60f))) DuplicateBrushAsset(brush);
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
                GUILayout.Label("tags", EditorStyles.miniLabel, GUILayout.Width(28f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawBrushTile(BrushShelfItem item, Rect tile)
        {
            bool selected = brush == item.preset;
            if (Event.current.type == EventType.Repaint)
                (selected ? WorkspaceStyles.AssetSelected : WorkspaceStyles.Asset).Draw(tile, false, false, selected, false);
            Rect preview = new Rect(tile.x + 7f, tile.y + 5f, tile.width - 14f, 55f);
            Texture thumbnail = item.preset.stampTexture != null
                ? AssetPreview.GetAssetPreview(item.preset.stampTexture) ?? AssetPreview.GetMiniThumbnail(item.preset.stampTexture)
                : null;
            DrawBrushThumbnail(preview, item.preset, thumbnail);
            Rect star = new Rect(tile.xMax - 23f, tile.y + 4f, 19f, 19f);
            bool favorite = favoriteBrushGuids.Contains(item.guid);
            if (GUI.Button(star, favorite ? "★" : "☆", WorkspaceStyles.Star)) ToggleFavorite(item.guid);
            GUI.Label(new Rect(tile.x + 5f, preview.yMax + 3f, tile.width - 10f, 18f), item.preset.name, WorkspaceStyles.AssetName);
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
            brush = item.preset;
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
                if (DragAndDrop.objectReferences[i] is BrushPreset || DragAndDrop.objectReferences[i] is Texture2D) { supported = true; break; }
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
                        ShowWorkspaceStatus("Session stamp: " + stamp.name);
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
                brushOrderGuids.Add(guid); brush = copy; workspaceRenameBrush = copy.name;
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
            BeginLayerCreationUndo("Add Paint Layer");
            TexturePaintLayer created = set.AddLayer("Paint Layer " + (set.layers.Count + 1));
            sourceMode = TexturePaintSourceMode.SourceOverlay;
            created.paintSettings = CreatePaintLayerSettings();
            CompleteLayerCreationUndo(created);
            SyncActiveLayerSelection(set);
        }

        private void AddFillLayer(TextureSet set)
        {
            BeginLayerCreationUndo("Add Fill Layer");
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = paintSource,
                sourceTexture = paintSourceTexture,
                sourceOverlay = paintSourceOverlay,
                color = paintColor,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendSharpness = 4f
            };
            TexturePaintLayer created = set.AddFillLayer("Fill Layer " + (set.layers.Count + 1), selectedChannel, settings);
            if (created == null)
            {
                pendingLayerCreationLabel = null;
                ShowWorkspaceStatus(paintSource == TexturePaintBrushSource.Texture && paintSourceTexture == null
                    ? "Select a source texture before adding a Fill layer."
                    : paintSource == TexturePaintBrushSource.Overlay && paintSourceOverlay == null
                        ? "Select an OverlayData source before adding a Fill layer."
                        : "The Fill source could not be generated for the selected channel.");
                return;
            }
            CompleteLayerCreationUndo(created);
            SyncActiveLayerSelection(set);
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
            MergeLayerWithHistory(set, set.activeLayerIndex);
            SyncActiveLayerSelection(set); MarkDocumentDirty();
        }

        private void DeleteLayer(TextureSet set, int index, bool confirm)
        {
            if (set == null || (uint)index >= (uint)set.layers.Count) return;
            TexturePaintLayer layer = set.layers[index];
            if (confirm && !EditorUtility.DisplayDialog("Delete Texture Layer", $"Delete '{layer.name}'? You can restore it with Undo.", "Delete", "Cancel")) return;
            DeleteLayerWithHistory(set, index);
            SyncActiveLayerSelection(set); MarkDocumentDirty();
        }

        private void ShowLayerMenu(TextureSet set, TexturePaintLayer layer, int index)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename\tF2"), false, () => BeginLayerRename(layer));
            menu.AddItem(new GUIContent("Duplicate\tCtrl+D"), false, () =>
            { set.activeLayerIndex = index; DuplicateActiveLayer(set); });
            if (index > 0) menu.AddItem(new GUIContent("Merge Down"), false, () =>
            { set.activeLayerIndex = index; MergeActiveLayer(set); });
            else menu.AddDisabledItem(new GUIContent("Merge Down"));
            menu.ShowAsContext();
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
            if (layer.kind == TexturePaintLayerKind.Spline) return "Path · " + layer.name;
            if (layer.kind == TexturePaintLayerKind.Fill) return "Fill · " + layer.name;
            if (layer.kind == TexturePaintLayerKind.Group) return "Folder · " + layer.name;
            return layer.name;
        }

        private static string LayerSubtitle(TexturePaintLayer layer)
        {
            if (!string.IsNullOrEmpty(layer.pluginId)) return layer.pluginId + " · " + layer.pluginVersion;
            if (layer.IsSplineLayer) return layer.spline.PointCount + " points · " + layer.blendMode;
            return Mathf.RoundToInt(layer.opacity * 100f) + "% · " + layer.blendMode;
        }

        private void InitializeWorkspaceUI()
        {
            if (workspaceInitialized) return;
            workspaceInitialized = true;
            workspaceBrushesDirty = true;
            favoriteBrushGuids ??= new List<string>(); recentBrushGuids ??= new List<string>(); brushOrderGuids ??= new List<string>();
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
            workspaceDebugMaterials.Clear(); workspaceUVEdges.Clear(); workspaceUVLineBuffer = null;
        }

        private void OnWorkspaceProjectChanged() => workspaceBrushesDirty = true;

        private void ApplyWorkspaceDisplay()
        {
            if (controller?.Textures == null) return;
            bool diagnostic = channelSolo;
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
                Texture texture = GetWorkspacePreviewTexture(set, previewBefore);
                debug.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
                debug.color = Color.white;
                renderer.sharedMaterial = debug;
            }
            workspacePreviewBeforeApplied = previewBefore;
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

        private Texture GetWorkspacePreviewTexture(TextureSet set, bool before)
        {
            TextureChannelTarget target = set?.GetChannel(selectedChannel);
            if (target == null) return null;
            return before ? target.sourceTexture : target.PreviewTexture;
        }

        private void ApplySceneViewDisplay(SceneView sceneView)
        {
            if (sceneView == null) return;
            ApplyWorkspaceDisplay();
        }

        private void SampleSurfaceColor(ReconstructedSurface surface, Vector2 uv, bool before)
        {
            TextureSet set = controller?.Textures?.FindSet(surface.index);
            Texture texture = GetWorkspacePreviewTexture(set, before);
            if (set == null || texture == null) return;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
                if (ReferenceEquals(controller.Textures.Sets[i], set)) { selectedSurface = i; break; }
            surface.TryUVToWorld(uv, -1, out _, out _, out int sampledTriangle, out _);
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
                if (brush != null) EditorUtility.SetDirty(brush);
                ShowWorkspaceStatus(current.shift ? $"Hardness {ActiveBrush.hardness:0.00}" : $"Size {ActiveBrush.size:0.000}");
                CommitPathRenderParameterChange(hadPathRenderState, pathSetBefore, pathLayerBefore,
                    pathSettingsBefore, pathSignatureBefore, "Adjust Path Brush");
                current.Use(); return true;
            }
            if (current.keyCode >= KeyCode.Alpha1 && current.keyCode <= KeyCode.Alpha7)
            {
                int channel = (int)current.keyCode - (int)KeyCode.Alpha1;
                selectedChannel = (TexturePaintChannel)Mathf.Clamp(channel, 0, 6);
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
                case KeyCode.N: tool = TexturePaintTool.NormalTouchup; selectedChannel = TexturePaintChannel.Normal; break;
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

        private bool HandleBrushModifierDrag(Event current)
        {
            if (current == null) return false;
            if (current.type == EventType.MouseDown && current.button == 1 && current.shift)
            {
                modifierBrushDrag = true; modifierBrushStartMouse = current.mousePosition;
                modifierBrushStartSize = ActiveBrush.size; modifierBrushStartHardness = ActiveBrush.hardness;
                modifierPathEditSet = null;
                modifierPathUndoStarted = false;
                TextureSet activeSet = ActiveTextureSet;
                if (TryGetActivePathLayer(activeSet, out _)) modifierPathEditSet = activeSet;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); current.Use(); return true;
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
                if (brush != null) EditorUtility.SetDirty(brush);
                if (modifierPathEditSet != null) CompleteLightweightPathEdit(modifierPathEditSet, true);
                ShowWorkspaceStatus($"Size {ActiveBrush.size:0.000} · Hardness {ActiveBrush.hardness:0.00}");
                current.Use(); return true;
            }
            if (modifierBrushDrag && (current.rawType == EventType.MouseUp || current.type == EventType.MouseLeaveWindow))
            {
                modifierBrushDrag = false;
                modifierPathEditSet = null;
                modifierPathUndoStarted = false;
                GUIUtility.hotControl = 0; current.Use(); return true;
            }
            return false;
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
            menu.AddItem(new GUIContent("Asset Shelf\tTab"), workspaceShowAssetShelf, () =>
            {
                workspaceShowAssetShelf = !workspaceShowAssetShelf;
                RefreshWorkspaceView();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Solo Selected Channel"), channelSolo, () =>
            {
                channelSolo = !channelSolo;
                if (channelSolo) previewBefore = false;
                RefreshWorkspaceView();
            });
            menu.AddItem(new GUIContent("Before in 3D"), previewBefore, () =>
            {
                previewBefore = !previewBefore;
                if (previewBefore) channelSolo = false;
                RefreshWorkspaceView();
            });
            menu.AddItem(new GUIContent("Isolate Selected Slots"), isolateSelectedSlots, () =>
            {
                isolateSelectedSlots = !isolateSelectedSlots;
                RefreshWorkspaceView();
            });
            menu.DropDown(anchor);
        }

        private void RefreshWorkspaceView()
        {
            ApplyWorkspaceDisplay();
            RepaintAll();
        }

        private void ShowLayoutMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Tool Rail"), workspaceShowToolRail, () =>
            { workspaceShowToolRail = !workspaceShowToolRail; RepaintAll(); });
            menu.AddItem(new GUIContent("Targets"), workspaceShowTargets, () =>
            { workspaceShowTargets = !workspaceShowTargets; RepaintAll(); });
            menu.AddItem(new GUIContent("Layers / Paths"), workspaceShowLayers, () =>
            { workspaceShowLayers = !workspaceShowLayers; RepaintAll(); });
            menu.AddItem(new GUIContent("Properties"), workspaceShowProperties, () =>
            { workspaceShowProperties = !workspaceShowProperties; RepaintAll(); });
            menu.AddItem(new GUIContent("Asset Shelf"), workspaceShowAssetShelf, () =>
            { workspaceShowAssetShelf = !workspaceShowAssetShelf; RepaintAll(); });
            menu.AddItem(new GUIContent("Open 2D Canvas"), false, TexturePaintUVWindow.ShowDockable);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Reset Workspace"), false, () =>
            {
                workspaceLeftWidth = 238f; workspaceRightWidth = 318f; workspaceShelfHeight = 178f;
                workspaceShowToolRail = workspaceShowTargets = workspaceShowLayers = workspaceShowProperties = workspaceShowAssetShelf = true;
                workspaceUVPan = Vector2.zero; workspaceUVZoom = 1f; RepaintAll();
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
            GUILayout.Label(title, WorkspaceStyles.SectionHeader);
            GUILayout.BeginVertical(WorkspaceStyles.PropertyBox);
            body?.Invoke();
            GUILayout.EndVertical();
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
                case TexturePaintChannel.Albedo: return Color.white;
                case TexturePaintChannel.AmbientOcclusion: return Color.white;
                default: return Color.black;
            }
        }

        private TextureSet ActiveTextureSet => controller?.Textures != null && controller.Textures.Sets.Count > 0
            ? controller.Textures.Sets[Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1)] : null;

        private static Rect Shrink(Rect rect, float amount) => new Rect(rect.x + amount, rect.y + amount,
            Mathf.Max(0f, rect.width - amount * 2f), Mathf.Max(0f, rect.height - amount * 2f));

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
            public static readonly GUIStyle Rail = new GUIStyle("ProjectBrowserBottomBarBg") { padding = new RectOffset(2, 2, 2, 2) };
            public static readonly GUIStyle RailButton = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 10,
                fixedWidth = 0f, fixedHeight = 0f, stretchWidth = true, stretchHeight = true,
                margin = new RectOffset(2, 2, 1, 1), padding = new RectOffset(1, 1, 1, 1),
                overflow = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip
            };
            public static readonly GUIStyle RegionHeader = new GUIStyle(EditorStyles.miniBoldLabel)
            { alignment = TextAnchor.MiddleLeft };
            public static readonly GUIStyle SectionHeader = new GUIStyle(EditorStyles.miniBoldLabel)
            { margin = new RectOffset(5, 3, 3, 2), normal = { textColor = new Color(0.55f, 0.75f, 1f) } };
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
            public static readonly GUIStyle Star = new GUIStyle(EditorStyles.miniButton)
            { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 13 };
            public static readonly GUIStyle CanvasBadge = new GUIStyle(EditorStyles.toolbarButton)
            { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            public static readonly GUIStyle CanvasHint = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.72f) } };
            public static readonly GUIStyle CenterMessage = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 12 };
        }
    }
}
