#if UNITY_INCLUDE_TESTS
using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairModifierSettings GatherSetup(bool scalp = false)
        {
            var group = groom.Groups[0]; var guide = group.guides[0]; guide.points.Clear();
            for (int i = 0; i < 33; i++) guide.points.Add(new HairGuidePoint { position = Vector3.up * (i / 32f * .04f), width = .008f });
            var helper = HairGroomCommands.AddHelper(groom, HairHelperType.Gather, new Vector3(.13f, .12f, .01f));
            helper.gather.radii = Vector2.zero; helper.gather.spacing = 0;
            var modifier = new HairModifierSettings { type = HairModifierType.Gather, name = "Gather test", helperId = helper.Id, rootInfluence = 1 };
            modifier.gather.followScalp = scalp; modifier.gather.length = HairGatherLength.ExtendToReach; modifier.gather.fullCardClearance = false;
            group.modifiers.Add(modifier); groom.EnsureIntegrity(); return modifier;
        }
        private HairEvaluationResult GatherEvaluate(HairEvaluationWorkspace workspace = null) => HairGroomEvaluator.Evaluate(groom,
            new HairEvaluationOptions { evaluateSurfaceAnchors = false }, workspace ?? new HairEvaluationWorkspace());

        [TestCase(false)] [TestCase(true)]
        public void GatherExtendAnchorsRootReachesTargetAndDoesNotAccumulate(bool scalp)
        {
            var modifier = GatherSetup(scalp); var before = EditorJsonUtility.ToJson(groom); var workspace = new HairEvaluationWorkspace();
            var result = GatherEvaluate(workspace); var curve = result.evaluatedGuides[0];
            Assert.That(curve.points[0].position, Is.EqualTo(Vector3.zero));
            Assert.That(Vector3.Distance(curve.points[^1].position, groom.FindHelper(modifier.helperId).position), Is.LessThan(.0001f));
            Assert.That(curve.Length, Is.GreaterThan(.15f)); Assert.That(result.warnings, Is.Empty);
            Assert.That(GatherEvaluate(workspace).evaluatedGuides[0].points.Select(p => p.position), Is.EqualTo(curve.points.Select(p => p.position)));
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
        }
        [TestCase(false)] [TestCase(true)]
        public void GatherPreservesEverySegmentAndReportsUnreachable(bool frozen)
        {
            var m = GatherSetup(); m.gather.length = HairGatherLength.Preserve;
            var points = groom.Groups[0].guides[0].points; if (frozen) points[16].freeze = 1;
            var result = GatherEvaluate(); var output = result.evaluatedGuides[0].points;
            Assert.That(result.warnings.Any(s => s.Contains("did not reach")), Is.True);
            for (int i = 1; i < points.Count; i++) Assert.That(Vector3.Distance(output[i - 1].position, output[i].position),
                Is.EqualTo(Vector3.Distance(points[i - 1].position, points[i].position)).Within(1e-5f));
            if (frozen) Assert.That(output[16].position, Is.EqualTo(points[16].position));
        }
        [Test]
        public void GatherInvalidatesSurfaceRoutesWhenSourceIsRemovedAndRestored()
        {
            GatherSetup(true); var workspace = new HairEvaluationWorkspace();
            var expected = GatherEvaluate(workspace).evaluatedGuides[0].points.Select(p => p.position).ToArray();
            groom.SetSource(null, "missing");
            var missing = GatherEvaluate(workspace);
            Assert.That(missing.evaluatedGuides[0].points.Select(p => p.position),
                Is.EqualTo(groom.Groups[0].guides[0].points.Select(p => p.position)));
            Assert.That(missing.warnings.Any(s => s.Contains("no connected scalp path")), Is.True);
            groom.SetSource(sourceMesh, "restored");
            Assert.That(GatherEvaluate(workspace).evaluatedGuides[0].points.Select(p => p.position), Is.EqualTo(expected));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void GatherZeroControlsLeaveGeometryUntouched(int control)
        {
            var m = GatherSetup(); var points = groom.Groups[0].guides[0].points;
            if (control == 0) m.enabled = false;
            if (control == 1) m.weight = 0;
            if (control == 2) m.rootToTip = AnimationCurve.Constant(0, 1, 0);
            if (control == 3) foreach (var point in points) point.freeze = 1;
            Assert.That(GatherEvaluate().evaluatedGuides[0].points.Select(p => p.position), Is.EqualTo(points.Select(p => p.position)));
        }
        [Test]
        public void GatherBendsAreIndependentAndKeepBothAnchors()
        {
            var m = GatherSetup(true); m.gather.rootLift = .015f; m.gather.arrivalLift = .015f;
            var initial = GatherEvaluate().evaluatedGuides[0].points;
            m.gather.rootBend = AnimationCurve.Constant(0, 1, 0);
            var noRoot = GatherEvaluate().evaluatedGuides[0].points;
            m.gather.arrivalBend = AnimationCurve.Constant(0, 1, 0);
            var noArrival = GatherEvaluate().evaluatedGuides[0].points;
            Assert.That(Vector3.Distance(initial[10].position, noRoot[10].position), Is.GreaterThan(.001f));
            Assert.That(Vector3.Distance(noRoot[29].position, noArrival[29].position), Is.GreaterThan(.001f));
            foreach (var points in new[] { initial, noRoot, noArrival })
            { Assert.That(points[0].position, Is.EqualTo(Vector3.zero)); Assert.That(points[^1].position, Is.EqualTo(initial[^1].position)); }
        }
        [Test]
        public void GatherProtectedDistanceUsesArcLengthAndSourceCoordinates()
        {
            var m = GatherSetup(); m.gather.startInMeters = true; m.gather.bendStart = .01f;
            var baseline = GatherEvaluate().evaluatedGuides[0];
            for (int i = 0; i <= 8; i++) Assert.That(baseline.points[i].position, Is.EqualTo(groom.Groups[0].guides[0].points[i].position));
            var moved = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false,
                sourceToWorld = Matrix4x4.TRS(new Vector3(9, 3, -7), Quaternion.Euler(24, 87, 30), new Vector3(2, .7f, 1.3f)) });
            Assert.That(moved.evaluatedGuides[0].points.Select(p => p.position), Is.EqualTo(baseline.points.Select(p => p.position)));
        }
        [Test]
        public void GatherPassThroughContinuesPastPackedTie()
        {
            var m = GatherSetup(); m.gather.mode = HairGatherMode.PassThrough; m.gather.passFraction = .5f;
            var points = GatherEvaluate().evaluatedGuides[0].points; var helper = groom.FindHelper(m.helperId);
            Assert.That(Vector3.Distance(points[16].position, helper.position), Is.LessThan(.0001f));
            Assert.That(points[^1].position.y, Is.GreaterThan(points[16].position.y + .015f));
        }
        [Test]
        public void PaintedScalpGrowthExpansionAndLinkedMapInvalidateCachedRoots()
        {
            var group = groom.Groups[0]; group.guides.Clear(); group.generation.enabled = true;
            var cards = group.generation.cards; cards.source = HairPopulationSource.PaintedScalp; cards.count = 80; cards.minimumSpacing = 0;
            var map = group.FindMap(HairMapKind.GrowthArea); HairGroomCommands.FillMap(groom, map, 1);
            HairGroomCommands.SetMapStorage(groom, map, HairMapStorage.Texture, 32);
            var tile = map.texture.EnsureTile(map, 0, 0, 0, 1, 2); var vs = sourceMesh.vertices; int n = map.texture.resolution;
            for (int y = 0; y <= n; y++) for (int x = 0; x <= n; x++)
            { var bc = HairTextureMap.TexelBarycentric(x, y, n); tile.pixels[y * (n + 1) + x] = (vs[0] * bc.x + vs[1] * bc.y + vs[2] * bc.z).x < 0 ? 1 : 0; }
            map.texture.Touch(); var workspace = new HairEvaluationWorkspace();
            var half = GatherEvaluate(workspace); Assert.That(half.curves.Count, Is.GreaterThan(50));
            Assert.That(half.curves.All(c => c.points[0].position.x < .035f), Is.True);
            HairGroomCommands.FillMap(groom, map, 1); var full = GatherEvaluate(workspace);
            Assert.That(full.curves.Count(c => c.points[0].position.x > .05f), Is.GreaterThan(15));
            var other = groom.CreateGroup("Linked", HairGroupRole.Coverage); other.profile = profile; other.generation.enabled = true;
            other.generation.cards.source = HairPopulationSource.PaintedScalp; other.generation.cards.count = 40; other.generation.cards.rootMapGroupId = group.Id;
            Assert.That(GatherEvaluate(workspace).curves.Count(c => c.groupId == other.Id), Is.GreaterThan(20));
            HairGroomCommands.FillMap(groom, map, 0); Assert.That(GatherEvaluate(workspace).CardCount, Is.Zero);
        }
        [Test]
        public void GatherLodSurvivorsKeepIdenticalShapesAndTargets()
        {
            var m = GatherSetup(true); var group = groom.Groups[0]; group.modifiers.Clear(); group.guides.Clear(); group.generation.enabled = true;
            var cards = group.generation.cards; cards.source = HairPopulationSource.PaintedScalp; cards.count = 45; cards.shapeSamples = 24;
            cards.modifiers.Add(m); HairGroomCommands.FillMap(groom, group.FindMap(HairMapKind.GrowthArea), 1);
            var h = groom.FindHelper(m.helperId); h.gather.radii = new Vector2(.02f, .02f); h.gather.spacing = .001f;
            var high = GatherEvaluate(); groom.Lods[0].cardFraction = .4f; var low = GatherEvaluate();
            Assert.That(low.curves.Count, Is.InRange(1, high.curves.Count - 1));
            foreach (var curve in low.curves) Assert.That(curve.points.Select(p => p.position), Is.EqualTo(high.curves.Single(c => c.curveId == curve.curveId).points.Select(p => p.position)));
            cards.form.preserveLodCoverage = true; var covered = GatherEvaluate();
            foreach (var curve in covered.curves)
            {
                var original = low.curves.Single(c => c.curveId == curve.curveId);
                Assert.That(curve.points.Select(p => p.position), Is.EqualTo(original.points.Select(p => p.position)));
                Assert.That(curve.points[0].width, Is.EqualTo(original.points[0].width * 2).Within(1e-7f));
            }
        }
        [TestCase(HairBunPart.WrappedVolume)] [TestCase(HairBunPart.CenterTuck)] [TestCase(HairBunPart.SurroundingBraid)]
        public void BunPartsFollowGatherAndKeepHelperDataImmutable(HairBunPart part)
        {
            var h = Form(HairPopulationSource.Bun); h.type = HairHelperType.Bun;
            var parent = HairGroomCommands.AddHelper(groom, HairHelperType.Gather, Vector3.up * .3f); h.bun.gatherHelperId = parent.Id;
            groom.Groups[0].generation.cards.form.bunPart = part;
            var before = EditorJsonUtility.ToJson(groom); var a = GatherEvaluate();
            Assert.That(a.CardCount, Is.EqualTo(30)); Assert.That(a.warnings, Is.Empty); Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            parent.position += Vector3.right * .2f; var b = GatherEvaluate();
            for (int c = 0; c < a.curves.Count; c++) for (int p = 0; p < a.curves[c].points.Count; p++)
                Assert.That(Vector3.Distance(a.curves[c].points[p].position + Vector3.right * .2f, b.curves[c].points[p].position), Is.LessThan(1e-5f));
        }
        [Test]
        public void GatherConformsAroundSphereAndCardEdgesStayOutside()
        {
            var m = GatherSetup(true); m.gather.fullCardClearance = true;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); var mesh = Object.Instantiate(ball.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(ball);
            try
            {
                mesh.vertices = mesh.vertices.Select(v => v * .2f).ToArray(); mesh.RecalculateBounds(); groom.SetSource(mesh, "sphere", "TestRace", "TestScalp");
                var guide = groom.Groups[0].guides[0]; var root = Vector3.back * .1f;
                guide.root = HairSurfaceAnchor.Create("sphere", 0, 0, Vector3.right, 0, root, Vector3.back);
                for (int i = 0; i < guide.points.Count; i++) guide.points[i].position = root + Vector3.back * (i * .0003f);
                groom.FindHelper(m.helperId).position = new Vector3(0, .135f, .015f);
                profile.Configure(HairCardShape.Ribbon, .01f, .006f, 17, generateBackfaces: false); groom.Lods[0].useProfileSamples = true;
                var result = GatherEvaluate(); var curve = result.evaluatedGuides[0];
                Assert.That(curve.points.All(p => p.position.magnitude > .097f), Is.True, "No chord through the scalp.");
                Assert.That(curve.points[0].position, Is.EqualTo(root));
                using var build = HairCardMeshGenerator.Build(result);
                Assert.That(build.frameFlipCount, Is.Zero); Assert.That(build.degenerateTriangleCount, Is.Zero);
                Assert.That(WorstCardDistance(build.mesh, new HairMeshRaycaster(mesh)), Is.GreaterThan(-.0001f));
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        [Test]
        public void GatherMirroringUsesGroomPlaneAndSideFiltersProtectOppositeRoots()
        {
            var m = GatherSetup(); var helper = groom.FindHelper(m.helperId);
            groom.SetSymmetryPlane(new Vector3(.02f, 0, 0), Vector3.right);
            var copy = HairGatherEditor.Mirror(groom, helper);
            Assert.That(copy.position.x, Is.EqualTo(.04f - helper.position.x).Within(1e-6f));
            Assert.That(copy.gather, Is.Not.SameAs(helper.gather)); Assert.That(copy.Id, Is.Not.EqualTo(helper.Id));
            m.gather.rootSide = HairGatherSide.Positive; var original = groom.Groups[0].guides[0].points.Select(p => p.position).ToArray();
            Assert.That(GatherEvaluate().evaluatedGuides[0].points.Select(p => p.position), Is.EqualTo(original));
            m.gather.rootSide = HairGatherSide.Negative;
            Assert.That(GatherEvaluate().evaluatedGuides[0].points[^1].position, Is.EqualTo(helper.position));
        }
        [Test]
        public void GatherMissingHelperIsReportedAndCurveDataStaysIntact()
        {
            var m = GatherSetup(); groom.SharedHelpers.Clear();
            Assert.That(GatherEvaluate().warnings.Any(w => w.Contains("assign a Gather helper")), Is.True);
            Assert.That(GatherEvaluate().evaluatedGuides[0].points.Select(p => p.position), Is.EqualTo(groom.Groups[0].guides[0].points.Select(p => p.position)));
        }
        [Test]
        public void GatherSetupIsUndoableAndLinkedGrowthNodeOpensRealPainting()
        {
            var group = groom.Groups[0]; group.generation.enabled = true; group.generation.cards.source = HairPopulationSource.PaintedScalp;
            var stage = CreateEditingStage();
            try
            {
                Undo.ClearUndo(groom); var node = stage.Nodes.Single(n => n.Kind == HairGroomNodeKind.Population && n.Population == group.generation.cards);
                var m = HairGatherEditor.AddGather(stage, node); Assert.That(m, Is.Not.Null); Assert.That(groom.FindHelper(m.helperId), Is.Not.Null);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(groom.Groups[0].generation.cards.modifiers, Is.Empty); Assert.That(groom.SharedHelpers, Is.Empty);
                Undo.PerformRedo(); Assert.That(groom.Groups[0].generation.cards.modifiers.Count, Is.EqualTo(1));
                var linked = groom.CreateGroup("Linked roots", HairGroupRole.Coverage); linked.generation.enabled = true;
                linked.generation.cards.source = HairPopulationSource.PaintedScalp; linked.generation.cards.rootMapGroupId = groom.Groups[0].Id;
                HairGroomCommands.Commit(groom);
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.GrowthMap, linked.FindMap(HairMapKind.GrowthArea).Id));
                Assert.That(stage.ActiveGroup, Is.SameAs(groom.Groups[0]));
                Assert.That(stage.ActiveMap, Is.SameAs(groom.Groups[0].FindMap(HairMapKind.GrowthArea)));
            }
            finally { Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }
        [UnityTest]
        public IEnumerator GatherAndBunPropertiesRenderWithoutMutatingData()
        {
            var m = GatherSetup(); var group = groom.Groups[0]; group.modifiers.Clear(); group.generation.enabled = true;
            group.generation.cards.source = HairPopulationSource.PaintedScalp; group.generation.cards.modifiers.Add(m);
            var bun = HairGroomCommands.AddHelper(groom, HairHelperType.Bun, Vector3.up); bun.bun.gatherHelperId = m.helperId;
            var stage = CreateEditingStage(); var properties = ScriptableObject.CreateInstance<HairGroomWorkspace>(); var tree = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            var active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static); object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage); properties.Show(); tree.Show(); string before = EditorJsonUtility.ToJson(groom);
                foreach (float width in new[] { 360f, 900f })
                {
                    properties.position = new Rect(420, 40, width, 900); tree.position = new Rect(20, 40, 360, 900);
                    foreach (var node in stage.Nodes.Where(n => n.Kind == HairGroomNodeKind.Population || n.Helper != null || n.Modifier != null).ToArray())
                    { stage.SelectNode(node.Key); properties.Repaint(); tree.Repaint(); yield return null; yield return null; LogAssert.NoUnexpectedReceived(); }
                }
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            }
            finally { properties.Close(); tree.Close(); active.SetValue(null, previous); DestroyEditingStage(stage); }
        }
    }
}
#endif
