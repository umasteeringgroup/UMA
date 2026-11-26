#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Cloth on cloth: a sheet dropped on a held sheet, the seam neighbourhoods, and when it is on.</summary>
    public sealed class SelfCollisionTests
    {
        private const float Thickness = 0.002f;
        // Away from the capsule: only the sheets meet.
        private static readonly Vector3 Origin = new Vector3(2f, 1f, 0f);
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

        /// <summary>A flat square sheet of cells x cells (1 cm each), its points appended to the lists.</summary>
        private static void Sheet(List<Vector3> points, List<int> triangles, int cells, Vector3 corner)
        {
            int first = points.Count, side = cells + 1;
            for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++) points.Add(corner + new Vector3(x * 0.01f, 0f, z * 0.01f));
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int a = first + z * side + x, b = a + 1, c = a + side, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
        }

        /// <summary>
        /// A held 10 cm sheet and a free 8 cm sheet a few millimetres above it, offset half a cell so its
        /// points lie over the held sheet's triangles. Returns the model; heldCount is the held sheet's points.
        /// </summary>
        private static ClothModel Layers(float gapM, out Vector3[] positions, out int heldCount)
        {
            var points = new List<Vector3>(); var triangles = new List<int>();
            Sheet(points, triangles, 10, Origin);
            heldCount = points.Count;
            Sheet(points, triangles, 8, Origin + new Vector3(0.015f, gapM, 0.015f));
            positions = points.ToArray();
            var weld = new int[positions.Length];
            for (int i = 0; i < weld.Length; i++) weld[i] = i;
            ClothModel model = ClothModel.BuildFromMesh(positions, triangles.ToArray(), weld, new FabricSettings());
            var group = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                group[i] = i < heldCount ? 0 : 1;
                if (i < heldCount) model.InverseMass[i] = 0f;
            }
            model.SetCollisionGroups(group);
            return model;
        }

        private IClothSolver Solver(bool gpu, ClothModel model, Vector3[] positions)
        {
            IClothSolver solver = gpu ? new ClothSolverGpu() : new ClothSolverCpu();
            disposables.Add(solver);
            solver.Initialize(model, capsule.Body.Collider, positions);
            return solver;
        }

        private static SimParams Params(float thickness, int selfCollide)
        {
            SimParams p = ClothModel.Params(1f, 0.005f, 0f, 1f, Physics.gravity, false, 1f);
            p.thickness = thickness;
            p.selfCollide = selfCollide;
            return p;
        }

        /// <summary>The lowest and highest height of the free sheet over the held one (m).</summary>
        private static Vector2 Heights(IClothSolver solver, int count, int heldCount)
        {
            var now = new Vector3[count];
            solver.GetPositions(now);
            float low = float.MaxValue, high = float.MinValue;
            for (int i = heldCount; i < count; i++) { low = Mathf.Min(low, now[i].y - Origin.y); high = Mathf.Max(high, now[i].y - Origin.y); }
            return new Vector2(low, high);
        }

        [Test]
        public void DroppedSheet_RestsTheThicknessAboveTheOther_CpuSolver()
        {
            ClothModel model = Layers(0.006f, out Vector3[] positions, out int held);
            IClothSolver solver = Solver(false, model, positions);
            solver.Step(Params(Thickness, 1), 120);
            Vector2 h = Heights(solver, positions.Length, held);
            Assert.That(h.x * 1000f, Is.GreaterThan(Thickness * 1000f - 0.3f), "lowest point above the held sheet (mm)");
            Assert.That(h.y * 1000f, Is.LessThan(Thickness * 1000f + 1.5f), "highest point: resting on it, not thrown off (mm)");
        }

        [Test]
        public void DroppedSheet_FallsThrough_WithNoThicknessOrCollisionOff()
        {
            foreach (var setting in new[] { new Vector2(0f, 1f), new Vector2(Thickness, 0f) })
            {
                ClothModel model = Layers(0.006f, out Vector3[] positions, out int held);
                IClothSolver solver = Solver(false, model, positions);
                solver.Step(Params(setting.x, (int)setting.y), 60);
                Vector2 h = Heights(solver, positions.Length, held);
                Assert.That(h.y, Is.LessThan(-0.01f), "thickness " + setting.x + ", collision " + setting.y);
            }
        }

        [Test]
        public void SheetsCloserThanTheThickness_EaseApartWithoutBeingThrown()
        {
            // Laid 0.5 mm apart: they part a little each frame instead of jumping the whole thickness at once.
            ClothModel model = Layers(0.0005f, out Vector3[] positions, out int held);
            IClothSolver solver = Solver(false, model, positions);
            SimParams p = Params(Thickness, 1);
            p.gravity = Vector3.zero;
            solver.Step(p, 1);
            Vector2 first = Heights(solver, positions.Length, held);
            Assert.That(first.y * 1000f, Is.LessThan(0.5f + ClothSolverShared.SelfGrowth * 1000f + 0.1f), "after one frame (mm)");
            solver.Step(p, 30);
            Vector2 later = Heights(solver, positions.Length, held);
            Assert.That(later.x * 1000f, Is.GreaterThan(Thickness * 1000f - 0.3f), "after 31 frames (mm)");
            Assert.That(later.y * 1000f, Is.LessThan(Thickness * 1000f + 0.5f), "after 31 frames (mm)");
        }

        [Test]
        public void GpuSolver_KeepsTheThicknessLikeTheCpuSolver()
        {
            if (!ClothSolverGpu.Supported) Assert.Ignore("This machine cannot run compute shaders; the CPU solver is the one in use.");
            ClothModel model = Layers(0.006f, out Vector3[] positions, out int held);
            IClothSolver cpu = Solver(false, model, positions), gpu = Solver(true, model, positions);
            cpu.Step(Params(Thickness, 1), 120); gpu.Step(Params(Thickness, 1), 120);
            var pc = new Vector3[positions.Length]; var pg = new Vector3[positions.Length];
            cpu.GetPositions(pc); gpu.GetPositions(pg);
            double sum = 0;
            for (int i = 0; i < pc.Length; i++) sum += (pc[i] - pg[i]).magnitude;
            Assert.That(sum / pc.Length * 1000.0, Is.LessThan(0.05), "mean distance in mm");
            Vector2 h = Heights(gpu, positions.Length, held);
            Assert.That(h.x * 1000f, Is.GreaterThan(Thickness * 1000f - 0.3f), "lowest point above the held sheet (mm)");
        }

        [Test]
        public void SeamNeighbourhood_IsTheOtherPieceWithinTwoRings()
        {
            // Two 5-cell sheets side by side, the second's first column sewn to the first's last.
            var points = new List<Vector3>(); var triangles = new List<int>();
            Sheet(points, triangles, 5, Vector3.zero);
            int second = points.Count;
            Sheet(points, triangles, 5, new Vector3(0.05f, 0f, 0f));
            var weld = new int[points.Count];
            for (int i = 0; i < weld.Length; i++) weld[i] = i;
            for (int z = 0; z <= 5; z++) weld[second + z * 6] = z * 6 + 5;
            ClothModel model = ClothModel.BuildFromMesh(points.ToArray(), triangles.ToArray(), weld, new FabricSettings());
            model.WeldGroup = weld;
            var group = new int[points.Count];
            for (int i = 0; i < group.Length; i++) group[i] = i < second ? 0 : 1;
            model.SetCollisionGroups(group);

            int[] NearOf(int i) { var list = new int[model.NearStart[i + 1] - model.NearStart[i]]; System.Array.Copy(model.Near, model.NearStart[i], list, 0, list.Length); return list; }
            int Column(int g) => g < second ? g % 6 : 5 + (g - second) % 6;
            // On the seam (first sheet, last column, shared with the second): the seam and the second sheet's next two columns.
            int[] seam = NearOf(2 * 6 + 5);
            Assert.That(seam.Length, Is.GreaterThan(0));
            foreach (int g in seam) Assert.That(Column(g), Is.InRange(5, 7), "seam group " + g);
            // One column in: the seam and one column across it.
            int[] inside = NearOf(2 * 6 + 4);
            Assert.That(inside.Length, Is.GreaterThan(0));
            foreach (int g in inside) Assert.That(Column(g), Is.InRange(5, 6), "group " + g);
            // Three columns from the seam: nothing.
            Assert.That(NearOf(2 * 6 + 2), Is.Empty);
            Assert.That(NearOf(second + 2 * 6 + 4), Is.Empty);
        }

        [Test]
        public void FitSession_KeepsPiecesApartOnlyOnceSewn()
        {
            ClothesPattern pattern = VestPattern.Create();
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(pattern); made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            fit.clothThicknessMm = 2f;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.Model.NearStart, Is.Not.Null);
            Assert.That(session.Sewn, Is.False);
            Assert.That(session.CurrentParams().selfCollide, Is.EqualTo(0), "while sewing, laid-out pieces may pass each other");
            session.SimTime = FitSession.SewSeconds + 1f;
            Assert.That(session.CurrentParams().selfCollide, Is.EqualTo(1));
            Assert.That(session.CurrentParams().thickness, Is.EqualTo(0.002f).Within(1e-6f));
            fit.clothThicknessMm = 0f;
            Assert.That(session.CurrentParams().selfCollide, Is.EqualTo(0));
        }

        [Test]
        public void BackFaces_AreTheTrianglesAgainWoundTheOtherWay()
        {
            int[] triangles = { 0, 1, 2, 2, 1, 3 };
            int[] both = DesignerSession.WithBackFaces(triangles, 4);
            Assert.That(both, Is.EqualTo(new[] { 0, 1, 2, 2, 1, 3, 4, 6, 5, 6, 7, 5 }));
            Vector3[] normals = DesignerSession.WithBackFaceNormals(new[] { Vector3.up, Vector3.up, Vector3.forward, Vector3.up });
            Assert.That(normals, Is.EqualTo(new[] { Vector3.up, Vector3.up, Vector3.forward, Vector3.up, Vector3.down, Vector3.down, Vector3.back, Vector3.down }));
        }
    }
}
#endif
