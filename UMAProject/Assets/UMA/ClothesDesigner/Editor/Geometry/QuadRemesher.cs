using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>How the baked slot is meshed.</summary>
    public enum SlotTopology
    {
        /// <summary>The triangles the cloth was simulated on (or made at the slot's own mesh level).</summary>
        Triangles = 0,
        /// <summary>Mostly quads: a grid along the grain and loops along the outlines, a few triangles where they meet.</summary>
        Quads = 1,
        /// <summary>Only quads: made at twice the spacing, then every face split into quads.</summary>
        AllQuads = 2
    }

    /// <summary>
    /// A quad mesh made from the pattern, as Retopo Planes and similar tools make one from a garment's flat pattern
    /// layout, but from the pattern itself: each piece is meshed flat with a grid along its grain and an edge loop
    /// along its outline, its triangles are paired into quads, and (for all quads) every face is split into quads.
    /// Seams, darts, pleats and lines stay where they are, matched vertex to vertex as in any cloth mesh, and the
    /// UVs come from the pattern. A drape is carried onto it through the flat pattern (<see cref="PatternMeshSampler"/>).
    /// </summary>
    public static class QuadRemesher
    {
        /// <summary>Corners further than this from a right angle (degrees) are not made into a quad.</summary>
        public const float MaxCornerOffDeg = 55f;

        /// <summary>The quad mesh of a pattern at an edge length (cm).</summary>
        public static PatternMesh Build(ClothesPattern pattern, PieceScale[] scales, float edgeLengthCm, bool allQuads)
        {
            float h = Mathf.Max(0.3f, edgeLengthCm) * (allQuads ? 2f : 1f);
            PatternMesh mesh = PatternMesher.Build(pattern, scales, h, MeshTopology.GrainGrid, true, allQuads ? 2f : 1f);
            PairTriangles(mesh);
            if (!allQuads) return mesh;
            PatternMesh split = SplitIntoQuads(pattern, mesh);
            split.edgeLengthCm = Mathf.Max(0.3f, edgeLengthCm);
            split.revision = mesh.revision + "/quads";
            return split;
        }

        /// <summary>
        /// Pairs neighbouring triangles into quads where the pair makes a convex quad with corners near right angles,
        /// best pairs first, never across an edge something holds (a seam, a dart, a pleat or a sewn line: they keep
        /// their edges, with a stitch at every vertex). The quads become the first triangles, two each.
        /// </summary>
        public static void PairTriangles(PatternMesh mesh)
        {
            int triCount = mesh.triangles.Count / 3;
            var held = HeldEdges(mesh);
            var edgeTris = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);
            for (int t = 0; t < triCount; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    long key = Key(mesh.triangles[t * 3 + e], mesh.triangles[t * 3 + (e + 1) % 3]);
                    if (!edgeTris.TryGetValue(key, out List<int> list)) edgeTris[key] = list = new List<int>(2);
                    list.Add(t);
                }
            }
            var candidates = new List<(float quality, int t1, int t2, int q0, int q1, int q2, int q3)>();
            foreach (KeyValuePair<long, List<int>> kv in edgeTris)
            {
                if (kv.Value.Count != 2 || held.Contains(kv.Key)) continue;
                int t1 = kv.Value[0], t2 = kv.Value[1];
                if (mesh.piece[mesh.triangles[t1 * 3]] != mesh.piece[mesh.triangles[t2 * 3]]) continue;
                // t1 runs u -> v -> o1; t2 has v -> u -> o2. The quad, counter-clockwise: u, o2, v, o1.
                if (!Corners(mesh, t1, kv.Key, out int u, out int v, out int o1)) continue;
                if (!Corners(mesh, t2, Key(v, u), out int v2, out int u2, out int o2) || v2 != v || u2 != u) continue;
                float quality = Quality(mesh.flat[u], mesh.flat[o2], mesh.flat[v], mesh.flat[o1]);
                if (quality <= 0f) continue;
                candidates.Add((quality, t1, t2, u, o2, v, o1));
            }
            candidates.Sort((a, b) => b.quality.CompareTo(a.quality));
            var used = new bool[triCount];
            var quads = new List<int>();
            foreach (var c in candidates)
            {
                if (used[c.t1] || used[c.t2]) continue;
                used[c.t1] = used[c.t2] = true;
                quads.Add(c.q0); quads.Add(c.q1); quads.Add(c.q2); quads.Add(c.q3);
            }
            var leftover = new List<int>();
            for (int t = 0; t < triCount; t++)
            {
                if (used[t]) continue;
                leftover.Add(mesh.triangles[t * 3]); leftover.Add(mesh.triangles[t * 3 + 1]); leftover.Add(mesh.triangles[t * 3 + 2]);
            }
            mesh.quads.Clear();
            mesh.quads.AddRange(quads);
            mesh.triangles.Clear();
            for (int q = 0; q < quads.Count; q += 4)
            {
                mesh.triangles.Add(quads[q]); mesh.triangles.Add(quads[q + 1]); mesh.triangles.Add(quads[q + 2]);
                mesh.triangles.Add(quads[q]); mesh.triangles.Add(quads[q + 2]); mesh.triangles.Add(quads[q + 3]);
            }
            mesh.triangles.AddRange(leftover);
        }

        /// <summary>
        /// Edges that must stay edges: between consecutive stitches of one seam, dart or pleat on either side, along
        /// creases, and round holes. A quad made across one would take its edge away.
        /// </summary>
        private static HashSet<long> HeldEdges(PatternMesh mesh)
        {
            var held = new HashSet<long>(EdgeKeyComparer.Instance);
            var tacks = new HashSet<int>(mesh.tackStitches);
            for (int k = 0; k + 1 < mesh.stitches.Count; k++)
            {
                if (mesh.stitchSeam[k] != mesh.stitchSeam[k + 1] || tacks.Contains(k) || tacks.Contains(k + 1)) continue;
                Vector2Int a = mesh.stitches[k], b = mesh.stitches[k + 1];
                held.Add(Key(a.x, b.x)); held.Add(Key(a.y, b.y));
                held.Add(Key(a.x, b.y)); held.Add(Key(a.y, b.x));
            }
            foreach (Vector2Int c in mesh.creases) held.Add(Key(c.x, c.y));
            return held;
        }

        private static bool Corners(PatternMesh mesh, int t, long edge, out int a, out int b, out int other)
        {
            a = b = other = -1;
            for (int e = 0; e < 3; e++)
            {
                int i = mesh.triangles[t * 3 + e], j = mesh.triangles[t * 3 + (e + 1) % 3];
                if (Key(i, j) != edge) continue;
                a = i; b = j; other = mesh.triangles[t * 3 + (e + 2) % 3];
                return true;
            }
            return false;
        }

        /// <summary>1 for a square, falling to 0 as a corner leaves a right angle by <see cref="MaxCornerOffDeg"/>; 0 when not convex.</summary>
        public static float Quality(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2[] q = { a, b, c, d };
            float worst = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 prev = q[(i + 3) % 4] - q[i], next = q[(i + 1) % 4] - q[i];
                float cross = next.x * prev.y - next.y * prev.x;
                if (cross <= 1e-9f) return 0f;
                worst = Mathf.Max(worst, Mathf.Abs(Vector2.Angle(prev, next) - 90f));
            }
            return worst >= MaxCornerOffDeg ? 0f : 1f - worst / MaxCornerOffDeg;
        }

        /// <summary>
        /// Splits every face into quads: a quad into four, a triangle into three, through the middles of its edges
        /// and its centre. The new points along a seam are stitched to their partners (the middles of the same stretch
        /// on the other side), so seams stay matched; creases and outline segments are split with their edges. Each
        /// piece keeps its vertices together.
        /// </summary>
        public static PatternMesh SplitIntoQuads(ClothesPattern pattern, PatternMesh src)
        {
            var dst = new PatternMesh();
            int pieces = src.pieceStart != null ? src.pieceStart.Length : 0;
            dst.pieceStart = new int[pieces];
            dst.pieceCount = new int[pieces];
            dst.scales = src.scales;
            dst.edgeLengthCm = src.edgeLengthCm * 0.5f;
            if (src.pieceEdgeCm != null) { dst.pieceEdgeCm = new float[src.pieceEdgeCm.Length]; for (int i = 0; i < dst.pieceEdgeCm.Length; i++) dst.pieceEdgeCm[i] = src.pieceEdgeCm[i] * 0.5f; }
            dst.warnings.AddRange(src.warnings);
            dst.unrecovered.AddRange(src.unrecovered);

            // The faces of each piece: quads, then triangles left over.
            var faces = new List<int[]>[pieces];
            for (int p = 0; p < pieces; p++) faces[p] = new List<int[]>();
            for (int q = 0; q < src.quads.Count; q += 4) faces[src.piece[src.quads[q]]].Add(new[] { src.quads[q], src.quads[q + 1], src.quads[q + 2], src.quads[q + 3] });
            for (int t = src.QuadCount * 6; t + 2 < src.triangles.Count; t += 3) faces[src.piece[src.triangles[t]]].Add(new[] { src.triangles[t], src.triangles[t + 1], src.triangles[t + 2] });

            var outlineEdge = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            foreach (Vector3Int s in src.outlineSegments) outlineEdge[Key(s.x, s.y)] = s.z;
            var faceCount = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            for (int p = 0; p < pieces; p++)
                foreach (int[] f in faces[p])
                    for (int e = 0; e < f.Length; e++) { long k = Key(f[e], f[(e + 1) % f.Length]); faceCount[k] = faceCount.TryGetValue(k, out int c) ? c + 1 : 1; }

            var map = new int[src.VertexCount];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var middle = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            void Copy(int v, int piece)
            {
                map[v] = dst.VertexCount;
                dst.flat.Add(src.flat[v]); dst.drawn.Add(src.drawn[v]); dst.layout.Add(src.layout[v]); dst.layoutDepth.Add(src.layoutDepth[v]);
                dst.piece.Add(piece); dst.boundaryEdge.Add(src.boundaryEdge[v]); dst.onHole.Add(src.onHole[v]); dst.uv.Add(Vector2.zero);
            }
            int Mix(int[] vs, int piece, int edge, bool hole)
            {
                Vector2 flat = Vector2.zero, drawn = Vector2.zero, layout = Vector2.zero;
                float depth = 0f;
                foreach (int v in vs) { flat += src.flat[v]; drawn += src.drawn[v]; layout += src.layout[v]; depth += src.layoutDepth[v]; }
                float w = 1f / vs.Length;
                dst.flat.Add(flat * w); dst.drawn.Add(drawn * w); dst.layout.Add(layout * w); dst.layoutDepth.Add(depth * w);
                dst.piece.Add(piece); dst.boundaryEdge.Add(edge); dst.onHole.Add(hole); dst.uv.Add(Vector2.zero);
                return dst.VertexCount - 1;
            }
            int Middle(int a, int b, int piece)
            {
                long k = Key(a, b);
                if (middle.TryGetValue(k, out int m)) return m;
                bool boundary = faceCount.TryGetValue(k, out int c) && c == 1;
                int edge = outlineEdge.TryGetValue(k, out int e) ? e : -1;
                m = Mix(new[] { a, b }, piece, edge, boundary && src.onHole[a] && src.onHole[b]);
                middle[k] = m;
                return m;
            }

            for (int p = 0; p < pieces; p++)
            {
                dst.pieceStart[p] = dst.VertexCount;
                for (int v = src.pieceStart[p]; v < src.pieceStart[p] + src.pieceCount[p]; v++) Copy(v, p);
                foreach (int[] f in faces[p])
                {
                    int n = f.Length;
                    var mids = new int[n];
                    for (int e = 0; e < n; e++) mids[e] = Middle(f[e], f[(e + 1) % n], p);
                    int centre = Mix(f, p, -1, false);
                    // Corner i: its own vertex, the middle of the edge after it, the centre, the middle of the edge before it.
                    for (int i = 0; i < n; i++)
                    {
                        dst.quads.Add(map[f[i]]); dst.quads.Add(mids[i]); dst.quads.Add(centre); dst.quads.Add(mids[(i + n - 1) % n]);
                    }
                }
                dst.pieceCount[p] = dst.VertexCount - dst.pieceStart[p];
            }
            for (int q = 0; q < dst.quads.Count; q += 4)
            {
                dst.triangles.Add(dst.quads[q]); dst.triangles.Add(dst.quads[q + 1]); dst.triangles.Add(dst.quads[q + 2]);
                dst.triangles.Add(dst.quads[q]); dst.triangles.Add(dst.quads[q + 2]); dst.triangles.Add(dst.quads[q + 3]);
            }

            // Stitches, group by group in order, with one between the middles of each stitched stretch.
            var tacks = new HashSet<int>(src.tackStitches);
            var folded = new HashSet<int>(src.foldedStitches);
            for (int k = 0; k < src.stitches.Count; k++)
            {
                Vector2Int s = src.stitches[k];
                dst.stitches.Add(new Vector2Int(map[s.x], map[s.y]));
                dst.stitchSeam.Add(src.stitchSeam[k]);
                if (tacks.Contains(k)) dst.tackStitches.Add(dst.stitches.Count - 1);
                if (folded.Contains(k)) dst.foldedStitches.Add(dst.stitches.Count - 1);
                if (k + 1 >= src.stitches.Count || src.stitchSeam[k + 1] != src.stitchSeam[k] || tacks.Contains(k) || tacks.Contains(k + 1)) continue;
                Vector2Int next = src.stitches[k + 1];
                if (!middle.TryGetValue(Key(s.x, next.x), out int ma) || !middle.TryGetValue(Key(s.y, next.y), out int mb)) continue;
                if (ma == mb) continue;
                dst.stitches.Add(new Vector2Int(ma, mb));
                dst.stitchSeam.Add(src.stitchSeam[k]);
                if (folded.Contains(k) && folded.Contains(k + 1)) dst.foldedStitches.Add(dst.stitches.Count - 1);
            }
            StitchLeftoverMiddles(src, dst, map, middle, outlineEdge);
            for (int c = 0; c < src.creases.Count; c++)
            {
                Vector2Int e = src.creases[c];
                float angle = c < src.creaseAngle.Count ? src.creaseAngle[c] : -1f;
                bool sharp = c < src.creaseSharp.Count && src.creaseSharp[c];
                if (middle.TryGetValue(Key(e.x, e.y), out int m))
                {
                    dst.creases.Add(new Vector2Int(map[e.x], m)); dst.creaseAngle.Add(angle); dst.creaseSharp.Add(sharp);
                    dst.creases.Add(new Vector2Int(m, map[e.y])); dst.creaseAngle.Add(angle); dst.creaseSharp.Add(sharp);
                }
                else { dst.creases.Add(new Vector2Int(map[e.x], map[e.y])); dst.creaseAngle.Add(angle); dst.creaseSharp.Add(sharp); }
            }
            foreach (Vector3Int s in src.outlineSegments)
            {
                if (middle.TryGetValue(Key(s.x, s.y), out int m))
                {
                    dst.outlineSegments.Add(new Vector3Int(map[s.x], m, s.z));
                    dst.outlineSegments.Add(new Vector3Int(m, map[s.y], s.z));
                }
                else dst.outlineSegments.Add(new Vector3Int(map[s.x], map[s.y], s.z));
            }
            foreach (Vector2Int t in src.tackVertices) dst.tackVertices.Add(new Vector2Int(t.x >= 0 ? map[t.x] : -1, t.y >= 0 ? map[t.y] : -1));
            for (int v = 0; v < dst.VertexCount; v++) dst.uv[v] = UvLayout.ToUV(pattern, dst.piece[v], dst.drawn[v]);
            return dst;
        }

        /// <summary>
        /// Stitches the middles of sewn stretches the in-order pass missed: where one side's stretch meets two
        /// stitch groups (a seam that skips a pleat's intake, a fold sewn to itself) the partner is the stretch
        /// between the points its ends are sewn to, by any stitches; a folded layer's middle with no such partner is
        /// caught to the nearest point of the layer over it, as the mesher catches pleat layers. Measured on the
        /// Oxford shirt's box pleat: 10 points on the yoke seam were sewn to nothing before this.
        /// </summary>
        private static void StitchLeftoverMiddles(PatternMesh src, PatternMesh dst, int[] map, Dictionary<long, int> middle, Dictionary<long, int> outlineEdge)
        {
            var tacks = new HashSet<int>(src.tackStitches);
            var srcFolded = new HashSet<int>(src.foldedStitches);
            // Which source points are sewn together, by any chain of stitches.
            var parent = new int[src.VertexCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var sewn = new bool[src.VertexCount];
            var foldedAt = new bool[src.VertexCount];
            var groupAt = new int[src.VertexCount];
            for (int k = 0; k < src.stitches.Count; k++)
            {
                if (tacks.Contains(k)) continue;
                Vector2Int s = src.stitches[k];
                if (!sewn[s.x]) groupAt[s.x] = src.stitchSeam[k];
                if (!sewn[s.y]) groupAt[s.y] = src.stitchSeam[k];
                sewn[s.x] = sewn[s.y] = true;
                if (srcFolded.Contains(k)) foldedAt[s.x] = foldedAt[s.y] = true;
                int ra = Find(s.x), rb = Find(s.y);
                if (ra != rb) parent[ra] = rb;
            }
            var members = new Dictionary<int, List<int>>();
            for (int v = 0; v < src.VertexCount; v++)
            {
                if (!sewn[v]) continue;
                int r = Find(v);
                if (!members.TryGetValue(r, out List<int> list)) members[r] = list = new List<int>(4);
                list.Add(v);
            }
            // Edges a seam runs along: outlines, creases and stretches between stitches of one group.
            var seamSide = HeldEdges(src);
            foreach (long k in outlineEdge.Keys) seamSide.Add(k);

            var stitched = new HashSet<int>();
            foreach (Vector2Int s in dst.stitches) { stitched.Add(s.x); stitched.Add(s.y); }
            var paired = new HashSet<long>(EdgeKeyComparer.Instance);
            var leftover = new List<(int a, int b, int m)>();
            foreach (long edge in seamSide)
            {
                if (!middle.TryGetValue(edge, out int m) || stitched.Contains(m)) continue;
                int a = (int)(edge >> 32), b = (int)(edge & 0xffffffffL);
                if (!sewn[a] || !sewn[b]) continue;
                bool any = false;
                foreach (int c in members[Find(a)])
                {
                    foreach (int d in members[Find(b)])
                    {
                        long other = Key(c, d);
                        if (other == edge || !seamSide.Contains(other) || !middle.TryGetValue(other, out int mp) || mp == m) continue;
                        if (!paired.Add(Key(m, mp))) { any = true; continue; }
                        dst.stitches.Add(new Vector2Int(m, mp));
                        dst.stitchSeam.Add(groupAt[a]);
                        if (foldedAt[a] && foldedAt[b] && foldedAt[c] && foldedAt[d]) dst.foldedStitches.Add(dst.stitches.Count - 1);
                        stitched.Add(m); stitched.Add(mp);
                        any = true;
                    }
                }
                if (!any && outlineEdge.ContainsKey(edge)) leftover.Add((a, b, m));
            }

            // Folded layers with nothing over them in the stitches: caught to the nearest point of the top layer.
            int group = 0;
            foreach (int g in dst.stitchSeam) group = Mathf.Max(group, g + 1);
            foreach (var (a, b, m) in leftover)
            {
                if (stitched.Contains(m) || dst.layoutDepth[m] <= 0.01f) continue;
                int p = dst.piece[m], edge = dst.boundaryEdge[m];
                float reach = 0.75f * (dst.pieceEdgeCm != null && p < dst.pieceEdgeCm.Length ? dst.pieceEdgeCm[p] : dst.edgeLengthCm);
                int best = -1;
                for (int v = dst.pieceStart[p]; v < dst.pieceStart[p] + dst.pieceCount[p]; v++)
                {
                    if (v == m || dst.boundaryEdge[v] != edge || dst.layoutDepth[v] > 0.01f) continue;
                    float d = (dst.layout[v] - dst.layout[m]).magnitude;
                    if (d < reach) { reach = d; best = v; }
                }
                if (best < 0) continue;
                dst.stitches.Add(new Vector2Int(best, m));
                dst.stitchSeam.Add(group);
                dst.foldedStitches.Add(dst.stitches.Count - 1);
                stitched.Add(m);
            }
        }

        /// <summary>
        /// Splits each quad's two triangles along its shorter diagonal in 3D, so a fold across a quad bends along the
        /// diagonal that suits it. The quads themselves do not change.
        /// </summary>
        public static void SplitAlongShorterDiagonals(PatternMesh mesh, Vector3[] positions)
        {
            for (int q = 0, t = 0; q + 3 < mesh.quads.Count; q += 4, t += 6)
            {
                int a = mesh.quads[q], b = mesh.quads[q + 1], c = mesh.quads[q + 2], d = mesh.quads[q + 3];
                bool other = (positions[b] - positions[d]).sqrMagnitude < (positions[a] - positions[c]).sqrMagnitude;
                if (other)
                {
                    mesh.triangles[t] = a; mesh.triangles[t + 1] = b; mesh.triangles[t + 2] = d;
                    mesh.triangles[t + 3] = b; mesh.triangles[t + 4] = c; mesh.triangles[t + 5] = d;
                }
                else
                {
                    mesh.triangles[t] = a; mesh.triangles[t + 1] = b; mesh.triangles[t + 2] = c;
                    mesh.triangles[t + 3] = a; mesh.triangles[t + 4] = c; mesh.triangles[t + 5] = d;
                }
            }
        }

        /// <summary>How much of a mesh is quads, and how regular: the share of its area in quads, and the inside vertices not shared by four quads.</summary>
        public static void Measure(PatternMesh mesh, out float quadShare, out int irregular, out int triangles)
        {
            float quadArea = 0f, total = 0f;
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.flat[mesh.triangles[t]], b = mesh.flat[mesh.triangles[t + 1]], c = mesh.flat[mesh.triangles[t + 2]];
                float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
                total += area;
                if (t < mesh.QuadCount * 6) quadArea += area;
            }
            quadShare = total > 0f ? quadArea / total : 0f;
            triangles = (mesh.triangles.Count - mesh.QuadCount * 6) / 3;
            var valence = new int[mesh.VertexCount];
            for (int q = 0; q < mesh.quads.Count; q++) valence[mesh.quads[q]]++;
            for (int t = mesh.QuadCount * 6; t < mesh.triangles.Count; t++) valence[mesh.triangles[t]]++;
            irregular = 0;
            for (int v = 0; v < mesh.VertexCount; v++) if (mesh.boundaryEdge[v] < 0 && !mesh.onHole[v] && valence[v] != 4 && valence[v] > 0) irregular++;
        }

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }
}
