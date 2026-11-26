using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Bakes the pieces that have a thickness of their own (<see cref="PatternPiece.thicknessMm"/>): a belt, a waistband,
    /// a strap. The piece's cloth stays where it was draped and a copy of it is laid the thickness further out, joined to
    /// it round the piece's outline by a narrow wall, so the piece stands out from what it is sewn to and whatever lies
    /// under it stays under it. The wall keeps normals of its own, so the edges stay crisp (side ids, as
    /// <see cref="SharpEdges"/> gives them). An edge sewn to another thick piece (a band's two halves, its ends sewn into
    /// a ring) gets no wall, so a band cut in parts bakes as one solid band.
    /// </summary>
    public static class PieceThickness
    {
        /// <summary>The first side id the walls take; sharp seams and folds use lower ones.</summary>
        public const int WallSides = 2000000;

        public sealed class Output
        {
            /// <summary>The outer copies' triangles and the walls' triangles added.</summary>
            public int shellTriangles, wallTriangles;
            /// <summary>Every wall vertex added, with its side id (one per piece).</summary>
            public readonly List<(int vertex, int side)> walls = new List<(int, int)>();
            /// <summary>The thick pieces, for the bake's messages.</summary>
            public readonly List<string> pieces = new List<string>();
            /// <summary>
            /// For each vertex added, in order: how far out along its cloth vertex's normal it lies (m). The live preview
            /// moves them with the cloth: each frame, the cloth vertex plus its normal times this.
            /// </summary>
            public readonly List<float> offsets = new List<float>();
        }

        /// <summary>True when any piece of the pattern has a thickness.</summary>
        public static bool Any(ClothesPattern pattern)
        {
            if (pattern == null) return false;
            foreach (PatternPiece piece in pattern.pieces) if (piece != null && piece.thicknessMm > 0f) return true;
            return false;
        }

        /// <summary>
        /// Adds the outer copies and walls of the thick pieces to the slot geometry. The first <c>mesh.VertexCount</c>
        /// slot vertices must be the cloth's own (as draped); new vertices go at the end, with <paramref name="sourceOf"/>
        /// pointing each at the cloth vertex it copies (for its weights).
        /// </summary>
        /// <param name="positions">The cloth's welded positions.</param>
        /// <param name="clothTriangles">The cloth's triangles, facing out.</param>
        public static Output Build(PatternMesh mesh, ClothesPattern pattern, Vector3[] positions, int[] clothTriangles,
            List<Vector3> slotPositions, List<int> slotTriangles, List<Vector2> slotUv, List<int> sourceOf)
        {
            var output = new Output();
            int n = mesh.VertexCount;
            var thickness = new float[pattern.pieces.Count];
            for (int p = 0; p < thickness.Length; p++)
            {
                PatternPiece piece = pattern.pieces[p];
                thickness[p] = piece != null ? Mathf.Max(0f, piece.thicknessMm) * 0.001f : 0f;
                if (thickness[p] > 0f) output.pieces.Add(piece.name);
            }
            if (output.pieces.Count == 0) return output;
            float Thick(int v) => mesh.piece[v] >= 0 && mesh.piece[v] < thickness.Length ? thickness[mesh.piece[v]] : 0f;

            // Outward normals, battle buddies (welded copies) sharing one, so the copy follows the cloth across seams.
            var normals = new Vector3[n];
            new UMABuddyNormals(positions, clothTriangles).Recalculate(positions, normals);

            // Points sewn to a point of a thick piece (itself included: a band's ends sewn into a ring, a dart's legs).
            var sewnToThick = new bool[n];
            foreach (Vector2Int s in mesh.stitches)
            {
                if (s.x < 0 || s.y < 0 || s.x >= n || s.y >= n) continue;
                if (Thick(s.y) > 0f) sewnToThick[s.x] = true;
                if (Thick(s.x) > 0f) sewnToThick[s.y] = true;
            }

            var outer = new Dictionary<int, int>();
            int Outer(int v)
            {
                if (outer.TryGetValue(v, out int c)) return c;
                c = slotPositions.Count;
                slotPositions.Add(positions[v] + normals[v] * Thick(v));
                slotUv.Add(mesh.uv[v]);
                sourceOf.Add(v);
                output.offsets.Add(Thick(v));
                outer[v] = c;
                return c;
            }

            // The outer copy: each thick piece's triangles again, the thickness further out, facing out as they do.
            var edgeUse = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            var edgeThird = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            for (int t = 0; t + 2 < clothTriangles.Length; t += 3)
            {
                int a = clothTriangles[t], b = clothTriangles[t + 1], c = clothTriangles[t + 2];
                if (Thick(a) <= 0f || mesh.piece[a] != mesh.piece[b] || mesh.piece[a] != mesh.piece[c]) continue;
                slotTriangles.Add(Outer(a)); slotTriangles.Add(Outer(b)); slotTriangles.Add(Outer(c));
                output.shellTriangles++;
                for (int k = 0; k < 3; k++)
                {
                    int u = clothTriangles[t + k], w = clothTriangles[t + (k + 1) % 3];
                    long key = u < w ? ((long)u << 32) | (uint)w : ((long)w << 32) | (uint)u;
                    edgeUse[key] = edgeUse.TryGetValue(key, out int used) ? used + 1 : 1;
                    edgeThird[key] = clothTriangles[t + (k + 2) % 3];
                }
            }

            // The walls: round each thick piece's outline (edges of one triangle), from the cloth out to its copy. Each
            // wall has vertices of its own, with the piece's side id, so it neither rounds the copy's edge nor bends the
            // cloth's normals.
            var wallIn = new Dictionary<int, int>();
            var wallOut = new Dictionary<int, int>();
            int Wall(Dictionary<int, int> map, int v, Vector3 at)
            {
                if (map.TryGetValue(v, out int c)) return c;
                c = slotPositions.Count;
                slotPositions.Add(at);
                slotUv.Add(mesh.uv[v]);
                sourceOf.Add(v);
                output.offsets.Add(map == wallOut ? Thick(v) : 0f);
                map[v] = c;
                output.walls.Add((c, WallSides + mesh.piece[v]));
                return c;
            }
            foreach (KeyValuePair<long, int> kv in edgeUse)
            {
                if (kv.Value != 1) continue;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                // Sewn to another thick piece along here: the two are one solid, with no wall between.
                if (sewnToThick[a] && sewnToThick[b]) continue;
                int third = edgeThird[kv.Key];
                int ia = Wall(wallIn, a, positions[a]), ib = Wall(wallIn, b, positions[b]);
                int oa = Wall(wallOut, a, slotPositions[Outer(a)]), ob = Wall(wallOut, b, slotPositions[Outer(b)]);
                // Facing away from the piece.
                Vector3 away = (positions[a] + positions[b]) * 0.5f - positions[third];
                Vector3 face = Vector3.Cross(positions[b] - positions[a], slotPositions[ob] - positions[a]);
                if (Vector3.Dot(face, away) >= 0f)
                {
                    slotTriangles.Add(ia); slotTriangles.Add(ib); slotTriangles.Add(ob);
                    slotTriangles.Add(ia); slotTriangles.Add(ob); slotTriangles.Add(oa);
                }
                else
                {
                    slotTriangles.Add(ia); slotTriangles.Add(ob); slotTriangles.Add(ib);
                    slotTriangles.Add(ia); slotTriangles.Add(oa); slotTriangles.Add(ob);
                }
                output.wallTriangles += 2;
            }
            return output;
        }
    }
}
