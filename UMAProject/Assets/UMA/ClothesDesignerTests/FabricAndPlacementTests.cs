#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Fabric per piece (presets, the solver model, taking up a change without losing the drape) and placing pieces round the body.</summary>
    public sealed class FabricAndPlacementTests
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

        private FitSession Vest(bool cpu, System.Action<ClothesPattern> change = null)
        {
            ClothesPattern pattern = VestPattern.Create();
            change?.Invoke(pattern);
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(pattern); made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = cpu };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            return session;
        }

        // ---- Presets and the artist's values ----

        [Test]
        public void NewPiece_IsCotton_AndCottonKeepsTheOldDefaults()
        {
            var piece = new PatternPiece();
            Assert.That(piece.fabric.preset, Is.EqualTo("Cotton"));
            Assert.That(FabricPresets.IndexOf(piece.fabric), Is.EqualTo(0));
            // The values every fit used before fabric moved to the pieces, so old patterns drape the same.
            Assert.That(piece.fabric.stretchCompliance, Is.EqualTo(1e-8f));
            Assert.That(piece.fabric.bendCompliance, Is.EqualTo(2e-5f));
            Assert.That(piece.fabric.density, Is.EqualTo(0.2f));
            Assert.That(piece.fabric.friction, Is.EqualTo(0.35f));
        }

        [Test]
        public void Presets_AreCopies_AndAChangedValueIsCustom()
        {
            FabricSettings elastic = FabricPresets.Create("Elastic");
            elastic.friction = 0.01f;
            Assert.That(FabricPresets.Find("Elastic").Settings.friction, Is.Not.EqualTo(0.01f), "Changing a piece's fabric must not change the preset.");
            Assert.That(FabricPresets.IndexOf(elastic), Is.EqualTo(FabricPresets.All.Count), "Custom is the last entry.");
            Assert.That(FabricPresets.Names()[FabricPresets.All.Count], Is.EqualTo(FabricPresets.Custom));
        }

        [Test]
        public void Sliders_RoundTrip()
        {
            var f = new FabricSettings();
            foreach (float v in new[] { 0.1f, 0.5f, 0.9f })
            {
                f.Stretch = v; Assert.That(f.Stretch, Is.EqualTo(v).Within(1e-4f));
                f.Softness = v; Assert.That(f.Softness, Is.EqualTo(v).Within(1e-4f));
            }
            f.Stretch = 0f;
            Assert.That(f.stretchCompliance, Is.LessThan(1e-6f), "Stretch 0 is as firm as cotton.");
            f.GramsPerSquareMetre = 450f;
            Assert.That(f.density, Is.EqualTo(0.45f).Within(1e-6f));
        }

        [Test]
        public void Pattern_GivesOldPiecesCotton_AndCopiesKeepTheFabric()
        {
            ClothesPattern pattern = VestPattern.Create();
            made.Add(pattern);
            pattern.pieces[0].fabric = null;
            pattern.Migrate();
            Assert.That(pattern.pieces[0].fabric.preset, Is.EqualTo("Cotton"));

            pattern.pieces[1].fabric = FabricPresets.Create("Denim");
            PatternPiece mirrored = PatternEditing.MirrorCopy(pattern.pieces[1], "Mirror");
            PatternPiece duplicate = PatternEditing.Duplicate(pattern.pieces[1], "Copy", Vector2.zero);
            Assert.That(mirrored.fabric.preset, Is.EqualTo("Denim"));
            Assert.That(duplicate.fabric.preset, Is.EqualTo("Denim"));
            mirrored.fabric.friction = 0f;
            Assert.That(pattern.pieces[1].fabric.friction, Is.Not.EqualTo(0f), "A copy's fabric is its own.");
        }

        // ---- The solver model ----

        [Test]
        public void Model_TakesEachPiecesOwnFabric()
        {
            FitSession session = Vest(true, p =>
            {
                p.pieces[0].fabric = FabricPresets.Create("Elastic");
                p.pieces[1].fabric = FabricPresets.Create("Denim");
            });
            PatternMesh mesh = session.Mesh;
            ClothModel model = session.Model;
            FabricSettings elastic = FabricPresets.Create("Elastic"), denim = FabricPresets.Create("Denim");
            int checkedConstraints = 0;
            foreach (ClothConstraint c in model.Constraints)
            {
                if (c.kind == (int)ConstraintKind.Stitch) { Assert.That(c.compliance, Is.EqualTo(0f)); continue; }
                Assert.That(mesh.piece[c.a], Is.EqualTo(mesh.piece[c.b]), "Stretch and bend stay inside a piece.");
                FabricSettings f = mesh.piece[c.a] == 0 ? elastic : denim;
                Assert.That(c.compliance, Is.EqualTo(c.kind == (int)ConstraintKind.Stretch ? f.stretchCompliance : Mathf.Max(f.bendCompliance, ClothModel.BendFloor(mesh.edgeLengthCm))));
                checkedConstraints++;
            }
            Assert.That(checkedConstraints, Is.GreaterThan(100));
            double massFront = 0, massBack = 0;
            for (int v = 0; v < model.ParticleCount; v++)
            {
                Assert.That(model.Friction[v], Is.EqualTo(mesh.piece[v] == 0 ? elastic.friction : denim.friction));
                if (mesh.piece[v] == 0) massFront += 1.0 / model.InverseMass[v]; else massBack += 1.0 / model.InverseMass[v];
            }
            // The two panels are nearly the same area; their masses follow their weights (0.3 and 0.45 kg/m²).
            Assert.That(massBack / massFront, Is.EqualTo(denim.density / elastic.density).Within(0.15));
        }

        [Test]
        public void StretchyFabric_StretchesMoreThanCotton()
        {
            FitSession cotton = Vest(true);
            FitSession jersey = Vest(true, p => { foreach (PatternPiece piece in p.pieces) piece.fabric = FabricPresets.Create("Spandex"); });
            cotton.Step(200); jersey.Step(200);
            FitMetrics a = cotton.Measure(), b = jersey.Measure();
            Assert.That(b.meanStretchPercent, Is.GreaterThan(a.meanStretchPercent * 2f), "cotton " + a + " / spandex " + b);
            Assert.That(b.maxStitchGapMm, Is.LessThan(1f), b.ToString());
        }

        [Test]
        public void MixedFabrics_GpuMatchesCpuBeforeContact()
        {
            if (!ClothSolverGpu.Supported) Assert.Ignore("No compute shaders on this machine.");
            System.Action<ClothesPattern> mix = p => { p.pieces[0].fabric = FabricPresets.Create("Elastic"); p.pieces[1].fabric = FabricPresets.Create("Silk"); };
            FitSession gpu = Vest(false, mix), cpu = Vest(true, mix);
            Assert.That(gpu.Solver.IsGpu, Is.True);
            gpu.Step(5); cpu.Step(5);
            Vector3[] pg = gpu.SyncPositions(), pc = cpu.SyncPositions();
            float worst = 0f;
            for (int i = 0; i < pg.Length; i++) worst = Mathf.Max(worst, (pg[i] - pc[i]).magnitude);
            Assert.That(worst, Is.LessThan(0.0001f), "GPU and CPU differ by " + worst * 1000f + " mm");
        }

        [Test]
        public void ApplyFabric_KeepsTheDrape()
        {
            FitSession session = Vest(true);
            session.Step(120);
            Vector3[] before = (Vector3[])session.SyncPositions().Clone();
            float time = session.SimTime;
            foreach (PatternPiece piece in session.Pattern.pieces) piece.fabric = FabricPresets.Create("Silk");
            session.ApplyFabric();
            Vector3[] after = session.SyncPositions();
            for (int i = 0; i < before.Length; i++) Assert.That(after[i], Is.EqualTo(before[i]));
            Assert.That(session.SimTime, Is.EqualTo(time));
            float silkBend = FabricPresets.Create("Silk").bendCompliance;
            Assert.That(System.Array.Exists(session.Model.Constraints, c => c.kind == (int)ConstraintKind.Bend && c.compliance == silkBend), Is.True, "The model has the new fabric.");
            Assert.That(System.Array.Exists(session.Model.Constraints, c => c.kind == (int)ConstraintKind.Bend && c.compliance == 2e-5f), Is.False, "No cotton left.");
        }

        // ---- Placement ----

        [Test]
        public void Placement_Distance_MovesOnlyThatPieceOut()
        {
            FitSession near = Vest(true);
            FitSession far = Vest(true, p => p.pieces[0].placement.distanceCm = 5f);
            Vector3[] a = near.Arranged.positions, b = far.Arranged.positions;
            double frontOut = 0, backOut = 0; int front = 0, back = 0;
            for (int v = 0; v < a.Length; v++)
            {
                float ra = new Vector2(a[v].x, a[v].z).magnitude, rb = new Vector2(b[v].x, b[v].z).magnitude;
                if (near.Mesh.piece[v] == 0) { frontOut += rb - ra; front++; } else { backOut += rb - ra; back++; }
            }
            // The layout moves every piece out together when any point would start in the body, so the
            // shared gap may differ between the two; the front moves 5 cm more than that.
            double shared = far.Arranged.gap - near.Arranged.gap;
            Assert.That(frontOut / front, Is.EqualTo(0.05 + shared).Within(0.005), "The front starts 5 cm farther out than the rest.");
            Assert.That(backOut / back, Is.EqualTo(shared).Within(0.002), "The back only moves with the shared gap.");
        }

        [Test]
        public void Placement_AngleAndHeight_MoveThePiece()
        {
            FitSession session = Vest(true);
            FitSession turned = Vest(true, p => { p.pieces[0].placement.angle = 30f; p.pieces[0].placement.topAboveShoulderCm = 10f; });
            Vector3 centerA = Vector3.zero, centerB = Vector3.zero; int n = 0;
            for (int v = 0; v < session.Mesh.VertexCount; v++)
            {
                if (session.Mesh.piece[v] != 0) continue;
                centerA += session.Arranged.positions[v]; centerB += turned.Arranged.positions[v]; n++;
            }
            centerA /= n; centerB /= n;
            float angleA = Mathf.Atan2(-centerA.x, centerA.z) * Mathf.Rad2Deg, angleB = Mathf.Atan2(-centerB.x, centerB.z) * Mathf.Rad2Deg;
            Assert.That(Mathf.DeltaAngle(angleA, angleB), Is.EqualTo(30f).Within(3f), "Faces 30 turns the piece towards the body's left.");
            Assert.That(centerB.y - centerA.y, Is.EqualTo(0.06f).Within(0.002f), "6 cm higher.");
        }

        [Test]
        public void Placement_IsPartOfTheRevision_ButFabricIsNot()
        {
            ClothesPattern pattern = VestPattern.Create();
            made.Add(pattern);
            string revision = pattern.ComputeRevision();
            pattern.pieces[0].fabric = FabricPresets.Create("Silk");
            Assert.That(pattern.ComputeRevision(), Is.EqualTo(revision), "A new fabric keeps a kept drape usable.");
            pattern.pieces[0].placement.distanceCm = 2f;
            Assert.That(pattern.ComputeRevision(), Is.Not.EqualTo(revision), "A new placement makes a kept drape out of date.");
        }
    }
}
#endif
