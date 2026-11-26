using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Writes a quad cloth mesh, as draped, to an OBJ for other tools: one object per piece, quads as quads (and any
    /// triangles left over as triangles), points sewn together shared, the pattern's UVs. Written the usual way for
    /// Unity meshes going to right-handed tools such as Blender: X negated, so front faces stay front faces.
    /// </summary>
    public static class QuadObjWriter
    {
        /// <param name="positions">Each vertex's position as draped (metres).</param>
        /// <param name="flip">The mesh's triangles face in as made (<see cref="Arrangement.Result.flipTriangles"/>): the faces are reversed.</param>
        public static void Write(string path, ClothesPattern pattern, PatternMesh mesh, Vector3[] positions, bool flip)
        {
            File.WriteAllText(Path.GetFullPath(path), ToText(pattern, mesh, positions, flip));
        }

        public static string ToText(ClothesPattern pattern, PatternMesh mesh, Vector3[] positions, bool flip)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            int[] group = GarmentBaker.WeldGroups(mesh.VertexCount, mesh.stitches);
            // One position per sewn group, one UV per vertex.
            var positionIndex = new Dictionary<int, int>();
            var sb = new StringBuilder();
            sb.Append("# UMA Clothes Designer quad mesh: ").Append(pattern != null ? pattern.name : "").Append('\n');
            sb.Append("# ").Append(mesh.QuadCount).Append(" quads, ").Append((mesh.triangles.Count - mesh.QuadCount * 6) / 3).Append(" triangles. Metres, Y up, X negated.\n");
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int g = group[v];
                if (positionIndex.ContainsKey(g)) continue;
                positionIndex[g] = positionIndex.Count + 1;
                Vector3 p = positions[g];
                sb.Append("v ").Append((-p.x).ToString("R", c)).Append(' ').Append(p.y.ToString("R", c)).Append(' ').Append(p.z.ToString("R", c)).Append('\n');
            }
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector2 uv = v < mesh.uv.Count ? mesh.uv[v] : Vector2.zero;
                sb.Append("vt ").Append(uv.x.ToString("R", c)).Append(' ').Append(uv.y.ToString("R", c)).Append('\n');
            }
            // Faces by piece.
            int pieces = mesh.pieceStart != null ? mesh.pieceStart.Length : 1;
            var faces = new List<int[]>[pieces];
            for (int p = 0; p < pieces; p++) faces[p] = new List<int[]>();
            for (int q = 0; q + 3 < mesh.quads.Count; q += 4) faces[mesh.piece[mesh.quads[q]]].Add(new[] { mesh.quads[q], mesh.quads[q + 1], mesh.quads[q + 2], mesh.quads[q + 3] });
            for (int t = mesh.QuadCount * 6; t + 2 < mesh.triangles.Count; t += 3) faces[mesh.piece[mesh.triangles[t]]].Add(new[] { mesh.triangles[t], mesh.triangles[t + 1], mesh.triangles[t + 2] });
            for (int p = 0; p < pieces; p++)
            {
                if (faces[p].Count == 0) continue;
                string name = pattern != null && p < pattern.pieces.Count ? pattern.pieces[p].name : "Piece " + (p + 1);
                sb.Append("o ").Append(Clean(name)).Append("\ns 1\n");
                foreach (int[] f in faces[p])
                {
                    sb.Append('f');
                    // Mirroring X turns Unity's (clockwise) front faces into the right-handed tools' (counter-clockwise) ones.
                    for (int i = 0; i < f.Length; i++)
                    {
                        int v = flip ? f[f.Length - 1 - i] : f[i];
                        sb.Append(' ').Append(positionIndex[group[v]]).Append('/').Append(v + 1);
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private static string Clean(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_');
            return sb.Length > 0 ? sb.ToString() : "Piece";
        }
    }
}
