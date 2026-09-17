using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Cached source-space geometry and face adjacency for texture painting.
    /// No Mesh.vertices reads, full-source scans or GPU readbacks per dab.</summary>
    public sealed class HairTexturePaintSurface
    {
        public readonly struct Face
        {
            public readonly int submesh, triangle, a, b, c;
            public Face(int sub, int tri, int a, int b, int c) { submesh = sub; triangle = tri; this.a = a; this.b = b; this.c = c; }
        }
        private struct StrokeSample { public float initial, maximum; }
        private static readonly ProfilerMarker PaintMarker = new ProfilerMarker("HairCards.PaintTexture");
        public readonly Mesh mesh;
        public readonly Vector3[] vertices;
        public readonly List<Face> faces = new List<Face>();
        private readonly HairMeshRaycaster raycaster;
        private readonly List<int> candidates = new List<int>();
        private readonly HashSet<int> combined = new HashSet<int>();
        private readonly Dictionary<(int face, int pixel), StrokeSample> stroke = new Dictionary<(int, int), StrokeSample>();
        private readonly Dictionary<long, int> faceLookup = new Dictionary<long, int>();
        private int[,] neighbors;
        private int[] vertexOwners;

        public HairTexturePaintSurface(Mesh source)
        {
            mesh = source; vertices = source.vertices; raycaster = new HairMeshRaycaster(source);
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var triangles = source.GetTriangles(sub);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                { faceLookup.Add(HairTextureMap.FaceKey(sub, i / 3), faces.Count); faces.Add(new Face(sub, i / 3, triangles[i], triangles[i + 1], triangles[i + 2])); }
            }
        }
        public void EndStroke() => stroke.Clear();
        public Vector3 Position(Face face, Vector3 bc) => vertices[face.a] * bc.x + vertices[face.b] * bc.y + vertices[face.c] * bc.z;
        public float Sample(HairGrowthMap map, Face face, Vector3 bc) => map.SampleTriangle(face.submesh, face.triangle, face.a, face.b, face.c, bc);

        public bool Paint(HairGrowthMap map, Vector3 center, float radius, bool mirror,
            float target, Func<Vector3, float> influence, Predicate<int> vertexVisible = null)
        {
            if (!map.UsesTexture || map.locked) return false;
            using var scope = PaintMarker.Auto();
            combined.Clear(); raycaster.QuerySphere(center, radius, candidates);
            foreach (int face in candidates) combined.Add(face);
            if (mirror)
            {
                raycaster.QuerySphere(new Vector3(-center.x, center.y, center.z), radius, candidates);
                foreach (int face in candidates) combined.Add(face);
            }
            bool changed = false; int n = map.texture.resolution;
            foreach (int faceIndex in combined)
            {
                var face = faces[faceIndex];
                if (vertexVisible != null && (!vertexVisible(face.a) || !vertexVisible(face.b) || !vertexVisible(face.c))) continue;
                var tile = map.texture.Find(face.submesh, face.triangle);
                for (int y = 0; y <= n; y++) for (int x = 0; x <= n - y; x++)
                {
                    float weight = Mathf.Clamp01(influence(Position(face, HairTextureMap.TexelBarycentric(x, y, n))));
                    if (weight <= 0f) continue;
                    tile ??= map.texture.EnsureTile(map, face.submesh, face.triangle, face.a, face.b, face.c);
                    int pixel = y * (n + 1) + x; var key = (faceIndex, pixel);
                    if (!stroke.TryGetValue(key, out var sample)) sample.initial = tile.pixels[pixel];
                    if (weight <= sample.maximum) continue;
                    sample.maximum = weight; stroke[key] = sample;
                    float value = Mathf.Lerp(sample.initial, target, weight);
                    if (tile.pixels[pixel] == value) continue;
                    tile.pixels[pixel] = value; changed = true;
                }
            }
            if (changed) map.texture.Touch(); return changed;
        }

        private void EnsureNeighbors()
        {
            if (neighbors != null) return;
            var owners = vertexOwners = new int[vertices.Length]; var weld = new Dictionary<Vector3, int>();
            for (int i = 0; i < vertices.Length; i++)
            { if (!weld.TryGetValue(vertices[i], out int owner)) { owner = i; weld.Add(vertices[i], i); } owners[i] = owner; }
            neighbors = new int[faces.Count, 3];
            var edges = new Dictionary<(int, int), (int face, int edge)>();
            for (int f = 0; f < faces.Count; f++) for (int edge = 0; edge < 3; edge++)
            {
                neighbors[f, edge] = -1; var face = faces[f];
                int a = owners[edge == 0 ? face.b : edge == 1 ? face.c : face.a];
                int b = owners[edge == 0 ? face.c : edge == 1 ? face.a : face.b];
                var key = a < b ? (a, b) : (b, a);
                if (edges.TryGetValue(key, out var other)) { neighbors[f, edge] = other.face; neighbors[other.face, other.edge] = f; }
                else edges.Add(key, (f, edge));
            }
        }

        public void Smooth(HairGrowthMap map, int iterations)
        {
            EnsureNeighbors(); int n = map.texture.resolution;
            // Promote nonuniform base faces as well, so smoothing a newly converted map
            // operates at texture resolution instead of modifying its old control vertices.
            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f]; float a = map.BaseVertex(face.a), b = map.BaseVertex(face.b), c = map.BaseVertex(face.c);
                if (a != b || b != c) map.texture.EnsureTile(map, face.submesh, face.triangle, face.a, face.b, face.c);
            }
            for (int iteration = 0; iteration < Mathf.Max(1, iterations); iteration++)
            {
                var touched = new List<HairTextureMap.Tile>(map.texture.tiles);
                foreach (var tile in touched)
                {
                    int f = faceLookup[tile.Key];
                    for (int edge = 0; edge < 3; edge++) if (neighbors[f, edge] >= 0)
                    { var face = faces[neighbors[f, edge]]; map.texture.EnsureTile(map, face.submesh, face.triangle, face.a, face.b, face.c); }
                }
                var buffers = new List<float[]>(map.texture.tiles.Count);
                foreach (var tile in map.texture.tiles)
                {
                    int f = faceLookup[tile.Key]; var data = (float[])tile.pixels.Clone();
                    float Near(int x, int y)
                    {
                        var bc = new Vector3(1f - (x + y) / (float)n, x / (float)n, y / (float)n);
                        if (bc.x >= 0 && bc.y >= 0 && bc.z >= 0) return Sample(map, faces[f], bc);
                        int edge = bc.x < 0 ? 0 : bc.y < 0 ? 1 : 2, other = neighbors[f, edge];
                        var sourceFace = faces[f];
                        if (other < 0) return Sample(map, sourceFace, HairTextureMap.TexelBarycentric(Mathf.Clamp(x, 0, n), Mathf.Clamp(y, 0, n), n));
                        var next = faces[other]; var p = HairMeshUtility.ClosestPointOnTriangle(Position(sourceFace, bc), vertices[next.a], vertices[next.b], vertices[next.c]);
                        return Sample(map, next, HairMeshUtility.Barycentric(p, vertices[next.a], vertices[next.b], vertices[next.c]));
                    }
                    for (int y = 0; y <= n; y++) for (int x = 0; x <= n - y; x++)
                        data[y * (n + 1) + x] = (Near(x, y) + Near(x - 1, y) + Near(x + 1, y) + Near(x, y - 1) + Near(x, y + 1) + Near(x - 1, y + 1) + Near(x + 1, y - 1)) / 7f;
                    buffers.Add(data);
                }
                // A UV seam or differently shaped adjacent face must not get two
                // different answers for the same edge texel after filtering.
                var seamValues = new Dictionary<(int, int, int), (float total, int count)>();
                (int, int, int) Seam(HairTextureMap.Tile tile, int x, int y)
                {
                    int a, b, t;
                    if (x == 0 && y == 0) return (vertexOwners[tile.a], vertexOwners[tile.a], 0);
                    if (x == n) return (vertexOwners[tile.b], vertexOwners[tile.b], 0);
                    if (y == n) return (vertexOwners[tile.c], vertexOwners[tile.c], 0);
                    if (y == 0) { a = vertexOwners[tile.a]; b = vertexOwners[tile.b]; t = x; }
                    else if (x == 0) { a = vertexOwners[tile.a]; b = vertexOwners[tile.c]; t = y; }
                    else { a = vertexOwners[tile.b]; b = vertexOwners[tile.c]; t = y; }
                    return a < b ? (a, b, t) : (b, a, n - t);
                }
                for (int t = 0; t < buffers.Count; t++)
                    for (int y = 0; y <= n; y++) for (int x = 0; x <= n - y; x++)
                        if (x == 0 || y == 0 || x + y == n)
                        {
                            var key = Seam(map.texture.tiles[t], x, y); seamValues.TryGetValue(key, out var aggregate);
                            seamValues[key] = (aggregate.total + buffers[t][y * (n + 1) + x], aggregate.count + 1);
                        }
                for (int t = 0; t < buffers.Count; t++)
                {
                    for (int y = 0; y <= n; y++) for (int x = 0; x <= n - y; x++)
                        if (x == 0 || y == 0 || x + y == n)
                        { var value = seamValues[Seam(map.texture.tiles[t], x, y)]; buffers[t][y * (n + 1) + x] = value.total / value.count; }
                    map.texture.tiles[t].pixels = buffers[t];
                }
            }
            map.texture.Touch(); EndStroke();
        }
    }
}
