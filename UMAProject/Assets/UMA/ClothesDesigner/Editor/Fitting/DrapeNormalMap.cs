using System;
using System.Collections.Generic;
using System.IO;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Bakes the folds of a fine drape into a tangent-space normal map for a coarser slot of the same
    /// pattern. Every mesh level of a pattern shares one UV layout (it comes from the drawn pieces), so a
    /// texel lies at the same place of the cloth on both meshes: no rays are cast. Each texel takes the
    /// fine surface's normal there, expressed in the coarse mesh's normal and tangent at the same place.
    /// </summary>
    public static class DrapeNormalMap
    {
        public sealed class Stats
        {
            public int size;
            /// <summary>Texels inside the pieces (before the gutter is filled).</summary>
            public int covered;
            /// <summary>The mean and largest angle between the fine and the coarse surface (degrees): how much the map adds.</summary>
            public float meanDegrees, maxDegrees;
        }

        /// <summary>
        /// The normal map, encoded the way Unity reads normal maps (x and y in red and green, +y up the V
        /// direction, z in blue). Triangles must face out, as baked; the coarse normals and tangents must
        /// be the ones the slot is made with.
        /// </summary>
        public static Texture2D Bake(PatternMesh fine, Vector3[] finePositions, int[] fineTriangles,
            PatternMesh coarse, int[] coarseTriangles, Vector3[] coarseNormals, Vector4[] coarseTangents,
            int size, int gutter, out Stats stats)
        {
            stats = new Stats { size = size };
            int texels = size * size;

            // The coarse frame under each texel.
            var coarseTri = new int[texels];
            var coarseBary = new Vector3[texels];
            for (int i = 0; i < texels; i++) coarseTri[i] = -1;
            for (int t = 0; t < coarseTriangles.Length; t += 3)
            {
                int a = coarseTriangles[t], b = coarseTriangles[t + 1], c = coarseTriangles[t + 2];
                Rasterize(coarse.uv[a], coarse.uv[b], coarse.uv[c], size, (texel, w) => { coarseTri[texel] = t; coarseBary[texel] = w; });
            }

            // The fine surface's normal under each texel; normals shared by the battle-buddy rule.
            var fineNormals = new Vector3[finePositions.Length];
            new UMABuddyNormals(finePositions, fineTriangles).Recalculate(finePositions, fineNormals);
            var fineNormal = new Vector3[texels];
            var hasFine = new bool[texels];
            for (int t = 0; t < fineTriangles.Length; t += 3)
            {
                int a = fineTriangles[t], b = fineTriangles[t + 1], c = fineTriangles[t + 2];
                Rasterize(fine.uv[a], fine.uv[b], fine.uv[c], size, (texel, w) =>
                {
                    fineNormal[texel] = fineNormals[a] * w.x + fineNormals[b] * w.y + fineNormals[c] * w.z;
                    hasFine[texel] = true;
                });
            }

            var pixels = new Color32[texels];
            var filled = new bool[texels];
            double sum = 0;
            for (int i = 0; i < texels; i++)
            {
                if (!hasFine[i] || coarseTri[i] < 0) continue;
                int t = coarseTri[i];
                Vector3 w = coarseBary[i];
                int a = coarseTriangles[t], b = coarseTriangles[t + 1], c = coarseTriangles[t + 2];
                Vector3 n = (coarseNormals[a] * w.x + coarseNormals[b] * w.y + coarseNormals[c] * w.z).normalized;
                Vector4 t4 = coarseTangents[a] * w.x + coarseTangents[b] * w.y + coarseTangents[c] * w.z;
                Vector3 tangent = new Vector3(t4.x, t4.y, t4.z);
                tangent = (tangent - n * Vector3.Dot(n, tangent)).normalized;
                float sign = coarseTangents[a].w < 0f ? -1f : 1f;
                Vector3 bitangent = Vector3.Cross(n, tangent) * sign;
                Vector3 fineN = fineNormal[i].normalized;
                var local = new Vector3(Vector3.Dot(fineN, tangent), Vector3.Dot(fineN, bitangent), Vector3.Dot(fineN, n));
                if (local.z < 0.05f) local.z = 0.05f; // never turned past the edge of the surface
                local.Normalize();
                pixels[i] = Encode(local);
                filled[i] = true;
                float angle = Vector3.Angle(fineN, n);
                sum += angle;
                stats.maxDegrees = Mathf.Max(stats.maxDegrees, angle);
                stats.covered++;
            }
            stats.meanDegrees = stats.covered > 0 ? (float)(sum / stats.covered) : 0f;

            // Grow each island into its gutter, so filtering and mip maps do not pull in the flat background.
            Dilate(pixels, filled, size, Mathf.Max(2, gutter));
            Color32 flat = Encode(Vector3.forward);
            for (int i = 0; i < texels; i++) if (!filled[i]) pixels[i] = flat;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "Drape Normal Map" };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Normals (battle-buddy rule) and tangents (Unity's, as the bake gives the slot) of a mesh, in the
        /// space its positions are in.
        /// </summary>
        public static void SlotFrame(Vector3[] positions, int[] triangles, IList<Vector2> uv, out Vector3[] normals, out Vector4[] tangents)
        {
            normals = new Vector3[positions.Length];
            new UMABuddyNormals(positions, triangles).Recalculate(positions, normals);
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                mesh.SetVertices(positions);
                mesh.SetTriangles(triangles, 0);
                mesh.SetUVs(0, new List<Vector2>(uv));
                mesh.SetNormals(normals);
                mesh.RecalculateTangents();
                tangents = mesh.tangents;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>Writes the map as a PNG and imports it as a normal map. Returns the imported texture.</summary>
        public static Texture2D Write(Texture2D map, string assetPath)
        {
            string full = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, map.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                bool change = importer.textureType != TextureImporterType.NormalMap || importer.isReadable;
                importer.textureType = TextureImporterType.NormalMap;
                importer.isReadable = false;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, map.width);
                if (change) importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Color32 Encode(Vector3 n) =>
            new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);

        /// <summary>Calls <paramref name="write"/> for each texel whose centre is inside the UV triangle, with its barycentric weights.</summary>
        private static void Rasterize(Vector2 uvA, Vector2 uvB, Vector2 uvC, int size, Action<int, Vector3> write)
        {
            Vector2 a = uvA * size, b = uvB * size, c = uvC * size;
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-9f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            const float edge = -1e-4f; // texels on a shared edge go to both triangles: no cracks
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((b.x - p.x) * (c.y - p.y) - (b.y - p.y) * (c.x - p.x)) / area;
                    float w1 = ((c.x - p.x) * (a.y - p.y) - (c.y - p.y) * (a.x - p.x)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < edge || w1 < edge || w2 < edge) continue;
                    write(y * size + x, new Vector3(w0, w1, w2));
                }
            }
        }

        private static void Dilate(Color32[] pixels, bool[] filled, int size, int steps)
        {
            var next = new bool[filled.Length];
            for (int step = 0; step < steps; step++)
            {
                Array.Copy(filled, next, filled.Length);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        if (filled[i]) continue;
                        int r = 0, g = 0, bl = 0, n = 0;
                        void Take(int j) { if (filled[j]) { r += pixels[j].r; g += pixels[j].g; bl += pixels[j].b; n++; } }
                        if (x > 0) Take(i - 1);
                        if (x < size - 1) Take(i + 1);
                        if (y > 0) Take(i - size);
                        if (y < size - 1) Take(i + size);
                        if (n == 0) continue;
                        pixels[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(bl / n), 255);
                        next[i] = true;
                    }
                }
                Array.Copy(next, filled, filled.Length);
            }
        }
    }
}
