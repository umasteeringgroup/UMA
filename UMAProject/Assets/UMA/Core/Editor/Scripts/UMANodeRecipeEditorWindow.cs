using System;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public class UMANodeRecipeEditorWindow : EditorWindow
    {
        private const float NodeWidth = 220f;
        private const float NodeHeight = 56f;
        private const float ColumnSpacing = 280f;
        private const float RowSpacing = 12f;
        private const float HeaderHeight = 24f;

        // Node and edge colors (edit here)
        private static readonly Color COLOR_NODE_SHARED = new Color(0.75f, 0.875f, 1f);
        private static readonly Color COLOR_NODE_OVERLAY = new Color(0.9f, 0.95f, 0.9f);
        private static readonly Color COLOR_NODE_SLOT = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color COLOR_NODE_RECIPE = new Color(0.96f, 0.96f, 0.96f);

        private static readonly Color COLOR_EDGE_SHARED_TO_OVERLAY = new Color(0.25f, 0.5f, 0.9f);
        private static readonly Color COLOR_EDGE_OVERLAY_TO_SLOT = new Color(0.3f, 0.7f, 0.3f);

        private static void SortNodesByYMin(List<Node> nodes)
        {
            nodes.Sort((left, right) => left.Rect.yMin.CompareTo(right.Rect.yMin));
        }

        private List<Node> GetSortedNodesByType(NodeType nodeType)
        {
            List<Node> nodes = new List<Node>();
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                Node node = _nodes[nodeIndex];
                if (node.Type == nodeType)
                {
                    nodes.Add(node);
                }
            }

            SortNodesByYMin(nodes);
            return nodes;
        }

        private Node GetFirstNodeByType(NodeType nodeType)
        {
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                Node node = _nodes[nodeIndex];
                if (node.Type == nodeType)
                {
                    return node;
                }
            }

            return null;
        }

        private List<SlotData> GetNonNullRecipeSlots()
        {
            List<SlotData> slots = new List<SlotData>();
            SlotData[] recipeSlots = _recipe.GetAllSlots();
            for (int slotIndex = 0; slotIndex < recipeSlots.Length; slotIndex++)
            {
                SlotData slot = recipeSlots[slotIndex];
                if (slot != null)
                {
                    slots.Add(slot);
                }
            }

            return slots;
        }

        private SlotData GetFirstNonNullRecipeSlot()
        {
            SlotData[] recipeSlots = _recipe.GetAllSlots();
            for (int slotIndex = 0; slotIndex < recipeSlots.Length; slotIndex++)
            {
                if (recipeSlots[slotIndex] != null)
                {
                    return recipeSlots[slotIndex];
                }
            }

            return null;
        }

        private static UMATextRecipe GetFirstDroppedRecipe(UnityEngine.Object[] draggedObjects)
        {
            for (int objectIndex = 0; objectIndex < draggedObjects.Length; objectIndex++)
            {
                UMATextRecipe recipe = draggedObjects[objectIndex] as UMATextRecipe;
                if (recipe != null)
                {
                    return recipe;
                }
            }

            return null;
        }

        private static readonly Color COLOR_PORT_FILL = new Color(0.18f, 0.18f, 0.18f);
        private static readonly Color COLOR_PORT_OUTLINE = Color.black;

        private enum NodeType { SharedColor, Overlay, Slot, Recipe, DNA, None }

        private enum PortKind
        {
            None,
            SharedColorOut,     // SharedColor -> Overlay (color)
            OverlayColorIn,     // Overlay color in (from SharedColor)
            OverlayOut,         // Overlay -> Slot (by port index)
            SlotIn              // Slot overlay port (by index)
        }

        private class Node
        {
            public Rect Rect; // graph-space rect
            public NodeType Type;
            public int Index; // index in source arrays
            public OverlayColorData SharedColor;   // SharedColor node
            public OverlayData Overlay;            // Overlay exemplar (for inspector)
            public SlotData Slot;                  // Slot node or first owner for display
            public string Title;
            public Color Tint = Color.gray;
        }

        // An identity for deduplicating overlay nodes across slots
        private readonly struct OverlayKey : IEquatable<OverlayKey>
        {
            public readonly OverlayDataAsset Asset;       // overlay asset identity
            public readonly OverlayColorData Color;       // shared color reference (ref equality)
            public readonly Rect Rect;                    // uv rect/placement
            public readonly int UVSet;                    // UV set/index
            public readonly string Name;                  // overlayName for readability

            public OverlayKey(OverlayData ov)
            {
                Asset = ov?.asset;
                Color = ov?.colorData;
                Rect = ov?.rect ?? default;
                UVSet = ov?.UVSet ?? 0;
                Name = ov?.overlayName ?? "";
            }

            public bool Equals(OverlayKey other)
            {
                // Compare by reference for Asset and Color, value for Rect & UVSet, name to be conservative
                return ReferenceEquals(Asset, other.Asset)
                    && ReferenceEquals(Color, other.Color)
                    && Rect.Equals(other.Rect)
                    && UVSet == other.UVSet
                    && string.Equals(Name, other.Name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is OverlayKey ok && Equals(ok);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = (h * 23) + (Asset ? Asset.GetHashCode() : 0);
                    h = (h * 23) + (Color != null ? Color.GetHashCode() : 0);
                    h = (h * 23) + Rect.GetHashCode();
                    h = (h * 23) + UVSet.GetHashCode();
                    h = (h * 23) + (Name != null ? Name.GetHashCode() : 0);
                    return h;
                }
            }
        }

        private static OverlayKey GetOverlayKey(OverlayData ov) => new OverlayKey(ov);

        // Active asset and working recipe
        private UMATextRecipe _asset;
        private UMAData.UMARecipe _recipe;

        // Editors reused from CharacterBaseEditor
        private DNAMasterEditor _dnaEditor;
        private SharedColorsCollectionEditor _sharedColorsEditor;

        // Selection
        private Node _selectedNode;
        private Vector2 _scrollGraph;
        private Vector2 _scrollInspector;

        // Dirty handling
        private bool _autoSave = true;
        private bool _needsSave;

        // Graph
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<(Node from, Node to, Color color)> _edges = new List<(Node from, Node to, Color color)>();

        // One overlay node per unique overlay identity (not instance)
        private readonly Dictionary<OverlayKey, Node> _overlayToNode = new Dictionary<OverlayKey, Node>();

        // Layout helpers
        private GUIStyle _nodeStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _dropStyle;
        

        // Object picker (Overlay -> Slot)
        private int _overlayPickerControlId = -1;
        private SlotData _overlayPickerTargetSlot;

        // Interactive linking (ports)
        private bool _isLinking;
        private Node _linkStartNode;
        private PortKind _linkStartPortKind = PortKind.None;
        private int _linkStartSlotPortIndex = -1; // only for SlotIn starts
        private Vector2 _linkMousePosGraph; // graph-space

        // Overlay → Slot move (drag overlay node body)
        private bool _isDraggingOverlay;
        private Node _dragOverlayNode;
        private SlotData _dragOverlaySourceSlot;
        private Vector2 _dragOverlayMousePos; // graph-space

        // Slot reorder (drag slot node)
        private bool _isDraggingSlot;
        private Node _dragSlotNode;
        private int _dragSlotOriginalIndex = -1;
        private Vector2 _dragSlotMousePos; // graph-space
        private int _slotInsertPreviewIndex = -1;
        private float _slotInsertPreviewY;

        // Manual layout + pan/zoom
        private bool _manualLayout = true;
        private float _zoom = 1.0f;
        private Vector2 _pan = Vector2.zero;
        [Serializable]
        private class LayoutData
        {
            public float zoom = 1f;
            public Vector2 pan = Vector2.zero;
            public List<string> keys = new List<string>();
            public List<Vector2> values = new List<Vector2>();
        }
        private readonly Dictionary<string, Vector2> _manualPositions = new Dictionary<string, Vector2>();
        private string _layoutPrefsKey => _asset == null ? "" : "UMA_NodeLayout_" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_asset));

        [MenuItem("UMA/Node Recipe Editor")]
        public static void Open()
        {
            var win = GetWindow<UMANodeRecipeEditorWindow>("UMA Node Recipe Editor");
            win.minSize = new Vector2(900, 600);
            win.Show();
        }

        private void OnEnable()
        {
            _nodeStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 8) };
            _titleStyle = new GUIStyle(EditorStyles.boldLabel);
            _dropStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 12
            };

            _sharedColorsEditor = new SharedColorsCollectionEditor();

            if (_asset == null && Selection.activeObject is UMATextRecipe tr)
            {
                LoadRecipe(tr);
            }
        }

        private void OnDisable()
        {
            SaveLayout();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_recipe == null)
            {
                DrawEmptyState();
                HandleGlobalDragAndDrop(null);
                HandleObjectPicker();
                return;
            }

            // Build nodes every GUI to stay in sync
            BuildGraph();

            var rect = GUILayoutUtility.GetRect(0, position.width, 0, position.height - HeaderHeight);
            var left = new Rect(rect.x, rect.y, rect.width * 0.60f, rect.height);
            var right = new Rect(rect.x + left.width + 6, rect.y, rect.width - left.width - 6, rect.height);

            using (new GUILayout.AreaScope(left))
            {
                HandlePanZoom(left);
                _scrollGraph = EditorGUILayout.BeginScrollView(_scrollGraph, GUIStyle.none, GUI.skin.verticalScrollbar);
                DrawGraph(left.width);
                EditorGUILayout.EndScrollView();
                HandleGlobalDragAndDrop(left);
            }

            using (new GUILayout.AreaScope(right))
            {
                _scrollInspector = EditorGUILayout.BeginScrollView(_scrollInspector);
                DrawInspector();
                EditorGUILayout.EndScrollView();
            }

            HandleObjectPicker();

            if (_autoSave && _needsSave)
            {
                SaveRecipe();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var newAsset = (UMATextRecipe)EditorGUILayout.ObjectField(_asset, typeof(UMATextRecipe), false, GUILayout.Width(320));
                if (newAsset != _asset)
                {
                    LoadRecipe(newAsset);
                }

                if (GUILayout.Button("New Recipe", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    CreateNewRecipe();
                }

                GUILayout.Space(6);
                _manualLayout = GUILayout.Toggle(_manualLayout, "Manual Layout", EditorStyles.toolbarButton, GUILayout.Width(100));

                if (GUILayout.Button("Auto Arrange Slots", EditorStyles.toolbarButton, GUILayout.Width(140)))
                {
                    AutoArrangeSlots();
                }

                GUILayout.Space(8);
                GUILayout.Label($"Zoom: {Mathf.RoundToInt(_zoom * 100)}%", EditorStyles.miniLabel, GUILayout.Width(80));
                if (GUILayout.Button("-", EditorStyles.toolbarButton, GUILayout.Width(20))) { _zoom = Mathf.Clamp(_zoom - 0.1f, 0.25f, 3f); SaveLayout(); Repaint(); }
                if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(20))) { _zoom = Mathf.Clamp(_zoom + 0.1f, 0.25f, 3f); SaveLayout(); Repaint(); }
                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    _zoom = 1f; _pan = Vector2.zero; SaveLayout(); Repaint();
                }

                if (GUILayout.Button("Save Layout", EditorStyles.toolbarButton, GUILayout.Width(100))) { SaveLayout(); }
                if (GUILayout.Button("Reset Layout", EditorStyles.toolbarButton, GUILayout.Width(100))) { _manualPositions.Clear(); SaveLayout(); Repaint(); }

                GUILayout.FlexibleSpace();

                _autoSave = GUILayout.Toggle(_autoSave, "Auto Save", EditorStyles.toolbarButton, GUILayout.Width(80));
                EditorGUI.BeginDisabledGroup(_autoSave);
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    SaveRecipe();
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Drop a UMATextRecipe here or pick one in the toolbar to start editing.", MessageType.Info);
            var rect = GUILayoutUtility.GetRect(0, 80, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none, _dropStyle);
            GUI.Label(rect, "Drag & Drop UMATextRecipe / SlotData / OverlayData / Folders", EditorStyles.miniLabel);
        }

        private void LoadRecipe(UMATextRecipe tr)
        {
            _asset = tr;
            _recipe = null;
            _dnaEditor = null;
            _selectedNode = null;

            if (_asset != null)
            {
                try
                {
                    _recipe = _asset.GetCachedRecipe();
                    _dnaEditor = new DNAMasterEditor(_recipe);
                    LoadLayout();
                }
                catch (Exception e)
                {
                    Debug.LogError($"UMA Node Editor: Failed to load recipe. {e.Message}");
                    _recipe = null;
                }
            }
            Repaint();
        }

        private void SaveRecipe()
        {
            if (_asset == null || _recipe == null) return;

            try
            {
                _asset.Save(_recipe);
                EditorUtility.SetDirty(_asset);
                AssetDatabase.SaveAssets();
                _needsSave = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"UMA Node Editor: Failed to save recipe. {e.Message}");
            }
        }

        private void CreateNewRecipe()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create UMA Text Recipe", "NewUMATextRecipe", "asset", "Choose a location for the new recipe.");
            if (string.IsNullOrEmpty(path)) return;

            var newRecipe = ScriptableObject.CreateInstance<UMATextRecipe>();
            AssetDatabase.CreateAsset(newRecipe, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = newRecipe;

            LoadRecipe(newRecipe);
        }

        private void BuildGraph()
        {
            _nodes.Clear();
            _edges.Clear();
            _overlayToNode.Clear();

            if (_recipe == null) return;

            float x0 = 12f;                     // Shared Color
            float x1 = x0 + ColumnSpacing;      // Overlay
            float x2 = x1 + ColumnSpacing;      // Slot

            float y0 = 8f;  // shared colors
            float y1 = 8f;  // overlays
            float y2 = 8f;  // slots

            // Shared Colors
            if (_recipe.sharedColors == null)
            {
                _recipe.sharedColors = new OverlayColorData[0];
            }

            for (int i = 0; i < _recipe.sharedColors.Length; i++)
            {
                var sc = _recipe.sharedColors[i];
                var n = new Node
                {
                    Type = NodeType.SharedColor,
                    SharedColor = sc,
                    Index = i,
                    Title = $"{i}: {(string.IsNullOrEmpty(sc.name) ? "Shared Color" : sc.name)}",
                    Rect = new Rect(x0, y0, NodeWidth, NodeHeight),
                    Tint = COLOR_NODE_SHARED
                };
                ApplyManualPosition(n);
                _nodes.Add(n);
                y0 += NodeHeight + RowSpacing;
            }

            // Gather slots and overlay identities in first-seen order
            var slots = _recipe.GetAllSlots();
            var overlayOrder = new List<OverlayKey>();
            var overlayFirstOwner = new Dictionary<OverlayKey, SlotData>();
            var overlayExemplar = new Dictionary<OverlayKey, OverlayData>();

            // Create Slot nodes, collect overlay identities
            for (int si = 0; si < slots.Length; si++)
            {
                var s = slots[si];
                if (s == null) continue;

                var slotNode = new Node
                {
                    Type = NodeType.Slot,
                    Slot = s,
                    Index = si,
                    Title = $"Slot: {s.slotName}",
                    Rect = new Rect(x2, y2, NodeWidth, NodeHeight),
                    Tint = COLOR_NODE_SLOT
                };
                ApplyManualPosition(slotNode);
                _nodes.Add(slotNode);
                y2 += NodeHeight + RowSpacing;

                var overlays = s.GetOverlayList() ?? new List<OverlayData>();
                foreach (var ov in overlays)
                {
                    if (ov == null) continue;
                    var key = GetOverlayKey(ov);
                    if (!overlayExemplar.ContainsKey(key))
                    {
                        overlayOrder.Add(key);
                        overlayFirstOwner[key] = s;
                        overlayExemplar[key] = ov; // keep an exemplar for inspector editing
                    }
                }
            }

            // Create a single Overlay node per unique overlay identity
            foreach (var key in overlayOrder)
            {
                overlayFirstOwner.TryGetValue(key, out var firstOwner);
                overlayExemplar.TryGetValue(key, out var exemplar);
                var overlayNode = new Node
                {
                    Type = NodeType.Overlay,
                    Overlay = exemplar,
                    Slot = firstOwner,
                    Index = 0,
                    Title = $"Overlay: {exemplar?.overlayName ?? key.Name}",
                    Rect = new Rect(x1, y1, NodeWidth, NodeHeight),
                    Tint = COLOR_NODE_OVERLAY
                };
                ApplyManualPosition(overlayNode);
                _nodes.Add(overlayNode);
                _overlayToNode[key] = overlayNode;

                y1 += NodeHeight + RowSpacing;
            }
        }

        private void DrawGraph(float visibleWidth)
        {
            var dropRect = GUILayoutUtility.GetRect(visibleWidth, position.height - HeaderHeight - 10);
            GUI.Box(dropRect, GUIContent.none, _dropStyle);
            GUI.Label(dropRect,
                "Drag & Drop Slots / Overlays / Recipes / Folders here\n" +
                "Click nodes to edit. Right-click for context.\n" +
                "Ports:\n" +
                "- SharedColor (right) ⇄ Overlay Color (left)\n" +
                "- Overlay (right) ⇄ Slot Port (left, one per overlay, ordered)\n" +
                "Drag Overlay out-port to Slot body to append (creates a new port). Alt = copy.",
                _dropStyle);

            // Draw edges
            Handles.BeginGUI();

            // SharedColor -> Overlay (port to port)
            foreach (var on in _nodes)
            {
                if (on.Type != NodeType.Overlay || on.Overlay == null) continue;
                var col = on.Overlay.colorData;
                if (col != null && col.IsASharedColor && _recipe.HasSharedColor(col))
                {
                    var scNode = FindSharedColorNode(col);
                    if (scNode != null)
                    {
                        var p0 = GetPortCenterScreen(scNode, PortKind.SharedColorOut, -1);
                        var p3 = GetPortCenterScreen(on, PortKind.OverlayColorIn, -1);
                        var p1 = p0 + Vector2.right * 40f;
                        var p2 = p3 + Vector2.left * 40f;
                        Handles.DrawBezier(p0, p3, p1, p2, COLOR_EDGE_SHARED_TO_OVERLAY, null, 2f);
                    }
                }
            }

            // Overlay -> Slot (single overlay node fans out to each slot that references an equivalent overlay)
            foreach (var on in _nodes)
            {
                if (on.Type != NodeType.Overlay || on.Overlay == null) continue;

                foreach (var sn in _nodes)
                {
                    if (sn.Type != NodeType.Slot || sn.Slot == null) continue;

                    var list = sn.Slot.GetOverlayList();
                    if (list == null) continue;

                    // Find any overlay in this slot that matches this overlay identity
                    int idx = -1;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (GetOverlayKey(list[i]).Equals(GetOverlayKey(on.Overlay)))
                        {
                            idx = i; break;
                        }
                    }
                    if (idx < 0) continue;

                    var p0 = GetPortCenterScreen(on, PortKind.OverlayOut, -1);
                    var p3 = GetPortCenterScreen(sn, PortKind.SlotIn, idx);
                    var p1 = p0 + Vector2.right * 40f;
                    var p2 = p3 + Vector2.left * 40f;
                    Handles.DrawBezier(p0, p3, p1, p2, COLOR_EDGE_OVERLAY_TO_SLOT, null, 2f);
                }
            }

            // Pending link (draw with color by intent)
            if (_isLinking && _linkStartNode != null)
            {
                var startPos = GetPortCenterScreen(_linkStartNode, _linkStartPortKind, _linkStartSlotPortIndex);
                var endPos = GraphToScreen(_linkMousePosGraph);
                var color = (_linkStartPortKind == PortKind.SharedColorOut || _linkStartPortKind == PortKind.OverlayColorIn)
                    ? COLOR_EDGE_SHARED_TO_OVERLAY
                    : COLOR_EDGE_OVERLAY_TO_SLOT;
                Handles.DrawBezier(startPos, endPos, startPos + Vector2.right * 40f, endPos + Vector2.left * 40f, color, null, 2f);
            }

            Handles.EndGUI();

            // Draw nodes and ports and handle events
            foreach (var node in _nodes)
            {
                var screenRect = GraphToScreenRect(node.Rect);

                // Draw node body
                // var c = GUI.color;
                // GUI.color = node.Tint;
                // GUI.Box(screenRect, GUIContent.none, _nodeStyle);
                // GUI.color = c;

                EditorGUI.DrawRect(screenRect, node.Tint);              // opaque fill using Tint (ignores helpBox texture alpha)
                DrawRectOutline(screenRect, Color.black * 0.25f, 1f);   // 

                var titleRect = new Rect(screenRect.x + 6, screenRect.y + 4, screenRect.width - 12, 18);
                GUI.Label(titleRect, node.Title, _titleStyle);

                // Draw ports and handle port clicks
                if (node.Type == NodeType.SharedColor)
                {
                    var pr = GraphToScreenRect(GetSharedColorOutPortRectGraph(node));
                    DrawPort(pr);
                    HandlePortClick(node, pr, PortKind.SharedColorOut, -1);
                }
                else if (node.Type == NodeType.Overlay)
                {
                    var inCol = GraphToScreenRect(GetOverlayColorInPortRectGraph(node));
                    DrawPort(inCol);
                    HandlePortClick(node, inCol, PortKind.OverlayColorIn, -1);

                    var outOv = GraphToScreenRect(GetOverlayOutPortRectGraph(node));
                    DrawPort(outOv);
                    HandlePortClick(node, outOv, PortKind.OverlayOut, -1);
                }
                else if (node.Type == NodeType.Slot)
                {
                    // One port per overlay index, distributed inside the slot (keeps edges readable while dragging)
                    var list = node.Slot.GetOverlayList() ?? new List<OverlayData>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var prGraph = GetSlotInPortRectGraph(node, i);
                        var pr = GraphToScreenRect(prGraph);
                        DrawPort(pr);
                        HandlePortClick(node, pr, PortKind.SlotIn, i);
                    }
                }

                // General selection and node body interactions
                var e = Event.current;
                var gm = ScreenToGraph(e.mousePosition);

                // Manual node repositioning
                if (_manualLayout && e.type == EventType.MouseDrag && e.button == 0 && screenRect.Contains(e.mousePosition))
                {
                    var deltaGraph = (e.delta) / _zoom;
                    node.Rect.position += deltaGraph;
                    _manualPositions[NodeKey(node)] = node.Rect.position;
                    e.Use();
                    Repaint();
                }

                // Start overlay drag by body (not ports)
                if (node.Type == NodeType.Overlay && e.type == EventType.MouseDown && e.button == 0 && node.Rect.Contains(gm))
                {
                    var colIn = GetOverlayColorInPortRectGraph(node);
                    var outPort = GetOverlayOutPortRectGraph(node);
                    if (!colIn.Contains(gm) && !outPort.Contains(gm))
                    {
                        _isDraggingOverlay = true;
                        _dragOverlayNode = node;
                        _dragOverlaySourceSlot = FindSlotForOverlay(node.Overlay);
                        _dragOverlayMousePos = gm;
                        _selectedNode = node;
                        e.Use();
                    }
                }

                // Start slot reorder by body (not ports)
                if (node.Type == NodeType.Slot && e.type == EventType.MouseDown && e.button == 0 && node.Rect.Contains(gm))
                {
                    _isDraggingSlot = true;
                    _dragSlotNode = node;
                    _dragSlotOriginalIndex = GetSlotIndex(node.Slot);
                    _dragSlotMousePos = gm;
                    _selectedNode = node;
                    ComputeSlotInsertPreview(_dragSlotMousePos.y, _dragSlotNode);
                    e.Use();
                }

                // Select/context on body
                if (e.type == EventType.MouseDown && node.Rect.Contains(gm))
                {
                    if (e.button == 0)
                    {
                        _selectedNode = node;
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        ShowNodeContext(node);
                        e.Use();
                    }
                }
            }

            HandleLinkingCanvasEvents(dropRect);
            HandleOverlayDragging(dropRect);
            HandleSlotDragging(dropRect);
        }

        private void HandleLinkingCanvasEvents(Rect canvasRect)
        {
            var e = Event.current;
            if (e == null) return;

            if (_isLinking && (e.type == EventType.MouseDrag || e.type == EventType.MouseMove))
            {
                _linkMousePosGraph = ScreenToGraph(e.mousePosition);
                Repaint();
            }

            if (_isLinking && e.type == EventType.MouseUp)
            {
                bool linked = TryCompleteLink(ScreenToGraph(e.mousePosition), e.alt);
                _isLinking = false;
                _linkStartNode = null;
                _linkStartPortKind = PortKind.None;
                _linkStartSlotPortIndex = -1;
                e.Use();

                if (linked)
                {
                    _needsSave = true;
                    Repaint();
                }
            }

            if (_isLinking && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _isLinking = false;
                _linkStartNode = null;
                _linkStartPortKind = PortKind.None;
                _linkStartSlotPortIndex = -1;
                e.Use();
                Repaint();
            }
        }

        private Node HitTestSlotNodeGraph(Vector2 mouseGraph)
        {
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.Slot) continue;
                if (n.Rect.Contains(mouseGraph)) return n;
            }
            return null;
        }

        private void HandleOverlayDragging(Rect canvasRect)
        {
            if (!_isDraggingOverlay || _dragOverlayNode == null) return;
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                _dragOverlayMousePos = ScreenToGraph(e.mousePosition);
                Repaint();
                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                var graphPos = ScreenToGraph(e.mousePosition);
                var targetSlotNode = HitTestSlotNodeGraph(graphPos);
                bool altCopy = e.alt;

                if (targetSlotNode != null && targetSlotNode.Slot != null && _dragOverlaySourceSlot != null)
                {
                    if (ReferenceEquals(targetSlotNode.Slot, _dragOverlaySourceSlot))
                    {
                        // Same slot: reorder (or duplicate-insert if Alt)
                        ComputeOverlayInsertPreview(_dragOverlaySourceSlot, graphPos.y, out var insertIdx, out _);
                        if (insertIdx >= 0)
                        {
                            var list = _dragOverlaySourceSlot.GetOverlayList() ?? new List<OverlayData>();
                            var currentIdx = list.IndexOf(_dragOverlayNode.Overlay);
                            if (altCopy)
                            {
                                var dup = CloneOverlay(_dragOverlayNode.Overlay);
                                insertIdx = Mathf.Clamp(insertIdx, 0, list.Count);
                                list.Insert(insertIdx, dup);
                                _dragOverlaySourceSlot.SetOverlayList(list);
                                _needsSave = true;
                            }
                            else if (currentIdx >= 0 && insertIdx != currentIdx && insertIdx != currentIdx + 1)
                            {
                                list.RemoveAt(currentIdx);
                                if (insertIdx > currentIdx) insertIdx--;
                                insertIdx = Mathf.Clamp(insertIdx, 0, list.Count);
                                list.Insert(insertIdx, _dragOverlayNode.Overlay);
                                _dragOverlaySourceSlot.SetOverlayList(list);
                                _needsSave = true;
                            }
                        }
                    }
                    else
                    {
                        // Different slot: move or copy (append if dropped on body)
                        if (altCopy)
                        {
                            var dup = CloneOverlay(_dragOverlayNode.Overlay);
                            var dst = targetSlotNode.Slot.GetOverlayList() ?? new List<OverlayData>();
                            dst.Add(dup);
                            targetSlotNode.Slot.SetOverlayList(dst);
                            _needsSave = true;
                        }
                        else
                        {
                            MoveOverlayToSlot(_dragOverlayNode.Overlay, _dragOverlaySourceSlot, targetSlotNode.Slot);
                            _needsSave = true;
                        }
                    }
                }

                _isDraggingOverlay = false;
                _dragOverlayNode = null;
                _dragOverlaySourceSlot = null;
                e.Use();
                Repaint();
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _isDraggingOverlay = false;
                _dragOverlayNode = null;
                _dragOverlaySourceSlot = null;
                e.Use();
                Repaint();
            }
        }

        private void HandleSlotDragging(Rect canvasRect)
        {
            if (!_isDraggingSlot || _dragSlotNode == null) return;
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                _dragSlotMousePos = ScreenToGraph(e.mousePosition);
                ComputeSlotInsertPreview(_dragSlotMousePos.y, _dragSlotNode);
                Repaint();
                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                if (_slotInsertPreviewIndex >= 0 && _dragSlotOriginalIndex >= 0)
                {
                    int targetIndex = _slotInsertPreviewIndex;
                    if (targetIndex > _dragSlotOriginalIndex) targetIndex--;

                    if (targetIndex != _dragSlotOriginalIndex)
                    {
                        MoveSlotToIndex(_dragSlotNode.Slot, targetIndex);
                        _needsSave = true;
                    }
                }

                _isDraggingSlot = false;
                _dragSlotNode = null;
                _dragSlotOriginalIndex = -1;
                _slotInsertPreviewIndex = -1;
                Repaint();
                e.Use();
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _isDraggingSlot = false;
                _dragSlotNode = null;
                _dragSlotOriginalIndex = -1;
                _slotInsertPreviewIndex = -1;
                e.Use();
                Repaint();
            }
        }

        private void ComputeSlotInsertPreview(float mouseYGraph, Node draggingSlotNode)
        {
            var slots = GetSortedNodesByType(NodeType.Slot);
            if (slots.Count == 0)
            {
                _slotInsertPreviewIndex = 0;
                _slotInsertPreviewY = 0;
                return;
            }

            int insertIndex = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (mouseYGraph < slots[i].Rect.center.y) { insertIndex = i; break; }
                insertIndex = i + 1;
            }
            _slotInsertPreviewIndex = insertIndex;

            if (insertIndex <= 0)
            {
                _slotInsertPreviewY = slots[0].Rect.yMin - RowSpacing * 0.5f;
            }
            else if (insertIndex >= slots.Count)
            {
                _slotInsertPreviewY = slots[slots.Count - 1].Rect.yMax + RowSpacing * 0.5f;
            }
            else
            {
                var above = slots[insertIndex - 1].Rect;
                var below = slots[insertIndex].Rect;
                _slotInsertPreviewY = (above.yMax + below.yMin) * 0.5f;
            }
        }

        private void ComputeOverlayInsertPreview(SlotData slot, float mouseYGraph, out int insertIndex, out float guideY)
        {
            insertIndex = -1; guideY = 0f;
            if (slot == null) return;

            var overlays = slot.GetOverlayList() ?? new List<OverlayData>();

            // Only consider overlay nodes that are actually used by this slot (match identity)
            var overlayNodes = new List<Node>();
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.Overlay || n.Overlay == null) continue;
                var keyN = GetOverlayKey(n.Overlay);
                foreach (var ov in overlays)
                {
                    if (GetOverlayKey(ov).Equals(keyN))
                    {
                        overlayNodes.Add(n);
                        break;
                    }
                }
            }

            SortNodesByYMin(overlayNodes);
            if (overlayNodes.Count == 0) { insertIndex = 0; guideY = GetOverlayColumnYStartGraphForSlot(slot); return; }

            int idx = 0;
            for (int i = 0; i < overlayNodes.Count; i++)
            {
                if (mouseYGraph < overlayNodes[i].Rect.center.y) { idx = i; break; }
                idx = i + 1;
            }
            insertIndex = idx;

            if (idx <= 0)
            {
                guideY = overlayNodes[0].Rect.yMin - RowSpacing * 0.5f;
            }
            else if (idx >= overlayNodes.Count)
            {
                guideY = overlayNodes[overlayNodes.Count - 1].Rect.yMax + RowSpacing * 0.5f;
            }
            else
            {
                var above = overlayNodes[idx - 1].Rect;
                var below = overlayNodes[idx].Rect;
                guideY = (above.yMax + below.yMin) * 0.5f;
            }
        }

        private float GetOverlayColumnYStartGraphForSlot(SlotData slot)
        {
            var overlays = slot?.GetOverlayList();
            if (overlays == null || overlays.Count == 0) return 0f;

            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.Overlay || n.Overlay == null) continue;
                var keyN = GetOverlayKey(n.Overlay);
                foreach (var ov in overlays)
                {
                    if (GetOverlayKey(ov).Equals(keyN))
                    {
                        return n.Rect.yMin - RowSpacing * 0.5f;
                    }
                }
            }
            return 0f;
        }

        private float GetSlotColumnXScreen()
        {
            var n = GetFirstNodeByType(NodeType.Slot);
            return n != null ? GraphToScreenRect(n.Rect).xMin : 0f;
        }

        private float GetOverlayColumnXScreenForSlot(Node slotNode)
        {
            var overlay = GetFirstNodeByType(NodeType.Overlay);
            if (overlay != null) return GraphToScreenRect(overlay.Rect).xMin;
            return GraphToScreenRect(slotNode.Rect).xMin - ColumnSpacing * _zoom;
        }

        private void DrawRectOutline(Rect r, Color color, float thickness)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Vector3 p1 = new Vector3(r.xMin, r.yMin);
            Vector3 p2 = new Vector3(r.xMax, r.yMin);
            Vector3 p3 = new Vector3(r.xMax, r.yMax);
            Vector3 p4 = new Vector3(r.xMin, r.yMax);
            Handles.DrawAAPolyLine(thickness, p1, p2, p3, p4, p1);
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void HandlePortClick(Node node, Rect portRectScreen, PortKind kind, int slotPortIndex)
        {
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDown && e.button == 0 && portRectScreen.Contains(e.mousePosition))
            {
                _isLinking = true;
                _linkStartNode = node;
                _linkStartPortKind = kind;
                _linkStartSlotPortIndex = slotPortIndex;
                _linkMousePosGraph = ScreenToGraph(e.mousePosition);
                e.Use();
            }

            if (portRectScreen.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.AddCursorRect(portRectScreen, MouseCursor.ArrowPlus);
            }
        }

        private bool TryCompleteLink(Vector2 mouseGraph, bool altCopy)
        {
            if (_linkStartNode == null || _linkStartPortKind == PortKind.None) return false;

            switch (_linkStartPortKind)
            {
                // SharedColor (out) -> Overlay Color (in)
                case PortKind.SharedColorOut:
                    {
                        var targetOverlay = HitTestOverlayColorInPort(mouseGraph);
                        if (targetOverlay != null && _linkStartNode.SharedColor != null)
                        {
                            targetOverlay.Overlay.colorData = _linkStartNode.SharedColor;
                            return true;
                        }
                        break;
                    }

                // Overlay Color (in) -> SharedColor (out)
                case PortKind.OverlayColorIn:
                    {
                        var targetSC = HitTestSharedColorOutPort(mouseGraph);
                        if (targetSC != null && targetSC.SharedColor != null && _linkStartNode.Overlay != null)
                        {
                            _linkStartNode.Overlay.colorData = targetSC.SharedColor;
                            return true;
                        }
                        break;
                    }

                // Overlay (out) -> Slot (in port) OR Slot body (append)
                case PortKind.OverlayOut:
                    {
                        Node slotNode;
                        int portIndex;
                        if (HitTestSlotInPort(mouseGraph, out slotNode, out portIndex))
                        {
                            if (slotNode != null && slotNode.Slot != null && _linkStartNode.Overlay != null)
                            {
                                AttachOverlayToSlotAt(slotNode.Slot, portIndex, _linkStartNode.Overlay, altCopy);
                                return true;
                            }
                        }
                        else
                        {
                            // Append if dropped on slot body
                            var bodySlot = HitTestSlotNodeGraph(mouseGraph);
                            if (bodySlot != null && bodySlot.Slot != null && _linkStartNode.Overlay != null)
                            {
                                var list = bodySlot.Slot.GetOverlayList() ?? new List<OverlayData>();
                                if (altCopy)
                                {
                                    var dup = CloneOverlay(_linkStartNode.Overlay);
                                    list.Add(dup);
                                }
                                else
                                {
                                    MoveOverlayToSlot(_linkStartNode.Overlay, FindSlotForOverlay(_linkStartNode.Overlay), bodySlot.Slot);
                                }
                                bodySlot.Slot.SetOverlayList(list);
                                return true;
                            }
                        }
                        break;
                    }

                // Slot (in port) -> Overlay (out)
                case PortKind.SlotIn:
                    {
                        var overlayOut = HitTestOverlayOutPort(mouseGraph);
                        if (overlayOut != null && overlayOut.Overlay != null && _linkStartNode.Slot != null && _linkStartSlotPortIndex >= 0)
                        {
                            AttachOverlayToSlotAt(_linkStartNode.Slot, _linkStartSlotPortIndex, overlayOut.Overlay, altCopy);
                            return true;
                        }
                        break;
                    }
            }

            return false;
        }

        private void AttachOverlayToSlotAt(SlotData targetSlot, int targetIndex, OverlayData overlay, bool altCopy)
        {
            if (targetSlot == null || overlay == null) return;

            var owner = FindSlotForOverlay(overlay);
            var tgtList = targetSlot.GetOverlayList() ?? new List<OverlayData>();

            OverlayData toInsert = overlay;
            if (altCopy)
            {
                toInsert = CloneOverlay(overlay);
            }
            else
            {
                // Moving: remove from current owner (adjust index if same slot)
                if (owner != null)
                {
                    var src = owner.GetOverlayList() ?? new List<OverlayData>();
                    int oldIdx = src.IndexOf(overlay);
                    if (oldIdx >= 0)
                    {
                        src.RemoveAt(oldIdx);
                        owner.SetOverlayList(src);

                        if (ReferenceEquals(owner, targetSlot) && targetIndex > oldIdx)
                        {
                            targetIndex--;
                        }
                    }
                }
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, tgtList.Count);
            tgtList.Insert(targetIndex, toInsert);
            targetSlot.SetOverlayList(tgtList);

            _needsSave = true;
            Repaint();
        }

        private Node HitTestOverlayColorInPort(Vector2 mouseGraph)
        {
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.Overlay) continue;
                if (GetOverlayColorInPortRectGraph(n).Contains(mouseGraph)) return n;
            }
            return null;
        }

        private Node HitTestOverlayOutPort(Vector2 mouseGraph)
        {
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.Overlay) continue;
                if (GetOverlayOutPortRectGraph(n).Contains(mouseGraph)) return n;
            }
            return null;
        }

        private Node HitTestSharedColorOutPort(Vector2 mouseGraph)
        {
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.SharedColor) continue;
                if (GetSharedColorOutPortRectGraph(n).Contains(mouseGraph)) return n;
            }
            return null;
        }

        private bool HitTestSlotInPort(Vector2 mouseGraph, out Node slotNode, out int portIndex)
        {
            foreach (var sn in _nodes)
            {
                if (sn.Type != NodeType.Slot) continue;
                var list = sn.Slot.GetOverlayList() ?? new List<OverlayData>();
                for (int i = 0; i < list.Count; i++)
                {
                    var r = GetSlotInPortRectGraph(sn, i);
                    if (r.Contains(mouseGraph))
                    {
                        slotNode = sn;
                        portIndex = i;
                        return true;
                    }
                }
            }
            slotNode = null;
            portIndex = -1;
            return false;
        }

        private Node FindSharedColorNode(OverlayColorData sc)
        {
            if (sc == null) return null;

            // Prefer an exact reference match first (fast and canonical)
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.SharedColor) continue;
                if (ReferenceEquals(n.SharedColor, sc)) return n;
            }

            // Fall back to value equality if reference didn't match.
            // OverlayColorData may be a different instance but represent the same shared color.
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.SharedColor) continue;
                if (n.SharedColor != null && sc != null && n.SharedColor.Equals(sc)) return n;
            }

            // Last-resort fallback: compare hash codes to handle cases where Equals isn't implemented robustly.
            foreach (var n in _nodes)
            {
                if (n.Type != NodeType.SharedColor) continue;
                if (n.SharedColor != null && sc != null && n.SharedColor.GetHashCode() == sc.GetHashCode()) return n;
            }

            return null;
        }

        private static void DrawPort(Rect rect)
        {
            Handles.BeginGUI();
            var center = rect.center;
            Handles.color = COLOR_PORT_FILL;
            Handles.DrawSolidDisc(center, Vector3.forward, 5f);
            Handles.color = COLOR_PORT_OUTLINE;
            Handles.DrawWireDisc(center, Vector3.forward, 5f);
            Handles.EndGUI();
        }

        // Ports in graph-space
        private static Rect GetSharedColorOutPortRectGraph(Node node)
        {
            return new Rect(node.Rect.xMax - 12f, node.Rect.center.y - 6f, 12f, 12f);
        }

        private static Rect GetOverlayColorInPortRectGraph(Node node)
        {
            return new Rect(node.Rect.x - 12f, node.Rect.center.y - 6f, 12f, 12f);
        }

        private static Rect GetOverlayOutPortRectGraph(Node node)
        {
            return new Rect(node.Rect.xMax - 12f, node.Rect.center.y - 6f, 12f, 12f);
        }

        private Rect GetSlotInPortRectGraph(Node slotNode, int overlayIndex)
        {
            var list = slotNode.Slot.GetOverlayList() ?? new List<OverlayData>();
            if (overlayIndex < 0 || overlayIndex >= list.Count)
            {
                // Fallback: center of the slot node
                return new Rect(slotNode.Rect.x - 12f, slotNode.Rect.center.y - 6f, 12f, 12f);
            }

            // Distribute ports vertically within the slot node, independent of overlay node Y.
            const float topBottomMargin = 10f; // keep ports inside the box
            float innerHeight = Mathf.Max(4f, NodeHeight - (topBottomMargin * 2f));
            float step = innerHeight / Mathf.Max(1, list.Count);
            float y = slotNode.Rect.yMin + topBottomMargin + (overlayIndex + 0.5f) * step;

            return new Rect(slotNode.Rect.x - 12f, y - 6f, 12f, 12f);
        }

        private Rect oldGetSlotInPortRectGraph(Node slotNode, int overlayIndex)
        {
            // (Kept for reference) Align slot ports to overlay node Y (caused zero deltaY issues while dragging)
            var list = slotNode.Slot.GetOverlayList() ?? new List<OverlayData>();
            if (overlayIndex < 0 || overlayIndex >= list.Count)
            {
                return new Rect(slotNode.Rect.x - 12f, slotNode.Rect.center.y - 6f, 12f, 12f);
            }

            var ov = list[overlayIndex];
            var key = GetOverlayKey(ov);
            if (_overlayToNode.TryGetValue(key, out var overlayNode) && overlayNode != null)
            {
                float y = overlayNode.Rect.center.y;
                return new Rect(slotNode.Rect.x - 12f, y - 6f, 12f, 12f);
            }

            float portY = slotNode.Rect.yMin + (overlayIndex + 1) * (NodeHeight / (Mathf.Max(1, list.Count) + 1));
            return new Rect(slotNode.Rect.x - 12f, portY - 6f, 12f, 12f);
        }

        private Vector2 GetPortCenterScreen(Node node, PortKind kind, int slotPortIndex)
        {
            Rect r;
            switch (kind)
            {
                case PortKind.SharedColorOut:
                    r = GetSharedColorOutPortRectGraph(node);
                    break;
                case PortKind.OverlayColorIn:
                    r = GetOverlayColorInPortRectGraph(node);
                    break;
                case PortKind.OverlayOut:
                    r = GetOverlayOutPortRectGraph(node);
                    break;
                case PortKind.SlotIn:
                    r = GetSlotInPortRectGraph(node, slotPortIndex);
                    break;
                default:
                    r = node.Rect;
                    break;
            }
            return GraphToScreenRect(r).center;
        }

        private void ShowNodeContext(Node node)
        {
            var menu = new GenericMenu();
            switch (node.Type)
            {
                case NodeType.Slot:
                    menu.AddItem(new GUIContent("Add Overlay..."), false, () =>
                    {
                        _overlayPickerTargetSlot = node.Slot;
                        _overlayPickerControlId = GUIUtility.GetControlID(FocusType.Passive);
                        EditorGUIUtility.ShowObjectPicker<OverlayDataAsset>(null, false, "", _overlayPickerControlId);
                    });
                    menu.AddItem(new GUIContent("Remove Slot"), false, () =>
                    {
                        _recipe.RemoveSlot(node.Slot);
                        _needsSave = true;
                        Repaint();
                    });
                    break;
                case NodeType.Overlay:
                    menu.AddItem(new GUIContent("Remove Overlay"), false, () =>
                    {
                        var slot = FindSlotForOverlay(node.Overlay);
                        if (slot != null)
                        {
                            var list = slot.GetOverlayList();
                            list.Remove(node.Overlay);
                            slot.SetOverlayList(list);
                            _needsSave = true;
                            Repaint();
                        }
                    });
                    break;
                case NodeType.SharedColor:
                    menu.AddDisabledItem(new GUIContent("Shared color is edited on the right Inspector"));
                    break;
            }
            menu.ShowAsContext();
        }

        private SlotData FindSlotForOverlay(OverlayData overlay)
        {
            var slots = _recipe.GetAllSlots();
            foreach (var s in slots)
            {
                if (s == null) continue;
                var list = s.GetOverlayList();
                if (list != null && list.Contains(overlay)) return s;
            }
            return null;
        }

        private void MoveOverlayToSlot(OverlayData overlay, SlotData fromSlot, SlotData toSlot)
        {
            if (overlay == null || fromSlot == null || toSlot == null) return;
            if (fromSlot == toSlot) return;

            var src = fromSlot.GetOverlayList() ?? new List<OverlayData>();
            if (src.Remove(overlay))
            {
                fromSlot.SetOverlayList(src);
            }

            var dst = toSlot.GetOverlayList() ?? new List<OverlayData>();
            dst.Add(overlay);
            toSlot.SetOverlayList(dst);
        }

        private OverlayData CloneOverlay(OverlayData src)
        {
            if (src == null || src.asset == null) return null;
            var ov = new OverlayData(src.asset);
            ov.CopyColors(src);
            try { ov.rect = src.rect; } catch { }
            try { ov.Rotation = src.Rotation; } catch { }
            try { ov.Scale = src.Scale; } catch { }
            try { ov.Translate = src.Translate; } catch { }
            try { ov.instanceTransformed = src.instanceTransformed; } catch { }
            try { ov.UVSet = src.UVSet; } catch { }
            return ov;
        }

        private int GetSlotIndex(SlotData slot)
        {
            if (slot == null) return -1;
            var slots = _recipe.GetAllSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                if (ReferenceEquals(slots[i], slot)) return i;
            }
            return -1;
        }

        private void MoveSlotToIndex(SlotData slot, int newIndex)
        {
            if (slot == null) return;
            var currentSlots = GetNonNullRecipeSlots();
            int oldIndex = currentSlots.FindIndex(s => ReferenceEquals(s, slot));
            if (oldIndex < 0) return;

            newIndex = Mathf.Clamp(newIndex, 0, currentSlots.Count);
            if (newIndex > oldIndex) newIndex--;

            if (newIndex == oldIndex) return;

            currentSlots.RemoveAt(oldIndex);
            currentSlots.Insert(newIndex, slot);
            _recipe.slotDataList = currentSlots.ToArray();
        }

        private void DrawInspector()
        {
            if (_recipe == null)
            {
                EditorGUILayout.HelpBox("No recipe loaded.", MessageType.Info);
                return;
            }

            // DNA & Race at top
            EditorGUILayout.LabelField("Recipe", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var newRace = (RaceData)EditorGUILayout.ObjectField("RaceData", _recipe.raceData, typeof(RaceData), false);
            if (EditorGUI.EndChangeCheck())
            {
                _recipe.SetRace(newRace);
                _recipe.ClearDNAConverters();
                _needsSave = true;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("DNA", EditorStyles.boldLabel);
            if (_dnaEditor == null) _dnaEditor = new DNAMasterEditor(_recipe);
            var dnaChanged = _dnaEditor.OnGUI(ref _dnaDirtyDummy, ref _textureDirtyDummy, ref _meshDirtyDummy);
            if (dnaChanged) _needsSave = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Shared Colors", EditorStyles.boldLabel);
            var scChanged = _sharedColorsEditor.OnGUI(_recipe);
            if (scChanged) _needsSave = true;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            if (_selectedNode == null)
            {
                EditorGUILayout.HelpBox("Select a node in the graph to edit its details.", MessageType.Info);
                return;
            }

            switch (_selectedNode.Type)
            {
                case NodeType.Slot:
                    {
                        var slot = _selectedNode.Slot;
                        var slotEditor = new SlotEditor(_recipe, slot, 0, _asset);
                        bool dna = false, tex = false, mesh = false;
                        var changed = slotEditor.OnGUI(ref dna, ref tex, ref mesh);
                        if (changed) _needsSave = true;

                        EditorGUILayout.Space(8);
                        var addOverlay = (OverlayDataAsset)EditorGUILayout.ObjectField("Add Overlay", null, typeof(OverlayDataAsset), false);
                        if (addOverlay != null)
                        {
                            var newOverlay = new OverlayData(addOverlay);
                            var list = slot.GetOverlayList();
                            list.Add(newOverlay);
                            slot.SetOverlayList(list);
                            _needsSave = true;
                            Repaint();
                        }
                        break;
                    }

                case NodeType.Overlay:
                    {
                        var ov = _selectedNode.Overlay;
                        var slot = FindSlotForOverlay(ov) ?? GetFirstNonNullRecipeSlot();
                        var overlayEditor = new OverlayEditor(_recipe, slot, ov, null, _asset);
                        var changed = overlayEditor.OnGUI();
                        if (changed) _needsSave = true;
                        break;
                    }

                case NodeType.SharedColor:
                    {
                        EditorGUILayout.LabelField("Selected Shared Color", EditorStyles.miniBoldLabel);
                        var sc = _selectedNode.SharedColor;
                        if (sc != null)
                        {
                            EditorGUILayout.LabelField("Name", string.IsNullOrEmpty(sc.name) ? "(unnamed)" : sc.name);
                            EditorGUILayout.HelpBox("Edit in the Shared Colors section above. Overlays linked to this shared color are shown in the graph.", MessageType.Info);
                        }
                        break;
                    }
            }
        }

        // Dummy flags
        private bool _dnaDirtyDummy, _textureDirtyDummy, _meshDirtyDummy;

        private void HandleGlobalDragAndDrop(Rect? canvasRect)
        {
            Rect dropRect;
            if (canvasRect.HasValue)
            {
                dropRect = new Rect(0, 0, canvasRect.Value.width, canvasRect.Value.height);
            }
            else
            {
                dropRect = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
            }

            var evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition))
                return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var changed = false;

                    if (_recipe == null)
                    {
                        var droppedRecipe = GetFirstDroppedRecipe(DragAndDrop.objectReferences);
                        if (droppedRecipe != null)
                        {
                            LoadRecipe(droppedRecipe);
                            evt.Use();
                            return;
                        }
                    }

                    if (_recipe != null)
                    {
                        var draggedObjects = DragAndDrop.objectReferences;
                        var draggedSlots = new List<SlotDataAsset>();
                        var draggedOverlays = new List<OverlayDataAsset>();

                        foreach (var obj in draggedObjects)
                        {
                            if (obj is SlotDataAsset sda) { draggedSlots.Add(sda); continue; }
                            if (obj is OverlayDataAsset oda) { draggedOverlays.Add(oda); continue; }
                            if (obj is UMATextRecipe tr)
                            {
                                var rec = tr.GetCachedRecipe();
                                if (rec != null)
                                {
                                    _recipe.Merge(rec, false);
                                    changed = true;
                                }
                                continue;
                            }

                            var path = AssetDatabase.GetAssetPath(obj);
                            if (Directory.Exists(path))
                            {
                                RecursiveScanFoldersForAssets(path, draggedSlots, draggedOverlays);
                            }
                        }

                        // If 1 overlay and multiple slots, add overlay to all slots
                        if (draggedSlots.Count >= 1 && draggedOverlays.Count == 1)
                        {
                            foreach (var sd in draggedSlots)
                            {
                                var slot = new SlotData(sd);
                                slot.AddOverlay(new OverlayData(draggedOverlays[0]));
                                _recipe.MergeSlot(slot, false);
                                changed = true;
                            }
                        }
                        else
                        {
                            // Add slots
                            SlotData firstSlot = null;
                            foreach (var sd in draggedSlots)
                            {
                                var slot = new SlotData(sd);
                                slot = _recipe.MergeSlot(slot, false);
                                changed = true;
                                if (firstSlot == null) firstSlot = slot;
                            }
                            // Add overlays to first available slot
                            if (draggedOverlays.Count > 0)
                            {
                                if (firstSlot == null)
                                {
                                    firstSlot = GetFirstNonNullRecipeSlot();
                                }
                                if (firstSlot != null)
                                {
                                    foreach (var od in draggedOverlays)
                                    {
                                        firstSlot.AddOverlay(new OverlayData(od));
                                        changed = true;
                                    }
                                }
                            }
                        }

                        if (changed)
                        {
                            _needsSave = true;
                            Repaint();
                        }
                    }

                    evt.Use();
                }
                else
                {
                    evt.Use();
                }
            }
        }

        private void HandleObjectPicker()
        {
            var e = Event.current;
            if (e == null) return;

            if ((_overlayPickerControlId != -1) &&
                (e.commandName == "ObjectSelectorUpdated" || e.commandName == "ObjectSelectorClosed") &&
                EditorGUIUtility.GetObjectPickerControlID() == _overlayPickerControlId)
            {
                var picked = EditorGUIUtility.GetObjectPickerObject() as OverlayDataAsset;
                if (picked != null && _overlayPickerTargetSlot != null)
                {
                    var newOverlay = new OverlayData(picked);
                    var list = _overlayPickerTargetSlot.GetOverlayList();
                    list.Add(newOverlay);
                    _overlayPickerTargetSlot.SetOverlayList(list);
                    _needsSave = true;
                    Repaint();
                }

                if (e.commandName == "ObjectSelectorClosed")
                {
                    _overlayPickerControlId = -1;
                    _overlayPickerTargetSlot = null;
                }
            }
        }

        private static void RecursiveScanFoldersForAssets(string path, List<SlotDataAsset> slots, List<OverlayDataAsset> overlays)
        {
            var assetFiles = Directory.GetFiles(path, "*.asset");
            foreach (var assetFile in assetFiles)
            {
                var sda = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(assetFile);
                if (sda != null) slots.Add(sda);

                var oda = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(assetFile);
                if (oda != null) overlays.Add(oda);
            }
            foreach (var subFolder in Directory.GetDirectories(path))
            {
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), slots, overlays);
            }
        }

        // Manual layout utils
        private void ApplyManualPosition(Node n)
        {
            if (!_manualLayout) return;
            var key = NodeKey(n);
            if (_manualPositions.TryGetValue(key, out var pos))
            {
                n.Rect.position = pos;
            }
            else
            {
                _manualPositions[key] = n.Rect.position;
            }
        }

        private string NodeKey(Node n)
        {
            switch (n.Type)
            {
                case NodeType.Slot: return $"Slot:{n.Slot?.slotName ?? n.Title}";
                case NodeType.Overlay:
                    {
                        string owner = n.Slot?.slotName ?? FindSlotForOverlay(n.Overlay)?.slotName ?? "UnknownSlot";
                        string oname = n.Overlay?.overlayName ?? n.Title;
                        return $"Overlay:{owner}/{oname}";
                    }
                case NodeType.SharedColor: return $"SharedColor:{n.Index}:{n.SharedColor?.name ?? "Unnamed"}";
                case NodeType.Recipe: return $"Recipe:{_asset?.name ?? "Recipe"}";
                default: return $"{n.Type}:{n.Title}";
            }
        }

        private void SaveLayout()
        {
            if (string.IsNullOrEmpty(_layoutPrefsKey)) return;
            var data = new LayoutData { zoom = _zoom, pan = _pan };
            foreach (var kv in _manualPositions)
            {
                data.keys.Add(kv.Key);
                data.values.Add(kv.Value);
            }
            var json = JsonUtility.ToJson(data);
            EditorPrefs.SetString(_layoutPrefsKey, json);
        }

        private void LoadLayout()
        {
            _manualPositions.Clear();
            if (string.IsNullOrEmpty(_layoutPrefsKey)) return;
            var json = EditorPrefs.GetString(_layoutPrefsKey, "");
            if (string.IsNullOrEmpty(json)) { _zoom = 1f; _pan = Vector2.zero; return; }
            try
            {
                var data = JsonUtility.FromJson<LayoutData>(json);
                _zoom = Mathf.Clamp(data.zoom, 0.25f, 3f);
                _pan = data.pan;
                if (data.keys != null && data.values != null && data.keys.Count == data.values.Count)
                {
                    for (int i = 0; i < data.keys.Count; i++) _manualPositions[data.keys[i]] = data.values[i];
                }
            }
            catch { _zoom = 1f; _pan = Vector2.zero; }
        }

        private void HandlePanZoom(Rect graphArea)
        {
            var e = Event.current;
            if (e == null) return;

            // Ctrl + scroll for zoom
            if (e.type == EventType.ScrollWheel && (e.control || e.command))
            {
                float delta = -e.delta.y * 0.05f;
                _zoom = Mathf.Clamp(_zoom + delta, 0.25f, 3f);

                // Zoom about mouse position
                var mouse = e.mousePosition;
                var graphBefore = ScreenToGraph(mouse);
                var graphAfter = ScreenToGraph(mouse);
                _pan += (graphBefore - graphAfter);

                SaveLayout();
                e.Use();
                Repaint();
            }

            // Middle mouse drag to pan
            if (e.type == EventType.MouseDrag && e.button == 2)
            {
                _pan += e.delta / _zoom;
                SaveLayout();
                e.Use();
                Repaint();
            }
        }

        // Transform helpers: graph<->screen
        private Rect GraphToScreenRect(Rect r) => new Rect(GraphToScreen(r.position), r.size * _zoom);
        private Vector2 GraphToScreen(Vector2 p) => (p + _pan) * _zoom;
        private float GraphToScreenY(float y) => (y + _pan.y) * _zoom;
        private Vector2 ScreenToGraph(Vector2 p) => (p / _zoom) - _pan;

        // Auto arrange: line up all Slot nodes in a vertical list
        private void AutoArrangeSlots()
        {
            if (_recipe == null) return;

            float defaultSlotX = 12f + ColumnSpacing * 2f;
            float slotX = defaultSlotX;

            var slots = _recipe.GetAllSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                var key = $"Slot:{s.slotName}";
                if (_manualPositions.TryGetValue(key, out var pos))
                {
                    slotX = pos.x;
                    break;
                }
            }

            float y = 8f;
            float step = NodeHeight + RowSpacing;

            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                var key = $"Slot:{s.slotName}";
                _manualPositions[key] = new Vector2(slotX, y);
                y += step;
            }

            _manualLayout = true;
            SaveLayout();
            Repaint();
        }
    }
}