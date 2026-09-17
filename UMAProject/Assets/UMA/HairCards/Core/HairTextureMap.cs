using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UMA.HairCards
{
    public enum HairMapStorage { Vertices, Texture }

    /// <summary>
    /// Sparse, single-channel face textures in dedicated barycentric paint coordinates.
    /// Character UVs are never used. Unedited faces retain the original vertex field exactly.
    /// Pixel arrays are serialized with the groom, so Undo, duplication and recovery are atomic.
    /// </summary>
    [Serializable]
    public sealed class HairTextureMap : ISerializationCallbackReceiver
    {
        [Serializable]
        public sealed class Tile
        {
            public int submesh, triangle, a, b, c;
            public float[] pixels = Array.Empty<float>();
            public long Key => FaceKey(submesh, triangle);
        }

        public int resolution = 16;
        public string topology;
        public List<Tile> tiles = new List<Tile>();
        [NonSerialized] private Dictionary<long, Tile> lookup;
        [NonSerialized] private Dictionary<int, float> corners;
        [NonSerialized] private int revision;
        [NonSerialized] private int validatedRevision, validatedCount;
        [NonSerialized] private string validatedTopology;
        [NonSerialized] private bool validData;
        private static int nextRevision;
        public int Revision { get { if (revision == 0) Touch(); return revision; } }
        public long PixelBytes => (long)tiles.Count * (resolution + 1) * (resolution + 1) * sizeof(float);
        public static long FaceKey(int submesh, int triangle) => ((long)submesh << 32) | (uint)triangle;
        public static int ClampResolution(int value) => value <= 8 ? 8 : value <= 16 ? 16 : value <= 32 ? 32 : 64;
        public void Touch() { revision = Interlocked.Increment(ref nextRevision); corners = null; }
        public void Clear() { tiles.Clear(); lookup = null; Touch(); }

        public bool IsValid(string signature, int vertexCount)
        {
            int current = Revision;
            if (validatedRevision == current && validatedCount == vertexCount && validatedTopology == signature) return validData;
            validatedRevision = current; validatedCount = vertexCount; validatedTopology = signature; validData = false;
            if (resolution != ClampResolution(resolution) || topology != signature || tiles == null) return false;
            var keys = new HashSet<long>();
            foreach (var tile in tiles)
            {
                if (!Valid(tile) || !keys.Add(tile.Key) || tile.submesh < 0 || tile.triangle < 0 ||
                    (uint)tile.a >= vertexCount || (uint)tile.b >= vertexCount || (uint)tile.c >= vertexCount) return false;
                foreach (float value in tile.pixels) if (!float.IsFinite(value)) return false;
            }
            validData = true; return true;
        }
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { lookup = null; corners = null; revision = validatedRevision = 0; }

        public Tile Find(int submesh, int triangle)
        {
            if (lookup == null || lookup.Count != tiles.Count)
            {
                lookup = new Dictionary<long, Tile>(tiles.Count);
                foreach (var tile in tiles) if (tile != null) lookup[tile.Key] = tile;
            }
            return lookup.TryGetValue(FaceKey(submesh, triangle), out var result) ? result : null;
        }

        public bool TryCorner(int vertex, out float value)
        {
            if (corners == null)
            {
                corners = new Dictionary<int, float>();
                foreach (var tile in tiles)
                {
                    if (!Valid(tile)) continue;
                    corners[tile.a] = tile.pixels[0]; corners[tile.b] = tile.pixels[resolution];
                    corners[tile.c] = tile.pixels[resolution * (resolution + 1)];
                }
            }
            return corners.TryGetValue(vertex, out value);
        }
        public bool Valid(Tile tile) => tile != null && tile.pixels != null && tile.pixels.Length == (resolution + 1) * (resolution + 1);

        public Tile EnsureTile(HairGrowthMap map, int submesh, int triangle, int a, int b, int c)
        {
            var tile = Find(submesh, triangle);
            if (tile != null) return tile;
            tile = new Tile { submesh = submesh, triangle = triangle, a = a, b = b, c = c,
                pixels = new float[(resolution + 1) * (resolution + 1)] };
            float av = map.BaseVertex(a), bv = map.BaseVertex(b), cv = map.BaseVertex(c);
            for (int y = 0; y <= resolution; y++)
                for (int x = 0; x <= resolution; x++)
                {
                    var bc = TexelBarycentric(x, y, resolution);
                    tile.pixels[y * (resolution + 1) + x] = av * bc.x + bv * bc.y + cv * bc.z;
                }
            tiles.Add(tile); lookup[tile.Key] = tile; Touch(); return tile;
        }

        public static Vector3 TexelBarycentric(int x, int y, int n)
        {
            float u = x / (float)n, v = y / (float)n;
            if (u + v > 1f) { float excess = (u + v - 1f) * .5f; u -= excess; v -= excess; }
            return new Vector3(1f - u - v, u, v);
        }

        // Piecewise-linear texture filtering on the triangular domain. Unlike bilinear
        // filtering, it cannot borrow a texel from outside the face along its diagonal.
        public static void FilterCoordinates(Vector3 bc, int n, out int a, out int b, out int c, out Vector3 weights)
        {
            float u = Mathf.Clamp01(bc.y), v = Mathf.Clamp01(bc.z);
            if (u + v > 1f) { float scale = 1f / (u + v); u *= scale; v *= scale; }
            float px = u * n, py = v * n;
            int x = Mathf.Min(n - 1, Mathf.FloorToInt(px)), y = Mathf.Min(n - 1, Mathf.FloorToInt(py));
            float fx = px - x, fy = py - y; int stride = n + 1;
            if (x + y >= n) { x = Mathf.Max(0, n - y - 1); fx = px - x; }
            if (fx + fy <= 1f || x + y == n - 1)
            {
                a = y * stride + x; b = a + 1; c = a + stride;
                weights = new Vector3(Mathf.Max(0f, 1f - fx - fy), fx, fy);
            }
            else
            {
                a = (y + 1) * stride + x + 1; b = a - 1; c = a - stride;
                weights = new Vector3(fx + fy - 1f, 1f - fx, 1f - fy);
            }
        }
        public float Sample(Tile tile, Vector3 barycentric)
        {
            FilterCoordinates(barycentric, resolution, out int a, out int b, out int c, out var weights);
            return tile.pixels[a] * weights.x + tile.pixels[b] * weights.y + tile.pixels[c] * weights.z;
        }

        public void Resize(int newResolution)
        {
            newResolution = ClampResolution(newResolution); if (resolution == newResolution) return;
            foreach (var tile in tiles)
            {
                var data = new float[(newResolution + 1) * (newResolution + 1)];
                for (int y = 0; y <= newResolution; y++) for (int x = 0; x <= newResolution; x++)
                    data[y * (newResolution + 1) + x] = Sample(tile, TexelBarycentric(x, y, newResolution));
                tile.pixels = data;
            }
            resolution = newResolution; Touch();
        }
        public HairTextureMap Clone() => JsonUtility.FromJson<HairTextureMap>(JsonUtility.ToJson(this));
    }
}
