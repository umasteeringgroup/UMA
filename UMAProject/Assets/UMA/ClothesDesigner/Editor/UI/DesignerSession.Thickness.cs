using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The live cloth drawn with the pieces' own thickness (<see cref="PatternPiece.thicknessMm"/>): the outer copy and
    /// walls the bake makes (<see cref="PieceThickness"/>) are added to the preview mesh, after the cloth's own points, and
    /// follow the cloth every frame. Picking and the tools still use the cloth alone.
    /// </summary>
    public sealed partial class DesignerSession
    {
        /// <summary>For each added point: the cloth point it follows, how far out along its normal, and whether it is a wall's.</summary>
        private int[] shellSource = new int[0];
        private float[] shellOffset = new float[0];
        private bool[] shellWall = new bool[0];
        /// <summary>The added triangles, indexing the cloth's points and then the added ones.</summary>
        private int[] shellTriangles = new int[0];
        private string builtShellKey = string.Empty;
        /// <summary>For each cloth point, its outer copy among the added points, or -1 (a thin piece's point).</summary>
        private int[] shellOuterOf = new int[0];
        /// <summary>The cloth's points and the added ones as last drawn (with normals), or null.</summary>
        private Vector3[] shellLaid;

        /// <summary>
        /// For each cloth point, the added point that is its outer copy (out along the normal by its piece's thickness,
        /// not a wall's), or -1.
        /// </summary>
        public static int[] OuterCopies(int clothPoints, int[] source, float[] offset, bool[] wall)
        {
            var outer = new int[clothPoints];
            for (int v = 0; v < clothPoints; v++) outer[v] = -1;
            for (int k = 0; k < source.Length; k++)
            {
                int s = source[k];
                if (wall[k] || offset[k] <= 0f || s < 0 || s >= clothPoints || outer[s] >= 0) continue;
                outer[s] = k;
            }
            return outer;
        }

        /// <summary>
        /// From a cloth point to the outside of its piece as last drawn: out along its normal by the piece's thickness,
        /// or zero on a thin piece (or before the cloth is drawn with normals).
        /// </summary>
        public Vector3 OutsideOffset(int v)
        {
            int n = shellOuterOf.Length;
            if (shellLaid == null || v < 0 || v >= n) return Vector3.zero;
            int k = shellOuterOf[v];
            if (k < 0 || n + k >= shellLaid.Length) return Vector3.zero;
            return shellLaid[n + k] - shellLaid[v];
        }

        /// <summary>The pieces' thicknesses: when it changes, the live cloth is drawn again.</summary>
        private string ShellKey()
        {
            if (Pattern == null) return string.Empty;
            var text = new System.Text.StringBuilder();
            foreach (PatternPiece piece in Pattern.pieces) text.Append(piece != null ? piece.thicknessMm.ToString("R") : "-").Append(';');
            return text.ToString();
        }

        /// <summary>Works out the outer copies and walls of the thick pieces for the cloth as it is now.</summary>
        private void BuildShell()
        {
            shellSource = new int[0]; shellOffset = new float[0]; shellWall = new bool[0]; shellTriangles = new int[0];
            shellOuterOf = new int[0]; shellLaid = null;
            builtShellKey = ShellKey();
            if (Pattern == null || !Fit.HasCloth || previewTriangles == null || !PieceThickness.Any(Pattern)) return;
            Vector3[] positions = Fit.Positions;
            int n = positions.Length;
            var slot = new List<Vector3>(positions);
            var triangles = new List<int>();
            var uv = new List<Vector2>(Fit.Mesh.uv);
            var source = new List<int>(n);
            for (int v = 0; v < n; v++) source.Add(v);
            PieceThickness.Output output = PieceThickness.Build(Fit.Mesh, Pattern, positions, previewTriangles, slot, triangles, uv, source);
            int added = slot.Count - n;
            shellSource = new int[added];
            shellOffset = new float[added];
            shellWall = new bool[added];
            for (int k = 0; k < added; k++)
            {
                shellSource[k] = source[n + k];
                shellOffset[k] = k < output.offsets.Count ? output.offsets[k] : 0f;
            }
            foreach ((int vertex, int side) in output.walls) if (vertex - n >= 0 && vertex - n < added) shellWall[vertex - n] = true;
            shellTriangles = triangles.ToArray();
            shellOuterOf = OuterCopies(n, shellSource, shellOffset, shellWall);
        }

        /// <summary>
        /// The cloth's points followed by the added ones, laid along the cloth's normals (with no normals, at the points they
        /// follow). <paramref name="all"/> gets the normals for them all: an outer copy's its cloth point's, a wall's its own.
        /// </summary>
        private Vector3[] WithShell(Vector3[] positions, Vector3[] normals, out Vector3[] all)
        {
            int n = positions.Length, added = shellSource.Length;
            if (added == 0) { all = normals; return positions; }
            var vertices = new Vector3[n + added];
            Array.Copy(positions, vertices, n);
            all = normals != null ? new Vector3[n + added] : null;
            if (all != null) Array.Copy(normals, all, n);
            for (int k = 0; k < added; k++)
            {
                int s = shellSource[k];
                Vector3 normal = normals != null ? normals[s] : Vector3.zero;
                vertices[n + k] = positions[s] + normal * shellOffset[k];
                if (all != null) all[n + k] = shellWall[k] ? Vector3.zero : normal;
            }
            if (all == null) return vertices;
            // A wall's normal from its own triangles: the piece's edge stays crisp.
            for (int t = 0; t + 2 < shellTriangles.Length; t += 3)
            {
                int a = shellTriangles[t], b = shellTriangles[t + 1], c = shellTriangles[t + 2];
                if (a < n || !shellWall[a - n]) continue;
                Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                all[a] += face; all[b] += face; all[c] += face;
            }
            for (int k = 0; k < added; k++)
            {
                if (!shellWall[k]) continue;
                Vector3 w = all[n + k];
                all[n + k] = w.sqrMagnitude > 1e-20f ? w.normalized : normals[shellSource[k]];
            }
            // Kept for the seams drawn over the cloth (OutsideOffset).
            shellLaid = vertices;
            return vertices;
        }

        /// <summary>The cloth's triangles followed by the added ones.</summary>
        private int[] WithShellTriangles(int[] triangles)
        {
            if (shellTriangles.Length == 0) return triangles;
            var all = new int[triangles.Length + shellTriangles.Length];
            Array.Copy(triangles, all, triangles.Length);
            Array.Copy(shellTriangles, 0, all, triangles.Length, shellTriangles.Length);
            return all;
        }

        /// <summary>Per-point values (UVs, colours) for the cloth followed by the added points', each its cloth point's.</summary>
        private T[] WithShellValues<T>(T[] values)
        {
            int added = shellSource.Length;
            if (added == 0 || values == null) return values;
            int n = values.Length;
            var all = new T[n + added];
            Array.Copy(values, all, n);
            for (int k = 0; k < added; k++) all[n + k] = shellSource[k] < n ? values[shellSource[k]] : default;
            return all;
        }
    }
}
