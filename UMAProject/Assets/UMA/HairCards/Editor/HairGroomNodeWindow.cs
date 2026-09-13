using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairGroomNodeWindow : EditorWindow
    {
        [SerializeField] private Vector2 nodeScroll;
        [SerializeField] private string nodeSearch = string.Empty;
        [SerializeField] private List<string> collapsedNodeKeys = new List<string>();
        [SerializeField] private List<string> expandedOptionalMapKeys = new List<string>();
        [SerializeField] private bool initialPlacementDone;
        private HairGroomAsset preferenceGroom;
        private string preferenceContext, lastSelectedKey, dragCandidate;
        private double nextSave;
        private Vector2 dragStart;
        private const string DragKey = "UMA.HairCards.NodeDrag";
        private sealed class NodeDrag { internal HairGroomAsset Groom; internal string Key; }

        [MenuItem("UMA/Hair Cards/Hair Nodes", priority = 211)]
        public static void Open()
        {
            var window = GetWindow<HairGroomNodeWindow>();
            window.titleContent = new GUIContent("Hair Nodes");
            window.minSize = new Vector2(320f, 360f);
            if (!window.initialPlacementDone) { window.position = new Rect(60f, 80f, 340f, 700f); window.initialPlacementDone = true; }
            window.Show();
        }

        internal static void RepaintOpenWindows()
        { foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomNodeWindow>()) window.Repaint(); }

        internal static void SaveOpenPreferences()
        {
            if (HairEditorPreferences.Suspended) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomNodeWindow>())
                HairEditorPreferences.instance.Remember(window, "nodes", window.preferenceContext);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Hair Nodes");
            minSize = new Vector2(320f, 360f);
            if (HairEditorPreferences.Suspended) return;
            RestoreContext();
            EditorApplication.update += SaveIdle;
            Undo.undoRedoPerformed += Repaint;
        }
        internal static void ResetOpenPreferences()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomNodeWindow>())
            { HairPreferenceCodec.Reset(window); window.lastSelectedKey = null; window.Repaint(); }
        }
        private void OnDisable()
        {
            EditorApplication.update -= SaveIdle;
            Undo.undoRedoPerformed -= Repaint;
            if (!HairEditorPreferences.Suspended) HairEditorPreferences.instance.Remember(this, "nodes", preferenceContext);
        }
        private void SaveIdle()
        {
            if (HairEditorPreferences.Suspended || EditorApplication.timeSinceStartup < nextSave || HairCardStage.ActiveStage?.IsEditing == true) return;
            nextSave = EditorApplication.timeSinceStartup + 2d;
            HairEditorPreferences.instance.Remember(this, "nodes", preferenceContext);
        }
        private void RestoreContext()
        {
            preferenceGroom = HairCardStage.ActiveStage?.Groom;
            preferenceContext = HairEditorPreferences.Context(preferenceGroom);
            collapsedNodeKeys.Clear(); expandedOptionalMapKeys.Clear(); nodeSearch = string.Empty; nodeScroll = Vector2.zero;
            HairEditorPreferences.instance.Restore(this, "nodes", preferenceContext);
            lastSelectedKey = null;
        }

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

        private void OnGUI()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage?.Groom != preferenceGroom && !HairEditorPreferences.Suspended)
            {
                HairEditorPreferences.instance.Remember(this, "nodes", preferenceContext);
                RestoreContext();
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Properties", EditorStyles.toolbarButton)) HairGroomWorkspace.OpenProperties();
                if (GUILayout.Button("Preview & Settings", EditorStyles.toolbarButton)) HairGroomPreviewWindow.Open();
                using (new EditorGUI.DisabledScope(stage == null))
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44f))) stage.SaveNow();
            }
            if (stage?.Groom == null)
            {
                EditorGUILayout.HelpBox("Open a Hair Groom asset or select a source character/mesh to begin. Hair Nodes, Hair Properties and Hair Preview & Settings can be docked independently.", MessageType.Info);
                if (GUILayout.Button("Open Selected Source", GUILayout.Height(30f))) HairCardMenu.OpenSelectedSource();
                return;
            }
            EditorGUILayout.LabelField(stage.Groom.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Select a node → edit Hair Properties. Grooming order: top to bottom.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Frame", EditorStyles.toolbarButton)) stage.FrameGroom();
                int issueCount = stage.ReleaseValidation?.issues.Count ?? 0;
                using (new EditorGUI.DisabledScope(issueCount == 0))
                    if (GUILayout.Button(new GUIContent("Issues (" + issueCount + ")", "Locate a validation setting or guide. Validate & Bake refreshes this report."), EditorStyles.toolbarButton))
                        ShowIssuesMenu(stage);
                if (GUILayout.Button("Help", EditorStyles.toolbarButton)) HairGroomWorkspace.OpenQuickStart();
                if (GUILayout.Button("Exit Stage", EditorStyles.toolbarButton))
                { UnityEditor.SceneManagement.StageUtility.GoBackToPreviousStage(); GUIUtility.ExitGUI(); }
            }
            DrawTreeActions(stage);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName("HairNodeSearch");
                nodeSearch = EditorGUILayout.TextField(nodeSearch, EditorStyles.toolbarSearchField);
                if (GUILayout.Button(new GUIContent("×", "Clear search"), GUILayout.Width(22f))) { nodeSearch = string.Empty; GUI.FocusControl(null); }
                if (GUILayout.Button("Expand", GUILayout.Width(56f)))
                { collapsedNodeKeys.Clear(); foreach (var item in stage.Nodes) if (item.Kind == HairGroomNodeKind.OptionalMaps && !expandedOptionalMapKeys.Contains(item.Key)) expandedOptionalMapKeys.Add(item.Key); }
                if (GUILayout.Button("Collapse", GUILayout.Width(62f)))
                {
                    collapsedNodeKeys.Clear(); expandedOptionalMapKeys.Clear();
                    foreach (var item in stage.Nodes) if (item.HasChildren) collapsedNodeKeys.Add(item.Key);
                    stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Source));
                }
            }
            HairGroomNode selected = stage.SelectedNode;
            bool selectionChanged = selected?.Key != lastSelectedKey;
            if (selectionChanged)
            {
                lastSelectedKey = selected?.Key;
                var parent = selected;
                while (parent?.ParentKey != null)
                {
                    parent = stage.FindNode(parent.ParentKey);
                    if (parent == null) break;
                    collapsedNodeKeys.Remove(parent.Key);
                    if (parent.Kind == HairGroomNodeKind.OptionalMaps && !expandedOptionalMapKeys.Contains(parent.Key)) expandedOptionalMapKeys.Add(parent.Key);
                }
            }
            var visible = Filter(stage.Nodes, nodeSearch, collapsedNodeKeys, expandedOptionalMapKeys);
            if (selectionChanged && selected != null)
            {
                if (!visible.Exists(item => item.Key == selected.Key))
                { nodeSearch = string.Empty; visible = Filter(stage.Nodes, nodeSearch, collapsedNodeKeys, expandedOptionalMapKeys); }
                int selectedIndex = visible.FindIndex(item => item.Key == selected.Key);
                if (selectedIndex >= 0) nodeScroll.y = Mathf.Max(0f, selectedIndex * 27f - Mathf.Max(60f, (position.height - 260f) * 0.5f));
            }
            Event keyboard = Event.current;
            if (keyboard.type == EventType.KeyDown)
            {
                if ((keyboard.control || keyboard.command) && keyboard.keyCode == KeyCode.F)
                { GUI.FocusControl("HairNodeSearch"); keyboard.Use(); }
                else if (GUI.GetNameOfFocusedControl() != "HairNodeSearch" && !EditorGUIUtility.editingTextField && HandleNodeNavigation(stage, visible, keyboard.keyCode))
                { keyboard.Use(); Repaint(); }
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(nodeScroll))
            {
                nodeScroll = scroll.scrollPosition;
                foreach (var node in visible) DrawNodeRow(stage, node, selected?.Key == node.Key);
                if (visible.Count == 0) EditorGUILayout.HelpBox("No matching nodes. Clear search to return to the complete groom.", MessageType.Info);
            }
            if (selected != null)
            {
                EditorGUILayout.LabelField(selected.Label, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(selected.Description, EditorStyles.wordWrappedMiniLabel);
                if (selected.CanReorder) EditorGUILayout.LabelField("Drag above/below a sibling, or use the tree's ↑ / ↓ buttons. Cross-pass drops are not allowed.", EditorStyles.wordWrappedMiniLabel);
            }
            HairGroomWorkspace.DrawStatus(stage);
            EditorGUILayout.LabelField(stage.SaveStatus + " · " + stage.PreviewStatus, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("↑ ↓ select · ← → collapse/expand · Ctrl/Cmd+F search", EditorStyles.wordWrappedMiniLabel);
        }

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
            bool expanded = selected.Kind == HairGroomNodeKind.OptionalMaps ? expandedOptionalMapKeys.Contains(selected.Key) : !collapsedNodeKeys.Contains(selected.Key);
            if (key == KeyCode.LeftArrow)
            {
                if (selected.HasChildren && expanded)
                { expandedOptionalMapKeys.Remove(selected.Key); if (!collapsedNodeKeys.Contains(selected.Key)) collapsedNodeKeys.Add(selected.Key); }
                else if (selected.ParentKey != null) stage.SelectNode(selected.ParentKey);
            }
            else if (selected.HasChildren)
            {
                if (!expanded)
                { collapsedNodeKeys.Remove(selected.Key); if (selected.Kind == HairGroomNodeKind.OptionalMaps) expandedOptionalMapKeys.Add(selected.Key); }
                else if (index + 1 < visible.Count && visible[index + 1].ParentKey == selected.Key) stage.SelectNode(visible[index + 1].Key);
            }
            return true;
        }

        private void DrawNodeRow(HairCardStage stage, HairGroomNode node, bool selected)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 25f);
            if (selected) EditorGUI.DrawRect(row, new Color(0.2f, 0.43f, 0.66f, 0.55f));
            float x = row.x + 6f + node.Depth * 14f;
            Rect foldout = new Rect(x, row.y + 4f, 14f, 18f);
            bool expanded = node.Kind == HairGroomNodeKind.OptionalMaps ? expandedOptionalMapKeys.Contains(node.Key) : !collapsedNodeKeys.Contains(node.Key);
            if (node.HasChildren)
            {
                bool next = EditorGUI.Foldout(foldout, !string.IsNullOrWhiteSpace(nodeSearch) || expanded, GUIContent.none);
                if (next != expanded && string.IsNullOrWhiteSpace(nodeSearch))
                {
                    if (node.Kind == HairGroomNodeKind.OptionalMaps)
                    { if (next) expandedOptionalMapKeys.Add(node.Key); else expandedOptionalMapKeys.Remove(node.Key); }
                    else { if (next) collapsedNodeKeys.Remove(node.Key); else collapsedNodeKeys.Add(node.Key); }
                    if (!next)
                    {
                        var parent = stage.SelectedNode;
                        while (parent?.ParentKey != null)
                        { if (parent.ParentKey == node.Key) { stage.SelectNode(node.Key); break; } parent = stage.FindNode(parent.ParentKey); }
                    }
                }
            }
            float controls = node.CanToggle ? 65f : 0f;
            Rect label = new Rect(x + 16f, row.y, Mathf.Max(10f, row.xMax - x - 18f - controls), row.height);
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && label.Contains(current.mousePosition))
            { dragCandidate = node.Key; dragStart = current.mousePosition; }
            if (current.type == EventType.MouseDrag && dragCandidate == node.Key && node.CanReorder && !node.Locked && (current.mousePosition - dragStart).sqrMagnitude > 20f)
            {
                DragAndDrop.PrepareStartDrag(); DragAndDrop.SetGenericData(DragKey, new NodeDrag { Groom = stage.Groom, Key = node.Key });
                DragAndDrop.StartDrag(node.Label); GUIUtility.hotControl = 0; dragCandidate = null; current.Use();
            }
            bool bypassed = stage.IsNodeTemporarilyBypassed(node);
            if (GUI.Button(label, new GUIContent((node.Locked ? "[Locked] " : "") +
                (bypassed ? "[bypassed] " : node.Layer != null && node.Layer == stage.ActiveLayer && stage.IsLayerEditing && node.Modifier == null ? "[editing] " : "") + node.Label,
                bypassed ? "Temporarily bypassed at the sculpt edit point. The saved Active toggle is unchanged." : node.Description),
                selected || node.Kind == HairGroomNodeKind.Group ? EditorStyles.boldLabel : EditorStyles.label))
            { GUI.FocusControl(null); EditorGUIUtility.editingTextField = false; stage.SelectNode(node.Key); if (Resources.FindObjectsOfTypeAll<HairGroomWorkspace>().Length == 0) HairGroomWorkspace.OpenProperties(); }
            if (node.CanToggle)
                using (new EditorGUI.DisabledScope(node.Locked))
                {
                    bool enabled = GUI.Toggle(new Rect(row.xMax - 62f, row.y + 2f, 60f, 21f), node.Enabled,
                        new GUIContent("Active", "Include this operation in evaluation. This is independent of selection."), "Button");
                    if (enabled != node.Enabled) HairGroomNodes.Toggle(stage, node, enabled);
                }
            if ((current.type == EventType.DragUpdated || current.type == EventType.DragPerform) && row.Contains(current.mousePosition) && DragAndDrop.GetGenericData(DragKey) is NodeDrag drag)
            {
                HairGroomNode source = drag.Groom == stage.Groom ? stage.FindNode(drag.Key) : null;
                bool allowed = source != null && source.CanReorder && !source.Locked && !node.Locked && source.Kind == node.Kind && source.ParentKey == node.ParentKey && source.Key != node.Key;
                if (allowed && source.Modifier == null && source.Layer.afterGroupOperations != node.Layer.afterGroupOperations) allowed = false;
                DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                bool after = current.mousePosition.y >= row.center.y;
                if (allowed) EditorGUI.DrawRect(new Rect(row.x, after ? row.yMax - 2f : row.y, row.width, 2f), Color.cyan);
                if (current.type == EventType.DragPerform && allowed)
                { DragAndDrop.AcceptDrag(); if (HairGroomNodes.Move(stage, source, node, after)) stage.SelectNode(source.Key); DragAndDrop.SetGenericData(DragKey, null); }
                current.Use(); Repaint();
            }
        }
    }
}
