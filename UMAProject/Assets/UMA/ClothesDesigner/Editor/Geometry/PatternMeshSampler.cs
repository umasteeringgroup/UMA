using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Finds where a point of the drawn pattern lies on a cloth mesh: its triangle and barycentric
    /// weights. Every mesh level of a pattern is made from the same drawn pieces, so this carries a drape
    /// (or anything else per vertex) from one mesh level to another.
    /// </summary>
    public sealed class PatternMeshSampler
    {
        private readonly PatternMesh mesh;
        private readonly float cell;
        private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);

        public PatternMeshSampler(PatternMesh mesh)
        {
            this.mesh = mesh;
            cell = Mathf.Max(0.5f, mesh.edgeLengthCm > 0f ? mesh.edgeLengthCm * 2f : 2f);
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.drawn[mesh.triangles[t]], b = mesh.drawn[mesh.triangles[t + 1]], c = mesh.drawn[mesh.triangles[t + 2]];
                int piece = mesh.piece[mesh.triangles[t]];
                Vector2 min = Vector2.Min(a, Vector2.Min(b, c)), max = Vector2.Max(a, Vector2.Max(b, c));
                for (int x = Mathf.FloorToInt(min.x / cell); x <= Mathf.FloorToInt(max.x / cell); x++)
                {
                    for (int y = Mathf.FloorToInt(min.y / cell); y <= Mathf.FloorToInt(max.y / cell); y++)
                    {
                        long key = Key(piece, x, y);
                        if (!grid.TryGetValue(key, out List<int> list)) grid[key] = list = new List<int>();
                        list.Add(t);
                    }
                }
            }
        }

        private static long Key(int piece, int x, int y) => ((long)piece << 40) | ((long)(x + (1 << 19)) << 20) | (long)(y + (1 << 19));

        /// <summary>
        /// The triangle (index of its first corner in the triangle list) holding a drawn point of a piece,
        /// and the point's barycentric weights. A point just off the mesh (a curved outline between its
        /// samples) takes the nearest point of the nearest triangle. False when the piece has no triangle near.
        /// </summary>
        public bool Locate(int piece, Vector2 drawn, out int triangle, out Vector3 barycentric)
        {
            triangle = -1; barycentric = Vector3.zero;
            int cx = Mathf.FloorToInt(drawn.x / cell), cy = Mathf.FloorToInt(drawn.y / cell);
            if (grid.TryGetValue(Key(piece, cx, cy), out List<int> here))
            {
                foreach (int t in here)
                {
                    Vector3 w = Barycentric(t, drawn);
                    if (w.x >= -1e-4f && w.y >= -1e-4f && w.z >= -1e-4f) { triangle = t; barycentric = Clamp(w); return true; }
                }
            }
            float best = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue(Key(piece, cx + dx, cy + dy), out List<int> list)) continue;
                    foreach (int t in list)
                    {
                        Vector2 a = mesh.drawn[mesh.triangles[t]], b = mesh.drawn[mesh.triangles[t + 1]], c = mesh.drawn[mesh.triangles[t + 2]];
                        Vector2 q = ClosestOnTriangle(drawn, a, b, c);
                        float d = (q - drawn).sqrMagnitude;
                        if (d < best) { best = d; triangle = t; barycentric = Clamp(Barycentric(t, q)); }
                    }
                }
            }
            return triangle >= 0;
        }

        /// <summary>A per-vertex value of the mesh at a drawn point of a piece; zero when the piece has no triangle near.</summary>
        public Vector3 Sample(Vector3[] values, int piece, Vector2 drawn)
        {
            if (!Locate(piece, drawn, out int t, out Vector3 w)) return Vector3.zero;
            return values[mesh.triangles[t]] * w.x + values[mesh.triangles[t + 1]] * w.y + values[mesh.triangles[t + 2]] * w.z;
        }

        /// <summary>Positions for every vertex of <paramref name="to"/>, taken from <paramref name="positions"/> on this mesh through the flat pattern.</summary>
        public Vector3[] Carry(Vector3[] positions, PatternMesh to) => Carry(positions, to, null);

        /// <summary>
        /// As <see cref="Carry(Vector3[], PatternMesh)"/>, for a pattern some of whose pieces were resized in between:
        /// <paramref name="drawnBefore"/> gives, for a piece and a point of it as drawn now, where that point was drawn on
        /// this mesh's pattern (null: in the same place).
        /// </summary>
        public Vector3[] Carry(Vector3[] positions, PatternMesh to, System.Func<int, Vector2, Vector2> drawnBefore)
        {
            var result = new Vector3[to.VertexCount];
            for (int v = 0; v < to.VertexCount; v++)
            {
                int piece = to.piece[v];
                result[v] = Sample(positions, piece, drawnBefore != null ? drawnBefore(piece, to.drawn[v]) : to.drawn[v]);
            }
            return result;
        }

        private Vector3 Barycentric(int t, Vector2 p)
        {
            Vector2 a = mesh.drawn[mesh.triangles[t]], b = mesh.drawn[mesh.triangles[t + 1]], c = mesh.drawn[mesh.triangles[t + 2]];
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-12f) return new Vector3(1f, 0f, 0f);
            float w0 = ((b.x - p.x) * (c.y - p.y) - (b.y - p.y) * (c.x - p.x)) / area;
            float w1 = ((c.x - p.x) * (a.y - p.y) - (c.y - p.y) * (a.x - p.x)) / area;
            return new Vector3(w0, w1, 1f - w0 - w1);
        }

        private static Vector3 Clamp(Vector3 w)
        {
            w = new Vector3(Mathf.Max(0f, w.x), Mathf.Max(0f, w.y), Mathf.Max(0f, w.z));
            float s = w.x + w.y + w.z;
            return s > 1e-9f ? w / s : new Vector3(1f, 0f, 0f);
        }

        private static Vector2 ClosestOnTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // Inside: the point itself. Otherwise the nearest point on the three edges.
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            if (!(neg && pos)) return p;
            Vector2 best = OnSegment(p, a, b);
            Vector2 q = OnSegment(p, b, c);
            if ((q - p).sqrMagnitude < (best - p).sqrMagnitude) best = q;
            q = OnSegment(p, c, a);
            if ((q - p).sqrMagnitude < (best - p).sqrMagnitude) best = q;
            return best;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        private static Vector2 OnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }
    }
}
