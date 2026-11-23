#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UMA.Editors;
using Unity.Mathematics;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The cloth solvers on a body made in code, and the weights and mesh the bake makes.</summary>
    public sealed class SolverAndBakeTests
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

        private FitSession Vest(bool cpu)
        {
            ClothesPattern pattern = VestPattern.Create();
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

        [Test]
        public void BodyCollider_NearSearchFindsTheSameNearestPointAsTheFullSearch()
        {
            // The fit report searches near each point first; it must find exactly what the full search does,
            // inside the body, on its surface, at the clearance and beyond the near box.
            using var capsule = new CapsuleBodyFixture();
            BodyCollider collider = capsule.Body.Collider;
            var random = new System.Random(7);
            int compared = 0;
            for (int i = 0; i < 4000; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = CapsuleBodyFixture.Radius + ((float)random.NextDouble() * 2f - 1f) * 0.03f;
                float y = Mathf.Lerp(CapsuleBodyFixture.Bottom + 0.05f, CapsuleBodyFixture.Top + 0.03f, (float)random.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                bool full = collider.Closest(p, out _, out Vector3 fullPoint, out float fullDistance);
                bool near = collider.ClosestNear(p, 0.01f, out _, out Vector3 nearPoint, out float nearDistance);
                Assert.AreEqual(full, near, "found at " + p.ToString("F4"));
                if (!full) continue;
                compared++;
                Assert.AreEqual((fullPoint - p).magnitude, (nearPoint - p).magnitude, 1e-6f, "nearest distance at " + p.ToString("F4"));
                Assert.AreEqual(fullDistance < 0f, nearDistance < 0f, "inside or outside at " + p.ToString("F4"));
            }
            Assert.Greater(compared, 3500);
        }

        [Test]
        public void WeightTransfer_GridFindsTheSameTriangleAsTheFullSearch()
        {
            UMASlotSkinTransfer.TargetSurface surface = capsule.Body.Surface;
            Assert.That(surface.Triangles.Length, Is.GreaterThan(256), "Large enough that the grid is used.");
            var random = new System.Random(7);
            var points = new Vector3[600];
            for (int i = 0; i < points.Length; i++)
            {
                // Round the body, inside it, and well outside its bounds (above, below, far off).
                float a = (float)random.NextDouble() * Mathf.PI * 2f, r = (float)random.NextDouble() * 0.6f, y = (float)random.NextDouble() * 2.4f - 0.2f;
                points[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            UMASlotSkinTransfer.WeightTransferResult fast = UMASlotSkinTransfer.TransferWeights(points, surface, capsule.Body.FallbackBone);
            int offset = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Assert.That(UMASlotSkinTransfer.TryFindClosestTriangle(points[i], surface, out UMASlotSkinTransfer.TriangleHit full), Is.True);
                var expected = UMASlotSkinTransfer.TransferWeights(new[] { points[i] }, surface, capsule.Body.FallbackBone); // one point: the full search
                Assert.That(fast.BonesPerVertex[i], Is.EqualTo(expected.BonesPerVertex[0]), "point " + i);
                for (int k = 0; k < expected.BonesPerVertex[0]; k++)
                {
                    Assert.That(fast.BoneWeights[offset + k].boneIndex, Is.EqualTo(expected.BoneWeights[k].boneIndex), "point " + i);
                    Assert.That(fast.BoneWeights[offset + k].weight, Is.EqualTo(expected.BoneWeights[k].weight), "point " + i);
                }
                offset += fast.BonesPerVertex[i];
            }
        }

        [Test]
        public void Capsule_FacesOut()
        {
            BodyCollider c = capsule.Body.Collider;
            int outward = 0;
            for (int t = 0; t < c.TriangleCount; t++)
            {
                Vector3 center = (c.A[t] + c.B[t] + c.C[t]) / 3f;
                if (center.y >= CapsuleBodyFixture.Top - 1e-4f) { if (c.N[t].y > 0f) outward++; continue; }
                if (Vector3.Dot(c.N[t], new Vector3(center.x, 0f, center.z)) > 0f) outward++;
            }
            Assert.That(outward, Is.EqualTo(c.TriangleCount));
        }

        [Test]
        public void Vest_SewsShutAndStaysOutOfTheBody_CpuSolver()
        {
            FitSession session = Vest(true);
            Assert.That(session.Solver.IsGpu, Is.False);
            session.Step(200);
            FitMetrics m = session.Measure();
            Assert.That(m.maxStitchGapMm, Is.LessThan(0.5f), m.ToString());
            // The capsule's top has a sharp 90-degree rim; contact there may leave a point a fraction of a
            // millimetre in. Anything deeper is a real failure.
            Assert.That(m.deepestMm, Is.LessThan(1f), m.ToString());
            Assert.That(m.meanStretchPercent, Is.LessThan(3f), m.ToString());
        }

        [Test]
        public void CpuSolver_IsDeterministic()
        {
            FitSession a = Vest(true), b = Vest(true);
            a.Step(60); b.Step(60);
            Vector3[] pa = a.SyncPositions(), pb = b.SyncPositions();
            Assert.That(pa.Length, Is.EqualTo(pb.Length));
            for (int i = 0; i < pa.Length; i++) Assert.That(pa[i], Is.EqualTo(pb[i]));
        }

        [Test]
        public void GpuSolver_AgreesWithTheCpuSolver()
        {
            if (!ClothSolverGpu.Supported) Assert.Ignore("This machine cannot run compute shaders; the CPU solver is the one in use.");
            FitSession cpu = Vest(true), gpu = Vest(false);
            Assert.That(gpu.Solver.IsGpu, Is.True);
            // Before the cloth touches the body the two must agree to rounding: they are one algorithm.
            // Once it is in contact, rounding differences grow (cloth contact is chaotic), so later frames
            // are compared by the fit metrics instead.
            cpu.Step(5); gpu.Step(5);
            Vector3[] pc = cpu.SyncPositions(), pg = gpu.SyncPositions();
            double sum = 0;
            for (int i = 0; i < pc.Length; i++) sum += (pc[i] - pg[i]).magnitude;
            Assert.That(sum / pc.Length * 1000.0, Is.LessThan(0.01), "mean distance in mm after 5 frames");
            cpu.Step(195); gpu.Step(195);
            FitMetrics mc = cpu.Measure(), mg = gpu.Measure();
            Assert.That(mg.maxStitchGapMm, Is.LessThan(0.5f), mg.ToString());
            Assert.That(Mathf.Abs(mg.meanStretchPercent - mc.meanStretchPercent), Is.LessThan(1f), mc + " / " + mg);
        }

        [Test]
        public void FullPin_HoldsItsParticle()
        {
            FitSession session = Vest(true);
            session.Step(120);
            Vector3[] p = session.SyncPositions();
            var pin = new SolverPin { particle = 5, target = p[5] + new Vector3(0f, 0.02f, 0f), weight = 1f };
            session.Solver.SetPins(new[] { pin });
            session.Step(30);
            Assert.That(Vector3.Distance(session.SyncPositions()[5], pin.target), Is.LessThan(0.0001f));
        }

        [Test]
        public void Restore_PutsTheClothBackExactly()
        {
            FitSession session = Vest(true);
            session.Step(60);
            Vector3[] kept = (Vector3[])session.SyncPositions().Clone();
            session.Step(30);
            session.Restore(kept, session.SimTime);
            Vector3[] now = session.SyncPositions();
            for (int i = 0; i < kept.Length; i++) Assert.That(now[i], Is.EqualTo(kept[i]));
        }

        [Test]
        public void KeptDrape_IsRestoredOnRebuild_AndDroppedWhenThePatternChanges()
        {
            FitSession session = Vest(true);
            session.Step(120);
            session.KeepDrape();
            Vector3[] kept = (Vector3[])session.Fit.drape.positions.Clone();
            session.Rebuild();
            Assert.That(session.Sewn, Is.True);
            Vector3[] restored = session.SyncPositions();
            for (int i = 0; i < kept.Length; i++) Assert.That(restored[i], Is.EqualTo(kept[i]));
            session.Pattern.pieces[0].points[0].position += new Vector2(-2f, 0f);
            session.Rebuild();
            Assert.That(session.Sewn, Is.False);
            Assert.That(session.Messages, Has.Some.Contains("older version"));
        }

        [Test]
        public void Weights_FollowTheBodyAndSumToOne()
        {
            var points = new[] { new Vector3(CapsuleBodyFixture.Radius + 0.01f, 0.75f, 0f), new Vector3(0f, 1.15f, CapsuleBodyFixture.Radius + 0.01f), new Vector3(-CapsuleBodyFixture.Radius - 0.01f, 1.55f, 0f) };
            UMASlotSkinTransfer.WeightTransferResult w = UMASlotSkinTransfer.TransferWeights(points, capsule.Body.Surface, 1);
            int offset = 0;
            float[] upperExpected = { (0.75f - 0.7f) / 0.9f, (1.15f - 0.7f) / 0.9f, (1.55f - 0.7f) / 0.9f };
            for (int v = 0; v < points.Length; v++)
            {
                float sum = 0f, upper = 0f;
                for (int k = 0; k < w.BonesPerVertex[v]; k++) { sum += w.BoneWeights[offset + k].weight; if (w.BoneWeights[offset + k].boneIndex == 1) upper += w.BoneWeights[offset + k].weight; }
                offset += w.BonesPerVertex[v];
                Assert.That(sum, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(upper, Is.EqualTo(upperExpected[v]).Within(0.05f));
            }
            Assert.That(w.FallbackVertices, Is.EqualTo(0));
        }

        [Test]
        public void WeldGroups_JoinEveryStitchedCopy()
        {
            FitSession session = Vest(true);
            int[] group = GarmentBaker.WeldGroups(session.Mesh.VertexCount, session.Mesh.stitches);
            foreach (Vector2Int s in session.Mesh.stitches) Assert.That(group[s.x], Is.EqualTo(group[s.y]));
        }

        [Test]
        public void SlotMesh_SeamCopiesShareNormals_AndTheShapeComesBack()
        {
            FitSession session = Vest(true);
            session.Step(200);
            Vector3[] p = (Vector3[])session.SyncPositions().Clone();
            int[] group = GarmentBaker.WeldGroups(p.Length, session.Mesh.stitches);
            var sum = new Dictionary<int, Vector3>(); var count = new Dictionary<int, int>();
            for (int v = 0; v < p.Length; v++) { sum[group[v]] = (sum.TryGetValue(group[v], out Vector3 s) ? s : Vector3.zero) + p[v]; count[group[v]] = (count.TryGetValue(group[v], out int c) ? c : 0) + 1; }
            for (int v = 0; v < p.Length; v++) p[v] = sum[group[v]] / count[group[v]];
            UMASlotSkinTransfer.WeightTransferResult w = UMASlotSkinTransfer.TransferWeights(p, capsule.Body.Surface, 1);
            var geometry = new UMASlotSkinTransfer.SourceGeometry { TargetLocalVertices = p, Triangles = session.Mesh.triangles.ToArray() };
            Mesh slot = UMASlotSkinTransfer.BuildSlotMesh(geometry, capsule.Body.Skin, w, new UMASlotSkinTransfer.SlotMeshOptions { SlotName = "Test", RecalculateMissingNormals = true });
            made.Add(slot);
            Vector3[] n = slot.normals;
            foreach (Vector2Int s in session.Mesh.stitches) Assert.That(Vector3.Angle(n[s.x], n[s.y]), Is.LessThan(0.01f));
            // The capsule is in its bind pose, so bind space is the same space.
            Vector3[] bind = slot.vertices;
            for (int v = 0; v < p.Length; v++) Assert.That((bind[v] - p[v]).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void Resample_CarriesTheDrapeToAnotherMeshLevel()
        {
            FitSession session = Vest(true);
            session.Step(200);
            PatternMesh coarse = session.Mesh;
            Vector3[] drape = (Vector3[])session.SyncPositions().Clone();
            PatternMesh fine = PatternMesher.Build(session.Pattern, coarse.scales, 1.5f);
            Vector3[] carried = DesignerSession.Resample(coarse, drape, fine);
            int[] group = GarmentBaker.WeldGroups(fine.VertexCount, fine.stitches);
            float worstGap = 0f;
            foreach (Vector2Int s in fine.stitches) worstGap = Mathf.Max(worstGap, Vector3.Distance(carried[s.x], carried[s.y]));
            Assert.That(worstGap, Is.LessThan(0.01f), "carried seams start nearly closed");
        }
    }
}
#endif
