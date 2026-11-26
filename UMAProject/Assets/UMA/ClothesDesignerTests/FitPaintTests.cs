#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The Fit Brush: paint kept on the pattern, built into the cloth, and what each kind does to it.</summary>
    public sealed class FitPaintTests
    {
        private CapsuleBodyFixture capsule;
        private readonly List<Object> made = new List<Object>();
        private readonly List<FitSession> sessions = new List<FitSession>();

        [SetUp]
        public void Make() => capsule = new CapsuleBodyFixture();

        [TearDown]
        public void Clean()
        {
            foreach (FitSession s in sessions) s.Dispose();
            sessions.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            capsule.Dispose();
        }

        private FitSession Vest(bool cpu, MeshLevel level = MeshLevel.Draft)
        {
            ClothesPattern pattern = VestPattern.Create();
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(pattern); made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = level;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = cpu };
            sessions.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            return session;
        }

        /// <summary>The middle of the front piece, away from its seams.</summary>
        private static bool FrontMiddle(FitSession s, int i)
        {
            if (s.Mesh.piece[i] != 0) return false;
            Rect b = s.Pattern.pieces[0].Bounds();
            Vector2 d = s.Mesh.drawn[i];
            return Mathf.Abs(d.x - b.center.x) < b.width * 0.3f && d.y > b.yMin + b.height * 0.2f && d.y < b.yMax - b.height * 0.3f;
        }

        private float MeanDistanceCm(FitSession s, System.Func<int, bool> region)
        {
            Vector3[] x = s.SyncPositions();
            double sum = 0; int n = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (!region(i) || !capsule.Body.Collider.ClosestNear(x[i], 0.02f, out _, out _, out float d)) continue;
                sum += d; n++;
            }
            Assert.That(n, Is.GreaterThan(10));
            return (float)(sum / n) * 100f;
        }

        private static void Paint(FitSession s, System.Func<int, bool> region, float hug, float smooth, float inflate)
        {
            for (int i = 0; i < s.Mesh.VertexCount; i++)
            {
                if (!region(i)) continue;
                s.PaintHug[i] = hug; s.PaintSmooth[i] = smooth; s.PaintInflate[i] = inflate;
            }
            s.StorePaint();
            s.ApplyPaint();
        }

        [Test]
        public void Brush_PaintsUpToItsStrength_AndErases()
        {
            float h = 0f, s = 0f, f = 0f;
            FitPaint.Brush(FitPaintMode.Hug, false, 0.6f, ref h, ref s, ref f);
            FitPaint.Brush(FitPaintMode.Hug, false, 0.3f, ref h, ref s, ref f);
            Assert.That(h, Is.EqualTo(0.6f), "a stroke paints up to its strength, not past it");
            FitPaint.Brush(FitPaintMode.Slim, false, 0.5f, ref h, ref s, ref f);
            Assert.That(f, Is.EqualTo(-0.5f));
            FitPaint.Brush(FitPaintMode.Puff, true, 1f, ref h, ref s, ref f);
            Assert.That(f, Is.EqualTo(-0.5f), "erasing puff leaves slim alone");
            FitPaint.Brush(FitPaintMode.Hug, true, 0.5f, ref h, ref s, ref f);
            Assert.That(h, Is.EqualTo(0.3f).Within(1e-6f));
            FitPaint.Brush(FitPaintMode.Erase, false, 1f, ref h, ref s, ref f);
            Assert.That(h, Is.Zero); Assert.That(f, Is.Zero);
        }

        [Test]
        public void Paint_IsKeptOnThePattern_AndCarriedToAnotherMeshLevel()
        {
            FitSession draft = Vest(true);
            Paint(draft, i => FrontMiddle(draft, i), 0.8f, 0.4f, -0.6f);
            Assert.That(draft.Fit.paint.Count, Is.GreaterThan(5));
            // The same mesh again: exactly the same values.
            FitPaint.Resolve(draft.Pattern, draft.Mesh, draft.Fit.paint, out float[] hug, out float[] smooth, out float[] inflate);
            for (int i = 0; i < hug.Length; i++)
            {
                Assert.That(hug[i], Is.EqualTo(draft.PaintHug[i]).Within(0.01f), "point " + i);
                Assert.That(inflate[i], Is.EqualTo(draft.PaintInflate[i]).Within(0.01f), "point " + i);
            }
            // A finer mesh: the middle of the painted area keeps its values, the far cloth stays unpainted.
            PatternMesh fine = PatternMesher.Build(draft.Pattern, draft.Mesh.scales, 1.5f);
            FitPaint.Resolve(draft.Pattern, fine, draft.Fit.paint, out hug, out smooth, out inflate);
            Rect b = draft.Pattern.pieces[0].Bounds();
            int middle = 0, far = 0;
            for (int i = 0; i < fine.VertexCount; i++)
            {
                Vector2 d = fine.drawn[i];
                bool inside = fine.piece[i] == 0 && Mathf.Abs(d.x - b.center.x) < b.width * 0.2f && d.y > b.yMin + b.height * 0.3f && d.y < b.yMax - b.height * 0.4f;
                if (inside) { middle++; Assert.That(hug[i], Is.EqualTo(0.8f).Within(0.05f)); Assert.That(inflate[i], Is.EqualTo(-0.6f).Within(0.05f)); Assert.That(smooth[i], Is.EqualTo(0.4f).Within(0.05f)); }
                if (fine.piece[i] == 1) { far++; Assert.That(hug[i], Is.Zero); }
            }
            Assert.That(middle, Is.GreaterThan(5));
            Assert.That(far, Is.GreaterThan(5));
            // And the fit keeps it as data.
            ClothesFit copy = Object.Instantiate(draft.Fit);
            made.Add(copy);
            Assert.That(copy.paint.Count, Is.EqualTo(draft.Fit.paint.Count));
        }

        [Test]
        public void Model_HuggedClothRestsShorter_AndInflateIsPressureEitherWay()
        {
            FitSession s = Vest(true);
            var rest = new Dictionary<long, float>();
            foreach (ClothConstraint c in s.Model.Constraints) if (c.kind == (int)ConstraintKind.Stretch) rest[((long)c.a << 32) | (uint)c.b] = c.rest;
            Assert.That(s.Model.Pressure, Is.Null, "no pressure anywhere, no pressure array");
            Paint(s, i => s.Mesh.piece[i] == 0, 1f, 0f, -0.5f);
            int checkedEdges = 0;
            foreach (ClothConstraint c in s.Model.Constraints)
            {
                if (c.kind != (int)ConstraintKind.Stretch || s.Mesh.piece[c.a] != 0 || s.Mesh.piece[c.b] != 0) continue;
                Assert.That(c.rest, Is.EqualTo(rest[((long)c.a << 32) | (uint)c.b] * (1f - FitPaint.MaxHugShrink)).Within(1e-6f));
                checkedEdges++;
            }
            Assert.That(checkedEdges, Is.GreaterThan(20));
            Assert.That(s.Model.Pressure, Is.Not.Null);
            for (int i = 0; i < s.Model.ParticleCount; i++)
            {
                Assert.That(s.Model.Pressure[i], Is.EqualTo(s.Mesh.piece[i] == 0 ? -0.5f * ClothModel.FullPressure : 0f).Within(1e-4f));
            }
        }

        [Test]
        public void Puff_PushesOut_Slim_DrawsIn_Hug_DrawsOntoTheBody()
        {
            FitSession control = Vest(true), puff = Vest(true), slim = Vest(true), hug = Vest(true);
            foreach (FitSession s in new[] { control, puff, slim, hug }) s.Step(150);
            Paint(puff, i => FrontMiddle(puff, i), 0f, 0f, 1f);
            Paint(slim, i => FrontMiddle(slim, i), 0f, 0f, -1f);
            Paint(hug, i => hug.Mesh.piece[i] == 0, 1f, 0f, 0f);
            Assert.That(hug.HasFramePaint, Is.True);
            foreach (FitSession s in new[] { control, puff, slim, hug }) s.Step(120);
            float c = MeanDistanceCm(control, i => FrontMiddle(control, i));
            // The vest is close round the capsule, so puff moves it a little (1.6 mm here; 5.8 mm on a loose hoodie).
            Assert.That(MeanDistanceCm(puff, i => FrontMiddle(puff, i)), Is.GreaterThan(c + 0.1f), "puff");
            // Already 7 mm off the capsule, the clearance stops slim soon (0.6 mm here; 22 mm on a loose hoodie).
            Assert.That(MeanDistanceCm(slim, i => FrontMiddle(slim, i)), Is.LessThan(c - 0.03f), "slim");
            float hugged = MeanDistanceCm(hug, i => hug.Mesh.piece[i] == 0), loose = MeanDistanceCm(control, i => control.Mesh.piece[i] == 0);
            Assert.That(hugged, Is.LessThan(loose), "hug");
            foreach (FitSession s in new[] { puff, slim, hug })
            {
                FitMetrics m = s.Measure();
                Assert.That(m.deepestMm, Is.LessThan(1f), m.ToString());
                Assert.That(m.maxStitchGapMm, Is.LessThan(2f), m.ToString());
            }
        }

        [Test]
        public void Smooth_FlattensBumps_AndKeepsTheClothWhereItIsRoundTheBody()
        {
            FitSession control = Vest(true, MeshLevel.Medium), smooth = Vest(true, MeshLevel.Medium);
            float Bumps(FitSession s)
            {
                Vector3[] p = s.SyncPositions();
                double sum = 0; int n = 0;
                for (int i = 0; i < p.Length; i++) if (FrontMiddle(s, i) && s.Mesh.boundaryEdge[i] < 0) { sum += FitPaint.Bulge(p, s.Model, i); n++; }
                return (float)(sum / n) * 1000f;
            }
            foreach (FitSession s in new[] { control, smooth })
            {
                s.Step(150);
                // The same bumps on both: the front's middle pushed in and out in a checker pattern.
                Vector3[] x = s.SyncPositions();
                var bumpy = (Vector3[])x.Clone();
                for (int i = 0; i < x.Length; i++)
                {
                    if (!FrontMiddle(s, i)) continue;
                    Vector2 d = s.Mesh.drawn[i];
                    Vector3 outward = new Vector3(x[i].x, 0f, x[i].z).normalized;
                    bumpy[i] = x[i] + outward * (0.004f * Mathf.Sin(d.x * 1.3f) * Mathf.Sin(d.y * 1.3f));
                }
                s.Restore(bumpy, s.SimTime);
            }
            float distance = MeanDistanceCm(control, i => FrontMiddle(control, i));
            Paint(smooth, i => FrontMiddle(smooth, i), 0f, 1f, 0f);
            control.Step(40); smooth.Step(40);
            Assert.That(Bumps(smooth), Is.LessThan(Bumps(control) * 0.85f), "bumps (mm) against the same cloth unpainted");
            Assert.That(MeanDistanceCm(smooth, i => FrontMiddle(smooth, i)), Is.EqualTo(MeanDistanceCm(control, i => FrontMiddle(control, i))).Within(0.3f), "smoothing keeps the cloth where it is round the body");
            Assert.That(distance, Is.GreaterThan(0f));
        }

        [Test]
        public void GpuSolver_AgreesWithTheCpuSolver_OnPaintedCloth()
        {
            if (!ClothSolverGpu.Supported) Assert.Ignore("This machine cannot run compute shaders; the CPU solver is the one in use.");
            // Pressure pushes along the nearest body triangle's normal, and where two triangles are equally near
            // (a point over a shared edge) rounding picks either, as in contact; so the two are compared by what the
            // paint does, not point by point (measured: 0.08 mm apart after 5 frames, 0.002 mm without pressure).
            FitSession cpu = Vest(true), gpu = Vest(false);
            Assert.That(gpu.Solver.IsGpu, Is.True);
            foreach (FitSession s in new[] { cpu, gpu })
            {
                s.Step(150);
                Paint(s, i => s.Mesh.piece[i] == 0, 0.5f, 0.5f, -0.5f);
                s.Step(120);
            }
            float dc = MeanDistanceCm(cpu, i => cpu.Mesh.piece[i] == 0), dg = MeanDistanceCm(gpu, i => gpu.Mesh.piece[i] == 0);
            Assert.That(Mathf.Abs(dc - dg), Is.LessThan(0.1f), "front's mean distance from the body (cm): CPU " + dc + ", GPU " + dg);
            FitMetrics mc = cpu.Measure(), mg = gpu.Measure();
            Assert.That(mg.maxStitchGapMm, Is.LessThan(2f), mg.ToString());
            Assert.That(Mathf.Abs(mg.meanStretchPercent - mc.meanStretchPercent), Is.LessThan(1f), mc + " / " + mg);
        }
    }
}
#endif
