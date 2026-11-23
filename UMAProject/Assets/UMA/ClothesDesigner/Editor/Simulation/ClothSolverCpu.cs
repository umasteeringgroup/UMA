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
                    for (int s = 0; s < sub; s++)
                    {
                        // 1. Predict.
                        for (int i = 0; i < x.Length; i++)
                        {
                            prev[i] = x[i];
                            if (w[i] <= 0f) continue;
                            float3 vel = (v[i] + g * h) * keep;
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
                        // 4. Body collision.
                        for (int i = 0; i < x.Length; i++)
                        {
                            if (w[i] <= 0f) continue;
                            Collide(i);
                        }
                        // 5. Velocities.
                        for (int i = 0; i < x.Length; i++)
                        {
                            float3 vel = (x[i] - prev[i]) / h;
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
