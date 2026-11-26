using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Sharp seams and sharp folds in the baked slot. UMA gives vertices at one position ("battle buddies") one
    /// shared normal; an edge marked sharp is the exception: each side of it keeps the normal of its own faces, while
    /// its vertices stay at one position and move as one (<see cref="UMABuddyNormals.Sides"/>).
    /// <list type="bullet">
    /// <item>A sharp seam already has a copy of each vertex on either piece: the copies get different side ids.</item>
    /// <item>A sharp fold (<see cref="PatternLine.sharp"/>, <see cref="PatternPleat.sharpFolds"/>) runs inside a piece: the
    /// slot is split along it, one copy of each fold vertex per side, at the same position with the same weights and
    /// UVs, each copy its own side id.</item>
    /// </list>
    /// </summary>
    public static class SharpEdges
    {
        /// <summary>The first side id given to the copies split along folds (seams use small ids).</summary>
        private const int FoldSides = 1000000;

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>True when the pattern has a sharp seam or its mesh a sharp fold.</summary>
        public static bool Any(ClothesPattern pattern, PatternMesh mesh)
        {
            if (pattern != null) foreach (PatternSeam seam in pattern.seams) if (seam != null && seam.sharp) return true;
            if (mesh != null) foreach (bool sharp in mesh.creaseSharp) if (sharp) return true;
            return false;
        }

        /// <summary>How many vertices have a side id (are on a sharp edge).</summary>
        public static int CountSharp(int[] sides)
        {
            int count = 0;
            if (sides != null) foreach (int s in sides) if (s != 0) count++;
            return count;
        }

        /// <summary>
        /// Gives the slot geometry its sharp edges: side ids for the sides of sharp seams, and a split along sharp folds
        /// (new vertices are added at the end, with <paramref name="sourceOf"/> pointing them at the cloth vertex they
        /// copy, for weights). The first <c>mesh.VertexCount</c> slot vertices must be the cloth's own. Returns a side
        /// id per slot vertex (0 where nothing is sharp).
        /// </summary>
        public static int[] Apply(ClothesPattern pattern, PatternMesh mesh, ref Vector3[] positions, ref int[] triangles, ref IList<Vector2> uv, ref int[] sourceOf, out int split)
        {
            split = 0;
            int n = positions.Length;
            var sides = new List<int>(new int[n]);
            // Sharp seams: each side of the seam its own id.
            for (int i = 0; i < mesh.stitches.Count; i++)
            {
                int seam = i < mesh.stitchSeam.Count ? mesh.stitchSeam[i] : -1;
                if (seam < 0 || seam >= pattern.seams.Count || pattern.seams[seam] == null || !pattern.seams[seam].sharp) continue;
                Vector2Int s = mesh.stitches[i];
                if (s.x < n) sides[s.x] = 1 + 2 * seam;
                if (s.y < n) sides[s.y] = 2 + 2 * seam;
            }
            // Sharp folds: the edges to split along.
            var cut = new HashSet<long>();
            var onCut = new HashSet<int>();
            for (int c = 0; c < mesh.creases.Count; c++)
            {
                if (c >= mesh.creaseSharp.Count || !mesh.creaseSharp[c]) continue;
                Vector2Int e = mesh.creases[c];
                if (e.x == e.y || e.x >= n || e.y >= n) continue;
                cut.Add(Key(e.x, e.y));
                onCut.Add(e.x); onCut.Add(e.y);
            }
            if (cut.Count > 0)
            {
                var pos = new List<Vector3>(positions);
                var texture = new List<Vector2>(uv);
                var source = new List<int>(sourceOf ?? Identity(n));
                int[] tris = (int[])triangles.Clone();
                var around = new Dictionary<int, List<int>>();
                foreach (int v in onCut) around[v] = new List<int>(8);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    for (int k = 0; k < 3; k++)
                        if (around.TryGetValue(tris[t + k], out List<int> list)) list.Add(t);
                int next = FoldSides;
                foreach (int v in onCut)
                {
                    List<int> fan = around[v];
                    var group = new Dictionary<int, int>();
                    int groups = 0;
                    foreach (int start in fan)
                    {
                        if (group.ContainsKey(start)) continue;
                        var stack = new Stack<int>();
                        stack.Push(start);
                        group[start] = groups;
                        while (stack.Count > 0)
                        {
                            int t = stack.Pop();
                            foreach (int other in fan)
                            {
                                if (group.ContainsKey(other)) continue;
                                int shared = SharedCorner(tris, t, other, v);
                                if (shared < 0 || cut.Contains(Key(v, shared))) continue;
                                group[other] = groups;
                                stack.Push(other);
                            }
                        }
                        groups++;
                    }
                    if (groups < 2) continue;
                    // The original keeps one side (with an id of its own), each further side gets a copy.
                    if (sides[v] == 0) sides[v] = next++;
                    for (int g = 1; g < groups; g++)
                    {
                        int copy = pos.Count;
                        pos.Add(pos[v]);
                        texture.Add(texture[v]);
                        source.Add(source[v]);
                        sides.Add(next++);
                        foreach (KeyValuePair<int, int> kv in group)
                        {
                            if (kv.Value != g) continue;
                            for (int k = 0; k < 3; k++) if (tris[kv.Key + k] == v) tris[kv.Key + k] = copy;
                        }
                        split++;
                    }
                }
                positions = pos.ToArray();
                triangles = tris;
                uv = texture;
                sourceOf = source.ToArray();
            }
            return sides.ToArray();
        }

        /// <summary>The corner (other than v) two triangles share, or -1.</summary>
        private static int SharedCorner(int[] tris, int t, int u, int v)
        {
            for (int a = 0; a < 3; a++)
            {
                int x = tris[t + a];
                if (x == v) continue;
                for (int b = 0; b < 3; b++) if (tris[u + b] == x) return x;
            }
            return -1;
        }

        private static int[] Identity(int count)
        {
            var a = new int[count];
            for (int i = 0; i < count; i++) a[i] = i;
            return a;
        }
    }
}
