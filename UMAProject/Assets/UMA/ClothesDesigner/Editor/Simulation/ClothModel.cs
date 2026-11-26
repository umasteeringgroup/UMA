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
        Stitch = 2,
        /// <summary>
        /// A seam of a free piece (a belt round a dress: on a higher layer and sewn to no other layer). It stays as laid
        /// out while the rest is sewn, and closes once the pieces keep apart (<see cref="SimParams.lateClosure"/>), so the
        /// piece tightens round the cloth under it instead of passing through it.
        /// </summary>
        LateStitch = 3
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
        /// <summary>The same for the free pieces' late stitches (<see cref="ConstraintKind.LateStitch"/>), which close after the rest.</summary>
        public float lateClosure;
        public float stitchCompliance;
        public float maxSpeed;
        public float collisionRange;
        /// <summary>The least distance kept between cloth of different pieces (m), when <see cref="selfCollide"/> is 1.</summary>
        public float thickness;
        /// <summary>1 to keep pieces apart from each other (once sewn), 0 not to.</summary>
        public int selfCollide;
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
        /// <summary>
        /// For each particle, the nearest sewn point within <see cref="SeamReachM"/> on its piece (itself, on a seam) and
        /// the point that one is sewn to, or (-1, -1). While that seam is more than <see cref="OpenSeamM"/> open the
        /// particle does not grip the body: cloth pulled across the body by a stitch is pressed into it, grips hard
        /// enough to lock the seam open, and stays so. Measured on Human Female 3.0 (2026-11-23): the hoodie's underarm
        /// seams stayed 11.5 mm open (now 1.8), the beanie's brim corners 5.1 mm (now 0), the A-line skirt's 2.3 mm
        /// (now 0). Once the seam closes it grips. Folded stitches (pleats, turned seams) are left out.
        /// </summary>
        public Vector2Int[] SeamPair;
        /// <summary>How far apart a sewn pair may be and still count as sewn, for <see cref="SeamPair"/> (m).</summary>
        public const float OpenSeamM = 0.002f;
        /// <summary>
        /// How far from a seam, over the piece as cut, cloth stops gripping while the seam is open (m). Farther (3 cm)
        /// closed no more of the samples' seams, and on the test capsule let panels slide onto its sharp top rim.
        /// </summary>
        public const float SeamReachM = 0.01f;
        public ClothConstraint[] Constraints;
        /// <summary>(start, count) of each colour in <see cref="Constraints"/>.</summary>
        public int2[] Colors;
        public int StitchCount;
        /// <summary>
        /// For cloth-on-cloth collision: each particle's collision group (its piece, merged with any piece
        /// turned over it on a folded seam, which the fold holds instead) and its seam group (particles sewn
        /// together share one). Cloth of one collision group does not collide with itself. Null for a slot
        /// being refitted.
        /// </summary>
        public int[] Piece, WeldGroup;
        /// <summary>
        /// For cloth-on-cloth collision: the seam groups within two rings of each particle's own on cloth of
        /// other collision groups (particle i's run from NearStart[i] to NearStart[i + 1] in Near). Triangles
        /// touching them are not collided with: next to a seam the pieces sewn there meet at an angle closer
        /// than any thickness, and pushing them apart fought the stitches (measured on the oxford shirt: every
        /// point that moved most sat beside a seam, against triangles one ring across it). Null with Piece.
        /// </summary>
        public int[] NearStart, Near;
        /// <summary>Stiffening constraints from interfaced pieces, and those holding folded seams turned.</summary>
        public int InterfacingConstraints, FoldConstraints;
        /// <summary>Ties from the Fit Brush's Stiffen paint (see <see cref="FitPaint.StiffTieCompliance"/>).</summary>
        public int StiffenedConstraints;
        public int[] Triangles;
        /// <summary>Particles next to each particle along the mesh and across stitches, for grabbing and pin brushes.</summary>
        public List<int>[] Neighbours;
        /// <summary>Each particle's place on its flat piece (m). Cloth of one piece only collides with itself where it is far apart on the flat piece.</summary>
        public Vector2[] Flat;
        /// <summary>
        /// How far under the top each particle lies as laid out (m): the layers of a pleat folded under. A
        /// stitch between two layers is closed when they lie this far apart (see FitMetrics). Null: none.
        /// </summary>
        public float[] LayerDepth;
        /// <summary>Each particle's layer (its piece's): where layers cross, the higher one is kept outside.</summary>
        public int[] Layer;
        /// <summary>Particles of free pieces (<see cref="FreePieces"/>): laid out outside the cloth under them, their seams closing late.</summary>
        public bool[] Free;
        /// <summary>How many stitches close late (<see cref="ConstraintKind.LateStitch"/>).</summary>
        public int LateStitchCount;

        /// <summary>
        /// The pattern's free pieces: on a layer above the lowest and sewn to no piece of another layer, like a belt worn
        /// over a dress. (A collar's leaf, a pocket or a shoulder guard is sewn to the layer under it, so is not free.)
        /// Tacks do not count: a belt tacked to the dress at points round it, like belt loops, is still free, and its
        /// tacks close late with its seams, drawing the dress in to it.
        /// </summary>
        public static bool[] FreePieces(ClothesPattern pattern)
        {
            if (pattern == null) return new bool[0];
            int count = pattern.pieces.Count, lowest = int.MaxValue;
            foreach (PatternPiece piece in pattern.pieces) if (piece != null) lowest = Mathf.Min(lowest, piece.layer);
            var free = new bool[count];
            for (int p = 0; p < count; p++) free[p] = pattern.pieces[p] != null && pattern.pieces[p].layer > lowest;
            foreach (PatternSeam seam in pattern.seams)
            {
                if (seam == null) continue;
                int a = pattern.IndexOfPiece(seam.a.pieceId), b = pattern.IndexOfPiece(seam.b.pieceId);
                if (a < 0 || b < 0 || pattern.pieces[a].layer == pattern.pieces[b].layer) continue;
                // A piece and its copy sewn round it (Duplicate Sewn Round) are two plies of one part: that seam does not count.
                if (IsPly(pattern.pieces[a], pattern.pieces[b])) continue;
                free[a] = false;
                free[b] = false;
            }
            // A ply is free only when the piece it was copied from is: the outside of a lined garment is not, a two-ply belt is.
            for (int p = 0; p < count; p++)
            {
                PatternPiece piece = pattern.pieces[p];
                if (piece == null || string.IsNullOrEmpty(piece.duplicateOf)) continue;
                int original = pattern.IndexOfPiece(piece.duplicateOf);
                if (original >= 0 && IsPly(piece, pattern.pieces[original])) free[p] &= free[original];
            }
            return free;
        }
        /// <summary>Whether one piece is a copy of the other on another layer (Duplicate Sewn Round): two plies of one part.</summary>
        private static bool IsPly(PatternPiece x, PatternPiece y) =>
            x != null && y != null && x.layer != y.layer && (x.duplicateOf == y.id || y.duplicateOf == x.id);

        /// <summary>How hard each particle is pushed out from the body (m/s^2), from its piece's pressure. Null when no piece has any.</summary>
        public float[] Pressure;
        /// <summary>Whether cloth of one piece is kept apart from itself (folds, gathers), not only from other pieces.</summary>
        public bool SelfWithinPieces;
        /// <summary>1 when the cloth's triangles face out from the body as meshed, -1 when they face in.</summary>
        public int OutSign = 1;
        /// <summary>Constraints shortened by elastic along an edge, and particles held still by a frozen piece.</summary>
        public int ElasticConstraints, FrozenParticles;

        /// <summary>The stretch compliance of elastic sewn along an edge: the Elastic fabric's.</summary>
        public const float ElasticCompliance = 0.08f;
        /// <summary>The push out from the body at a piece pressure of 1 (m/s^2): three times gravity.</summary>
        public const float FullPressure = 30f;

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

        /// <summary>
        /// The compliance of interfacing's stiffening constraints. Measured on the sample shirt collar
        /// (2026-11-22): at the collar's mesh detail both levels are close to as stiff as the solver holds
        /// (Firm at 0.0005 and 0.00005 drape the same), and in a hanging drape they matter less than the cut:
        /// with interfacing the stand crumples less (mean fold between triangles 12 against 16 degrees) and
        /// the leaf stays turned under (80% against 66%). Light is ten times softer than Firm, to give where
        /// Firm holds under load.
        /// </summary>
        public static float InterfacingCompliance(InterfacingLevel level) => level == InterfacingLevel.Firm ? FirmInterfacingCompliance : LightInterfacingCompliance;
        public static float FirmInterfacingCompliance = 0.0005f;
        public static float LightInterfacingCompliance = 0.05f;
        /// <summary>How folded seams resist unfolding: as a pressed pleat's crease.</summary>
        public static float FoldCompliance = PressedCreaseCompliance;

        public static ClothModel Build(PatternMesh mesh, IList<FabricSettings> pieceFabric, Vector3[] arranged) => Build(mesh, pieceFabric, arranged, (ClothModelOptions)null);

        /// <param name="interfacing">Each piece's interfacing, or null for none.</param>
        public static ClothModel Build(PatternMesh mesh, IList<FabricSettings> pieceFabric, Vector3[] arranged, IList<InterfacingLevel> interfacing) =>
            Build(mesh, pieceFabric, arranged, new ClothModelOptions { Interfacing = interfacing });

        /// <summary>
        /// Turns on cloth-on-cloth collision for this model: each particle's collision group (cloth of one
        /// group never collides with itself), and from it and <see cref="WeldGroup"/> (set first; null is
        /// taken as no seams) the seam neighbourhoods that are left out.
        /// </summary>
        public void SetCollisionGroups(int[] group)
        {
            if (WeldGroup == null)
            {
                WeldGroup = new int[ParticleCount];
                for (int i = 0; i < ParticleCount; i++) WeldGroup[i] = i;
            }
            Piece = group;
            BuildSeamNeighbourhoods(this, ParticleCount);
        }

        /// <summary>Fills NearStart and Near from the triangles, the seam groups and the collision groups.</summary>
        private static void BuildSeamNeighbourhoods(ClothModel model, int n)
        {
            int[] weld = model.WeldGroup, piece = model.Piece, tris = model.Triangles;
            // Seam groups sharing a triangle, and the collision groups each seam group's particles belong to.
            var ring = new Dictionary<int, HashSet<int>>();
            var groupsOf = new Dictionary<int, HashSet<int>>();
            void Link(int a, int b)
            {
                if (!ring.TryGetValue(a, out var set)) ring[a] = set = new HashSet<int>();
                set.Add(b);
            }
            for (int t = 0; t + 2 < tris.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = weld[tris[t + k]], b = weld[tris[t + (k + 1) % 3]];
                    if (a != b) { Link(a, b); Link(b, a); }
                }
            for (int i = 0; i < n; i++)
            {
                if (!groupsOf.TryGetValue(weld[i], out var set)) groupsOf[weld[i]] = set = new HashSet<int>();
                set.Add(piece[i]);
            }
            var cache = new Dictionary<long, int[]>();
            var start = new int[n + 1];
            var near = new List<int>();
            var found = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                start[i] = near.Count;
                int own = weld[i];
                long key = ((long)own << 32) | (uint)piece[i];
                if (!cache.TryGetValue(key, out int[] list))
                {
                    found.Clear();
                    if (ring.TryGetValue(own, out var first))
                        foreach (int g in first)
                        {
                            found.Add(g);
                            if (ring.TryGetValue(g, out var second)) foreach (int h in second) found.Add(h);
                        }
                    found.Remove(own);
                    var kept = new List<int>();
                    foreach (int g in found)
                    {
                        // Only seam groups on other cloth matter: a particle never collides with its own.
                        bool other = false;
                        foreach (int p in groupsOf[g]) if (p != piece[i]) { other = true; break; }
                        if (other) kept.Add(g);
                    }
                    kept.Sort();
                    cache[key] = list = kept.ToArray();
                }
                near.AddRange(list);
            }
            start[n] = near.Count;
            model.NearStart = start;
            model.Near = near.Count > 0 ? near.ToArray() : new int[1];
        }

        public static ClothModel Build(PatternMesh mesh, IList<FabricSettings> pieceFabric, Vector3[] arranged, ClothModelOptions options)
        {
            options = options ?? new ClothModelOptions();
            IList<InterfacingLevel> interfacing = options.Interfacing;
            ClothesPattern pattern = options.Pattern;
            var model = new ClothModel();
            int n = mesh.VertexCount;
            model.ParticleCount = n;
            model.Triangles = mesh.triangles.ToArray();
            model.WeldGroup = GarmentBaker.WeldGroups(n, mesh.stitches);
            // Pieces turned over each other (a collar's leaf over its stand) lie folded closer than any
            // thickness at the fold: measured on the shirt collar, colliding them pushed the leaf through the
            // stand (1,404 crossing triangle pairs against 161). Their fold holds them instead.
            int pieceCount = mesh.pieceStart != null ? mesh.pieceStart.Length : 0;
            var group = new int[pieceCount];
            for (int p = 0; p < pieceCount; p++) group[p] = p;
            int Root(int p) { while (group[p] != p) p = group[p] = group[group[p]]; return p; }
            foreach (int i in mesh.foldedStitches)
            {
                int a = Root(mesh.piece[mesh.stitches[i].x]), b = Root(mesh.piece[mesh.stitches[i].y]);
                if (a != b) group[Mathf.Max(a, b)] = Mathf.Min(a, b);
            }
            var collisionGroup = new int[n];
            for (int i = 0; i < n; i++) collisionGroup[i] = mesh.piece[i] < pieceCount ? Root(mesh.piece[i]) : mesh.piece[i];
            model.SetCollisionGroups(collisionGroup);
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
            // Painted weight: a weighted hem hangs heavier.
            float[] weight = options.Weight != null && options.Weight.Length == n ? options.Weight : null;
            if (weight != null) for (int i = 0; i < n; i++) mass[i] *= 1f + FitPaint.MaxExtraWeight * Mathf.Clamp01(weight[i]);
            float[] stiff = options.Stiff != null && options.Stiff.Length == n ? options.Stiff : null;
            float Firmer(int a, int b) => stiff == null ? 1f : FitPaint.StiffFactor(stiff[a], stiff[b]);
            model.InverseMass = new float[n];
            model.Friction = new float[n];
            model.SeamPair = SeamPairs(mesh, n);
            model.Flat = new Vector2[n];
            model.Layer = new int[n];
            model.SelfWithinPieces = options.SelfWithinPieces;
            model.OutSign = options.TrianglesFaceIn ? -1 : 1;
            bool anyPressure = false;
            if (pattern != null) foreach (PatternPiece piece in pattern.pieces) if (piece != null && piece.pressure > 0f) anyPressure = true;
            float[] inflate = options.Inflate != null && options.Inflate.Length == n ? options.Inflate : null;
            if (inflate != null) foreach (float f in inflate) if (f != 0f) anyPressure = true;
            float[] hug = options.Hug != null && options.Hug.Length == n ? options.Hug : null;
            // Hugged cloth rests shorter: an edge by the mean of its ends' hug.
            float Shrink(int a, int b) => hug == null ? 1f : 1f - FitPaint.MaxHugShrink * 0.5f * (hug[a] + hug[b]);
            if (anyPressure) model.Pressure = new float[n];
            for (int i = 0; i < n; i++)
            {
                int p = mesh.piece[i];
                bool frozen = options.FrozenPieces != null && options.FrozenPieces.Contains(p);
                if (frozen) model.FrozenParticles++;
                model.InverseMass[i] = mass[i] > 0f && !frozen ? 1f / mass[i] : 0f;
                model.Friction[i] = FabricOf(i).friction;
                // Pieces lie apart on the flat pattern as far as the solver cares: only a piece's own cloth is compared.
                model.Flat[i] = mesh.flat[i] * 0.01f;
                if (i < mesh.layoutDepth.Count && mesh.layoutDepth[i] > 0f)
                {
                    if (model.LayerDepth == null) model.LayerDepth = new float[n];
                    model.LayerDepth[i] = mesh.layoutDepth[i] * 0.01f;
                }
                PatternPiece owner = pattern != null && p < pattern.pieces.Count ? pattern.pieces[p] : null;
                if (owner != null) model.Layer[i] = owner.layer;
                if (anyPressure) model.Pressure[i] = ((owner != null ? owner.pressure : 0f) + (inflate != null ? inflate[i] : 0f)) * FullPressure;
            }
            // Elastic along an edge: its segments rest shorter and stretch back.
            var elastic = new Dictionary<long, float>(EdgeKeyComparer.Instance);
            if (pattern != null)
            {
                foreach (Vector3Int segment in mesh.outlineSegments)
                {
                    int p = mesh.piece[segment.x];
                    if (p >= pattern.pieces.Count || segment.z >= pattern.pieces[p].edges.Count) continue;
                    PatternEdge edge = pattern.pieces[p].edges[segment.z];
                    if (edge == null || !edge.elastic) continue;
                    long key = segment.x < segment.y ? ((long)segment.x << 32) | (uint)segment.y : ((long)segment.y << 32) | (uint)segment.x;
                    elastic[key] = Mathf.Clamp(edge.elasticPercent, 30f, 100f) * 0.01f;
                }
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
            // Bending as stiff as the fabric asks, but never so stiff that a fine mesh oscillates: each piece
            // by its own edge length (pieces can be meshed finer than the rest).
            var pieceFloor = new float[mesh.pieceStart != null ? mesh.pieceStart.Length : 0];
            for (int p = 0; p < pieceFloor.Length; p++) pieceFloor[p] = BendFloor(mesh.EdgeCmOf(p));
            float FloorOf(int vertex) => mesh.piece[vertex] < pieceFloor.Length ? pieceFloor[mesh.piece[vertex]] : BendFloor(mesh.edgeLengthCm);
            // Pressed pleat folds: the bend across them rests folded, as laid out, not flat.
            // A fold line drawn on a piece rests folded by its own angle instead.
            var creases = new Dictionary<long, float>(EdgeKeyComparer.Instance);
            for (int i = 0; i < mesh.creases.Count; i++)
            {
                Vector2Int c = mesh.creases[i];
                creases[c.x < c.y ? ((long)c.x << 32) | (uint)c.y : ((long)c.y << 32) | (uint)c.x] = i < mesh.creaseAngle.Count ? mesh.creaseAngle[i] : -1f;
            }
            Vector3 Folded(int v) => new Vector3(mesh.layout[v].x, mesh.layout[v].y, -mesh.layoutDepth[v]) * 0.01f;
            var list0 = new List<ClothConstraint>();
            model.Neighbours = new List<int>[n];
            for (int i = 0; i < n; i++) model.Neighbours[i] = new List<int>(8);
            foreach (KeyValuePair<long, List<int>> kv in edgeOpposite)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                // Mesh edges never cross pieces (pieces only meet at stitches), so each constraint has one fabric.
                FabricSettings fabric = FabricOf(a);
                if (elastic.TryGetValue(kv.Key, out float ratio))
                {
                    list0.Add(new ClothConstraint { a = a, b = b, rest = FlatDistance(mesh, a, b) * ratio * Shrink(a, b), kind = (int)ConstraintKind.Stretch, compliance = Mathf.Max(fabric.stretchCompliance, ElasticCompliance) * Firmer(a, b) });
                    model.ElasticConstraints++;
                }
                else list0.Add(new ClothConstraint { a = a, b = b, rest = FlatDistance(mesh, a, b) * Shrink(a, b), kind = (int)ConstraintKind.Stretch, compliance = StretchAlong(mesh, pattern, fabric, a, b) * Firmer(a, b) });
                model.Neighbours[a].Add(b); model.Neighbours[b].Add(a);
                if (kv.Value.Count == 2)
                {
                    int c = kv.Value[0], d = kv.Value[1];
                    if (creases.TryGetValue(kv.Key, out float foldAngle))
                    {
                        float rest = foldAngle < 0f ? Vector3.Distance(Folded(c), Folded(d)) : LineGeometry.FoldedDistance(mesh.flat[a], mesh.flat[b], mesh.flat[c], mesh.flat[d], foldAngle) * 0.01f;
                        list0.Add(new ClothConstraint { a = c, b = d, rest = rest, kind = (int)ConstraintKind.Bend, compliance = PressedCreaseCompliance });
                    }
                    else list0.Add(new ClothConstraint { a = c, b = d, rest = FlatDistance(mesh, c, d) * Shrink(c, d), kind = (int)ConstraintKind.Bend, compliance = Mathf.Max(fabric.bendCompliance * Firmer(c, d), FloorOf(a)) });
                }
            }
            model.InterfacingConstraints = AddInterfacing(mesh, model, list0, interfacing);
            model.StiffenedConstraints = AddPaintedStiffness(mesh, model, list0, stiff);
            // Free pieces (a belt round a dress) close their own seams late, once the pieces keep apart.
            bool[] freePiece = FreePieces(pattern);
            model.Free = new bool[n];
            for (int i = 0; i < n; i++) model.Free[i] = mesh.piece[i] >= 0 && mesh.piece[i] < freePiece.Length && freePiece[mesh.piece[i]];
            foreach (Vector2Int s in mesh.stitches)
            {
                float start = arranged != null ? Vector3.Distance(arranged[s.x], arranged[s.y]) : 0f;
                // A free piece's own seams, and its tacks to the cloth under it (belt loops), close late.
                bool late = model.Free[s.x] || model.Free[s.y];
                if (late) model.LateStitchCount++;
                list0.Add(new ClothConstraint { a = s.x, b = s.y, rest = start, kind = (int)(late ? ConstraintKind.LateStitch : ConstraintKind.Stitch) });
                model.Neighbours[s.x].Add(s.y); model.Neighbours[s.y].Add(s.x);
                model.StitchCount++;
            }
            model.FoldConstraints = AddFolds(mesh, model, list0, arranged);

            Colour(model, list0);
            return model;
        }

        /// <summary>
        /// Interfacing: a distance constraint from each point to the points two edges away on its piece that
        /// lie mostly up or down from it as drawn. Folding the piece across its height shortens them, so they
        /// resist it; curving it the other way (round the neck) hardly changes them.
        /// </summary>
        private static int AddInterfacing(PatternMesh mesh, ClothModel model, List<ClothConstraint> list, IList<InterfacingLevel> interfacing)
        {
            if (interfacing == null) return 0;
            int added = 0;
            var done = new HashSet<long>(EdgeKeyComparer.Instance);
            for (int a = 0; a < mesh.VertexCount; a++)
            {
                int p = mesh.piece[a];
                if (p >= interfacing.Count || interfacing[p] == InterfacingLevel.None) continue;
                float compliance = InterfacingCompliance(interfacing[p]);
                foreach (int b in model.Neighbours[a])
                {
                    foreach (int c in model.Neighbours[b])
                    {
                        if (c == a || mesh.piece[c] != p || model.Neighbours[a].Contains(c)) continue;
                        Vector2 d = mesh.flat[c] - mesh.flat[a];
                        if (Mathf.Abs(d.y) < Mathf.Abs(d.x) * 0.75f) continue;
                        long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                        if (!done.Add(key)) continue;
                        list.Add(new ClothConstraint { a = a, b = c, rest = FlatDistance(mesh, a, c), kind = (int)ConstraintKind.Bend, compliance = compliance });
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>
        /// Painted stiffness: each painted point is tied to the painted points two edges away on its piece, in every
        /// direction, at a compliance from the two points' paint (<see cref="FitPaint.StiffTieCompliance"/>): firm
        /// interfacing's where fully painted, light's at half. (Interfacing ties only up and down the piece, so a collar
        /// still curves round the neck; tied that way, a stiffened skirt kept its vertical folds: hem spread 5.1 to 4.8 cm.)
        /// </summary>
        private static int AddPaintedStiffness(PatternMesh mesh, ClothModel model, List<ClothConstraint> list, float[] stiff)
        {
            if (stiff == null) return 0;
            int added = 0;
            var done = new HashSet<long>(EdgeKeyComparer.Instance);
            for (int a = 0; a < mesh.VertexCount; a++)
            {
                if (stiff[a] < FitPaint.Keep) continue;
                int p = mesh.piece[a];
                foreach (int b in model.Neighbours[a])
                {
                    foreach (int c in model.Neighbours[b])
                    {
                        if (c == a || mesh.piece[c] != p || stiff[c] < FitPaint.Keep || model.Neighbours[a].Contains(c)) continue;
                        long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                        if (!done.Add(key)) continue;
                        float compliance = FitPaint.StiffTieCompliance(0.5f * (stiff[a] + stiff[c]));
                        list.Add(new ClothConstraint { a = a, b = c, rest = FlatDistance(mesh, a, c), kind = (int)ConstraintKind.Bend, compliance = compliance });
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>
        /// Folded seams, which otherwise hinge freely: each stitched pair's neighbours on either side are
        /// held as far apart as they were laid out, so the turned piece stays turned (a collar's leaf over
        /// its stand) instead of swinging about the seam. (Stiffening ordinary seams next to interfaced
        /// pieces was tried and left out: measured on the shirt collar, holding them straight on made the
        /// collar crumple more, because a neckline seam is not straight on the body.)
        /// </summary>
        private static int AddFolds(PatternMesh mesh, ClothModel model, List<ClothConstraint> list, Vector3[] arranged)
        {
            if (arranged == null || mesh.foldedStitches.Count == 0) return 0;
            var onSeam = new HashSet<int>();
            foreach (Vector2Int s in mesh.stitches) { onSeam.Add(s.x); onSeam.Add(s.y); }
            int added = 0;
            var done = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (int i in mesh.foldedStitches)
            {
                int x = mesh.stitches[i].x, y = mesh.stitches[i].y;
                foreach (int c in model.Neighbours[x])
                {
                    if (mesh.piece[c] != mesh.piece[x] || onSeam.Contains(c)) continue;
                    foreach (int d in model.Neighbours[y])
                    {
                        if (mesh.piece[d] != mesh.piece[y] || onSeam.Contains(d)) continue;
                        long key = c < d ? ((long)c << 32) | (uint)d : ((long)d << 32) | (uint)c;
                        if (!done.Add(key)) continue;
                        list.Add(new ClothConstraint { a = c, b = d, rest = Vector3.Distance(arranged[c], arranged[d]), kind = (int)ConstraintKind.Bend, compliance = FoldCompliance });
                        added++;
                    }
                }
            }
            return added;
        }

        private static float FlatDistance(PatternMesh mesh, int a, int b) => (mesh.flat[a] - mesh.flat[b]).magnitude * 0.01f;

        /// <summary>
        /// The stretch compliance of a mesh edge, from the way it runs against its piece's grain: along the
        /// grain, across it, or on the bias. Fabric with no cross or bias value of its own stretches the
        /// same every way.
        /// </summary>
        private static float StretchAlong(PatternMesh mesh, ClothesPattern pattern, FabricSettings fabric, int a, int b)
        {
            if (!fabric.HasGrain || pattern == null) return fabric.stretchCompliance;
            int p = mesh.piece[a];
            float grain = p < pattern.pieces.Count && pattern.pieces[p] != null ? pattern.pieces[p].grainAngle * Mathf.Deg2Rad : 0f;
            // On the pattern as drawn: sizing a piece does not turn its threads.
            Vector2 edge = mesh.drawn[b] - mesh.drawn[a];
            return fabric.StretchAt(edge, new Vector2(-Mathf.Sin(grain), Mathf.Cos(grain)));
        }

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
            model.SeamPair = new Vector2Int[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                model.SeamPair[i] = new Vector2Int(-1, -1);
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
                    bool stitch = list0[i].kind == (int)ConstraintKind.Stitch || list0[i].kind == (int)ConstraintKind.LateStitch;
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
        /// <summary>
        /// Each particle's <see cref="SeamPair"/>: the nearest sewn point over its piece as cut (within
        /// <see cref="SeamReachM"/>, through the mesh's edges, never across a seam) and the point that one is sewn to.
        /// </summary>
        public static Vector2Int[] SeamPairs(PatternMesh mesh, int n)
        {
            var pair = new Vector2Int[n];
            var reach = new float[n];
            for (int i = 0; i < n; i++) { pair[i] = new Vector2Int(-1, -1); reach[i] = float.MaxValue; }
            var heap = new SortedSet<(float d, int i)>();
            // Folded stitches hold layers together (pleats, turned seams) rather than close a seam across the body, and
            // some keep their layers apart on purpose: they never count as open seams.
            var folded = new HashSet<int>(mesh.foldedStitches);
            for (int k = 0; k < mesh.stitches.Count; k++)
            {
                Vector2Int s = mesh.stitches[k];
                if (folded.Contains(k) || s.x < 0 || s.y < 0 || s.x >= n || s.y >= n || s.x == s.y) continue;
                pair[s.x] = s; pair[s.y] = new Vector2Int(s.y, s.x);
                reach[s.x] = 0f; reach[s.y] = 0f;
            }
            for (int i = 0; i < n; i++) if (reach[i] == 0f) heap.Add((0f, i));
            var neighbours = new List<int>[n];
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = mesh.triangles[t + e], b = mesh.triangles[t + (e + 1) % 3];
                    (neighbours[a] ??= new List<int>(6)).Add(b);
                    (neighbours[b] ??= new List<int>(6)).Add(a);
                }
            }
            float limit = SeamReachM * 100f;
            while (heap.Count > 0)
            {
                var top = heap.Min; heap.Remove(top);
                if (top.d > reach[top.i] || neighbours[top.i] == null) continue;
                foreach (int j in neighbours[top.i])
                {
                    float d = top.d + (mesh.flat[top.i] - mesh.flat[j]).magnitude;
                    if (d > limit || d >= reach[j]) continue;
                    if (reach[j] < float.MaxValue) heap.Remove((reach[j], j));
                    reach[j] = d;
                    pair[j] = pair[top.i];
                    heap.Add((d, j));
                }
            }
            return pair;
        }

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

    /// <summary>What a cloth model is built with besides the mesh and the fabrics.</summary>
    public sealed class ClothModelOptions
    {
        /// <summary>Each piece's interfacing, or null for none.</summary>
        public IList<InterfacingLevel> Interfacing;
        /// <summary>The pattern, for what its pieces and edges say: elastic, layers, pressure, grain.</summary>
        public ClothesPattern Pattern;
        /// <summary>Pieces (by index) whose cloth is held where it is.</summary>
        public HashSet<int> FrozenPieces;
        /// <summary>Keep cloth of one piece apart from itself too.</summary>
        public bool SelfWithinPieces;
        /// <summary>The mesh's triangles face into the body (the arrangement's flipTriangles).</summary>
        public bool TrianglesFaceIn;
        /// <summary>Painted hug per point (0 to 1), or null: the cloth there rests up to <see cref="FitPaint.MaxHugShrink"/> shorter.</summary>
        public float[] Hug;
        /// <summary>Painted puff (positive) or slim (negative) per point, or null: added to the piece's pressure.</summary>
        public float[] Inflate;
        /// <summary>Painted stiffness per point (0 to 1), or null: see <see cref="FitPaint.StiffFactor"/> and <see cref="FitPaint.StiffTieCompliance"/>.</summary>
        public float[] Stiff;
        /// <summary>Painted weight per point (0 to 1), or null: the point weighs up to <see cref="FitPaint.MaxExtraWeight"/> times more.</summary>
        public float[] Weight;
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
        /// <summary>
        /// Takes the model's constraints again, the same ones in the same order with new rest lengths (a piece resized
        /// while it is simulated); the cloth carries on from where it is.
        /// </summary>
        void SetConstraints(ClothConstraint[] constraints);
        /// <summary>Advances whole frames of <see cref="SimParams.frameTime"/>.</summary>
        void Step(SimParams parameters, int frames);
        /// <summary>The positions after the last finished step (the GPU solver waits for them).</summary>
        void GetPositions(Vector3[] destination);
        /// <summary>Bytes the solver keeps on the GPU (0 for the CPU solver).</summary>
        long GpuBytes { get; }
    }
}
