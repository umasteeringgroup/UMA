#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Layout = UMA.TexturePaint.Editor.TexturePaintWorkspaceLayout;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintSavedLayoutTests
    {
        private string folder;
        [SetUp] public void Setup() { folder = Path.Combine("Temp", "PainterLayoutTest_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
        [TearDown] public void Cleanup() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        [Test]
        public void ClosedPreviewCreationTargetIsClearedBeforeDeferredWork()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var get = typeof(EditorSceneManager).GetMethod("GetTargetSceneForNewGameObjects", flags);
            var set = typeof(EditorSceneManager).GetMethod("SetTargetSceneForNewGameObjects", flags,
                null, new[] { typeof(Scene) }, null);
            var clear = typeof(EditorSceneManager).GetMethod("ClearTargetSceneForNewGameObjects", flags);
            Scene previous = (Scene)get.Invoke(null, null);
            Scene previousActive = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                set.Invoke(null, new object[] { preview });
                EditorSceneManager.ClosePreviewScene(preview);
                TexturePaintStageWindow.ClearInvalidEditorCreationTarget();
                Scene target = (Scene)get.Invoke(null, null);
                Assert.That(target, Is.Not.EqualTo(preview), "No destroyed preview may remain the native creation target.");
            }
            finally
            {
                clear.Invoke(null, null);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (previous != previousActive && previous.IsValid() && previous.isLoaded)
                    set.Invoke(null, new object[] { previous });
            }
        }

        [Test]
        public void ReloadClosesDedicatedPainterContainerButKeepsOrdinarySceneView()
        {
            var show = typeof(EditorWindow).Assembly.GetType("UnityEditor.WindowLayout")
                .GetMethod("ShowWindowWithDynamicLayout", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            string id = Layout.CompactWindowId + ".Saved.ReloadTest." + Guid.NewGuid().ToString("N");
            string path = Path.GetFullPath(Path.Combine(folder, "Reload.sjson"));
            File.WriteAllText(path, Layout.CompactLayoutDefinition);
            UnityEngine.Object container = null;
            SceneView ordinaryScene = ScriptableObject.CreateInstance<SceneView>();
            ordinaryScene.Show();
            try
            {
                container = (UnityEngine.Object)show.Invoke(null, new object[] { id, path });
                Assert.That(container != null, Is.True);
                Assert.That(Resources.FindObjectsOfTypeAll<TexturePaintDockWindow>(), Is.Not.Empty);

                TexturePaintStageWindow.ExitIfActive();
                TexturePaintStageWindow.ExitIfActive(); // Compilation and before-reload both call it.

                Assert.That(container == null, Is.True, "The painter's Scene view must close with its native container.");
                Assert.That(ordinaryScene != null, Is.True, "An ordinary Scene view must remain open.");
                Assert.That(Resources.FindObjectsOfTypeAll<TexturePaintDockWindow>(), Is.Empty);
                Assert.That(Resources.FindObjectsOfTypeAll<TexturePaintUVWindow>(), Is.Empty);
                Assert.That(Resources.FindObjectsOfTypeAll<TexturePaintMaterialWindow>(), Is.Empty);
                Assert.That(TexturePaintDockWindow.LayoutTransitionInProgress, Is.False);
            }
            finally
            {
                Close(container);
                if (ordinaryScene != null) ordinaryScene.Close();
                foreach (string suffix in new[] { "x", "y", "w", "h", "z" }) EditorPrefs.DeleteKey(id + suffix);
            }
        }

        [Test]
        public void AfterReloadClosesOrphanedSeparatePainterWindows()
        {
            EditorWindow[] panes = {
                ScriptableObject.CreateInstance<TexturePaintDockWindow>(),
                ScriptableObject.CreateInstance<TexturePaintUVWindow>(),
                ScriptableObject.CreateInstance<TexturePaintBrushWindow>(),
                ScriptableObject.CreateInstance<TexturePaintPropertiesWindow>(),
                ScriptableObject.CreateInstance<TexturePaintMaterialWindow>(),
                ScriptableObject.CreateInstance<TexturePaintSpriteMaterialsWindow>(),
                ScriptableObject.CreateInstance<OverlayPainterSpriteSetPickerWindow>(),
                ScriptableObject.CreateInstance<OverlayPainterOverlayPickerWindow>()
            };
            try
            {
                foreach (EditorWindow pane in panes) pane.Show();
                typeof(TexturePaintStageWindow).GetMethod("CloseOrphanedWorkspaceAfterReload",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Assert.That(panes.All(pane => pane == null), Is.True,
                    "Reloaded panes must not stay open without their runtime painting controller.");
                Assert.That(TexturePaintDockWindow.LayoutTransitionInProgress, Is.False);
            }
            finally
            {
                TexturePaintDockWindow.LayoutTransitionInProgress = true;
                foreach (EditorWindow pane in panes) if (pane != null) pane.Close();
                TexturePaintDockWindow.LayoutTransitionInProgress = false;
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void PickerCloseDefersSelectionAndShutdownCancelsPendingCallback(bool shutdown)
        {
            var window = ScriptableObject.CreateInstance<OverlayPainterOverlayPickerWindow>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            int calls = 0;
            bool closedBeforeCallback = false;
            try
            {
                typeof(OverlayPainterOverlayPickerWindow).GetField("onAdd", flags).SetValue(window,
                    new Action<OverlayDataAsset>(selected =>
                    {
                        calls++;
                        closedBeforeCallback = window == null;
                        Assert.That(selected, Is.SameAs(overlay));
                    }));
                typeof(OverlayPainterOverlayPickerWindow).GetField("overlay", flags).SetValue(window, overlay);
                window.ShowUtility();
                typeof(OverlayPainterOverlayPickerWindow).GetMethod("AddSelectedOverlay", flags).Invoke(window, null);
                Assert.That(window != null, Is.True, "Selection must not destroy its window during GUI dispatch.");
                Assert.That(calls, Is.Zero);
                if (shutdown)
                {
                    OverlayPainterSessionPickerWindow.CloseAll();
                    Assert.That(calls, Is.Zero, "Shutdown must not invoke a callback into a disposed painting session.");
                }
                else
                {
                    typeof(OverlayPainterSessionPickerWindow).GetMethod("CompleteClose", flags).Invoke(window, null);
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(closedBeforeCallback, Is.True);
                }
                Assert.That(window == null, Is.True);
            }
            finally
            {
                if (window != null) window.Close();
                UnityEngine.Object.DestroyImmediate(overlay);
            }
        }

        [Test]
        public void DetachedSpritePickerClosesWithoutRefreshingAssets()
        {
            var window = ScriptableObject.CreateInstance<OverlayPainterSpriteSetPickerWindow>();
            try
            {
                window.ShowUtility();
                typeof(OverlayPainterSessionPickerWindow).GetMethod("CloseIfDetached",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                Assert.That(window == null, Is.True, "A reload cannot restore the session callback.");
            }
            finally { if (window != null) window.Close(); }
        }

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
        public void SavedLayoutReplacementWaitsForTemporaryReader()
        {
            string path = Path.Combine(folder, "Layout.json");
            var saved = SinglePanelLayout();
            Layout.SaveLayoutFile(path, saved);
            saved.windows[0].position.width = 1500;
            using var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(100);
                reader.Dispose();
            }) { IsBackground = true };
            release.Start();
            try { Layout.SaveLayoutFile(path, saved); }
            finally { release.Join(); }
            Assert.That(Layout.ReadLayoutFile(path).windows[0].position.width, Is.EqualTo(1500));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1), "No staging files should remain.");
        }

        [Test]
        public void SavedLayoutReplacementFailurePreservesPreviousSnapshot()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("Windows file sharing prevents replacing a file held open without delete sharing.");
            string path = Path.Combine(folder, "Layout.json");
            var saved = SinglePanelLayout();
            Layout.SaveLayoutFile(path, saved);
            byte[] original = File.ReadAllBytes(path);
            saved.windows[0].position.width = 1500;
            using (var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => Layout.SaveLayoutFile(path, saved));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1));
            Layout.SaveLayoutFile(path, saved);
            Assert.That(Layout.ReadLayoutFile(path).windows[0].position.width, Is.EqualTo(1500));
        }

        private static Layout.SavedLayout SinglePanelLayout()
        {
            var layout = new Layout.SavedLayout();
            layout.windows.Add(new Layout.SavedWindow
            {
                position = new Rect(90, 100, 1200, 800),
                view = new Layout.LayoutNode { className = nameof(TexturePaintDockWindow) }
            });
            return layout;
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
                Assert.That(snapshot.children[0].children[1].children.Select(n => n.className), Is.EqualTo(new[] { nameof(TexturePaintPropertiesWindow), nameof(TexturePaintMaterialWindow) }));
                Assert.That(snapshot.children[1].children.Single().className, Is.EqualTo(nameof(TexturePaintSpriteMaterialsWindow)));
                snapshot.children[0].children[1].selected = 1;
                snapshot.children[0].children[0].selected = 1;
                snapshot.children[0].children[0].size = .4f;
                snapshot.children[0].children[1].size = .6f;
                second = Open(secondId, Layout.DynamicDefinition(snapshot));
                Layout.RestoreSelectedTabs(Root(second), snapshot);
                var restored = Layout.CaptureView(Root(second), _ => true);
                Assert.That(restored.children[0].children[0].selected, Is.EqualTo(1));
                Assert.That(restored.children[0].children[1].selected, Is.EqualTo(1));
                Assert.That(restored.children[0].children[0].size, Is.EqualTo(.4f).Within(.06f));
                Assert.That(restored.children[0].children[1].children.Select(n => n.className), Is.EqualTo(new[] { nameof(TexturePaintPropertiesWindow), nameof(TexturePaintMaterialWindow) }));
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
