using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal static partial class TexturePaintWorkspaceLayout
    {
        internal const string SavedLayoutPath = "UserSettings/UMA/OverlayPainter/Layout.json";
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static bool HasSavedLayout => File.Exists(SavedLayoutPath);

        [Serializable] internal sealed class SavedLayout
        {
            public int version = 1;
            public List<SavedWindow> windows = new();
        }
        [Serializable] internal sealed class SavedWindow
        {
            public Rect position;
            public LayoutNode view;
        }
        [Serializable] internal sealed class LayoutNode
        {
            public bool vertical;
            public bool tabs;
            public float size = 1;
            public int selected;
            public string className;
            public List<LayoutNode> children = new();
        }

        // A flat file avoids Unity's inline-serialization depth limit for recursive types.
        [Serializable] private sealed class LayoutFile
        {
            public int version = 1;
            public List<WindowRecord> windows = new();
            public List<NodeRecord> nodes = new();
        }
        [Serializable] private sealed class WindowRecord { public Rect position; public int root; }
        [Serializable] private sealed class NodeRecord
        {
            public bool vertical, tabs;
            public float size;
            public int selected;
            public string className;
            public List<int> children = new();
        }

        [MenuItem("Window/UMA/Save Overlay Painter Layout", false, 2026)]
        internal static void SaveLayout()
        {
            try
            {
                SaveLayoutFile(SavedLayoutPath, CaptureLayout());
                EditorWindow.focusedWindow?.ShowNotification(new GUIContent("Overlay Painter layout saved"));
            }
            catch (Exception exception)
            { EditorUtility.DisplayDialog("Save Overlay Painter Layout", exception.Message, "OK"); }
        }

        [MenuItem("Window/UMA/Restore Overlay Painter Layout", false, 2027)]
        internal static void RestoreLayout()
        {
            // Let the menu/OnGUI event finish before replacing its host windows.
            EditorApplication.delayCall += () =>
            {
                if (TexturePaintStageWindow.ActiveStage == null) return;
                if (!TryRestoreSavedLayout(out string error))
                    EditorUtility.DisplayDialog("Restore Overlay Painter Layout", error, "OK");
            };
        }

        [MenuItem("Window/UMA/Save Overlay Painter Layout", true)]
        private static bool CanSaveLayout() => TexturePaintStageWindow.ActiveStage != null;
        [MenuItem("Window/UMA/Restore Overlay Painter Layout", true)]
        private static bool CanRestoreLayout() => CanSaveLayout() && HasSavedLayout;

        internal static bool IsPainterContainerId(string id) => id == CompactWindowId ||
            (id != null && id.StartsWith(CompactWindowId + ".Saved.", StringComparison.Ordinal));

        private static object Member(object value, string name)
        {
            if (value == null) return null;
            Type type = value.GetType();
            return type.GetProperty(name, InstanceMembers)?.GetValue(value) ??
                type.GetField(name, InstanceMembers)?.GetValue(value);
        }

        private static bool PainterPane(EditorWindow pane) => pane is TexturePaintDockWindow ||
            pane is TexturePaintBrushWindow || pane is TexturePaintPropertiesWindow || pane is TexturePaintUVWindow ||
            pane is TexturePaintMaterialWindow;

        internal static SavedLayout CaptureLayout(IEnumerable<EditorWindow> windows = null)
        {
            var containers = new HashSet<UnityEngine.Object>();
            foreach (EditorWindow pane in windows ?? Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (PainterPane(pane) || ReferenceEquals(pane, compactSceneView))
                    if (Member(Member(pane, "m_Parent"), "window") is UnityEngine.Object container)
                        containers.Add(container);
            var result = new SavedLayout();
            foreach (var container in containers)
            {
                bool dedicated = IsPainterContainerId(Member(container, "windowID") as string);
                LayoutNode view = CaptureView(Member(container, "rootView"), pane =>
                    PainterPane(pane) || (pane is SceneView && (dedicated || ReferenceEquals(pane, compactSceneView))));
                if (view == null) continue;
                view.size = 1;
                result.windows.Add(new SavedWindow { position = (Rect)Member(container, "position"), view = view });
            }
            ValidateLayout(result);
            return result;
        }

        // Only window placement is captured. Document, layer, brush and selection state stay on the stage.
        internal static LayoutNode CaptureView(object view, Func<EditorWindow, bool> include)
        {
            if (view == null) return null;
            if (Member(view, "m_Panes") is IList panes)
            {
                var node = new LayoutNode { tabs = true };
                int selected = (int)(Member(view, "selected") ?? 0);
                for (int i = 0; i < panes.Count; i++)
                    if (panes[i] is EditorWindow pane && include(pane))
                    {
                        if (i == selected) node.selected = node.children.Count;
                        node.children.Add(new LayoutNode { className = pane.GetType().Name });
                    }
                return node.children.Count == 0 ? null : node;
            }
            if (Member(view, "actualView") is EditorWindow single && include(single))
                return new LayoutNode { tabs = true, children = new() { new LayoutNode { className = single.GetType().Name } } };
            if (Member(view, "children") is not IEnumerable children) return null;
            var split = new LayoutNode { vertical = (bool)(Member(view, "vertical") ?? false) };
            float total = 0;
            foreach (object child in children)
            {
                LayoutNode node = CaptureView(child, include);
                if (node == null) continue;
                Rect rect = (Rect)Member(child, "position");
                node.size = Mathf.Max(1, split.vertical ? rect.height : rect.width);
                total += node.size;
                split.children.Add(node);
            }
            if (split.children.Count == 0) return null;
            if (split.children.Count == 1) return split.children[0];
            foreach (var child in split.children) child.size /= total;
            return split;
        }

        internal static void ValidateLayout(SavedLayout layout)
        {
            if (layout == null || layout.version != 1 || layout.windows == null || layout.windows.Count == 0 || layout.windows.Count > 8)
                throw new InvalidDataException("The saved Overlay Painter layout is empty or uses an unsupported format.");
            var classes = new HashSet<string>();
            foreach (var window in layout.windows)
            {
                if (window == null || !float.IsFinite(window.position.x) || !float.IsFinite(window.position.y) ||
                    !float.IsFinite(window.position.width) || !float.IsFinite(window.position.height) ||
                    window.position.width <= 0 || window.position.height <= 0)
                    throw new InvalidDataException("The saved layout contains an invalid window position.");
                ValidateNode(window.view, 0);
            }
            if (!classes.Contains(nameof(TexturePaintDockWindow)))
                throw new InvalidDataException("Open the Overlay Painter Layers window before saving its layout.");
            void ValidateNode(LayoutNode node, int depth)
            {
                if (node == null || depth > 12 || !float.IsFinite(node.size) || node.size <= 0)
                    throw new InvalidDataException("The saved layout contains an invalid split.");
                if (!string.IsNullOrEmpty(node.className))
                {
                    if (!new[] { nameof(TexturePaintDockWindow), nameof(TexturePaintBrushWindow), nameof(TexturePaintPropertiesWindow),
                        nameof(TexturePaintUVWindow), nameof(TexturePaintMaterialWindow), nameof(SceneView) }.Contains(node.className) || !classes.Add(node.className))
                        throw new InvalidDataException("The saved layout contains an unsupported or duplicate window.");
                    return;
                }
                if (node.children == null || node.children.Count == 0 || node.children.Count > 8)
                    throw new InvalidDataException("The saved layout contains an empty panel group.");
                if (node.tabs && (node.selected < 0 || node.selected >= node.children.Count ||
                    node.children.Any(c => c == null || string.IsNullOrEmpty(c.className))))
                    throw new InvalidDataException("The saved layout contains an invalid tab group.");
                foreach (var child in node.children) ValidateNode(child, depth + 1);
            }
        }

        internal static void SaveLayoutFile(string path, SavedLayout layout)
        {
            ValidateLayout(layout);
            var file = new LayoutFile();
            foreach (var window in layout.windows)
                file.windows.Add(new WindowRecord { position = window.position, root = Flatten(window.view) });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string pending = path + ".tmp";
            try
            {
                File.WriteAllText(pending, JsonUtility.ToJson(file, true));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            int Flatten(LayoutNode node)
            {
                int index = file.nodes.Count;
                var record = new NodeRecord { vertical = node.vertical, tabs = node.tabs, size = node.size,
                    selected = node.selected, className = node.className };
                file.nodes.Add(record);
                foreach (var child in node.children) record.children.Add(Flatten(child));
                return index;
            }
        }

        internal static SavedLayout ReadLayoutFile(string path)
        {
            var file = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(path));
            if (file == null || file.version != 1 || file.windows == null || file.windows.Count > 8 ||
                file.nodes == null || file.nodes.Count > 128)
                throw new InvalidDataException("The saved Overlay Painter layout is invalid or uses an unsupported format.");
            var result = new SavedLayout();
            var visited = new HashSet<int>();
            foreach (var window in file.windows)
            {
                if (window == null) throw new InvalidDataException("The saved layout contains an invalid window.");
                result.windows.Add(new SavedWindow { position = window.position, view = Expand(window.root, 0) });
            }
            ValidateLayout(result);
            return result;
            LayoutNode Expand(int index, int depth)
            {
                if (index < 0 || index >= file.nodes.Count || depth > 12 || !visited.Add(index))
                    throw new InvalidDataException("The saved layout contains an invalid or cyclic split.");
                NodeRecord record = file.nodes[index];
                if (record == null || record.children == null) throw new InvalidDataException("The saved layout contains an invalid node.");
                var node = new LayoutNode { vertical = record.vertical, tabs = record.tabs, size = record.size,
                    selected = record.selected, className = record.className };
                foreach (int child in record.children) node.children.Add(Expand(child, depth + 1));
                return node;
            }
        }

        internal static string DynamicDefinition(LayoutNode view)
        {
            var text = new StringBuilder("{\"restore_saved_layout\":false,\"restore_layout_dimension\":true,\"min_width\":300,\"min_height\":320,\"max_width\":8192,\"max_height\":8192,\"view\":");
            Write(view); text.Append('}'); return text.ToString();
            void Write(LayoutNode node)
            {
                text.Append("{\"size\":").Append(node.size.ToString("R", CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(node.className))
                    text.Append(",\"class_name\":\"").Append(node.className).Append('"');
                else
                {
                    text.Append(node.tabs ? ",\"tabs\":true" : node.vertical ? ",\"vertical\":true" : ",\"horizontal\":true");
                    text.Append(",\"children\":[");
                    for (int i = 0; i < node.children.Count; i++) { if (i > 0) text.Append(','); Write(node.children[i]); }
                    text.Append(']');
                }
                text.Append('}');
            }
        }

        private static bool TryRestoreSavedLayout(out string error)
        {
            error = null;
            bool replacingWindows = false;
            try
            {
                var saved = ReadLayoutFile(SavedLayoutPath);
                ValidateLayout(saved); // Validate everything before closing any windows.
                MethodInfo show = ResolveDynamicLayoutMethod() ?? throw new NotSupportedException("Unity's workspace layout API is unavailable.");
                var paths = new List<string>();
                for (int i = 0; i < saved.windows.Count; i++)
                {
                    string path = Path.GetFullPath($"Library/UMA/OverlayPainter/SavedWindow{i}.sjson");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, DynamicDefinition(saved.windows[i].view)); paths.Add(path);
                }
                TexturePaintDockWindow.LayoutTransitionInProgress = true;
                replacingWindows = true;
                compactSceneView = null; compactWorkspaceActive = false;
                CloseCompactContainer();
                TexturePaintDockWindow.CloseOpenWindowsForLayoutChange();
                TexturePaintUVWindow.CloseOpenWindowsForLayoutChange();
                TexturePaintBrushWindow.CloseOpenWindowsForLayoutChange();
                TexturePaintPropertiesWindow.CloseOpenWindowsForLayoutChange();
                TexturePaintMaterialWindow.CloseOpenWindowsForLayoutChange();
                for (int i = 0; i < saved.windows.Count; i++)
                {
                    string id = CompactWindowId + ".Saved." + i;
                    Rect rect = saved.windows[i].position;
                    EditorPrefs.SetFloat(id + "x", rect.x); EditorPrefs.SetFloat(id + "y", rect.y);
                    EditorPrefs.SetFloat(id + "w", rect.width); EditorPrefs.SetFloat(id + "h", rect.height);
                    EditorPrefs.SetBool(id + "z", false);
                    var container = show.Invoke(null, new object[] { id, paths[i] }) as UnityEngine.Object;
                    if (container == null) throw new InvalidOperationException("Unity could not restore an Overlay Painter window.");
                    SetContainerTitle(container, "Overlay Painter");
                    var scene = SelectDefaultTabs(container);
                    if (scene != null) compactSceneView = scene;
                    RestoreSelectedTabs(Member(container, "rootView"), saved.windows[i].view);
                }
                compactWorkspaceActive = compactSceneView != null;
                TexturePaintSceneOverlayVisibility.RefreshAll();
                SceneView restoredScene = compactSceneView;
                EditorApplication.delayCall += () =>
                {
                    if (restoredScene != null)
                        TexturePaintStageWindow.ActiveStage?.FrameRestoredLayoutScene(restoredScene);
                };
                SceneView.RepaintAll();
                return true;
            }
            catch (Exception exception)
            {
                error = exception is TargetInvocationException invocation ? invocation.InnerException?.Message ?? invocation.Message : exception.Message;
                if (replacingWindows) OpenSeparateWindows();
                return false;
            }
            finally { TexturePaintDockWindow.LayoutTransitionInProgress = false; }
        }

        internal static void RestoreSelectedTabs(object root, LayoutNode layout)
        {
            // Match by pane types rather than native wrapper/split nesting.
            var selection = new Dictionary<string, string>();
            Collect(layout);
            Apply(root);
            void Collect(LayoutNode node)
            {
                if (node.tabs) selection[node.children[0].className] = node.children[node.selected].className;
                foreach (var child in node.children) Collect(child);
            }
            void Apply(object view)
            {
                if (Member(view, "m_Panes") is IList panes && panes.Count > 0 && panes[0] is EditorWindow first &&
                    selection.TryGetValue(first.GetType().Name, out string selected))
                    for (int i = 0; i < panes.Count; i++)
                        if (panes[i].GetType().Name == selected)
                            view.GetType().GetProperty("selected", InstanceMembers)?.SetValue(view, i);
                if (Member(view, "children") is IEnumerable children) foreach (var child in children) Apply(child);
            }
        }
    }
}
