#if UNITY_EDITOR
#if UNITY_6000_4_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UMA.CharacterSystem;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    [Graph("umawardroberecipegraph", GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    internal sealed class UMAWardrobeRecipeGraph : Graph
    {
    }

    [UseWithGraph(typeof(UMAWardrobeRecipeGraph))]
    internal sealed class UMAWardrobeRecipeOutputGraphNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("Slots").WithDataType(typeof(SlotDataAsset)).Build();
        }
    }

    [UseWithGraph(typeof(UMAWardrobeRecipeGraph))]
    internal sealed class UMASlotDataAssetGraphNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("Overlays").WithDataType(typeof(OverlayDataAsset)).Build();
            context.AddOutputPort("Slot").WithDataType(typeof(SlotDataAsset)).Build();
        }
    }

    [UseWithGraph(typeof(UMAWardrobeRecipeGraph))]
    internal sealed class UMAOverlayDataAssetGraphNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort("Color").WithDataType(typeof(OverlayColorData)).Build();
            context.AddOutputPort("Overlay").WithDataType(typeof(OverlayDataAsset)).Build();
        }
    }

    [UseWithGraph(typeof(UMAWardrobeRecipeGraph))]
    internal sealed class UMAOverlayColorDataGraphNode : Node
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort("Color").WithDataType(typeof(OverlayColorData)).Build();
        }
    }

    public sealed class UMAWardrobeRecipeGraphEditorWindow : EditorWindow, CharacterBaseEditor.IRecipeWorkingCopyOwner, CharacterBaseEditor.IRecipeWorkingCopySerializer
    {
        private const float NodeWidth = 230f;
        private const float NodeHeight = 64f;
        private const float ColumnSpacing = 280f;
        private const float RowSpacing = 22f;
        private const float ToolbarHeight = 24f;
        private const float NodeListWidth = 220f;
        private const float NodeListGap = 4f;
        private const float NodeListPadding = 8f;
        private const float NodeListItemHeight = 26f;
        private const float NodeListItemSpacing = 6f;
        private const float NodeListItemRadius = 6f;
        private const float NodeListSelectionBorder = 2f;
        private const float NodeListTextPadding = 8f;
        private const float InspectorSplitterWidth = 8f;
        private const float InspectorDefaultWidthRatio = 0.36f;
        private const float InspectorDefaultMinWidth = 380f;
        private const float InspectorDefaultMaxWidth = 560f;
        private const float InspectorMinWidth = 320f;
        private const float InspectorMaxWidth = 760f;
        private const float GraphMinWidth = 320f;
        private const float NodeHeaderHeight = 18f;
        private const float NoteDefaultWidth = 260f;
        private const float NoteDefaultHeight = 150f;
        private const float NoteMinWidth = 150f;
        private const float NoteMinHeight = 90f;
        private const float NoteResizeHandleSize = 14f;
        private const float PortHitRadius = 12f;
        private const float EdgeHitDistance = 8f;
        private const float EdgeStrokeWidth = 4f;
        private const float DragEdgeStrokeWidth = 5f;
        private const float EdgeContrastStrokePadding = 2f;
        private const float SlotInputTopPadding = 30f;
        private const float SlotInputBottomPadding = 12f;
        private const float SlotInputSpacing = 16f;
        private const int NodeHeaderFontSize = 10;
        private const int NodeTitleFontSize = 12;
        private const int NodeSubtitleFontSize = 9;
        private const int NodeWarningFontSize = 9;
        private const int NoteTextFontSize = 11;

        private static readonly Color SharedColorTint = new Color(0.62f, 0.78f, 1f);
        private static readonly Color OverlayTint = new Color(0.62f, 0.90f, 0.68f);
        private static readonly Color SlotTint = new Color(0.88f, 0.82f, 0.64f);
        private static readonly Color OutputTint = new Color(0.86f, 0.86f, 0.92f);
        private static readonly Color NoteTint = new Color(1f, 0.94f, 0.58f);
        private static readonly Color SelectionOutlineTint = new Color(1f, 0.76f, 0.16f);
        private static readonly Color SelectionFillTint = new Color(0.25f, 0.55f, 1f, 0.15f);
        private static readonly Color EdgeContrastTint = new Color(0f, 0f, 0f, 0.38f);
        private static readonly Color ColorConnectionTint = new Color(0.10f, 0.56f, 1f);
        private static readonly Color OverlayConnectionTint = new Color(0.10f, 0.86f, 0.28f);
        private static readonly Color SlotConnectionTint = new Color(1f, 0.58f, 0.12f);
        private static readonly Dictionary<int, Texture2D> NodeListRoundedTextures = new Dictionary<int, Texture2D>();
        private static int s_nextObjectPickerControlId = 46000;

        private enum NodeKind
        {
            Output,
            Slot,
            Overlay,
            SharedColor,
            Note
        }

        private enum GraphConnectionKind
        {
            SlotToOutput,
            OverlayToSlot,
            SharedColorToOverlay
        }

        private enum PendingObjectPickerKind
        {
            None,
            AddSlot,
            AddOverlay,
            AddDetachedOverlay,
            AddSlotThenOverlay
        }

        private enum InspectorMode
        {
            Selection,
            Recipe,
            LegacyInspector
        }

        private sealed class GraphNode
        {
            public NodeKind Kind;
            public string Key;
            public string AssetKey;
            public string Title;
            public string Subtitle;
            public Rect Rect;
            public Color Tint;
            public SlotData Slot;
            public OverlayData Overlay;
            public OverlayColorData SharedColor;
            public int SlotIndex = -1;
            public int OverlayIndex = -1;
            public int SharedColorIndex = -1;
            public bool HasWarning;
            public string Warning;
            public NoteData Note;
        }

        private readonly struct GraphEdge
        {
            public readonly GraphNode From;
            public readonly GraphNode To;
            public readonly Color Color;
            public readonly GraphConnectionKind Kind;
            public readonly int ToPortIndex;

            public GraphEdge(GraphNode from, GraphNode to, Color color, GraphConnectionKind kind, int toPortIndex = -1)
            {
                From = from;
                To = to;
                Color = color;
                Kind = kind;
                ToPortIndex = toPortIndex;
            }
        }

        private readonly struct GraphPortHit
        {
            public readonly GraphNode Node;
            public readonly int PortIndex;

            public GraphPortHit(GraphNode node, int portIndex)
            {
                Node = node;
                PortIndex = portIndex;
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            private ReferenceComparer()
            {
            }

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        [Serializable]
        private sealed class ViewData
        {
            public float zoom = 1f;
            public Vector2 pan = Vector2.zero;
            public float inspectorWidth;
        }

        [Serializable]
        private sealed class LayoutData
        {
            public List<string> keys = new List<string>();
            public List<Vector2> positions = new List<Vector2>();
            public List<NoteData> notes = new List<NoteData>();
            public List<DetachedLayer> detached = new List<DetachedLayer>();
        }

        [Serializable]
        private sealed class DetachedLayer
        {
            public string assetId;
            public UMAPackedRecipeBase.PackedOverlayDataV3 overlay;
            public UMAPackedRecipeBase.PackedOverlayColorDataV3 color;
            public int sharedIndex = -1;
        }

        [Serializable]
        internal sealed class NoteData
        {
            public string id;
            public string text;
            public Rect rect;
        }

        [SerializeField] private UMAWardrobeRecipe _asset;
        // A decoded view of the asset. Every recipe edit is packed back into the asset in the same Undo group.
        [NonSerialized] private UMAData.UMARecipe _recipe;
        private SerializedObject _serializedRecipe;
        private DNAMasterEditor _dnaEditor;
        private SharedColorsCollectionEditor _sharedColorsEditor;
        private Editor _legacyInspector;
        private string _errorMessage;
        // The recipe did not load completely (a missing slot or overlay): saving it would remove those for good.
        private bool _loadIncomplete;
        // The recipe text the graph loaded or last saved. A different text in the asset means it was
        // changed outside the graph (the Inspector, Undo, another tool).
        private string _loadedRevision;
        private string _loadedGraphData;
        // The slot and overlay editors of the selected node, kept between draws: an asset picker they
        // open reports its pick to the editor that opened it.
        private SlotEditor _slotEditor;
        private SlotData _slotEditorSlot;
        private UMAData.UMARecipe _slotEditorRecipe;
        private OverlayEditor _overlayEditor;
        private OverlayData _overlayEditorOverlay;
        private UMAData.UMARecipe _overlayEditorRecipe;
        // The overlay list (and its order) the cached slot editor was built from. A reorder in the graph
        // left the editor's rows in the old order, and Remove on a row then removed another overlay.
        private List<OverlayData> _slotEditorOverlays;
        private readonly List<OverlayData> _slotEditorOverlayOrder = new List<OverlayData>();
        private int _assetDirtyCount;

        private readonly List<GraphNode> _nodes = new List<GraphNode>();
        private readonly List<GraphEdge> _edges = new List<GraphEdge>();
        private readonly Dictionary<string, Vector2> _layoutPositions = new Dictionary<string, Vector2>();
        private readonly Dictionary<SlotData, GraphNode> _slotNodes = new Dictionary<SlotData, GraphNode>();
        private readonly Dictionary<OverlayData, GraphNode> _overlayNodes = new Dictionary<OverlayData, GraphNode>(ReferenceComparer<OverlayData>.Instance);
        private readonly Dictionary<object, string> _nodeKeys = new Dictionary<object, string>(ReferenceComparer<object>.Instance);
        private readonly Dictionary<OverlayColorData, GraphNode> _sharedColorNodes = new Dictionary<OverlayColorData, GraphNode>(ReferenceComparer<OverlayColorData>.Instance);
        private readonly List<NoteData> _notes = new List<NoteData>();
        private readonly List<OverlayData> _detachedOverlays = new List<OverlayData>();
        internal IReadOnlyList<OverlayData> DetachedOverlays => _detachedOverlays;
        internal IReadOnlyList<NoteData> Notes => _notes;
        private readonly HashSet<string> _selectedKeys = new HashSet<string>();
        private readonly HashSet<string> _selectionBoxInitialKeys = new HashSet<string>();
        private readonly Dictionary<string, Vector2> _dragStartPositions = new Dictionary<string, Vector2>();

        private Vector2 _inspectorScroll;
        private Vector2 _nodeListScroll;
        private Vector2 _recipeScroll;
        private Vector2 _legacyScroll;
        private Vector2 _pan;
        private float _zoom = 1f;
        private float _inspectorWidth;
        private string _selectedKey;
        private GraphNode _selectedNode;
        private string _draggingNodeKey;
        private bool _draggingConnection;
        private bool _disconnectConnectionOnCancel;
        private GraphConnectionKind _draggingConnectionKind;
        private GraphNode _connectionDragSourceNode;
        private GraphNode _connectionDragOriginalTargetNode;
        private Vector2 _connectionDragGraphMouse;
        private string _resizingNoteKey;
        private Vector2 _dragStartGraphMouse;
        private Rect _resizeStartRect;
        private bool _draggingSelectionBox;
        private bool _selectionBoxAdditive;
        private Vector2 _selectionStartGraph;
        private Vector2 _selectionCurrentGraph;
        private bool _draggingCanvas;
        private bool _resizingInspector;
        [SerializeField] private bool _autoSave = CharacterBaseEditor._AutomaticUpdates;
        internal bool AutoSave
        {
            get => _autoSave;
            set
            {
                if (_autoSave == value) return;
                _autoSave = value;
                if (value) SaveAssetToDisk();
            }
        }
        private InspectorMode _inspectorMode = InspectorMode.Selection;
        private int _selectedSharedColorForOverlay = -1;
        private int _selectedSuppressedSlot;
        private int _selectedHideSlot;
        private int _selectedReplaceSlot;
        private int _selectedWardrobeSlot;
        private int _selectedRaceForDna;
        private int _selectedDna;
        private string _layoutPrefsKey;
        private PendingObjectPickerKind _pendingObjectPickerKind;
        private int _objectPickerControlId;
        private Vector2 _pendingAddGraphPosition;
        private Vector2 _pendingSlotGraphPosition;
        private SlotData _pendingOverlayTargetSlot;
        private string _focusedSharedColorInspectorKey;

        [MenuItem("Assets/Edit in Node Graph", true)]
        private static bool ValidateEditInNodeGraph()
        {
            return Selection.activeObject is UMAWardrobeRecipe;
        }

        [MenuItem("Assets/Edit in Node Graph", false, 2010)]
        private static void EditInNodeGraph()
        {
            Open(Selection.activeObject as UMAWardrobeRecipe);
        }

        public static void Open(UMAWardrobeRecipe recipe)
        {
            UMAWardrobeRecipeGraphEditorWindow window = GetWindow<UMAWardrobeRecipeGraphEditorWindow>("UMA Wardrobe Graph");
            window.minSize = new Vector2(1100f, 680f);
            window.LoadRecipe(recipe);
            window.Show();
            window.Focus();
        }

        // The slot and overlay controls in the inspector panel write the graph's working recipe themselves.
        Object CharacterBaseEditor.IRecipeWorkingCopyOwner.WorkingCopyAsset => _asset;
        UMAData.UMARecipe CharacterBaseEditor.IRecipeWorkingCopyOwner.WorkingCopy => _recipe;
        bool CharacterBaseEditor.IRecipeWorkingCopyOwner.WorkingCopyInSync =>
            _asset != null && !_loadIncomplete && _loadedRevision == _asset.recipeString && _loadedGraphData == _asset.editorGraphData;

        bool CharacterBaseEditor.IRecipeWorkingCopyOwner.WritesToDisk => _autoSave;

        void CharacterBaseEditor.IRecipeWorkingCopySerializer.PackWorkingCopy()
        {
            BuildGraph();
            string graphData = SerializeGraphData();
            SaveKeepingOverlayLists(_asset, _recipe);
            _asset.editorGraphData = graphData;
        }

        /// <summary>
        /// Saves the recipe into <paramref name="target"/>, leaving its slots with their own overlay lists. Saving
        /// stores slots whose overlays all match as one stack, and gave those slots one shared list in memory too:
        /// with every graph edit saved, the second slot's overlay nodes vanished as soon as the stacks matched, and
        /// editing one slot then changed both. The file is the same; the graph keeps the stacks apart while editing.
        /// </summary>
        private static void SaveKeepingOverlayLists(UMARecipeBase target, UMAData.UMARecipe recipe)
        {
            SlotData[] slots = recipe.slotDataList;
            List<OverlayData>[] lists = null;
            if (slots != null)
            {
                lists = new List<OverlayData>[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    lists[i] = slots[i]?.GetOverlayList();
                }
            }
            try
            {
                target.Save(recipe);
            }
            finally
            {
                for (int i = 0; slots != null && i < slots.Length; i++)
                {
                    if (slots[i] != null && lists[i] != null)
                    {
                        slots[i].SetOverlayList(lists[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Gives a slot that shares another slot's overlay stack its own copy of those overlays (linked to the same
        /// shared colors), so its overlays can be edited without changing the other slot. While the two stacks still
        /// match, the recipe file keeps storing them once.
        /// </summary>
        internal bool GiveSlotItsOwnOverlays(SlotData slot)
        {
            if (slot == null || _recipe == null || !IsSharedOverlayStackSecondary(slot))
            {
                return false;
            }
            var own = new List<OverlayData>();
            foreach (OverlayData overlay in slot.GetOverlayList())
            {
                if (overlay == null) continue;
                OverlayData copy = overlay.Duplicate();
                if (overlay.colorData != null && _recipe.sharedColors != null &&
                    Array.Exists(_recipe.sharedColors, color => ReferenceEquals(color, overlay.colorData)))
                {
                    copy.colorData = overlay.colorData;
                }
                own.Add(copy);
            }
            slot.SetOverlayList(own);
            CommitRecipeEdit("Give Wardrobe Slot Its Own Overlays");
            return true;
        }

        void CharacterBaseEditor.IRecipeWorkingCopyOwner.OnWorkingCopyWritten(bool saved)
        {
            _loadedRevision = _asset.recipeString;
            _loadedGraphData = _asset.editorGraphData;
            _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
            if (!_autoSave)
            {
                UMAUpdateProcessor.UpdateRecipe(_asset);
            }
            Repaint();
        }

        void CharacterBaseEditor.IRecipeWorkingCopyOwner.OnWorkingCopyEditPending()
        {
            // An asynchronous control still held an older view. Keep the asset's current contents.
            ReloadRecipeData();
            ShowNotification(new GUIContent("The recipe changed. Please repeat that edit."));
        }

        private void OnEnable()
        {
            CharacterBaseEditor.RegisterWorkingCopyOwner(this);
            Undo.undoRedoPerformed -= OnUndoRedoGraphEdit;
            Undo.undoRedoPerformed += OnUndoRedoGraphEdit;
            LoadRecipe(_asset != null ? _asset : Selection.activeObject as UMAWardrobeRecipe);
        }

        private void OnDisable()
        {
            FinishNoteTyping();
            CharacterBaseEditor.UnregisterWorkingCopyOwner(this);
            Undo.undoRedoPerformed -= OnUndoRedoGraphEdit;
            CharacterBaseEditor.SetRecipeSaveBlocked(_asset, false, this);
            SaveView();
            DestroyLegacyInspector();
            _serializedRecipe?.Dispose();
            _serializedRecipe = null;
            _recipe = null;
        }

        private void OnLostFocus()
        {
            FinishNoteTyping();
        }

        private void OnGUI()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorGUILayout.HelpBox("Unity is compiling/updating. Please wait before editing wardrobe recipes.", MessageType.Info);
                return;
            }
            // Control names are registered as controls are drawn, so whether a note's text has focus is read at
            // the end of the previous pass; asked here, before anything is drawn, it never matched.
            bool noteTextFocused = _noteTextFocused;
            _noteTextFocused = false;
            if (HandleNoteUndoShortcut(Event.current, noteTextFocused ? NoteTextControlName : null))
            {
                GUIUtility.ExitGUI();
            }
            if (_typingNoteId != null && GUIUtility.keyboardControl != _typingKeyboardControl)
            {
                FinishNoteTyping();
            }
            if (_asset != null && (_asset.recipeString != _loadedRevision || _asset.editorGraphData != _loadedGraphData))
            {
                ReloadRecipeData();
            }
            if (_asset != null)
            {
                _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
            }
            HandleObjectPickerEvents();
            DrawToolbar();

            if (_asset != null && _loadIncomplete)
            {
                EditorGUILayout.HelpBox(_errorMessage + " Restore the missing data and refresh, or use Save As to edit a copy of the parts that loaded.", MessageType.Error);
                return;
            }
            if (_asset == null || _recipe == null)
            {
                DrawEmptyState();
                return;
            }

            BuildGraph();

            Rect body = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight);
            float nodeListWidth = GetNodeListWidth(body.width);
            Rect nodeListRect = new Rect(body.x, body.y, nodeListWidth, body.height);
            float workspaceX = nodeListRect.xMax + NodeListGap;
            float workspaceWidth = Mathf.Max(1f, body.xMax - workspaceX);
            Rect workspaceRect = new Rect(workspaceX, body.y, workspaceWidth, body.height);
            float inspectorWidth = GetInspectorWidth(workspaceWidth);
            Rect graphRect = new Rect(workspaceX, body.y, Mathf.Max(1f, workspaceWidth - inspectorWidth - InspectorSplitterWidth), body.height);
            Rect splitterRect = new Rect(graphRect.xMax, body.y, InspectorSplitterWidth, body.height);
            Rect inspectorRect = new Rect(splitterRect.xMax, body.y, inspectorWidth, body.height);

            HandleInspectorSplitter(splitterRect, workspaceRect);
            DrawNodeList(nodeListRect);
            DrawGraph(graphRect);
            DrawInspectorSplitter(splitterRect);
            DrawInspector(inspectorRect);
            _noteTextFocused = IsNoteTextControl(GUI.GetNameOfFocusedControl());
        }

        private static float GetNodeListWidth(float bodyWidth)
        {
            float availableWidth = bodyWidth - GraphMinWidth - InspectorMinWidth - InspectorSplitterWidth - NodeListGap;
            return Mathf.Clamp(Mathf.Min(NodeListWidth, availableWidth), 0f, NodeListWidth);
        }

        private float GetInspectorWidth(float bodyWidth)
        {
            float requestedWidth = _inspectorWidth > 0f ? _inspectorWidth : GetDefaultInspectorWidth(bodyWidth);
            return ClampInspectorWidth(requestedWidth, bodyWidth);
        }

        private static float GetDefaultInspectorWidth(float bodyWidth)
        {
            return Mathf.Clamp(bodyWidth * InspectorDefaultWidthRatio, InspectorDefaultMinWidth, InspectorDefaultMaxWidth);
        }

        private static float ClampInspectorWidth(float width, float bodyWidth)
        {
            float availableWidth = Mathf.Max(0f, bodyWidth - InspectorSplitterWidth);
            float maxWidth = Mathf.Min(InspectorMaxWidth, Mathf.Max(0f, availableWidth - GraphMinWidth));
            if (maxWidth <= 0f)
            {
                return availableWidth;
            }

            float minWidth = Mathf.Min(InspectorMinWidth, maxWidth);
            return Mathf.Clamp(width, minWidth, maxWidth);
        }

        private void HandleInspectorSplitter(Rect splitterRect, Rect bodyRect)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive, splitterRect);
            Rect hitRect = new Rect(splitterRect.x - 2f, splitterRect.y, splitterRect.width + 4f, splitterRect.height);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal, controlId);

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && hitRect.Contains(current.mousePosition))
                    {
                        _resizingInspector = true;
                        GUIUtility.hotControl = controlId;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && _resizingInspector)
                    {
                        _inspectorWidth = ClampInspectorWidth(bodyRect.xMax - current.mousePosition.x - InspectorSplitterWidth * 0.5f, bodyRect.width);
                        Repaint();
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && _resizingInspector)
                    {
                        _resizingInspector = false;
                        GUIUtility.hotControl = 0;
                        SaveView();
                        Repaint();
                        current.Use();
                    }
                    break;
            }
        }

        private void DrawInspectorSplitter(Rect splitterRect)
        {
            Color background = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.68f, 0.68f, 0.68f);
            Color center = _resizingInspector
                ? new Color(0.25f, 0.55f, 1f, 0.75f)
                : EditorGUIUtility.isProSkin ? new Color(0.34f, 0.34f, 0.34f) : new Color(0.48f, 0.48f, 0.48f);
            EditorGUI.DrawRect(splitterRect, background);
            EditorGUI.DrawRect(new Rect(splitterRect.center.x - 1f, splitterRect.y + 4f, 2f, Mathf.Max(0f, splitterRect.height - 8f)), center);
        }

        private void DrawNodeList(Rect nodeListRect)
        {
            if (nodeListRect.width <= 1f || nodeListRect.height <= 1f)
            {
                return;
            }

            GUI.Box(nodeListRect, GUIContent.none, EditorStyles.helpBox);
            Rect contentRect = new Rect(
                nodeListRect.x + NodeListPadding,
                nodeListRect.y + NodeListPadding,
                Mathf.Max(1f, nodeListRect.width - NodeListPadding * 2f),
                Mathf.Max(1f, nodeListRect.height - NodeListPadding * 2f));

            float totalHeight = Mathf.Max(contentRect.height, _nodes.Count * NodeListItemHeight + Mathf.Max(0, _nodes.Count - 1) * NodeListItemSpacing);
            float scrollbarWidth = totalHeight > contentRect.height ? 16f : 0f;
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(1f, contentRect.width - scrollbarWidth), totalHeight);

            _nodeListScroll = GUI.BeginScrollView(contentRect, _nodeListScroll, viewRect);
            try
            {
                float itemY = 0f;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    GraphNode node = _nodes[i];
                    if (node == null)
                    {
                        continue;
                    }

                    Rect itemRect = new Rect(0f, itemY, viewRect.width, NodeListItemHeight);
                    DrawNodeListItem(itemRect, node);
                    itemY += NodeListItemHeight + NodeListItemSpacing;
                }
            }
            finally
            {
                GUI.EndScrollView();
            }
        }

        private void DrawNodeListItem(Rect itemRect, GraphNode node)
        {
            bool selected = _selectedKeys.Contains(node.Key);
            Rect fillRect = itemRect;
            Color fillColor = selected ? Color.Lerp(node.Tint, Color.white, 0.18f) : node.Tint;

            if (selected)
            {
                GUI.Box(itemRect, GUIContent.none, CreateNodeListRoundedStyle(SelectionOutlineTint));
                fillRect = InsetRect(itemRect, NodeListSelectionBorder);
            }

            GUI.Box(fillRect, GUIContent.none, CreateNodeListRoundedStyle(fillColor));

            Rect labelRect = new Rect(
                fillRect.x + NodeListTextPadding,
                fillRect.y,
                Mathf.Max(0f, fillRect.width - NodeListTextPadding * 2f),
                fillRect.height);
            GUI.Label(labelRect, node.Title ?? string.Empty, CreateNodeListTextStyle());

            if (GUI.Button(itemRect, GUIContent.none, GUIStyle.none))
            {
                SelectOnly(node);
                Repaint();
            }
        }

        private static Rect InsetRect(Rect rect, float inset)
        {
            return new Rect(rect.x + inset, rect.y + inset, Mathf.Max(0f, rect.width - inset * 2f), Mathf.Max(0f, rect.height - inset * 2f));
        }

        private static GUIStyle CreateNodeListRoundedStyle(Color color)
        {
            GUIStyle style = new GUIStyle(GUIStyle.none);
            style.normal.background = GetNodeListRoundedTexture(color);
            int border = Mathf.CeilToInt(NodeListItemRadius);
            style.border = new RectOffset(border, border, border, border);
            return style;
        }

        private static GUIStyle CreateNodeListTextStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontSize = 11,
                padding = new RectOffset()
            };
            style.normal.textColor = Color.black;
            style.hover.textColor = Color.black;
            style.active.textColor = Color.black;
            style.focused.textColor = Color.black;
            return style;
        }

        private static Texture2D GetNodeListRoundedTexture(Color color)
        {
            Color32 color32 = color;
            int key = color32.r | (color32.g << 8) | (color32.b << 16) | (color32.a << 24);
            if (NodeListRoundedTextures.TryGetValue(key, out Texture2D texture) && texture != null)
            {
                return texture;
            }

            const int size = 16;
            texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = IsInsideRoundedRect(x + 0.5f, y + 0.5f, size, size, NodeListItemRadius) ? color : clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            NodeListRoundedTextures[key] = texture;
            return texture;
        }

        private static bool IsInsideRoundedRect(float x, float y, float width, float height, float radius)
        {
            float closestX = Mathf.Clamp(x, radius, width - radius);
            float closestY = Mathf.Clamp(y, radius, height - radius);
            float deltaX = x - closestX;
            float deltaY = y - closestY;
            return deltaX * deltaX + deltaY * deltaY <= radius * radius;
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight));

            EditorGUI.BeginChangeCheck();
            UMAWardrobeRecipe newAsset = (UMAWardrobeRecipe)EditorGUILayout.ObjectField(_asset, typeof(UMAWardrobeRecipe), false, GUILayout.Width(310f));
            if (EditorGUI.EndChangeCheck())
            {
                LoadRecipe(newAsset);
            }

            using (new EditorGUI.DisabledScope(_asset == null))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    SaveAssetToDisk(importLegacyLayout: true);
                }

                if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                {
                    SaveAsRecipe();
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                {
                    ReloadRecipeData();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    FrameGraph();
                }
            }

            // The graph's own setting. Copying it into the inspectors' shared Automatic Updates on every repaint
            // switched them back on after they had been turned off in an inspector.
            AutoSave = GUILayout.Toggle(AutoSave, "Auto Save", EditorStyles.toolbarButton, GUILayout.Width(82f));

            GUILayout.Space(8f);
            GUILayout.Label("Graph Toolkit", EditorStyles.miniLabel, GUILayout.Width(86f));
            GUILayout.Label("Unity 6.4 module", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(110f));

            GUILayout.FlexibleSpace();

            if (_asset != null && EditorUtility.IsDirty(_asset))
            {
                GUILayout.Label("Unsaved", EditorStyles.miniBoldLabel, GUILayout.Width(62f));
            }

            GUILayout.EndHorizontal();
        }

        private void DrawEmptyState()
        {
            GUILayout.Space(10f);
            EditorGUILayout.HelpBox("Select a UMAWardrobeRecipe in the toolbar, or right-click one in the Project window and choose Edit in Node Graph.", MessageType.Info);

            Rect dropRect = GUILayoutUtility.GetRect(0f, 130f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop a UMAWardrobeRecipe here", EditorStyles.helpBox);
            HandleRecipeDrop(dropRect);
        }

        internal void LoadRecipe(UMAWardrobeRecipe recipe)
        {
            if (_asset == recipe && _recipe != null)
            {
                return;
            }

            if (_asset != recipe)
            {
                FinishNoteTyping();
                SaveView();
                DestroyLegacyInspector();
                CharacterBaseEditor.SetRecipeSaveBlocked(_asset, false, this);
                ClearSelection();
                _detachedOverlays.Clear();
                _focusedSharedColorInspectorKey = null;
            }

            _asset = recipe;
            string path = _asset != null ? AssetDatabase.GetAssetPath(_asset) : null;
            _layoutPrefsKey = string.IsNullOrEmpty(path) ? null : "UMA_WardrobeGraphLayout_" + AssetDatabase.AssetPathToGUID(path);
            LoadView();
            ReloadRecipeData();
        }

        private void ReloadRecipeData()
        {
            _serializedRecipe?.Dispose();
            _serializedRecipe = null;
            _recipe = null;
            _dnaEditor = null;
            _slotEditor = null;
            _overlayEditor = null;
            _slotEditorSlot = null;
            _overlayEditorOverlay = null;
            _slotEditorRecipe = null;
            _overlayEditorRecipe = null;
            _slotEditorOverlays = null;
            _slotEditorOverlayOrder.Clear();
            _nodeKeys.Clear();
            _selectedNode = null;
            _pendingObjectPickerKind = PendingObjectPickerKind.None;
            _pendingOverlayTargetSlot = null;
            // The notes were replaced (an Undo, or a change made elsewhere): a later keystroke starts a new Undo step.
            _typingNoteId = null;
            _typingNoteUndoGroup = -1;
            ResetConnectionDrag();
            _loadIncomplete = false;
            _errorMessage = null;
            if (_asset != null)
            {
                _loadedRevision = _asset.recipeString;
                _loadedGraphData = _asset.editorGraphData;
                _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
                try
                {
                    var recipe = new UMAData.UMARecipe();
                    _asset.Load(recipe);
                    EnsureRecipeEditorDefaults(recipe);
                    _recipe = recipe;
                    _errorMessage = CharacterBaseEditor.MissingAssetsMessage(recipe);
                    string graphError = LoadGraphData();
                    if (graphError != null)
                        _errorMessage = string.IsNullOrEmpty(_errorMessage) ? graphError : _errorMessage + "\n" + graphError;
                    _serializedRecipe = new SerializedObject(_asset);
                    _dnaEditor = new DNAMasterEditor(recipe);
                    _sharedColorsEditor = new SharedColorsCollectionEditor();
                    BuildGraph();
                }
                catch (Exception e)
                {
                    _errorMessage = e.Message;
                    Debug.LogException(e, _asset);
                }
                _loadIncomplete = _errorMessage != null;
                CharacterBaseEditor.SetRecipeSaveBlocked(_asset, _loadIncomplete, this);
            }
            Repaint();
        }

        // Import an older, per-user layout on its first explicit save.
        internal void SaveAssetToDisk(bool importLegacyLayout = false)
        {
            if (_asset != null && !_loadIncomplete && EditorUtility.IsPersistent(_asset))
            {
                if (importLegacyLayout && HasLegacyLayout && _recipe != null)
                {
                    Undo.RecordObject(_asset, "Import Wardrobe Graph Layout");
                    _asset.editorGraphData = _loadedGraphData = SerializeGraphData();
                    EditorUtility.SetDirty(_asset);
                }
                AssetDatabase.SaveAssetIfDirty(_asset);
                _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
            }
        }

        /// <summary>Saves the graph's recipe (with the asset's other fields) as a new asset at <paramref name="assetPath"/>.</summary>
        internal UMARecipeBase CreateRecipeCopy(string assetPath)
        {
            // A new asset only. Choosing the recipe itself wrote the graph's copy over it, a partial one too when
            // the load was incomplete; any other existing asset would be replaced with a new one (new GUID).
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(assetPath))
            {
                bool self = assetPath == AssetDatabase.GetAssetPath(_asset);
                EditorUtility.DisplayDialog("UMA Wardrobe Graph",
                    (self ? "Save As makes a new recipe, so it cannot be saved over '" + _asset.name + "' itself" + (_loadIncomplete ? " (it did not load completely, and this copy is missing those assets)." : "; use Save.") :
                    "'" + assetPath + "' already exists. Save As makes a new recipe: choose a new name."), "OK");
                return null;
            }
            Type targetType = _asset.GetType();
            UMARecipeBase newRecipe = ScriptableObject.CreateInstance(targetType) as UMARecipeBase;
            if (newRecipe == null)
            {
                Debug.LogError("Unable to create the selected recipe type for Save As.");
                return null;
            }

            EditorUtility.CopySerialized(_asset, newRecipe);
            newRecipe.name = Path.GetFileNameWithoutExtension(assetPath);

            try
            {
                if (_loadIncomplete)
                {
                    // Save As explicitly makes a copy of the parts that loaded.
                    SaveKeepingOverlayLists(newRecipe, _recipe);
                    BuildGraph();
                    ((UMAWardrobeRecipe)newRecipe).editorGraphData = SerializeGraphData();
                }
                else if (HasLegacyLayout && newRecipe is UMAWardrobeRecipe copy)
                {
                    copy.editorGraphData = SerializeGraphData();
                }
            }
            catch
            {
                DestroyImmediate(newRecipe);
                throw;
            }
            AssetDatabase.CreateAsset(newRecipe, assetPath);
            EditorUtility.SetDirty(newRecipe);
            AssetDatabase.SaveAssetIfDirty(newRecipe);

            if (newRecipe is UMATextRecipe textRecipe)
            {
                UMAUpdateProcessor.UpdateRecipe(textRecipe);
            }
            return newRecipe;
        }

        private void SaveAsRecipe()
        {
            if (_asset == null || _recipe == null)
            {
                return;
            }

            string currentPath = AssetDatabase.GetAssetPath(_asset);
            string directory = string.IsNullOrEmpty(currentPath) ? "Assets" : Path.GetDirectoryName(currentPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                directory = "Assets";
            }

            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Save Wardrobe Recipe As",
                _asset.name,
                "asset",
                "Choose where to save the duplicated wardrobe recipe.",
                directory);

            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            UMARecipeBase newRecipe = CreateRecipeCopy(assetPath);
            if (newRecipe == null)
            {
                return;
            }

            AssetDatabase.Refresh();
            Selection.activeObject = newRecipe;
            EditorGUIUtility.PingObject(newRecipe);

            LoadRecipe(newRecipe as UMAWardrobeRecipe);
        }

        private void CommitRecipeEdit(string undoName)
        {
            FinishNoteTyping();
            try
            {
                // Slot and overlay controls use this same path. It records Undo on the asset before packing.
                if (CharacterBaseEditor.WriteWorkingCopy(_asset, _recipe, undoName))
                {
                    BuildGraph();
                }
            }
            catch (Exception e)
            {
                ReloadRecipeData();
                Debug.LogException(e, _asset);
            }
        }

        // Metadata already lives on the asset. Never repack its slots or DNA for a metadata edit.
        private void FinishAssetEdit()
        {
            FinishNoteTyping();
            EditorUtility.SetDirty(_asset);
            if (_autoSave)
            {
                SaveAssetToDisk();
            }
            _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
            UMAUpdateProcessor.UpdateRecipe(_asset);
            Repaint();
        }

        private void OnUndoRedoGraphEdit()
        {
            if (_asset == null) return;
            bool recipeChanged = _asset.recipeString != _loadedRevision || _asset.editorGraphData != _loadedGraphData;
            if (!recipeChanged && _assetDirtyCount == EditorUtility.GetDirtyCount(_asset)) return;

            if (recipeChanged)
            {
                ReloadRecipeData();
            }
            if (_autoSave)
            {
                SaveAssetToDisk();
            }
            _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
            UMAUpdateProcessor.UpdateRecipe(_asset);
            Repaint();
        }


        private static void EnsureRecipeEditorDefaults(UMAData.UMARecipe recipe)
        {
            if (recipe == null)
            {
                return;
            }

            if (recipe.slotDataList == null)
            {
                recipe.slotDataList = new SlotData[0];
            }

            if (recipe.sharedColors == null)
            {
                recipe.sharedColors = new OverlayColorData[0];
            }

            if (recipe.dnaValues == null)
            {
                recipe.dnaValues = new List<UMADnaBase>();
            }
        }

        private void BuildGraph()
        {
            _nodes.Clear();
            _edges.Clear();
            _slotNodes.Clear();
            _overlayNodes.Clear();
            _sharedColorNodes.Clear();
            _selectedNode = null;

            if (_asset == null || _recipe == null)
            {
                return;
            }

            float sharedY = 24f;
            float overlayY = 24f;
            float slotY = 24f;

            GraphNode outputNode = CreateNode(NodeKind.Output, "Output", "UMAWardrobeRecipe", _asset.name, new Rect(24f + ColumnSpacing * 3f, 24f, NodeWidth, NodeHeight), OutputTint);
            _nodes.Add(outputNode);

            if (_recipe.sharedColors == null)
            {
                _recipe.sharedColors = new OverlayColorData[0];
            }

            for (int i = 0; i < _recipe.sharedColors.Length; i++)
            {
                OverlayColorData colorData = _recipe.sharedColors[i];
                string colorName = colorData != null && !string.IsNullOrEmpty(colorData.name) ? colorData.name : "Shared Color " + (i + 1);
                GraphNode colorNode = CreateNode(NodeKind.SharedColor, GetSharedColorNodeKey(i, colorName), colorName, colorData != null ? colorData.channelCount + " channel(s)" : "Missing color", new Rect(24f, sharedY, NodeWidth, NodeHeight), SharedColorTint, colorData);
                colorNode.SharedColor = colorData;
                colorNode.SharedColorIndex = i;
                if (colorData == null)
                {
                    colorNode.HasWarning = true;
                    colorNode.Warning = "Shared color entry is null.";
                }

                _nodes.Add(colorNode);
                if (colorData != null && !_sharedColorNodes.ContainsKey(colorData))
                {
                    _sharedColorNodes.Add(colorData, colorNode);
                }
                sharedY += NodeHeight + RowSpacing;
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData slot = slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                string slotName = GetSlotDisplayName(slot);
                GraphNode slotNode = CreateNode(NodeKind.Slot, GetSlotNodeKey(slotIndex, slotName), slotName, slot.isPlaceholderSlot ? "Placeholder wildcard" : "SlotDataAsset", new Rect(24f + ColumnSpacing * 2f, slotY, NodeWidth, GetSlotNodeHeight(slot)), SlotTint, slot);
                slotNode.Slot = slot;
                slotNode.SlotIndex = slotIndex;
                if (slot.isPlaceholderSlot && (slot.tags == null || slot.tags.Length == 0))
                {
                    slotNode.HasWarning = true;
                    slotNode.Warning = "Placeholder slot has no matching tags.";
                }
                else if (!slot.isPlaceholderSlot && slot.asset != null && !UMAAssetIndexer.Instance.HasSlot(slot.asset.slotName))
                {
                    slotNode.HasWarning = true;
                    slotNode.Warning = "Slot is not indexed.";
                }

                _nodes.Add(slotNode);
                if (!_slotNodes.ContainsKey(slot))
                {
                    _slotNodes.Add(slot, slotNode);
                }
                _edges.Add(new GraphEdge(slotNode, outputNode, SlotConnectionTint, GraphConnectionKind.SlotToOutput));

                List<OverlayData> overlays = slot.GetOverlayList();
                for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                {
                    OverlayData overlay = overlays[overlayIndex];
                    if (overlay == null)
                    {
                        continue;
                    }

                    string overlayName = overlay.asset != null && !string.IsNullOrEmpty(overlay.asset.overlayName) ? overlay.asset.overlayName : "Missing Overlay";
                    GraphNode overlayNode = GetOrCreateOverlayNode(overlay, overlayName, slotName, slot, slotIndex, overlayIndex, ref overlayY);
                    _edges.Add(new GraphEdge(overlayNode, slotNode, OverlayConnectionTint, GraphConnectionKind.OverlayToSlot, overlayIndex));
                    AddSharedColorEdgeForOverlay(overlayNode);
                    UpdateOverlayUsageSubtitle(overlayNode);
                }

                slotY += slotNode.Rect.height + RowSpacing;
            }

            for (int i = _detachedOverlays.Count - 1; i >= 0; i--)
            {
                OverlayData detachedOverlay = _detachedOverlays[i];
                if (detachedOverlay == null || detachedOverlay.asset == null || CountOverlayUsages(detachedOverlay) > 0)
                {
                    _detachedOverlays.RemoveAt(i);
                    continue;
                }

                string overlayName = detachedOverlay.asset != null && !string.IsNullOrEmpty(detachedOverlay.asset.overlayName) ? detachedOverlay.asset.overlayName : "Missing Overlay";
                GraphNode detachedNode = GetOrCreateOverlayNode(detachedOverlay, overlayName, "Detached overlay", null, -1, i, ref overlayY);
                detachedNode.Subtitle = "Detached overlay";
                AddSharedColorEdgeForOverlay(detachedNode);
            }

            for (int i = 0; i < _notes.Count; i++)
            {
                NoteData note = _notes[i];
                if (note == null)
                {
                    continue;
                }

                EnsureNoteDefaults(note);
                GraphNode noteNode = CreateNode(NodeKind.Note, GetNoteNodeKey(note), "Note", string.Empty, note.rect, NoteTint);
                noteNode.Note = note;
                _nodes.Add(noteNode);
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                GraphNode node = _nodes[i];
                ApplyLayout(node);
                if (node.Key == _selectedKey)
                {
                    _selectedNode = node;
                }
            }

            PruneSelectionToVisibleNodes();
        }

        private GraphNode CreateNode(NodeKind kind, string key, string title, string subtitle, Rect defaultRect, Color tint, object subject = null)
        {
            return new GraphNode
            {
                Kind = kind,
                Key = GetNodeKey(subject, key),
                AssetKey = key,
                Title = title,
                Subtitle = subtitle,
                Rect = defaultRect,
                Tint = tint
            };
        }

        private GraphNode GetOrCreateOverlayNode(OverlayData overlay, string overlayName, string slotName, SlotData slot, int slotIndex, int overlayIndex, ref float overlayY)
        {
            if (_overlayNodes.TryGetValue(overlay, out GraphNode overlayNode))
            {
                return overlayNode;
            }

            string overlayKey = GetOverlayNodeKey(slotIndex, overlayIndex, overlayName);
            overlayNode = CreateNode(NodeKind.Overlay, overlayKey, overlayName, slotName + " [" + overlayIndex + "]", new Rect(24f + ColumnSpacing, overlayY, NodeWidth, NodeHeight), OverlayTint, overlay);
            overlayNode.Slot = slot;
            overlayNode.SlotIndex = slotIndex;
            overlayNode.Overlay = overlay;
            overlayNode.OverlayIndex = overlayIndex;

            if (overlay.asset == null)
            {
                overlayNode.HasWarning = true;
                overlayNode.Warning = "Overlay asset is missing.";
            }
            else if (!UMAAssetIndexer.Instance.HasOverlay(overlay.overlayName))
            {
                overlayNode.HasWarning = true;
                overlayNode.Warning = "Overlay is not indexed.";
            }
            else if (overlay.asset.material == null)
            {
                overlayNode.HasWarning = true;
                overlayNode.Warning = "Overlay has no UMA material.";
            }

            _overlayNodes.Add(overlay, overlayNode);
            _nodes.Add(overlayNode);
            overlayY += NodeHeight + RowSpacing;
            return overlayNode;
        }

        private void UpdateOverlayUsageSubtitle(GraphNode overlayNode)
        {
            if (overlayNode == null || overlayNode.Overlay == null)
            {
                return;
            }

            int useCount = CountOverlayUsages(overlayNode.Overlay);
            if (useCount > 1)
            {
                overlayNode.Subtitle = "Shared by " + useCount + " slots";
            }
        }

        private bool HasSharedColorEdge(GraphNode overlayNode)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                GraphEdge edge = _edges[i];
                if (edge.Kind == GraphConnectionKind.SharedColorToOverlay && edge.To == overlayNode)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddSharedColorEdgeForOverlay(GraphNode overlayNode)
        {
            if (overlayNode == null || overlayNode.Overlay == null)
            {
                return;
            }

            GraphNode sharedNode = FindSharedColorNode(overlayNode.Overlay.colorData);
            if (sharedNode != null && !HasSharedColorEdge(overlayNode))
            {
                _edges.Add(new GraphEdge(sharedNode, overlayNode, ColorConnectionTint, GraphConnectionKind.SharedColorToOverlay));
            }
        }

        private int CountOverlayUsages(OverlayData overlay)
        {
            if (overlay == null || _recipe == null)
            {
                return 0;
            }

            int count = 0;
            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                List<OverlayData> overlays = slot.GetOverlayList();
                for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                {
                    if (ReferenceEquals(overlays[overlayIndex], overlay))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private SlotData FindFirstSlotUsingOverlay(OverlayData overlay)
        {
            if (overlay == null || _recipe == null)
            {
                return null;
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData slot = slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                if (ContainsOverlayReference(slot.GetOverlayList(), overlay))
                {
                    return slot;
                }
            }

            return null;
        }

        private bool IsSharedOverlayStackSecondary(SlotData slot)
        {
            if (slot == null || _recipe == null)
            {
                return false;
            }

            List<OverlayData> overlays = slot.GetOverlayList();
            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData candidate = slots[slotIndex];
                if (candidate == null)
                {
                    continue;
                }

                if (ReferenceEquals(candidate, slot))
                {
                    return false;
                }

                if (ReferenceEquals(candidate.GetOverlayList(), overlays))
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetSlotNodeHeight(SlotData slot)
        {
            int portCount = GetSlotInputPortCount(slot);
            return Mathf.Max(NodeHeight, SlotInputTopPadding + SlotInputBottomPadding + Mathf.Max(0, portCount - 1) * SlotInputSpacing);
        }

        private void ApplyLayout(GraphNode node)
        {
            if (node.Kind == NodeKind.Note)
            {
                return;
            }

            if (_layoutPositions.TryGetValue(node.Key, out Vector2 positionValue))
            {
                node.Rect.position = positionValue;
            }
        }

        private GraphNode FindSharedColorNode(OverlayColorData colorData)
        {
            if (colorData == null || _recipe == null || _recipe.sharedColors == null)
            {
                return null;
            }

            if (_sharedColorNodes.TryGetValue(colorData, out GraphNode exactNode))
            {
                return exactNode;
            }

            for (int i = 0; i < _recipe.sharedColors.Length; i++)
            {
                OverlayColorData sharedColor = _recipe.sharedColors[i];
                if (sharedColor != null && colorData.IsASharedColor && sharedColor.name == colorData.name && sharedColor.Equals(colorData))
                {
                    return _nodes.Find(node => node.Kind == NodeKind.SharedColor && node.SharedColorIndex == i);
                }
            }

            return null;
        }

        private static string GetSharedColorNodeKey(int sharedColorIndex, string colorName)
        {
            return "SharedColor:" + sharedColorIndex + ":" + colorName;
        }

        private static string GetSlotNodeKey(int slotIndex, string slotName)
        {
            return "Slot:" + slotIndex + ":" + slotName;
        }

        private static string GetOverlayNodeKey(int slotIndex, int overlayIndex, string overlayName)
        {
            return "Overlay:" + slotIndex + ":" + overlayIndex + ":" + overlayName;
        }

        // Keep the node's position and selection while its name, slot or layer index changes.
        private string GetNodeKey(object subject, string assetKey)
        {
            if (subject == null) return assetKey;
            if (_nodeKeys.TryGetValue(subject, out string key)) return key;
            key = _nodeKeys.ContainsValue(assetKey) ? assetKey + ":" + Guid.NewGuid().ToString("N") : assetKey;
            _nodeKeys.Add(subject, key);
            return key;
        }

        private static string GetNoteNodeKey(NoteData note)
        {
            return "Note:" + note.id;
        }

        private static void EnsureNoteDefaults(NoteData note)
        {
            if (string.IsNullOrEmpty(note.id))
            {
                note.id = Guid.NewGuid().ToString("N");
            }

            if (note.text == null)
            {
                note.text = string.Empty;
            }

            if (note.rect.width <= 0f || note.rect.height <= 0f)
            {
                note.rect = new Rect(note.rect.x, note.rect.y, NoteDefaultWidth, NoteDefaultHeight);
            }
        }

        private void DrawGraph(Rect graphRect)
        {
            GUI.Box(graphRect, GUIContent.none, EditorStyles.helpBox);
            Rect canvasRect = new Rect(graphRect.x + 8f, graphRect.y + 8f, graphRect.width - 16f, graphRect.height - 16f);
            if (canvasRect.width <= 1f || canvasRect.height <= 1f)
            {
                return;
            }

            HandleGraphEvents(canvasRect);
            HandleGraphDrop(canvasRect);

            GUI.BeginGroup(canvasRect);
            try
            {
                Rect localCanvasRect = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
                DrawGrid(localCanvasRect, 32f, new Color(0f, 0f, 0f, 0.10f));
                DrawGrid(localCanvasRect, 128f, new Color(0f, 0f, 0f, 0.16f));

                Handles.BeginGUI();
                for (int i = 0; i < _edges.Count; i++)
                {
                    DrawEdge(localCanvasRect, _edges[i]);
                }
                DrawConnectionDrag(localCanvasRect);
                Handles.EndGUI();

                for (int i = 0; i < _nodes.Count; i++)
                {
                    DrawNode(localCanvasRect, _nodes[i]);
                }

                DrawSelectionBox(localCanvasRect);

                Rect hintRect = new Rect(12f, localCanvasRect.yMax - 44f, localCanvasRect.width - 24f, 32f);
                GUI.Label(hintRect, "Drop SlotDataAsset, OverlayDataAsset, RaceData, UMATextRecipe, or folders. Middle-drag pans. Mouse wheel zooms.", EditorStyles.centeredGreyMiniLabel);
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        private void DrawGrid(Rect canvasRect, float spacing, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;

            float scaledSpacing = spacing * _zoom;
            if (scaledSpacing < 8f)
            {
                Handles.EndGUI();
                return;
            }

            Vector2 offset = new Vector2((_pan.x * _zoom) % scaledSpacing, (_pan.y * _zoom) % scaledSpacing);
            for (float x = canvasRect.x + offset.x; x < canvasRect.xMax; x += scaledSpacing)
            {
                Handles.DrawLine(new Vector3(x, canvasRect.y), new Vector3(x, canvasRect.yMax));
            }

            for (float y = canvasRect.y + offset.y; y < canvasRect.yMax; y += scaledSpacing)
            {
                Handles.DrawLine(new Vector3(canvasRect.x, y), new Vector3(canvasRect.xMax, y));
            }

            Handles.EndGUI();
        }

        private void DrawEdge(Rect canvasRect, GraphEdge edge)
        {
            if (edge.From == null || edge.To == null)
            {
                return;
            }

            Rect fromRect = GraphToScreenRect(canvasRect, edge.From.Rect);
            Rect toRect = GraphToScreenRect(canvasRect, edge.To.Rect);
            Vector2 start = GetOutputPort(fromRect);
            Vector2 end = GetEdgeInputPort(edge, toRect);
            Vector2 startTangent = start + Vector2.right * 70f * _zoom;
            Vector2 endTangent = end + Vector2.left * 70f * _zoom;
            DrawContrastingBezier(start, end, startTangent, endTangent, edge.Color, EdgeStrokeWidth);
        }

        private void DrawConnectionDrag(Rect canvasRect)
        {
            if (!_draggingConnection || _connectionDragSourceNode == null)
            {
                return;
            }

            Rect fromRect = GraphToScreenRect(canvasRect, _connectionDragSourceNode.Rect);
            Vector2 start = GetOutputPort(fromRect);
            Vector2 end = GraphToScreenPoint(canvasRect, _connectionDragGraphMouse);
            Vector2 startTangent = start + Vector2.right * 70f * _zoom;
            Vector2 endTangent = end + Vector2.left * 70f * _zoom;
            DrawContrastingBezier(start, end, startTangent, endTangent, GetConnectionColor(_draggingConnectionKind), DragEdgeStrokeWidth);
        }

        private void DrawContrastingBezier(Vector2 start, Vector2 end, Vector2 startTangent, Vector2 endTangent, Color color, float baseWidth)
        {
            float width = Mathf.Max(2f, baseWidth * _zoom);
            Handles.DrawBezier(start, end, startTangent, endTangent, EdgeContrastTint, null, width + EdgeContrastStrokePadding);
            Handles.DrawBezier(start, end, startTangent, endTangent, color, null, width);
        }

        private void DrawNode(Rect canvasRect, GraphNode node)
        {
            Rect screenRect = GraphToScreenRect(canvasRect, node.Rect);
            if (screenRect.width <= 1f || screenRect.height <= 1f || !screenRect.Overlaps(canvasRect))
            {
                return;
            }

            bool selected = _selectedKeys.Contains(node.Key);
            Color oldColor = GUI.color;
            GUI.color = selected ? Color.white : new Color(0.94f, 0.94f, 0.94f, 1f);
            EditorGUI.DrawRect(screenRect, node.Tint);
            if (node.Kind != NodeKind.Note)
            {
                GUI.Box(screenRect, GUIContent.none, EditorStyles.helpBox);
            }
            GUI.color = oldColor;

            GUI.BeginGroup(screenRect);
            try
            {
                float headerHeight = Mathf.Min(screenRect.height, NodeHeaderHeight * _zoom);
                EditorGUI.DrawRect(new Rect(0f, 0f, screenRect.width, headerHeight), Color.black);

                Rect headerRect = new Rect(8f * _zoom, 0f, Mathf.Max(0f, screenRect.width - 16f * _zoom), headerHeight);
                GUI.Label(headerRect, GetNodeTypeLabel(node.Kind), CreateScaledNodeStyle(EditorStyles.miniBoldLabel, NodeHeaderFontSize, _zoom, Color.white));

                if (node.Kind == NodeKind.Note)
                {
                    DrawNoteNodeContent(node, screenRect, headerHeight);
                    DrawNoteResizeHandle(screenRect);
                }
                else
                {
                    float horizontalPadding = 12f * _zoom;
                    float contentY = headerHeight + 4f * _zoom;
                    Rect titleRect = new Rect(horizontalPadding, contentY, Mathf.Max(0f, screenRect.width - horizontalPadding * 2f), 20f * _zoom);
                    GUI.Label(titleRect, node.Title, CreateScaledNodeStyle(EditorStyles.boldLabel, NodeTitleFontSize, _zoom, Color.black));

                    float warningTop = node.HasWarning ? screenRect.height - 18f * _zoom : screenRect.height;
                    float subtitleY = titleRect.yMax + 2f * _zoom;
                    float subtitleHeight = Mathf.Max(0f, Mathf.Min(18f * _zoom, warningTop - subtitleY - 2f * _zoom));
                    Rect subtitleRect = new Rect(horizontalPadding, subtitleY, Mathf.Max(0f, screenRect.width - horizontalPadding * 2f), subtitleHeight);
                    GUI.Label(subtitleRect, node.Subtitle, CreateScaledNodeStyle(EditorStyles.miniLabel, NodeSubtitleFontSize, _zoom, Color.black));

                    if (node.HasWarning)
                    {
                        Rect warningRect = new Rect(8f * _zoom, screenRect.height - 18f * _zoom, Mathf.Max(0f, screenRect.width - 16f * _zoom), 14f * _zoom);
                        GUI.Label(warningRect, node.Warning, CreateScaledNodeStyle(EditorStyles.miniBoldLabel, NodeWarningFontSize, _zoom, Color.black));
                    }
                }
            }
            finally
            {
                GUI.EndGroup();
            }

            Handles.BeginGUI();
            if (node.Kind == NodeKind.Slot)
            {
                float portRadius = Mathf.Clamp(5f * _zoom, 2f, 8f);
                int portCount = GetSlotInputPortCount(node.Slot);
                for (int portIndex = 0; portIndex < portCount; portIndex++)
                {
                    Vector2 port = GetSlotInputPort(screenRect, portIndex);
                    bool freePort = portIndex == portCount - 1;
                    Handles.color = freePort ? new Color(OverlayConnectionTint.r, OverlayConnectionTint.g, OverlayConnectionTint.b, 0.75f) : OverlayConnectionTint;
                    if (freePort)
                    {
                        Handles.DrawWireDisc(port, Vector3.forward, portRadius);
                        Handles.DrawLine(port + Vector2.left * portRadius * 0.55f, port + Vector2.right * portRadius * 0.55f);
                        Handles.DrawLine(port + Vector2.down * portRadius * 0.55f, port + Vector2.up * portRadius * 0.55f);
                    }
                    else
                    {
                        Handles.DrawSolidDisc(port, Vector3.forward, portRadius);
                    }
                }
            }
            else if (HasInputPort(node))
            {
                float portRadius = Mathf.Clamp(5f * _zoom, 2f, 8f);
                Handles.color = GetInputPortColor(node);
                Handles.DrawSolidDisc(GetInputPort(screenRect), Vector3.forward, portRadius);
            }

            if (HasOutputPort(node))
            {
                float portRadius = Mathf.Clamp(5f * _zoom, 2f, 8f);
                Handles.color = GetOutputPortColor(node);
                Handles.DrawSolidDisc(GetOutputPort(screenRect), Vector3.forward, portRadius);
            }

            if (selected)
            {
                Handles.color = SelectionOutlineTint;
                Handles.DrawAAPolyLine(Mathf.Max(3f, 4f * _zoom), new Vector3(screenRect.xMin, screenRect.yMin), new Vector3(screenRect.xMax, screenRect.yMin), new Vector3(screenRect.xMax, screenRect.yMax), new Vector3(screenRect.xMin, screenRect.yMax), new Vector3(screenRect.xMin, screenRect.yMin));
            }
            Handles.EndGUI();
        }

        private void DrawNoteNodeContent(GraphNode node, Rect screenRect, float headerHeight)
        {
            if (node.Note == null)
            {
                return;
            }

            float padding = 10f * _zoom;
            Rect textRect = new Rect(padding, headerHeight + padding, Mathf.Max(0f, screenRect.width - padding * 2f), Mathf.Max(0f, screenRect.height - headerHeight - padding * 2f - NoteResizeHandleSize * _zoom));
            GUIStyle style = CreateScaledNodeStyle(GUIStyle.none, NoteTextFontSize, _zoom, Color.black, true);
            style.alignment = TextAnchor.UpperLeft;
            ClearStyleBackgrounds(style);
            style.padding = new RectOffset();
            style.border = new RectOffset();
            style.margin = new RectOffset();

            EditorGUI.BeginChangeCheck();
            // Each note text box has its own name: Unity keeps one control per name, so with the panel's text box
            // also named the same, the canvas note was never seen as focused.
            GUI.SetNextControlName(NoteTextControlName + ".Canvas." + node.Note.id);
            string text = GUI.TextArea(textRect, node.Note.text, style);
            if (EditorGUI.EndChangeCheck())
            {
                SetNoteText(node.Note, text);
                Repaint();
            }
        }

        private static void ClearStyleBackgrounds(GUIStyle style)
        {
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;
        }

        private void DrawNoteResizeHandle(Rect screenRect)
        {
            float handleSize = Mathf.Clamp(NoteResizeHandleSize * _zoom, 8f, 18f);
            Rect handleRect = new Rect(screenRect.width - handleSize, screenRect.height - handleSize, handleSize, handleSize);
            EditorGUI.DrawRect(handleRect, new Color(0f, 0f, 0f, 0.28f));

            Handles.BeginGUI();
            Handles.color = Color.black;
            Handles.DrawLine(new Vector3(screenRect.width - handleSize + 3f, screenRect.height - 3f), new Vector3(screenRect.width - 3f, screenRect.height - handleSize + 3f));
            Handles.DrawLine(new Vector3(screenRect.width - handleSize + 7f, screenRect.height - 3f), new Vector3(screenRect.width - 3f, screenRect.height - handleSize + 7f));
            Handles.EndGUI();
        }

        private void DrawSelectionBox(Rect canvasRect)
        {
            if (!_draggingSelectionBox)
            {
                return;
            }

            Rect graphSelection = CreateRectFromPoints(_selectionStartGraph, _selectionCurrentGraph);
            Rect screenSelection = GraphToScreenRect(canvasRect, graphSelection);
            EditorGUI.DrawRect(screenSelection, SelectionFillTint);

            Handles.BeginGUI();
            Handles.color = SelectionOutlineTint;
            Handles.DrawAAPolyLine(2f, new Vector3(screenSelection.xMin, screenSelection.yMin), new Vector3(screenSelection.xMax, screenSelection.yMin), new Vector3(screenSelection.xMax, screenSelection.yMax), new Vector3(screenSelection.xMin, screenSelection.yMax), new Vector3(screenSelection.xMin, screenSelection.yMin));
            Handles.EndGUI();
        }

        private static string GetNodeTypeLabel(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Output:
                    return "UMAWardrobeRecipe";
                case NodeKind.Slot:
                    return "SlotDataAsset";
                case NodeKind.Overlay:
                    return "OverlayDataAsset";
                case NodeKind.SharedColor:
                    return "OverlayColorData";
                case NodeKind.Note:
                    return "Note";
                default:
                    return kind.ToString();
            }
        }

        private static GUIStyle CreateScaledNodeStyle(GUIStyle source, int baseFontSize, float zoom, Color? textColor = null, bool wordWrap = false)
        {
            GUIStyle style = new GUIStyle(source)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontSize = Mathf.Max(1, Mathf.RoundToInt(baseFontSize * zoom)),
                wordWrap = wordWrap
            };
            if (textColor.HasValue)
            {
                style.normal.textColor = textColor.Value;
                style.hover.textColor = textColor.Value;
                style.active.textColor = textColor.Value;
                style.focused.textColor = textColor.Value;
            }
            return style;
        }

        private static Vector2 GetInputPort(Rect rect)
        {
            return new Vector2(rect.xMin, rect.center.y);
        }

        private Vector2 GetSlotInputPort(Rect rect, int portIndex)
        {
            return new Vector2(rect.xMin, rect.yMin + (SlotInputTopPadding + Mathf.Max(0, portIndex) * SlotInputSpacing) * _zoom);
        }

        private static int GetSlotInputPortCount(SlotData slot)
        {
            return (slot != null ? slot.GetOverlayList().Count : 0) + 1;
        }

        private Vector2 GetEdgeInputPort(GraphEdge edge, Rect toRect)
        {
            if (edge.Kind == GraphConnectionKind.OverlayToSlot && edge.To != null && edge.To.Kind == NodeKind.Slot)
            {
                return GetSlotInputPort(toRect, Mathf.Max(0, edge.ToPortIndex));
            }

            return GetInputPort(toRect);
        }

        private static Vector2 GetOutputPort(Rect rect)
        {
            return new Vector2(rect.xMax, rect.center.y);
        }

        private static bool HasInputPort(GraphNode node)
        {
            return node != null && (node.Kind == NodeKind.Output || node.Kind == NodeKind.Slot || node.Kind == NodeKind.Overlay);
        }

        private static bool HasOutputPort(GraphNode node)
        {
            return node != null && (node.Kind == NodeKind.Slot || node.Kind == NodeKind.Overlay || node.Kind == NodeKind.SharedColor);
        }

        private static Color GetInputPortColor(GraphNode node)
        {
            if (node == null)
            {
                return Color.black;
            }

            switch (node.Kind)
            {
                case NodeKind.Overlay:
                    return ColorConnectionTint;
                case NodeKind.Slot:
                    return OverlayConnectionTint;
                case NodeKind.Output:
                    return SlotConnectionTint;
                default:
                    return Color.black;
            }
        }

        private static Color GetOutputPortColor(GraphNode node)
        {
            if (node == null)
            {
                return Color.black;
            }

            switch (node.Kind)
            {
                case NodeKind.SharedColor:
                    return ColorConnectionTint;
                case NodeKind.Overlay:
                    return OverlayConnectionTint;
                case NodeKind.Slot:
                    return SlotConnectionTint;
                default:
                    return Color.black;
            }
        }

        private static Color GetConnectionColor(GraphConnectionKind kind)
        {
            switch (kind)
            {
                case GraphConnectionKind.SharedColorToOverlay:
                    return ColorConnectionTint;
                case GraphConnectionKind.OverlayToSlot:
                    return OverlayConnectionTint;
                case GraphConnectionKind.SlotToOutput:
                    return SlotConnectionTint;
                default:
                    return Color.black;
            }
        }

        private Rect GraphToScreenRect(Rect canvasRect, Rect graphSpaceRect)
        {
            return new Rect(
                canvasRect.x + (graphSpaceRect.x + _pan.x) * _zoom,
                canvasRect.y + (graphSpaceRect.y + _pan.y) * _zoom,
                graphSpaceRect.width * _zoom,
                graphSpaceRect.height * _zoom);
        }

        private Vector2 GraphToScreenPoint(Rect canvasRect, Vector2 graphSpacePosition)
        {
            return new Vector2(
                canvasRect.x + (graphSpacePosition.x + _pan.x) * _zoom,
                canvasRect.y + (graphSpacePosition.y + _pan.y) * _zoom);
        }

        private Vector2 ScreenToGraph(Rect canvasRect, Vector2 screenPosition)
        {
            return new Vector2(
                (screenPosition.x - canvasRect.x) / _zoom - _pan.x,
                (screenPosition.y - canvasRect.y) / _zoom - _pan.y);
        }

        private void HandleGraphEvents(Rect canvasRect)
        {
            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            bool inCanvas = canvasRect.Contains(current.mousePosition);
            bool hasActiveGesture = _draggingCanvas || _draggingSelectionBox || _draggingConnection || !string.IsNullOrEmpty(_draggingNodeKey) || !string.IsNullOrEmpty(_resizingNoteKey);
            if (!inCanvas && !hasActiveGesture)
            {
                return;
            }

            if (inCanvas && current.type == EventType.ContextClick)
            {
                ShowGraphContextMenu(canvasRect, current.mousePosition);
                current.Use();
                return;
            }

            // The mouse wheel zooms around the pointer, as in other node editors (it needed Ctrl, and the wheel alone
            // did nothing on the canvas). Ctrl+wheel still zooms.
            if (inCanvas && current.type == EventType.ScrollWheel)
            {
                float oldZoom = _zoom;
                Vector2 graphMouseBefore = ScreenToGraph(canvasRect, current.mousePosition);
                _zoom = Mathf.Clamp(_zoom - current.delta.y * 0.03f, 0.35f, 2.2f);
                if (!Mathf.Approximately(oldZoom, _zoom))
                {
                    Vector2 graphMouseAfter = ScreenToGraph(canvasRect, current.mousePosition);
                    _pan += graphMouseAfter - graphMouseBefore;
                    SaveView();
                    Repaint();
                }
                current.Use();
                return;
            }

            if (inCanvas && current.type == EventType.MouseDown && current.button == 2)
            {
                _draggingCanvas = true;
                current.Use();
                return;
            }

            if (_draggingCanvas && current.type == EventType.MouseDrag && current.button == 2)
            {
                _pan += current.delta / _zoom;
                SaveView();
                Repaint();
                current.Use();
                return;
            }

            if (_draggingCanvas && current.type == EventType.MouseUp && current.button == 2)
            {
                _draggingCanvas = false;
                current.Use();
                return;
            }

            if (_draggingConnection && current.type == EventType.MouseDrag && current.button == 0)
            {
                _connectionDragGraphMouse = ScreenToGraph(canvasRect, current.mousePosition);
                Repaint();
                current.Use();
                return;
            }

            if (_draggingConnection && current.type == EventType.MouseUp && current.button == 0)
            {
                FinishConnectionDrag(canvasRect, current.mousePosition);
                current.Use();
                return;
            }

            if (!string.IsNullOrEmpty(_resizingNoteKey) && current.type == EventType.MouseDrag && current.button == 0)
            {
                GraphNode resizeNode = FindNodeByKey(_resizingNoteKey);
                if (resizeNode != null && resizeNode.Note != null)
                {
                    Vector2 graphMouse = ScreenToGraph(canvasRect, current.mousePosition);
                    Vector2 delta = graphMouse - _dragStartGraphMouse;
                    Rect newRect = _resizeStartRect;
                    newRect.width = Mathf.Max(NoteMinWidth, _resizeStartRect.width + delta.x);
                    newRect.height = Mathf.Max(NoteMinHeight, _resizeStartRect.height + delta.y);
                    UpdateNodeRect(resizeNode, newRect);
                    Repaint();
                }
                current.Use();
                return;
            }

            if (!string.IsNullOrEmpty(_resizingNoteKey) && current.type == EventType.MouseUp && current.button == 0)
            {
                if (FindNodeByKey(_resizingNoteKey)?.Note?.rect != _resizeStartRect) SaveLayout("Resize Wardrobe Graph Note");
                _resizingNoteKey = null;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (!inCanvas)
                {
                    return;
                }

                // A click anywhere else on the canvas ends editing a note's text (focus stayed in the text, so Undo
                // still went to it).
                GraphNode clickedNode = HitTestNode(canvasRect, current.mousePosition);
                if (!(clickedNode != null && clickedNode.Kind == NodeKind.Note && IsNoteBodyHit(canvasRect, clickedNode, current.mousePosition)))
                {
                    ReleaseNoteFocus();
                    FinishNoteTyping();
                }

                GraphNode resizeNode = HitTestNoteResizeHandle(canvasRect, current.mousePosition);
                if (resizeNode != null)
                {
                    SelectOnly(resizeNode);
                    _resizingNoteKey = resizeNode.Key;
                    _dragStartGraphMouse = ScreenToGraph(canvasRect, current.mousePosition);
                    _resizeStartRect = resizeNode.Rect;
                    Repaint();
                    current.Use();
                    return;
                }

                GraphNode outputPortNode = HitTestOutputPort(canvasRect, current.mousePosition);
                if (outputPortNode != null && TryGetEditableOutputConnectionKind(outputPortNode, out GraphConnectionKind outputConnectionKind))
                {
                    BeginConnectionDrag(outputPortNode, null, outputConnectionKind, false, canvasRect, current.mousePosition);
                    current.Use();
                    return;
                }

                if (TryHitTestInputPort(canvasRect, current.mousePosition, out GraphPortHit inputPort) && TryGetIncomingEditableEdge(inputPort, out GraphEdge incomingEdge))
                {
                    BeginConnectionDrag(incomingEdge.From, incomingEdge.To, incomingEdge.Kind, true, canvasRect, current.mousePosition);
                    current.Use();
                    return;
                }

                if (TryHitTestEditableEdge(canvasRect, current.mousePosition, out GraphEdge hitEdge))
                {
                    BeginConnectionDrag(hitEdge.From, hitEdge.To, hitEdge.Kind, true, canvasRect, current.mousePosition);
                    current.Use();
                    return;
                }

                GraphNode hitNode = HitTestNode(canvasRect, current.mousePosition);
                if (hitNode != null)
                {
                    if (hitNode.Kind == NodeKind.Note && IsNoteBodyHit(canvasRect, hitNode, current.mousePosition))
                    {
                        if (!_selectedKeys.Contains(hitNode.Key) || _selectedKeys.Count == 0)
                        {
                            SelectOnly(hitNode);
                        }
                        else
                        {
                            SetPrimarySelection(hitNode);
                        }

                        Repaint();
                        return;
                    }

                    bool additive = current.shift || current.control || current.command;
                    if (additive)
                    {
                        ToggleSelection(hitNode);
                    }
                    else if (!_selectedKeys.Contains(hitNode.Key) || _selectedKeys.Count == 0)
                    {
                        SelectOnly(hitNode);
                    }
                    else
                    {
                        SetPrimarySelection(hitNode);
                    }

                    if (_selectedKeys.Contains(hitNode.Key))
                    {
                        BeginSelectedNodeDrag(hitNode, canvasRect, current.mousePosition);
                    }

                    Repaint();
                    current.Use();
                    return;
                }

                _draggingSelectionBox = true;
                _selectionBoxAdditive = current.shift || current.control || current.command;
                _selectionBoxInitialKeys.Clear();
                foreach (string key in _selectedKeys)
                {
                    _selectionBoxInitialKeys.Add(key);
                }
                _selectionStartGraph = ScreenToGraph(canvasRect, current.mousePosition);
                _selectionCurrentGraph = _selectionStartGraph;
                if (!_selectionBoxAdditive)
                {
                    ClearSelection();
                }
                current.Use();
                return;
            }

            if (!string.IsNullOrEmpty(_draggingNodeKey) && current.type == EventType.MouseDrag && current.button == 0)
            {
                Vector2 graphMouse = ScreenToGraph(canvasRect, current.mousePosition);
                Vector2 delta = graphMouse - _dragStartGraphMouse;
                foreach (KeyValuePair<string, Vector2> pair in _dragStartPositions)
                {
                    GraphNode node = FindNodeByKey(pair.Key);
                    if (node != null)
                    {
                        UpdateNodePosition(node, pair.Value + delta);
                    }
                }
                Repaint();
                current.Use();
                return;
            }

            if (!string.IsNullOrEmpty(_draggingNodeKey) && current.type == EventType.MouseUp && current.button == 0)
            {
                foreach (var initial in _dragStartPositions)
                {
                    if (FindNodeByKey(initial.Key)?.Rect.position == initial.Value) continue;
                    SaveLayout("Move Wardrobe Graph Nodes");
                    break;
                }
                _draggingNodeKey = null;
                _dragStartPositions.Clear();
                current.Use();
                return;
            }

            if (_draggingSelectionBox && current.type == EventType.MouseDrag && current.button == 0)
            {
                _selectionCurrentGraph = ScreenToGraph(canvasRect, current.mousePosition);
                ApplyMarqueeSelection();
                Repaint();
                current.Use();
                return;
            }

            if (_draggingSelectionBox && current.type == EventType.MouseUp && current.button == 0)
            {
                _selectionCurrentGraph = ScreenToGraph(canvasRect, current.mousePosition);
                ApplyMarqueeSelection();
                _draggingSelectionBox = false;
                Repaint();
                current.Use();
            }
        }

        private void BeginSelectedNodeDrag(GraphNode hitNode, Rect canvasRect, Vector2 mousePosition)
        {
            _draggingNodeKey = hitNode.Key;
            _dragStartGraphMouse = ScreenToGraph(canvasRect, mousePosition);
            _dragStartPositions.Clear();

            foreach (string key in _selectedKeys)
            {
                GraphNode selectedNode = FindNodeByKey(key);
                if (selectedNode != null)
                {
                    _dragStartPositions[key] = selectedNode.Rect.position;
                }
            }

            if (!_dragStartPositions.ContainsKey(hitNode.Key))
            {
                _dragStartPositions[hitNode.Key] = hitNode.Rect.position;
            }
        }

        private void BeginConnectionDrag(GraphNode sourceNode, GraphNode originalTargetNode, GraphConnectionKind kind, bool disconnectOnCancel, Rect canvasRect, Vector2 mousePosition)
        {
            if (sourceNode == null)
            {
                return;
            }

            _draggingConnection = true;
            _disconnectConnectionOnCancel = disconnectOnCancel;
            _draggingConnectionKind = kind;
            _connectionDragSourceNode = sourceNode;
            _connectionDragOriginalTargetNode = originalTargetNode;
            _connectionDragGraphMouse = ScreenToGraph(canvasRect, mousePosition);
            _draggingNodeKey = null;
            _dragStartPositions.Clear();

            if (originalTargetNode != null)
            {
                SelectOnly(originalTargetNode);
            }
            else
            {
                SelectOnly(sourceNode);
            }

            Repaint();
        }

        private void FinishConnectionDrag(Rect canvasRect, Vector2 mousePosition)
        {
            bool droppedOnInput = TryHitTestInputPort(canvasRect, mousePosition, out GraphPortHit targetPort);

            if (droppedOnInput && CanAcceptConnection(targetPort, _draggingConnectionKind))
            {
                ConnectDraggedConnection(targetPort);
            }
            else if (_disconnectConnectionOnCancel && !droppedOnInput)
            {
                DisconnectOriginalConnection();
            }
            else if (droppedOnInput)
            {
                ShowNotification(new GUIContent("That input port does not accept this connection type."));
            }

            ResetConnectionDrag();
            Repaint();
        }

        private void ResetConnectionDrag()
        {
            _draggingConnection = false;
            _disconnectConnectionOnCancel = false;
            _connectionDragSourceNode = null;
            _connectionDragOriginalTargetNode = null;
        }

        private bool TryHitTestInputPort(Rect canvasRect, Vector2 mousePosition, out GraphPortHit hit)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                GraphNode node = _nodes[i];
                if (!HasInputPort(node))
                {
                    continue;
                }

                Rect screenRect = GraphToScreenRect(canvasRect, node.Rect);
                if (node.Kind == NodeKind.Slot)
                {
                    int portCount = GetSlotInputPortCount(node.Slot);
                    for (int portIndex = 0; portIndex < portCount; portIndex++)
                    {
                        if (Vector2.Distance(mousePosition, GetSlotInputPort(screenRect, portIndex)) <= PortHitRadius)
                        {
                            hit = new GraphPortHit(node, portIndex);
                            return true;
                        }
                    }

                    continue;
                }

                if (Vector2.Distance(mousePosition, GetInputPort(screenRect)) <= PortHitRadius)
                {
                    hit = new GraphPortHit(node, -1);
                    return true;
                }
            }

            hit = default;
            return false;
        }

        private GraphNode HitTestOutputPort(Rect canvasRect, Vector2 mousePosition)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                GraphNode node = _nodes[i];
                if (!HasOutputPort(node))
                {
                    continue;
                }

                Rect screenRect = GraphToScreenRect(canvasRect, node.Rect);
                if (Vector2.Distance(mousePosition, GetOutputPort(screenRect)) <= PortHitRadius)
                {
                    return node;
                }
            }

            return null;
        }

        private bool TryHitTestEditableEdge(Rect canvasRect, Vector2 mousePosition, out GraphEdge hitEdge)
        {
            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                GraphEdge edge = _edges[i];
                if (!IsEditableConnection(edge.Kind) || edge.From == null || edge.To == null)
                {
                    continue;
                }

                if (DistanceToEdge(canvasRect, edge, mousePosition) <= EdgeHitDistance)
                {
                    hitEdge = edge;
                    return true;
                }
            }

            hitEdge = default;
            return false;
        }

        private float DistanceToEdge(Rect canvasRect, GraphEdge edge, Vector2 mousePosition)
        {
            Rect fromRect = GraphToScreenRect(canvasRect, edge.From.Rect);
            Rect toRect = GraphToScreenRect(canvasRect, edge.To.Rect);
            Vector2 start = GetOutputPort(fromRect);
            Vector2 end = GetEdgeInputPort(edge, toRect);
            Vector2 startTangent = start + Vector2.right * 70f * _zoom;
            Vector2 endTangent = end + Vector2.left * 70f * _zoom;
            float closestDistance = float.MaxValue;
            Vector2 previousPoint = start;

            for (int i = 1; i <= 24; i++)
            {
                float t = i / 24f;
                Vector2 point = GetCubicBezierPoint(start, startTangent, endTangent, end, t);
                closestDistance = Mathf.Min(closestDistance, DistancePointToSegment(mousePosition, previousPoint, point));
                previousPoint = point;
            }

            return closestDistance;
        }

        private static Vector2 GetCubicBezierPoint(Vector2 start, Vector2 startTangent, Vector2 endTangent, Vector2 end, float t)
        {
            float inverseT = 1f - t;
            return inverseT * inverseT * inverseT * start + 3f * inverseT * inverseT * t * startTangent + 3f * inverseT * t * t * endTangent + t * t * t * end;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, segmentStart);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 projection = segmentStart + segment * t;
            return Vector2.Distance(point, projection);
        }

        private bool TryGetIncomingEditableEdge(GraphPortHit targetPort, out GraphEdge incomingEdge)
        {
            int matchCount = 0;
            incomingEdge = default;
            for (int i = 0; i < _edges.Count; i++)
            {
                GraphEdge edge = _edges[i];
                if (edge.To != targetPort.Node || !IsEditableConnection(edge.Kind))
                {
                    continue;
                }

                if (edge.Kind == GraphConnectionKind.OverlayToSlot && edge.ToPortIndex != targetPort.PortIndex)
                {
                    continue;
                }

                if (edge.Kind == GraphConnectionKind.SharedColorToOverlay || edge.Kind == GraphConnectionKind.OverlayToSlot)
                {
                    incomingEdge = edge;
                    matchCount++;
                    if (matchCount > 1)
                    {
                        incomingEdge = default;
                        return false;
                    }
                }
            }

            return matchCount == 1;
        }

        private static bool TryGetEditableOutputConnectionKind(GraphNode node, out GraphConnectionKind kind)
        {
            if (node != null && node.Kind == NodeKind.SharedColor)
            {
                kind = GraphConnectionKind.SharedColorToOverlay;
                return true;
            }

            if (node != null && node.Kind == NodeKind.Overlay)
            {
                kind = GraphConnectionKind.OverlayToSlot;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsEditableConnection(GraphConnectionKind kind)
        {
            return kind == GraphConnectionKind.SharedColorToOverlay || kind == GraphConnectionKind.OverlayToSlot;
        }

        private static bool CanAcceptConnection(GraphPortHit targetPort, GraphConnectionKind kind)
        {
            GraphNode node = targetPort.Node;
            if (node == null)
            {
                return false;
            }

            switch (kind)
            {
                case GraphConnectionKind.SharedColorToOverlay:
                    return node.Kind == NodeKind.Overlay && node.Overlay != null;
                case GraphConnectionKind.OverlayToSlot:
                    return node.Kind == NodeKind.Slot && node.Slot != null;
                default:
                    return false;
            }
        }

        private GraphNode HitTestNode(Rect canvasRect, Vector2 mousePosition)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                GraphNode node = _nodes[i];
                if (GraphToScreenRect(canvasRect, node.Rect).Contains(mousePosition))
                {
                    return node;
                }
            }

            return null;
        }

        private GraphNode HitTestNoteResizeHandle(Rect canvasRect, Vector2 mousePosition)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                GraphNode node = _nodes[i];
                if (node.Kind != NodeKind.Note)
                {
                    continue;
                }

                Rect screenRect = GraphToScreenRect(canvasRect, node.Rect);
                float handleSize = Mathf.Clamp(NoteResizeHandleSize * _zoom, 8f, 18f);
                Rect handleRect = new Rect(screenRect.xMax - handleSize, screenRect.yMax - handleSize, handleSize, handleSize);
                if (handleRect.Contains(mousePosition))
                {
                    return node;
                }
            }

            return null;
        }

        private bool IsNoteBodyHit(Rect canvasRect, GraphNode node, Vector2 mousePosition)
        {
            if (node == null || node.Kind != NodeKind.Note)
            {
                return false;
            }

            Rect screenRect = GraphToScreenRect(canvasRect, node.Rect);
            float headerHeight = Mathf.Min(screenRect.height, NodeHeaderHeight * _zoom);
            float handleSize = Mathf.Clamp(NoteResizeHandleSize * _zoom, 8f, 18f);
            Rect handleRect = new Rect(screenRect.xMax - handleSize, screenRect.yMax - handleSize, handleSize, handleSize);
            return screenRect.Contains(mousePosition) && mousePosition.y > screenRect.yMin + headerHeight && !handleRect.Contains(mousePosition);
        }

        private GraphNode FindNodeByKey(string key)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Key == key)
                {
                    return _nodes[i];
                }
            }

            return null;
        }

        private void UpdateNodePosition(GraphNode node, Vector2 position)
        {
            Rect rect = node.Rect;
            rect.position = position;
            UpdateNodeRect(node, rect);
        }

        private void UpdateNodeRect(GraphNode node, Rect rect)
        {
            node.Rect = rect;
            if (node.Kind == NodeKind.Note && node.Note != null)
            {
                node.Note.rect = rect;
                return;
            }

            _layoutPositions[node.Key] = rect.position;
        }

        private void SelectOnly(GraphNode node)
        {
            _selectedKeys.Clear();
            if (node != null)
            {
                _selectedKeys.Add(node.Key);
            }
            SetPrimarySelection(node);
        }

        private void ToggleSelection(GraphNode node)
        {
            if (node == null)
            {
                return;
            }

            if (_selectedKeys.Contains(node.Key))
            {
                _selectedKeys.Remove(node.Key);
                if (_selectedKey == node.Key)
                {
                    SetPrimarySelection(GetFirstSelectedNode());
                }
                return;
            }

            _selectedKeys.Add(node.Key);
            SetPrimarySelection(node);
        }

        private void SetPrimarySelection(GraphNode node)
        {
            _selectedNode = node;
            _selectedKey = node != null ? node.Key : null;
            if (node == null || node.Kind != NodeKind.SharedColor)
            {
                _focusedSharedColorInspectorKey = null;
            }

            if (node == null)
            {
                return;
            }

            _inspectorMode = node.Kind == NodeKind.Output ? InspectorMode.Recipe : InspectorMode.Selection;
        }

        private void ClearSelection()
        {
            _selectedKeys.Clear();
            SetPrimarySelection(null);
        }

        private GraphNode GetFirstSelectedNode()
        {
            foreach (string key in _selectedKeys)
            {
                GraphNode node = FindNodeByKey(key);
                if (node != null)
                {
                    return node;
                }
            }

            return null;
        }

        private void PruneSelectionToVisibleNodes()
        {
            if (_selectedKeys.Count == 0)
            {
                return;
            }

            List<string> missingKeys = new List<string>();
            foreach (string key in _selectedKeys)
            {
                if (FindNodeByKey(key) == null)
                {
                    missingKeys.Add(key);
                }
            }

            for (int i = 0; i < missingKeys.Count; i++)
            {
                _selectedKeys.Remove(missingKeys[i]);
            }

            if (string.IsNullOrEmpty(_selectedKey) || !_selectedKeys.Contains(_selectedKey))
            {
                SetPrimarySelection(GetFirstSelectedNode());
            }
        }

        private void ApplyMarqueeSelection()
        {
            Rect selectionRect = CreateRectFromPoints(_selectionStartGraph, _selectionCurrentGraph);
            _selectedKeys.Clear();
            if (_selectionBoxAdditive)
            {
                foreach (string key in _selectionBoxInitialKeys)
                {
                    _selectedKeys.Add(key);
                }
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                GraphNode node = _nodes[i];
                if (selectionRect.Overlaps(node.Rect, true) || selectionRect.Contains(node.Rect.min) || selectionRect.Contains(node.Rect.max))
                {
                    _selectedKeys.Add(node.Key);
                }
            }

            SetPrimarySelection(GetFirstSelectedNode());
        }

        private static Rect CreateRectFromPoints(Vector2 first, Vector2 second)
        {
            float xMin = Mathf.Min(first.x, second.x);
            float yMin = Mathf.Min(first.y, second.y);
            float xMax = Mathf.Max(first.x, second.x);
            float yMax = Mathf.Max(first.y, second.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void ShowGraphContextMenu(Rect canvasRect, Vector2 mousePosition)
        {
            Vector2 graphPosition = ScreenToGraph(canvasRect, mousePosition);
            GraphNode contextNode = HitTestNode(canvasRect, mousePosition);

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add/SlotDataAsset Node"), false, () => ShowSlotPicker(PendingObjectPickerKind.AddSlot, graphPosition, graphPosition));
            menu.AddItem(new GUIContent("Add/Placeholder Slot"), false, () => AddPlaceholderSlotAt(graphPosition));
            menu.AddItem(new GUIContent("Add/OverlayDataAsset Node"), false, () => StartAddOverlayNode(graphPosition));
            menu.AddItem(new GUIContent("Add/OverlayColorData Node"), false, () => AddSharedColorNodeAt(graphPosition));
            menu.AddItem(new GUIContent("Add/Note Node"), false, () => AddNoteNodeAt(graphPosition));
            menu.AddSeparator(string.Empty);
            menu.AddDisabledItem(new GUIContent("UMAWardrobeRecipe Output Node"));

            if (contextNode != null)
            {
                menu.AddSeparator(string.Empty);
                if (CanDeleteGraphNode(contextNode))
                {
                    menu.AddItem(new GUIContent("Delete current node"), false, () => DeleteCurrentNode(contextNode));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Delete current node"));
                }
            }

            menu.ShowAsContext();
        }

        private bool CanDeleteGraphNode(GraphNode node)
        {
            if (node == null)
            {
                return false;
            }

            switch (node.Kind)
            {
                case NodeKind.Slot:
                    return node.Slot != null;
                case NodeKind.Overlay:
                    return node.Overlay != null;
                case NodeKind.SharedColor:
                    return node.SharedColorIndex >= 0;
                case NodeKind.Note:
                    return node.Note != null;
                default:
                    return false;
            }
        }

        private void DeleteCurrentNode(GraphNode node)
        {
            if (!CanDeleteGraphNode(node))
            {
                return;
            }

            switch (node.Kind)
            {
                case NodeKind.Slot:
                    RemoveSlot(node.Slot);
                    break;
                case NodeKind.Overlay:
                    RemoveOverlayNode(node.Overlay);
                    break;
                case NodeKind.SharedColor:
                    RemoveSharedColorNode(node.SharedColor, node.SharedColorIndex);
                    break;
                case NodeKind.Note:
                    RemoveNote(node.Note);
                    break;
            }
        }

        private void StartAddOverlayNode(Vector2 graphPosition)
        {
            ShowDetachedOverlayPicker(graphPosition);
        }

        private void ShowSlotPicker(PendingObjectPickerKind pickerKind, Vector2 slotPosition, Vector2 addPosition)
        {
            _pendingObjectPickerKind = pickerKind;
            _pendingSlotGraphPosition = slotPosition;
            _pendingAddGraphPosition = addPosition;
            _pendingOverlayTargetSlot = null;
            _objectPickerControlId = CreateObjectPickerControlId(pickerKind);
            EditorGUIUtility.ShowObjectPicker<SlotDataAsset>(null, false, string.Empty, _objectPickerControlId);
        }

        private void ShowOverlayPicker(SlotData targetSlot, Vector2 graphPosition)
        {
            _pendingObjectPickerKind = PendingObjectPickerKind.AddOverlay;
            _pendingOverlayTargetSlot = targetSlot;
            _pendingAddGraphPosition = graphPosition;
            _objectPickerControlId = CreateObjectPickerControlId(PendingObjectPickerKind.AddOverlay);
            EditorGUIUtility.ShowObjectPicker<OverlayDataAsset>(null, false, string.Empty, _objectPickerControlId);
        }

        private void ShowDetachedOverlayPicker(Vector2 graphPosition)
        {
            _pendingObjectPickerKind = PendingObjectPickerKind.AddDetachedOverlay;
            _pendingOverlayTargetSlot = null;
            _pendingAddGraphPosition = graphPosition;
            _objectPickerControlId = CreateObjectPickerControlId(PendingObjectPickerKind.AddDetachedOverlay);
            EditorGUIUtility.ShowObjectPicker<OverlayDataAsset>(null, false, string.Empty, _objectPickerControlId);
        }

        private int CreateObjectPickerControlId(PendingObjectPickerKind pickerKind)
        {
            unchecked
            {
                s_nextObjectPickerControlId++;
                if (s_nextObjectPickerControlId <= 0)
                {
                    s_nextObjectPickerControlId = 46000;
                }

                return s_nextObjectPickerControlId + (int)pickerKind;
            }
        }

        private void HandleObjectPickerEvents()
        {
            Event current = Event.current;
            if (current == null || _pendingObjectPickerKind == PendingObjectPickerKind.None)
            {
                return;
            }

            if (current.commandName != "ObjectSelectorClosed" || EditorGUIUtility.GetObjectPickerControlID() != _objectPickerControlId)
            {
                return;
            }

            Object pickedObject = EditorGUIUtility.GetObjectPickerObject();
            PendingObjectPickerKind pickerKind = _pendingObjectPickerKind;
            Vector2 addPosition = _pendingAddGraphPosition;
            Vector2 slotPosition = _pendingSlotGraphPosition;
            SlotData targetSlot = _pendingOverlayTargetSlot;
            _pendingObjectPickerKind = PendingObjectPickerKind.None;
            _pendingOverlayTargetSlot = null;
            _objectPickerControlId = 0;

            switch (pickerKind)
            {
                case PendingObjectPickerKind.AddSlot:
                {
                    SlotDataAsset slotAsset = pickedObject as SlotDataAsset;
                    if (slotAsset != null)
                    {
                        SlotData slot = AddSlotDataAsset(slotAsset, null, addPosition);
                        SelectNodeForSlot(slot);
                    }
                    break;
                }
                case PendingObjectPickerKind.AddSlotThenOverlay:
                {
                    SlotDataAsset slotAsset = pickedObject as SlotDataAsset;
                    if (slotAsset != null)
                    {
                        SlotData slot = AddSlotDataAsset(slotAsset, null, slotPosition);
                        SelectNodeForSlot(slot);
                        if (slot != null)
                        {
                            EditorApplication.delayCall += () =>
                            {
                                if (this != null && _recipe != null && Array.IndexOf(_recipe.slotDataList, slot) >= 0)
                                {
                                    ShowOverlayPicker(slot, addPosition);
                                }
                            };
                        }
                    }
                    break;
                }
                case PendingObjectPickerKind.AddOverlay:
                {
                    OverlayDataAsset overlayAsset = pickedObject as OverlayDataAsset;
                    SlotData overlayTarget = targetSlot ?? GetPreferredSlotForOverlayDrop();
                    if (overlayAsset != null && overlayTarget != null)
                    {
                        OverlayData overlay = AddOverlayToSlot(overlayTarget, overlayAsset, addPosition);
                        SelectNodeForOverlay(overlayTarget, overlay);
                    }
                    else if (overlayAsset != null)
                    {
                        ShowNotification(new GUIContent("Add or select a SlotDataAsset node first."));
                    }
                    break;
                }
                case PendingObjectPickerKind.AddDetachedOverlay:
                {
                    OverlayDataAsset overlayAsset = pickedObject as OverlayDataAsset;
                    if (overlayAsset != null)
                    {
                        AddDetachedOverlayNode(overlayAsset, addPosition);
                    }
                    break;
                }
            }

            current.Use();
        }

        internal OverlayData AddDetachedOverlayNode(OverlayDataAsset overlayAsset, Vector2 graphPosition)
        {
            if (overlayAsset == null || !AcceptLibraryAsset(overlayAsset))
            {
                return null;
            }

            OverlayData overlay = new OverlayData(overlayAsset);
            _detachedOverlays.Add(overlay);
            SetDetachedOverlayLayoutPosition(overlay, graphPosition);
            BuildGraph();
            SelectNodeForOverlay(null, overlay);
            SaveLayout("Add Detached Wardrobe Overlay");
            Repaint();
            return overlay;
        }

        internal void AddNoteNodeAt(Vector2 graphPosition)
        {
            NoteData note = new NoteData
            {
                id = Guid.NewGuid().ToString("N"),
                text = string.Empty,
                rect = new Rect(graphPosition.x, graphPosition.y, NoteDefaultWidth, NoteDefaultHeight)
            };
            _notes.Add(note);
            SaveLayout("Add Wardrobe Graph Note");
            BuildGraph();
            SelectNodeByKey(GetNoteNodeKey(note));
            Repaint();
        }

        private void RemoveNote(NoteData note)
        {
            if (note == null)
            {
                return;
            }

            string key = GetNoteNodeKey(note);
            _notes.Remove(note);
            _selectedKeys.Remove(key);
            if (_selectedKey == key)
            {
                SetPrimarySelection(GetFirstSelectedNode());
            }
            SaveLayout("Remove Wardrobe Graph Note");
            BuildGraph();
            Repaint();
        }

        private void AddSharedColorNodeAt(Vector2 graphPosition)
        {
            if (_recipe == null)
            {
                return;
            }

            OverlayColorData sharedColor = new OverlayColorData(3)
            {
                name = GetUniqueSharedColorName(),
                displayColor = Color.white
            };

            OverlayColorData[] colors = _recipe.sharedColors ?? new OverlayColorData[0];
            Array.Resize(ref colors, colors.Length + 1);
            colors[colors.Length - 1] = sharedColor;
            _recipe.sharedColors = colors;

            string key = GetNodeKey(sharedColor, GetSharedColorNodeKey(colors.Length - 1, sharedColor.name));
            _layoutPositions[key] = graphPosition;
            CommitRecipeEdit("Add Wardrobe Shared Color Node");
            SelectNodeByKey(key);
        }

        private string GetUniqueSharedColorName()
        {
            int index = 1;
            while (true)
            {
                string name = "Shared Color " + index;
                bool exists = false;
                OverlayColorData[] colors = _recipe != null ? _recipe.sharedColors : null;
                if (colors != null)
                {
                    for (int i = 0; i < colors.Length; i++)
                    {
                        if (colors[i] != null && colors[i].name == name)
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (!exists)
                {
                    return name;
                }

                index++;
            }
        }

        private void SelectNodeByKey(string key)
        {
            GraphNode node = FindNodeByKey(key);
            if (node != null)
            {
                SelectOnly(node);
            }
        }

        private void SelectNodeForSlot(SlotData slot)
        {
            if (slot == null)
            {
                return;
            }

            BuildGraph();
            if (_slotNodes.TryGetValue(slot, out GraphNode node))
            {
                SelectOnly(node);
                Repaint();
            }
        }

        private void SelectNodeForOverlay(SlotData slot, OverlayData overlay)
        {
            if (overlay == null)
            {
                return;
            }

            BuildGraph();
            for (int i = 0; i < _nodes.Count; i++)
            {
                GraphNode node = _nodes[i];
                if (node.Kind == NodeKind.Overlay && ReferenceEquals(node.Overlay, overlay))
                {
                    SelectOnly(node);
                    Repaint();
                    return;
                }
            }
        }

        private void ConnectDraggedConnection(GraphPortHit targetPort)
        {
            switch (_draggingConnectionKind)
            {
                case GraphConnectionKind.SharedColorToOverlay:
                    ConnectSharedColorToOverlay(_connectionDragSourceNode, targetPort.Node);
                    break;
                case GraphConnectionKind.OverlayToSlot:
                    ConnectOverlayToSlot(_connectionDragSourceNode, targetPort.Node, targetPort.PortIndex);
                    break;
            }
        }

        private void ConnectSharedColorToOverlay(GraphNode sourceNode, GraphNode targetNode)
        {
            if (sourceNode == null || sourceNode.SharedColor == null || targetNode == null || targetNode.Overlay == null)
            {
                return;
            }

            if (_disconnectConnectionOnCancel && _connectionDragOriginalTargetNode == targetNode)
            {
                return;
            }

            if (_disconnectConnectionOnCancel && _connectionDragOriginalTargetNode != null && _connectionDragOriginalTargetNode.Overlay != null)
            {
                SetOverlayColorUnshared(_connectionDragOriginalTargetNode.Overlay);
            }

            targetNode.Overlay.colorData = sourceNode.SharedColor;
            CommitRecipeEdit("Reconnect Wardrobe Overlay Color");
            SelectNodeForOverlay(targetNode.Slot, targetNode.Overlay);
        }

        private void ConnectOverlayToSlot(GraphNode sourceNode, GraphNode targetNode, int targetPortIndex)
        {
            if (sourceNode == null || sourceNode.Overlay == null || targetNode == null || targetNode.Slot == null)
            {
                return;
            }

            if (_disconnectConnectionOnCancel)
            {
                SlotData sourceSlot = _connectionDragOriginalTargetNode != null && _connectionDragOriginalTargetNode.Slot != null ? _connectionDragOriginalTargetNode.Slot : sourceNode.Slot;
                if (MoveOverlayConnection(sourceSlot, sourceNode.Overlay, targetNode.Slot, targetPortIndex, sourceNode.Rect.position))
                {
                    SelectNodeForOverlay(targetNode.Slot, sourceNode.Overlay);
                }
                return;
            }

            if (ShareOverlayToSlot(sourceNode.Overlay, targetNode.Slot, targetPortIndex))
            {
                SelectNodeForOverlay(targetNode.Slot, sourceNode.Overlay);
            }
        }

        private void DisconnectOriginalConnection()
        {
            switch (_draggingConnectionKind)
            {
                case GraphConnectionKind.SharedColorToOverlay:
                    DisconnectSharedColorFromOverlay(_connectionDragOriginalTargetNode);
                    break;
                case GraphConnectionKind.OverlayToSlot:
                    DisconnectOverlayFromSlot(_connectionDragOriginalTargetNode, _connectionDragSourceNode != null ? _connectionDragSourceNode.Overlay : null);
                    break;
            }
        }

        private void DisconnectSharedColorFromOverlay(GraphNode overlayNode)
        {
            if (overlayNode == null || overlayNode.Overlay == null)
            {
                return;
            }

            SetOverlayColorUnshared(overlayNode.Overlay);
            CommitRecipeEdit("Disconnect Wardrobe Overlay Color");
            SelectNodeForOverlay(overlayNode.Slot, overlayNode.Overlay);
        }

        private void DisconnectOverlayFromSlot(GraphNode slotNode, OverlayData overlay)
        {
            if (slotNode == null || slotNode.Slot == null || overlay == null)
            {
                return;
            }

            RemoveOverlay(slotNode.Slot, overlay);
        }

        internal bool ShareOverlayToSlot(OverlayData overlay, SlotData targetSlot, int targetIndex)
        {
            if (overlay == null || targetSlot == null)
            {
                return false;
            }

            List<OverlayData> targetOverlays = targetSlot.GetOverlayList();
            SlotData sourceSlot = FindFirstSlotUsingOverlay(overlay);
            if (sourceSlot != null)
            {
                List<OverlayData> sourceOverlays = sourceSlot.GetOverlayList();
                if (ReferenceEquals(targetOverlays, sourceOverlays))
                {
                    ShowNotification(new GUIContent("That slot already uses this shared overlay stack."));
                    return false;
                }

                if (targetOverlays == null || targetOverlays.Count == 0)
                {
                    // A slot with no overlays of its own shares the whole stack.
                    targetSlot.SetOverlayList(sourceOverlays);
                    CommitRecipeEdit("Share Wardrobe Overlay Stack");
                    return true;
                }

                // A slot with its own overlays keeps them: sharing the stack replaced them all. A recipe can
                // only share whole stacks, so the slot gets a copy of this one overlay (linked to the same
                // shared color, if any).
                OverlayData copy = overlay.Duplicate();
                if (overlay.colorData != null && _recipe.sharedColors != null &&
                    Array.Exists(_recipe.sharedColors, color => ReferenceEquals(color, overlay.colorData)))
                {
                    copy.colorData = overlay.colorData;
                }
                targetOverlays = new List<OverlayData>(targetOverlays);
                InsertOverlayAt(targetOverlays, copy, targetIndex);
                targetSlot.SetOverlayList(targetOverlays);
                CommitRecipeEdit("Copy Wardrobe Overlay Node");
                ShowNotification(new GUIContent("Added a copy of the overlay. Connect it to a slot with no overlays to share the whole stack."));
                return true;
            }

            targetOverlays = new List<OverlayData>(targetOverlays);
            if (ContainsOverlayReference(targetOverlays, overlay))
            {
                ShowNotification(new GUIContent("That slot already uses this overlay."));
                return false;
            }

            InsertOverlayAt(targetOverlays, overlay, targetIndex);
            targetSlot.SetOverlayList(targetOverlays);
            _detachedOverlays.RemoveAll(detachedOverlay => ReferenceEquals(detachedOverlay, overlay));
            CommitRecipeEdit("Share Wardrobe Overlay Node");
            return true;
        }

        internal bool MoveOverlayConnection(SlotData sourceSlot, OverlayData overlay, SlotData targetSlot, int targetIndex, Vector2 graphPosition)
        {
            if (sourceSlot == null || overlay == null || targetSlot == null)
            {
                return false;
            }

            List<OverlayData> sourceOverlays = sourceSlot.GetOverlayList();
            int sourceIndex = IndexOfOverlayReference(sourceOverlays, overlay);
            if (sourceIndex < 0)
            {
                return false;
            }

            if (sourceSlot == targetSlot)
            {
                sourceOverlays.RemoveAt(sourceIndex);
                InsertOverlayAt(sourceOverlays, overlay, targetIndex);
                sourceSlot.SetOverlayList(sourceOverlays);
                SetOverlayLayoutPosition(targetSlot, overlay, graphPosition);
                CommitRecipeEdit("Reorder Wardrobe Overlay Node");
                return true;
            }

            List<OverlayData> targetOverlays = targetSlot.GetOverlayList();
            if (ReferenceEquals(sourceOverlays, targetOverlays))
            {
                ShowNotification(new GUIContent("That slot already uses this shared overlay stack."));
                return false;
            }

            // Moves this one overlay. The target slot was given the source's whole list (the moved overlay and
            // the ones left behind) in place of its own overlays.
            List<OverlayData> targetSlotOverlays = targetOverlays != null ? new List<OverlayData>(targetOverlays) : new List<OverlayData>();
            if (ContainsOverlayReference(targetSlotOverlays, overlay))
            {
                ShowNotification(new GUIContent("That slot already uses this overlay."));
                return false;
            }
            List<OverlayData> sourceSlotOverlays = new List<OverlayData>(sourceOverlays);
            sourceSlotOverlays.RemoveAt(sourceIndex);
            sourceSlot.SetOverlayList(sourceSlotOverlays);
            InsertOverlayAt(targetSlotOverlays, overlay, targetIndex);
            targetSlot.SetOverlayList(targetSlotOverlays);
            SetOverlayLayoutPosition(targetSlot, overlay, graphPosition);
            CommitRecipeEdit("Reconnect Wardrobe Overlay Node");
            return true;
        }

        private static void InsertOverlayAt(List<OverlayData> overlays, OverlayData overlay, int index)
        {
            if (overlays == null || overlay == null)
            {
                return;
            }

            overlays.Insert(Mathf.Clamp(index, 0, overlays.Count), overlay);
        }

        private static bool ContainsOverlayReference(List<OverlayData> overlays, OverlayData overlay)
        {
            return IndexOfOverlayReference(overlays, overlay) >= 0;
        }

        private static int IndexOfOverlayReference(List<OverlayData> overlays, OverlayData overlay)
        {
            if (overlays == null || overlay == null)
            {
                return -1;
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                if (ReferenceEquals(overlays[i], overlay))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SetOverlayColorUnshared(OverlayData overlay)
        {
            if (overlay == null)
            {
                return;
            }

            OverlayColorData clone = overlay.colorData != null ? overlay.colorData.Clone() : new OverlayColorData(1);
            clone.name = OverlayColorData.UNSHARED;
            overlay.colorData = clone;
        }

        private void DrawInspector(Rect inspectorRect)
        {
            GUI.Box(inspectorRect, GUIContent.none, EditorStyles.helpBox);
            GUILayout.BeginArea(new Rect(inspectorRect.x + 6f, inspectorRect.y + 6f, inspectorRect.width - 12f, inspectorRect.height - 12f));

            _inspectorMode = (InspectorMode)GUILayout.Toolbar((int)_inspectorMode, new[] { "Selection", "Recipe", "Legacy Inspector" });
            GUILayout.Space(6f);


            switch (_inspectorMode)
            {
                case InspectorMode.Selection:
                    _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
                    DrawSelectionInspector();
                    EditorGUILayout.EndScrollView();
                    break;
                case InspectorMode.Recipe:
                    _recipeScroll = EditorGUILayout.BeginScrollView(_recipeScroll);
                    DrawRecipeInspector();
                    EditorGUILayout.EndScrollView();
                    break;
                case InspectorMode.LegacyInspector:
                    _legacyScroll = EditorGUILayout.BeginScrollView(_legacyScroll);
                    DrawLegacyInspector();
                    EditorGUILayout.EndScrollView();
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawSelectionInspector()
        {
            if (_selectedNode == null)
            {
                EditorGUILayout.HelpBox("Select a node to edit its details. Use the Recipe tab for wardrobe-level settings.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(_selectedNode.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_selectedNode.Kind.ToString(), EditorStyles.miniLabel);

            if (_selectedNode.HasWarning)
            {
                EditorGUILayout.HelpBox(_selectedNode.Warning, MessageType.Warning);
            }

            GUILayout.Space(6f);

            switch (_selectedNode.Kind)
            {
                case NodeKind.Output:
                    DrawRecipeInspector();
                    break;
                case NodeKind.Slot:
                    DrawSlotInspector(_selectedNode);
                    break;
                case NodeKind.Overlay:
                    DrawOverlayInspector(_selectedNode);
                    break;
                case NodeKind.SharedColor:
                    DrawSharedColorInspector(_selectedNode);
                    break;
                case NodeKind.Note:
                    DrawNoteInspector(_selectedNode);
                    break;
            }
        }

        private void DrawNoteInspector(GraphNode node)
        {
            if (node.Note == null)
            {
                EditorGUILayout.HelpBox("This note is missing its editor-only data.", MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName(NoteTextControlName + ".Panel");
            string text = EditorGUILayout.TextArea(node.Note.text, GUILayout.MinHeight(120f));
            Vector2 size = EditorGUILayout.Vector2Field("Size", node.Note.rect.size);
            if (EditorGUI.EndChangeCheck())
            {
                Rect rect = node.Note.rect;
                rect.size = new Vector2(Mathf.Max(NoteMinWidth, size.x), Mathf.Max(NoteMinHeight, size.y));
                UpdateNodeRect(node, rect);
                SetNoteText(node.Note, text);
                Repaint();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Remove Note"))
            {
                RemoveNote(node.Note);
                GUIUtility.ExitGUI();
            }
        }

        private void DrawSlotInspector(GraphNode node)
        {
            if (node.Slot == null)
            {
                EditorGUILayout.HelpBox("Slot data is missing.", MessageType.Warning);
                return;
            }

            bool dnaDirty = false;
            bool textureDirty = false;
            bool meshDirty = false;
            bool sharedOverlayStackSecondary = IsSharedOverlayStackSecondary(node.Slot);
            List<OverlayData> slotOverlays = node.Slot.GetOverlayList();
            if (_slotEditor == null || !ReferenceEquals(_slotEditorSlot, node.Slot) || !ReferenceEquals(_slotEditorRecipe, _recipe) ||
                !SameOverlayOrder(slotOverlays))
            {
                _slotEditor = new SlotEditor(_recipe, node.Slot, node.SlotIndex, _asset);
                _slotEditorSlot = node.Slot;
                _slotEditorRecipe = _recipe;
                _slotEditorOverlays = slotOverlays;
                _slotEditorOverlayOrder.Clear();
                _slotEditorOverlayOrder.AddRange(slotOverlays);
            }
            SlotEditor slotEditor = _slotEditor;
            slotEditor.sharedOverlays = sharedOverlayStackSecondary;
            if (slotEditor.OnGUI(ref dnaDirty, ref textureDirty, ref meshDirty))
            {
                CommitRecipeEdit("Edit Wardrobe Slot Node");
            }

            if (sharedOverlayStackSecondary)
            {
                GUILayout.Space(8f);
                EditorGUILayout.HelpBox("This slot shares another slot's overlays: editing them changes both slots. Slots whose overlays all match are stored as one stack.", MessageType.Info);
                if (GUILayout.Button("Give This Slot Its Own Overlays"))
                {
                    GiveSlotItsOwnOverlays(node.Slot);
                    GUIUtility.ExitGUI();
                }
            }
            if (!sharedOverlayStackSecondary)
            {
                GUILayout.Space(8f);
                OverlayDataAsset addedOverlay = (OverlayDataAsset)EditorGUILayout.ObjectField("Stack Overlay", null, typeof(OverlayDataAsset), false);
                if (addedOverlay != null)
                {
                    AddOverlayToSlot(node.Slot, addedOverlay);
                }
            }

            if (GUILayout.Button("Remove Slot From Recipe"))
            {
                RemoveSlot(node.Slot);
            }
        }

        private void DrawOverlayInspector(GraphNode node)
        {
            if (node.Overlay == null)
            {
                EditorGUILayout.HelpBox("Overlay data is missing.", MessageType.Warning);
                return;
            }

            DrawOverlayColorPopup(node);

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(node.Slot == null || node.OverlayIndex <= 0))
                {
                    if (GUILayout.Button("Move Up"))
                    {
                        MoveOverlay(node.Slot, node.OverlayIndex, -1);
                    }
                }

                using (new EditorGUI.DisabledScope(node.Slot == null || node.OverlayIndex >= node.Slot.GetOverlayList().Count - 1))
                {
                    if (GUILayout.Button("Move Down"))
                    {
                        MoveOverlay(node.Slot, node.OverlayIndex, 1);
                    }
                }
            }

            if (GUILayout.Button("Remove Overlay"))
            {
                if (node.Slot != null)
                {
                    RemoveOverlay(node.Slot, node.Overlay);
                }
                else
                {
                    RemoveDetachedOverlay(node.Overlay);
                }
                return;
            }

            GUILayout.Space(6f);
            if (_overlayEditor == null || !ReferenceEquals(_overlayEditorOverlay, node.Overlay) || !ReferenceEquals(_overlayEditorRecipe, _recipe))
            {
                _overlayEditor = new OverlayEditor(_recipe, node.Slot, node.Overlay, null, _asset);
                _overlayEditorOverlay = node.Overlay;
                _overlayEditorRecipe = _recipe;
            }
            OverlayEditor overlayEditor = _overlayEditor;
            if (overlayEditor.OnGUI())
            {
                CommitRecipeEdit("Edit Wardrobe Overlay Node");
            }
        }

        private void DrawSharedColorInspector(GraphNode node)
        {
            if (node.SharedColor == null)
            {
                EditorGUILayout.HelpBox("Shared color entry is missing.", MessageType.Warning);
                return;
            }

            FocusSharedColorInspector(node);

            if (_sharedColorsEditor.OnGUI(_recipe))
            {
                CommitRecipeEdit("Edit Wardrobe Shared Color Node");
            }
        }

        private void FocusSharedColorInspector(GraphNode node)
        {
            if (node == null || node.Kind != NodeKind.SharedColor || _recipe == null || node.SharedColorIndex < 0)
            {
                return;
            }

            string focusKey = node.Key + ":" + node.SharedColorIndex;
            if (_focusedSharedColorInspectorKey == focusKey)
            {
                return;
            }

            _sharedColorsEditor.OpenSharedColor(_recipe, node.SharedColorIndex);
            _focusedSharedColorInspectorKey = focusKey;
        }

        private void DrawOverlayColorPopup(GraphNode node)
        {
            if (_recipe.sharedColors == null)
            {
                _recipe.sharedColors = new OverlayColorData[0];
            }

            string[] options = BuildSharedColorOptions();
            _selectedSharedColorForOverlay = GetSharedColorIndex(node.Overlay != null ? node.Overlay.colorData : null) + 1;

            EditorGUI.BeginChangeCheck();
            int newSelection = EditorGUILayout.Popup("Shared Color", _selectedSharedColorForOverlay, options);
            if (EditorGUI.EndChangeCheck())
            {
                if (newSelection == 0)
                {
                    SetOverlayColorUnshared(node.Overlay);
                }
                else
                {
                    node.Overlay.colorData = _recipe.sharedColors[newSelection - 1];
                }

                if (node.Slot == null) SaveLayout("Connect Detached Overlay Color");
                else CommitRecipeEdit("Connect Wardrobe Overlay Color");
            }
        }

        private string[] BuildSharedColorOptions()
        {
            List<string> options = new List<string> { "Unshared" };
            if (_recipe.sharedColors != null)
            {
                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    OverlayColorData color = _recipe.sharedColors[i];
                    options.Add(color != null && !string.IsNullOrEmpty(color.name) ? color.name : "Shared Color " + (i + 1));
                }
            }

            return options.ToArray();
        }

        private int GetSharedColorIndex(OverlayColorData colorData)
        {
            if (colorData == null || _recipe.sharedColors == null)
            {
                return -1;
            }

            for (int i = 0; i < _recipe.sharedColors.Length; i++)
            {
                if (ReferenceEquals(_recipe.sharedColors[i], colorData))
                {
                    return i;
                }
            }

            for (int i = 0; i < _recipe.sharedColors.Length; i++)
            {
                OverlayColorData sharedColor = _recipe.sharedColors[i];
                if (sharedColor != null && colorData.IsASharedColor && sharedColor.name == colorData.name && sharedColor.Equals(colorData))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether the slot's overlay list is still the one, in the same order, the cached slot editor was built from.</summary>
        private bool SameOverlayOrder(List<OverlayData> overlays)
        {
            if (!ReferenceEquals(overlays, _slotEditorOverlays) || overlays.Count != _slotEditorOverlayOrder.Count)
            {
                return false;
            }
            for (int i = 0; i < overlays.Count; i++)
            {
                if (!ReferenceEquals(overlays[i], _slotEditorOverlayOrder[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private void DrawRecipeInspector()
        {
            if (_asset == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Wardrobe Recipe", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string displayValue = EditorGUILayout.DelayedTextField("Display Value", _asset.DisplayValue);
            string userField = EditorGUILayout.DelayedTextField("User Field", _asset.UserField);
            bool appended = EditorGUILayout.Toggle("Is Appended", _asset.Appended);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_asset, "Edit Wardrobe Recipe Metadata");
                _asset.DisplayValue = displayValue;
                _asset.UserField = userField;
                _asset.Appended = appended;
                FinishAssetEdit();
            }

            GUILayout.Space(8f);
            DrawCompatibleRacesInspector();
            GUILayout.Space(8f);
            DrawWardrobeSlotInspector();
            GUILayout.Space(8f);
            DrawRecipeListsInspector();
            GUILayout.Space(8f);
            DrawSharedColorsAndDnaInspector();
            GUILayout.Space(8f);
            DrawSerializedWardrobeFields();

            GUILayout.Space(10f);
            if (GUILayout.Button("Open Asset In Inspector"))
            {
                Selection.activeObject = _asset;
                EditorGUIUtility.PingObject(_asset);
            }
        }

        private void DrawCompatibleRacesInspector()
        {
            EditorGUILayout.LabelField("Compatible Races", EditorStyles.boldLabel);

            RaceData raceToAdd = (RaceData)EditorGUILayout.ObjectField("Add Race", null, typeof(RaceData), false);
            if (raceToAdd != null)
            {
                AddRaceDataAsset(raceToAdd);
            }

            for (int i = 0; i < (_asset.compatibleRaces?.Count ?? 0); i++)
            {
                string raceName = _asset.compatibleRaces[i];
                RaceData raceData = UMAAssetIndexer.Instance.GetAsset<RaceData>(raceName);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(raceName);
                    }

                    if (raceData == null)
                    {
                        GUILayout.Label("Missing", EditorStyles.miniBoldLabel, GUILayout.Width(58f));
                    }
                    else if (UMAAssetIndexer.Instance.HasRace(raceData.raceName) == null)
                    {
                        if (GUILayout.Button("Add to Index", GUILayout.Width(92f)))
                        {
                            UMAAssetIndexer.Instance.EvilAddAsset(typeof(RaceData), raceData);
                            UMAAssetIndexer.Instance.ForceSave();
                        }
                    }

                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        RemoveCompatibleRaceAt(i);
                        GUIUtility.ExitGUI();
                    }
                }

                WardrobeRecipeThumb thumb = _asset.wardrobeRecipeThumbs?.Find(entry => entry != null && entry.race == raceName);
                EditorGUI.BeginChangeCheck();
                Sprite newThumb = (Sprite)EditorGUILayout.ObjectField("Thumbnail", thumb?.thumb, typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_asset, "Edit Wardrobe Race Thumbnail");
                    GetOrCreateThumb(raceName).thumb = newThumb;
                    FinishAssetEdit();
                }
            }
        }

        private void DrawWardrobeSlotInspector()
        {
            EditorGUILayout.LabelField("Wardrobe Slots", EditorStyles.boldLabel);
            List<string> wardrobeSlots = GetWardrobeSlotOptions();
            if (!wardrobeSlots.Contains(_asset.wardrobeSlot))
            {
                wardrobeSlots.Add(_asset.wardrobeSlot);
            }

            _selectedWardrobeSlot = Mathf.Max(0, wardrobeSlots.IndexOf(_asset.wardrobeSlot));
            EditorGUI.BeginChangeCheck();
            int newWardrobeIndex = EditorGUILayout.Popup("Wardrobe Region", _selectedWardrobeSlot, wardrobeSlots.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_asset, "Edit Wardrobe Slot");
                _asset.wardrobeSlot = wardrobeSlots[newWardrobeIndex];
                FinishAssetEdit();
            }

            List<string> suppressSlots = _asset.suppressWardrobeSlots;
            EditorGUILayout.LabelField("Suppress Wardrobe Slots", EditorStyles.miniBoldLabel);
            for (int i = 0; i < (suppressSlots?.Count ?? 0); i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(suppressSlots[i]);
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        Undo.RecordObject(_asset, "Remove Suppressed Wardrobe Slot");
                        suppressSlots.RemoveAt(i);
                        FinishAssetEdit();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _selectedSuppressedSlot = EditorGUILayout.Popup(_selectedSuppressedSlot, wardrobeSlots.ToArray());
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && wardrobeSlots.Count > 0)
                {
                    string slot = wardrobeSlots[Mathf.Clamp(_selectedSuppressedSlot, 0, wardrobeSlots.Count - 1)];
                    if (suppressSlots == null || !suppressSlots.Contains(slot))
                    {
                        Undo.RecordObject(_asset, "Add Suppressed Wardrobe Slot");
                        (_asset.suppressWardrobeSlots ??= new List<string>()).Add(slot);
                        FinishAssetEdit();
                    }
                }
            }
        }

        private void DrawRecipeListsInspector()
        {
            List<string> baseSlots = GetBaseSlotOptions();

            EditorGUILayout.LabelField("Hide Base Slots", EditorStyles.boldLabel);
            List<string> hides = _asset.Hides;
            for (int i = 0; i < (hides?.Count ?? 0); i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(hides[i]);
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        Undo.RecordObject(_asset, "Remove Hidden Base Slot");
                        hides.RemoveAt(i);
                        FinishAssetEdit();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _selectedHideSlot = EditorGUILayout.Popup(_selectedHideSlot, baseSlots.ToArray());
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && baseSlots.Count > 0)
                {
                    string slot = baseSlots[Mathf.Clamp(_selectedHideSlot, 0, baseSlots.Count - 1)];
                    if (hides == null || !hides.Contains(slot))
                    {
                        Undo.RecordObject(_asset, "Add Hidden Base Slot");
                        (_asset.Hides ??= new List<string>()).Add(slot);
                        FinishAssetEdit();
                    }
                }
            }

            List<string> replaces = new List<string> { "Nothing" };
            replaces.AddRange(baseSlots);
            _selectedReplaceSlot = Mathf.Max(0, replaces.IndexOf(string.IsNullOrEmpty(_asset.replaces) ? "Nothing" : _asset.replaces));
            EditorGUI.BeginChangeCheck();
            int newReplaceIndex = EditorGUILayout.Popup("Replaces", _selectedReplaceSlot, replaces.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_asset, "Edit Replaced Base Slot");
                _asset.replaces = replaces[newReplaceIndex];
                FinishAssetEdit();
            }

            EditorGUILayout.LabelField("Incompatible Recipes", EditorStyles.boldLabel);
            for (int i = 0; i < (_asset.IncompatibleRecipes?.Count ?? 0); i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    UMAWardrobeRecipe newRecipe = (UMAWardrobeRecipe)EditorGUILayout.ObjectField(_asset.IncompatibleRecipes[i], typeof(UMAWardrobeRecipe), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_asset, "Edit Incompatible Wardrobe Recipe");
                        _asset.IncompatibleRecipes[i] = newRecipe;
                        FinishAssetEdit();
                    }

                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        Undo.RecordObject(_asset, "Remove Incompatible Wardrobe Recipe");
                        _asset.IncompatibleRecipes.RemoveAt(i);
                        FinishAssetEdit();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            UMAWardrobeRecipe incompatibleToAdd = (UMAWardrobeRecipe)EditorGUILayout.ObjectField("Add Incompatible", null, typeof(UMAWardrobeRecipe), false);
            if (incompatibleToAdd != null && (_asset.IncompatibleRecipes == null || !_asset.IncompatibleRecipes.Contains(incompatibleToAdd)))
            {
                Undo.RecordObject(_asset, "Add Incompatible Wardrobe Recipe");
                (_asset.IncompatibleRecipes ??= new List<UMAWardrobeRecipe>()).Add(incompatibleToAdd);
                FinishAssetEdit();
            }
        }

        private void DrawSharedColorsAndDnaInspector()
        {
            EditorGUILayout.LabelField("Shared Colors", EditorStyles.boldLabel);
            if (_sharedColorsEditor.OnGUI(_recipe))
            {
                CommitRecipeEdit("Edit Wardrobe Shared Colors");
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("DNA", EditorStyles.boldLabel);
            if (_dnaEditor == null)
            {
                _dnaEditor = new DNAMasterEditor(_recipe);
            }

            // A wardrobe recipe usually has no DNA of its own. Drawing the DNA editor then threw (it read the first
            // entry of an empty list), which left the GUI layout unbalanced: the Recipe tab was drawn over the
            // toolbar and the node list instead of in its panel.
            if (!_dnaEditor.IsValid)
            {
                EditorGUILayout.HelpBox("This recipe has no DNA of its own.", MessageType.None);
            }
            else
            {
                bool dnaDirty = false;
                bool textureDirty = false;
                bool meshDirty = false;
                int dnaCount = _recipe.GetAllDna().Length;
                if (_dnaEditor.OnGUI(ref dnaDirty, ref textureDirty, ref meshDirty))
                {
                    if (_recipe.GetAllDna().Length != dnaCount)
                    {
                        // A DNA entry was removed: the editor's list of entries is out of date.
                        int view = _dnaEditor.viewDna;
                        _dnaEditor = new DNAMasterEditor(_recipe);
                        _dnaEditor.viewDna = Mathf.Min(view, _recipe.GetAllDna().Length - 1);
                    }
                    CommitRecipeEdit("Edit Wardrobe Recipe DNA");
                }
            }

            DrawOverrideDnaInspector();
        }

        private void DrawOverrideDnaInspector()
        {
            EditorGUILayout.LabelField("Override DNA", EditorStyles.boldLabel);
            List<RaceData> races = GetCompatibleRaceDatas();
            if (races.Count == 0)
            {
                EditorGUILayout.HelpBox("Add compatible races before editing override DNA.", MessageType.Info);
                return;
            }

            string[] raceNames = races.ConvertAll(race => race.raceName).ToArray();
            _selectedRaceForDna = EditorGUILayout.Popup("Race", Mathf.Clamp(_selectedRaceForDna, 0, raceNames.Length - 1), raceNames);
            RaceData selectedRace = races[Mathf.Clamp(_selectedRaceForDna, 0, races.Count - 1)];
            List<string> dnaNames = selectedRace.GetDNANames();
            if (dnaNames.Count > 0)
            {
                string[] dnaLabels = dnaNames.ConvertAll(name => name.MenuCamelCase()).ToArray();
                using (new EditorGUILayout.HorizontalScope())
                {
                    _selectedDna = EditorGUILayout.Popup(_selectedDna, dnaLabels);
                    if (GUILayout.Button("Add DNA", GUILayout.Width(80f)))
                    {
                        string dnaName = dnaNames[Mathf.Clamp(_selectedDna, 0, dnaNames.Count - 1)];
                        if (_asset.OverrideDNA == null || !_asset.OverrideDNA.ContainsName(dnaName))
                        {
                            Undo.RecordObject(_asset, "Add Wardrobe Override DNA");
                            (_asset.OverrideDNA ??= new UMAPredefinedDNA()).AddDNA(dnaName, 0.5f);
                            FinishAssetEdit();
                        }
                    }
                }
            }

            string dnaToRemove = null;
            if (_asset.OverrideDNA == null) return;
            foreach (DnaValue dnaData in _asset.OverrideDNA.PreloadValues)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(ObjectNames.NicifyVariableName(dnaData.Name), GUILayout.Width(120f));
                    EditorGUI.BeginChangeCheck();
                    float value = EditorGUILayout.Slider(dnaData.Value, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_asset, "Edit Wardrobe Override DNA");
                        dnaData.Value = value;
                        FinishAssetEdit();
                    }
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        dnaToRemove = dnaData.Name;
                    }
                }
            }

            if (!string.IsNullOrEmpty(dnaToRemove))
            {
                Undo.RecordObject(_asset, "Remove Wardrobe Override DNA");
                _asset.OverrideDNA.RemoveDNA(dnaToRemove);
                FinishAssetEdit();
            }
        }

        private void DrawSerializedWardrobeFields()
        {
            if (_serializedRecipe == null)
            {
                return;
            }

            _serializedRecipe.Update();
            DrawSerializedProperty("HideTags", "Tags to Hide");
            DrawSerializedProperty("MeshHideAssets", "Mesh Hide Assets");
            DrawSerializedProperty("MeshHideAssetCollections", "Mesh Hide Asset Collections");
            DrawSerializedProperty("MeshModifiers", "Mesh Modifiers");
            if (_serializedRecipe.ApplyModifiedProperties())
            {
                FinishAssetEdit();
            }
        }

        private void DrawSerializedProperty(string propertyName, string label)
        {
            SerializedProperty property = _serializedRecipe.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
            }
        }

        private void DrawLegacyInspector()
        {
            if (_asset == null)
            {
                return;
            }

            if (_legacyInspector == null || _legacyInspector.target != _asset)
            {
                DestroyLegacyInspector();
                _legacyInspector = Editor.CreateEditor(_asset);
                if (_legacyInspector is CharacterBaseEditor recipeInspector)
                    recipeInspector.HostAutoSave = () => _autoSave;
            }

            if (_legacyInspector == null)
            {
                EditorGUILayout.HelpBox("Unable to create the existing UMA wardrobe inspector.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox("This is the existing UMAWardrobeRecipe inspector embedded here for full feature parity. It is also still available as the normal asset inspector.", MessageType.Info);

            // Some Advanced controls change recipe fields directly, including during ExitGUI.
            if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint)
                Undo.RecordObject(_asset, "Edit Wardrobe Recipe");
            _legacyInspector.OnInspectorGUI();
        }

        private void DestroyLegacyInspector()
        {
            if (_legacyInspector != null)
            {
                DestroyImmediate(_legacyInspector);
                _legacyInspector = null;
            }
        }

        /// <summary>
        /// Whether the Global Library finds this same asset under its name, as the recipe inspector checks: the
        /// recipe stores the name, and a reload finds whatever the library has under it. The object pickers and
        /// the Stack Overlay field offered any asset of the type.
        /// </summary>
        private bool AcceptLibraryAsset<T>(T asset) where T : Object
        {
            if (asset == null || IndexedRecipeAssetPicker<T>.IsIndexed(asset))
            {
                return asset != null;
            }
            Debug.LogWarning("UMA Wardrobe Graph: '" + asset.name + "' was not added. The UMA Global Library does not have it under its name '" +
                IndexedRecipeAssetPicker<T>.LookupName(asset) + "' (it has none, or a different asset with that name), and that is what the recipe would load. Add it to the library first.", asset);
            return false;
        }

        private SlotData AddSlotDataAsset(SlotDataAsset slotAsset, OverlayDataAsset initialOverlay = null, Vector2? graphPosition = null)
        {
            if (slotAsset == null || _recipe == null || !AcceptLibraryAsset(slotAsset) ||
                (initialOverlay != null && !AcceptLibraryAsset(initialOverlay)))
            {
                return null;
            }

            SlotData slot = new SlotData(slotAsset);
            OverlayData overlay = null;
            if (initialOverlay != null)
            {
                overlay = new OverlayData(initialOverlay);
                slot.AddOverlay(overlay);
            }
            _recipe.MergeSlot(slot, false);

            SlotData recipeSlot = FindRecipeSlot(slot) ?? slot;
            if (graphPosition.HasValue)
            {
                SetSlotLayoutPosition(recipeSlot, graphPosition.Value);
                if (overlay != null)
                {
                    SetOverlayLayoutPosition(recipeSlot, overlay, graphPosition.Value - new Vector2(ColumnSpacing, 0f));
                }
            }

            CommitRecipeEdit("Add Wardrobe Slot Node");
            return recipeSlot;
        }

        internal SlotData AddPlaceholderSlotAt(Vector2 graphPosition)
        {
            if (_recipe == null)
            {
                return null;
            }

            SlotData slot = SlotData.CreatePlaceholder(GetUniquePlaceholderSlotName(), new string[0]);
            SlotData recipeSlot = _recipe.MergeSlot(slot, false) ?? slot;
            SetSlotLayoutPosition(recipeSlot, graphPosition);
            CommitRecipeEdit("Add Wardrobe Placeholder Slot Node");
            SelectNodeForSlot(recipeSlot);
            return recipeSlot;
        }

        private string GetUniquePlaceholderSlotName()
        {
            const string baseName = "Placeholder Slot";
            if (_recipe == null)
            {
                return baseName;
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            int suffix = 1;
            while (true)
            {
                string candidateName = suffix == 1 ? baseName : baseName + " " + suffix;
                bool exists = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    SlotData slot = slots[i];
                    if (slot != null && slot.isPlaceholderSlot && slot.placeholderSlotName == candidateName)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    return candidateName;
                }

                suffix++;
            }
        }

        private OverlayData AddOverlayToSlot(SlotData slot, OverlayDataAsset overlayAsset, Vector2? graphPosition = null)
        {
            if (slot == null || overlayAsset == null || !AcceptLibraryAsset(overlayAsset))
            {
                return null;
            }

            OverlayData overlay = new OverlayData(overlayAsset);
            slot.AddOverlay(overlay);
            if (graphPosition.HasValue)
            {
                SetOverlayLayoutPosition(slot, overlay, graphPosition.Value);
            }
            CommitRecipeEdit("Stack Wardrobe Overlay Node");
            return overlay;
        }

        private SlotData FindRecipeSlot(SlotData slot)
        {
            if (slot == null || _recipe == null)
            {
                return null;
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int i = 0; i < slots.Length; i++)
            {
                if (ReferenceEquals(slots[i], slot))
                {
                    return slots[i];
                }
            }

            string slotName = GetSlotDisplayName(slot);
            for (int i = slots.Length - 1; i >= 0; i--)
            {
                SlotData candidate = slots[i];
                if (candidate != null && GetSlotDisplayName(candidate) == slotName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void SetSlotLayoutPosition(SlotData slot, Vector2 graphPosition)
        {
            if (slot == null || _recipe == null)
            {
                return;
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int i = 0; i < slots.Length; i++)
            {
                if (ReferenceEquals(slots[i], slot))
                {
                    _layoutPositions[GetNodeKey(slot, GetSlotNodeKey(i, GetSlotDisplayName(slot)))] = graphPosition;
                    return;
                }
            }
        }

        private void SetOverlayLayoutPosition(SlotData slot, OverlayData overlay, Vector2 graphPosition)
        {
            if (slot == null || overlay == null || _recipe == null)
            {
                return;
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                if (!ReferenceEquals(slots[slotIndex], slot))
                {
                    continue;
                }

                List<OverlayData> overlays = slot.GetOverlayList();
                for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                {
                    if (ReferenceEquals(overlays[overlayIndex], overlay))
                    {
                        string overlayName = overlay.asset != null && !string.IsNullOrEmpty(overlay.asset.overlayName) ? overlay.asset.overlayName : "Missing Overlay";
                        _layoutPositions[GetNodeKey(overlay, GetOverlayNodeKey(slotIndex, overlayIndex, overlayName))] = graphPosition;
                        return;
                    }
                }
            }
        }

        private void SetDetachedOverlayLayoutPosition(OverlayData overlay, Vector2 graphPosition)
        {
            if (overlay == null)
            {
                return;
            }

            string overlayName = overlay.asset != null && !string.IsNullOrEmpty(overlay.asset.overlayName) ? overlay.asset.overlayName : "Missing Overlay";
            int detachedIndex = IndexOfOverlayReference(_detachedOverlays, overlay);
            _layoutPositions[GetNodeKey(overlay, GetOverlayNodeKey(-1, detachedIndex, overlayName))] = graphPosition;
        }

        private void AddDetachedOverlay(OverlayData overlay)
        {
            if (overlay == null || ContainsOverlayReference(_detachedOverlays, overlay) || CountOverlayUsages(overlay) > 0)
            {
                return;
            }

            _detachedOverlays.Add(overlay);
        }

        private void RemoveSlot(SlotData slot)
        {
            if (slot == null || _recipe == null)
            {
                return;
            }

            _recipe.RemoveSlot(slot);
            ClearSelection();
            CommitRecipeEdit("Remove Wardrobe Slot Node");
        }

        private void RemoveOverlay(SlotData slot, OverlayData overlay)
        {
            if (slot == null || overlay == null)
            {
                return;
            }

            List<OverlayData> overlays = slot.GetOverlayList();
            int overlayIndex = IndexOfOverlayReference(overlays, overlay);
            if (overlayIndex < 0)
            {
                return;
            }

            overlays = new List<OverlayData>(overlays);
            overlays.RemoveAt(overlayIndex);
            slot.SetOverlayList(overlays);
            if (CountOverlayUsages(overlay) == 0)
            {
                AddDetachedOverlay(overlay);
            }

            CommitRecipeEdit("Remove Wardrobe Overlay Node");
            SelectNodeForOverlay(null, overlay);
        }

        private void RemoveDetachedOverlay(OverlayData overlay)
        {
            if (overlay == null)
            {
                return;
            }

            _detachedOverlays.RemoveAll(detachedOverlay => ReferenceEquals(detachedOverlay, overlay));
            _nodeKeys.Remove(overlay);
            ClearSelection();
            BuildGraph();
            SaveLayout("Remove Detached Wardrobe Overlay");
            Repaint();
        }

        private void RemoveOverlayNode(OverlayData overlay)
        {
            if (overlay == null || _recipe == null)
            {
                return;
            }

            bool removed = false;
            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData slot = slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                List<OverlayData> overlays = slot.GetOverlayList();
                bool slotChanged = false;
                for (int overlayIndex = overlays.Count - 1; overlayIndex >= 0; overlayIndex--)
                {
                    if (ReferenceEquals(overlays[overlayIndex], overlay))
                    {
                        overlays.RemoveAt(overlayIndex);
                        slotChanged = true;
                        removed = true;
                    }
                }

                if (slotChanged)
                {
                    slot.SetOverlayList(overlays);
                }
            }

            if (!removed)
            {
                if (ContainsOverlayReference(_detachedOverlays, overlay))
                {
                    RemoveDetachedOverlay(overlay);
                }
                return;
            }

            _detachedOverlays.RemoveAll(detachedOverlay => ReferenceEquals(detachedOverlay, overlay));
            _nodeKeys.Remove(overlay);
            ClearSelection();
            CommitRecipeEdit("Delete Wardrobe Overlay Node");
        }

        private void RemoveSharedColorNode(OverlayColorData sharedColor, int sharedColorIndex)
        {
            if (_recipe == null || _recipe.sharedColors == null)
            {
                return;
            }

            if (sharedColorIndex < 0 || sharedColorIndex >= _recipe.sharedColors.Length ||
                !ReferenceEquals(_recipe.sharedColors[sharedColorIndex], sharedColor))
            {
                return;
            }

            foreach (OverlayData layer in _overlayNodes.Keys)
            {
                if (GetSharedColorIndex(layer.colorData) == sharedColorIndex)
                    SetOverlayColorUnshared(layer);
            }

            List<OverlayColorData> colors = new List<OverlayColorData>(_recipe.sharedColors);
            colors.RemoveAt(sharedColorIndex);
            _recipe.sharedColors = colors.ToArray();

            _focusedSharedColorInspectorKey = null;
            ClearSelection();
            CommitRecipeEdit("Delete Wardrobe Shared Color Node");
        }


        private void MoveOverlay(SlotData slot, int overlayIndex, int direction)
        {
            if (slot == null)
            {
                return;
            }

            List<OverlayData> overlays = slot.GetOverlayList();
            int newIndex = overlayIndex + direction;
            if (overlayIndex < 0 || overlayIndex >= overlays.Count || newIndex < 0 || newIndex >= overlays.Count)
            {
                return;
            }

            OverlayData overlay = overlays[overlayIndex];
            overlays.RemoveAt(overlayIndex);
            overlays.Insert(newIndex, overlay);
            slot.SetOverlayList(overlays);
            CommitRecipeEdit("Reorder Wardrobe Overlay Nodes");
        }

        private SlotData GetPreferredSlotForOverlayDrop()
        {
            if (_selectedNode != null)
            {
                if (_selectedNode.Kind == NodeKind.Slot && _selectedNode.Slot != null)
                {
                    return _selectedNode.Slot;
                }

                if (_selectedNode.Slot != null)
                {
                    return _selectedNode.Slot;
                }
            }

            SlotData[] slots = _recipe.GetAllSlots() ?? new SlotData[0];
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    return slots[i];
                }
            }

            return null;
        }

        private void HandleRecipeDrop(Rect dropRect)
        {
            Event current = Event.current;
            if (current == null || !dropRect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.DragUpdated || current.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                    {
                        if (DragAndDrop.objectReferences[i] is UMAWardrobeRecipe recipe)
                        {
                            LoadRecipe(recipe);
                            break;
                        }
                    }
                }
                current.Use();
            }
        }

        private void HandleGraphDrop(Rect canvasRect)
        {
            Event current = Event.current;
            if (current == null || !canvasRect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                HandleDroppedObjects(DragAndDrop.objectReferences, ScreenToGraph(canvasRect, current.mousePosition));
            }
            current.Use();
        }

        private void HandleDroppedObjects(Object[] droppedObjects, Vector2 graphPosition)
        {
            List<SlotDataAsset> slots = new List<SlotDataAsset>();
            List<OverlayDataAsset> overlays = new List<OverlayDataAsset>();
            List<RaceData> races = new List<RaceData>();
            List<UMATextRecipe> recipes = new List<UMATextRecipe>();

            for (int i = 0; i < droppedObjects.Length; i++)
            {
                Object droppedObject = droppedObjects[i];
                if (droppedObject == null)
                {
                    continue;
                }

                if (droppedObject is SlotDataAsset slotAsset)
                {
                    slots.Add(slotAsset);
                    continue;
                }

                if (droppedObject is OverlayDataAsset overlayAsset)
                {
                    overlays.Add(overlayAsset);
                    continue;
                }

                if (droppedObject is RaceData raceData)
                {
                    races.Add(raceData);
                    continue;
                }

                if (droppedObject is UMATextRecipe recipe)
                {
                    recipes.Add(recipe);
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(droppedObject);
                if (Directory.Exists(path))
                {
                    RecursiveScanFoldersForAssets(path, slots, overlays, races, recipes);
                }
            }

            for (int i = 0; i < races.Count; i++)
            {
                AddRaceDataAsset(races[i]);
            }

            // The same checks as the recipe inspector's drop area: the recipe stores each slot's and overlay's
            // UMA name, and a reload finds whatever the Global Library has under that name; a recipe that did
            // not load completely would merge only part of itself.
            List<string> refused = new List<string>();
            slots.RemoveAll(slot =>
            {
                if (IndexedRecipeAssetPicker<SlotDataAsset>.IsIndexed(slot)) return false;
                refused.Add(slot.name);
                return true;
            });
            bool slotRefused = refused.Count > 0;
            overlays.RemoveAll(overlay =>
            {
                // Overlays dropped with a refused slot were meant for it, not for another slot.
                if (!slotRefused && IndexedRecipeAssetPicker<OverlayDataAsset>.IsIndexed(overlay)) return false;
                refused.Add(overlay.name);
                return true;
            });
            if (refused.Count > 0)
            {
                Debug.LogWarning("UMA Wardrobe Graph: not added: " + string.Join(", ", refused) +
                    ". The UMA Global Library does not have a slot or overlay under that name (or has a different asset with it), or it was dropped with such a slot. Add them to the library first.", _asset);
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                UMAData.UMARecipe recipeToMerge = recipes[i].GetCachedRecipe();
                if (recipeToMerge != null && recipeToMerge.missingAssets != null && recipeToMerge.missingAssets.Count > 0 &&
                    !EditorUtility.DisplayDialog("Recipe not fully loaded",
                        recipes[i].name + " uses assets that could not be found: " + string.Join(", ", recipeToMerge.missingAssets) +
                        ".\n\nMerging adds only the parts that loaded.", "Merge what loaded", "Cancel"))
                {
                    recipeToMerge = null;
                }
                if (recipeToMerge != null)
                {
                    _recipe.Merge(recipeToMerge, false);
                    CommitRecipeEdit("Merge UMA Recipe Into Wardrobe Graph");
                }
            }

            if (slots.Count >= 1 && overlays.Count == 1)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    AddSlotDataAsset(slots[i], overlays[0]);
                }
                return;
            }

            if (slots.Count == 0 && overlays.Count > 0)
            {
                for (int i = 0; i < overlays.Count; i++)
                {
                    AddDetachedOverlayNode(overlays[i], graphPosition + new Vector2(0f, i * (NodeHeight + RowSpacing)));
                }
                return;
            }

            // The overlays go on the first slot this drop added. They went on the selected (or first) slot, an
            // existing one, whenever the recipe already had a slot; when every dropped slot was already in the
            // recipe, they become detached overlay nodes instead of changing an existing slot.
            var slotsBefore = new HashSet<SlotData>(ReferenceComparer<SlotData>.Instance);
            if (_recipe.slotDataList != null)
            {
                foreach (SlotData existing in _recipe.slotDataList)
                {
                    if (existing != null) slotsBefore.Add(existing);
                }
            }
            SlotData firstAddedSlot = null;
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData added = AddSlotDataAsset(slots[i]);
                if (firstAddedSlot == null && added != null && !slotsBefore.Contains(added))
                {
                    firstAddedSlot = added;
                }
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                if (firstAddedSlot != null)
                {
                    AddOverlayToSlot(firstAddedSlot, overlays[i]);
                }
                else
                {
                    AddDetachedOverlayNode(overlays[i], graphPosition + new Vector2(0f, i * (NodeHeight + RowSpacing)));
                }
            }

        }

        private static void RecursiveScanFoldersForAssets(string path, List<SlotDataAsset> slots, List<OverlayDataAsset> overlays, List<RaceData> races, List<UMATextRecipe> recipes)
        {
            string[] assetFiles = Directory.GetFiles(path, "*.asset");
            for (int i = 0; i < assetFiles.Length; i++)
            {
                string assetFile = assetFiles[i].Replace('\\', '/');
                SlotDataAsset slotAsset = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(assetFile);
                if (slotAsset != null)
                {
                    slots.Add(slotAsset);
                }

                OverlayDataAsset overlayAsset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(assetFile);
                if (overlayAsset != null)
                {
                    overlays.Add(overlayAsset);
                }

                RaceData raceData = AssetDatabase.LoadAssetAtPath<RaceData>(assetFile);
                if (raceData != null)
                {
                    races.Add(raceData);
                }

                UMATextRecipe recipe = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(assetFile);
                if (recipe != null)
                {
                    recipes.Add(recipe);
                }
            }

            string[] directories = Directory.GetDirectories(path);
            for (int i = 0; i < directories.Length; i++)
            {
                RecursiveScanFoldersForAssets(directories[i].Replace('\\', '/'), slots, overlays, races, recipes);
            }
        }

        private void AddRaceDataAsset(RaceData raceData)
        {
            if (raceData == null || _asset == null)
            {
                return;
            }

            if (_asset.compatibleRaces == null || !_asset.compatibleRaces.Contains(raceData.raceName))
            {
                Undo.RecordObject(_asset, "Add Compatible Wardrobe Race");
                (_asset.compatibleRaces ??= new List<string>()).Add(raceData.raceName);
                GetOrCreateThumb(raceData.raceName);
                FinishAssetEdit();
            }
        }

        internal void RemoveCompatibleRaceAt(int index)
        {
            if (_asset == null || _asset.compatibleRaces == null || index < 0 || index >= _asset.compatibleRaces.Count)
            {
                return;
            }

            Undo.RecordObject(_asset, "Remove Compatible Wardrobe Race");
            string raceName = _asset.compatibleRaces[index];
            _asset.compatibleRaces.RemoveAt(index);
            if (_asset.wardrobeRecipeThumbs != null)
            {
                _asset.wardrobeRecipeThumbs.RemoveAll(thumb => thumb != null && thumb.race == raceName);
            }
            FinishAssetEdit();
        }

        private WardrobeRecipeThumb GetOrCreateThumb(string raceName)
        {
            if (_asset.wardrobeRecipeThumbs == null)
            {
                _asset.wardrobeRecipeThumbs = new List<WardrobeRecipeThumb>();
            }

            for (int i = 0; i < _asset.wardrobeRecipeThumbs.Count; i++)
            {
                WardrobeRecipeThumb thumb = _asset.wardrobeRecipeThumbs[i];
                if (thumb != null && thumb.race == raceName)
                {
                    return thumb;
                }
            }

            WardrobeRecipeThumb newThumb = new WardrobeRecipeThumb(raceName);
            _asset.wardrobeRecipeThumbs.Add(newThumb);
            return newThumb;
        }

        private List<string> GetWardrobeSlotOptions()
        {
            List<string> options = new List<string> { "None" };
            List<RaceData> races = GetCompatibleRaceDatas();
            for (int i = 0; i < races.Count; i++)
            {
                RaceData race = races[i];
                if (race.wardrobeSlots == null)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < race.wardrobeSlots.Count; slotIndex++)
                {
                    string slot = race.wardrobeSlots[slotIndex];
                    if (!string.IsNullOrEmpty(slot) && !options.Contains(slot))
                    {
                        options.Add(slot);
                    }
                }
            }

            return options;
        }

        private List<string> GetBaseSlotOptions()
        {
            List<string> options = new List<string>();
            List<RaceData> races = GetCompatibleRaceDatas();
            for (int i = 0; i < races.Count; i++)
            {
                RaceData race = races[i];
                if (race == null || race.baseRaceRecipe == null)
                {
                    continue;
                }

                UMAData.UMARecipe baseRecipe = race.baseRaceRecipe.GetCachedRecipe();
                if (baseRecipe == null)
                {
                    continue;
                }

                SlotData[] slots = baseRecipe.GetAllSlots() ?? new SlotData[0];
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    SlotData slot = slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }

                    string slotName = GetSlotDisplayName(slot);
                    if (!string.IsNullOrEmpty(slotName) && !options.Contains(slotName))
                    {
                        options.Add(slotName);
                    }
                }
            }

            return options;
        }

        private List<RaceData> GetCompatibleRaceDatas()
        {
            List<RaceData> races = new List<RaceData>();
            if (_asset == null || _asset.compatibleRaces == null)
            {
                return races;
            }

            for (int i = 0; i < _asset.compatibleRaces.Count; i++)
            {
                RaceData race = UMAAssetIndexer.Instance.GetAsset<RaceData>(_asset.compatibleRaces[i]);
                if (race != null)
                {
                    races.Add(race);
                }
            }

            return races;
        }

        private static string GetSlotDisplayName(SlotData slot)
        {
            if (slot == null)
            {
                return "Missing Slot";
            }

            if (slot.isPlaceholderSlot)
            {
                return string.IsNullOrEmpty(slot.placeholderSlotName) ? "Placeholder Slot" : slot.placeholderSlotName;
            }

            if (slot.asset != null && !string.IsNullOrEmpty(slot.asset.slotName))
            {
                return slot.asset.slotName;
            }

            return string.IsNullOrEmpty(slot.slotName) ? "Unnamed Slot" : slot.slotName;
        }

        private void FrameGraph()
        {
            _pan = Vector2.zero;
            _zoom = 1f;
            SaveView();
            Repaint();
        }

        private bool HasLegacyLayout => string.IsNullOrEmpty(_asset.editorGraphData) &&
            !string.IsNullOrEmpty(_layoutPrefsKey) && EditorPrefs.HasKey(_layoutPrefsKey);

        // Typing in a note: every keystroke went in as its own Undo step and, with Auto Save, its own disk write.
        // The keystrokes typed into one note are now one Undo step, written to disk when the typing ends (the note
        // loses keyboard focus, another edit, a recipe switch, or the window closing).
        private string _typingNoteId;
        private int _typingNoteUndoGroup = -1;
        private int _typingKeyboardControl;

        internal void SetNoteText(NoteData note, string text)
        {
            if (note == null || _asset == null) return;
            if (_typingNoteId != note.id || _typingNoteUndoGroup < 0)
            {
                FinishNoteTyping();
                Undo.IncrementCurrentGroup();
                _typingNoteUndoGroup = Undo.GetCurrentGroup();
                _typingNoteId = note.id;
            }
            _typingKeyboardControl = GUIUtility.keyboardControl;
            note.text = text;
            SaveLayout("Edit Wardrobe Graph Note", writeToDisk: false);
            Undo.CollapseUndoOperations(_typingNoteUndoGroup);
        }

        private const string NoteTextControlName = "UMAWardrobeGraphNoteText";
        private bool _noteTextFocused;

        private static bool IsNoteTextControl(string controlName) =>
            controlName != null && controlName.StartsWith(NoteTextControlName, StringComparison.Ordinal);

        /// <summary>
        /// Undo and Redo pressed while a note's text has keyboard focus. The text box took the keys and does not undo,
        /// so nothing happened until the note was deselected. Ends the typing, lets go of the text (so it shows the
        /// restored note) and runs Unity's Undo or Redo. Returns true when it handled the event.
        /// </summary>
        internal bool HandleNoteUndoShortcut(Event current, string focusedControl)
        {
            if (current == null || current.type != EventType.KeyDown || !IsNoteTextControl(focusedControl) ||
                !(current.control || current.command) || current.alt)
            {
                return false;
            }
            bool redo;
            if (current.keyCode == KeyCode.Z) redo = current.shift;
            else if (current.keyCode == KeyCode.Y && !current.shift) redo = true;
            else return false;

            current.Use();
            FinishNoteTyping();
            ReleaseNoteFocus();
            if (redo) Undo.PerformRedo();
            else Undo.PerformUndo();
            Repaint();
            return true;
        }

        private void ReleaseNoteFocus()
        {
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        /// <summary>Ends a note typing session: with Auto Save on, writes the asset to disk.</summary>
        internal void FinishNoteTyping()
        {
            if (_typingNoteId == null) return;
            _typingNoteId = null;
            _typingNoteUndoGroup = -1;
            if (_autoSave && _asset != null) SaveAssetToDisk();
        }

        private void SaveLayout(string undoName = "Edit Wardrobe Graph Layout", bool writeToDisk = true)
        {
            if (_asset == null || _recipe == null || _loadIncomplete) return;
            if (writeToDisk)
            {
                // Another layout edit ends a note typing session, so it gets its own Undo step.
                FinishNoteTyping();
            }
            if (_loadedRevision != _asset.recipeString || _loadedGraphData != _asset.editorGraphData)
            {
                ReloadRecipeData();
                return;
            }

            BuildGraph();
            string json = SerializeGraphData();
            if (json == _asset.editorGraphData) return;
            Undo.RecordObject(_asset, undoName);
            _asset.editorGraphData = _loadedGraphData = json;
            EditorUtility.SetDirty(_asset);
            if (_autoSave && writeToDisk) SaveAssetToDisk();
            _assetDirtyCount = EditorUtility.GetDirtyCount(_asset);
            Repaint();
        }

        private string SerializeGraphData()
        {
            var data = new LayoutData { notes = _notes };
            foreach (GraphNode node in _nodes)
            {
                if (node.Kind == NodeKind.Note || !_layoutPositions.TryGetValue(node.Key, out Vector2 position)) continue;
                data.keys.Add(node.AssetKey);
                data.positions.Add(position);
            }

            if (_detachedOverlays.Count > 0)
            {
                var slot = SlotData.CreatePlaceholder("Detached", Array.Empty<string>());
                slot.SetOverlayList(_detachedOverlays);
                var packed = UMAPackedRecipeBase.PackRecipeV3(new UMAData.UMARecipe { slotDataList = new[] { slot } });
                for (int i = 0; i < _detachedOverlays.Count; i++)
                {
                    OverlayData layer = _detachedOverlays[i];
                    var settings = packed.slotsV3[0].overlays[i];
                    var color = packed.fColors[settings.colorIdx];
                    settings.colorIdx = 0;
                    data.detached.Add(new DetachedLayer
                    {
                        assetId = GlobalObjectId.GetGlobalObjectIdSlow(layer.asset).ToString(),
                        overlay = settings,
                        color = color,
                        sharedIndex = Array.FindIndex(_recipe.sharedColors, c => ReferenceEquals(c, layer.colorData))
                    });
                }
            }
            return JsonUtility.ToJson(data);
        }

        private string LoadGraphData()
        {
            _layoutPositions.Clear();
            _notes.Clear();
            _detachedOverlays.Clear();
            string json = HasLegacyLayout ? EditorPrefs.GetString(_layoutPrefsKey) : _asset.editorGraphData;
            if (string.IsNullOrEmpty(json)) return null;

            var data = JsonUtility.FromJson<LayoutData>(json);
            if (data.keys != null && data.positions != null && data.keys.Count == data.positions.Count)
            {
                for (int i = 0; i < data.keys.Count; i++) _layoutPositions[data.keys[i]] = data.positions[i];
            }
            if (data.notes != null)
            {
                foreach (var note in data.notes)
                {
                    if (note == null) continue;
                    EnsureNoteDefaults(note);
                    _notes.Add(note);
                }
            }
            if (data.detached == null) return null;
            var missing = new List<string>();
            foreach (DetachedLayer row in data.detached)
            {
                if (row == null || row.overlay == null || row.color == null)
                {
                    missing.Add("(incomplete layer)");
                    continue;
                }
                var asset = GlobalObjectId.TryParse(row.assetId, out var id)
                    ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as OverlayDataAsset : null;
                if (asset == null)
                {
                    missing.Add(row.overlay.id);
                    continue;
                }

                var overlay = new OverlayData(asset);
                var color = new OverlayColorData();
                row.color.SetOverlayColorData(color);
                var colors = _recipe.sharedColors;
                OverlayColorData shared = null;
                if (row.sharedIndex >= 0)
                {
                    if (row.sharedIndex < colors.Length && colors[row.sharedIndex]?.name == color.name)
                        shared = colors[row.sharedIndex];
                    else
                        shared = Array.Find(colors, candidate => candidate != null && candidate.name == color.name);
                }
                var palette = new[] { shared ?? color };
                UMAPackedRecipeBase.ApplyPackedOverlay(overlay, row.overlay, shared != null ? 1 : 0, palette, palette);
                _detachedOverlays.Add(overlay);
            }
            return missing.Count == 0 ? null : "Could not load detached overlays: " + string.Join(", ", missing);
        }

        private void SaveView()
        {
            if (string.IsNullOrEmpty(_layoutPrefsKey)) return;
            var view = new ViewData { pan = _pan, zoom = _zoom, inspectorWidth = _inspectorWidth };
            EditorPrefs.SetString(_layoutPrefsKey + "_View", JsonUtility.ToJson(view));
        }

        private void LoadView()
        {
            var view = new ViewData();
            if (!string.IsNullOrEmpty(_layoutPrefsKey))
            {
                string json = EditorPrefs.GetString(_layoutPrefsKey + "_View", EditorPrefs.GetString(_layoutPrefsKey, string.Empty));
                if (!string.IsNullOrEmpty(json))
                {
                    try { JsonUtility.FromJsonOverwrite(json, view); }
                    catch (ArgumentException) { view = new ViewData(); }
                }
            }
            _pan = view.pan;
            _zoom = Mathf.Clamp(view.zoom, 0.35f, 2.2f);
            _inspectorWidth = Mathf.Max(0f, view.inspectorWidth);
        }
    }
}
#endif
#endif
