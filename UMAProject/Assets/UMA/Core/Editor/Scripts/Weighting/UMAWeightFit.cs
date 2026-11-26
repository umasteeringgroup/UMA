using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// Simulated Fit: weights that reproduce how the cloth hangs in several poses. For each vertex off the body,
    /// it looks for non-negative weights summing to one whose skinning puts the vertex where the simulation put it
    /// in every pose, drawn towards its neighbours' weights and its starting weights. This is the weight step of
    /// Smooth Skinning Decomposition with Rigid Bones (Le and Deng 2012, EA's Dem Bones) with the bones given.
    /// <para>
    /// Skinning cannot hang cloth the way a simulation does in every pose, so the fit is a compromise, and a plain
    /// least-squares one gives up following a leg where the cloth hangs free in another pose, and the leg then
    /// pokes through. Two rules keep the poke-through out: where the simulated cloth touches the body in a pose,
    /// that pose counts <see cref="ContactBoost"/> times as much (cloth on a moving limb must move with it); and a
    /// vertex keeps its fitted weights only if they put it inside the posed body no more often than its starting
    /// weights did. Cloth lying on the body keeps the body's weights, so the DNA bones it follows are not lost.
    /// </para>
    /// </summary>
    public static class UMAWeightFit
    {
        /// <summary>The most bones a vertex is fitted over.</summary>
        public const int Candidates = 10;
        /// <summary>How much more a pose counts where the simulated cloth touches the body.</summary>
        public const float ContactBoost = 10f;
        /// <summary>Simulated cloth this close to the posed body touches it (m).</summary>
        public const float ContactDistance = 0.015f;
        private const int Sweeps = 300;
        /// <summary>Smoothing passes over the fitted weights before the guard.</summary>
        public const int SmoothPasses = 10;

        internal static void Fit(UMAWeightGraph graph, UMAWeightTarget target, UMAWeightDonor donor, UMAWeightSettings settings, IList<UMAWeightPose> poses, int B, float[] W, UMAWeightVertexState[] state, UMAWeightHit[] hits, UMAWeightReport report)
        {
            if (poses == null || poses.Count == 0) return;
            int nodes = graph.NodeCount;
            int P = poses.Count;
            // Each node's place in each pose: the mean of its vertices' simulated places.
            var posed = new Vector3[P][];
            var members = new int[nodes];
            for (int v = 0; v < target.VertexCount; v++) members[graph.NodeOf[v]]++;
            for (int p = 0; p < P; p++)
            {
                posed[p] = new Vector3[nodes];
                for (int v = 0; v < target.VertexCount; v++) posed[p][graph.NodeOf[v]] += poses[p].TargetPositions[v];
                for (int i = 0; i < nodes; i++) posed[p][i] /= Mathf.Max(1, members[i]);
            }
            var fit = new bool[nodes];
            for (int i = 0; i < nodes; i++)
            {
                UMAWeightVertexState s = state[i];
                if (s == UMAWeightVertexState.Locked || s == UMAWeightVertexState.Exact) continue;
                if (s == UMAWeightVertexState.Matched && hits[i].Found && hits[i].Distance <= settings.MatchDistance * 0.5f) continue;
                fit[i] = true;
            }

            // The posed bodies, when the poses carry them: where the simulated cloth touches the body, and the guard.
            bool bodies = donor != null;
            foreach (UMAWeightPose pose in poses) if (pose.DonorPositions == null || pose.DonorPositions.Length != donor?.Positions.Length) bodies = false;
            var surfaces = new UMAWeightSurface[P];
            var contact = new bool[P][];
            if (bodies)
            {
                for (int p = 0; p < P; p++)
                {
                    surfaces[p] = UMAWeightScoring.SurfaceOf(poses[p], donor);
                    contact[p] = new bool[nodes];
                    UMAWeightSurface surface = surfaces[p];
                    bool[] touches = contact[p];
                    Vector3[] at = posed[p];
                    Parallel.For(0, nodes, () => surface.NewQuery(), (i, loop, q) =>
                    {
                        if (fit[i]) touches[i] = surface.Closest(at[i], q, ContactDistance).Found;
                        return q;
                    }, _ => { });
                }
            }

            float[] start = (float[])W.Clone();
            float scale = P * 0.01f;
            float prior = Mathf.Max(0f, settings.FitPrior) * scale;
            float smooth = Mathf.Max(0f, settings.FitSmoothness) * scale;
            float sumPenalty = 10f * scale;
            float[] current = W;
            for (int round = 0; round < Mathf.Max(1, settings.FitIterations); round++)
            {
                float[] next = (float[])current.Clone();
                float[] read = current;
                Parallel.For(0, nodes, i =>
                {
                    if (!fit[i]) return;
                    var mean = new float[B];
                    int[] nb = graph.Neighbours[i];
                    foreach (int j in nb) for (int b = 0; b < B; b++) mean[b] += read[j * B + b];
                    if (nb.Length > 0) for (int b = 0; b < B; b++) mean[b] /= nb.Length;
                    // The candidate bones: the strongest of its own and its neighbours' weights.
                    var order = new List<int>();
                    for (int b = 0; b < B; b++) if (read[i * B + b] + mean[b] > 1e-3f) order.Add(b);
                    order.Sort((x, y) => (read[i * B + y] + mean[y]).CompareTo(read[i * B + x] + mean[x]));
                    if (order.Count > Candidates) order.RemoveRange(Candidates, order.Count - Candidates);
                    int m = order.Count;
                    if (m < 2) return;
                    var G = new double[m * m];
                    var g = new double[m];
                    var a = new Vector3[m];
                    Vector3 rest = graph.Position[i];
                    for (int p = 0; p < P; p++)
                    {
                        Matrix4x4[] S = poses[p].BoneMatrices;
                        float wp = bodies && contact[p][i] ? ContactBoost : 1f;
                        Vector3 centre = Vector3.zero;
                        for (int k = 0; k < m; k++) { a[k] = S[order[k]].MultiplyPoint3x4(rest); centre += a[k]; }
                        centre /= m;
                        Vector3 y = posed[p][i] - centre;
                        for (int k = 0; k < m; k++) a[k] -= centre;
                        for (int k = 0; k < m; k++)
                        {
                            g[k] += wp * Vector3.Dot(a[k], y);
                            for (int l = 0; l < m; l++) G[k * m + l] += wp * Vector3.Dot(a[k], a[l]);
                        }
                    }
                    for (int k = 0; k < m; k++)
                    {
                        int b = order[k];
                        G[k * m + k] += prior + smooth;
                        g[k] += prior * start[i * B + b] + smooth * mean[b];
                        for (int l = 0; l < m; l++) G[k * m + l] += sumPenalty;
                        g[k] += sumPenalty;
                    }
                    var w = new double[m];
                    for (int k = 0; k < m; k++) w[k] = read[i * B + order[k]];
                    for (int sweep = 0; sweep < Sweeps; sweep++)
                    {
                        double change = 0;
                        for (int k = 0; k < m; k++)
                        {
                            double r = g[k];
                            for (int l = 0; l < m; l++) if (l != k) r -= G[k * m + l] * w[l];
                            double v = Math.Max(0.0, r / G[k * m + k]);
                            change = Math.Max(change, Math.Abs(v - w[k]));
                            w[k] = v;
                        }
                        if (change < 1e-7) break;
                    }
                    double total = 0;
                    for (int k = 0; k < m; k++) total += w[k];
                    if (total <= 1e-9 || double.IsNaN(total)) return;
                    Array.Clear(next, i * B, B);
                    for (int k = 0; k < m; k++) next[i * B + order[k]] = (float)(w[k] / total);
                });
                current = next;
            }

            // Each vertex is fitted on its own, so neighbours can land on different compromises and the cloth
            // between them stretches: smooth the fitted weights over the cloth (convex averages keep the sum one).
            var active = new List<int>();
            for (int b = 0; b < B; b++)
                for (int i = 0; i < nodes; i++)
                    if (current[i * B + b] != 0f) { active.Add(b); break; }
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                float[] read = current;
                float[] next = (float[])current.Clone();
                Parallel.For(0, nodes, i =>
                {
                    int[] nb = graph.Neighbours[i];
                    if (!fit[i] || nb.Length == 0) return;
                    float inv = 1f / nb.Length;
                    foreach (int b in active)
                    {
                        float mean = 0f;
                        foreach (int j in nb) mean += read[j * B + b];
                        next[i * B + b] = 0.5f * read[i * B + b] + 0.5f * mean * inv;
                    }
                });
                current = next;
            }

            // The guard: fitted weights that skin a vertex into the posed body more often than its starting weights are
            // halved back towards those, and dropped if that is still worse.
            var kept = new bool[nodes];
            int reverted = 0, halved = 0;
            if (bodies)
            {
                float tol = settings.ScoreInsideTolerance;
                var outcome = new int[nodes];
                Parallel.For(0, nodes, () => new UMAWeightSurface.Query[P], (i, loop, queries) =>
                {
                    if (!fit[i]) return queries;
                    int InsideCount(float[] source, float blend)
                    {
                        int count = 0;
                        for (int p = 0; p < P; p++)
                        {
                            Matrix4x4[] S = poses[p].BoneMatrices;
                            Vector3 x = Vector3.zero;
                            float total = 0f;
                            for (int b = 0; b < B; b++)
                            {
                                float wb = blend * source[i * B + b] + (1f - blend) * start[i * B + b];
                                if (wb <= 0f || b >= S.Length) continue;
                                x += S[b].MultiplyPoint3x4(graph.Position[i]) * wb;
                                total += wb;
                            }
                            if (total <= 0f) continue;
                            if (queries[p] == null) queries[p] = surfaces[p].NewQuery();
                            if (surfaces[p].Closest(x / total, queries[p]).IsInside(tol)) count++;
                        }
                        return count;
                    }
                    int before = InsideCount(start, 1f);
                    if (InsideCount(current, 1f) <= before) { outcome[i] = 1; return queries; }
                    outcome[i] = InsideCount(current, 0.5f) <= before ? 2 : 3;
                    return queries;
                }, _ => { });
                for (int i = 0; i < nodes; i++)
                {
                    if (!fit[i]) continue;
                    if (outcome[i] == 1) { kept[i] = true; continue; }
                    if (outcome[i] == 2) { for (int b = 0; b < B; b++) current[i * B + b] = 0.5f * current[i * B + b] + 0.5f * start[i * B + b]; kept[i] = true; halved++; continue; }
                    Array.Copy(start, i * B, current, i * B, B);
                    reverted++;
                }
                if (halved + reverted > 0) report?.Messages.Add("Simulated Fit: " + halved + " vertices were eased back halfway and " + reverted + " kept their starting weights, where the fit would have put them into the body.");
            }
            else
            {
                for (int i = 0; i < nodes; i++) kept[i] = fit[i];
            }
            Array.Copy(current, W, W.Length);
            for (int i = 0; i < nodes; i++) if (kept[i]) state[i] = UMAWeightVertexState.Fitted;
        }
    }
}
