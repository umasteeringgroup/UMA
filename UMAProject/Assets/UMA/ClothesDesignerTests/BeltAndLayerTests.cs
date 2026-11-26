#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// A thick elastic band at the waist: pieces baked with a thickness of their own, the Elastic Waist Dress sample, and a
    /// grab limited to one layer (pull the bodice up without moving the band).
    /// </summary>
    public sealed class BeltAndLayerTests
    {
        private readonly List<Object> made = new List<Object>();
        private readonly List<FitSession> sessions = new List<FitSession>();

        [TearDown]
        public void Clean()
        {
            foreach (FitSession s in sessions) s.Dispose();
            sessions.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        /// <summary>Two band halves sewn end to end (A, B), and a thin piece sewn along A's top (C).</summary>
        private ClothesPattern Bands(float thickA, float thickB)
        {
            var b = new PatternBuilder("Bands", "test");
            PieceBuilder a = b.Piece("A").Point(-10f, 0f, "Bottom", true).Point(10f, 0f, "Right end").Point(10f, 4f, "Top").Point(-10f, 4f, "Left end", true)
                .Place(BodyPart.Torso, 0f, -20f).Thickness(thickA);
            PieceBuilder bb = b.Piece("B").Point(-10f, 0f, "Bottom", true).Point(10f, 0f, "Right end", true).Point(10f, 4f, "Top", true).Point(-10f, 4f, "Left end")
                .Place(BodyPart.Torso, 90f, -20f).Thickness(thickB);
            PieceBuilder c = b.Piece("C").Point(-10f, 0f, "Hem").Point(10f, 0f, "Right side", true).Point(10f, 8f, "Top", true).Point(-10f, 8f, "Left side", true)
                .Place(BodyPart.Torso, 0f, -12f);
            b.Sew(a, "Right end", bb, "Left end");
            b.Sew(a, "Top", c, "Hem");
            ClothesPattern pattern = b.Finish();
            made.Add(pattern);
            return pattern;
        }

        private static PieceThickness.Output BuildThickness(ClothesPattern pattern, out PatternMesh mesh, out Vector3[] positions, out List<Vector3> slot, out List<int> source)
        {
            mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
            positions = new Vector3[mesh.VertexCount];
            for (int v = 0; v < positions.Length; v++) positions[v] = new Vector3(mesh.drawn[v].x * 0.01f, mesh.drawn[v].y * 0.01f, 0f);
            int[] triangles = mesh.triangles.ToArray();
            slot = new List<Vector3>(positions);
            var slotTriangles = new List<int>(triangles);
            var uv = new List<Vector2>(mesh.uv);
            source = new List<int>();
            for (int v = 0; v < positions.Length; v++) source.Add(v);
            return PieceThickness.Build(mesh, pattern, positions, triangles, slot, slotTriangles, uv, source);
        }

        [Test]
        public void ThickPiece_IsCopiedItsThicknessOut_AndWalledRound_TheClothItselfUnchanged()
        {
            ClothesPattern pattern = Bands(4f, 0f);
            Assert.That(PieceThickness.Any(pattern), Is.True);
            PieceThickness.Output output = BuildThickness(pattern, out PatternMesh mesh, out Vector3[] positions, out List<Vector3> slot, out List<int> source);
            int aTriangles = 0;
            for (int t = 0; t < mesh.triangles.Count; t += 3) if (mesh.piece[mesh.triangles[t]] == 0) aTriangles++;
            Assert.That(output.shellTriangles, Is.EqualTo(aTriangles), "the thick piece's triangles once more");
            Assert.That(output.wallTriangles, Is.GreaterThan(0));
            Assert.That(output.pieces, Is.EqualTo(new[] { "A" }));
            for (int v = 0; v < positions.Length; v++) Assert.That(slot[v], Is.EqualTo(positions[v]), "the cloth's own vertices stay where they were draped");
            var walls = new HashSet<int>();
            foreach ((int vertex, int side) in output.walls)
            {
                walls.Add(vertex);
                Assert.That(side, Is.EqualTo(PieceThickness.WallSides + 0), "the walls take the piece's side id");
            }
            int copies = 0;
            for (int v = positions.Length; v < slot.Count; v++)
            {
                Assert.That(mesh.piece[source[v]], Is.EqualTo(0), "only the thick piece is copied");
                if (walls.Contains(v)) continue;
                copies++;
                Assert.That((slot[v] - positions[source[v]]).magnitude, Is.EqualTo(0.004f).Within(1e-5f), "4 mm out");
            }
            Assert.That(copies, Is.EqualTo(mesh.pieceCount[0]), "one copy of each of its points");
            Assert.That(PieceThickness.Any(Bands(0f, 0f)), Is.False);
        }

        [Test]
        public void OuterCopies_GiveEachPointOfAThickPieceItsCopyOnTheOutside_AndNoneOnAThinPiece()
        {
            ClothesPattern pattern = Bands(4f, 0f);
            PieceThickness.Output output = BuildThickness(pattern, out PatternMesh mesh, out Vector3[] positions, out List<Vector3> slot, out List<int> source);
            int n = positions.Length, added = slot.Count - n;
            var addedSource = new int[added];
            var offset = new float[added];
            var wall = new bool[added];
            for (int k = 0; k < added; k++) { addedSource[k] = source[n + k]; offset[k] = output.offsets[k]; }
            foreach ((int vertex, int side) in output.walls) wall[vertex - n] = true;
            int[] outer = DesignerSession.OuterCopies(n, addedSource, offset, wall);
            Assert.That(outer.Length, Is.EqualTo(n));
            for (int v = 0; v < n; v++)
            {
                if (mesh.piece[v] != 0) { Assert.That(outer[v], Is.EqualTo(-1), "a thin piece's point is drawn on the cloth"); continue; }
                Assert.That(outer[v], Is.GreaterThanOrEqualTo(0), "every point of the thick piece");
                Assert.That(wall[outer[v]], Is.False, "the outer copy, not a wall's point");
                Assert.That((slot[n + outer[v]] - positions[v]).magnitude, Is.EqualTo(0.004f).Within(1e-5f), "4 mm out");
            }
        }

        [Test]
        public void SeamLines_AreDrawnInRunsThatDoNotTurnBackOnScreen()
        {
            List<(int start, int count)> Runs(params float[] xy)
            {
                var points = new List<Vector2>();
                for (int i = 0; i + 1 < xy.Length; i += 2) points.Add(new Vector2(xy[i], xy[i + 1]));
                return DesignerSession.ScreenRuns(points);
            }
            Assert.That(Runs(0, 0, 10, 0, 20, 0, 30, 0), Is.EqualTo(new[] { (0, 4) }), "straight: one line");
            Assert.That(Runs(0, 0, 10, 0, 18, 6, 22, 14), Is.EqualTo(new[] { (0, 4) }), "turning gently: one line");
            // A seam round the body seen from the side: it runs out to the outline and comes back.
            Assert.That(Runs(0, 0, 10, 0, 20, 1, 12, 2, 2, 3), Is.EqualTo(new[] { (0, 3), (2, 3) }), "split at the turning point, which both share");
            Assert.That(Runs(0, 0, 10, 0, 20, 0, 20.3f, 0.2f, 30, 0, 40, 0), Is.EqualTo(new[] { (0, 3), (3, 3) }), "a segment under a pixel is left out");
            Assert.That(Runs(0, 0, 0.2f, 0, 0.4f, 0), Is.Empty, "nothing to draw");
            Assert.That(Runs(5, 5), Is.Empty);
        }

        [Test]
        public void ThickPieces_SewnTogether_HaveNoWallBetweenThem()
        {
            int wallsA = BuildThickness(Bands(4f, 0f), out _, out _, out _, out _).wallTriangles;
            int wallsB = BuildThickness(Bands(0f, 4f), out _, out _, out _, out _).wallTriangles;
            int both = BuildThickness(Bands(4f, 4f), out _, out _, out _, out _).wallTriangles;
            Assert.That(both, Is.LessThan(wallsA + wallsB), "the seam between the two thick pieces is inside the solid");
            Assert.That(both, Is.GreaterThan(wallsA), "but the rest of B is walled");
        }

        [Test]
        public void Mirror_KeepsThePiecesThickness()
        {
            ClothesPattern pattern = Bands(4f, 0f);
            Assert.That(PatternEditing.MirrorCopy(pattern.pieces[0], "A Mirrored").thicknessMm, Is.EqualTo(4f));
        }

        [Test]
        public void GrabOneLayer_StaysOnItsLayer_AndHoldsTheOthers()
        {
            using var capsule = new CapsuleBodyFixture();
            var b = new PatternBuilder("Layered", "test");
            PieceBuilder lower = b.Piece("Bodice").Point(-20f, 0f, "Hem").Point(20f, 0f, "Right", true).Point(20f, 10f, "Top", true).Point(-20f, 10f, "Left", true)
                .Place(BodyPart.Torso, 0f, -20f);
            PieceBuilder band = b.Piece("Band").Point(-20f, 0f, "Bottom", true).Point(20f, 0f, "Right", true).Point(20f, 4f, "Top").Point(-20f, 4f, "Left", true)
                .Place(BodyPart.Torso, 0f, -30f, 0.5f).Layer(1);
            b.Sew(band, "Top", lower, "Hem");
            ClothesPattern pattern = b.Finish();
            made.Add(pattern);
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            sessions.Add(session);
            session.Rebuild();
            session.SyncPositions();
            PatternMesh mesh = session.Mesh;
            // A bodice point on the seam to the band.
            int start = -1;
            for (int v = 0; v < mesh.VertexCount; v++) if (mesh.piece[v] == 0 && mesh.drawn[v].y - pattern.pieces[0].Bounds().yMin < 0.01f) { start = v; break; }
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int OnBand(Dictionary<int, float> picked) { int n = 0; foreach (int v in picked.Keys) if (mesh.piece[v] == 1) n++; return n; }
            Assert.That(OnBand(session.ConnectedWithin(start, 1f)), Is.GreaterThan(0), "a plain grab carries on across the seam");
            Dictionary<int, float> oneLayer = session.ConnectedWithin(start, 1f, 0);
            Assert.That(OnBand(oneLayer), Is.EqualTo(0), "limited to the bodice's layer");
            Assert.That(oneLayer.Count, Is.GreaterThan(0));
            Assert.That(session.ConnectedWithin(start, 1f, 1), Is.Empty, "the pressed point is not on that layer");
            List<int> held = session.ParticlesOffLayer(0);
            Assert.That(held.Count, Is.EqualTo(mesh.pieceCount[1]));
            foreach (int v in held) Assert.That(mesh.piece[v], Is.EqualTo(1));
        }

        /// <summary>A loose tube (a front and a back sewn at the sides) with a belt in two halves round its middle, on layer 1 and sewn to nothing else.</summary>
        private FitSession BeltOnTube(CapsuleBodyFixture capsule, bool cpu)
        {
            var b = new PatternBuilder("Belted Tube", "test");
            PieceBuilder Panel(string name, float angle) => b.Piece(name).Point(-30f, 0f, "Bottom", true).Point(30f, 0f, "Right").Point(30f, 40f, "Top", true).Point(-30f, 40f, "Left")
                .Place(BodyPart.Torso, angle, -20f);
            PieceBuilder front = Panel("Front", 0f), back = Panel("Back", 180f);
            b.Sew(front, "Right", back, "Left");
            b.Sew(front, "Left", back, "Right");
            PieceBuilder Belt(string name, float angle) => b.Piece(name).Point(-22.5f, 0f, "Bottom", true).Point(22.5f, 0f, "Right end").Point(22.5f, 5f, "Top", true).Point(-22.5f, 5f, "Left end")
                .Place(BodyPart.Torso, angle, -37f, 0.5f).Layer(1);
            PieceBuilder beltFront = Belt("Front Belt", 0f), beltBack = Belt("Back Belt", 180f);
            b.Sew(beltFront, "Right end", beltBack, "Left end");
            b.Sew(beltFront, "Left end", beltBack, "Right end");
            ClothesPattern pattern = b.Finish();
            made.Add(pattern);
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = cpu };
            sessions.Add(session);
            session.Rebuild();
            return session;
        }

        private static (float mean, float max) Gaps(FitSession session, ConstraintKind kind)
        {
            Vector3[] x = session.SyncPositions();
            double sum = 0; float max = 0f; int n = 0;
            foreach (ClothConstraint c in session.Model.Constraints)
            {
                if (c.kind != (int)kind) continue;
                float d = Vector3.Distance(x[c.a], x[c.b]);
                sum += d; max = Mathf.Max(max, d); n++;
            }
            return (n > 0 ? (float)(sum / n) : 0f, max);
        }

        [Test]
        public void FreePieces_AreHigherLayersSewnToNoOtherLayer_TacksDoNotCount()
        {
            ClothesPattern belted = SamplePatterns.Create("Belted Dress"), banded = SamplePatterns.Create("Elastic Waist Dress"), oxford = SamplePatterns.Create("Oxford Shirt");
            made.Add(belted); made.Add(banded); made.Add(oxford);
            bool[] free = ClothModel.FreePieces(belted);
            for (int p = 0; p < belted.pieces.Count; p++)
                Assert.That(free[p], Is.EqualTo(belted.pieces[p].name.EndsWith("Belt")), belted.pieces[p].name + ": the belt is free though tacked to the dress (belt loops)");
            Assert.That(belted.tacks.Count, Is.GreaterThan(0));
            foreach (bool f in ClothModel.FreePieces(banded)) Assert.That(f, Is.False, "a band sewn between bodice and skirt is not free");
            foreach (bool f in ClothModel.FreePieces(oxford)) Assert.That(f, Is.False, "a collar's leaf is sewn to its stand");
        }

        [Test]
        public void FreeBelt_IsLaidOutsideTheCloth_ItsSeamsCloseLate_AndGravityWaitsForThem()
        {
            using var capsule = new CapsuleBodyFixture();
            FitSession session = BeltOnTube(capsule, true);
            Assert.That(session.HasLateSeams, Is.True);
            Assert.That(session.Model.LateStitchCount, Is.GreaterThan(0));
            Assert.That(session.GravityStart, Is.EqualTo(FitSession.SewSeconds + FitSession.LateSewSeconds).Within(1e-4f));
            // Laid out, the belt is outside the tube at its height.
            Vector3[] laid = session.Arranged.positions;
            Vector2 c = capsule.Body.Landmarks.torsoCenter;
            float Radius(Vector3 q) => new Vector2(q.x - c.x, q.z - c.y).magnitude;
            float beltLow = float.MaxValue, beltHigh = float.MinValue, beltInner = float.MaxValue, tubeOuter = 0f;
            for (int v = 0; v < laid.Length; v++)
                if (session.Model.Free[v]) { beltLow = Mathf.Min(beltLow, laid[v].y); beltHigh = Mathf.Max(beltHigh, laid[v].y); beltInner = Mathf.Min(beltInner, Radius(laid[v])); }
            for (int v = 0; v < laid.Length; v++)
                if (!session.Model.Free[v] && laid[v].y >= beltLow - 0.01f && laid[v].y <= beltHigh + 0.01f) tubeOuter = Mathf.Max(tubeOuter, Radius(laid[v]));
            Assert.That(beltInner, Is.GreaterThan(tubeOuter), "the belt starts outside the cloth under it");
            // The tube sews; the belt's own seams stay open. (One frame more: 48 sixtieths add up to just under 0.8 s.)
            session.Step(Mathf.CeilToInt(FitSession.SewSeconds * 60f) + 1);
            Assert.That(session.Sewn, Is.True);
            Assert.That(session.AllSewn, Is.False);
            Assert.That(session.GravityScale, Is.EqualTo(0f));
            Assert.That(Gaps(session, ConstraintKind.Stitch).max * 1000f, Is.LessThan(3f), "the tube is sewn");
            Assert.That(Gaps(session, ConstraintKind.LateStitch).mean * 100f, Is.GreaterThan(2f), "the belt's seams are still open");
            // Then the belt's seams close, before gravity comes in.
            session.Step(Mathf.CeilToInt(FitSession.LateSewSeconds * 60f) + 10);
            Assert.That(session.AllSewn, Is.True);
            Assert.That(Gaps(session, ConstraintKind.LateStitch).max * 1000f, Is.LessThan(5f), "the belt is closed round the tube");
        }

        [Test]
        public void FreePieces_AreLaidOutAgainAtTheFirstLayoutsGap_TheSameLayoutAsSearchingAgain()
        {
            using var capsule = new CapsuleBodyFixture();
            FitSession session = BeltOnTube(capsule, true);
            Arrangement.Result first = Arrangement.Arrange(session.Pattern, session.Fit, session.Mesh, capsule.Body);
            float[] clearance = Arrangement.FreePieceClearance(session.Pattern, session.Fit, session.Mesh, first.positions, capsule.Body);
            Assert.That(clearance, Is.Not.Null, "the belt is moved out");
            Arrangement.Result searched = Arrangement.Arrange(session.Pattern, session.Fit, session.Mesh, capsule.Body, null, clearance);
            Assert.That(session.Arranged.gap, Is.EqualTo(first.gap));
            float most = 0f;
            for (int v = 0; v < searched.positions.Length; v++) most = Mathf.Max(most, (searched.positions[v] - session.Arranged.positions[v]).magnitude);
            Assert.That(most, Is.LessThan(1e-5f), "the same layout as searching for the gap again");
            Assert.That(session.Arranged.messages, Is.EqualTo(first.messages), "the first layout's messages");
        }

        [Test]
        public void GapSearch_StopsWhenLayingFurtherOutFreesNoMorePoints_AndKeepsTheClosestBestGap()
        {
            using var capsule = new CapsuleBodyFixture();
            var b = new PatternBuilder("Held In", "test");
            // A large negative Distance holds the panel 5 mm outside the torso at every gap: inside the clearance check.
            b.Piece("Panel").Point(-15f, 0f, "Bottom", true).Point(15f, 0f, "Right", true).Point(15f, 20f, "Top", true).Point(-15f, 20f, "Left", true)
                .Place(BodyPart.Torso, 0f, -30f, -20f);
            ClothesPattern pattern = b.Finish();
            made.Add(pattern);
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, capsule.Body.Landmarks, fit, null), fit.EdgeLengthCm());
            Arrangement.Result result = Arrangement.Arrange(pattern, fit, mesh, capsule.Body);
            Assert.That(result.gapsTried, Is.EqualTo(3), "the first gap, then two that freed no more points (all eight before)");
            Assert.That(result.gap, Is.EqualTo(Arrangement.DefaultGapM), "the smallest gap with the fewest points inside");
            Assert.That(result.messages.Exists(m => m.Contains("start inside or against the body") && m.Contains("'Panel'") && m.Contains("did not free them")), Is.True,
                string.Join(" | ", result.messages));
            Assert.That(Arrangement.Arrange(pattern, fit, mesh, capsule.Body, Arrangement.DefaultGapM).gapsTried, Is.EqualTo(1), "a fixed gap: one layout");
        }

        [Test]
        public void GpuSolver_ClosesLateSeamsLikeTheCpuSolver()
        {
            if (!ClothSolverGpu.Supported) Assert.Ignore("This machine cannot run compute shaders; the CPU solver is the one in use.");
            using var capsule = new CapsuleBodyFixture();
            FitSession cpu = BeltOnTube(capsule, true), gpu = BeltOnTube(capsule, false);
            Assert.That(gpu.Solver.IsGpu, Is.True);
            float before = Gaps(cpu, ConstraintKind.LateStitch).mean;
            // Only the late seams pull: the others held open, no gravity, no contact between pieces.
            foreach (FitSession s in new[] { cpu, gpu })
            {
                SimParams p = s.CurrentParams();
                p.closure = 1f; p.lateClosure = 0.1f; p.gravityScale = 0f; p.selfCollide = 0;
                s.Solver.Step(p, 5);
            }
            Vector3[] pc = cpu.SyncPositions(), pg = gpu.SyncPositions();
            double sum = 0;
            for (int i = 0; i < pc.Length; i++) sum += (pc[i] - pg[i]).magnitude;
            Assert.That(sum / pc.Length * 1000.0, Is.LessThan(0.01), "mean distance in mm after 5 frames");
            Assert.That(Gaps(cpu, ConstraintKind.LateStitch).mean, Is.LessThan(before * 0.95f), "the late seams pulled");
        }

        /// <summary>
        /// Drapes the belted tube (sewn, the belt tightened, then a second of gravity) and returns how many of the tube's
        /// points at the belt's height there are and how many of them end up more than 3 mm outside the belt, and the
        /// furthest out.
        /// </summary>
        private (int under, int over, float worstMm) DrapedThroughTheBelt(CapsuleBodyFixture capsule, bool cpu, bool oneWay)
        {
            bool was = ClothSolverShared.OneWayLayers;
            ClothSolverShared.OneWayLayers = oneWay;
            try
            {
                FitSession session = BeltOnTube(capsule, cpu);
                Assert.That(session.Solver.IsGpu, Is.EqualTo(!cpu));
                session.Step(Mathf.CeilToInt(session.GravityStart * 60f) + 60);
                Assert.That(session.AllSewn, Is.True);
                Vector3[] x = session.SyncPositions();
                bool[] free = session.Model.Free;
                Vector2 c = capsule.Body.Landmarks.torsoCenter;
                float Radius(Vector3 q) => new Vector2(q.x - c.x, q.z - c.y).magnitude;
                float low = float.MaxValue, high = float.MinValue;
                for (int v = 0; v < x.Length; v++) if (free[v]) { low = Mathf.Min(low, x[v].y); high = Mathf.Max(high, x[v].y); }
                int under = 0, over = 0;
                float worst = 0f;
                for (int v = 0; v < x.Length; v++)
                {
                    if (free[v] || x[v].y <= low + 0.005f || x[v].y >= high - 0.005f) continue;
                    under++;
                    // Over the belt: further from the body's axis than the belt's nearest point (2 cm apart at most, so
                    // within a fraction of a millimetre of the belt's own radius there).
                    int nearest = -1;
                    float best = float.MaxValue;
                    for (int b = 0; b < x.Length; b++) if (free[b]) { float d = (x[b] - x[v]).sqrMagnitude; if (d < best) { best = d; nearest = b; } }
                    float outside = Radius(x[v]) - Radius(x[nearest]);
                    if (outside > 0.003f) over++;
                    worst = Mathf.Max(worst, outside * 1000f);
                }
                return (under, over, worst);
            }
            finally { ClothSolverShared.OneWayLayers = was; }
        }

        /// <summary>
        /// The one-way layer pass (ClothSolverShared.OneWayLayers): a belt on a higher layer, tightened round a loose tube,
        /// keeps the gathered tube under it. Without the pass, soft cloth contact lets folds of the tube through the belt
        /// here (measured: 12 to 18 points, up to 42 mm out, and the belt slid); the second check keeps this scenario one
        /// that tests the pass.
        /// </summary>
        private void AssertTheBeltKeepsTheTubeUnderIt(bool cpu)
        {
            using var capsule = new CapsuleBodyFixture();
            (int under, int over, float worst) = DrapedThroughTheBelt(capsule, cpu, true);
            (int underOff, int overOff, float worstOff) = DrapedThroughTheBelt(capsule, cpu, false);
            string numbers = $"with the pass {over} of {under} points over the belt (worst {worst:F1} mm); without it {overOff} of {underOff} (worst {worstOff:F1} mm)";
            Assert.That(under, Is.GreaterThan(20), numbers);
            Assert.That(over, Is.LessThanOrEqualTo(Mathf.Max(2, under / 20)), "the belt keeps the tube under it; " + numbers);
            Assert.That(worst, Is.LessThan(10f), numbers);
            Assert.That(overOff, Is.GreaterThanOrEqualTo(5), "without the pass the tube gets through the belt here; " + numbers);
        }

        [Test]
        public void OneWayLayers_KeepTheClothUnderAHigherLayerUnderIt_Cpu() => AssertTheBeltKeepsTheTubeUnderIt(true);

        [Test]
        public void OneWayLayers_KeepTheClothUnderAHigherLayerUnderIt_Gpu()
        {
            if (!ClothSolverGpu.Supported) Assert.Ignore("This machine cannot run compute shaders; the CPU solver is the one in use.");
            AssertTheBeltKeepsTheTubeUnderIt(false);
        }

        [Test]
        public void BeltedDress_IsTwoLoosePanels_WithAFreeThickBelt_TackedLikeBeltLoops()
        {
            ClothesPattern dress = SamplePatterns.Create("Belted Dress");
            made.Add(dress);
            Assert.That(SamplePatterns.Category("Belted Dress"), Is.EqualTo("Dresses and Skirts"));
            Assert.That(dress.FindProblems(), Is.Empty);
            PatternPiece front = dress.pieces.Find(p => p.name == "Front");
            Assert.That(front, Is.Not.Null);
            Assert.That(front.points[0].position.y, Is.EqualTo(0f).Within(1e-3f), "one panel from the hem to the shoulder");
            foreach (string half in new[] { "Front Belt", "Back Belt" })
            {
                PatternPiece belt = dress.pieces.Find(p => p.name == half);
                Assert.That(belt, Is.Not.Null, half);
                Assert.That(belt.layer, Is.GreaterThan(front.layer));
                Assert.That(belt.thicknessMm, Is.GreaterThanOrEqualTo(4f));
                Assert.That(front.Bounds().width, Is.GreaterThan(belt.Bounds().width * 1.5f), "the panels are cut loose: the belt gathers them in");
                Assert.That(dress.seams.Exists(s => (s.a.pieceId == belt.id) != (s.b.pieceId == belt.id) && dress.FindPiece(s.a.pieceId == belt.id ? s.b.pieceId : s.a.pieceId).layer != belt.layer), Is.False, "sewn to nothing on the dress");
                Assert.That(dress.tacks.FindAll(t => t.pieceA == belt.id).Count, Is.EqualTo(2), "two belt loops a half");
            }
        }

        [Test]
        public void ElasticWaistDress_GathersTheBodiceAndSkirtIntoAThickBandOutsideThem()
        {
            ClothesPattern dress = SamplePatterns.Create("Elastic Waist Dress");
            made.Add(dress);
            Assert.That(SamplePatterns.Category("Elastic Waist Dress"), Is.EqualTo("Dresses and Skirts"));
            Assert.That(dress.FindProblems(), Is.Empty);
            float Edge(string piece, string edge)
            {
                PatternPiece p = dress.pieces.Find(x => x.name == piece);
                return PatternGeometry.EdgeLength(p, p.edges.FindIndex(e => e.name == edge), PieceScale.Identity);
            }
            foreach (string half in new[] { "Front", "Back" })
            {
                PatternPiece band = dress.pieces.Find(p => p.name == half + " Band");
                Assert.That(band, Is.Not.Null, half);
                Assert.That(band.thicknessMm, Is.GreaterThanOrEqualTo(3f), "a thick band");
                Assert.That(band.layer, Is.GreaterThan(dress.pieces.Find(p => p.name == half).layer), "kept outside the gathers");
                Assert.That(band.interfacing, Is.EqualTo(InterfacingLevel.Firm), "so it does not roll");
                // The band is cut shorter than what is sewn to it, so it gathers it in.
                Assert.That(Edge(half, "Hem"), Is.GreaterThan(Edge(half + " Band", "Top") * 1.2f), half + " bodice");
                Assert.That(Edge("Skirt " + half, "Waist"), Is.GreaterThan(Edge(half + " Band", "Bottom") * 1.5f), half + " skirt");
            }
        }
    }
}
#endif
