using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    public enum ConstraintKind
    {
        Stretch = 0,
        Bend = 1,
        Stitch = 2
    }

    /// <summary>A distance constraint between two particles. Both solvers read the same list; the GPU buffer's stride is 20 bytes.</summary>
    [Serializable]
    public struct ClothConstraint
    {
        public int a;
        public int b;
        /// <summary>Rest length (m). For a stitch: the starting distance, scaled down by the closure as the seam is sewn.</summary>
        public float rest;
        public int kind;
        /// <summary>XPBD compliance (m/N) from the fabric of the piece it is in. Stitches use <see cref="SimParams.stitchCompliance"/>.</summary>
        public float compliance;
    }

    /// <summary>A target a particle is drawn to: a pin, or the cursor while grabbing.</summary>
    public struct SolverPin
    {
        public int particle;
        public float3 target;
        /// <summary>1 holds the particle on the target; less lets it move.</summary>
        public float weight;
    }

    /// <summary>The settings of one simulation step. The fabric is in the model, per constraint and per particle.</summary>
    public struct SimParams
    {
        public float frameTime;
        public int substeps;
        public float3 gravity;
        public float gravityScale;
        public float damping;
        public float clearance;
        /// <summary>1 when sewing starts, 0 when every stitch is closed.</summary>
        public float closure;
        public float stitchCompliance;
        public float maxSpeed;
        public float collisionRange;
    }

    /// <summary>
    /// The cloth as both solvers see it: one particle per mesh vertex, the stretch, bend and stitch
    /// constraints taken from the flat pattern, and the constraints grouped in colours so that no two in a
    /// colour move the same particle. Solving colour by colour is what makes the CPU and GPU results agree.
    /// </summary>
    public sealed class ClothModel
    {
        public int ParticleCount;
        public float[] InverseMass;
        /// <summary>How much each particle grips the body, from its piece's fabric.</summary>
        public float[] Friction;
        public ClothConstraint[] Constraints;
        /// <summary>(start, count) of each colour in <see cref="Constraints"/>.</summary>
        public int2[] Colors;
        public int StitchCount;
        public int[] Triangles;
        /// <summary>Particles next to each particle along the mesh and across stitches, for grabbing and pin brushes.</summary>
        public List<int>[] Neighbours;

        /// <summary>
        /// The model of a pattern's mesh. <paramref name="pieceFabric"/> has one fabric per pattern piece
        /// (a missing entry is cotton): each piece's constraints, mass and grip come from its own fabric.
        /// </summary>
        /// <summary>
        /// How easily a pressed pleat fold opens (bend compliance). Pressed folds prefer to stay folded but
        /// open where the body needs the fabric. Measured on two pleated skirts (2026-11-22): 0 kept them
        /// shut over the hips (6-11% stretch, side seams 5-8 mm open); 0.5 kept them folded at the waist
        /// and let them open over the hips (2-3% stretch, seams shut).
        /// </summary>
        public const float PressedCreaseCompliance = 0.5f;

        public static ClothModel Build(PatternMesh mesh, IList<FabricSettings> pieceFabric, Vector3[] arranged)
        {
            var model = new ClothModel();
            int n = mesh.VertexCount;
            model.ParticleCount = n;
            model.Triangles = mesh.triangles.ToArray();
            var fallback = new FabricSettings();
            FabricSettings FabricOf(int vertex)
            {
                int p = mesh.piece[vertex];
                return pieceFabric != null && p < pieceFabric.Count && pieceFabric[p] != null ? pieceFabric[p] : fallback;
            }

            // Mass from the flat area round each particle, at its piece's weight.
            var mass = new float[n];
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.flat[mesh.triangles[t]], b = mesh.flat[mesh.triangles[t + 1]], c = mesh.flat[mesh.triangles[t + 2]];
                float areaM2 = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f * 1e-4f;
                float m = areaM2 * FabricOf(mesh.triangles[t]).density / 3f;
                mass[mesh.triangles[t]] += m; mass[mesh.triangles[t + 1]] += m; mass[mesh.triangles[t + 2]] += m;
            }
            model.InverseMass = new float[n];
            model.Friction = new float[n];
            for (int i = 0; i < n; i++)
            {
                model.InverseMass[i] = mass[i] > 0f ? 1f / mass[i] : 0f;
                model.Friction[i] = FabricOf(i).friction;
            }

            // Edges and the triangles on each side, from the flat mesh.
            var edgeOpposite = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);
            void AddEdge(int a, int b, int opposite)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!edgeOpposite.TryGetValue(key, out List<int> list)) edgeOpposite[key] = list = new List<int>(2);
                list.Add(opposite);
            }
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                int a = mesh.triangles[t], b = mesh.triangles[t + 1], c = mesh.triangles[t + 2];
                AddEdge(a, b, c); AddEdge(b, c, a); AddEdge(c, a, b);
            }
            // Bending as stiff as the fabric asks, but never so stiff that a fine mesh oscillates.
            float bendFloor = BendFloor(mesh.edgeLengthCm);
            // Pressed pleat folds: the bend across them rests folded, as laid out, not flat.
            var creases = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (Vector2Int c in mesh.creases) creases.Add(c.x < c.y ? ((long)c.x << 32) | (uint)c.y : ((long)c.y << 32) | (uint)c.x);
            Vector3 Folded(int v) => new Vector3(mesh.layout[v].x, mesh.layout[v].y, -mesh.layoutDepth[v]) * 0.01f;
            var list0 = new List<ClothConstraint>();
            model.Neighbours = new List<int>[n];
            for (int i = 0; i < n; i++) model.Neighbours[i] = new List<int>(8);
            foreach (KeyValuePair<long, List<int>> kv in edgeOpposite)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                // Mesh edges never cross pieces (pieces only meet at stitches), so each constraint has one fabric.
                FabricSettings fabric = FabricOf(a);
                list0.Add(new ClothConstraint { a = a, b = b, rest = FlatDistance(mesh, a, b), kind = (int)ConstraintKind.Stretch, compliance = fabric.stretchCompliance });
                model.Neighbours[a].Add(b); model.Neighbours[b].Add(a);
                if (kv.Value.Count == 2)
                {
                    int c = kv.Value[0], d = kv.Value[1];
                    if (creases.Contains(kv.Key)) list0.Add(new ClothConstraint { a = c, b = d, rest = Vector3.Distance(Folded(c), Folded(d)), kind = (int)ConstraintKind.Bend, compliance = PressedCreaseCompliance });
                    else list0.Add(new ClothConstraint { a = c, b = d, rest = FlatDistance(mesh, c, d), kind = (int)ConstraintKind.Bend, compliance = Mathf.Max(fabric.bendCompliance, bendFloor) });
                }
            }
            foreach (Vector2Int s in mesh.stitches)
            {
                float start = arranged != null ? Vector3.Distance(arranged[s.x], arranged[s.y]) : 0f;
                list0.Add(new ClothConstraint { a = s.x, b = s.y, rest = start, kind = (int)ConstraintKind.Stitch });
                model.Neighbours[s.x].Add(s.y); model.Neighbours[s.y].Add(s.x);
                model.StitchCount++;
            }

            Colour(model, list0);
            return model;
        }

        private static float FlatDistance(PatternMesh mesh, int a, int b) => (mesh.flat[a] - mesh.flat[b]).magnitude * 0.01f;

        /// <summary>
        /// A model of an existing mesh (Fit Existing Slot): rest lengths from its shape as bound, and the
        /// copies of one point (UV seams) held together by zero-length stitches.
        /// </summary>
        public static ClothModel BuildFromMesh(Vector3[] rest, int[] triangles, int[] weldGroup, FabricSettings fabric)
        {
            float density = fabric.density;
            var model = new ClothModel { ParticleCount = rest.Length, Triangles = triangles };
            var mass = new float[rest.Length];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                float area = Vector3.Cross(rest[triangles[t + 1]] - rest[triangles[t]], rest[triangles[t + 2]] - rest[triangles[t]]).magnitude * 0.5f;
                for (int k = 0; k < 3; k++) mass[triangles[t + k]] += area * density / 3f;
            }
            model.InverseMass = new float[rest.Length];
            model.Friction = new float[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                model.InverseMass[i] = mass[i] > 0f ? 1f / mass[i] : 0f;
                model.Friction[i] = fabric.friction;
            }
            var list = new List<ClothConstraint>();
            var seen = new HashSet<long>(EdgeKeyComparer.Instance);
            var opposite = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            model.Neighbours = new List<int>[rest.Length];
            for (int i = 0; i < rest.Length; i++) model.Neighbours[i] = new List<int>(6);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k], b = triangles[t + (k + 1) % 3], c = triangles[t + (k + 2) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (seen.Add(key))
                    {
                        list.Add(new ClothConstraint { a = a, b = b, rest = Vector3.Distance(rest[a], rest[b]), kind = (int)ConstraintKind.Stretch, compliance = fabric.stretchCompliance });
                        model.Neighbours[a].Add(b); model.Neighbours[b].Add(a);
                        opposite[key] = c;
                    }
                    else if (opposite.TryGetValue(key, out int d) && d != c)
                    {
                        list.Add(new ClothConstraint { a = c, b = d, rest = Vector3.Distance(rest[c], rest[d]), kind = (int)ConstraintKind.Bend, compliance = fabric.bendCompliance });
                    }
                }
            }
            for (int v = 0; v < rest.Length; v++)
            {
                if (weldGroup[v] == v) continue;
                list.Add(new ClothConstraint { a = v, b = weldGroup[v], rest = 0f, kind = (int)ConstraintKind.Stitch });
                model.StitchCount++;
            }
            Colour(model, list);
            return model;
        }

        /// <summary>
        /// Greedy colouring: a constraint goes in the first colour where neither particle is used yet.
        /// Stitches get colours of their own after all the others, so every pass ends with the seams
        /// pulled shut instead of a stretch correction undoing them.
        /// </summary>
        private static void Colour(ClothModel model, List<ClothConstraint> list0)
        {
            // Each particle keeps a bit per colour it is already in: finding a free colour is a few bit
            // tests, so a fine mesh (100,000 constraints) colours in milliseconds.
            int particles = model.ParticleCount;
            int words = 2;
            var mask = new ulong[particles * words];
            bool Used(int p, int c) => (mask[p * words + (c >> 6)] & (1UL << (c & 63))) != 0;
            void Use(int p, int c) => mask[p * words + (c >> 6)] |= 1UL << (c & 63);
            var colorOf = new int[list0.Count];
            int colors = 0, firstStitchColor = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) firstStitchColor = colors;
                for (int i = 0; i < list0.Count; i++)
                {
                    bool stitch = list0[i].kind == (int)ConstraintKind.Stitch;
                    if (stitch != (pass == 1)) continue;
                    int a = list0[i].a, b = list0[i].b;
                    int color = firstStitchColor;
                    while (color < colors && (Used(a, color) || Used(b, color))) color++;
                    if (color >= words * 64)
                    {
                        // More colours than the masks hold: widen them.
                        int wider = words * 2;
                        var grown = new ulong[particles * wider];
                        for (int p = 0; p < particles; p++) for (int w = 0; w < words; w++) grown[p * wider + w] = mask[p * words + w];
                        mask = grown;
                        words = wider;
                    }
                    if (color == colors) colors++;
                    Use(a, color); Use(b, color);
                    colorOf[i] = color;
                }
            }
            // Group the constraints by colour, keeping their order inside each colour.
            var counts = new int[colors + 1];
            for (int i = 0; i < list0.Count; i++) counts[colorOf[i] + 1]++;
            for (int c = 0; c < colors; c++) counts[c + 1] += counts[c];
            model.Constraints = new ClothConstraint[list0.Count];
            model.Colors = new int2[colors];
            for (int c = 0; c < colors; c++) model.Colors[c] = new int2(counts[c], counts[c + 1] - counts[c]);
            var next = (int[])counts.Clone();
            for (int i = 0; i < list0.Count; i++) model.Constraints[next[colorOf[i]]++] = list0[i];
        }

        /// <summary>
        /// The least bend compliance for a mesh level. Near-rigid bending (cotton's 2e-5) is stable at 2 cm
        /// but on finer meshes the bend constraints fight each other and the cloth never comes to rest
        /// (measured on the vest, 2026-11-22: 2.5 mm a frame at 1 cm, 0.9 at 5 mm, for 1,800 frames). From
        /// 0.003 at 1 cm and 0.01 at 5 mm it settles (0.02-0.04 mm a frame); at 2 cm no value up to 0.1 shows.
        /// </summary>
        public static float BendFloor(float edgeLengthCm)
        {
            float edge = Mathf.Max(0.3f, edgeLengthCm > 0f ? edgeLengthCm : 2f);
            return 0.003f / (edge * edge);
        }

        /// <summary>
        /// Substeps a frame for a mesh level: 12 (24 at final quality) at 2 cm, and more for finer meshes.
        /// A finer mesh has more edges in a row to hold, and with the same substeps it stretched more
        /// (measured on the vest, 2026-11-22: mean stretch 0.7% at 2 cm, 1.5% at 1 cm, 4.3% at 5 mm).
        /// Doubling them (tried 2026-11-22, to hold a silk circle skirt up) made the stiff bending unstable
        /// at 2 cm as it is on fine meshes: the vest flew off the test body while sewing. The bend floor
        /// (<see cref="BendFloor"/>) is tuned for these counts.
        /// </summary>
        public static int Substeps(bool final, float edgeLengthCm)
        {
            int baseSteps = final ? 24 : 12;
            float finer = Mathf.Max(1f, 2f / Mathf.Max(0.3f, edgeLengthCm));
            return Mathf.Clamp(Mathf.RoundToInt(baseSteps * finer), baseSteps, 160);
        }

        /// <summary>The settings for a step, from the fit. The fabric is already in the model.</summary>
        public static SimParams Params(float damping, float clearanceM, float closure, float gravityScale, Vector3 gravity, bool final, float edgeLengthCm = 2f)
        {
            return new SimParams
            {
                frameTime = 1f / 60f,
                substeps = Substeps(final, edgeLengthCm),
                gravity = gravity,
                gravityScale = gravityScale,
                damping = damping,
                clearance = clearanceM,
                closure = closure,
                stitchCompliance = 0f,
                maxSpeed = 4f,
                collisionRange = clearanceM + 0.02f
            };
        }
    }

    /// <summary>A cloth solver: the GPU one, or the CPU one that is also the tests' reference.</summary>
    public interface IClothSolver : IDisposable
    {
        string Name { get; }
        bool IsGpu { get; }
        void Initialize(ClothModel model, BodyCollider body, Vector3[] positions);
        void SetBody(BodyCollider body);
        void SetPins(IList<SolverPin> pins);
        void SetPositions(Vector3[] positions);
        /// <summary>Advances whole frames of <see cref="SimParams.frameTime"/>.</summary>
        void Step(SimParams parameters, int frames);
        /// <summary>The positions after the last finished step (the GPU solver waits for them).</summary>
        void GetPositions(Vector3[] destination);
        /// <summary>Bytes the solver keeps on the GPU (0 for the CPU solver).</summary>
        long GpuBytes { get; }
    }
}
