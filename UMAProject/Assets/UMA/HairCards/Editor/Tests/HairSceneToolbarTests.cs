#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [TestCase(700f, 0f, 0f)]
        [TestCase(700f, 40f, 0f)]
        [TestCase(700f, 0f, 56f)]
        [TestCase(700f, 40f, 56f)]
        [TestCase(320f, 40f, 40f)]
        [TestCase(240f, 40f, 40f)]
        [TestCase(1800f, 80f, 64f)]
        public void SceneToolbarFitsBetweenBothDockedRails(float windowWidth, float leftRail, float rightRail)
        {
            var viewport = new Rect(leftRail, 38f, windowWidth - leftRail - rightRail, 600f);
            var layout = new HairCardStage.SceneToolbarLayout(viewport);
            Assert.That(layout.Toolbar.x, Is.EqualTo(12f), "Scene GUI is already viewport-relative: do not add the left rail twice.");
            Assert.That(viewport.x + layout.Toolbar.x, Is.GreaterThan(leftRail));
            Assert.That(viewport.x + layout.Toolbar.xMax, Is.LessThanOrEqualTo(windowWidth - rightRail - 12f));
            Assert.That(layout.Toolbar.width, Is.EqualTo(Mathf.Min(1080f, viewport.width - 24f)));
            Assert.That(layout.Save.width, Is.EqualTo(46f));
            Assert.That(layout.Exit.width, Is.EqualTo(46f));
            Assert.That(layout.Tools.xMax, Is.LessThanOrEqualTo(layout.Save.x));
            Assert.That(layout.Save.xMax, Is.EqualTo(layout.Exit.x));
            Assert.That(layout.Exit.xMax, Is.EqualTo(layout.Toolbar.xMax));
            Assert.That(layout.Status.x, Is.EqualTo(layout.Toolbar.x));
            Assert.That(layout.Status.xMax, Is.EqualTo(layout.Toolbar.xMax));
            Assert.That(layout.HelpWidth, Is.LessThanOrEqualTo(layout.Toolbar.width));
        }

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(100f)]
        public void SceneToolbarKeepsValidBoundsDuringExtremeResize(float width)
        {
            var layout = new HairCardStage.SceneToolbarLayout(new Rect(40f, 38f, width, 600f));
            foreach (var rect in new[] { layout.Toolbar, layout.Tools, layout.Save, layout.Exit, layout.Status })
            {
                Assert.That(rect.width, Is.GreaterThanOrEqualTo(0f));
                Assert.That(rect.x, Is.GreaterThanOrEqualTo(0f));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(width));
            }
        }

        [UnityTest]
        public IEnumerator SceneToolbarRendersAfterResizeWithLongStatus()
        {
            var view = ScriptableObject.CreateInstance<SceneView>();
            HairCardStage stage = null;
            int repaints = 0;
            float renderedWidth = 0f;
            System.Action<SceneView> observe = target =>
            {
                if (target != view || Event.current.type != EventType.Repaint) return;
                var layout = new HairCardStage.SceneToolbarLayout(view.cameraViewport);
                Assert.That(layout.Exit.xMax, Is.LessThanOrEqualTo(view.cameraViewport.width - 12f));
                Assert.That(layout.Status.xMax, Is.EqualTo(layout.Exit.xMax));
                renderedWidth = layout.Toolbar.width;
                repaints++;
            };
            try
            {
                view.position = new Rect(100f, 100f, 1100f, 600f);
                view.Show(); view.Focus(); yield return null;
                groom.Groups[0].name = new string('W', 220);
                stage = HairCardStage.ShowStage(groom);
                SceneView.duringSceneGui += observe;
                // Opening a stage restores its workspace layout; size our test view afterward.
                yield return null; yield return null;
                view.position = new Rect(100f, 100f, 1100f, 600f);
                view.Repaint(); yield return null; yield return null;
                Assert.That(repaints, Is.GreaterThan(0), "Exercise the actual Scene GUI, not just the layout helper.");
                float wideWidth = renderedWidth;
                repaints = 0;
                view.position = new Rect(100f, 100f, 420f, 600f);
                view.Repaint(); yield return null; yield return null;
                Assert.That(repaints, Is.GreaterThan(0));
                Assert.That(renderedWidth, Is.LessThan(wideWidth));
                Assert.That(renderedWidth, Is.EqualTo(view.cameraViewport.width - 24f).Within(0.01f));
                // Native mouse injection is unreliable in batch mode. Geometry above covers the
                // reserved hit rect; the exit action also has separate persistence/restore tests.
                stage.ExitGrooming();
                Assert.That(HairCardStage.ActiveStage, Is.Null);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                SceneView.duringSceneGui -= observe;
                if (stage != null && HairCardStage.ActiveStage == stage) StageUtility.GoBackToPreviousStage();
                view.Close();
            }
        }
    }
}
#endif
