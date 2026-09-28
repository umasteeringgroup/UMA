#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairGuide EraseTestGuide(Vector3 root, Vector3 tip)
        {
            HairGuide guide = CreateLinearGuide("Erase test", 123, root, tip - root);
            // Keep the deliberately spaced test guides off the single shared triangle anchor.
            guide.root = HairSurfaceAnchor.Create("erase:test", 0, 0, new Vector3(0.25f, 0.25f, 0.5f),
                0f, root, Vector3.up);
            return guide;
        }

        [Test]
        public void EraseSweepsWholeGuidesAndAllLayerDeltasAsOneUndoableStroke()
        {
            HairGroup group = groom.Groups[0]; group.guides.Clear();
            for (int i = 0; i < 4; i++) group.guides.Add(EraseTestGuide(new Vector3(i, 0, 0), new Vector3(i, 1, 0)));
            var layer = HairGroomCommands.AddSculptLayer(groom, group);
            var other = HairGroomCommands.AddSculptLayer(groom, group); other.locked = true;
            foreach (var guide in group.guides)
                foreach (var pass in group.sculptLayers)
                    pass.deltas.Add(new HairGuideDelta { guideId = guide.Id, positionOffsets = new Vector3[3] });
            group.children.childrenPerGuide = 2;
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(layer));
            HairCardStage stage = scope.Stage; stage.SceneTool = HairSceneTool.Erase;
            stage.BrushRadius = 0.03f; stage.BrushHardness = 0f; stage.BrushStrength = 0.01f;
            stage.PaintErase = true; // Reverse must not turn deletion into creation or suppress it.
            stage.BrushScope = HairBrushScope.DepthVolume;
            string survivor = group.guides[3].Id;
            stage.SetGuideSelection(group.guides.Select(g => g.Id));
            string original = EditorJsonUtility.ToJson(groom);
            InvokeStageMethod(stage, "EraseGuidesAt", new Vector3(-0.1f, 0.5f, 0), new Vector3(1.1f, 0.5f, 0), Vector3.forward);
            Assert.That(group.guides.Count, Is.EqualTo(2));
            InvokeStageMethod(stage, "EraseGuidesAt", new Vector3(1.1f, 0.5f, 0), new Vector3(2.1f, 0.5f, 0), Vector3.forward);
            Assert.That(group.guides.Select(g => g.Id), Is.EqualTo(new[] { survivor }));
            foreach (var pass in group.sculptLayers) Assert.That(pass.deltas.Select(d => d.guideId), Is.EqualTo(new[] { survivor }));
            Assert.That(stage.SelectedGuideIds, Is.EqualTo(new[] { survivor }));
            var entries = (IList)typeof(HairCardStage).GetField("curveBrushEntries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
            Assert.That(entries.Count, Is.EqualTo(1), "Deleted guides must immediately stop drawing and receiving brush hits.");
            InvokeStageMethod(stage, "EndStroke"); stage.RebuildNow();
            Assert.That(stage.Evaluation.curves.All(c => c.parentGuideId == survivor), Is.True);
            string erased = EditorJsonUtility.ToJson(groom);
            Undo.PerformUndo(); InvokeStageMethod(stage, "OnUndoRedo"); stage.RebuildNow();
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
            Undo.PerformRedo(); InvokeStageMethod(stage, "OnUndoRedo"); stage.RebuildNow();
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(erased));
        }

        [TestCase(0, 0.01f)] [TestCase(1, 1f)] [TestCase(2, 0.5f)]
        public void EraseProtectsEntireGuideIfAnyAuthoredPointIsFrozen(int index, float freeze)
        {
            var group = groom.Groups[0]; group.guides.Clear();
            var protectedGuide = EraseTestGuide(Vector3.zero, Vector3.up);
            protectedGuide.points[index].freeze = freeze;
            group.guides.Add(protectedGuide); group.guides.Add(EraseTestGuide(Vector3.zero, Vector3.up));
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage; stage.SceneTool = HairSceneTool.Erase;
            stage.BrushRadius = 0.03f;
            InvokeStageMethod(stage, "SculptAt", Vector3.up * 0.75f, Vector3.forward, Vector3.zero);
            Assert.That(group.guides.Select(g => g.Id), Is.EqualTo(new[] { protectedGuide.Id }));
        }

        [TestCase(HairBrushScope.ThroughDepth, false, 0)]
        [TestCase(HairBrushScope.DepthVolume, false, 1)]
        [TestCase(HairBrushScope.SelectedGuidesOnly, false, 1)]
        [TestCase(HairBrushScope.ThroughDepth, true, 1)]
        public void EraseRespectsDepthSelectionAndIsolation(HairBrushScope brushScope, bool isolate, int remaining)
        {
            var group = groom.Groups[0]; group.guides.Clear();
            var front = EraseTestGuide(Vector3.zero, Vector3.up);
            var back = EraseTestGuide(Vector3.forward, Vector3.up + Vector3.forward);
            group.guides.Add(front); group.guides.Add(back);
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage; stage.SceneTool = HairSceneTool.Erase;
            stage.SetGuideSelection(new[] { front.Id }); stage.IsolateSelectedGuides = isolate;
            stage.BrushScope = brushScope; stage.BrushRadius = 0.03f; stage.RebuildNow();
            InvokeStageMethod(stage, "SculptAt", Vector3.up * 0.25f, Vector3.forward, Vector3.zero);
            Assert.That(group.guides.Count, Is.EqualTo(remaining));
            if (remaining > 0) Assert.That(group.guides[0].Id, Is.EqualTo(back.Id));
            Assert.That(stage.IsolateSelectedGuides, Is.EqualTo(isolate), "Erasing the selection must not silently expand edit scope.");
        }

        [TestCase(false)] [TestCase(true)]
        public void EraseVisibleScopeChecksSurfaceAtTheHitBetweenControlPoints(bool transformed)
        {
            var group = groom.Groups[0]; group.guides.Clear();
            var front = EraseTestGuide(new Vector3(-0.1f, 0.1f, 0), new Vector3(0.1f, 0.1f, 0));
            var back = EraseTestGuide(new Vector3(-0.1f, -0.1f, 0), new Vector3(0.1f, -0.1f, 0));
            front.points.RemoveAt(1); back.points.RemoveAt(1);
            group.guides.Add(front); group.guides.Add(back);
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage;
            var cameraObject = new GameObject("Erase test camera"); var sourceObject = new GameObject("Erase test source");
            try
            {
                if (transformed) sourceObject.transform.SetPositionAndRotation(new Vector3(3, 5, 7), Quaternion.Euler(20, 35, 10));
                sourceObject.transform.localScale = transformed ? Vector3.one * 2f : Vector3.one;
                SetStageField(stage, "sourceSpaceObject", sourceObject);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = sourceObject.transform.TransformPoint(Vector3.up * 2);
                camera.transform.rotation = sourceObject.transform.rotation * Quaternion.LookRotation(Vector3.down, Vector3.forward);
                camera.orthographic = true;
                SetStageField(stage, "brushCamera", camera);
                SetStageField(stage, "surfaceRaycaster", new HairMeshRaycaster(sourceMesh));
                stage.SceneTool = HairSceneTool.Erase; stage.BrushScope = HairBrushScope.VisibleHair; stage.BrushRadius = 0.02f;
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.up, Vector3.zero);
                Assert.That(group.guides.Select(g => g.Id), Is.EqualTo(new[] { back.Id }));
                stage.BrushScope = HairBrushScope.ThroughDepth;
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.up, Vector3.zero);
                Assert.That(group.guides, Is.Empty);
            }
            finally { SetStageField(stage, "sourceSpaceObject", null); Object.DestroyImmediate(sourceObject); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void EraseUsesFinalModifierResultWithoutChangingEditBoundaryOrOtherGroups()
        {
            var flow = TestSplineFlow(); var group = groom.Groups[0]; group.modifiers.Clear();
            var layer = HairGroomCommands.AddSculptLayer(groom, group); layer.modifiers.Add(flow);
            var unrelated = new HairGroup { name = "Other", profile = profile };
            unrelated.guides.Add(EraseTestGuide(Vector3.zero, Vector3.up)); groom.Groups.Add(unrelated); groom.EnsureIntegrity();
            using var scope = new TestSculptStageScope(this, CreateEditingStage());
            var stage = scope.Stage; stage.SetActiveGroup(group.Id); stage.SetActiveLayer(layer.Id);
            stage.SceneTool = HairSceneTool.Erase; stage.BrushRadius = 0.001f; stage.RebuildNow();
            string otherBefore = JsonUtility.ToJson(unrelated), modifierBefore = JsonUtility.ToJson(flow);
            var curve = stage.Evaluation.evaluatedGuides.Find(c => c.groupId == group.Id);
            Vector3 center = curve.points[^1].position;
            Assert.That(stage.IsLayerEditing, Is.False);
            Assert.That(stage.CanEraseGuides, Is.True);
            InvokeStageMethod(stage, "SculptAt", center, Vector3.forward, Vector3.zero);
            Assert.That(group.guides, Is.Empty);
            Assert.That(stage.IsLayerEditing, Is.False, "Erase must never disable modifiers or switch the displayed shape.");
            Assert.That(JsonUtility.ToJson(flow), Is.EqualTo(modifierBefore));
            Assert.That(JsonUtility.ToJson(unrelated), Is.EqualTo(otherBefore));
        }

        [TestCase("groupLocked")] [TestCase("groupHidden")] [TestCase("groupDisabled")]
        [TestCase("layerLocked")] [TestCase("layerHidden")] [TestCase("layerZeroOpacity")]
        [TestCase("modifierSelected")] [TestCase("collectionSelected")]
        public void EraseCannotDeleteFromAnInvalidSculptTarget(string state)
        {
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage; var group = groom.Groups[0]; var layer = group.sculptLayers[0];
            stage.SceneTool = HairSceneTool.Erase;
            switch (state)
            {
                case "groupLocked": group.locked = true; break;
                case "groupHidden": group.visible = false; break;
                case "groupDisabled": group.enabled = false; break;
                case "layerLocked": layer.locked = true; break;
                case "layerHidden": layer.visible = false; break;
                case "layerZeroOpacity": layer.opacity = 0f; break;
                case "modifierSelected": stage.SetActiveModifier(layer.Id, HairGroomCommands.AddModifier(groom, group, HairModifierType.Wave, layer).Id); break;
                case "collectionSelected": SetStageField(stage, "activeNodeKey", HairGroomNodes.Key(HairGroomNodeKind.Groom, group.Id)); break;
            }
            Assert.That(stage.CanEraseGuides, Is.False);
            string before = EditorJsonUtility.ToJson(groom);
            InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.zero);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
        }

        [Test]
        public void EraseToolRoutesToGroomButIsNotRearmedBySavedPreferences()
        {
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage; stage.SceneTool = HairSceneTool.Erase;
            Assert.That(HairWorkflowState.IsGroomTool(HairSceneTool.Erase), Is.True);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Groom, HairSceneTool.Erase), Is.True);
            Assert.That(HairWorkflowState.StepForTool(HairSceneTool.Erase, HairWorkflowStep.Growth), Is.EqualTo(HairWorkflowStep.Groom));
            foreach (bool global in new[] { false, true })
            {
                stage.SceneTool = HairSceneTool.Erase;
                string saved = HairPreferenceCodec.Capture(stage, global);
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.Erase), "Saving must not interrupt erasing.");
                HairPreferenceCodec.Restore(stage, saved);
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.Comb));
                Assert.That(stage.SceneHelpText, Does.Not.Contain("ERASE GUIDES"));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void EraseSweptIntersectionHandlesDegenerateParallelAndCrossingSegments(bool throughDepth)
        {
            Vector3 shift = throughDepth ? Vector3.forward * 10f : Vector3.zero;
            Vector3 point = HairEraseBrushUtility.ClosestGuidePoint(Vector3.left, Vector3.right,
                Vector3.down + shift, Vector3.up + shift, Vector3.forward, throughDepth, out float square);
            Assert.That(square, Is.LessThan(1e-10f)); Assert.That(point, Is.EqualTo(shift));
            HairEraseBrushUtility.ClosestGuidePoint(Vector3.zero, Vector3.zero,
                Vector3.left + shift, Vector3.right + shift, Vector3.forward, throughDepth, out square);
            Assert.That(square, Is.LessThan(1e-10f));
            HairEraseBrushUtility.ClosestGuidePoint(Vector3.left, Vector3.right,
                Vector3.left + Vector3.up, Vector3.right + Vector3.up, Vector3.forward, throughDepth, out square);
            Assert.That(square, Is.EqualTo(1f).Within(1e-6f));
            HairEraseBrushUtility.ClosestGuidePoint(Vector3.left, Vector3.right, shift, shift, Vector3.forward, throughDepth, out square);
            Assert.That(square, Is.LessThan(1e-10f));
        }

        [Test]
        public void EraseStrokeCanStartInEmptySpaceSweepAcrossHairAndRelease()
        {
            var group = groom.Groups[0]; group.guides.Clear();
            group.guides.Add(EraseTestGuide(Vector3.zero, Vector3.up));
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage; stage.SceneTool = HairSceneTool.Erase; stage.BrushRadius = 0.03f;
            stage.BrushScope = HairBrushScope.DepthVolume;
            stage.ShowGuideRoots = stage.ShowControlPoints = true;
            stage.SetActiveGuide(group.guides[0].Id, 0);
            Ray start = new Ray(new Vector3(-1, 0.5f, -3), Vector3.forward);
            Vector3 center = stage.EraseFallbackCenter(start, Vector3.back, Vector3.zero);
            Assert.That(center, Is.EqualTo(new Vector3(-1, 0.5f, 0)));
            // These are the same stroke paths invoked by the MouseDown/MouseDrag/MouseUp handlers.
            // Actual OS capture/Scene hit ownership is covered by the manual QA checklist.
            InvokeStageMethod(stage, "BeginCurveBrushStroke", start, center, Vector3.back, 1f);
            Assert.That(stage.IsEditing, Is.True);
            Assert.That(group.guides.Count, Is.EqualTo(1));
            InvokeStageMethod(stage, "DragCurveBrushStroke", new Vector3(1, 0.5f, 0), Vector3.back);
            Assert.That(group.guides, Is.Empty, "Fast drags must erase crossed guides even when both event positions miss.");
            InvokeStageMethod(stage, "ReleaseSceneInputCapture", true);
            Assert.That(stage.IsEditing, Is.False);
            stage.RebuildNow(); Assert.That(stage.Evaluation.evaluatedGuides, Is.Empty);
            Undo.PerformUndo(); InvokeStageMethod(stage, "OnUndoRedo"); stage.RebuildNow();
            Assert.That(groom.Groups[0].guides.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EraseShelfAndPropertiesRenderAtNarrowAndWideWidths()
        {
            using var scope = new TestSculptStageScope(this, CreateEditingStage(true));
            var stage = scope.Stage; stage.SceneTool = HairSceneTool.Erase;
            var properties = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            var active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage); properties.Show();
                foreach (int width in new[] { 320, 650 })
                {
                    properties.position = new Rect(20, 50, width, 950);
                    foreach (HairBrushScope brushScope in System.Enum.GetValues(typeof(HairBrushScope)))
                    {
                        stage.BrushScope = brushScope; properties.Repaint(); yield return null; yield return null;
                        Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.Erase));
                        LogAssert.NoUnexpectedReceived();
                    }
                }
            }
            finally { properties.Close(); active.SetValue(null, previous); }
        }
    }
}
#endif
