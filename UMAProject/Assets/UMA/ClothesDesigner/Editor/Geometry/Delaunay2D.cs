using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Incremental Bowyer-Watson Delaunay triangulation in double precision. Points are located by
    /// walking from the last triangle made, and each insertion only rebuilds its cavity.
    /// </summary>
    public sealed class Delaunay2D
    {
        private readonly List<double> xs = new List<double>();
        private readonly List<double> ys = new List<double>();
        private readonly List<int> tv = new List<int>();   // 3 per triangle
        private readonly List<int> tn = new List<int>();   // neighbour opposite each vertex, -1 none
        private readonly List<bool> alive = new List<bool>();
        private int last;

        public Delaunay2D(Rect bounds)
        {
            double cx = bounds.center.x, cy = bounds.center.y;
            double size = Math.Max(bounds.width, bounds.height) * 20.0 + 100.0;
            AddRaw(cx - size, cy - size);
            AddRaw(cx + size, cy - size);
            AddRaw(cx, cy + size);
            NewTriangle(0, 1, 2, -1, -1, -1);
        }

        public int SuperVertexCount => 3;

        private void AddRaw(double x, double y) { xs.Add(x); ys.Add(y); }

        private int NewTriangle(int a, int b, int c, int na, int nb, int nc)
        {
            int t = alive.Count;
            tv.Add(a); tv.Add(b); tv.Add(c);
            tn.Add(na); tn.Add(nb); tn.Add(nc);
            alive.Add(true);
            return t;
        }

        private double Orient(int a, int b, double px, double py) => (xs[b] - xs[a]) * (py - ys[a]) - (ys[b] - ys[a]) * (px - xs[a]);

        private bool InCircumcircle(int t, double px, double py)
        {
            int a = tv[3 * t], b = tv[3 * t + 1], c = tv[3 * t + 2];
            double ax = xs[a] - px, ay = ys[a] - py;
            double bx = xs[b] - px, by = ys[b] - py;
            double cx = xs[c] - px, cy = ys[c] - py;
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return det > 1e-12;
        }

        private int Locate(double px, double py)
        {
            int t = last >= 0 && last < alive.Count && alive[last] ? last : FirstAlive();
            for (int guard = 0; guard < alive.Count * 3 + 10; guard++)
            {
                bool moved = false;
                for (int k = 0; k < 3; k++)
                {
                    int a = tv[3 * t + (k + 1) % 3], b = tv[3 * t + (k + 2) % 3];
                    if (Orient(a, b, px, py) < 0)
                    {
                        int n = tn[3 * t + k];
                        if (n >= 0) { t = n; moved = true; break; }
                    }
                }
                if (!moved) return t;
            }
            // The walk went round in circles (degenerate input): look everywhere.
            for (int t2 = 0; t2 < alive.Count; t2++)
            {
                if (!alive[t2]) continue;
                bool inside = true;
                for (int k = 0; k < 3 && inside; k++)
                {
                    int a = tv[3 * t2 + (k + 1) % 3], b = tv[3 * t2 + (k + 2) % 3];
                    if (Orient(a, b, px, py) < -1e-12) inside = false;
                }
                if (inside) return t2;
            }
            return FirstAlive();
        }

        private int FirstAlive()
        {
            for (int t = 0; t < alive.Count; t++) if (alive[t]) return t;
            return -1;
        }

        /// <summary>Adds a point and returns its index (points are numbered from 0 in the order added).</summary>
        public int Insert(Vector2 p)
        {
            double px = p.x, py = p.y;
            int index = xs.Count;
            AddRaw(px, py);
            int start = Locate(px, py);
            var cavity = new List<int>();
            var inCavity = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start); inCavity.Add(start);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                cavity.Add(t);
                for (int k = 0; k < 3; k++)
                {
                    int n = tn[3 * t + k];
                    if (n >= 0 && !inCavity.Contains(n) && InCircumcircle(n, px, py))
                    {
                        inCavity.Add(n); queue.Enqueue(n);
                    }
                }
            }
            // The cavity's outline, edge (a, b) counter-clockwise, with the triangle outside it.
            var edgesA = new List<int>(); var edgesB = new List<int>(); var outer = new List<int>();
            foreach (int t in cavity)
            {
                for (int k = 0; k < 3; k++)
                {
                    int n = tn[3 * t + k];
                    if (n >= 0 && inCavity.Contains(n)) continue;
                    edgesA.Add(tv[3 * t + (k + 1) % 3]);
                    edgesB.Add(tv[3 * t + (k + 2) % 3]);
                    outer.Add(n);
                }
                alive[t] = false;
            }
            var byFirst = new Dictionary<int, int>();
            var bySecond = new Dictionary<int, int>();
            var made = new int[edgesA.Count];
            for (int i = 0; i < edgesA.Count; i++)
            {
                int t = NewTriangle(edgesA[i], edgesB[i], index, -1, -1, outer[i]);
                made[i] = t;
                byFirst[edgesA[i]] = t;
                bySecond[edgesB[i]] = t;
                int o = outer[i];
                if (o >= 0)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tv[3 * o + (k + 1) % 3], b = tv[3 * o + (k + 2) % 3];
                        if (a == edgesB[i] && b == edgesA[i]) { tn[3 * o + k] = t; break; }
                    }
                }
            }
            for (int i = 0; i < made.Length; i++)
            {
                int t = made[i];
                int a = tv[3 * t], b = tv[3 * t + 1];
                tn[3 * t] = byFirst.TryGetValue(b, out int na) ? na : -1;      // across (b, p)
                tn[3 * t + 1] = bySecond.TryGetValue(a, out int nb) ? nb : -1; // across (p, a)
            }
            last = made.Length > 0 ? made[0] : last;
            return index - 3;
        }

        /// <summary>The triangles that do not touch the starting super-triangle, as point indices, counter-clockwise.</summary>
        public List<int> Triangles()
        {
            var result = new List<int>();
            for (int t = 0; t < alive.Count; t++)
            {
                if (!alive[t]) continue;
                int a = tv[3 * t], b = tv[3 * t + 1], c = tv[3 * t + 2];
                if (a < 3 || b < 3 || c < 3) continue;
                result.Add(a - 3); result.Add(b - 3); result.Add(c - 3);
            }
            return result;
        }
    }
}
