using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// A facing and a rim for the openings of baked clothes. The clothes are one layer of cloth; with a
    /// material that hides back faces, looking into the neck, an armhole or under the hem shows nothing.
    /// The facing is a turned-in copy of the cloth near each opening, its triangles facing inwards, to a
    /// depth measured along the cloth; the rim joins the opening's edge to the facing's edge, so the edge
    /// shows a thickness.
    /// </summary>
    public static class OpeningFacing
    {
        /// <summary>How far inside the cloth the facing lies without a rim (m): enough to keep it apart from the outside.</summary>
        public const float FacingGap = 0.0005f;

        public sealed class Output
        {
            /// <summary>The cloth's vertices followed by the facing's (and the rim's inner edge).</summary>
            public Vector3[] positions;
            public int[] triangles;
            public List<Vector2> uv;
            /// <summary>For every vertex, the cloth vertex it copies (itself for the cloth's own).</summary>
            public int[] sourceOf;
            public int facingTriangles, rimTriangles, addedVertices;
        }

        /// <param name="positions">The cloth's welded positions, in the space the slot is built from.</param>
        /// <param name="triangles">The cloth's triangles, facing out.</param>
        /// <param name="group">Each vertex's welded group: the facing carries on across sewn seams.</param>
        /// <param name="depth">How far into the cloth the facing reaches, along the cloth (m).</param>
        /// <param name="thickness">With a rim: how far inside the cloth the facing lies, the rim's width (m).</param>
        public static Output Build(PatternMesh mesh, ClothesPattern pattern, Vector3[] positions, int[] triangles, int[] group,
            bool facing, float depth, bool rim, float thickness)
        {
            int n = positions.Length;
            var output = new Output();
            var normals = new Vector3[n];
            new UMABuddyNormals(positions, triangles).Recalculate(positions, normals);

            // The opening's own segments and vertices, corners included.
            List<(int a, int b, int third)> segments = OpeningSegments(mesh, pattern, triangles, out bool[] atOpening);

            // Distance along the cloth from the nearest opening; welded copies are one point.
            float[] distance = DistanceFromOpenings(positions, triangles, group, atOpening);

            var copyOf = new Dictionary<int, int>();
            var positionsOut = new List<Vector3>(positions);
            var uvOut = new List<Vector2>(mesh.uv);
            var sourceOf = new List<int>(n);
            for (int v = 0; v < n; v++) sourceOf.Add(v);
            float gap = rim ? Mathf.Max(FacingGap, thickness) : FacingGap;
            int Copy(int v)
            {
                if (copyOf.TryGetValue(v, out int c)) return c;
                c = positionsOut.Count;
                positionsOut.Add(positions[v] - normals[v] * gap);
                uvOut.Add(mesh.uv[v]);
                sourceOf.Add(v);
                copyOf[v] = c;
                return c;
            }

            var trianglesOut = new List<int>(triangles);
            if (facing && depth > 0f)
            {
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (Mathf.Max(distance[a], Mathf.Max(distance[b], distance[c])) > depth) continue;
                    // Turned round: it faces into the clothes.
                    trianglesOut.Add(Copy(a)); trianglesOut.Add(Copy(c)); trianglesOut.Add(Copy(b));
                    output.facingTriangles++;
                }
            }

            if (rim)
            {
                // Each segment of the opening, joined to its copy by two triangles facing out of the opening.
                foreach ((int a, int b, int third) in segments)
                {
                    int a2 = Copy(a), b2 = Copy(b);
                    Vector3 outward = (positions[a] + positions[b]) * 0.5f - positions[third];
                    Vector3 face = Vector3.Cross(positions[b] - positions[a], positionsOut[b2] - positions[a]);
                    if (Vector3.Dot(face, outward) >= 0f)
                    {
                        trianglesOut.Add(a); trianglesOut.Add(b); trianglesOut.Add(b2);
                        trianglesOut.Add(a); trianglesOut.Add(b2); trianglesOut.Add(a2);
                    }
                    else
                    {
                        trianglesOut.Add(a); trianglesOut.Add(b2); trianglesOut.Add(b);
                        trianglesOut.Add(a); trianglesOut.Add(a2); trianglesOut.Add(b2);
                    }
                    output.rimTriangles += 2;
                }
            }

            output.positions = positionsOut.ToArray();
            output.triangles = trianglesOut.ToArray();
            output.uv = uvOut;
            output.sourceOf = sourceOf.ToArray();
            output.addedVertices = positionsOut.Count - n;
            return output;
        }

        /// <summary>
        /// The pattern edge a segment of a piece's outline runs along, or -1 (a dart leg, or not on the
        /// outline). An edge's points start at its first corner and stop short of its last, which is the
        /// next edge's first point; so a segment between points of two edges in a row runs along the first.
        /// Judging by the points alone would leave out the last segment of every edge.
        /// </summary>
        public static int SegmentEdge(PatternMesh mesh, ClothesPattern pattern, int a, int b)
        {
            int ea = mesh.boundaryEdge[a], eb = mesh.boundaryEdge[b];
            if (ea < 0 || eb < 0 || mesh.piece[a] != mesh.piece[b]) return -1;
            if (ea == eb) return ea;
            int count = pattern.pieces[mesh.piece[a]].edges.Count;
            if ((ea + 1) % count == eb) return ea;
            if ((eb + 1) % count == ea) return eb;
            return -1;
        }

        /// <summary>
        /// The outline segments (mesh edges used by one triangle) that run along an opening, each with the
        /// third point of its triangle, and every vertex on them.
        /// </summary>
        public static List<(int a, int b, int third)> OpeningSegments(PatternMesh mesh, ClothesPattern pattern, int[] triangles, out bool[] atOpening)
        {
            var edgeUse = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            var edgeThird = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k], b = triangles[t + (k + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    edgeUse[key] = edgeUse.TryGetValue(key, out int u) ? u + 1 : 1;
                    edgeThird[key] = triangles[t + (k + 2) % 3];
                }
            }
            var segments = new List<(int, int, int)>();
            atOpening = new bool[mesh.VertexCount];
            foreach (KeyValuePair<long, int> kv in edgeUse)
            {
                if (kv.Value != 1) continue;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                int e = SegmentEdge(mesh, pattern, a, b);
                // The rim of a hole cut in a piece is an opening too.
                bool hole = e < 0 && a < mesh.onHole.Count && b < mesh.onHole.Count && mesh.onHole[a] && mesh.onHole[b];
                if (e < 0 && !hole) continue;
                List<PatternEdge> edges = pattern.pieces[mesh.piece[a]].edges;
                if (!hole && (e >= edges.Count || !edges[e].opening)) continue;
                segments.Add((a, b, edgeThird[kv.Key]));
                atOpening[a] = atOpening[b] = true;
            }
            return segments;
        }

        /// <summary>Shortest distance along the cloth's edges from any opening vertex (m); welded copies share one distance.</summary>
        public static float[] DistanceFromOpenings(Vector3[] positions, int[] triangles, int[] group, bool[] atOpening)
        {
            int n = positions.Length;
            var neighbours = new List<int>[n];
            for (int v = 0; v < n; v++) neighbours[v] = new List<int>(6);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k], b = triangles[t + (k + 1) % 3];
                    neighbours[group[a]].Add(group[b]);
                    neighbours[group[b]].Add(group[a]);
                }
            }
            var best = new float[n];
            for (int v = 0; v < n; v++) best[v] = float.MaxValue;
            var queue = new SortedSet<(float, int)>();
            for (int v = 0; v < n; v++)
            {
                if (!atOpening[v]) continue;
                int g = group[v];
                if (best[g] == 0f) continue;
                best[g] = 0f;
                queue.Add((0f, g));
            }
            while (queue.Count > 0)
            {
                (float d, int g) = queue.Min;
                queue.Remove(queue.Min);
                if (d > best[g]) continue;
                foreach (int h in neighbours[g])
                {
                    float nd = d + Vector3.Distance(positions[g], positions[h]);
                    if (nd >= best[h]) continue;
                    if (best[h] < float.MaxValue) queue.Remove((best[h], h));
                    best[h] = nd;
                    queue.Add((nd, h));
                }
            }
            var result = new float[n];
            for (int v = 0; v < n; v++) result[v] = best[group[v]];
            return result;
        }
    }
}
