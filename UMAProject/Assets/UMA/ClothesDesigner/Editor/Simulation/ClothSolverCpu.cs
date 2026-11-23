using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>Numbers both solvers share, so they stay the same algorithm.</summary>
    public static class ClothSolverShared
    {
        /// <summary>Body triangles kept per particle each frame for the substeps' collision tests.</summary>
        public const int Candidates = 8;
        /// <summary>How much farther than the collision range candidates are gathered, for movement within a frame (m).</summary>
        public const float CandidateMargin = 0.03f;

        // Cloth on cloth: each particle keeps the nearest few triangles of other pieces each frame, found in
        // a hashed grid of the cloth's triangles, and is kept the thickness away on the side it started on.
        // The grid is counted, then filled, so no bucket ever runs out of room (fixed-size buckets lost a
        // sixth of the oxford's entries, and which ones depended on the order they went in).
        /// <summary>Cloth triangles kept per particle each frame.</summary>
        public const int SelfCandidates = 4;
        /// <summary>Buckets in the hashed grid (a power of two, a multiple of 1024 for the GPU's count scan).</summary>
        public const int SelfHashSize = 1 << 16;
        /// <summary>The most grid cells along each axis one triangle is listed in (a cloth triangle is far smaller).</summary>
        public const int SelfTriangleSpan = 4;
        /// <summary>The grid's cell size (m).</summary>
        public const float SelfCell = 0.025f;
        /// <summary>How much farther than the thickness triangles are gathered, for movement within a frame (m).</summary>
        public const float SelfMargin = 0.01f;
        /// <summary>The most a particle is pushed away from cloth in one substep (m).</summary>
        public const float SelfMaxPush = 0.01f;
        /// <summary>
        /// How much farther apart than at the frame start layers may be pushed each frame (m). Layers that
        /// start closer than the thickness ease apart over a few frames instead of being thrown apart in one
        /// substep, which turned the push into speed.
        /// </summary>
        public const float SelfGrowth = 0.0005f;
        /// <summary>
        /// How far beside a triangle (in its plane) a particle may be and still be over or under it (m).
        /// Cloth meeting edge to edge has no side to keep: pushing it along the normal flipped back and forth
        /// (measured on the oxford's yoke and back, 10-13 mm a frame).
        /// </summary>
        public const float SelfBeside = 0.0002f;
        /// <summary>
        /// Cloth of one piece is only kept apart from itself where the two points lie at least this far
        /// apart on the flat piece (m): nearer, they are neighbours, not a fold lying on the cloth.
        /// </summary>
        public const float SelfMinFlatDistance = 0.04f;

        /// <summary>
        /// The velocity after a substep in which a particle was pushed away from cloth: the push may stop
        /// it moving towards the cloth, but must not send it away faster than it was already going, or
        /// layers that start closer than the thickness are thrown apart.
        /// </summary>
        public static float3 LimitPushSpeed(float3 vel, float3 push, float h)
        {
            float len = math.length(push);
            if (len < 1e-12f) return vel;
            float3 n = push / len;
            float vn = math.dot(vel, n);
            float before = math.dot(vel - push / h, n);
            float cap = math.max(before, 0f);
            return vn > cap ? vel - n * (vn - cap) : vel;
        }

        /// <summary>Whether a particle is over or under a triangle rather than beside it (q: its closest point there).</summary>
        public static bool Overlaps(float3 pos, float3 q, float3 unitNormal)
        {
            float3 off = pos - q;
            float along = math.dot(off, unitNormal);
            return math.lengthsq(off) - along * along <= SelfBeside * SelfBeside;
        }

        public static uint Hash(int cx, int cy, int cz) => unchecked(((uint)cx * 73856093u) ^ ((uint)cy * 19349663u) ^ ((uint)cz * 83492791u)) & (SelfHashSize - 1);

        /// <summary>The grid cells a triangle is listed in: those its bounds touch, at most SelfTriangleSpan along each axis.</summary>
        public static void TriangleCells(float3 a, float3 b, float3 c, out int3 c0, out int3 c1)
        {
            c0 = (int3)math.floor(math.min(a, math.min(b, c)) / SelfCell);
            c1 = math.min((int3)math.floor(math.max(a, math.max(b, c)) / SelfCell), c0 + (SelfTriangleSpan - 1));
        }

        /// <summary>Room in the grid for every listing: each triangle is in at most SelfTriangleSpan cubed cells.</summary>
        public static int SelfListCapacity(int triangles) => System.Math.Max(1, triangles * SelfTriangleSpan * SelfTriangleSpan * SelfTriangleSpan);
    }

    /// <summary>
    /// The CPU cloth solver: the same XPBD steps as the GPU solver, in the same order, as one Burst job.
    /// It runs Simulate where compute shaders are missing and is the reference the GPU result is
    /// checked against.
    /// </summary>
    public sealed class ClothSolverCpu : IClothSolver
    {
        private NativeArray<float3> x, prev, v;
        private NativeArray<float> w, friction;
        private NativeArray<ClothConstraint> constraints;
        private NativeArray<int2> colors;
        private NativeArray<SolverPin> pins;
        private NativeArray<float3> ta, tb, tc, tn;
        private NativeArray<int> cellStart, cellTris;
        private NativeArray<int> candidates;
        private NativeArray<int> tris, piece, weld, nearStart, near, selfCandidates, hashStart, hashCursor, hashTris;
        private NativeArray<float3> selfDelta;
        private NativeArray<float> selfTarget;
        private NativeArray<float2> flat;
        private NativeArray<int> layer;
        private NativeArray<float> pressure;
        private bool hasSelf, selfWithin;
        private int outSign;
        private float3 gridOrigin;
        private float cellSize;
        private int3 dims;
        private bool ready;

        public string Name => "CPU";
        public bool IsGpu => false;
        public long GpuBytes => 0;

        public void Initialize(ClothModel model, BodyCollider body, Vector3[] positions)
        {
            Dispose();
            int n = model.ParticleCount;
            x = new NativeArray<float3>(n, Allocator.Persistent);
            prev = new NativeArray<float3>(n, Allocator.Persistent);
            v = new NativeArray<float3>(n, Allocator.Persistent);
            w = new NativeArray<float>(model.InverseMass, Allocator.Persistent);
            friction = new NativeArray<float>(model.Friction, Allocator.Persistent);
            constraints = new NativeArray<ClothConstraint>(model.Constraints, Allocator.Persistent);
            colors = new NativeArray<int2>(model.Colors, Allocator.Persistent);
            pins = new NativeArray<SolverPin>(0, Allocator.Persistent);
            candidates = new NativeArray<int>(n * ClothSolverShared.Candidates, Allocator.Persistent);
            hasSelf = model.Piece != null && model.WeldGroup != null && model.NearStart != null && model.Triangles != null && model.Triangles.Length > 0;
            tris = new NativeArray<int>(hasSelf ? model.Triangles : new int[3], Allocator.Persistent);
            piece = new NativeArray<int>(hasSelf ? model.Piece : new int[math.max(1, n)], Allocator.Persistent);
            weld = new NativeArray<int>(hasSelf ? model.WeldGroup : new int[math.max(1, n)], Allocator.Persistent);
            nearStart = new NativeArray<int>(hasSelf ? model.NearStart : new int[n + 1], Allocator.Persistent);
            near = new NativeArray<int>(hasSelf ? model.Near : new int[1], Allocator.Persistent);
            selfCandidates = new NativeArray<int>(math.max(1, n) * ClothSolverShared.SelfCandidates, Allocator.Persistent);
            hashStart = new NativeArray<int>(hasSelf ? ClothSolverShared.SelfHashSize + 1 : 1, Allocator.Persistent);
            hashCursor = new NativeArray<int>(hasSelf ? ClothSolverShared.SelfHashSize : 1, Allocator.Persistent);
            hashTris = new NativeArray<int>(hasSelf ? ClothSolverShared.SelfListCapacity(model.Triangles.Length / 3) : 1, Allocator.Persistent);
            selfDelta = new NativeArray<float3>(math.max(1, n), Allocator.Persistent);
            selfTarget = new NativeArray<float>(math.max(1, n), Allocator.Persistent);
            flat = new NativeArray<float2>(math.max(1, n), Allocator.Persistent);
            layer = new NativeArray<int>(math.max(1, n), Allocator.Persistent);
            pressure = new NativeArray<float>(math.max(1, n), Allocator.Persistent);
            for (int i = 0; i < n; i++)
            {
                if (model.Flat != null && i < model.Flat.Length) flat[i] = new float2(model.Flat[i].x, model.Flat[i].y);
                if (model.Layer != null && i < model.Layer.Length) layer[i] = model.Layer[i];
                if (model.Pressure != null && i < model.Pressure.Length) pressure[i] = model.Pressure[i];
            }
            selfWithin = model.SelfWithinPieces && model.Flat != null;
            outSign = model.OutSign >= 0 ? 1 : -1;
            SetPositions(positions);
            SetBody(body);
            ready = true;
        }

        public void SetBody(BodyCollider body)
        {
            if (ta.IsCreated) { ta.Dispose(); tb.Dispose(); tc.Dispose(); tn.Dispose(); cellStart.Dispose(); cellTris.Dispose(); }
            int t = body.TriangleCount;
            ta = new NativeArray<float3>(t, Allocator.Persistent);
            tb = new NativeArray<float3>(t, Allocator.Persistent);
            tc = new NativeArray<float3>(t, Allocator.Persistent);
            tn = new NativeArray<float3>(t, Allocator.Persistent);
            for (int i = 0; i < t; i++) { ta[i] = body.A[i]; tb[i] = body.B[i]; tc[i] = body.C[i]; tn[i] = body.N[i]; }
            cellStart = new NativeArray<int>(body.CellStart, Allocator.Persistent);
            cellTris = new NativeArray<int>(body.CellTriangles.Length > 0 ? body.CellTriangles : new int[1], Allocator.Persistent);
            gridOrigin = body.GridOrigin;
            cellSize = body.CellSize;
            dims = new int3(body.Dims.x, body.Dims.y, body.Dims.z);
        }

        public void SetPins(IList<SolverPin> list)
        {
            if (pins.IsCreated) pins.Dispose();
            pins = new NativeArray<SolverPin>(list != null ? list.Count : 0, Allocator.Persistent);
            for (int i = 0; i < pins.Length; i++) pins[i] = list[i];
        }

        public void SetPositions(Vector3[] positions)
        {
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = positions[i]; prev[i] = positions[i]; v[i] = float3.zero;
            }
        }

        public void Step(SimParams p, int frames)
        {
            if (!ready || frames <= 0) return;
            var job = new StepJob
            {
                x = x, prev = prev, v = v, w = w, friction = friction, constraints = constraints, colors = colors, pins = pins,
                ta = ta, tb = tb, tc = tc, tn = tn, cellStart = cellStart, cellTris = cellTris, candidates = candidates,
                tris = tris, piece = piece, weld = weld, nearStart = nearStart, near = near, selfCandidates = selfCandidates, hashStart = hashStart, hashCursor = hashCursor, hashTris = hashTris, selfDelta = selfDelta, selfTarget = selfTarget,
                flat = flat, layer = layer, pressure = pressure, selfWithin = selfWithin, outSign = outSign,
                self = hasSelf && p.selfCollide != 0 && p.thickness > 0f,
                gridOrigin = gridOrigin, cellSize = cellSize, dims = dims, p = p, frames = frames
            };
            job.Run();
        }

        public void GetPositions(Vector3[] destination)
        {
            for (int i = 0; i < x.Length && i < destination.Length; i++) destination[i] = x[i];
        }

        public void Dispose()
        {
            ready = false;
            if (x.IsCreated) x.Dispose();
            if (prev.IsCreated) prev.Dispose();
            if (v.IsCreated) v.Dispose();
            if (w.IsCreated) w.Dispose();
            if (friction.IsCreated) friction.Dispose();
            if (constraints.IsCreated) constraints.Dispose();
            if (colors.IsCreated) colors.Dispose();
            if (pins.IsCreated) pins.Dispose();
            if (ta.IsCreated) { ta.Dispose(); tb.Dispose(); tc.Dispose(); tn.Dispose(); }
            if (cellStart.IsCreated) cellStart.Dispose();
            if (cellTris.IsCreated) cellTris.Dispose();
            if (candidates.IsCreated) candidates.Dispose();
            // Each field disposed itself: disposing a copy leaves the field looking created.
            if (tris.IsCreated) tris.Dispose();
            if (piece.IsCreated) piece.Dispose();
            if (weld.IsCreated) weld.Dispose();
            if (nearStart.IsCreated) nearStart.Dispose();
            if (near.IsCreated) near.Dispose();
            if (selfCandidates.IsCreated) selfCandidates.Dispose();
            if (hashStart.IsCreated) hashStart.Dispose();
            if (hashCursor.IsCreated) hashCursor.Dispose();
            if (hashTris.IsCreated) hashTris.Dispose();
            if (selfDelta.IsCreated) selfDelta.Dispose();
            if (selfTarget.IsCreated) selfTarget.Dispose();
            if (flat.IsCreated) flat.Dispose();
            if (layer.IsCreated) layer.Dispose();
            if (pressure.IsCreated) pressure.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct StepJob : IJob
        {
            public NativeArray<float3> x, prev, v;
            [ReadOnly] public NativeArray<float> w, friction;
            [ReadOnly] public NativeArray<ClothConstraint> constraints;
            [ReadOnly] public NativeArray<int2> colors;
            [ReadOnly] public NativeArray<SolverPin> pins;
            [ReadOnly] public NativeArray<float3> ta, tb, tc, tn;
            [ReadOnly] public NativeArray<int> cellStart, cellTris;
            public NativeArray<int> candidates;
            [ReadOnly] public NativeArray<int> tris, piece, weld, nearStart, near;
            public NativeArray<int> selfCandidates, hashStart, hashCursor, hashTris;
            public NativeArray<float3> selfDelta;
            public NativeArray<float> selfTarget;
            [ReadOnly] public NativeArray<float2> flat;
            [ReadOnly] public NativeArray<int> layer;
            [ReadOnly] public NativeArray<float> pressure;
            public bool self, selfWithin;
            public int outSign;
            public float3 gridOrigin;
            public float cellSize;
            public int3 dims;
            public SimParams p;
            public int frames;

            public void Execute()
            {
                int sub = math.max(1, p.substeps);
                float h = p.frameTime / sub;
                float invH2 = 1f / (h * h);
                float alphaStitch = p.stitchCompliance * invH2;
                float3 g = p.gravity * p.gravityScale;
                float keep = math.max(0f, 1f - p.damping * h);
                for (int f = 0; f < frames; f++)
                {
                    // Once a frame: the nearest body triangles of each particle. The substeps only test these.
                    for (int i = 0; i < x.Length; i++) FindCandidates(i);
                    if (self)
                    {
                        BuildClothGrid();
                        for (int i = 0; i < x.Length; i++) FindClothCandidates(i);
                    }
                    for (int s = 0; s < sub; s++)
                    {
                        // 1. Predict.
                        for (int i = 0; i < x.Length; i++)
                        {
                            prev[i] = x[i];
                            if (w[i] <= 0f) continue;
                            float3 vel = (v[i] + g * h) * keep;
                            // Padding or air: a push out from the body, along the nearest body triangle's normal.
                            int near0 = candidates[i * ClothSolverShared.Candidates];
                            if (pressure[i] > 0f && near0 >= 0) vel += tn[near0] * (pressure[i] * h);
                            v[i] = vel;
                            x[i] = x[i] + vel * h;
                        }
                        // 2. Constraints, colour by colour.
                        for (int c = 0; c < colors.Length; c++)
                        {
                            int2 range = colors[c];
                            for (int k = range.x; k < range.x + range.y; k++)
                            {
                                ClothConstraint con = constraints[k];
                                float rest = con.kind == 2 ? con.rest * p.closure : con.rest;
                                float alpha = con.kind == 2 ? alphaStitch : con.compliance * invH2;
                                SolveDistance(con.a, con.b, rest, alpha);
                            }
                        }
                        // 3. Pins and grab targets.
                        for (int k = 0; k < pins.Length; k++)
                        {
                            SolverPin pin = pins[k];
                            x[pin.particle] = math.lerp(x[pin.particle], pin.target, math.saturate(pin.weight));
                        }
                        // 4. Cloth on cloth: every push worked out from the same positions, then applied.
                        // Before the body, so a layer pushed toward the body is put back out of it.
                        if (self)
                        {
                            for (int i = 0; i < x.Length; i++) selfDelta[i] = w[i] > 0f ? ClothPush(i) : float3.zero;
                            for (int i = 0; i < x.Length; i++) x[i] = x[i] + selfDelta[i];
                        }
                        // 4b. Body collision.
                        for (int i = 0; i < x.Length; i++)
                        {
                            if (w[i] <= 0f) continue;
                            Collide(i);
                        }
                        // 5. Velocities.
                        for (int i = 0; i < x.Length; i++)
                        {
                            float3 vel = (x[i] - prev[i]) / h;
                            if (self) vel = ClothSolverShared.LimitPushSpeed(vel, selfDelta[i], h);
                            float speed = math.length(vel);
                            if (speed > p.maxSpeed) vel *= p.maxSpeed / speed;
                            v[i] = vel;
                        }
                    }
                }
            }

            private void SolveDistance(int a, int b, float rest, float alpha)
            {
                float wa = w[a], wb = w[b];
                float wsum = wa + wb;
                if (wsum <= 0f) return;
                float3 d = x[a] - x[b];
                float len = math.length(d);
                if (len < 1e-9f) return;
                float3 n = d / len;
                float lambda = -(len - rest) / (wsum + alpha);
                x[a] = x[a] + n * (wa * lambda);
                x[b] = x[b] - n * (wb * lambda);
            }

            private void FindCandidates(int i)
            {
                const int K = ClothSolverShared.Candidates;
                float3 pos = x[i];
                float radius = p.collisionRange + ClothSolverShared.CandidateMargin;
                int baseIndex = i * K;
                var dist = new FixedList64Bytes<float>();
                for (int k = 0; k < K; k++) { candidates[baseIndex + k] = -1; dist.Add(float.MaxValue); }
                int3 c0 = math.clamp((int3)math.floor((pos - radius - gridOrigin) / cellSize), int3.zero, dims - 1);
                int3 c1 = math.clamp((int3)math.floor((pos + radius - gridOrigin) / cellSize), int3.zero, dims - 1);
                float limit = radius * radius;
                for (int z = c0.z; z <= c1.z; z++)
                    for (int y = c0.y; y <= c1.y; y++)
                        for (int xx = c0.x; xx <= c1.x; xx++)
                        {
                            int cell = xx + dims.x * (y + dims.y * z);
                            for (int k = cellStart[cell]; k < cellStart[cell + 1]; k++)
                            {
                                int t = cellTris[k];
                                float plane = math.dot(pos - ta[t], tn[t]);
                                if (plane * plane >= math.min(limit, dist[K - 1])) continue;
                                float d2 = math.lengthsq(ClosestOnTriangle(pos, ta[t], tb[t], tc[t]) - pos);
                                if (d2 >= limit || d2 >= dist[K - 1]) continue;
                                // A triangle in several cells is listed once.
                                bool listed = false;
                                for (int q = 0; q < K; q++) if (candidates[baseIndex + q] == t) { listed = true; break; }
                                if (listed) continue;
                                int at = K - 1;
                                for (int m = K - 1; m > 0; m--)
                                {
                                    if (dist[m - 1] > d2)
                                    {
                                        dist[m] = dist[m - 1];
                                        candidates[baseIndex + m] = candidates[baseIndex + m - 1];
                                        at = m - 1;
                                    }
                                }
                                dist[at] = d2;
                                candidates[baseIndex + at] = t;
                            }
                        }
            }

            private void BuildClothGrid()
            {
                const int H = ClothSolverShared.SelfHashSize;
                int triCount = tris.Length / 3;
                // Count each bucket's triangles, add the counts up into starts, then fill.
                for (int k = 0; k < H; k++) hashCursor[k] = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1)
                    {
                        int run = 0;
                        for (int k = 0; k < H; k++) { int count = hashCursor[k]; hashStart[k] = run; hashCursor[k] = run; run += count; }
                        hashStart[H] = run;
                    }
                    for (int t = 0; t < triCount; t++)
                    {
                        ClothSolverShared.TriangleCells(x[tris[t * 3]], x[tris[t * 3 + 1]], x[tris[t * 3 + 2]], out int3 c0, out int3 c1);
                        for (int z = c0.z; z <= c1.z; z++)
                            for (int y = c0.y; y <= c1.y; y++)
                                for (int xx = c0.x; xx <= c1.x; xx++)
                                {
                                    int bucket = (int)ClothSolverShared.Hash(xx, y, z);
                                    int slot = hashCursor[bucket];
                                    hashCursor[bucket] = slot + 1;
                                    if (pass == 1) hashTris[slot] = t;
                                }
                    }
                }
            }

            /// <summary>The nearest triangles of other pieces (not sewn to this particle), and which side of each it is on.</summary>
            private void FindClothCandidates(int i)
            {
                const int K = ClothSolverShared.SelfCandidates;
                float3 pos = x[i];
                float r = p.thickness + ClothSolverShared.SelfMargin;
                int baseIndex = i * K;
                var dist = new FixedList64Bytes<float>();
                var ids = new FixedList64Bytes<int>();
                for (int k = 0; k < K; k++) { dist.Add(float.MaxValue); ids.Add(-1); selfCandidates[baseIndex + k] = 0; }
                selfTarget[i] = p.thickness;
                if (w[i] <= 0f) return;
                // The cells within reach (the cell is more than twice the reach: at most two along each axis).
                int3 q0 = (int3)math.floor((pos - r) / ClothSolverShared.SelfCell);
                int3 q1 = (int3)math.floor((pos + r) / ClothSolverShared.SelfCell);
                for (int cz = q0.z; cz <= q1.z; cz++)
                    for (int cy = q0.y; cy <= q1.y; cy++)
                        for (int cx = q0.x; cx <= q1.x; cx++)
                        {
                            int bucket = (int)ClothSolverShared.Hash(cx, cy, cz);
                            for (int j = hashStart[bucket]; j < hashStart[bucket + 1]; j++)
                            {
                                int t = hashTris[j];
                                int va = tris[t * 3], vb = tris[t * 3 + 1], vc = tris[t * 3 + 2];
                                if (weld[va] == weld[i] || weld[vb] == weld[i] || weld[vc] == weld[i]) continue;
                                // A piece's own cloth only where it is far from this point on the flat piece: a fold, not a neighbour.
                                if (piece[va] == piece[i] && (!selfWithin || FlatNear(i, va) || FlatNear(i, vb) || FlatNear(i, vc))) continue;
                                float d2 = math.lengthsq(ClosestOnTriangle(pos, x[va], x[vb], x[vc]) - pos);
                                if (d2 >= r * r) continue;
                                if (NearSeam(i, weld[va], weld[vb], weld[vc])) continue;
                                float3 tnorm = math.cross(x[vb] - x[va], x[vc] - x[va]);
                                float tlen = math.length(tnorm);
                                if (tlen < 1e-12f || !ClothSolverShared.Overlaps(pos, ClosestOnTriangle(pos, x[va], x[vb], x[vc]), tnorm / tlen)) continue;
                                bool listed = false;
                                for (int q = 0; q < K; q++) if (ids[q] == t) listed = true;
                                if (listed) continue;
                                // Kept by distance, then triangle number: the same set whatever order the grid lists them in.
                                if (d2 > dist[K - 1] || (d2 == dist[K - 1] && t > ids[K - 1])) continue;
                                int at = K - 1;
                                for (int m = K - 1; m > 0; m--)
                                {
                                    if (dist[m - 1] > d2 || (dist[m - 1] == d2 && ids[m - 1] > t)) { dist[m] = dist[m - 1]; ids[m] = ids[m - 1]; at = m - 1; }
                                    else break;
                                }
                                dist[at] = d2; ids[at] = t;
                            }
                        }
                for (int k = 0; k < K; k++)
                {
                    int t = ids[k];
                    if (t < 0) break;
                    float3 a = x[tris[t * 3]], b = x[tris[t * 3 + 1]], c = x[tris[t * 3 + 2]];
                    float3 q = ClosestOnTriangle(pos, a, b, c);
                    float3 n = math.cross(b - a, c - a);
                    float side = math.dot(pos - q, n) >= 0f ? 1f : -1f;
                    // Pieces of different layers: the higher layer belongs outside, whichever side it is on now.
                    int va = tris[t * 3];
                    if (piece[va] != piece[i] && layer[va] != layer[i]) side = (layer[i] > layer[va] ? 1f : -1f) * outSign;
                    selfCandidates[baseIndex + k] = (int)side * (t + 1);
                    float len = math.length(n);
                    if (len > 1e-12f) selfTarget[i] = math.min(selfTarget[i], math.max(0f, math.dot(pos - q, n / len) * side) + ClothSolverShared.SelfGrowth);
                }
            }

            private bool FlatNear(int i, int j) => math.lengthsq(flat[i] - flat[j]) < ClothSolverShared.SelfMinFlatDistance * ClothSolverShared.SelfMinFlatDistance;

            /// <summary>Whether a triangle touches cloth within two rings of the particle's seam (see ClothModel.Near).</summary>
            private bool NearSeam(int i, int wa, int wb, int wc)
            {
                for (int j = nearStart[i]; j < nearStart[i + 1]; j++)
                {
                    int g = near[j];
                    if (g == wa || g == wb || g == wc) return true;
                }
                return false;
            }

            /// <summary>How far to move a particle so it keeps the thickness from the cloth it started the frame beside.</summary>
            private float3 ClothPush(int i)
            {
                const int K = ClothSolverShared.SelfCandidates;
                float3 pos = x[i];
                float r = p.thickness + ClothSolverShared.SelfMargin;
                float best = 0f, target = selfTarget[i];
                float3 push = float3.zero;
                for (int k = 0; k < K; k++)
                {
                    int code = selfCandidates[i * K + k];
                    if (code == 0) break;
                    int t = math.abs(code) - 1;
                    float side = code > 0 ? 1f : -1f;
                    float3 a = x[tris[t * 3]], b = x[tris[t * 3 + 1]], c = x[tris[t * 3 + 2]];
                    float3 n = math.cross(b - a, c - a);
                    float len = math.length(n);
                    if (len < 1e-12f) continue;
                    n /= len;
                    float3 q = ClosestOnTriangle(pos, a, b, c);
                    if (math.lengthsq(pos - q) > r * r || !ClothSolverShared.Overlaps(pos, q, n)) continue;
                    float d = math.dot(pos - q, n) * side;
                    if (d >= target) continue;
                    // Half: the other layer's particles are pushed the other way by as much.
                    float amount = math.min(0.5f * (target - d), ClothSolverShared.SelfMaxPush);
                    if (amount > best) { best = amount; push = n * (side * amount); }
                }
                return push;
            }

            private void Collide(int i)
            {
                const int K = ClothSolverShared.Candidates;
                float3 pos = x[i];
                float best = p.collisionRange * p.collisionRange;
                int bestTri = -1;
                float3 bestPoint = pos;
                for (int k = 0; k < K; k++)
                {
                    int t = candidates[i * K + k];
                    if (t < 0) break;
                    float3 q = ClosestOnTriangle(pos, ta[t], tb[t], tc[t]);
                    float d2 = math.lengthsq(q - pos);
                    if (d2 < best) { best = d2; bestTri = t; bestPoint = q; }
                }
                if (bestTri < 0) return;
                float3 nrm = tn[bestTri];
                float dist = math.dot(pos - bestPoint, nrm);
                if (dist >= p.clearance) return;
                float push = p.clearance - dist;
                pos += nrm * push;
                // Coulomb friction: the grip is limited by how hard the cloth was pressed into the body.
                float3 moved = pos - prev[i];
                float3 tangential = moved - nrm * math.dot(moved, nrm);
                float slide = math.length(tangential);
                float grip = friction[i] * push;
                if (slide > 1e-12f) pos -= tangential * math.min(1f, grip / slide);
                x[i] = pos;
            }

            private static float3 ClosestOnTriangle(float3 p, float3 a, float3 b, float3 c)
            {
                float3 ab = b - a, ac = c - a, ap = p - a;
                float d1 = math.dot(ab, ap), d2 = math.dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return a;
                float3 bp = p - b;
                float d3 = math.dot(ab, bp), d4 = math.dot(ac, bp);
                if (d3 >= 0f && d4 <= d3) return b;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
                float3 cp = p - c;
                float d5 = math.dot(ab, cp), d6 = math.dot(ac, cp);
                if (d6 >= 0f && d5 <= d6) return c;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                float denom = 1f / (va + vb + vc);
                return a + ab * (vb * denom) + ac * (vc * denom);
            }
        }
    }
}
