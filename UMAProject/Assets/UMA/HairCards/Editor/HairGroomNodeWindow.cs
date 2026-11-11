using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.HairCards.Editor
{
    /// <summary>
    /// Groom Outliner: the groom's ordered stack (maps, guides, sculpt passes and modifiers,
    /// constraints, populations, cards resources and shared helpers) as a tree. Selecting an
    /// item edits it in Hair Properties; right-click (or Add) creates, orders or removes items.
    /// </summary>
    public sealed partial class HairGroomNodeWindow : EditorWindow
    {
        internal const string WindowTitle = "Groom Outliner";

        // Serialized fields double as per-groom preferences (see HairPreferenceCodec); keep names.
        [SerializeField] private Vector2 nodeScroll;
        [SerializeField] private string nodeSearch = string.Empty;
        [SerializeField] private List<string> collapsedNodeKeys = new List<string>();
        [SerializeField] private List<string> expandedOptionalMapKeys = new List<string>();
        [SerializeField] private bool initialPlacementDone;
        private HairGroomAsset preferenceGroom;
        private string preferenceContext;
        private double nextSave;
        private int savedInteraction = -1;

        private MultiColumnTreeView tree;
        private ToolbarSearchField searchField;
        private ToolbarButton issuesButton;
        private VisualElement content, emptyState, stepBar;
        private Label groomLabel;
        private readonly List<ToolbarToggle> stepToggles = new List<ToolbarToggle>();
        // TreeView ids are window-local integers mapped from the nodes' stable string keys.
        private readonly Dictionary<string, int> idsByKey = new Dictionary<string, int>();
        private readonly Dictionary<int, string> keysById = new Dictionary<int, string>();
        private int nextId = 1;
        private HairCardStage shownStage;
        private HairGroomAsset shownGroom;
        private int shownStructure, shownState;
        private string shownSearch, syncedKey, lastStageSelection;
        private readonly HashSet<string> shownKeys = new HashSet<string>();
        private bool rebuildRequested = true, suppressTreeEvents;

        private static readonly (HairWorkflowStep step, string label, string tooltip)[] Steps =
        {
            (HairWorkflowStep.Setup, "Setup", "Source & Setup"),
            (HairWorkflowStep.Growth, "Growth", "Paint growth, density and optional maps"),
            (HairWorkflowStep.Guides, "Guides", "Place, generate and select guides"),
            (HairWorkflowStep.Groom, "Groom", "Sculpt passes, modifiers, constraints and helpers"),
            (HairWorkflowStep.Cards, "Cards", "Generate hair, card geometry, materials and scalp shading"),
            (HairWorkflowStep.Optimize, "LODs", "Optimize & LODs"),
            (HairWorkflowStep.ValidateAndBake, "Bake", "Validate & Bake")
        };

        [MenuItem("UMA/Hair Cards/Groom Outliner", priority = 211)]
        public static void Open()
        {
            var window = GetWindow<HairGroomNodeWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(240f, 300f);
            if (!window.initialPlacementDone) { window.position = new Rect(60f, 80f, 340f, 700f); window.initialPlacementDone = true; }
            window.Show();
        }

        internal static void RepaintOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomNodeWindow>())
            { window.rebuildRequested = true; window.Repaint(); }
        }

        internal static void SaveOpenPreferences()
        {
            if (HairEditorPreferences.Suspended) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomNodeWindow>())
                HairEditorPreferences.instance.Remember(window, "nodes", window.preferenceContext);
        }

        internal static void ResetOpenPreferences()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomNodeWindow>())
            {
                HairPreferenceCodec.Reset(window);
                window.syncedKey = null; window.rebuildRequested = true;
                window.searchField?.SetValueWithoutNotify(window.nodeSearch ?? string.Empty);
                window.Repaint();
            }
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(240f, 300f);
            if (HairEditorPreferences.Suspended) return;
            RestoreContext();
            EditorApplication.update += SaveIdle;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            EditorApplication.update -= SaveIdle;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (!HairEditorPreferences.Suspended) HairEditorPreferences.instance.Remember(this, "nodes", preferenceContext);
        }

        private void OnUndoRedo() { rebuildRequested = true; Repaint(); }

        private void SaveIdle()
        {
            if (HairEditorPreferences.Suspended || EditorApplication.timeSinceStartup < nextSave || HairCardStage.ActiveStage?.IsEditing == true ||
                savedInteraction == HairEditorPreferences.InteractionVersion) return;
            nextSave = EditorApplication.timeSinceStartup + 2d;
            savedInteraction = HairEditorPreferences.InteractionVersion;
            HairEditorPreferences.instance.Remember(this, "nodes", preferenceContext);
        }

        private void RestoreContext()
        {
            preferenceGroom = HairCardStage.ActiveStage?.Groom;
            preferenceContext = HairEditorPreferences.Context(preferenceGroom);
            collapsedNodeKeys.Clear(); expandedOptionalMapKeys.Clear(); nodeSearch = string.Empty; nodeScroll = Vector2.zero;
            HairEditorPreferences.instance.Restore(this, "nodes", preferenceContext);
            nodeSearch ??= string.Empty;
            searchField?.SetValueWithoutNotify(nodeSearch);
            syncedKey = null; rebuildRequested = true;
        }

        /// <summary>Visible rows for a search, with matching items' ancestors and descendants;
        /// without a search, rows beneath collapsed items are omitted.</summary>
        internal static List<HairGroomNode> Filter(IReadOnlyList<HairGroomNode> nodes, string search,
            ICollection<string> collapsed, ICollection<string> optionalExpanded)
        {
            var byKey = new Dictionary<string, HairGroomNode>();
            foreach (var node in nodes) byKey[node.Key] = node;
            var matches = new HashSet<string>();
            bool searching = !string.IsNullOrWhiteSpace(search);
            if (searching)
                foreach (var node in nodes)
                    if ((node.Label + " " + node.Description + " " + node.Kind).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        matches.Add(node.Key);
            var visibleMatches = new HashSet<string>(matches);
            foreach (string key in matches)
            {
                var item = byKey[key];
                while (item.ParentKey != null && byKey.TryGetValue(item.ParentKey, out item)) visibleMatches.Add(item.Key);
            }
            var visible = new List<HairGroomNode>();
            foreach (var node in nodes)
            {
                bool show = !searching || visibleMatches.Contains(node.Key);
                var ancestor = node;
                while (ancestor.ParentKey != null && byKey.TryGetValue(ancestor.ParentKey, out ancestor))
                {
                    if (searching) { if (matches.Contains(ancestor.Key)) show = true; }
                    else if (ancestor.Kind == HairGroomNodeKind.OptionalMaps ? !optionalExpanded.Contains(ancestor.Key) : collapsed.Contains(ancestor.Key))
                    { show = false; break; }
                }
                if (show) visible.Add(node);
            }
            return visible;
        }

        // ---- Layout ----------------------------------------------------------------------

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(_ => HairEditorPreferences.NoteInteraction(), TrickleDown.TrickleDown);

            emptyState = new VisualElement();
            emptyState.style.paddingLeft = emptyState.style.paddingRight = emptyState.style.paddingTop = 6f;
            emptyState.Add(new HelpBox("Open a Hair Groom asset or select a source character/mesh to begin. The Groom Outliner, Hair Properties and Hair Preview & Settings can be docked independently.", HelpBoxMessageType.Info));
            var openSource = new Button(HairCardMenu.OpenSelectedSource) { text = "Open Selected Source" };
            openSource.style.height = 30f; openSource.style.marginTop = 6f;
            emptyState.Add(openSource);
            root.Add(emptyState);

            content = new VisualElement();
            content.style.flexGrow = 1f;
            root.Add(content);

            var toolbar = new Toolbar();
            var add = new ToolbarButton(() => ShowAddMenu()) { text = "Add ▾", tooltip = "Create an item for the selection, a group, or a shared helper." };
            add.name = "outliner-add";
            toolbar.Add(add);
            issuesButton = new ToolbarButton(() => ShowIssuesMenu()) { text = "Issues (0)", tooltip = "Locate a validation setting or guide. Validate & Bake refreshes this report." };
            toolbar.Add(issuesButton);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f; toolbar.Add(spacer);
            var more = new ToolbarButton(() => ShowMoreMenu()) { text = "⋮", tooltip = "Frame, expand or collapse, open windows, save or exit." };
            toolbar.Add(more);
            content.Add(toolbar);

            groomLabel = new Label();
            groomLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groomLabel.style.paddingLeft = 6f; groomLabel.style.paddingTop = 3f; groomLabel.style.paddingBottom = 2f;
            groomLabel.style.overflow = Overflow.Hidden; groomLabel.style.textOverflow = TextOverflow.Ellipsis;
            content.Add(groomLabel);

            stepBar = new Toolbar();
            stepBar.style.flexWrap = Wrap.Wrap; stepBar.style.height = StyleKeyword.Auto;
            stepToggles.Clear();
            foreach (var (step, label, tooltip) in Steps)
            {
                var captured = step;
                var toggle = new ToolbarToggle { text = label, tooltip = tooltip + " — selecting an item also switches to its step." };
                toggle.RegisterValueChangedCallback(evt => OnStepToggled(captured, evt.newValue));
                stepToggles.Add(toggle); stepBar.Add(toggle);
            }
            content.Add(stepBar);

            var searchRow = new Toolbar();
            searchField = new ToolbarSearchField { tooltip = "Filter by name, description or kind (Ctrl/Cmd+F)." };
            searchField.style.flexGrow = 1f; searchField.style.width = StyleKeyword.Auto;
            searchField.SetValueWithoutNotify(nodeSearch ?? string.Empty);
            searchField.RegisterValueChangedCallback(evt =>
            {
                nodeSearch = evt.newValue ?? string.Empty;
                HairEditorPreferences.NoteInteraction();
                rebuildRequested = true; RefreshNow();
            });
            searchRow.Add(searchField);
            content.Add(searchRow);

            tree = CreateTree();
            content.Add(tree);

            var footer = new IMGUIContainer(DrawFooter);
            footer.style.flexShrink = 0f;
            content.Add(footer);

            rebuildRequested = true;
            RefreshNow();
            root.schedule.Execute(RefreshNow).Every(100);
        }

        private MultiColumnTreeView CreateTree()
        {
            var columns = new Columns();
            columns.Add(new Column { name = "item", title = "Item", stretchable = true, minWidth = 120f, makeCell = MakeItemCell, bindCell = BindItemCell });
            columns.Add(new Column { name = "active", title = "On", width = 30f, minWidth = 30f, maxWidth = 30f, resizable = false, makeCell = MakeActiveCell, bindCell = BindActiveCell });
            columns.Add(new Column { name = "lock", title = "Lock", width = 38f, minWidth = 38f, maxWidth = 38f, resizable = false, makeCell = MakeLockCell, bindCell = BindLockCell });
            columns.reorderable = false;
            var view = new MultiColumnTreeView(columns)
            {
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                reorderable = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                viewDataKey = null
            };
            view.style.flexGrow = 1f;
            view.selectionChanged += _ => OnTreeSelectionChanged();
            view.itemExpandedChanged += OnItemExpandedChanged;
            view.canStartDrag += CanStartDrag;
            view.dragAndDropUpdate += args => ValidateDrop(args, false);
            view.handleDrop += args => ValidateDrop(args, true);
            view.RegisterCallback<ContextClickEvent>(evt => { ShowNodeMenu(null, evt.mousePosition); evt.StopPropagation(); });
            view.RegisterCallback<KeyDownEvent>(OnTreeKeyDown);
            return view;
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void RefreshNow()
        {
            if (content == null) return;
            HairCardStage stage = HairCardStage.ActiveStage;
            if (!HairEditorPreferences.Suspended && stage?.Groom != preferenceGroom)
            {
                HairEditorPreferences.instance.Remember(this, "nodes", preferenceContext);
                RestoreContext();
            }
            bool hasGroom = stage?.Groom != null;
            content.style.display = hasGroom ? DisplayStyle.Flex : DisplayStyle.None;
            emptyState.style.display = hasGroom ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hasGroom) { shownStage = null; shownGroom = null; return; }

            groomLabel.text = stage.Groom.name;
            int issues = stage.ReleaseValidation?.issues.Count ?? 0;
            issuesButton.text = "Issues (" + issues + ")";
            issuesButton.SetEnabled(issues > 0);
            for (int i = 0; i < Steps.Length; i++) stepToggles[i].SetValueWithoutNotify(stage.WorkflowStep == Steps[i].step);

            IReadOnlyList<HairGroomNode> nodes = stage.Nodes;
            int structure = HairGroomNodes.StructureSignature(nodes);
            if (rebuildRequested || stage != shownStage || stage.Groom != shownGroom || structure != shownStructure || nodeSearch != shownSearch)
                RebuildTree(stage, nodes, structure);
            int state = StateSignature(stage);
            if (state != shownState) { shownState = state; tree.RefreshItems(); }
            SyncSelection(stage);
        }

        private void RebuildTree(HairCardStage stage, IReadOnlyList<HairGroomNode> nodes, int structure)
        {
            rebuildRequested = false;
            shownStage = stage; shownGroom = stage.Groom; shownStructure = structure; shownSearch = nodeSearch;
            bool searching = !string.IsNullOrWhiteSpace(nodeSearch);
            IReadOnlyList<HairGroomNode> visible = searching ? Filter(nodes, nodeSearch, Array.Empty<string>(), Array.Empty<string>()) : nodes;
            var present = shownKeys;
            present.Clear();
            foreach (var node in visible) present.Add(node.Key);
            var children = new Dictionary<string, List<HairGroomNode>>();
            var roots = new List<HairGroomNode>();
            foreach (var node in visible)
            {
                if (node.ParentKey == null || !present.Contains(node.ParentKey)) { roots.Add(node); continue; }
                if (!children.TryGetValue(node.ParentKey, out var list)) children[node.ParentKey] = list = new List<HairGroomNode>();
                list.Add(node);
            }
            TreeViewItemData<string> Make(HairGroomNode node)
            {
                List<TreeViewItemData<string>> items = null;
                if (children.TryGetValue(node.Key, out var list))
                {
                    items = new List<TreeViewItemData<string>>(list.Count);
                    foreach (var child in list) items.Add(Make(child));
                }
                return new TreeViewItemData<string>(IdFor(node.Key), node.Key, items);
            }
            var rootItems = new List<TreeViewItemData<string>>(roots.Count);
            foreach (var node in roots) rootItems.Add(Make(node));

            suppressTreeEvents = true;
            try
            {
                tree.SetRootItems(rootItems);
                tree.Rebuild();
                foreach (var node in visible)
                {
                    if (!children.ContainsKey(node.Key)) continue;
                    if (searching || IsExpanded(node)) tree.ExpandItem(IdFor(node.Key), false, false);
                    else tree.CollapseItem(IdFor(node.Key), false, false);
                }
                tree.RefreshItems();
            }
            finally { suppressTreeEvents = false; }
            shownState = StateSignature(stage);
            syncedKey = null;
        }

        private int IdFor(string key)
        {
            if (idsByKey.TryGetValue(key, out int id)) return id;
            id = nextId++;
            idsByKey[key] = id; keysById[id] = key;
            return id;
        }

        private string KeyForIndex(int index) => index >= 0 ? tree.GetItemDataForIndex<string>(index) : null;

        private bool IsExpanded(HairGroomNode node) =>
            node.Kind == HairGroomNodeKind.OptionalMaps ? expandedOptionalMapKeys.Contains(node.Key) : !collapsedNodeKeys.Contains(node.Key);

        private void SetExpandedPreference(HairGroomNode node, bool expanded)
        {
            if (node.Kind == HairGroomNodeKind.OptionalMaps)
            {
                if (expanded) { if (!expandedOptionalMapKeys.Contains(node.Key)) expandedOptionalMapKeys.Add(node.Key); }
                else expandedOptionalMapKeys.Remove(node.Key);
            }
            else if (expanded) collapsedNodeKeys.Remove(node.Key);
            else if (!collapsedNodeKeys.Contains(node.Key)) collapsedNodeKeys.Add(node.Key);
        }

        private static int StateSignature(HairCardStage stage)
        {
            unchecked
            {
                int hash = 19;
                hash = hash * 31 + (stage.IsLayerEditing ? 1 : 0);
                hash = hash * 31 + (stage.ActiveLayer?.Id?.GetHashCode() ?? 0);
                hash = hash * 31 + (stage.InspectedPopulationId?.GetHashCode() ?? 0);
                hash = hash * 31 + (stage.InspectedMaskId?.GetHashCode() ?? 0);
                hash = hash * 31 + (stage.SoloLayerId?.GetHashCode() ?? 0);
                hash = hash * 31 + (stage.ActiveGroup?.Id?.GetHashCode() ?? 0);
                return hash;
            }
        }

        private void SyncSelection(HairCardStage stage)
        {
            string key = stage.SelectedNode?.Key;
            // syncedKey is cleared after a rebuild (re-apply) and when the stage's selection moves.
            bool selectionMoved = key != lastStageSelection;
            if (key == syncedKey && !selectionMoved) return;
            syncedKey = key; lastStageSelection = key;
            suppressTreeEvents = true;
            try
            {
                if (key == null || !shownKeys.Contains(key))
                {
                    if (selectionMoved && key != null && !string.IsNullOrWhiteSpace(nodeSearch))
                    {
                        // The selection is outside the search results; show the whole groom again.
                        nodeSearch = string.Empty; searchField.SetValueWithoutNotify(string.Empty);
                        rebuildRequested = true; syncedKey = null;
                    }
                    else tree.ClearSelection();
                    return;
                }
                int id = IdFor(key);
                RevealAncestors(stage, key);
                tree.SetSelectionByIdWithoutNotify(new[] { id });
                tree.ScrollToItemById(id);
            }
            finally { suppressTreeEvents = false; }
        }

        private void RevealAncestors(HairCardStage stage, string key)
        {
            bool expanded = false;
            var node = stage.FindNode(key);
            while (node?.ParentKey != null)
            {
                node = stage.FindNode(node.ParentKey);
                if (node == null || !shownKeys.Contains(node.Key)) break;
                SetExpandedPreference(node, true);
                int parentId = IdFor(node.Key);
                if (!tree.IsExpanded(parentId)) { tree.ExpandItem(parentId, false, false); expanded = true; }
            }
            if (expanded) tree.RefreshItems();
        }

        // ---- Tree callbacks --------------------------------------------------------------

        private void OnTreeSelectionChanged()
        {
            if (suppressTreeEvents) return;
            HairCardStage stage = HairCardStage.ActiveStage;
            string key = KeyForIndex(tree.selectedIndex);
            if (stage == null || key == null || stage.SelectedNode?.Key == key) return;
            GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
            stage.SelectNode(key);
            HairEditorPreferences.NoteInteraction();
            if (Resources.FindObjectsOfTypeAll<HairGroomWorkspace>().Length == 0 && !HairEditorPreferences.Suspended)
                HairGroomWorkspace.OpenProperties();
            syncedKey = null;
        }

        private void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            if (suppressTreeEvents || !string.IsNullOrWhiteSpace(nodeSearch)) return;
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage == null || !keysById.TryGetValue(args.id, out string key)) return;
            HairGroomNode node = stage.FindNode(key);
            if (node == null) return;
            SetExpandedPreference(node, args.isExpanded);
            HairEditorPreferences.NoteInteraction();
            if (args.isExpanded) return;
            // Collapsing an ancestor of the selection selects the collapsed item, like the Hierarchy.
            var selected = stage.SelectedNode;
            while (selected?.ParentKey != null)
            {
                if (selected.ParentKey == key) { stage.SelectNode(key); syncedKey = null; break; }
                selected = stage.FindNode(selected.ParentKey);
            }
        }

        private void OnStepToggled(HairWorkflowStep step, bool on)
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage == null) return;
            if (on && stage.WorkflowStep != step)
            {
                stage.WorkflowStep = step;
                HairEditorPreferences.NoteInteraction();
                HairGroomWorkspace.RepaintOpenWindows();
            }
            syncedKey = null;
            RefreshNow();
        }

        private void DrawFooter()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage?.Groom == null) return;
            HairEditorPreferences.NoteInteraction(Event.current);
            HairGroomWorkspace.DrawStatus(stage);
            EditorGUILayout.LabelField(stage.SaveStatus + " · " + stage.PreviewStatus, EditorStyles.wordWrappedMiniLabel);
        }

        // ---- Cells -----------------------------------------------------------------------

        private VisualElement MakeItemCell()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.flexGrow = 1f;
            row.style.overflow = Overflow.Hidden;
            var label = new Label { name = "label" };
            label.style.flexShrink = 0f; label.style.unityTextAlign = TextAnchor.MiddleLeft;
            var badges = new Label { name = "badges" };
            badges.style.flexShrink = 1f; badges.style.marginLeft = 6f; badges.style.fontSize = 10f;
            badges.style.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            badges.style.overflow = Overflow.Hidden; badges.style.textOverflow = TextOverflow.Ellipsis; badges.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(label); row.Add(badges);
            row.RegisterCallback<ContextClickEvent>(evt => { ShowNodeMenu(row.userData as string, evt.mousePosition); evt.StopPropagation(); });
            return row;
        }

        private void BindItemCell(VisualElement element, int index)
        {
            string key = KeyForIndex(index);
            element.userData = key;
            HairCardStage stage = HairCardStage.ActiveStage;
            HairGroomNode node = stage?.FindNode(key);
            var label = element.Q<Label>("label");
            var badges = element.Q<Label>("badges");
            if (node == null) { label.text = key; badges.text = string.Empty; return; }
            label.text = node.Label;
            bool inactive = (node.CanToggle && !node.Enabled) || (node.Group != null && (!node.Group.enabled || !node.Group.visible) && node.Kind != HairGroomNodeKind.Group);
            label.style.unityFontStyleAndWeight = node.Kind == HairGroomNodeKind.Group ? FontStyle.Bold : FontStyle.Normal;
            element.style.opacity = inactive ? 0.55f : 1f;
            element.tooltip = node.Description;
            var parts = new List<string>();
            bool bypassed = stage.IsNodeTemporarilyBypassed(node);
            if (bypassed) parts.Add("bypassed");
            else if (node.Layer != null && node.Modifier == null && node.Layer == stage.ActiveLayer && stage.IsLayerEditing) parts.Add("editing");
            if (node.Population != null && node.Modifier == null && stage.InspectedPopulationId == node.Population.Id) parts.Add("inspecting");
            if (node.Modifier != null && stage.InspectedMaskId == node.Modifier.Id) parts.Add("mask shown");
            if (node.Layer != null && node.Modifier == null && stage.SoloLayerId == node.Layer.Id) parts.Add("solo");
            HairGroomNodeReferences.AppendBadges(stage.Groom, node, parts);
            badges.text = string.Join(" · ", parts);
            badges.tooltip = bypassed ? "Temporarily bypassed at the sculpt edit point. The saved Active toggle is unchanged." : badges.text;
        }

        private VisualElement MakeActiveCell()
        {
            var toggle = new Toggle { tooltip = "Include this operation in evaluation. This is independent of selection." };
            toggle.style.alignSelf = Align.Center; toggle.style.marginLeft = 6f;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (suppressTreeEvents) return;
                HairCardStage stage = HairCardStage.ActiveStage;
                HairGroomNode node = stage?.FindNode(toggle.userData as string);
                if (node == null || !HairGroomNodes.Toggle(stage, node, evt.newValue)) toggle.SetValueWithoutNotify(node?.Enabled ?? evt.previousValue);
                HairEditorPreferences.NoteInteraction();
            });
            return toggle;
        }

        private void BindActiveCell(VisualElement element, int index)
        {
            var toggle = (Toggle)element;
            string key = KeyForIndex(index);
            toggle.userData = key;
            HairGroomNode node = HairCardStage.ActiveStage?.FindNode(key);
            bool show = node != null && node.CanToggle;
            toggle.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
            if (!show) return;
            toggle.SetValueWithoutNotify(node.Enabled);
            toggle.SetEnabled(!node.Locked);
        }

        private VisualElement MakeLockCell()
        {
            var toggle = new Toggle { tooltip = "Lock this group, sculpt pass or helper against edits." };
            toggle.style.alignSelf = Align.Center; toggle.style.marginLeft = 10f;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (suppressTreeEvents) return;
                HairCardStage stage = HairCardStage.ActiveStage;
                HairGroomNode node = stage?.FindNode(toggle.userData as string);
                if (node == null || !HairGroomNodes.SetLocked(stage, node, evt.newValue))
                    toggle.SetValueWithoutNotify(node != null && node.Handler.IsLockedSelf(node));
                HairEditorPreferences.NoteInteraction();
            });
            return toggle;
        }

        private void BindLockCell(VisualElement element, int index)
        {
            var toggle = (Toggle)element;
            string key = KeyForIndex(index);
            toggle.userData = key;
            HairGroomNode node = HairCardStage.ActiveStage?.FindNode(key);
            bool show = node != null && (node.Kind == HairGroomNodeKind.Group || node.Kind == HairGroomNodeKind.Layer || node.Kind == HairGroomNodeKind.Helper);
            toggle.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
            if (!show) return;
            toggle.SetValueWithoutNotify(node.Handler.IsLockedSelf(node));
            toggle.SetEnabled(node.Handler.CanLock(node));
        }
    }
}
