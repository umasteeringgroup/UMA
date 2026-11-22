using System;
using System.Numerics;
using System.Threading.Tasks;

namespace UMA.Editors
{
    /// <summary>
    /// A mesh rasterised into its UV square: for every texel, the surface position, normal and tangent frame, and how
    /// far one texel reaches on the surface. Texels outside every UV island take the values of the nearest covered
    /// texel, so anything computed from them extends a little past the island edges (mipmap and atlas padding).
    /// Row 0 is the top of the texture (v = 1). Pure C#: no UnityEngine types.
    /// </summary>
    public sealed class UMAWrinkleSurfaceMap
    {
        public readonly int Size;
        /// <summary>True where a triangle covers the texel centre.</summary>
        public readonly bool[] Covered;
        /// <summary>Index of the covered texel whose values this texel holds (itself when covered, -1 if the mesh has no triangles).</summary>
        public readonly int[] Source;
        public readonly Vector3[] Position, Normal, Tangent, Bitangent;
        /// <summary>Surface movement, in mesh units, for one texel to the right (U) and one texel down (-V).</summary>
        public readonly Vector3[] TexelRight, TexelDown;

        UMAWrinkleSurfaceMap(int size)
        {
            Size = size; int n = size * size;
            Covered = new bool[n]; Source = new int[n];
            Position = new Vector3[n]; Normal = new Vector3[n]; Tangent = new Vector3[n]; Bitangent = new Vector3[n];
            TexelRight = new Vector3[n]; TexelDown = new Vector3[n];
        }

        /// <param name="tangents">xyz tangent, w bitangent sign (Unity convention: bitangent = cross(normal, tangent) * w).</param>
        public static UMAWrinkleSurfaceMap Build(Vector3[] positions, Vector3[] normals, Vector4[] tangents, Vector2[] uvs, int[] triangles, int size)
        {
            if (positions == null || normals == null || tangents == null || uvs == null || triangles == null) throw new ArgumentNullException(nameof(positions));
            if (size < 4) throw new ArgumentOutOfRangeException(nameof(size));
            var map = new UMAWrinkleSurfaceMap(size);
            for (int i = 0; i < map.Source.Length; i++) map.Source[i] = -1;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                float x0 = uvs[i0].X * size, y0 = (1 - uvs[i0].Y) * size;
                float x1 = uvs[i1].X * size, y1 = (1 - uvs[i1].Y) * size;
                float x2 = uvs[i2].X * size, y2 = (1 - uvs[i2].Y) * size;
                float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (Math.Abs(area) < 1e-9f) continue;
                Vector3 e1 = positions[i1] - positions[i0], e2 = positions[i2] - positions[i0];
                float du1 = x1 - x0, dv1 = y1 - y0, du2 = x2 - x0, dv2 = y2 - y0, det = du1 * dv2 - du2 * dv1;
                Vector3 right = (e1 * dv2 - e2 * dv1) / det, down = (e2 * du1 - e1 * du2) / det;
                int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))) - 1), maxX = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))) + 1);
                int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))) - 1), maxY = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))) + 1);
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) / area;
                        float w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) / area;
                        float w2 = 1 - w0 - w1;
                        const float edge = -1e-4f;
                        if (w0 < edge || w1 < edge || w2 < edge) continue;
                        int i = y * size + x;
                        if (map.Covered[i]) continue;
                        map.Covered[i] = true; map.Source[i] = i;
                        map.Position[i] = positions[i0] * w0 + positions[i1] * w1 + positions[i2] * w2;
                        var n = Vector3.Normalize(normals[i0] * w0 + normals[i1] * w1 + normals[i2] * w2);
                        var tg4 = tangents[i0] * w0 + tangents[i1] * w1 + tangents[i2] * w2;
                        var tg = new Vector3(tg4.X, tg4.Y, tg4.Z);
                        tg = tg - n * Vector3.Dot(tg, n);
                        tg = tg.LengthSquared() > 1e-12f ? Vector3.Normalize(tg) : Vector3.Normalize(right);
                        map.Normal[i] = n; map.Tangent[i] = tg;
                        map.Bitangent[i] = Vector3.Cross(n, tg) * (tangents[i0].W < 0 ? -1f : 1f);
                        map.TexelRight[i] = right; map.TexelDown[i] = down;
                    }
            }
            // Padding: every other texel copies its nearest covered texel.
            var nearest = new int[map.Source.Length];
            var distance = new float[map.Source.Length];
            UMAWrinkleDistanceField.NearestSource(size, size, map.Covered, nearest, distance);
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x, s = nearest[i];
                    if (map.Covered[i] || s < 0) continue;
                    map.Source[i] = s; map.Position[i] = map.Position[s]; map.Normal[i] = map.Normal[s];
                    map.Tangent[i] = map.Tangent[s]; map.Bitangent[i] = map.Bitangent[s];
                    map.TexelRight[i] = map.TexelRight[s]; map.TexelDown[i] = map.TexelDown[s];
                }
            });
            return map;
        }
    }
}
