#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairCardStage CreateLayerEditingStage(HairSculptLayer layer)
        {
            HairCardStage stage = CreateEditingStage();
            stage.SetActiveGroup(groom.Groups[0].Id);
            stage.SetActiveLayer(layer.Id);
            Assert.That(stage.BeginLayerEditing(), Is.True);
            stage.RebuildNow();
            return stage;
        }

        private HairEvaluationOptions AtLayer(HairSculptLayer layer) => new HairEvaluationOptions
        { editGroupId = groom.Groups[0].Id, editLayerId = layer.Id };

        [TestCase(false, 0, 1f, HairSculptBlendMode.Additive)]
        [TestCase(false, 0, 0.25f, HairSculptBlendMode.Override)]
        [TestCase(true, 0, 1f, HairSculptBlendMode.Additive)]
        [TestCase(true, 13, 0.25f, HairSculptBlendMode.Additive)]
        [TestCase(true, 13, 0.25f, HairSculptBlendMode.Override)]
        public void SculptEditPointRepeatedFlowStrokesPreserveReevaluatedSegments(bool finishing, int samples, float opacity, HairSculptBlendMode blend)
        {
            HairModifierSettings flow = TestSplineFlow();
            HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer baseLayer = HairGroomCommands.AddSculptLayer(groom, group);
            baseLayer.modifiers.Add(flow);
            if (samples > 0) HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample, baseLayer).amount = samples;
            HairSculptLayer edit = finishing ? HairGroomCommands.AddSculptLayer(groom, group, "Finishing", true) : baseLayer;
            edit.opacity = opacity; edit.blendMode = blend;
            // An enabled downstream operation must never feed back into the brush's input.
            HairSculptLayer above = HairGroomCommands.AddSculptLayer(groom, group);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Wave, above).amount = 0.02f;
            using var unused = new TestSculptStageScope(this, CreateLayerEditingStage(edit));
            HairCardStage stage = unused.Stage;
            HairEvaluatedCurve initial = HairGroomEvaluator.Evaluate(groom, AtLayer(edit)).evaluatedGuides[0];
            Vector3[] authored = group.guides[0].points.Select(p => p.position).ToArray();
            stage.SceneTool = HairSceneTool.Comb;
            for (int stroke = 0; stroke < 40; stroke++)
            {
                InvokeStageMethod(stage, "BeginStroke", new object[] { null });
                for (int dab = 0; dab < 4; dab++)
                    InvokeStageMethod(stage, "SculptAt", new Vector3(0.1f, 0.1f, 0f), Vector3.forward,
                        new Vector3(0.003f, 0.004f, 0.001f));
                InvokeStageMethod(stage, "EndStroke");
                stage.RebuildNow();
                HairEvaluatedCurve resolved = HairGroomEvaluator.Evaluate(groom, AtLayer(edit)).evaluatedGuides[0];
                AssertFlowLengths(initial, resolved);
                for (int i = 0; i < resolved.points.Count; i++)
                    Assert.That(Vector3.Distance(stage.Evaluation.evaluatedGuides[0].points[i].position, resolved.points[i].position), Is.LessThan(2e-6f));
            }
            Assert.That(edit.deltas.Count, Is.EqualTo(1), "The strokes must actually edit, not merely be rejected.");
            Assert.That(edit.deltas[0].positionOffsets.Length, Is.EqualTo(initial.points.Count));
            Assert.That(Vector3.Distance(initial.points[^1].position, stage.Evaluation.evaluatedGuides[0].points[^1].position), Is.GreaterThan(0.005f));
            Assert.That(group.guides[0].points.Select(p => p.position), Is.EqualTo(authored));
            Assert.That(flow.enabled && above.visible && above.modifiers[0].enabled, Is.True);
        }

        private sealed class TestSculptStageScope : IDisposable
        {
            private readonly HairCoreTests owner;
            internal readonly HairCardStage Stage;
            internal TestSculptStageScope(HairCoreTests owner, HairCardStage stage) { this.owner = owner; Stage = stage; }
            public void Dispose() => owner.DestroyEditingStage(Stage);
        }

        [TestCase(HairSceneTool.Grab)] [TestCase(HairSceneTool.Smooth)]
        [TestCase(HairSceneTool.Clump)] [TestCase(HairSceneTool.Part)]
        public void SculptEditPointAllShapeBrushesRetainLengthsAfterFlowAndResampling(HairSceneTool tool)
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer first = HairGroomCommands.AddSculptLayer(groom, group); first.modifiers.Add(flow);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample, first).amount = 15;
            HairSculptLayer last = HairGroomCommands.AddSculptLayer(groom, group, "Finishing", true);
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(last));
            HairCardStage stage = scope.Stage; stage.SceneTool = tool;
            HairEvaluatedCurve before = HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0];
            for (int i = 0; i < 15; i++)
            {
                InvokeStageMethod(stage, "SculptAt", new Vector3(0.15f, 0.02f, 0), Vector3.up, Vector3.forward * 0.01f);
                stage.RebuildNow();
                AssertFlowLengths(before, HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0]);
            }
        }

        [Test]
        public void SculptFinalPreviewCannotWriteUpstreamAndEditSessionDoesNotChangeFlags()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group); layer.modifiers.Add(flow);
            using var scope = new TestSculptStageScope(this, CreateEditingStage());
            HairCardStage stage = scope.Stage; stage.SetActiveGroup(group.Id); stage.SetActiveLayer(layer.Id);
            string before = EditorJsonUtility.ToJson(groom);
            InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.right * 0.1f);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            Assert.That(stage.MoveGuidePoint(group.guides[0].Id, 1, Vector3.one), Is.False);
            stage.SetGravityHeld(true); Assert.That(stage.GravitySimulationActive, Is.False);
            Assert.That(stage.BeginLayerEditing(), Is.True);
            stage.RebuildNow();
            Assert.That(FlowSegment(stage.Evaluation.evaluatedGuides[0]).normalized, Is.EqualTo(Vector3.up));
            Assert.That(stage.IsNodeTemporarilyBypassed(stage.FindNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, flow.Id))), Is.True);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            stage.ReturnToFinalPreview(); stage.RebuildNow();
            Assert.That(stage.IsLayerEditing, Is.False);
            Assert.That(Vector3.Dot(FlowSegment(stage.Evaluation.evaluatedGuides[0]).normalized, Vector3.right), Is.GreaterThan(0.999f));
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            stage.SetActiveModifier(layer.Id, flow.Id);
            InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.right * 0.1f);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
        }

        [Test]
        public void SculptFinishingActionPreservesFinalShapeIncludingLegacyModifiersAndChildren()
        {
            TestSplineFlow(); HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 2; group.children.lengthVariation = 0f;
            HairEvaluationResult before = HairGroomEvaluator.Evaluate(groom);
            using var scope = new TestSculptStageScope(this, CreateEditingStage());
            HairCardStage stage = scope.Stage;
            HairSculptLayer finishing = HairGroomNodeWindow.AddFinishingSculptLayer(stage, group);
            Assert.That(finishing.afterGroupOperations && stage.IsLayerEditing, Is.True);
            HairEvaluationResult editing = HairGroomEvaluator.Evaluate(groom, AtLayer(finishing));
            Assert.That(editing.evaluatedGuides[0].points, Is.EqualTo(before.evaluatedGuides[0].points));
            Assert.That(editing.curves.Count, Is.EqualTo(before.curves.Count));
            for (int i = 0; i < before.curves.Count; i++) Assert.That(editing.curves[i].points, Is.EqualTo(before.curves[i].points));
            Assert.That(HairGroomCommands.DuplicateLayer(groom, group, finishing).afterGroupOperations, Is.True);
        }

        [Test]
        public void SculptEditPointLengthAndLayerLocalCutRemainIntentionalAndUndoable()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer first = HairGroomCommands.AddSculptLayer(groom, group); first.modifiers.Add(flow);
            HairSculptLayer last = HairGroomCommands.AddSculptLayer(groom, group, "Finishing", true);
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(last));
            HairCardStage stage = scope.Stage;
            string authored = JsonUtility.ToJson(group.guides[0]);
            float length = HairGroomEvaluator.Evaluate(groom).evaluatedGuides[0].Length;
            stage.SceneTool = HairSceneTool.Length;
            InvokeStageMethod(stage, "BeginStroke", new object[] { null });
            InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.zero);
            InvokeStageMethod(stage, "EndStroke"); stage.RebuildNow();
            Assert.That(stage.Evaluation.evaluatedGuides[0].Length, Is.GreaterThan(length));
            float longer = stage.Evaluation.evaluatedGuides[0].Length;
            var entries = (IList)typeof(HairCardStage).GetField("curveBrushEntries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
            InvokeStageMethod(stage, "BeginStroke", new object[] { "Slice Cut Hair" });
            InvokeStageMethod(stage, "CutEvaluatedGuide", entries[0], 0.5f);
            InvokeStageMethod(stage, "EndStroke"); stage.RebuildNow();
            Assert.That(stage.Evaluation.evaluatedGuides[0].Length, Is.EqualTo(longer * 0.5f).Within(2e-6f));
            Assert.That(JsonUtility.ToJson(group.guides[0]), Is.EqualTo(authored));
            Undo.PerformUndo(); InvokeStageMethod(stage, "OnUndoRedo"); stage.RebuildNow();
            Assert.That(stage.Evaluation.evaluatedGuides[0].Length, Is.EqualTo(longer).Within(2e-6f));
        }

        [Test]
        public void SculptBoundaryKeepsEarlierOperationsAndOtherGroupsButBypassesOwnChildrenAndConstraints()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer first = HairGroomCommands.AddSculptLayer(groom, group); first.modifiers.Add(flow);
            HairSculptLayer edit = HairGroomCommands.AddSculptLayer(groom, group);
            HairModifierSettings own = HairGroomCommands.AddModifier(groom, group, HairModifierType.Length, edit);
            own.amount = 2f; own.domain = HairModifierDomain.Children;
            HairSculptLayer later = HairGroomCommands.AddSculptLayer(groom, group);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Noise, later).amount = 0.03f;
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Length).amount = 1.6f;
            var helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.up * 0.1f);
            HairGroomCommands.AddConstraint(groom, group, HairConstraintType.FollowCurve, helper);
            group.children.childrenPerGuide = 3;
            HairGroup other = HairGroomCommands.AddGroup(groom, HairGroupRole.Flyaway);
            other.guides.Add(CreateGuide("Unaffected group", 31, Vector3.zero)); other.profile = profile;
            HairGroomCommands.AddModifier(groom, other, HairModifierType.Length).amount = 2f;
            string before = EditorJsonUtility.ToJson(groom);
            HairGroomAsset expectedAsset = Object.Instantiate(groom);
            try
            {
                var expectedGroup = expectedAsset.Groups[0];
                expectedGroup.sculptLayers.RemoveAt(2); expectedGroup.sculptLayers[1].modifiers.Clear();
                expectedGroup.modifiers.Clear(); expectedGroup.constraints.Clear();
                var expected = HairGroomEvaluator.Evaluate(expectedAsset);
                var actual = HairGroomEvaluator.Evaluate(groom, AtLayer(edit));
                Assert.That(actual.curves.Count, Is.EqualTo(expected.curves.Count));
                for (int i = 0; i < expected.curves.Count; i++) Assert.That(actual.curves[i].points, Is.EqualTo(expected.curves[i].points));
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
                // Bake/runtime callers do not inherit the editor's temporary boundary.
                var full = HairGroomEvaluator.Evaluate(groom);
                Assert.That(full.evaluatedGuides[0].points, Is.Not.EqualTo(actual.evaluatedGuides[0].points));
            }
            finally { Object.DestroyImmediate(expectedAsset); }
        }

        [Test]
        public void SculptFinishingAfterConstraintKeepsInputAndReopenDoesNotPersistEditBoundary()
        {
            TestSplineFlow(); HairGroup group = groom.Groups[0];
            var helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.up * 0.1f);
            HairGroomCommands.AddConstraint(groom, group, HairConstraintType.FollowCurve, helper);
            var before = HairGroomEvaluator.Evaluate(groom).evaluatedGuides[0];
            using var scope = new TestSculptStageScope(this, CreateEditingStage());
            var stage = scope.Stage;
            var finish = HairGroomNodeWindow.AddFinishingSculptLayer(stage, group);
            Assert.That(HairGroomEvaluator.Evaluate(groom, AtLayer(finish)).evaluatedGuides[0].points, Is.EqualTo(before.points));
            var restored = ScriptableObject.CreateInstance<HairCardStage>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(stage), restored);
                Assert.That(restored.IsLayerEditing, Is.False);
                Assert.That(typeof(HairCardStage).GetField("editingLayerId", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetCustomAttribute<NonSerializedAttribute>(), Is.Not.Null, "Unity hot reload must not persist the temporary boundary either.");
                var nodes = stage.Nodes.ToList();
                Assert.That(nodes.FindIndex(n => n.Kind == HairGroomNodeKind.Constraints), Is.LessThan(nodes.FindIndex(n => n.Layer == finish)));
            }
            finally { Object.DestroyImmediate(restored); }
        }

        [Test]
        public void SculptFirstZeroCombAndNonFiniteTargetsDoNotCreateOverrideDeltas()
        {
            TestSplineFlow(); var group = groom.Groups[0];
            var last = HairGroomCommands.AddSculptLayer(groom, group, "Finish", true); last.blendMode = HairSculptBlendMode.Override;
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(last));
            var stage = scope.Stage; stage.SceneTool = HairSceneTool.Comb;
            string before = EditorJsonUtility.ToJson(groom);
            InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.zero);
            InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, new Vector3(float.NaN, 0, 0));
            Assert.That(stage.MoveGuidePoint(group.guides[0].Id, 1, Vector3.one * float.PositiveInfinity), Is.False);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
        }

        [Test]
        public void SculptFinishingRebindsItsControlLatticeWhenAnEarlierResampleChanges()
        {
            var flow = TestSplineFlow(); var group = groom.Groups[0]; group.modifiers.Clear();
            var first = HairGroomCommands.AddSculptLayer(groom, group); first.modifiers.Add(flow);
            var resample = HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample, first); resample.amount = 13;
            var last = HairGroomCommands.AddSculptLayer(groom, group, "Finish", true);
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(last));
            var stage = scope.Stage;
            foreach (int samples in new[] { 13, 21, 7 })
            {
                resample.amount = samples; EditorUtility.SetDirty(groom); stage.RebuildNow();
                var before = HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0];
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.up * 0.02f);
                stage.RebuildNow();
                var after = HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0];
                AssertFlowLengths(before, after);
                Assert.That(last.deltas[0].positionOffsets.Length, Is.EqualTo(samples));
            }
        }

        [TestCase(HairSculptBlendMode.Additive)] [TestCase(HairSculptBlendMode.Override)]
        public void SculptPointHandleAfterFlowAndResampleKeepsItsPreviewOnReevaluation(HairSculptBlendMode blend)
        {
            var flow = TestSplineFlow(); var group = groom.Groups[0]; group.modifiers.Clear();
            var first = HairGroomCommands.AddSculptLayer(groom, group); first.modifiers.Add(flow);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample, first).amount = 12;
            var last = HairGroomCommands.AddSculptLayer(groom, group, "Finish", true); last.blendMode = blend; last.opacity = 0.5f;
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(last));
            var stage = scope.Stage; var guide = group.guides[0];
            var before = HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0];
            for (int i = 0; i < 6; i++)
            {
                Assert.That(stage.MoveGuidePoint(guide.Id, 1, stage.DisplayedControlPoint(guide, 1) + Vector3.up * 0.03f), Is.True);
                Vector3 preview = stage.DisplayedControlPoint(guide, 1);
                InvokeStageMethod(stage, "EndStroke"); stage.RebuildNow();
                Assert.That(Vector3.Distance(stage.DisplayedControlPoint(guide, 1), preview), Is.LessThan(2e-6f));
                AssertFlowLengths(before, HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0]);
            }
        }

        [TestCase(0f)] [TestCase(1f)]
        public void SculptGravityAfterResamplingHonorsFrozenAnchorRootAndSegmentLengths(float rootInfluence)
        {
            var flow = TestSplineFlow(); var group = groom.Groups[0]; group.modifiers.Clear();
            var first = HairGroomCommands.AddSculptLayer(groom, group); first.modifiers.Add(flow);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample, first).amount = 13f;
            var last = HairGroomCommands.AddSculptLayer(groom, group, "Finish", true);
            group.guides[0].points[^1].freeze = 1f;
            using var scope = new TestSculptStageScope(this, CreateLayerEditingStage(last));
            var stage = scope.Stage; stage.GravityCollision = false; stage.BrushRootInfluence = rootInfluence;
            var before = HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0];
            stage.SetGravityHeld(true);
            for (int i = 0; i < 30; i++) InvokeStageMethod(stage, "ApplyGravityStep", 1f / 60f);
            stage.SetGravityHeld(false); stage.RebuildNow();
            var after = HairGroomEvaluator.Evaluate(groom, AtLayer(last)).evaluatedGuides[0];
            AssertFlowLengths(before, after);
            Assert.That(after.points[^1].position, Is.EqualTo(before.points[^1].position));
        }

        [UnityTest]
        public IEnumerator SculptEditActionsAndPropertiesRenderInNarrowAndWideDockedLayouts()
        {
            var flow = TestSplineFlow(); var group = groom.Groups[0]; group.modifiers.Clear();
            var layer = HairGroomCommands.AddSculptLayer(groom, group); layer.modifiers.Add(flow);
            using var scope = new TestSculptStageScope(this, CreateEditingStage());
            var stage = scope.Stage;
            var nodes = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            var properties = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            var active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage);
                nodes.Show(); properties.Show();
                foreach (int width in new[] { 320, 540 })
                {
                    nodes.position = new Rect(30, 80, width, 900);
                    properties.position = new Rect(width + 40, 80, 650, 900);
                    stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id));
                    foreach (int state in new[] { 0, 1, 2, 3 })
                    {
                        if (state == 1) stage.BeginLayerEditing();
                        if (state == 2) stage.ReturnToFinalPreview();
                        if (state == 3) HairGroomNodeWindow.AddFinishingSculptLayer(stage, group);
                        nodes.Repaint(); properties.Repaint(); yield return null; yield return null;
                        LogAssert.NoUnexpectedReceived();
                    }
                }
            }
            finally { nodes.Close(); properties.Close(); active.SetValue(null, previous); }
        }
    }
}
#endif
