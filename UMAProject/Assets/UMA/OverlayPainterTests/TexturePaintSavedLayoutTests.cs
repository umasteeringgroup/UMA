#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Layout = UMA.TexturePaint.Editor.TexturePaintWorkspaceLayout;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintSavedLayoutTests
    {
        private string folder;
        [SetUp] public void Setup() { folder = Path.Combine("Temp", "PainterLayoutTest_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
        [TearDown] public void Cleanup() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        [Test]
        public void SavedLayoutRoundTripsAndReplacesOnlyOneSnapshot()
        {
            string path = Path.Combine(folder, "Layout.json");
            var saved = new Layout.SavedLayout();
            saved.windows.Add(new Layout.SavedWindow { position = new Rect(90, 100, 1200, 800), view = new Layout.LayoutNode
            { tabs = true, selected = 1, children = new() { new() { className = nameof(TexturePaintDockWindow) }, new() { className = nameof(TexturePaintBrushWindow) } } } });
            Layout.SaveLayoutFile(path, saved);
            var read = Layout.ReadLayoutFile(path);
            Assert.That(read.windows[0].view.selected, Is.EqualTo(1));
            Assert.That(read.windows[0].position, Is.EqualTo(saved.windows[0].position));
            saved.windows[0].position.width = 1500;
            Layout.SaveLayoutFile(path, saved);
            Assert.That(Layout.ReadLayoutFile(path).windows[0].position.width, Is.EqualTo(1500));
            Assert.That(Directory.GetFiles(folder).Length, Is.EqualTo(1));
            string good = File.ReadAllText(path);
            saved.windows[0].view.children.Add(new() { className = nameof(TexturePaintDockWindow) });
            Assert.Throws<InvalidDataException>(() => Layout.SaveLayoutFile(path, saved));
            Assert.That(File.ReadAllText(path), Is.EqualTo(good), "Invalid replacement must preserve the saved layout.");
        }

        [Test]
        public void DeepLayoutDoesNotUseUnitysRecursiveSerializationAndRejectsCycles()
        {
            var node = new Layout.LayoutNode { tabs = true, children = new() { new() { className = nameof(TexturePaintDockWindow) } } };
            for (int i = 0; i < 10; i++) node = new Layout.LayoutNode { vertical = true, children = new() { node } };
            var layout = new Layout.SavedLayout();
            layout.windows.Add(new Layout.SavedWindow { position = new Rect(0, 0, 1000, 800), view = node });
            string path = Path.Combine(folder, "Deep.json");
            Layout.SaveLayoutFile(path, layout);
            var restored = Layout.ReadLayoutFile(path).windows[0].view;
            for (int i = 0; i < 10; i++) restored = restored.children.Single();
            Assert.That(restored.children.Single().className, Is.EqualTo(nameof(TexturePaintDockWindow)));
            File.WriteAllText(path, "{\"version\":1,\"windows\":[{\"root\":0}],\"nodes\":[{\"size\":1,\"children\":[0]}]}");
            Assert.Throws<InvalidDataException>(() => Layout.ReadLayoutFile(path));
        }

        [Test]
        public void NativeStackedLayoutCapturesAndRestoresSplitsAndSelectedTabs()
        {
            var assembly = typeof(EditorWindow).Assembly;
            var show = assembly.GetType("UnityEditor.WindowLayout").GetMethod("ShowWindowWithDynamicLayout", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            UnityEngine.Object first = null, second = null;
            string firstId = "UMA.LayoutTest." + Guid.NewGuid().ToString("N"), secondId = firstId + ".RoundTrip";
            TexturePaintDockWindow.LayoutTransitionInProgress = true;
            try
            {
                first = Open(firstId, Layout.CompactLayoutDefinition);
                object root = Root(first);
                var owned = Resources.FindObjectsOfTypeAll<EditorWindow>().Where(pane =>
                {
                    var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pane);
                    return parent != null && ReferenceEquals(parent.GetType().GetProperty("window").GetValue(parent), first);
                }).ToArray();
                var captured = Layout.CaptureLayout(owned);
                Assert.That(captured.windows.Count, Is.EqualTo(1), "Capture the actual native container through each pane's parent.");
                var snapshot = Layout.CaptureView(root, _ => true);
                Assert.That(snapshot.children[0].vertical, Is.True);
                Assert.That(snapshot.children[0].children[0].children.Select(n => n.className),
                    Is.EqualTo(new[] { nameof(TexturePaintDockWindow), nameof(TexturePaintBrushWindow) }));
                Assert.That(snapshot.children[0].children[1].children.Single().className, Is.EqualTo(nameof(TexturePaintPropertiesWindow)));
                snapshot.children[0].children[0].selected = 1;
                snapshot.children[0].children[0].size = .4f;
                snapshot.children[0].children[1].size = .6f;
                second = Open(secondId, Layout.DynamicDefinition(snapshot));
                Layout.RestoreSelectedTabs(Root(second), snapshot);
                var restored = Layout.CaptureView(Root(second), _ => true);
                Assert.That(restored.children[0].children[0].selected, Is.EqualTo(1));
                Assert.That(restored.children[0].children[0].size, Is.EqualTo(.4f).Within(.06f));
                Assert.That(restored.children[0].children[1].children.Single().className, Is.EqualTo(nameof(TexturePaintPropertiesWindow)));
                var data = new Layout.SavedLayout(); data.windows.Add(new Layout.SavedWindow { position = new Rect(10, 20, 1400, 1000), view = restored });
                Layout.ValidateLayout(data);
            }
            finally
            {
                Close(second); Close(first);
                TexturePaintDockWindow.LayoutTransitionInProgress = false;
                foreach (string id in new[] { firstId, secondId }) foreach (string suffix in new[] { "x", "y", "w", "h", "z" }) EditorPrefs.DeleteKey(id + suffix);
            }
            UnityEngine.Object Open(string id, string definition)
            {
                string path = Path.GetFullPath(Path.Combine(folder, id + ".sjson")); File.WriteAllText(path, definition);
                EditorPrefs.SetFloat(id + "x", 100); EditorPrefs.SetFloat(id + "y", 100);
                EditorPrefs.SetFloat(id + "w", 1400); EditorPrefs.SetFloat(id + "h", 1000);
                return (UnityEngine.Object)show.Invoke(null, new object[] { id, path });
            }
        }
        private static object Root(UnityEngine.Object window) => window.GetType().GetProperty("rootView").GetValue(window);
        private static void Close(UnityEngine.Object window)
        {
            if (window != null) window.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(window, null);
        }
    }
}
#endif
