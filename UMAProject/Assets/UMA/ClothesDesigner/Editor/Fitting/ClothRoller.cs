using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Roller: lifts the sheet of cloth the artist presses on out over the cloth behind it, where the
    /// two lie closer than the cloth thickness or have crossed (a collar's leaf sunk into its stand, a pleat's
    /// fold poking through). The solver does not keep a folded seam's two pieces apart, and keeps any other
    /// cloth on whichever side it was sewn, so a crossing left by the drape stays until it is rolled out.
    /// </summary>
    public static class ClothRoller
    {
        /// <summary>How far behind (or in front of) the pressed sheet other cloth is looked for, at least (m).</summary>
        public const float MinReachM = 0.015f;
        /// <summary>The most one stroke step lifts a point (m): a guard against a wrong match far away.</summary>
        public const float MaxLiftM = 0.02f;
        /// <summary>The steepest the lifted sheet rises from the cloth round it (lift per length along the cloth).</summary>
        public const float MaxSlope = 0.4f;
        private const int SpreadPasses = 12;
        /// <summary>The sheet ends where the cloth turns more than 60 degrees from one point to the next.</summary>
        private const float SheetTurnCos = 0.5f;

        public struct Result
        {
            /// <summary>Points of the pressed sheet under the brush.</summary>
            public int sheet;
            /// <summary>Points that were lifted.</summary>
            public int lifted;
            /// <summary>The largest lift (m), before the brush's falloff.</summary>
            public float maxLift;
        }

        /// <summary>
        /// One dab of the roller at <paramref name="start"/>. The pressed sheet is the cloth connected to it
        /// within <paramref name="radius"/> that does not turn back on itself (the walk stops where the
        /// cloth folds over, at a collar's fold line or a pleat's crease); everything else near the brush is
        /// the cloth behind. Each point of the sheet lying closer than <paramref name="thickness"/> in front
        /// of the cloth behind it, or behind it, is moved out (away from the body, by
        /// <paramref name="outward"/>) until it is the thickness clear, softly towards the brush's edge.
        /// Cloth within two rings of a point (across stitches too) is not counted as behind it: next to a seam
        /// the pieces meet closer than any thickness. Moves <paramref name="positions"/> in place.
        /// </summary>
        public static Result Roll(Vector3[] positions, int[] triangles, IList<List<int>> neighbours, Func<Vector3, Vector3> outward,
            int start, float radius, float thickness, float strength)
        {
            Result result = Lifts(positions, triangles, neighbours, outward, start, radius, thickness, strength, out Dictionary<int, Vector3> moves);
            foreach (var kv in moves) positions[kv.Key] += kv.Value;
            return result;
        }

        /// <summary>
        /// What <see cref="Roll"/> would move each point by, without moving it. A stroke works out every dab
        /// from the cloth as it was when the stroke began and keeps each point's largest move, so dabs that
        /// overlap do not add up.
        /// </summary>
        public static Result Lifts(Vector3[] positions, int[] triangles, IList<List<int>> neighbours, Func<Vector3, Vector3> outward,
            int start, float radius, float thickness, float strength, out Dictionary<int, Vector3> moves)
        {
            var result = new Result();
            moves = new Dictionary<int, Vector3>();
            int n = positions.Length;
            if (start < 0 || start >= n || radius <= 0f || thickness <= 0f || strength <= 0f) return result;
            float reach = Mathf.Max(MinReachM, thickness * 3f);
            Vector3 centre = positions[start];
            float near = radius + reach;

            // Normals as meshed (not turned out from the body): where the cloth folds over, they turn round.
            var normals = new Vector3[n];
            var nearTriangles = new List<int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if ((positions[a] - centre).sqrMagnitude > near * near && (positions[b] - centre).sqrMagnitude > near * near && (positions[c] - centre).sqrMagnitude > near * near) continue;
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                normals[a] += face; normals[b] += face; normals[c] += face;
                nearTriangles.Add(t);
            }

            // The pressed sheet: a walk over the cloth from the pressed point, not across a fold. It goes on a
            // little past the brush, so the sheet's own cloth just outside it is not taken for cloth behind.
            var weight = new Dictionary<int, float>();
            var sameSheet = new HashSet<int>();
            var distance = new Dictionary<int, float> { [start] = 0f };
            var queue = new SortedSet<(float, int)> { (0f, start) };
            float walk = radius + reach;
            while (queue.Count > 0)
            {
                (float d, int i) = queue.Min;
                queue.Remove(queue.Min);
                if (d > walk) break;
                if (distance.TryGetValue(i, out float known) && known < d) continue;
                sameSheet.Add(i);
                if (d <= radius)
                {
                    float u = d / radius;
                    weight[i] = 1f - u * u * (3f - 2f * u);
                }
                foreach (int j in neighbours[i])
                {
                    // Not across a fold, nor round a turn sharper than SheetTurnCos allows.
                    float ni = normals[i].magnitude, nj = normals[j].magnitude;
                    if (ni < 1e-10f || nj < 1e-10f || Vector3.Dot(normals[i], normals[j]) <= SheetTurnCos * ni * nj) continue;
                    float nd = d + Vector3.Distance(positions[i], positions[j]);
                    if (nd <= walk && (!distance.TryGetValue(j, out float old) || nd < old))
                    {
                        distance[j] = nd;
                        queue.Add((nd, j));
                    }
                }
            }
            result.sheet = weight.Count;

            // The cloth behind: the triangles near the brush with no point on the sheet, facing out from the body.
            var behind = new List<int>();
            var behindOut = new List<Vector3>();
            foreach (int t in nearTriangles)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (sameSheet.Contains(a) || sameSheet.Contains(b) || sameSheet.Contains(c)) continue;
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (face.sqrMagnitude < 1e-16f) continue;
                face.Normalize();
                Vector3 centroid = (positions[a] + positions[b] + positions[c]) / 3f;
                if (Vector3.Dot(face, outward(centroid)) < 0f) face = -face;
                behind.Add(t);
                behindOut.Add(face);
            }
            if (behind.Count == 0) return result;

            // How far each sheet point must move out.
            var lift = new Dictionary<int, float>();
            var direction = new Dictionary<int, Vector3>();
            var ring = new HashSet<int>();
            foreach (int f in weight.Keys)
            {
                TwoRings(f, neighbours, ring);
                Vector3 q = positions[f];
                float best = float.MaxValue, bestSigned = 0f;
                Vector3 bestOut = Vector3.zero;
                for (int k = 0; k < behind.Count; k++)
                {
                    int t = behind[k];
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (ring.Contains(a) || ring.Contains(b) || ring.Contains(c)) continue;
                    Vector3 nOut = behindOut[k];
                    float s = Vector3.Dot(q - positions[a], nOut);
                    if (Mathf.Abs(s) >= reach || Mathf.Abs(s) >= best) continue;
                    if (!OverTriangle(q - nOut * s, positions[a], positions[b], positions[c])) continue;
                    best = Mathf.Abs(s); bestSigned = s; bestOut = nOut;
                }
                if (best == float.MaxValue || bestSigned >= thickness || normals[f].sqrMagnitude < 1e-20f) continue;
                // Along the sheet's own normal, turned away from the cloth behind: neighbours share it, so the
                // sheet moves as one instead of each point along whichever triangle happens to lie under it.
                Vector3 own = normals[f].normalized;
                if (Vector3.Dot(own, bestOut) < 0f) own = -own;
                float need = Mathf.Min((thickness - bestSigned) / Mathf.Max(0.5f, Vector3.Dot(own, bestOut)), MaxLiftM);
                result.maxLift = Mathf.Max(result.maxLift, need);
                lift[f] = need * weight[f] * Mathf.Clamp01(strength);
                direction[f] = own;
            }
            if (lift.Count == 0) return result;

            // Carried onto the sheet round each lifted point, so the sheet rises in a gentle slope (at most
            // MaxSlope of lift per length along the cloth, about 8% stretch) instead of one point standing up.
            for (int pass = 0; pass < SpreadPasses; pass++)
            {
                bool changed = false;
                foreach (int f in weight.Keys)
                {
                    if (!lift.TryGetValue(f, out float lf)) continue;
                    foreach (int j in neighbours[f])
                    {
                        if (!weight.ContainsKey(j) || normals[j].sqrMagnitude < 1e-20f) continue;
                        float carried = lf - MaxSlope * Vector3.Distance(positions[f], positions[j]);
                        if (carried <= 0f || (lift.TryGetValue(j, out float lj) && lj >= carried)) continue;
                        lift[j] = carried;
                        if (!direction.ContainsKey(j))
                        {
                            Vector3 own = normals[j].normalized;
                            direction[j] = Vector3.Dot(own, direction[f]) < 0f ? -own : own;
                        }
                        changed = true;
                    }
                }
                if (!changed) break;
            }

            // Where the sheet ends against other cloth (sewn to it at a collar's fold line or a seam, or where it
            // turns sharply), its points stay where they are and the sheet rises from them at the same gentle
            // slope: lifting them would pull the seam open, or push them along the cloth past the turn.
            var cap = new Dictionary<int, float>();
            foreach (int f in weight.Keys)
            {
                foreach (int j in neighbours[f])
                {
                    if (!sameSheet.Contains(j)) { cap[f] = 0f; break; }
                }
            }
            for (int pass = 0; pass < SpreadPasses && cap.Count > 0; pass++)
            {
                bool changed = false;
                foreach (int f in new List<int>(cap.Keys))
                {
                    float cf = cap[f];
                    foreach (int j in neighbours[f])
                    {
                        if (!weight.ContainsKey(j)) continue;
                        float limit = cf + MaxSlope * Vector3.Distance(positions[f], positions[j]);
                        if (cap.TryGetValue(j, out float cj) && cj <= limit) continue;
                        cap[j] = limit;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
            foreach (var kv in cap)
            {
                if (lift.TryGetValue(kv.Key, out float l) && l > kv.Value) lift[kv.Key] = kv.Value;
            }

            foreach (var kv in lift)
            {
                if (kv.Value <= 0f) continue;
                moves[kv.Key] = direction[kv.Key] * kv.Value;
                result.lifted++;
            }
            return result;
        }

        /// <summary>A point and the points within two steps of it (across stitches too).</summary>
        private static void TwoRings(int i, IList<List<int>> neighbours, HashSet<int> ring)
        {
            ring.Clear();
            ring.Add(i);
            foreach (int j in neighbours[i])
            {
                ring.Add(j);
                foreach (int k in neighbours[j]) ring.Add(k);
            }
        }

        /// <summary>Whether a point in a triangle's plane lies inside it.</summary>
        private static bool OverTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 u = b - a, w = c - a, v = p - a;
            float d00 = Vector3.Dot(u, u), d01 = Vector3.Dot(u, w), d11 = Vector3.Dot(w, w), d20 = Vector3.Dot(v, u), d21 = Vector3.Dot(v, w);
            float den = d00 * d11 - d01 * d01;
            if (Mathf.Abs(den) < 1e-20f) return false;
            float bv = (d11 * d20 - d01 * d21) / den, bw = (d00 * d21 - d01 * d20) / den;
            const float slack = 0.02f;
            return bv >= -slack && bw >= -slack && bv + bw <= 1f + slack;
        }
    }
}
