using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// The target mesh as the weighting sees it: one node per point of the surface. Vertices at the same position
    /// (battle buddies: the copies along a UV island edge, sewn seams) are one node, so they always end with one set
    /// of weights. Each node knows its neighbours, its area and the cotangent weight of each edge.
    /// </summary>
    public sealed class UMAWeightGraph
    {
        /// <summary>The node of each vertex.</summary>
        public int[] NodeOf;
        /// <summary>The first vertex of each node.</summary>
        public int[] FirstVertex;
        public Vector3[] Position;
        /// <summary>Area-weighted node normals (zero where opposite layers cancel).</summary>
        public Vector3[] Normal;
        public int[][] Neighbours;
        /// <summary>Cotangent weight of each neighbour edge, aligned with <see cref="Neighbours"/> (never below a small floor).</summary>
        public double[][] EdgeWeight;
        /// <summary>A third of the area of the triangles round each node (lumped mass).</summary>
        public double[] Mass;
        /// <summary>Node triangles (three node indices each), degenerate ones left out.</summary>
        public int[] Triangles;
        public int NodeCount => Position.Length;

        /// <summary>The position key that decides which vertices are one point (0.01 mm).</summary>
        public static Vector3Int PositionKey(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));

        public static UMAWeightGraph Build(Vector3[] positions, int[] triangles, int[] groups = null, Vector3[] normals = null)
        {
            int n = positions.Length;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }

            var first = new Dictionary<Vector3Int, int>();
            for (int v = 0; v < n; v++)
            {
                Vector3Int k = PositionKey(positions[v]);
                if (first.TryGetValue(k, out int o)) Union(o, v); else first[k] = v;
            }
            if (groups != null && groups.Length == n)
            {
                for (int v = 0; v < n; v++) if (groups[v] >= 0 && groups[v] < n) Union(v, groups[v]);
            }

            var g = new UMAWeightGraph { NodeOf = new int[n] };
            var nodeOfRoot = new Dictionary<int, int>();
            var firstVertex = new List<int>();
            for (int v = 0; v < n; v++)
            {
                int r = Find(v);
                if (!nodeOfRoot.TryGetValue(r, out int node)) { node = firstVertex.Count; nodeOfRoot[r] = node; firstVertex.Add(v); }
                g.NodeOf[v] = node;
            }
            int nodes = firstVertex.Count;
            g.FirstVertex = firstVertex.ToArray();
            g.Position = new Vector3[nodes];
            var count = new int[nodes];
            for (int v = 0; v < n; v++) { g.Position[g.NodeOf[v]] += positions[v]; count[g.NodeOf[v]]++; }
            for (int i = 0; i < nodes; i++) g.Position[i] /= Mathf.Max(1, count[i]);

            var tris = new List<int>(triangles != null ? triangles.Length : 0);
            if (triangles != null)
            {
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if ((uint)a >= n || (uint)b >= n || (uint)c >= n) continue;
                    a = g.NodeOf[a]; b = g.NodeOf[b]; c = g.NodeOf[c];
                    if (a == b || b == c || c == a) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
            }
            g.Triangles = tris.ToArray();

            // Normals: the given ones averaged per node, or area-weighted from the triangles.
            g.Normal = new Vector3[nodes];
            if (normals != null && normals.Length == n)
            {
                for (int v = 0; v < n; v++) g.Normal[g.NodeOf[v]] += normals[v];
            }
            else
            {
                for (int t = 0; t + 2 < g.Triangles.Length; t += 3)
                {
                    int a = g.Triangles[t], b = g.Triangles[t + 1], c = g.Triangles[t + 2];
                    Vector3 nn = Vector3.Cross(g.Position[b] - g.Position[a], g.Position[c] - g.Position[a]);
                    g.Normal[a] += nn; g.Normal[b] += nn; g.Normal[c] += nn;
                }
            }
            for (int i = 0; i < nodes; i++) g.Normal[i] = g.Normal[i].sqrMagnitude > 1e-20f ? g.Normal[i].normalized : Vector3.zero;

            // Edges with cotangent weights, and the lumped mass.
            var edges = new Dictionary<long, double>();
            g.Mass = new double[nodes];
            long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            void AddCot(int a, int b, Vector3 pa, Vector3 pb, Vector3 opposite)
            {
                Vector3 u = pa - opposite, w = pb - opposite;
                double cross = Vector3.Cross(u, w).magnitude;
                double cot = cross > 1e-12 ? Vector3.Dot(u, w) / cross : 0.0;
                long k = Key(a, b);
                edges.TryGetValue(k, out double s);
                edges[k] = s + 0.5 * cot;
            }
            for (int t = 0; t + 2 < g.Triangles.Length; t += 3)
            {
                int a = g.Triangles[t], b = g.Triangles[t + 1], c = g.Triangles[t + 2];
                Vector3 pa = g.Position[a], pb = g.Position[b], pc = g.Position[c];
                AddCot(b, c, pb, pc, pa);
                AddCot(c, a, pc, pa, pb);
                AddCot(a, b, pa, pb, pc);
                double area = 0.5 * Vector3.Cross(pb - pa, pc - pa).magnitude;
                g.Mass[a] += area / 3.0; g.Mass[b] += area / 3.0; g.Mass[c] += area / 3.0;
            }
            double meanMass = 0;
            for (int i = 0; i < nodes; i++) meanMass += g.Mass[i];
            meanMass = nodes > 0 ? meanMass / nodes : 1e-4;
            for (int i = 0; i < nodes; i++) if (g.Mass[i] < meanMass * 1e-3 || g.Mass[i] <= 0) g.Mass[i] = Math.Max(meanMass * 1e-3, 1e-10);

            var lists = new List<int>[nodes];
            var weights = new List<double>[nodes];
            for (int i = 0; i < nodes; i++) { lists[i] = new List<int>(6); weights[i] = new List<double>(6); }
            foreach (KeyValuePair<long, double> e in edges)
            {
                int a = (int)(e.Key >> 32), b = (int)(e.Key & 0xffffffff);
                // Obtuse triangles give negative cotangents; a small floor keeps every edge pulling (and the fill positive).
                double w = Math.Max(e.Value, 0.05);
                lists[a].Add(b); weights[a].Add(w);
                lists[b].Add(a); weights[b].Add(w);
            }
            g.Neighbours = new int[nodes][];
            g.EdgeWeight = new double[nodes][];
            for (int i = 0; i < nodes; i++) { g.Neighbours[i] = lists[i].ToArray(); g.EdgeWeight[i] = weights[i].ToArray(); }
            return g;
        }

        /// <summary>The node normals averaged over a number of rings of neighbours, normalized.</summary>
        public Vector3[] SmoothedNormals(int rings)
        {
            Vector3[] current = (Vector3[])Normal.Clone();
            for (int r = 0; r < rings; r++)
            {
                var next = new Vector3[current.Length];
                for (int i = 0; i < current.Length; i++)
                {
                    Vector3 s = current[i];
                    foreach (int j in Neighbours[i]) s += current[j];
                    next[i] = s;
                }
                for (int i = 0; i < next.Length; i++) next[i] = next[i].sqrMagnitude > 1e-20f ? next[i].normalized : Vector3.zero;
                current = next;
            }
            return current;
        }

        /// <summary>The connected part of the mesh each node is in.</summary>
        public int[] Components(out int count)
        {
            var comp = new int[NodeCount];
            for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            count = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < comp.Length; s++)
            {
                if (comp[s] >= 0) continue;
                comp[s] = count;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    foreach (int j in Neighbours[i]) if (comp[j] < 0) { comp[j] = count; stack.Push(j); }
                }
                count++;
            }
            return comp;
        }

        /// <summary>Each edge once (a &lt; b), for stretch measurements.</summary>
        public List<Vector2Int> Edges()
        {
            var list = new List<Vector2Int>();
            for (int i = 0; i < NodeCount; i++) foreach (int j in Neighbours[i]) if (i < j) list.Add(new Vector2Int(i, j));
            return list;
        }
    }
}
