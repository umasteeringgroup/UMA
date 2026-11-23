using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// A labelled checker texture drawn from the pattern: each piece in its own colour with 5 cm
    /// squares measured on the flat cloth, its outline, its grain arrow and its name. On the character it
    /// shows at once whether a print lands on the right piece, the right way up and unstretched.
    /// </summary>
    public static class UvCheckTexture
    {
        private static readonly Color[] PieceColors =
        {
            new Color(0.95f, 0.55f, 0.35f), new Color(0.35f, 0.65f, 0.95f), new Color(0.45f, 0.85f, 0.45f),
            new Color(0.95f, 0.85f, 0.35f), new Color(0.75f, 0.5f, 0.95f), new Color(0.4f, 0.85f, 0.85f)
        };

        /// <summary>Draws the texture into memory. Pixels outside every piece are transparent grey.</summary>
        public static Texture2D Draw(ClothesPattern pattern, PatternMesh mesh, int size)
        {
            var pixels = new Color32[size * size];
            var empty = new Color32(128, 128, 128, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = empty;

            // Fill the triangles: the checker is measured on the flat cloth, so it shows stretch and grain.
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                int a = mesh.triangles[t], b = mesh.triangles[t + 1], c = mesh.triangles[t + 2];
                Color baseColor = PieceColors[mesh.piece[a] % PieceColors.Length];
                FillTriangle(pixels, size, mesh.uv[a] * size, mesh.uv[b] * size, mesh.uv[c] * size, mesh.flat[a], mesh.flat[b], mesh.flat[c], baseColor);
            }
            // Outlines, grain arrows and names.
            for (int p = 0; p < pattern.pieces.Count; p++)
            {
                PatternPiece piece = pattern.pieces[p];
                List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, null);
                for (int i = 0; i < outline.Count; i++)
                {
                    Vector2 u0 = UvLayout.ToUV(pattern, p, outline[i]) * size, u1 = UvLayout.ToUV(pattern, p, outline[(i + 1) % outline.Count]) * size;
                    Line(pixels, size, u0, u1, new Color32(20, 20, 20, 255), 2);
                }
                Rect bounds = BoundsOf(outline);
                Vector2 center = bounds.center;
                float grain = piece.grainAngle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(-Mathf.Sin(grain), Mathf.Cos(grain));
                Vector2 g0 = UvLayout.ToUV(pattern, p, center - dir * bounds.height * 0.3f) * size;
                Vector2 g1 = UvLayout.ToUV(pattern, p, center + dir * bounds.height * 0.3f) * size;
                Line(pixels, size, g0, g1, new Color32(255, 255, 255, 255), 2);
                Vector2 back = (g0 - g1).normalized * size * 0.02f;
                Vector2 side = new Vector2(-back.y, back.x) * 0.6f;
                Line(pixels, size, g1, g1 + back + side, new Color32(255, 255, 255, 255), 2);
                Line(pixels, size, g1, g1 + back - side, new Color32(255, 255, 255, 255), 2);
                string label = (piece.name ?? "PIECE").ToUpperInvariant();
                Vector2 labelAt = UvLayout.ToUV(pattern, p, center + new Vector2(0f, bounds.height * 0.12f)) * size;
                int pixelSize = Mathf.Max(1, size / 160);
                Text(pixels, size, label, labelAt, pixelSize, new Color32(15, 15, 15, 255));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Draws the texture and saves it as a PNG asset. Returns the imported texture.</summary>
        public static Texture2D Write(ClothesPattern pattern, PatternMesh mesh, string assetPath, int size)
        {
            Texture2D texture = Draw(pattern, mesh, Mathf.Clamp(size, 256, 4096));
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Rect BoundsOf(List<Vector2> points)
        {
            Vector2 min = points[0], max = points[0];
            foreach (Vector2 p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void FillTriangle(Color32[] pixels, int size, Vector2 a, Vector2 b, Vector2 c, Vector2 fa, Vector2 fb, Vector2 fc, Color baseColor)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-8f) return;
            Color dark = baseColor * 0.75f; dark.a = 1f;
            Color light = baseColor; light.a = 1f;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((b.x - p.x) * (c.y - p.y) - (b.y - p.y) * (c.x - p.x)) / area;
                    float w1 = ((c.x - p.x) * (a.y - p.y) - (c.y - p.y) * (a.x - p.x)) / area;
                    float w2 = 1f - w0 - w1;
                    // A small margin so the gutter round each island is filled too and no seam line shows.
                    if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                    Vector2 f = fa * w0 + fb * w1 + fc * w2;
                    bool odd = ((Mathf.FloorToInt(f.x / 5f) + Mathf.FloorToInt(f.y / 5f)) & 1) == 1;
                    pixels[y * size + x] = odd ? dark : light;
                }
            }
        }

        private static void Line(Color32[] pixels, int size, Vector2 a, Vector2 b, Color32 color, int width)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                for (int dy = -width / 2; dy <= width / 2; dy++)
                    for (int dx = -width / 2; dx <= width / 2; dx++)
                    {
                        int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                        if (x >= 0 && y >= 0 && x < size && y < size) pixels[y * size + x] = color;
                    }
            }
        }

        // 5x7 capitals and digits, one row per byte, top row first; bit 4 is the left column.
        private static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            ['A'] = new byte[] { 14, 17, 17, 31, 17, 17, 17 }, ['B'] = new byte[] { 30, 17, 17, 30, 17, 17, 30 },
            ['C'] = new byte[] { 14, 17, 16, 16, 16, 17, 14 }, ['D'] = new byte[] { 30, 17, 17, 17, 17, 17, 30 },
            ['E'] = new byte[] { 31, 16, 16, 30, 16, 16, 31 }, ['F'] = new byte[] { 31, 16, 16, 30, 16, 16, 16 },
            ['G'] = new byte[] { 14, 17, 16, 23, 17, 17, 15 }, ['H'] = new byte[] { 17, 17, 17, 31, 17, 17, 17 },
            ['I'] = new byte[] { 14, 4, 4, 4, 4, 4, 14 }, ['J'] = new byte[] { 7, 2, 2, 2, 2, 18, 12 },
            ['K'] = new byte[] { 17, 18, 20, 24, 20, 18, 17 }, ['L'] = new byte[] { 16, 16, 16, 16, 16, 16, 31 },
            ['M'] = new byte[] { 17, 27, 21, 21, 17, 17, 17 }, ['N'] = new byte[] { 17, 17, 25, 21, 19, 17, 17 },
            ['O'] = new byte[] { 14, 17, 17, 17, 17, 17, 14 }, ['P'] = new byte[] { 30, 17, 17, 30, 16, 16, 16 },
            ['Q'] = new byte[] { 14, 17, 17, 17, 21, 18, 13 }, ['R'] = new byte[] { 30, 17, 17, 30, 20, 18, 17 },
            ['S'] = new byte[] { 15, 16, 16, 14, 1, 1, 30 }, ['T'] = new byte[] { 31, 4, 4, 4, 4, 4, 4 },
            ['U'] = new byte[] { 17, 17, 17, 17, 17, 17, 14 }, ['V'] = new byte[] { 17, 17, 17, 17, 17, 10, 4 },
            ['W'] = new byte[] { 17, 17, 17, 21, 21, 21, 10 }, ['X'] = new byte[] { 17, 17, 10, 4, 10, 17, 17 },
            ['Y'] = new byte[] { 17, 17, 10, 4, 4, 4, 4 }, ['Z'] = new byte[] { 31, 1, 2, 4, 8, 16, 31 },
            ['0'] = new byte[] { 14, 17, 19, 21, 25, 17, 14 }, ['1'] = new byte[] { 4, 12, 4, 4, 4, 4, 14 },
            ['2'] = new byte[] { 14, 17, 1, 2, 4, 8, 31 }, ['3'] = new byte[] { 31, 2, 4, 2, 1, 17, 14 },
            ['4'] = new byte[] { 2, 6, 10, 18, 31, 2, 2 }, ['5'] = new byte[] { 31, 16, 30, 1, 1, 17, 14 },
            ['6'] = new byte[] { 6, 8, 16, 30, 17, 17, 14 }, ['7'] = new byte[] { 31, 1, 2, 4, 8, 8, 8 },
            ['8'] = new byte[] { 14, 17, 17, 14, 17, 17, 14 }, ['9'] = new byte[] { 14, 17, 17, 15, 1, 2, 12 },
            ['-'] = new byte[] { 0, 0, 0, 31, 0, 0, 0 }
        };

        /// <summary>Writes text centred on a pixel position, upright in the texture.</summary>
        private static void Text(Color32[] pixels, int size, string text, Vector2 center, int scale, Color32 color)
        {
            int width = text.Length * 6 * scale;
            int left = Mathf.RoundToInt(center.x - width * 0.5f);
            int top = Mathf.RoundToInt(center.y + 3.5f * scale);
            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(text[i], out byte[] rows)) continue;
                for (int r = 0; r < 7; r++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if ((rows[r] & (16 >> col)) == 0) continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int x = left + (i * 6 + col) * scale + sx;
                                int y = top - r * scale - sy;
                                if (x >= 0 && y >= 0 && x < size && y < size) pixels[y * size + x] = color;
                            }
                    }
                }
            }
        }
    }
}
