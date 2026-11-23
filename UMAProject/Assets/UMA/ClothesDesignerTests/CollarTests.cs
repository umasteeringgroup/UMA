#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Per-piece mesh detail, interfacing and folded seams: what a shirt collar is made with.</summary>
    public sealed class CollarTests
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

        private ClothesPattern Vest(System.Action<ClothesPattern> change = null)
        {
            ClothesPattern p = VestPattern.Create();
            made.Add(p);
            change?.Invoke(p);
            return p;
        }

        private static PatternMesh Mesh(ClothesPattern p) => PatternMesher.Build(p, PatternGeometry.Evaluate(p, null, null, null), 2f);

        private static int VerticesOf(PatternMesh m, int piece) => m.pieceCount[piece];

        [Test]
        public void MeshDetail_MeshesOnePieceFiner_AndItsSeamsStillPair()
        {
            PatternMesh plain = Mesh(Vest());
            PatternMesh detailed = Mesh(Vest(p => p.pieces[0].meshDetail = 2f));
            // Twice as fine: about four times the points on that piece, the other unchanged.
            Assert.That(VerticesOf(detailed, 0), Is.GreaterThan(VerticesOf(plain, 0) * 3));
            Assert.That(VerticesOf(detailed, 1), Is.EqualTo(VerticesOf(plain, 1)).Within(VerticesOf(plain, 1) * 0.35f), "the back keeps its own level except along the seams");
            Assert.That(detailed.EdgeCmOf(0), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(detailed.EdgeCmOf(1), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(detailed.warnings, Is.Empty);
            var sewn = new HashSet<int>(detailed.stitchSeam);
            Assert.That(sewn.Count, Is.EqualTo(4), "every seam pairs one to one");
        }

        [Test]
        public void MeshDetail_IsKeptInRange_AndOldPatternsGetOne()
        {
            var piece = new PatternPiece { meshDetail = 0f };
            Assert.That(PatternPiece.DetailOf(piece), Is.EqualTo(1f));
            piece.meshDetail = 9f;
            Assert.That(PatternPiece.DetailOf(piece), Is.EqualTo(4f));
            ClothesPattern p = Vest(v => v.pieces[0].meshDetail = 0f);
            p.Migrate();
            Assert.That(p.pieces[0].meshDetail, Is.EqualTo(1f));
        }

        [Test]
        public void Revision_ChangesWithDetailInterfacingAndFolds_AndNotOtherwise()
        {
            ClothesPattern p = Vest();
            string before = p.ComputeRevision();
            Assert.That(Vest().ComputeRevision(), Is.Not.EqualTo(before), "ids differ between patterns");
            Assert.That(p.ComputeRevision(), Is.EqualTo(before));
            p.pieces[0].meshDetail = 2f;
            string detail = p.ComputeRevision();
            Assert.That(detail, Is.Not.EqualTo(before));
            p.pieces[0].interfacing = InterfacingLevel.Firm;
            string interfaced = p.ComputeRevision();
            Assert.That(interfaced, Is.Not.EqualTo(detail));
            p.seams[0].folded = true;
            Assert.That(p.ComputeRevision(), Is.Not.EqualTo(interfaced));
        }

        [Test]
        public void Interfacing_AddsStiffeningOnlyToItsPiece_AcrossItsHeight()
        {
            ClothesPattern p = Vest();
            PatternMesh mesh = Mesh(p);
            var none = ClothModel.Build(mesh, null, null, new[] { InterfacingLevel.None, InterfacingLevel.None });
            var firm = ClothModel.Build(mesh, null, null, new[] { InterfacingLevel.Firm, InterfacingLevel.None });
            Assert.That(none.InterfacingConstraints, Is.EqualTo(0));
            Assert.That(firm.InterfacingConstraints, Is.GreaterThan(VerticesOf(mesh, 0)));
            int across = 0, along = 0;
            foreach (ClothConstraint c in firm.Constraints)
            {
                if (c.kind != (int)ConstraintKind.Bend || Mathf.Abs(c.compliance - ClothModel.FirmInterfacingCompliance) > 1e-9f) continue;
                Assert.That(mesh.piece[c.a], Is.EqualTo(0));
                Assert.That(mesh.piece[c.b], Is.EqualTo(0));
                Vector2 d = mesh.flat[c.b] - mesh.flat[c.a];
                if (Mathf.Abs(d.y) >= Mathf.Abs(d.x)) along++; else across++;
            }
            Assert.That(along, Is.GreaterThan(across * 3), "mostly up and down the piece as drawn");
        }

        [Test]
        public void FoldedSeam_HoldsTheTurnAsLaidOut()
        {
            // A band with a leaf turned down over it, both laid round the capsule.
            using var capsule = new CapsuleBodyFixture();
            var b = new PatternBuilder("Folded", "test");
            PieceBuilder band = b.Piece("Band").Point(-20f, 0f, "Bottom", true).Point(20f, 0f, "Right", true).Point(20f, 4f, "Top").Point(-20f, 4f, "Left", true)
                .Place(BodyPart.Torso, 0f, -10f).Stiffen(InterfacingLevel.Firm, 2f);
            PieceBuilder leaf = b.Piece("Leaf").Point(-21f, -5f, "Edge", true).Point(21f, -5f, "Right", true).Point(20f, 0f, "Top").Point(-20f, 0f, "Left", true)
                .Place(BodyPart.Torso, 0f, -10f, 1f).Stiffen(InterfacingLevel.Light, 2f);
            PatternSeam seam = b.SewFolded(band, "Top", leaf, "Top");
            Assert.That(seam.folded, Is.True);
            Assert.That(seam.reversed, Is.False, "a turned piece runs the same way as the edge it turns over");
            ClothesPattern pattern = b.Finish();
            made.Add(pattern);
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            sessions.Add(session);
            session.Rebuild();
            Assert.That(session.Mesh.foldedStitches.Count, Is.EqualTo(session.Mesh.stitches.Count));
            Assert.That(session.Model.FoldConstraints, Is.GreaterThan(session.Mesh.stitches.Count));
            Assert.That(session.Model.InterfacingConstraints, Is.GreaterThan(0));
            // Laid out the leaf hangs outside the band; after sewing it still does, the seam closed at the top.
            session.Step(150);
            Vector3[] p = session.SyncPositions();
            PatternMesh mesh = session.Mesh;
            float bandTop = float.MinValue; double bandR = 0, leafR = 0; int nb = 0, nl = 0, leafBelow = 0;
            Vector2 c = capsule.Body.Landmarks.torsoCenter;
            float Radial(Vector3 q) => new Vector2(q.x - c.x, q.z - c.y).magnitude;
            for (int v = 0; v < mesh.VertexCount; v++) if (mesh.piece[v] == 0) { bandTop = Mathf.Max(bandTop, p[v].y); bandR += Radial(p[v]); nb++; }
            for (int v = 0; v < mesh.VertexCount; v++) if (mesh.piece[v] == 1) { leafR += Radial(p[v]); nl++; if (p[v].y < bandTop + 0.002f) leafBelow++; }
            Assert.That(session.Measure().maxStitchGapMm, Is.LessThan(2f), session.Measure().ToString());
            Assert.That(leafBelow, Is.GreaterThan(nl * 0.9f), "the leaf stays turned down");
            Assert.That(leafR / nl, Is.GreaterThan(bandR / nb), "outside the band");
        }

        [Test]
        public void OxfordShirt_HasATwoPartCollar_StandFirmLeafLightAndFolded()
        {
            ClothesPattern shirt = SamplePatterns.Create("Oxford Shirt");
            made.Add(shirt);
            PatternPiece stand = shirt.pieces.Find(p => p.name == "Collar Stand"), leaf = shirt.pieces.Find(p => p.name == "Collar");
            Assert.That(stand, Is.Not.Null);
            Assert.That(leaf, Is.Not.Null);
            Assert.That(stand.interfacing, Is.EqualTo(InterfacingLevel.Firm));
            Assert.That(leaf.interfacing, Is.EqualTo(InterfacingLevel.Light));
            Assert.That(PatternPiece.DetailOf(stand), Is.GreaterThan(1f));
            PatternSeam fold = shirt.seams.Find(s => s.folded);
            Assert.That(fold, Is.Not.Null);
            Assert.That(new[] { fold.a.pieceId, fold.b.pieceId }, Is.EquivalentTo(new[] { stand.id, leaf.id }));
            // The band is cut as an arc: its top edge shorter than its neck edge, so it hugs the neck.
            float neck = PatternGeometry.EdgeLength(stand, 0, PieceScale.Identity), top = PatternGeometry.EdgeLength(stand, 2, PieceScale.Identity);
            Assert.That(top, Is.LessThan(neck * 0.99f));
            Assert.That(shirt.FindProblems(), Is.Empty);
        }
    }
}
#endif
