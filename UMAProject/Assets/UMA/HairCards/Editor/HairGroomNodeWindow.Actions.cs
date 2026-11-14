using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairGroomNodeWindow
    {
        private string dragKey;

        // ---- Menus -----------------------------------------------------------------------

        private void ShowAddMenu()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage?.Groom == null) return;
            HairGroomNodeCommands.BuildAddMenu(stage, stage.SelectedNode).DropDown(ButtonRect("outliner-add"));
        }

        private void ShowIssuesMenu()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            var menu = HairGroomNodeCommands.BuildIssuesMenu(stage);
            if (menu.GetItemCount() > 0) menu.DropDown(issuesButton.worldBound);
        }

        private void ShowMoreMenu()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            var menu = new GenericMenu();
            bool has = stage?.Groom != null;
            Item(menu, "Frame Groom", has, () => stage.FrameGroom());
            menu.AddSeparator(string.Empty);
            Item(menu, "Expand All", has, ExpandAll);
            Item(menu, "Collapse All", has, CollapseAll);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Open Hair Properties"), false, HairGroomWorkspace.OpenProperties);
            menu.AddItem(new GUIContent("Open Hair Preview && Settings"), false, HairGroomPreviewWindow.Open);
            menu.AddItem(new GUIContent("Quick Start Help"), false, HairGroomWorkspace.OpenQuickStart);
            menu.AddSeparator(string.Empty);
            Item(menu, "Save", has, () => stage.SaveNow());
            Item(menu, "Exit Stage", has, () => stage.ExitGrooming());
            var bounds = rootVisualElement.worldBound;
            menu.DropDown(new Rect(bounds.xMax - 24f, 0f, 24f, 20f));
        }

        private void ShowNodeMenu(string key, Vector2 position)
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage?.Groom == null) return;
            HairGroomNode node = key != null ? stage.FindNode(key) : null;
            // Right-click selects first, like the Hierarchy, so commands act on what was clicked.
            if (node != null && stage.SelectedNode?.Key != node.Key)
            {
                stage.SelectNode(node.Key);
                syncedKey = null; RefreshNow();
                node = stage.FindNode(key);
            }
            HairGroomNodeCommands.BuildNodeMenu(stage, node).DropDown(new Rect(position, Vector2.zero));
        }

        private Rect ButtonRect(string name)
        {
            var element = rootVisualElement.Q(name);
            return element != null ? element.worldBound : new Rect(0f, 0f, 60f, 20f);
        }

        private static void Item(GenericMenu menu, string path, bool enabled, GenericMenu.MenuFunction action)
        {
            if (enabled) menu.AddItem(new GUIContent(path), false, action);
            else menu.AddDisabledItem(new GUIContent(path));
        }

        private void ExpandAll()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage == null) return;
            collapsedNodeKeys.Clear();
            foreach (var node in stage.Nodes)
                if (node.Kind == HairGroomNodeKind.OptionalMaps && !expandedOptionalMapKeys.Contains(node.Key)) expandedOptionalMapKeys.Add(node.Key);
            HairEditorPreferences.NoteInteraction();
            rebuildRequested = true; RefreshNow();
        }

        private void CollapseAll()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage == null) return;
            collapsedNodeKeys.Clear(); expandedOptionalMapKeys.Clear();
            foreach (var node in stage.Nodes) if (node.HasChildren) collapsedNodeKeys.Add(node.Key);
            stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Source));
            HairEditorPreferences.NoteInteraction();
            rebuildRequested = true; syncedKey = null; RefreshNow();
        }

        // ---- Drag and drop: reorder siblings only ----------------------------------------

        private bool CanStartDrag(CanStartDragArgs args)
        {
            dragKey = null;
            HairGroomNode node = HairCardStage.ActiveStage?.FindNode(keysById.TryGetValue(args.id, out string key) ? key : null);
            if (node == null || !node.CanReorder || node.Locked) return false;
            dragKey = node.Key;
            return true;
        }

        private DragVisualMode ValidateDrop(HandleDragAndDropArgs args, bool perform)
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            HairGroomNode source = stage?.FindNode(dragKey);
            if (source == null || !TryGetDropTarget(stage, source, args, out HairGroomNode target, out bool after))
                return DragVisualMode.Rejected;
            if (!perform) return DragVisualMode.Move;
            bool moved = HairGroomNodes.Move(stage, source, target, after);
            dragKey = null;
            if (moved) { stage.SelectNode(source.Key); HairEditorPreferences.NoteInteraction(); }
            rebuildRequested = true; syncedKey = null;
            rootVisualElement.schedule.Execute(RefreshNow);
            return moved ? DragVisualMode.Move : DragVisualMode.Rejected;
        }

        private bool TryGetDropTarget(HairCardStage stage, HairGroomNode source, HandleDragAndDropArgs args,
            out HairGroomNode target, out bool after)
        {
            target = null; after = false;
            if (args.dropPosition != DragAndDropPosition.BetweenItems) return false;
            // Siblings of the dragged item, in tree order. Drops between items of another parent are refused.
            if (!keysById.TryGetValue(args.parentId, out string parentKey) || parentKey != source.ParentKey) return false;
            var siblings = new List<HairGroomNode>();
            foreach (var node in stage.Nodes) if (node.ParentKey == parentKey && shownKeys.Contains(node.Key)) siblings.Add(node);
            if (siblings.Count == 0) return false;
            int index = Mathf.Clamp(args.childIndex, 0, siblings.Count);
            if (index < siblings.Count) target = siblings[index];
            else { target = siblings[siblings.Count - 1]; after = true; }
            if (target.Key == source.Key)
            {
                // Dropping just before itself: aim at the neighbor so the move is a no-op, not refused.
                return false;
            }
            return HairGroomNodes.CanDrop(source, target);
        }

        // ---- Keyboard --------------------------------------------------------------------

        /// <summary>Hierarchy-style navigation over the visible rows (the tree view provides the
        /// same keys natively; this model-level form keeps the rules testable).</summary>
        internal bool HandleNodeNavigation(HairCardStage stage, IReadOnlyList<HairGroomNode> visible, KeyCode key)
        {
            if (stage == null || visible.Count == 0) return false;
            var selected = stage.SelectedNode;
            int index = 0;
            for (int i = 0; i < visible.Count; i++) if (visible[i].Key == selected?.Key) { index = i; break; }
            if (key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.Home || key == KeyCode.End)
            {
                int next = key == KeyCode.Home ? 0 : key == KeyCode.End ? visible.Count - 1 : Mathf.Clamp(index + (key == KeyCode.UpArrow ? -1 : 1), 0, visible.Count - 1);
                stage.SelectNode(visible[next].Key); return true;
            }
            if (selected == null || (key != KeyCode.LeftArrow && key != KeyCode.RightArrow)) return false;
            bool expanded = IsExpanded(selected);
            if (key == KeyCode.LeftArrow)
            {
                if (selected.HasChildren && expanded) SetExpandedPreference(selected, false);
                else if (selected.ParentKey != null) stage.SelectNode(selected.ParentKey);
            }
            else if (selected.HasChildren)
            {
                if (!expanded) SetExpandedPreference(selected, true);
                else if (index + 1 < visible.Count && visible[index + 1].ParentKey == selected.Key) stage.SelectNode(visible[index + 1].Key);
            }
            rebuildRequested = true;
            return true;
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.F)
            { searchField?.Focus(); evt.StopPropagation(); }
        }

        private void OnTreeKeyDown(KeyDownEvent evt)
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            HairGroomNode node = stage?.SelectedNode;
            if (node == null) return;
            if (evt.keyCode == KeyCode.Delete || (evt.keyCode == KeyCode.Backspace && evt.commandKey))
            {
                if (HairGroomNodeCommands.CanRemoveNode(stage, node)) HairGroomNodeCommands.RemoveNode(stage, node);
                evt.StopPropagation();
            }
            else if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.D)
            {
                HairGroomNodeCommands.DuplicateNode(stage, node);
                evt.StopPropagation();
            }
            else if (evt.altKey && (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow))
            {
                HairGroomNodeCommands.MoveSelected(stage, node, evt.keyCode == KeyCode.UpArrow ? -1 : 1);
                evt.StopPropagation();
            }
            else return;
            HairEditorPreferences.NoteInteraction();
            rebuildRequested = true; syncedKey = null;
        }
    }
}
