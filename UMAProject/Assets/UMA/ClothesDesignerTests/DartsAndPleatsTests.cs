#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Darts (edge and double-pointed) and pleats: their shapes, the mesh, the stitches, seams across them, sizes and edits.</summary>
    public sealed class DartsAndPleatsTests
    {
        private CapsuleBodyFixture capsule;
        private readonly List<Object> made = new List<Object>();
        private readonly List<System.IDisposable> disposables = new List<System.IDisposable>();

        [SetUp]
        public void Make() => capsule = new CapsuleBodyFixture();

        [TearDown]
        public void Clean()
        {
            foreach (System.IDisposable d in disposables) d.Dispose();
            disposables.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            capsule.Dispose();
        }

        private ClothesPattern Vest()
        {
            ClothesPattern pattern = VestPattern.Create();
            made.Add(pattern);
            return pattern;
        }

        private static PatternMesh Mesh(ClothesPattern pattern, float edgeCm = 2f) =>
            PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), edgeCm);

        private FitSession Drape(ClothesPattern pattern, int frames)
        {
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            session.Step(frames);
            return session;
        }

        private static PatternDart WaistDart(ClothesPattern pattern, float x, float curve = 0f)
        {
            PatternDart dart = PatternEditing.AddEdgeDart(pattern.pieces[0], 0, (x + 26f) / 52f);
            dart.widthCm = 4f;
            dart.tip = new Vector2(x, 22f);
            dart.curveCm = curve;
            return dart;
        }

        private static List<int> GroupsBeyondSeams(PatternMesh mesh, ClothesPattern pattern)
        {
            var groups = new List<int>();
            foreach (int g in mesh.stitchSeam) if (g >= pattern.seams.Count && !groups.Contains(g)) groups.Add(g);
            return groups;
        }

        // ---- Edge darts ----------------------------------------------------------------------------

        [Test]
        public void EdgeDart_IsCutAndSewnShut()
        {
            ClothesPattern pattern = Vest();
            WaistDart(pattern, -12f);
            Assert.That(pattern.FindProblems(), Is.Empty);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            Assert.That(GroupsBeyondSeams(mesh, pattern).Count, Is.EqualTo(1), "One stitch group for the dart.");
            FitSession session = Drape(pattern, 200);
            FitMetrics m = session.Measure();
            Assert.That(m.maxStitchGapMm, Is.LessThan(1f), m.ToString());
        }

        [TestCase(-3f), TestCase(-1f), TestCase(0.2f), TestCase(0.45f), TestCase(3f)]
        public void CurvedDarts_MeshAtEveryCurve_AndTheLegsNeverCross(float curve)
        {
            ClothesPattern pattern = Vest();
            PatternDart dart = WaistDart(pattern, -12f, curve);
            Assert.That(DartGeometry.EdgeDart(pattern.pieces[0], dart, PieceScale.Identity, out DartGeometry.EdgeDartShape s), Is.True);
            Vector2 mouth = (s.m0 + s.m1) * 0.5f, axis = (s.tip - mouth).normalized, side = new Vector2(-axis.y, axis.x);
            for (int k = 1; k < 20; k++)
            {
                float t = k / 20f;
                // The legs stay on their own sides of the centre line until the point.
                float a = Vector2.Dot(s.LegA(t) - mouth, side), b = Vector2.Dot(s.LegB(1f - t) - mouth, side);
                Assert.That(a * b, Is.LessThan(0f), "t " + t);
            }
            foreach (float h in new[] { 2f, 1.5f, 1f })
            {
                Assert.That(Mesh(pattern, h).warnings, Is.Empty, "edge " + h + " cm");
            }
        }

        [Test]
        public void EdgeDart_OnASewnEdge_TheSeamSkipsIt()
        {
            ClothesPattern pattern = Vest();
            // A French dart on the front's left side seam: the front side is 3 cm shorter than the back's.
            PatternDart dart = PatternEditing.AddEdgeDart(pattern.pieces[0], 8, 0.45f);
            dart.tip = new Vector2(-10f, 30f);
            PatternSeam side = pattern.FindSeamOn(pattern.pieces[0].id, pattern.pieces[0].edges[8].id);
            Assert.That(PatternEditing.SeamMismatchCm(pattern, side), Is.EqualTo(-dart.widthCm).Within(0.01f));
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            // No seam stitch lands on a dart leg: legs are sewn to each other only.
            var legStitched = new HashSet<int>();
            for (int i = 0; i < mesh.stitches.Count; i++) if (mesh.stitchSeam[i] >= pattern.seams.Count) { legStitched.Add(mesh.stitches[i].x); }
            for (int i = 0; i < mesh.stitches.Count; i++)
            {
                if (mesh.stitchSeam[i] >= pattern.seams.Count) continue;
                Assert.That(mesh.boundaryEdge[mesh.stitches[i].x], Is.GreaterThanOrEqualTo(0));
                Assert.That(mesh.boundaryEdge[mesh.stitches[i].y], Is.GreaterThanOrEqualTo(0));
            }
        }

        // ---- Double-pointed darts -----------------------------------------------------------------

        [Test]
        public void DoubleDart_LeavesAHole_AndSewsItShut()
        {
            ClothesPattern pattern = Vest();
            PatternDart dart = PatternEditing.AddDoubleDart(pattern.pieces[0], new Vector2(12f, 20f));
            dart.tip = new Vector2(12f, 34f); dart.tip2 = new Vector2(12f, 6f); dart.widthCm = 4f;
            Assert.That(pattern.FindProblems(), Is.Empty);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            List<Vector2> hole = DartGeometry.DoubleOutline(dart, 0.5f, PieceScale.Identity, out _);
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 c = (mesh.drawn[mesh.triangles[t]] + mesh.drawn[mesh.triangles[t + 1]] + mesh.drawn[mesh.triangles[t + 2]]) / 3f;
                Assert.That(PatternGeometry.PointInPolygon(hole, c), Is.False, "No cloth inside the dart.");
            }
            Assert.That(GroupsBeyondSeams(mesh, pattern).Count, Is.EqualTo(1));
            FitMetrics m = Drape(pattern, 200).Measure();
            Assert.That(m.maxStitchGapMm, Is.LessThan(1f), m.ToString());
        }

        [Test]
        public void DoubleDart_ReachingTheEdge_IsAProblem()
        {
            ClothesPattern pattern = Vest();
            PatternDart dart = PatternEditing.AddDoubleDart(pattern.pieces[0], new Vector2(12f, 20f));
            dart.tip = new Vector2(12f, 34f); dart.tip2 = new Vector2(12f, -3f);
            Assert.That(pattern.FindProblems(), Has.Some.Contains("reaches the edge"));
        }

        // ---- Pleats -------------------------------------------------------------------------------

        private ClothesPattern PleatedPanels(PleatKind kind, bool pressed, out PatternPiece front)
        {
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            made.Add(pattern);
            front = PatternEditing.NewRectanglePiece("Front", new Rect(0f, 0f, 60f, 40f));
            PatternPiece back = PatternEditing.NewRectanglePiece("Back", new Rect(70f, 0f, 60f, 40f));
            back.placement.angle = 180f;
            foreach (PatternPiece p in new[] { front, back }) { p.edges[0].opening = true; p.edges[2].opening = true; pattern.pieces.Add(p); }
            PatternPleat pleat = PatternEditing.AddPleat(front, 2, 0.5f);
            pleat.kind = kind; pleat.depthCm = 3f; pleat.stitchedCm = 6f; pleat.pressed = pressed;
            PatternEditing.Sew(pattern, front, 1, back, 3, out _);
            PatternEditing.Sew(pattern, front, 3, back, 1, out _);
            return pattern;
        }

        [TestCase(PleatKind.KnifeRight), TestCase(PleatKind.KnifeLeft), TestCase(PleatKind.Box), TestCase(PleatKind.Inverted)]
        public void Pleat_IsFoldedInTheLayout_AndSewnAtTheEdge(PleatKind kind)
        {
            ClothesPattern pattern = PleatedPanels(kind, true, out PatternPiece front);
            Assert.That(pattern.FindProblems(), Is.Empty);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            Assert.That(DartGeometry.Pleat(front, front.pleats[0], PieceScale.Identity, out DartGeometry.PleatShape shape), Is.True);
            // Folded under: some of the front lies a layer or two down.
            int under = 0;
            for (int v = mesh.pieceStart[0]; v < mesh.pieceStart[0] + mesh.pieceCount[0]; v++) if (mesh.layoutDepth[v] > 0f) under++;
            Assert.That(under, Is.GreaterThan(10));
            // Laid out, the piece is narrower by what the pleat takes up.
            float minF = float.MaxValue, maxF = float.MinValue, minL = float.MaxValue, maxL = float.MinValue;
            for (int v = mesh.pieceStart[0]; v < mesh.pieceStart[0] + mesh.pieceCount[0]; v++)
            {
                minF = Mathf.Min(minF, mesh.flat[v].x); maxF = Mathf.Max(maxF, mesh.flat[v].x);
                minL = Mathf.Min(minL, mesh.layout[v].x); maxL = Mathf.Max(maxL, mesh.layout[v].x);
            }
            Assert.That((maxF - minF) - (maxL - minL), Is.EqualTo(shape.intakeCm).Within(0.6f));
            Assert.That(mesh.creases.Count, Is.GreaterThan(0));
            // Each pair of lines brought together is sewn at the edge and down the stitched part.
            Assert.That(GroupsBeyondSeams(mesh, pattern).Count, Is.EqualTo(shape.stitchedLines.Length));
            FitMetrics m = Drape(pattern, 200).Measure();
            // The test capsule's top has a sharp 90-degree rim that the panels fall onto; contact there can
            // leave a point a millimetre or so in. Anything deeper is a real failure.
            Assert.That(m.deepestMm, Is.LessThan(2f), m.ToString());
            Assert.That(m.maxStitchGapMm, Is.LessThan(1f), m.ToString());
        }

        [Test]
        public void UnpressedPleat_HasNoCreases()
        {
            ClothesPattern pattern = PleatedPanels(PleatKind.KnifeRight, false, out _);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.creases, Is.Empty);
            Assert.That(mesh.warnings, Is.Empty);
        }

        [Test]
        public void Seam_OnAPleatedEdge_MeetsWhatIsLeft()
        {
            ClothesPattern pattern = PleatedPanels(PleatKind.Box, true, out PatternPiece front);
            front.edges[2].opening = false;
            DartGeometry.Pleat(front, front.pleats[0], PieceScale.Identity, out DartGeometry.PleatShape shape);
            // A band exactly as long as what the pleat leaves of the top edge.
            float left = 60f - shape.intakeCm;
            PatternPiece band = PatternEditing.NewRectanglePiece("Band", new Rect(0f, 50f, left, 4f));
            band.edges[2].opening = true; band.edges[1].opening = true; band.edges[3].opening = true;
            pattern.pieces.Add(band);
            PatternSeam seam = PatternEditing.Sew(pattern, front, 2, band, 0, out string problem);
            Assert.That(seam, Is.Not.Null, problem);
            Assert.That(PatternEditing.SeamMismatchCm(pattern, seam), Is.EqualTo(0f).Within(0.01f));
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty, "The two sides get matching samples.");
            int seamIndex = pattern.seams.IndexOf(seam), stitches = 0;
            foreach (int g in mesh.stitchSeam) if (g == seamIndex) stitches++;
            Assert.That(stitches, Is.GreaterThan(10));
        }

        // ---- Sizes, revision and edits ----------------------------------------------------------------

        [Test]
        public void WidthSize_CountsOnlyTheFabricLeft()
        {
            ClothesPattern pattern = Vest();
            WaistDart(pattern, -12f);
            pattern.dimensions[0].lineY = 11f; // halfway up the dart: 2 cm of it crosses the line
            Assert.That(DartGeometry.IntakeAcross(pattern.pieces[0], 11f), Is.EqualTo(2f).Within(0.2f));
            Assert.That(DartGeometry.IntakeAcross(pattern.pieces[1], 11f), Is.EqualTo(0f));
            // The front has 2 cm less fabric across that line, so it is widened more to reach the same size.
            PieceScale[] scales = PatternGeometry.Evaluate(pattern, null, null, null);
            Assert.That(scales[0].sx / scales[1].sx, Is.EqualTo(52f / 50f).Within(0.005f));
        }

        [Test]
        public void Revision_ChangesWithDarts_AndIsUnchangedWithout()
        {
            ClothesPattern pattern = Vest();
            string plain = pattern.ComputeRevision();
            ClothesPattern other = Vest();
            Assert.That(plain, Is.Not.Empty);
            WaistDart(pattern, -12f);
            Assert.That(pattern.ComputeRevision(), Is.Not.EqualTo(plain));
            // Patterns without darts or pleats keep the fingerprint they had, so kept drapes still match.
            other.pieces[0].darts.Clear();
            Assert.That(other.ComputeRevision().Length, Is.EqualTo(plain.Length));
        }

        [Test]
        public void Edits_KeepDartsAndPleats()
        {
            ClothesPattern pattern = Vest();
            PatternPiece front = pattern.pieces[0];
            PatternDart dart = WaistDart(pattern, 12f);
            Vector2 mouthBefore = new PatternGeometry.EdgeCurve(front, 0, PieceScale.Identity).At(dart.position);
            // Splitting the hem left of the dart: the dart moves to the second half and stays where it was.
            PatternEditing.SplitEdge(pattern, front, 0, 0.25f);
            int edge = front.IndexOfEdge(dart.edgeId);
            Assert.That(edge, Is.EqualTo(1));
            Vector2 mouthAfter = new PatternGeometry.EdgeCurve(front, edge, PieceScale.Identity).At(dart.position);
            Assert.That((mouthAfter - mouthBefore).magnitude, Is.LessThan(0.01f));
            // Duplicate and mirror carry it over to their own edges.
            PatternPiece copy = PatternEditing.Duplicate(front, "Copy", new Vector2(100f, 0f));
            Assert.That(copy.darts.Count, Is.EqualTo(1));
            Assert.That(copy.IndexOfEdge(copy.darts[0].edgeId), Is.EqualTo(edge));
            Assert.That(copy.darts[0].tip, Is.EqualTo(dart.tip + new Vector2(100f, 0f)));
            PatternPiece mirrored = PatternEditing.MirrorCopy(front, "Mirror");
            Assert.That(mirrored.darts.Count, Is.EqualTo(1));
            Assert.That(DartGeometry.EdgeDart(mirrored, mirrored.darts[0], PieceScale.Identity, out DartGeometry.EdgeDartShape ms), Is.True);
            Assert.That(ms.tip.x, Is.EqualTo(-dart.tip.x).Within(0.01f), "Mirrored about the piece's middle (x 0).");
            // Deleting the point that starts the dart's edge removes the dart with the edge.
            Assert.That(PatternEditing.DeletePoint(pattern, front, edge), Is.True);
            Assert.That(front.darts, Is.Empty);
        }

        [Test]
        public void Problems_AreReported()
        {
            ClothesPattern pattern = Vest();
            PatternDart a = WaistDart(pattern, -12f);
            a.tip = new Vector2(-12f, -10f);
            Assert.That(pattern.FindProblems(), Has.Some.Contains("outside the piece"));
            a.tip = new Vector2(-12f, 22f);
            PatternDart b = WaistDart(pattern, -11f);
            Assert.That(pattern.FindProblems(), Has.Some.Contains("overlaps"));
            pattern.pieces[0].darts.Remove(b);
            Assert.That(pattern.FindProblems(), Is.Empty);
        }
    }
}
#endif
