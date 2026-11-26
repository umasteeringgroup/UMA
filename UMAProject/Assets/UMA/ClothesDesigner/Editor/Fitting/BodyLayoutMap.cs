using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Lays a piece made from a painted body region (<see cref="BodyLayout"/>) where it was painted: each point of the
    /// cloth, by where it is on the piece as drawn, is found in the piece's flattened source triangles and taken to the
    /// same place on the body. Only on the race it was painted on, and only while its anchor point is still on it.
    /// </summary>
    public sealed class BodyLayoutMap
    {
        private const float Cell = 2f;
        private readonly BodyLayout layout;
        private readonly Vector2 shift;
        private readonly Vector2 origin;
        private readonly int columns, rows;
        private readonly List<int>[] cells;

        /// <summary>The map for a piece on a race, or null when the piece has no body layout for it.</summary>
        public static BodyLayoutMap For(PatternPiece piece, string race)
        {
            BodyLayout l = piece != null ? piece.bodyLayout : null;
            if (l == null || !l.Valid || string.IsNullOrEmpty(race) || l.race != race) return null;
            PatternPoint anchor = piece.points.Find(p => p != null && p.id == l.anchorPointId);
            if (anchor == null) return null;
            return new BodyLayoutMap(l, anchor.position - l.anchor);
        }

        private BodyLayoutMap(BodyLayout layout, Vector2 shift)
        {
            this.layout = layout;
            this.shift = shift;
            Vector2 min = layout.flat[0], max = layout.flat[0];
            foreach (Vector2 p in layout.flat) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            origin = min;
            columns = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / Cell) + 1);
            rows = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / Cell) + 1);
            cells = new List<int>[columns * rows];
            for (int t = 0; t + 2 < layout.triangles.Length; t += 3)
            {
                Vector2 a = layout.flat[layout.triangles[t]], b = layout.flat[layout.triangles[t + 1]], c = layout.flat[layout.triangles[t + 2]];
                Vector2 lo = Vector2.Min(a, Vector2.Min(b, c)) - origin, hi = Vector2.Max(a, Vector2.Max(b, c)) - origin;
                for (int y = Mathf.FloorToInt(lo.y / Cell); y <= Mathf.FloorToInt(hi.y / Cell); y++)
                {
                    for (int x = Mathf.FloorToInt(lo.x / Cell); x <= Mathf.FloorToInt(hi.x / Cell); x++)
                    {
                        if (x < 0 || y < 0 || x >= columns || y >= rows) continue;
                        int k = y * columns + x;
                        (cells[k] ?? (cells[k] = new List<int>(4))).Add(t);
                    }
                }
            }
        }

        /// <summary>The point on the body (metres) for a point on the piece as drawn (cm), and the body's normal there.</summary>
        public Vector3 Position(Vector2 drawn, out Vector3 normal)
        {
            Vector2 p = drawn - shift;
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / Cell), 0, columns - 1), cy = Mathf.Clamp(Mathf.FloorToInt((p.y - origin.y) / Cell), 0, rows - 1);
            int best = -1;
            float bestDistance = float.MaxValue;
            Vector3 bestWeights = Vector3.zero;
            // Look in the point's cell, then rings round it, until a triangle is found (outside the source, the nearest).
            for (int ring = 0; ring < Mathf.Max(columns, rows) && best < 0 || ring < 2; ring++)
            {
                for (int y = cy - ring; y <= cy + ring; y++)
                {
                    for (int x = cx - ring; x <= cx + ring; x++)
                    {
                        if (x < 0 || y < 0 || x >= columns || y >= rows) continue;
                        if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(y - cy)) != ring) continue;
                        List<int> list = cells[y * columns + x];
                        if (list == null) continue;
                        foreach (int t in list)
                        {
                            Vector3 w = Barycentric(p, layout.flat[layout.triangles[t]], layout.flat[layout.triangles[t + 1]], layout.flat[layout.triangles[t + 2]], out float outside);
                            if (outside < bestDistance) { bestDistance = outside; best = t; bestWeights = w; }
                        }
                    }
                }
                if (best >= 0 && bestDistance <= 0f) break;
            }
            if (best < 0) { normal = Vector3.up; return layout.positions[0]; }
            int i0 = layout.triangles[best], i1 = layout.triangles[best + 1], i2 = layout.triangles[best + 2];
            normal = (layout.normals[i0] * bestWeights.x + layout.normals[i1] * bestWeights.y + layout.normals[i2] * bestWeights.z).normalized;
            return layout.positions[i0] * bestWeights.x + layout.positions[i1] * bestWeights.y + layout.positions[i2] * bestWeights.z;
        }

        /// <summary>Barycentric weights of p in a triangle, clamped onto it; <paramref name="outside"/> is how far outside it lies (0 inside).</summary>
        private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float outside)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d11 = Vector2.Dot(v1, v1), d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
            float den = d00 * d11 - d01 * d01;
            if (Mathf.Abs(den) < 1e-12f) { outside = float.MaxValue; return new Vector3(1f, 0f, 0f); }
            float v = (d11 * d20 - d01 * d21) / den, w = (d00 * d21 - d01 * d20) / den, u = 1f - v - w;
            if (u >= 0f && v >= 0f && w >= 0f) { outside = 0f; return new Vector3(u, v, w); }
            // Clamp onto the triangle and measure how far that moved the point.
            u = Mathf.Max(0f, u); v = Mathf.Max(0f, v); w = Mathf.Max(0f, w);
            float s = u + v + w;
            if (s <= 0f) { u = 1f; s = 1f; }
            u /= s; v /= s; w /= s;
            Vector2 q = a * u + b * v + c * w;
            outside = (q - p).magnitude;
            return new Vector3(u, v, w);
        }
    }
}
