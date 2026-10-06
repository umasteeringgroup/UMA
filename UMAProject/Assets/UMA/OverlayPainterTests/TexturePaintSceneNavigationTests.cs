#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintSceneNavigationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private TexturePaintStageWindow stage;

        [SetUp]
        public void SetUp()
        {
            stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            Invoke("ClearLightweightHistory");
            Object.DestroyImmediate(stage);
        }

        [TestCase("paintHotControl")]
        [TestCase("splineHandleHotControl")]
        [TestCase("modifierBrushHotControl")]
        [TestCase("projectionHandleControl")]
        [TestCase("stencilControl")]
        [TestCase("regionControl")]
        [TestCase("symmetryHandleControl")]
        public void AltNavigationReleasesStaleCaptureBeforeReconstructionOrLayerChecks(string field)
        {
            InGUI(new Event { type = EventType.MouseDown, button = 0, modifiers = EventModifiers.Alt }, () =>
            {
            Set(field, 741);
            GUIUtility.hotControl = 741;
            // No reconstruction or active layer, and all gesture flags false: this is the
            // interrupted/recovered-state path that the old handoff never reached.
            Invoke("OnSceneGUI", (SceneView)null);
            Assert.That(GUIUtility.hotControl, Is.Zero);
            Assert.That(Get(field), Is.EqualTo(0));
            Assert.That(Event.current.type, Is.EqualTo(EventType.MouseDown), "Leave the orbit event for Unity.");
            });
        }

        [TestCase(EventType.Layout, 0)]
        [TestCase(EventType.Repaint, 0)]
        [TestCase(EventType.MouseDown, 0)]
        [TestCase(EventType.MouseDown, 1)]
        [TestCase(EventType.MouseDown, 2)]
        [TestCase(EventType.MouseDrag, 0)]
        [TestCase(EventType.MouseUp, 0)]
        public void NavigationNeverConsumesEventsOrStealsUnitysCapture(EventType type, int button)
        {
            InGUI(new Event { type = type, button = button, modifiers = EventModifiers.Alt }, () =>
            {
            Set("paintHotControl", 741);
            Set("paintGestureActive", true);
            Set("modifierBrushDrag", true);
            Set("regionDragging", true);
            GUIUtility.hotControl = 987; // A control owned by Unity navigation, not the painter.
            Invoke("OnSceneGUI", (SceneView)null);
            Assert.That(GUIUtility.hotControl, Is.EqualTo(987));
            Assert.That(Event.current.type, Is.EqualTo(type));
            Assert.That(Get("paintGestureActive"), Is.False);
            Assert.That(Get("modifierBrushDrag"), Is.False);
            Assert.That(Get("regionDragging"), Is.False);
            Assert.That(Get("paintHotControl"), Is.EqualTo(0));
            });
        }

        [TestCase(KeyCode.LeftAlt)]
        [TestCase(KeyCode.RightAlt)]
        public void AltKeyReleasesCaptureBeforeFirstNavigationMouseDown(KeyCode key)
        {
            InGUI(new Event { type = EventType.KeyDown, keyCode = key }, () =>
            {
            Set("paintHotControl", 741);
            GUIUtility.hotControl = 741;
            Invoke("OnSceneGUI", (SceneView)null);
            Assert.That(GUIUtility.hotControl, Is.Zero);
            Assert.That(Event.current.type, Is.EqualTo(EventType.KeyDown));
            });
        }

        [Test]
        public void OrdinaryPaintEventsDoNotRequestNavigation()
        {
            Assert.That(TexturePaintStageWindow.ShouldYieldToSceneNavigation(
                new Event { type = EventType.MouseDrag, button = 0 }), Is.False);
            Assert.That(TexturePaintStageWindow.ShouldYieldToSceneNavigation(null), Is.False);
        }

        [Test]
        public void YieldingStencilDragKeepsItsCompletedMovementUndoable()
        {
            InGUI(new Event { type = EventType.MouseDown, modifiers = EventModifiers.Alt }, () =>
            {
            var before = new TexturePaintStencil { center = new Vector2(.2f, .3f) };
            var after = before.Clone(); after.center = new Vector2(.7f, .8f);
            Set("paintingStencil", after);
            Set("stencilDragStart", before);
            Set("stencilControl", 741);
            GUIUtility.hotControl = 741;
            Invoke("OnSceneGUI", (SceneView)null);
            Assert.That(GUIUtility.hotControl, Is.Zero);
            Assert.That(((TexturePaintStencil)Get("paintingStencil")).center, Is.EqualTo(after.center));
            Invoke("UndoLightweight");
            Assert.That(((TexturePaintStencil)Get("paintingStencil")).center, Is.EqualTo(before.center));
            });
        }

        private static void InGUI(Event input, System.Action test)
        {
            // Event.current exists only during a real IMGUI dispatch in Unity 6.3.
            var window = ScriptableObject.CreateInstance<TexturePaintPluginParameterInputTestWindow>();
            var previous = EditorWindow.focusedWindow;
            System.Exception failure = null;
            bool ran = false;
            try
            {
                window.ShowUtility(); window.position = new Rect(100, 100, 240, 100); window.Focus();
                window.draw = () =>
                {
                    if (ran || Event.current.type != input.type) return;
                    ran = true;
                    int hot = GUIUtility.hotControl;
                    // Native Repaint dispatch uses the physical keyboard state rather than
                    // SendEvent's modifiers. Supply the simulated held-key state inside GUI.
                    EventModifiers modifiers = Event.current.modifiers;
                    Event.current.modifiers = input.modifiers;
                    try { test(); }
                    catch (System.Exception exception) { failure = exception; }
                    finally { GUIUtility.hotControl = hot; Event.current.modifiers = modifiers; }
                };
                window.SendEvent(input);
                Assert.That(ran, Is.True, "The test input reached OnGUI.");
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally { window.draw = null; window.Close(); if (previous != null) previous.Focus(); }
        }

        private void Set(string field, object value) => typeof(TexturePaintStageWindow).GetField(field, Private).SetValue(stage, value);
        private object Get(string field) => typeof(TexturePaintStageWindow).GetField(field, Private).GetValue(stage);
        private object Invoke(string method, params object[] args) => typeof(TexturePaintStageWindow).GetMethod(method, Private).Invoke(stage, args);
    }
}
#endif
