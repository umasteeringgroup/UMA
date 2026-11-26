using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// Gives a mesh (clothes) bone weights from a body, by one of several methods (<see cref="UMAWeightMethod"/>).
    /// The Clothes Designer, Weight Touchup and the slot builders all use it.
    /// <para>
    /// Every method works on welded nodes: vertices at one position (battle buddies, sewn seams) always end with one
    /// set of weights, so seams never open. Locked vertices keep their weights and act as fixed points for the fill.
    /// The fill (Robust Inpaint) is the method of Abdrashitov, Raichstat, Monsen and Hill, "Robust Skin Weights
    /// Transfer via Weight Inpainting" (SIGGRAPH Asia 2023): copy only confident matches, then minimise
    /// wᵀ(K + K M⁻¹ K)w over the rest (K the cotangent stiffness, M the lumped mass), solved exactly.
    /// </para>
    /// </summary>
    public static class UMAWeightTransfer
    {
        /// <summary>Weights the target from the donor.</summary>
        public static UMAWeightResult Transfer(UMAWeightDonor donor, UMAWeightTarget target, UMAWeightSettings settings)
        {
            if (donor == null) throw new ArgumentNullException(nameof(donor));
            if (target == null || target.Positions == null) throw new ArgumentNullException(nameof(target));
            settings = settings ?? new UMAWeightSettings();
            var report = new UMAWeightReport { Requested = settings.Method };
            var clock = Stopwatch.StartNew();
            Context ctx = Context.Prepare(donor, target, settings, report);
            report.Timings.Add("prepare " + clock.ElapsedMilliseconds + " ms");
            clock.Restart();

            Solution best = null;
            if (settings.Method == UMAWeightMethod.Auto)
            {
                bool canScore = HasScoringPoses(settings.Poses, donor);
                UMAWeightMethod[] candidates = settings.AutoCandidates != null && settings.AutoCandidates.Length > 0 ? settings.AutoCandidates : new[] { UMAWeightMethod.RobustInpaint };
                if (!canScore)
                {
                    report.Messages.Add("Auto needs poses to compare the methods (a humanoid character); it used Robust Inpaint.");
                    best = Run(ctx, UMAWeightMethod.RobustInpaint);
                }
                else
                {
                    float bestScore = float.MaxValue;
                    foreach (UMAWeightMethod m in candidates)
                    {
                        if (m == UMAWeightMethod.Auto) continue;
                        if (m == UMAWeightMethod.SimulatedFit && !HasSimulatedPoses(settings.Poses, target)) continue;
                        Solution s = Run(ctx, m);
                        Finish(ctx, s);
                        UMAWeightScore score = Score(ctx, s, settings.Poses);
                        score.Method = m;
                        report.Scores.Add(score);
                        report.Timings.Add(UMAWeightSettings.DisplayName(m) + " " + clock.ElapsedMilliseconds + " ms");
                        clock.Restart();
                        if (score.Total < bestScore) { bestScore = score.Total; best = s; }
                    }
                    if (best == null) best = Run(ctx, UMAWeightMethod.RobustInpaint);
                }
            }
            else
            {
                UMAWeightMethod method = settings.Method;
                if (method == UMAWeightMethod.SimulatedFit && !HasSimulatedPoses(settings.Poses, target))
                {
                    report.Messages.Add("Simulated Fit needs the clothes draped in poses; it used Robust Inpaint.");
                    method = UMAWeightMethod.RobustInpaint;
                }
                best = Run(ctx, method);
                report.Timings.Add(UMAWeightSettings.DisplayName(method) + " " + clock.ElapsedMilliseconds + " ms");
                clock.Restart();
                if (HasScoringPoses(settings.Poses, donor))
                {
                    if (!best.Finished) Finish(ctx, best);
                    UMAWeightScore score = Score(ctx, best, settings.Poses);
                    score.Method = method;
                    report.Scores.Add(score);
                }
            }
            if (!best.Finished) Finish(ctx, best);
            report.Used = best.Method;
            UMAWeightResult result = Expand(ctx, best, report);
            report.Timings.Add("finish " + clock.ElapsedMilliseconds + " ms");
            report.Count();
            return result;
        }

        private static bool HasScoringPoses(IList<UMAWeightPose> poses, UMAWeightDonor donor)
        {
            if (poses == null || poses.Count == 0) return false;
            foreach (UMAWeightPose p in poses)
                if (p == null || p.BoneMatrices == null || p.BoneMatrices.Length < donor.BoneCount || p.DonorPositions == null || p.DonorPositions.Length != donor.Positions.Length) return false;
            return true;
        }

        private static bool HasSimulatedPoses(IList<UMAWeightPose> poses, UMAWeightTarget target)
        {
            if (poses == null || poses.Count == 0) return false;
            foreach (UMAWeightPose p in poses)
                if (p == null || p.BoneMatrices == null || p.TargetPositions == null || p.TargetPositions.Length != target.VertexCount) return false;
            return true;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Shared data: the graph, the closest points, the locks.

        private sealed class Context
        {
            public UMAWeightDonor Donor;
            public UMAWeightTarget Target;
            public UMAWeightSettings Settings;
            public UMAWeightReport Report;
            public UMAWeightGraph Graph;
            public int B;
            public int[] NodeRegion;
            public UMAWeightSurface FullSurface;
            public UMAWeightSurface[] RegionSurface;
            public UMAWeightHit[] Hit;
            public BoneWeight1[][] Closest;
            public bool[] DeepInside;
            public Vector3[] NodeNormal;
            public bool[] Locked;
            public BoneWeight1[][] LockedWeights;
            public float MatchCos;

            public static Context Prepare(UMAWeightDonor donor, UMAWeightTarget target, UMAWeightSettings settings, UMAWeightReport report)
            {
                var c = new Context { Donor = donor, Target = target, Settings = settings, Report = report };
                c.Graph = UMAWeightGraph.Build(target.Positions, target.Triangles, target.Groups, target.Normals);
                int nodes = c.Graph.NodeCount;
                c.B = Math.Max(1, donor.BoneCount);
                c.MatchCos = Mathf.Cos(settings.MatchAngleDegrees * Mathf.Deg2Rad);
                c.NodeNormal = c.Graph.SmoothedNormals(settings.NormalSmoothingRings);

                // Regions: each a surface of the donor triangles it may copy from.
                c.FullSurface = new UMAWeightSurface(donor.Positions, donor.Triangles, donor.VertexNormals);
                int regions = settings.RegionTriangles != null ? settings.RegionTriangles.Count : 0;
                c.RegionSurface = new UMAWeightSurface[Math.Max(1, regions)];
                for (int r = 0; r < c.RegionSurface.Length; r++)
                {
                    bool[] mask = regions > 0 ? settings.RegionTriangles[r] : null;
                    if (mask == null || mask.Length != donor.TriangleCount) { c.RegionSurface[r] = c.FullSurface; continue; }
                    UMAWeightDonor part = donor.WithTriangles(t => mask[t]);
                    c.RegionSurface[r] = part.TriangleCount > 0 ? new UMAWeightSurface(donor.Positions, part.Triangles, donor.VertexNormals) : c.FullSurface;
                }
                c.NodeRegion = new int[nodes];
                if (target.Region != null && target.Region.Length == target.VertexCount)
                {
                    for (int v = target.VertexCount - 1; v >= 0; v--) c.NodeRegion[c.Graph.NodeOf[v]] = Mathf.Clamp(target.Region[v], 0, c.RegionSurface.Length - 1);
                }

                // Locks: a node is locked when all its vertices are; it keeps the first one's weights. A node only
                // partly locked (two layers at one place, one of them chosen) is weighted, and its locked vertices
                // keep their own weights in the result.
                c.Locked = new bool[nodes];
                c.LockedWeights = new BoneWeight1[nodes][];
                if (target.Locked != null && target.Locked.Length == target.VertexCount && target.CurrentWeights != null && target.CurrentWeights.Length == target.VertexCount)
                {
                    var unlocked = new bool[nodes];
                    for (int v = 0; v < target.VertexCount; v++)
                    {
                        bool locked = target.Locked[v] && target.CurrentWeights[v] != null && target.CurrentWeights[v].Length > 0;
                        if (!locked) unlocked[c.Graph.NodeOf[v]] = true;
                    }
                    for (int v = 0; v < target.VertexCount; v++)
                    {
                        int node = c.Graph.NodeOf[v];
                        if (unlocked[node] || c.Locked[node]) continue;
                        c.Locked[node] = true;
                        c.LockedWeights[node] = target.CurrentWeights[v];
                    }
                }

                // The closest body point of every node, on its region's surface; and whether it is deep inside the body.
                c.Hit = new UMAWeightHit[nodes];
                c.Closest = new BoneWeight1[nodes][];
                c.DeepInside = new bool[nodes];
                bool regionsDiffer = false;
                foreach (UMAWeightSurface s in c.RegionSurface) if (s != c.FullSurface) regionsDiffer = true;
                Context ctx = c;
                Parallel.For(0, nodes, () => (ctx.FullSurface.NewQuery(), new UMAWeightSurface.Query[ctx.RegionSurface.Length], new List<BoneWeight1>(16)), (i, state, local) =>
                {
                    UMAWeightSurface surface = ctx.RegionSurface[ctx.NodeRegion[i]];
                    UMAWeightSurface.Query q = surface == ctx.FullSurface ? local.Item1 : (local.Item2[ctx.NodeRegion[i]] ?? (local.Item2[ctx.NodeRegion[i]] = surface.NewQuery()));
                    UMAWeightHit hit = surface.Closest(ctx.Graph.Position[i], q);
                    ctx.Hit[i] = hit;
                    UMAWeightSurface.WeightsAt(hit, surface, ctx.Donor.Weights, local.Item3, 12);
                    ctx.Closest[i] = local.Item3.ToArray();
                    UMAWeightHit body = regionsDiffer ? ctx.FullSurface.Closest(ctx.Graph.Position[i], local.Item1) : hit;
                    ctx.DeepInside[i] = body.IsInside(ctx.Settings.InsideDepth);
                    return local;
                }, _ => { });
                report.DeepInside = new bool[target.VertexCount];
                for (int v = 0; v < target.VertexCount; v++) report.DeepInside[v] = c.DeepInside[c.Graph.NodeOf[v]];
                return c;
            }

            /// <summary>The cloth's normal agrees with the body's at the closest point (or one of them has none).</summary>
            public bool NormalAgrees(int node, float cos)
            {
                Vector3 n = NodeNormal[node];
                if (n == Vector3.zero || !Hit[node].Found) return true;
                float d = Vector3.Dot(n, Hit[node].Normal);
                return d >= cos || (Settings.AllowFlippedNormals && -d >= cos);
            }
        }

        /// <summary>One method's weights: dense per node (node * B + bone), and each node's state.</summary>
        private sealed class Solution
        {
            public UMAWeightMethod Method;
            public float[] W;
            public UMAWeightVertexState[] State;
            /// <summary>After <see cref="Finish"/>: each node's final weights.</summary>
            public BoneWeight1[][] Final;
            public bool Finished;
        }

        private static Solution NewSolution(Context ctx, UMAWeightMethod method)
        {
            int nodes = ctx.Graph.NodeCount;
            var s = new Solution { Method = method, W = new float[nodes * ctx.B], State = new UMAWeightVertexState[nodes] };
            for (int i = 0; i < nodes; i++)
            {
                if (ctx.Locked[i]) { Set(s.W, ctx.B, i, ctx.LockedWeights[i]); s.State[i] = UMAWeightVertexState.Locked; }
                else if (ctx.Closest[i] != null && ctx.Closest[i].Length > 0) { Set(s.W, ctx.B, i, ctx.Closest[i]); s.State[i] = UMAWeightVertexState.Matched; }
                else s.State[i] = UMAWeightVertexState.Fallback;
            }
            return s;
        }

        private static void Set(float[] w, int b, int node, BoneWeight1[] weights)
        {
            Array.Clear(w, node * b, b);
            if (weights == null) return;
            foreach (BoneWeight1 bw in weights) if ((uint)bw.boneIndex < (uint)b) w[node * b + bw.boneIndex] += bw.weight;
        }

        private static Solution Run(Context ctx, UMAWeightMethod method)
        {
            switch (method)
            {
                case UMAWeightMethod.ExactMatch: return RunExact(ctx);
                case UMAWeightMethod.TightSurface: return RunTight(ctx);
                case UMAWeightMethod.Volumetric: return RunVolumetric(ctx);
                case UMAWeightMethod.SimulatedFit:
                    {
                        Solution s = RunRobust(ctx);
                        s.Method = UMAWeightMethod.SimulatedFit;
                        UMAWeightFit.Fit(ctx.Graph, ctx.Target, ctx.Donor, ctx.Settings, ctx.Settings.Poses, ctx.B, s.W, s.State, ctx.Hit, ctx.Report);
                        return s;
                    }
                default: return RunRobust(ctx);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Methods.

        private static Solution RunTight(Context ctx)
        {
            Solution s = NewSolution(ctx, UMAWeightMethod.TightSurface);
            var free = new bool[ctx.Graph.NodeCount];
            bool any = false;
            for (int i = 0; i < free.Length; i++)
            {
                // Deep poke-through copies the wrong part of the body: fill it from round about instead.
                if (s.State[i] == UMAWeightVertexState.Matched && ctx.DeepInside[i]) { free[i] = true; any = true; }
                if (s.State[i] == UMAWeightVertexState.Fallback) { free[i] = true; any = true; }
            }
            if (any) { Inpaint(ctx, s, free); Smooth(ctx, s, free); }
            Mirror(ctx, s);
            return s;
        }

        private static Solution RunRobust(Context ctx)
        {
            Solution s = NewSolution(ctx, UMAWeightMethod.RobustInpaint);
            var free = new bool[ctx.Graph.NodeCount];
            for (int i = 0; i < free.Length; i++)
            {
                if (s.State[i] == UMAWeightVertexState.Locked) continue;
                bool matched = s.State[i] == UMAWeightVertexState.Matched
                    && ctx.Hit[i].Distance <= ctx.Settings.MatchDistance
                    && !ctx.DeepInside[i]
                    && ctx.NormalAgrees(i, ctx.MatchCos);
                free[i] = !matched;
            }
            Inpaint(ctx, s, free);
            Smooth(ctx, s, free);
            Mirror(ctx, s);
            return s;
        }

        private static Solution RunExact(Context ctx)
        {
            Solution s = NewSolution(ctx, UMAWeightMethod.ExactMatch);
            UMAWeightDonor donor = ctx.Donor;
            int nodes = ctx.Graph.NodeCount;
            var exact = new bool[nodes];

            // By position: a body vertex within the tolerance.
            float tol = Mathf.Max(1e-6f, ctx.Settings.ExactTolerance);
            var grid = new Dictionary<Vector3Int, List<int>>();
            var used = new bool[donor.Positions.Length];
            for (int t = 0; t < donor.Triangles.Length; t++) used[donor.Triangles[t]] = true;
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / tol), Mathf.FloorToInt(p.y / tol), Mathf.FloorToInt(p.z / tol));
            for (int v = 0; v < donor.Positions.Length; v++)
            {
                if (!used[v] || donor.Weights[v] == null || donor.Weights[v].Length == 0) continue;
                Vector3Int k = Key(donor.Positions[v]);
                if (!grid.TryGetValue(k, out List<int> list)) grid[k] = list = new List<int>(1);
                list.Add(v);
            }
            for (int i = 0; i < nodes; i++)
            {
                if (s.State[i] == UMAWeightVertexState.Locked) continue;
                Vector3 p = ctx.Graph.Position[i];
                Vector3Int k = Key(p);
                int bestV = -1; float bestD = tol * tol;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (!grid.TryGetValue(new Vector3Int(k.x + dx, k.y + dy, k.z + dz), out List<int> list)) continue;
                            foreach (int v in list)
                            {
                                float d = (donor.Positions[v] - p).sqrMagnitude;
                                if (d <= bestD) { bestD = d; bestV = v; }
                            }
                        }
                if (bestV >= 0) { Set(s.W, ctx.B, i, donor.Weights[bestV]); s.State[i] = UMAWeightVertexState.Exact; exact[i] = true; }
            }

            // By UV: the body point with the same UV, when it is also near in space.
            if (ctx.Settings.ExactUseUV && ctx.Target.UVs != null && ctx.Target.UVs.Length == ctx.Target.VertexCount && donor.UVs != null && donor.UVs.Length == donor.Positions.Length)
            {
                var uvGrid = new UvGrid(donor);
                for (int i = 0; i < nodes; i++)
                {
                    if (exact[i] || s.State[i] == UMAWeightVertexState.Locked) continue;
                    Vector2 uv = ctx.Target.UVs[ctx.Graph.FirstVertex[i]];
                    if (uvGrid.Find(uv, ctx.Graph.Position[i], ctx.Settings.ExactUvMaxDistance, out BoneWeight1[] w))
                    {
                        Set(s.W, ctx.B, i, w); s.State[i] = UMAWeightVertexState.Exact; exact[i] = true;
                    }
                }
            }

            // The rest as Tight Surface: copied, unless deep inside.
            var free = new bool[nodes];
            bool any = false;
            for (int i = 0; i < nodes; i++)
            {
                if (exact[i] || s.State[i] == UMAWeightVertexState.Locked) continue;
                if (s.State[i] == UMAWeightVertexState.Fallback || ctx.DeepInside[i]) { free[i] = true; any = true; }
            }
            if (any) { Inpaint(ctx, s, free); Smooth(ctx, s, free); }
            int exactCount = 0;
            foreach (bool e in exact) if (e) exactCount++;
            if (exactCount == 0) ctx.Report.Messages.Add("Exact Match found no vertex at a body vertex's place or UV; every vertex was weighted as Tight Surface.");
            Mirror(ctx, s);
            return s;
        }

        private static Solution RunVolumetric(Context ctx)
        {
            Solution s = NewSolution(ctx, UMAWeightMethod.Volumetric);
            UMAWeightSettings st = ctx.Settings;
            int nodes = ctx.Graph.NodeCount;
            // One field per region, over the clothes' bounds and the margin round them.
            for (int r = 0; r < ctx.RegionSurface.Length; r++)
            {
                var members = new List<int>();
                for (int i = 0; i < nodes; i++) if (ctx.NodeRegion[i] == r && s.State[i] != UMAWeightVertexState.Locked) members.Add(i);
                if (members.Count == 0) continue;
                var bounds = new Bounds(ctx.Graph.Position[members[0]], Vector3.zero);
                foreach (int i in members) bounds.Encapsulate(ctx.Graph.Position[i]);
                bounds.Expand(2f * st.FieldMargin);
                UMAWeightSurface surface = ctx.RegionSurface[r];
                List<UMAWeightField.Level> levels = UMAWeightField.BuildLevels(surface, ctx.Donor.Weights, bounds, st.FieldCellSize);
                List<int> columns = UMAWeightField.Columns(levels);
                if (columns.Count == 0) continue;
                var points = new Vector3[members.Count];
                for (int k = 0; k < members.Count; k++) points[k] = ctx.Graph.Position[members[k]];
                float[] field = UMAWeightField.Solve(levels, columns, points, st.PreferGpu, out bool gpu, ctx.Report.Messages);
                if (gpu) ctx.Report.UsedGpu = true;
                ctx.Report.Messages.Add("Weight field: " + levels[0].Dims.x + "x" + levels[0].Dims.y + "x" + levels[0].Dims.z + " cells of " + (levels[0].Cell * 100f).ToString("0.#") + " cm, " + columns.Count + " bones, " + levels.Count + " levels, on the " + (gpu ? "GPU" : "CPU") + ".");
                for (int k = 0; k < members.Count; k++)
                {
                    int i = members[k];
                    float f;
                    if (s.State[i] == UMAWeightVertexState.Fallback || ctx.DeepInside[i] || !ctx.NormalAgrees(i, ctx.MatchCos)) f = 1f;
                    else f = Smooth01((ctx.Hit[i].Distance - st.FieldAnchor) / Mathf.Max(1e-4f, st.FieldBlend));
                    if (f <= 0f) continue;
                    float total = 0f;
                    for (int c = 0; c < columns.Count; c++) total += Mathf.Max(0f, field[k * columns.Count + c]);
                    if (total <= 1e-6f) continue;
                    int row = i * ctx.B;
                    for (int b = 0; b < ctx.B; b++) s.W[row + b] *= 1f - f;
                    for (int c = 0; c < columns.Count; c++) s.W[row + columns[c]] += f * Mathf.Max(0f, field[k * columns.Count + c]) / total;
                    s.State[i] = UMAWeightVertexState.Field;
                }
            }
            Mirror(ctx, s);
            return s;
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // ---------------------------------------------------------------------------------------------------------
        // The fill.

        /// <summary>
        /// Fills the free nodes from the fixed ones by minimising the fill energy, solved exactly. Free nodes in a
        /// part of the mesh with no fixed node keep the closest point's weights.
        /// </summary>
        private static void Inpaint(Context ctx, Solution s, bool[] free)
        {
            UMAWeightGraph g = ctx.Graph;
            int nodes = g.NodeCount;
            int[] comp = g.Components(out int compCount);
            var compHasFixed = new bool[compCount];
            for (int i = 0; i < nodes; i++) if (!free[i]) compHasFixed[comp[i]] = true;
            var index = new int[nodes];
            var freeNodes = new List<int>();
            for (int i = 0; i < nodes; i++)
            {
                index[i] = -1;
                if (!free[i]) continue;
                if (!compHasFixed[comp[i]]) { s.State[i] = s.State[i] == UMAWeightVertexState.Fallback ? UMAWeightVertexState.Fallback : UMAWeightVertexState.Unreached; continue; }
                index[i] = freeNodes.Count;
                freeNodes.Add(i);
            }
            int n = freeNodes.Count;
            if (n == 0) return;

            // The stiffness K (cotangent) per node, as (neighbour, value) with the diagonal first.
            bool biharmonic = ctx.Settings.Energy == UMAInpaintEnergy.Biharmonic;
            double KDiag(int i) { double sum = 0; foreach (double w in g.EdgeWeight[i]) sum += 2.0 * w; return sum; }
            var kdiag = new double[nodes];
            for (int i = 0; i < nodes; i++) kdiag[i] = KDiag(i);

            // Q = K (harmonic) or K + K M⁻¹ K (biharmonic), rows of the free nodes only.
            var rowCols = new List<int>[n];
            var rowVals = new List<double>[n];
            Parallel.For(0, n, () => new Dictionary<int, double>(64), (r, state, acc) =>
            {
                int i = freeNodes[r];
                acc.Clear();
                void Add(int j, double v) { acc.TryGetValue(j, out double o); acc[j] = o + v; }
                Add(i, kdiag[i]);
                for (int e = 0; e < g.Neighbours[i].Length; e++) Add(g.Neighbours[i][e], -2.0 * g.EdgeWeight[i][e]);
                if (biharmonic)
                {
                    // (K M⁻¹ K)_ij = Σ_k K_ik K_kj / M_k over k in {i} and its neighbours.
                    void Through(int k, double kik)
                    {
                        double coef = kik / g.Mass[k];
                        Add(k, coef * kdiag[k]);
                        for (int e = 0; e < g.Neighbours[k].Length; e++) Add(g.Neighbours[k][e], coef * -2.0 * g.EdgeWeight[k][e]);
                    }
                    Through(i, kdiag[i]);
                    for (int e = 0; e < g.Neighbours[i].Length; e++) Through(g.Neighbours[i][e], -2.0 * g.EdgeWeight[i][e]);
                }
                var cols = new List<int>(acc.Count);
                var vals = new List<double>(acc.Count);
                foreach (KeyValuePair<int, double> kv in acc) { cols.Add(kv.Key); vals.Add(kv.Value); }
                rowCols[r] = cols;
                rowVals[r] = vals;
                return acc;
            }, _ => { });

            // The free-free block (in free numbering), and the fixed columns kept for the right-hand side.
            var ffCols = new List<int>[n];
            var ffVals = new List<double>[n];
            double maxDiag = 0;
            for (int r = 0; r < n; r++)
            {
                ffCols[r] = new List<int>(rowCols[r].Count);
                ffVals[r] = new List<double>(rowCols[r].Count);
                for (int e = 0; e < rowCols[r].Count; e++)
                {
                    int j = index[rowCols[r][e]];
                    if (j < 0) continue;
                    ffCols[r].Add(j); ffVals[r].Add(rowVals[r][e]);
                    if (j == r) maxDiag = Math.Max(maxDiag, rowVals[r][e]);
                }
            }
            var positions = new Vector3[n];
            for (int r = 0; r < n; r++) positions[r] = g.Position[freeNodes[r]];

            var chol = new UMASparseCholesky();
            bool ok = false;
            double reg = maxDiag * 1e-10;
            for (int attempt = 0; attempt < 4 && !ok; attempt++)
            {
                for (int r = 0; r < n; r++)
                    for (int e = 0; e < ffCols[r].Count; e++)
                        if (ffCols[r][e] == r) ffVals[r][e] += reg;
                ok = chol.Factor(ffCols, ffVals, positions);
                reg = Math.Max(reg * 1000.0, maxDiag * 1e-8);
            }
            if (!ok)
            {
                ctx.Report.Messages.Add("The fill could not be solved; " + n + " vertices kept the closest point's weights.");
                return;
            }

            // Which bones the fixed nodes carry: only those columns can be non-zero in the fill.
            int B = ctx.B;
            var active = new bool[B];
            for (int i = 0; i < nodes; i++)
            {
                if (free[i] && index[i] >= 0) continue;
                if (free[i]) continue;
                for (int b = 0; b < B; b++) if (s.W[i * B + b] > 0f) active[b] = true;
            }
            var solved = new double[n * B];
            var columns = new List<int>();
            for (int b = 0; b < B; b++) if (active[b]) columns.Add(b);
            Parallel.For(0, columns.Count, k =>
            {
                int b = columns[k];
                var rhs = new double[n];
                for (int r = 0; r < n; r++)
                {
                    double sum = 0;
                    List<int> cols = rowCols[r];
                    List<double> vals = rowVals[r];
                    for (int e = 0; e < cols.Count; e++)
                    {
                        int j = cols[e];
                        if (index[j] >= 0) continue;
                        float wj = s.W[j * B + b];
                        if (wj != 0f) sum -= vals[e] * wj;
                    }
                    rhs[r] = sum;
                }
                chol.Solve(rhs);
                for (int r = 0; r < n; r++) solved[r * B + b] = rhs[r];
            });
            for (int r = 0; r < n; r++)
            {
                int i = freeNodes[r];
                double total = 0;
                foreach (int b in columns) total += Math.Max(0.0, solved[r * B + b]);
                if (total <= 1e-9 || double.IsNaN(total)) continue;
                Array.Clear(s.W, i * B, B);
                foreach (int b in columns)
                {
                    double v = solved[r * B + b];
                    if (v > 0) s.W[i * B + b] = (float)(v / total);
                }
                s.State[i] = UMAWeightVertexState.Inpainted;
            }
        }

        /// <summary>
        /// Smooths the filled nodes and the cloth round them (within the match distance of a filled node), as the
        /// paper's reference code does: a few passes of each node towards its neighbours' average. Copied nodes
        /// further in, exact and locked nodes are not touched.
        /// </summary>
        private static void Smooth(Context ctx, Solution s, bool[] free)
        {
            UMAWeightSettings st = ctx.Settings;
            if (st.SmoothIterations <= 0 || st.SmoothAlpha <= 0f) return;
            UMAWeightGraph g = ctx.Graph;
            int nodes = g.NodeCount, B = ctx.B;
            var inRegion = new bool[nodes];
            var region = new List<int>();
            var queue = new Queue<int>();
            var seed = new int[nodes];
            for (int i = 0; i < nodes; i++)
            {
                if (!free[i] || s.State[i] != UMAWeightVertexState.Inpainted) continue;
                inRegion[i] = true; seed[i] = i; queue.Enqueue(i); region.Add(i);
            }
            if (region.Count == 0) return;
            float reach = st.MatchDistance;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                foreach (int j in g.Neighbours[i])
                {
                    if (inRegion[j]) continue;
                    if (s.State[j] == UMAWeightVertexState.Locked || s.State[j] == UMAWeightVertexState.Exact) continue;
                    if ((g.Position[j] - g.Position[seed[i]]).magnitude >= reach) continue;
                    inRegion[j] = true; seed[j] = seed[i]; queue.Enqueue(j); region.Add(j);
                }
            }
            List<int> columns = ActiveColumns(s.W, nodes, B);
            float alpha = st.SmoothAlpha;
            var mean = new float[B];
            for (int pass = 0; pass < st.SmoothIterations; pass++)
            {
                foreach (int i in region)
                {
                    int[] nb = g.Neighbours[i];
                    if (nb.Length == 0) continue;
                    foreach (int b in columns) mean[b] = 0f;
                    foreach (int j in nb) foreach (int b in columns) mean[b] += s.W[j * B + b];
                    float inv = 1f / nb.Length;
                    foreach (int b in columns) s.W[i * B + b] = (1f - alpha) * s.W[i * B + b] + alpha * mean[b] * inv;
                }
            }
        }

        private static List<int> ActiveColumns(float[] w, int nodes, int B)
        {
            var active = new bool[B];
            for (int i = 0; i < nodes; i++)
                for (int b = 0; b < B; b++)
                    if (w[i * B + b] != 0f) active[b] = true;
            var list = new List<int>();
            for (int b = 0; b < B; b++) if (active[b]) list.Add(b);
            return list;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Symmetry.

        /// <summary>The bone with the other side's name (Left/Right, _L/_R, .L/.R), or the bone itself.</summary>
        public static int[] MirrorBones(string[] names)
        {
            int count = names != null ? names.Length : 0;
            var map = new int[count];
            var byName = new Dictionary<string, int>();
            for (int b = 0; b < count; b++) { map[b] = b; if (!string.IsNullOrEmpty(names[b]) && !byName.ContainsKey(names[b])) byName[names[b]] = b; }
            for (int b = 0; b < count; b++)
            {
                string name = names[b];
                if (string.IsNullOrEmpty(name)) continue;
                string other = MirrorName(name);
                if (other != name && byName.TryGetValue(other, out int o)) map[b] = o;
            }
            return map;
        }

        public static string MirrorName(string name)
        {
            string[,] pairs = { { "Left", "Right" }, { "left", "right" }, { "LEFT", "RIGHT" } };
            for (int p = 0; p < pairs.GetLength(0); p++)
            {
                if (name.Contains(pairs[p, 0])) return name.Replace(pairs[p, 0], pairs[p, 1]);
                if (name.Contains(pairs[p, 1])) return name.Replace(pairs[p, 1], pairs[p, 0]);
            }
            string[] suffixL = { "_L", ".L", "_l", ".l" }, suffixR = { "_R", ".R", "_r", ".r" };
            for (int k = 0; k < suffixL.Length; k++)
            {
                if (name.EndsWith(suffixL[k], StringComparison.Ordinal)) return name.Substring(0, name.Length - 2) + suffixR[k];
                if (name.EndsWith(suffixR[k], StringComparison.Ordinal)) return name.Substring(0, name.Length - 2) + suffixL[k];
            }
            if (name.StartsWith("L_", StringComparison.Ordinal)) return "R_" + name.Substring(2);
            if (name.StartsWith("R_", StringComparison.Ordinal)) return "L_" + name.Substring(2);
            return name;
        }

        private static void Mirror(Context ctx, Solution s)
        {
            if (!ctx.Settings.MirrorLeftRight || ctx.Donor.BoneNames == null) return;
            s.W = MirrorDense(ctx, s.W, s.State, true);
        }

        /// <summary>Each node averaged with its mirror image's weights, Left and Right bones swapped. Locked nodes are kept.</summary>
        private static float[] MirrorDense(Context ctx, float[] W, UMAWeightVertexState[] state, bool report)
        {
            UMAWeightSettings st = ctx.Settings;
            UMAWeightGraph g = ctx.Graph;
            int nodes = g.NodeCount, B = ctx.B;
            int axis = Mathf.Clamp(st.MirrorAxis, 0, 2);
            float tol = Mathf.Max(1e-4f, st.MirrorTolerance);
            int[] boneMap = MirrorBones(ctx.Donor.BoneNames);
            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / tol), Mathf.FloorToInt(p.y / tol), Mathf.FloorToInt(p.z / tol));
            for (int i = 0; i < nodes; i++)
            {
                Vector3Int k = Key(g.Position[i]);
                if (!grid.TryGetValue(k, out List<int> list)) grid[k] = list = new List<int>(2);
                list.Add(i);
            }
            var mirrored = (float[])W.Clone();
            int paired = 0;
            for (int i = 0; i < nodes; i++)
            {
                if (state[i] == UMAWeightVertexState.Locked) continue;
                Vector3 p = g.Position[i];
                p[axis] = 2f * st.MirrorPlane - p[axis];
                Vector3Int k = Key(p);
                int best = -1; float bestD = tol * tol;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (!grid.TryGetValue(new Vector3Int(k.x + dx, k.y + dy, k.z + dz), out List<int> list)) continue;
                            foreach (int j in list)
                            {
                                float d = (g.Position[j] - p).sqrMagnitude;
                                if (d <= bestD) { bestD = d; best = j; }
                            }
                        }
                if (best < 0) continue;
                paired++;
                for (int b = 0; b < B; b++)
                {
                    float other = W[best * B + b];
                    if (other == 0f) continue;
                    int mb = b < boneMap.Length ? boneMap[b] : b;
                    mirrored[i * B + mb] += other;
                }
                for (int b = 0; b < B; b++) mirrored[i * B + b] *= 0.5f;
            }
            if (report && paired < nodes / 2) ctx.Report.Messages.Add("Mirror Left/Right found a mirror image for only " + paired + " of " + nodes + " vertices; the others were left as they were.");
            return mirrored;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Final weights.

        /// <summary>Each node's strongest few bones, normalized, then one gentle pass towards the neighbours for filled nodes.</summary>
        private static void Finish(Context ctx, Solution s)
        {
            UMAWeightGraph g = ctx.Graph;
            int nodes = g.NodeCount, B = ctx.B;
            int maxBones = Mathf.Clamp(ctx.Settings.MaxBones, 1, 32);
            float minWeight = Mathf.Max(0f, ctx.Settings.MinWeight);
            var final = new BoneWeight1[nodes][];
            var list = new List<BoneWeight1>(B);
            for (int i = 0; i < nodes; i++)
            {
                if (s.State[i] == UMAWeightVertexState.Locked) { final[i] = ctx.LockedWeights[i]; continue; }
                final[i] = Prune(s.W, i, B, maxBones, minWeight, list);
            }
            // The gentle pass: filled nodes move a quarter of the way to their neighbours, on the bones they kept.
            var relaxed = new BoneWeight1[nodes][];
            for (int i = 0; i < nodes; i++)
            {
                relaxed[i] = final[i];
                UMAWeightVertexState st = s.State[i];
                if (st != UMAWeightVertexState.Inpainted && st != UMAWeightVertexState.Field && st != UMAWeightVertexState.Fitted) continue;
                BoneWeight1[] own = final[i];
                if (own == null || own.Length < 2 || g.Neighbours[i].Length == 0) continue;
                var w = new BoneWeight1[own.Length];
                float sum = 0f;
                for (int k = 0; k < own.Length; k++)
                {
                    float mean = 0f;
                    foreach (int j in g.Neighbours[i]) mean += WeightOf(final[j], own[k].boneIndex);
                    mean /= g.Neighbours[i].Length;
                    float v = 0.75f * own[k].weight + 0.25f * mean;
                    w[k] = new BoneWeight1 { boneIndex = own[k].boneIndex, weight = v };
                    sum += v;
                }
                if (sum <= 0f) continue;
                for (int k = 0; k < w.Length; k++) w[k].weight /= sum;
                Array.Sort(w, (x, y) => y.weight.CompareTo(x.weight));
                relaxed[i] = w;
            }
            // The pass above treats the two sides alike only where their states match: mirror the result once more.
            if (ctx.Settings.MirrorLeftRight && ctx.Donor.BoneNames != null)
            {
                var dense = new float[nodes * B];
                for (int i = 0; i < nodes; i++)
                    if (relaxed[i] != null) foreach (BoneWeight1 bw in relaxed[i]) if ((uint)bw.boneIndex < (uint)B) dense[i * B + bw.boneIndex] += bw.weight;
                float[] mirrored = MirrorDense(ctx, dense, s.State, false);
                for (int i = 0; i < nodes; i++)
                {
                    if (s.State[i] == UMAWeightVertexState.Locked) continue;
                    BoneWeight1[] w = Prune(mirrored, i, B, maxBones, minWeight, list);
                    if (w != null) relaxed[i] = w;
                }
            }
            for (int i = 0; i < nodes; i++)
            {
                if (relaxed[i] == null || relaxed[i].Length == 0)
                {
                    relaxed[i] = new[] { new BoneWeight1 { boneIndex = Mathf.Clamp(ctx.Donor.FallbackBone, 0, B - 1), weight = 1f } };
                    if (s.State[i] != UMAWeightVertexState.Locked) s.State[i] = UMAWeightVertexState.Fallback;
                }
            }
            s.Final = relaxed;
            s.Finished = true;
        }

        private static float WeightOf(BoneWeight1[] w, int bone)
        {
            if (w == null) return 0f;
            foreach (BoneWeight1 bw in w) if (bw.boneIndex == bone) return bw.weight;
            return 0f;
        }

        private static BoneWeight1[] Prune(float[] W, int node, int B, int maxBones, float minWeight, List<BoneWeight1> list)
        {
            list.Clear();
            int row = node * B;
            for (int b = 0; b < B; b++) if (W[row + b] > 0f && !float.IsNaN(W[row + b])) list.Add(new BoneWeight1 { boneIndex = b, weight = W[row + b] });
            list.Sort((x, y) => y.weight.CompareTo(x.weight));
            if (list.Count > maxBones) list.RemoveRange(maxBones, list.Count - maxBones);
            float sum = 0f;
            foreach (BoneWeight1 w in list) sum += w.weight;
            while (list.Count > 1 && list[list.Count - 1].weight < minWeight * sum) { sum -= list[list.Count - 1].weight; list.RemoveAt(list.Count - 1); }
            if (sum <= 0f) return null;
            var result = new BoneWeight1[list.Count];
            for (int k = 0; k < list.Count; k++) result[k] = new BoneWeight1 { boneIndex = list[k].boneIndex, weight = list[k].weight / sum };
            return result;
        }

        private static UMAWeightResult Expand(Context ctx, Solution s, UMAWeightReport report)
        {
            UMAWeightTarget t = ctx.Target;
            int n = t.VertexCount;
            var counts = new byte[n];
            var all = new List<BoneWeight1>(n * 4);
            report.States = new UMAWeightVertexState[n];
            for (int v = 0; v < n; v++)
            {
                int node = ctx.Graph.NodeOf[v];
                BoneWeight1[] w = s.Final[node];
                // A locked vertex keeps its own weights, even where it shares a place with another (two layers of cloth).
                if (t.Locked != null && t.Locked.Length == n && t.Locked[v] && t.CurrentWeights != null && t.CurrentWeights[v] != null && t.CurrentWeights[v].Length > 0)
                {
                    w = t.CurrentWeights[v];
                    report.States[v] = UMAWeightVertexState.Locked;
                }
                else report.States[v] = s.State[node];
                counts[v] = (byte)w.Length;
                all.AddRange(w);
            }
            return new UMAWeightResult { BonesPerVertex = counts, BoneWeights = all.ToArray(), Report = report };
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scoring.

        private static UMAWeightScore Score(Context ctx, Solution s, IList<UMAWeightPose> poses)
        {
            return UMAWeightScoring.Score(ctx.Graph, ctx.Donor, s.Final, poses, ctx.Settings.ScoreInsideTolerance);
        }

        /// <summary>Scores weights already worked out (per target vertex) over some poses, without transferring again.</summary>
        public static UMAWeightScore Score(UMAWeightDonor donor, UMAWeightTarget target, UMAWeightResult weights, IList<UMAWeightPose> poses, float insideTolerance = 0.003f)
        {
            UMAWeightGraph graph = UMAWeightGraph.Build(target.Positions, target.Triangles, target.Groups, target.Normals);
            BoneWeight1[][] perVertex = weights.PerVertex();
            var perNode = new BoneWeight1[graph.NodeCount][];
            for (int i = 0; i < graph.NodeCount; i++) perNode[i] = perVertex[graph.FirstVertex[i]];
            UMAWeightScore score = UMAWeightScoring.Score(graph, donor, perNode, poses, insideTolerance);
            score.Method = weights.Report != null ? weights.Report.Used : UMAWeightMethod.RobustInpaint;
            return score;
        }

        /// <summary>A small helper for UV matching: the donor's triangles in a grid over UV space.</summary>
        private sealed class UvGrid
        {
            private readonly UMAWeightDonor donor;
            private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();
            private const float Cell = 1f / 128f;

            public UvGrid(UMAWeightDonor donor)
            {
                this.donor = donor;
                int[] t = donor.Triangles;
                for (int k = 0; k * 3 + 2 < t.Length; k++)
                {
                    Vector2 a = donor.UVs[t[k * 3]], b = donor.UVs[t[k * 3 + 1]], c = donor.UVs[t[k * 3 + 2]];
                    Vector2 min = Vector2.Min(a, Vector2.Min(b, c)), max = Vector2.Max(a, Vector2.Max(b, c));
                    if ((max - min).sqrMagnitude > 0.25f) continue; // a triangle wrapping the whole layout is not a match
                    for (int y = Mathf.FloorToInt(min.y / Cell); y <= Mathf.FloorToInt(max.y / Cell); y++)
                        for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                        {
                            var key = new Vector2Int(x, y);
                            if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>(4);
                            list.Add(k);
                        }
                }
            }

            public bool Find(Vector2 uv, Vector3 position, float maxDistance, out BoneWeight1[] weights)
            {
                weights = null;
                if (!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(uv.x / Cell), Mathf.FloorToInt(uv.y / Cell)), out List<int> list)) return false;
                float best = maxDistance * maxDistance;
                int[] t = donor.Triangles;
                var blend = new List<BoneWeight1>(12);
                foreach (int k in list)
                {
                    int ia = t[k * 3], ib = t[k * 3 + 1], ic = t[k * 3 + 2];
                    if (!InsideUv(uv, donor.UVs[ia], donor.UVs[ib], donor.UVs[ic], out Vector3 bary)) continue;
                    Vector3 p = donor.Positions[ia] * bary.x + donor.Positions[ib] * bary.y + donor.Positions[ic] * bary.z;
                    float d = (p - position).sqrMagnitude;
                    if (d > best) continue;
                    best = d;
                    blend.Clear();
                    UMAWeightSurface.Accumulate(blend, donor.Weights[ia], bary.x);
                    UMAWeightSurface.Accumulate(blend, donor.Weights[ib], bary.y);
                    UMAWeightSurface.Accumulate(blend, donor.Weights[ic], bary.z);
                    UMAWeightSurface.Finish(blend, 12);
                    weights = blend.ToArray();
                }
                return weights != null && weights.Length > 0;
            }

            private static bool InsideUv(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 bary)
            {
                bary = Vector3.zero;
                float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(d) < 1e-14f) return false;
                float u = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
                float v = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
                float w = 1f - u - v;
                const float e = -1e-4f;
                if (u < e || v < e || w < e) return false;
                bary = new Vector3(Mathf.Max(0f, u), Mathf.Max(0f, v), Mathf.Max(0f, w));
                float s = bary.x + bary.y + bary.z;
                bary /= s;
                return true;
            }
        }
    }
}
