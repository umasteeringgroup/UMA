#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// Lines inside pieces (pockets sewn onto them, folds, holes), tacks, seams on part of an edge and one
    /// edge sewn to several, elastic edges, frozen pieces, layer order, grain and pressure.
    /// </summary>
    public sealed class PatternFeatureTests
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

        private static PatternPiece Front(ClothesPattern p) => p.pieces[0];
        private static PatternPiece Back(ClothesPattern p) => p.pieces[1];

        private static PatternMesh Mesh(ClothesPattern pattern, float edgeCm = 2f) =>
            PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), edgeCm);

        private FitSession Session(ClothesPattern pattern, bool cpu = true, System.Action<ClothesFit> change = null)
        {
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            change?.Invoke(fit);
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = cpu };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            return session;
        }

        /// <summary>A 12 x 14 cm patch pocket, open at the top.</summary>
        private static PatternPiece Pocket(ClothesPattern pattern)
        {
            PatternPiece pocket = PatternEditing.NewRectanglePiece("Pocket", new Rect(-150f, 0f, 12f, 14f));
            pocket.edges[2].opening = true; // the top: from (xMax, yMax) to (xMin, yMax)
            pattern.pieces.Add(pocket);
            return pocket;
        }

        // ---- Step 1: lines, pockets, holes, tacks, folds ----

        [Test]
        public void Pocket_SewsOntoLinesDrawnOnThePanel_AndTheLinesStayMeshEdges()
        {
            ClothesPattern pattern = Vest();
            PatternPiece pocket = Pocket(pattern);
            List<PatternSeam> seams = PatternEditing.AttachPiece(pattern, pocket, Front(pattern), new Vector2(-12f, 15f), out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(seams.Count, Is.EqualTo(3), "the two sides and the bottom; the top is the pocket's opening");
            Assert.That(Front(pattern).lines.Count, Is.EqualTo(3));
            Assert.That(pocket.layer, Is.EqualTo(Front(pattern).layer + 1), "the pocket lies over its panel");
            Assert.That(pattern.FindProblems(), Is.Empty);

            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            int onLine = 0;
            for (int k = 0; k < mesh.stitches.Count; k++)
            {
                int seam = mesh.stitchSeam[k];
                if (seam < 4 || seam >= pattern.seams.Count) continue;
                Vector2Int s = mesh.stitches[k];
                int pocketSide = mesh.piece[s.x] == 2 ? s.x : s.y, frontSide = pocketSide == s.x ? s.y : s.x;
                Assert.That(mesh.piece[frontSide], Is.EqualTo(0), "sewn to the front");
                Assert.That(mesh.boundaryEdge[pocketSide], Is.GreaterThanOrEqualTo(0), "from the pocket's outline");
                // The pocket's place on the front, from its own outline: the same point once sewn.
                Vector2 expected = mesh.drawn[pocketSide] - pocket.Bounds().center + new Vector2(-12f, 15f);
                Assert.That((mesh.drawn[frontSide] - expected).magnitude, Is.LessThan(0.05f), "stitch " + k);
                onLine++;
            }
            Assert.That(onLine, Is.GreaterThan(15));
        }

        [Test]
        public void Pocket_DrapesSewnShutOnTheBody()
        {
            ClothesPattern pattern = Vest();
            PatternPiece pocket = Pocket(pattern);
            PatternEditing.AttachPiece(pattern, pocket, Front(pattern), new Vector2(-12f, 15f), out _);
            FitSession session = Session(pattern);
            session.Step(300);
            FitMetrics m = session.Measure();
            Assert.That(m.maxStitchGapMm, Is.LessThan(3f), m.ToString());
            Assert.That(m.particlesInBody, Is.EqualTo(0), m.ToString());
            // The pocket ends outside the front, not through it: each of its inside points farther from the capsule's
            // axis than the front under it (the nearest front point round the body and in height). A fixed radius for
            // the front was too tight: the pocket lies flat on it, from 0.1 mm out (2026-11-23).
            Vector3[] x = session.SyncPositions();
            int outside = 0, inside = 0;
            for (int v = session.Mesh.pieceStart[2]; v < session.Mesh.pieceStart[2] + session.Mesh.pieceCount[2]; v++)
            {
                if (session.Mesh.boundaryEdge[v] >= 0) continue;
                Vector2 pocketDir = new Vector2(x[v].x, x[v].z);
                float best = float.MaxValue, frontR = 0f;
                for (int f = session.Mesh.pieceStart[0]; f < session.Mesh.pieceStart[0] + session.Mesh.pieceCount[0]; f++)
                {
                    Vector2 frontDir = new Vector2(x[f].x, x[f].z);
                    float across = (frontDir.normalized - pocketDir.normalized).magnitude * CapsuleBodyFixture.Radius, up = x[f].y - x[v].y;
                    float d = across * across + up * up;
                    if (d < best) { best = d; frontR = frontDir.magnitude; }
                }
                if (pocketDir.magnitude > frontR) outside++; else inside++;
            }
            Assert.That(outside, Is.GreaterThan(inside * 4), "pocket points outside the front: " + outside + " against " + inside);
        }

        [Test]
        public void Hole_IsCutOut_AndItsRimIsAnOpening()
        {
            ClothesPattern pattern = Vest();
            PatternLine hole = PatternEditing.AddShape(Back(pattern), PatternLineKind.Hole, new Rect(61f, 26f, 8f, 8f), 12);
            Assert.That(pattern.FindProblems(), Is.Empty);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            var center = new Vector2(65f, 30f);
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 c = (mesh.drawn[mesh.triangles[t]] + mesh.drawn[mesh.triangles[t + 1]] + mesh.drawn[mesh.triangles[t + 2]]) / 3f;
                if (mesh.piece[mesh.triangles[t]] == 1) Assert.That((c - center).magnitude, Is.GreaterThan(3.5f), "triangle " + t / 3 + " in the hole");
            }
            int rim = 0;
            for (int v = 0; v < mesh.VertexCount; v++) if (mesh.onHole[v]) rim++;
            Assert.That(rim, Is.GreaterThan(8));
            int[] tris = mesh.triangles.ToArray();
            OpeningFacing.OpeningSegments(mesh, pattern, tris, out bool[] atOpening);
            int holeOpening = 0;
            for (int v = 0; v < mesh.VertexCount; v++) if (mesh.onHole[v] && atOpening[v]) holeOpening++;
            Assert.That(holeOpening, Is.EqualTo(rim), "the facing and the mesh hide treat the hole as an opening");
        }

        [Test]
        public void Tack_IsOneStitchBetweenThePointsChosen()
        {
            ClothesPattern pattern = Vest();
            PatternEditing.AddTack(pattern, Front(pattern), new Vector2(0f, 10f), Back(pattern), new Vector2(65f, 10f), true);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.tackStitches.Count, Is.EqualTo(1));
            Vector2Int s = mesh.stitches[mesh.tackStitches[0]];
            Assert.That((mesh.drawn[s.x] - new Vector2(0f, 10f)).magnitude, Is.LessThan(0.01f));
            Assert.That((mesh.drawn[s.y] - new Vector2(65f, 10f)).magnitude, Is.LessThan(0.01f));
            Assert.That(mesh.piece[s.x], Is.EqualTo(0)); Assert.That(mesh.piece[s.y], Is.EqualTo(1));
        }

        [Test]
        public void FoldLine_RestsFoldedAtItsAngle_AndTheSeamsItMeetsStillPair()
        {
            ClothesPattern pattern = Vest();
            PatternLine fold = PatternEditing.AddLine(Front(pattern), PatternLineKind.Fold, new Vector2(-26f, 20f), new Vector2(26f, 20f));
            fold.foldAngle = 90f;
            Assert.That(pattern.FindProblems(), Is.Empty);
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty, "the side seams take the fold line's end on both sides");
            int folds = 0;
            for (int i = 0; i < mesh.creases.Count; i++) if (Mathf.Approximately(mesh.creaseAngle[i], 90f)) folds++;
            Assert.That(folds, Is.GreaterThan(20));
            // A right-angle fold: the bend across it rests at about the hypotenuse, shorter than flat.
            Assert.That(LineGeometry.FoldedDistance(Vector2.zero, Vector2.right, new Vector2(0.5f, 1f), new Vector2(0.5f, -1f), 90f), Is.EqualTo(Mathf.Sqrt(2f)).Within(1e-4f));
            Assert.That(LineGeometry.FoldedDistance(Vector2.zero, Vector2.right, new Vector2(0.5f, 1f), new Vector2(0.5f, -1f), 0f), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Lines_ThatLeaveThePiece_OrCrossEachOther_AreReported()
        {
            ClothesPattern pattern = Vest();
            PatternEditing.AddLine(Front(pattern), PatternLineKind.Seam, new Vector2(0f, 10f), new Vector2(60f, 10f));
            Assert.That(string.Join(" ", pattern.FindProblems()), Does.Contain("leaves the piece"));
            Front(pattern).lines.Clear();
            PatternEditing.AddLine(Front(pattern), PatternLineKind.Seam, new Vector2(-10f, 10f), new Vector2(10f, 10f));
            PatternEditing.AddLine(Front(pattern), PatternLineKind.Fold, new Vector2(0f, 5f), new Vector2(0f, 15f));
            Assert.That(string.Join(" ", pattern.FindProblems()), Does.Contain("crosses"));
        }

        [Test]
        public void OldPatterns_GainTheNewListsWhenMigrated()
        {
            ClothesPattern pattern = Vest();
            pattern.schemaVersion = 2;
            pattern.tacks = null; pattern.parts = null;
            Front(pattern).lines = null;
            pattern.Migrate();
            Assert.That(pattern.schemaVersion, Is.EqualTo(ClothesPattern.CurrentSchemaVersion));
            Assert.That(pattern.tacks, Is.Not.Null); Assert.That(pattern.parts, Is.Not.Null);
            Assert.That(Front(pattern).lines, Is.Not.Null);
            string before = VestPattern.Create().ComputeRevision();
            Assert.That(pattern.ComputeRevision(), Is.Not.EqualTo(string.Empty));
            Assert.That(before.Length, Is.EqualTo(16));
        }

        // ---- Step 2: part of an edge, one edge to several, elastic ----

        [Test]
        public void OneEdge_SewsToTwo_EachOnItsShare()
        {
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            made.Add(pattern);
            PatternPiece band = PatternEditing.NewRectanglePiece("Band", new Rect(0f, 0f, 40f, 10f));
            PatternPiece left = PatternEditing.NewRectanglePiece("Left", new Rect(0f, 20f, 20f, 10f));
            PatternPiece right = PatternEditing.NewRectanglePiece("Right", new Rect(30f, 20f, 20f, 10f));
            pattern.pieces.Add(band); pattern.pieces.Add(left); pattern.pieces.Add(right);
            List<PatternSeam> seams = PatternEditing.SewOneToMany(pattern, band, 2, new List<(PatternPiece, int)> { (left, 0), (right, 0) }, true, out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(seams.Count, Is.EqualTo(2));
            Assert.That(seams[0].a.start, Is.EqualTo(0.5f).Within(1e-4f)); Assert.That(seams[0].a.end, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(seams[1].a.start, Is.EqualTo(0f).Within(1e-4f)); Assert.That(seams[1].a.end, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(pattern.FreeSpan(band.id, band.edges[2].id, out _, out _), Is.False, "the band's top is sewn all along");
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            var perSeam = new int[2];
            foreach (int s in mesh.stitchSeam) if (s < 2) perSeam[s]++;
            Assert.That(perSeam[0], Is.EqualTo(perSeam[1]));
            Assert.That(perSeam[0], Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void SewingAPartlySewnEdge_UsesWhatIsStillFree()
        {
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            made.Add(pattern);
            PatternPiece a = PatternEditing.NewRectanglePiece("A", new Rect(0f, 0f, 40f, 10f));
            PatternPiece b = PatternEditing.NewRectanglePiece("B", new Rect(0f, 20f, 20f, 10f));
            PatternPiece c = PatternEditing.NewRectanglePiece("C", new Rect(30f, 20f, 10f, 10f));
            pattern.pieces.Add(a); pattern.pieces.Add(b); pattern.pieces.Add(c);
            pattern.seams.Add(new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(a.id, a.edges[2].id, 0f, 0.5f), b = new SeamSpan(b.id, b.edges[0].id), reversed = true });
            PatternSeam second = PatternEditing.Sew(pattern, a, 2, c, 0, out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(second.a.start, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(second.a.end, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(pattern.FindProblems(), Is.Empty);
            pattern.seams.Add(new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(a.id, a.edges[2].id, 0.4f, 0.6f), b = new SeamSpan(c.id, c.edges[1].id), reversed = true });
            Assert.That(string.Join(" ", pattern.FindProblems()), Does.Contain("same stretch"));
        }

        [Test]
        public void ElasticEdge_RestsShorterAndStretches()
        {
            ClothesPattern pattern = Vest();
            Front(pattern).edges[0].elastic = true;
            Front(pattern).edges[0].elasticPercent = 75f;
            FitSession session = Session(pattern);
            Assert.That(session.Model.ElasticConstraints, Is.GreaterThan(10));
            int checkedOnes = 0;
            foreach (ClothConstraint c in session.Model.Constraints)
            {
                if (c.kind != (int)ConstraintKind.Stretch || session.Mesh.boundaryEdge[c.a] != 0 || session.Mesh.boundaryEdge[c.b] != 0 || session.Mesh.piece[c.a] != 0) continue;
                float flat = (session.Mesh.flat[c.a] - session.Mesh.flat[c.b]).magnitude * 0.01f;
                Assert.That(c.rest, Is.EqualTo(flat * 0.75f).Within(1e-5f));
                Assert.That(c.compliance, Is.GreaterThanOrEqualTo(ClothModel.ElasticCompliance));
                checkedOnes++;
            }
            Assert.That(checkedOnes, Is.GreaterThan(10));
        }

        // ---- Step 3: frozen pieces ----

        [Test]
        public void FrozenPiece_StaysWhereItIs()
        {
            ClothesPattern pattern = Vest();
            FitSession session = Session(pattern, true, fit => fit.frozenPieces.Add(Back(pattern).id));
            Assert.That(session.Model.FrozenParticles, Is.EqualTo(session.Mesh.pieceCount[1]));
            Vector3[] start = (Vector3[])session.SyncPositions().Clone();
            session.Step(90);
            Vector3[] now = session.SyncPositions();
            float backMoved = 0f, frontMoved = 0f;
            for (int v = 0; v < now.Length; v++)
            {
                float d = (now[v] - start[v]).magnitude;
                if (session.Mesh.piece[v] == 1) backMoved = Mathf.Max(backMoved, d); else frontMoved = Mathf.Max(frontMoved, d);
            }
            Assert.That(backMoved, Is.EqualTo(0f));
            Assert.That(frontMoved, Is.GreaterThan(0.01f));
        }

        // ---- Step 4: layer order ----

        /// <summary>A held lower sheet and a free upper-layer sheet that starts 3 mm under it (crossed).</summary>
        private static ClothModel CrossedLayers(out Vector3[] positions, out int heldCount)
        {
            var origin = new Vector3(2f, 1f, 0f);
            var points = new List<Vector3>(); var triangles = new List<int>();
            void Sheet(int cells, Vector3 corner)
            {
                int first = points.Count, side = cells + 1;
                for (int z = 0; z <= cells; z++) for (int x = 0; x <= cells; x++) points.Add(corner + new Vector3(x * 0.01f, 0f, z * 0.01f));
                for (int z = 0; z < cells; z++)
                    for (int x = 0; x < cells; x++)
                    {
                        int a = first + z * side + x, b = a + 1, c = a + side, d = c + 1;
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
            }
            Sheet(10, origin);
            heldCount = points.Count;
            Sheet(8, origin + new Vector3(0.015f, -0.003f, 0.015f));
            positions = points.ToArray();
            var weld = new int[positions.Length];
            for (int i = 0; i < weld.Length; i++) weld[i] = i;
            ClothModel model = ClothModel.BuildFromMesh(positions, triangles.ToArray(), weld, new FabricSettings());
            var group = new int[positions.Length];
            model.Layer = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                group[i] = i < heldCount ? 0 : 1;
                model.Layer[i] = i < heldCount ? 0 : 1;
                if (i < heldCount) model.InverseMass[i] = 0f;
            }
            model.SetCollisionGroups(group);
            model.OutSign = 1; // the sheets' triangles face up: out
            return model;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HigherLayer_ThatStartsUnderneath_EndsOutside(bool gpu)
        {
            if (gpu && !ClothSolverGpu.Supported) Assert.Ignore("This machine cannot run compute shaders.");
            ClothModel model = CrossedLayers(out Vector3[] positions, out int held);
            IClothSolver solver = gpu ? new ClothSolverGpu() : new ClothSolverCpu();
            disposables.Add(solver);
            solver.Initialize(model, capsule.Body.Collider, positions);
            SimParams p = ClothModel.Params(1f, 0.005f, 0f, 1f, Vector3.zero, false, 1f);
            p.thickness = 0.002f;
            p.selfCollide = 1;
            solver.Step(p, 90);
            var now = new Vector3[positions.Length];
            solver.GetPositions(now);
            float low = float.MaxValue, high = float.MinValue;
            for (int i = held; i < now.Length; i++) { low = Mathf.Min(low, now[i].y - 1f); high = Mathf.Max(high, now[i].y - 1f); }
            Assert.That(low * 1000f, Is.GreaterThan(1f), "the upper layer's lowest point is above the lower layer (mm)");
            Assert.That(high * 1000f, Is.LessThan(6f), "and was not thrown off (mm)");
        }

        // ---- Step 7: grain and pressure ----

        [Test]
        public void Stretch_FollowsTheGrain()
        {
            var fabric = new FabricSettings { stretchCompliance = 1e-8f, crossStretchCompliance = 0.05f };
            Vector2 grain = Vector2.up;
            Assert.That(fabric.StretchAt(Vector2.up, grain), Is.EqualTo(1e-8f).Within(1e-9f));
            Assert.That(fabric.StretchAt(Vector2.right, grain), Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(fabric.StretchAt(new Vector2(1f, 1f), grain), Is.EqualTo(0.0375f).Within(1e-4f), "on the bias: as stretchy as the stretchier way by default");
            Assert.That(new FabricSettings().HasGrain, Is.False, "old fabrics stretch the same every way");
            ClothesPattern pattern = Vest();
            foreach (PatternPiece piece in pattern.pieces) piece.fabric = fabric.Clone();
            FitSession session = Session(pattern);
            float across = 0f, along = 0f;
            foreach (ClothConstraint c in session.Model.Constraints)
            {
                if (c.kind != (int)ConstraintKind.Stretch) continue;
                Vector2 d = session.Mesh.drawn[c.b] - session.Mesh.drawn[c.a];
                if (Mathf.Abs(d.x) < 1e-4f) along = Mathf.Max(along, c.compliance);
                if (Mathf.Abs(d.y) < 1e-4f) across = Mathf.Max(across, c.compliance);
            }
            Assert.That(along, Is.LessThan(1e-6f));
            Assert.That(across, Is.EqualTo(0.05f).Within(1e-5f));
        }

        [Test]
        public void Pressure_PushesThePieceOutFromTheBody()
        {
            float MeanGap(float pressure)
            {
                ClothesPattern pattern = Vest();
                Front(pattern).pressure = pressure;
                FitSession session = Session(pattern);
                session.Step(240);
                Vector3[] x = session.SyncPositions();
                double sum = 0; int count = 0;
                for (int v = 0; v < x.Length; v++)
                {
                    if (session.Mesh.piece[v] != 0 || x[v].y < CapsuleBodyFixture.Bottom + 0.1f || x[v].y > CapsuleBodyFixture.Top - 0.05f) continue;
                    sum += new Vector2(x[v].x, x[v].z).magnitude - CapsuleBodyFixture.Radius;
                    count++;
                }
                return (float)(sum / Mathf.Max(1, count));
            }
            float without = MeanGap(0f), with = MeanGap(1f);
            // Firm cotton with the vest's ease can only bulge so far: measured +1.9 mm at full pressure (2026-11-23).
            Assert.That(with, Is.GreaterThan(without + 0.001f), "mean gap " + with * 1000f + " mm with pressure, " + without * 1000f + " without");
        }
    }
}
#endif
