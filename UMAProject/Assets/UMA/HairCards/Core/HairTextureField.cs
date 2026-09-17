using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>Cached texture-resolution connectivity/distance field. This CPU sampling
    /// graph is not the authoring mesh: all persisted anchors retain their original faces.</summary>
    internal sealed class HairTextureField
    {
        private sealed class Face { public int resolution; public int[] grid; }
        private readonly Dictionary<long, Face> faces = new Dictionary<long, Face>();
        private readonly HashSet<long> emptyFaces = new HashSet<long>();
        private readonly HairSurfaceFields graph = new HairSurfaceFields();

        internal HairTextureField(IReadOnlyList<Vector3> source, IReadOnlyList<Vector3> sourceNormals,
            IReadOnlyList<int[]> submeshes, HairGrowthMap map)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            var values = new List<float>(); var indices = new List<int>();
            var canonical = new Dictionary<Vector3, int>(); var owners = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            { if (!canonical.TryGetValue(source[i], out int owner)) { owner = i; canonical.Add(source[i], i); } owners[i] = owner; }
            // Refinement creates T junctions beside coarse, unpainted faces. Only actual
            // source borders are hairline seeds, never those internal sampling edges.
            var uses = new Dictionary<(int, int), int>(); var borders = new List<int>();
            (int, int) Key(int a, int b) { a = owners[a]; b = owners[b]; return a < b ? (a, b) : (b, a); }
            foreach (var ts in submeshes) for (int i = 0; i + 2 < ts.Length; i += 3)
                for (int edge = 0; edge < 3; edge++)
                { var key = Key(ts[i + edge], ts[i + (edge + 1) % 3]); uses.TryGetValue(key, out int count); uses[key] = count + 1; }
            Vector3 Edge(int a, int b, int t, int n)
            {
                a = owners[a]; b = owners[b]; if (a > b) { (a, b) = (b, a); t = n - t; }
                return (source[a] * (n - t) + source[b] * t) / n;
            }
            for (int sub = 0; sub < submeshes.Count; sub++)
            {
                var triangles = submeshes[sub];
                for (int offset = 0; offset + 2 < triangles.Length; offset += 3)
                {
                    int a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2], triangle = offset / 3;
                    var tile = map.texture.Find(sub, triangle);
                    if (tile == null && map.BaseVertex(a) <= .001f && map.BaseVertex(b) <= .001f && map.BaseVertex(c) <= .001f)
                    { emptyFaces.Add(HairTextureMap.FaceKey(sub, triangle)); continue; }
                    int n = tile != null ? map.texture.resolution : 1;
                    var face = new Face { resolution = n, grid = new int[(n + 1) * (n + 1)] };
                    faces.Add(HairTextureMap.FaceKey(sub, triangle), face);
                    for (int y = 0; y <= n; y++) for (int x = 0; x <= n - y; x++)
                    {
                        var bc = HairTextureMap.TexelBarycentric(x, y, n);
                        Vector3 p = y == 0 ? Edge(a, b, x, n) : x == 0 ? Edge(a, c, y, n) :
                            x + y == n ? Edge(b, c, y, n) : source[a] * bc.x + source[b] * bc.y + source[c] * bc.z;
                        Vector3 normal = sourceNormals.Count == source.Count ? sourceNormals[a] * bc.x + sourceNormals[b] * bc.y + sourceNormals[c] * bc.z :
                            Vector3.Cross(source[b] - source[a], source[c] - source[a]);
                        face.grid[y * (n + 1) + x] = positions.Count;
                        if ((y == 0 && uses[Key(a, b)] == 1) || (x == 0 && uses[Key(a, c)] == 1) ||
                            (x + y == n && uses[Key(b, c)] == 1)) borders.Add(positions.Count);
                        positions.Add(p); normals.Add(normal.normalized);
                        float value = map.SampleTriangle(sub, triangle, a, b, c, bc);
                        values.Add(float.IsFinite(value) ? Mathf.Clamp01(value) : 0f);
                    }
                    for (int y = 0; y < n; y++) for (int x = 0; x < n - y; x++)
                    {
                        int q = y * (n + 1) + x;
                        void Cell(int i, int j, int k)
                        {
                            if (values[i] <= .001f && values[j] <= .001f && values[k] <= .001f) return;
                            indices.Add(i); indices.Add(j); indices.Add(k);
                        }
                        Cell(face.grid[q], face.grid[q + 1], face.grid[q + n + 1]);
                        if (x + y < n - 1)
                            Cell(face.grid[q + n + 2], face.grid[q + n + 1], face.grid[q + 1]);
                    }
                }
            }
            graph.PrepareGraph(positions, normals, values, indices.ToArray(), borders);
        }

        private bool Find(HairSurfaceAnchor anchor, out int a, out int b, out int c, out Vector3 weights)
        {
            a = b = c = 0; weights = default;
            if (!anchor.IsValid || !faces.TryGetValue(HairTextureMap.FaceKey(anchor.SubmeshIndex, anchor.TriangleIndex), out var face)) return false;
            HairTextureMap.FilterCoordinates(anchor.Barycentric, face.resolution, out int x, out int y, out int z, out weights);
            a = face.grid[x]; b = face.grid[y]; c = face.grid[z]; return true;
        }
        internal float Distance(HairSurfaceAnchor anchor) => Find(anchor, out int a, out int b, out int c, out var w)
            ? graph.GraphDistance(a) * w.x + graph.GraphDistance(b) * w.y + graph.GraphDistance(c) * w.z :
                emptyFaces.Contains(HairTextureMap.FaceKey(anchor.SubmeshIndex, anchor.TriangleIndex)) ? 0f : 1000f;
        internal Vector3 Inward(HairSurfaceAnchor anchor) => Find(anchor, out int a, out int b, out int c, out var w)
            ? (graph.GraphInward(a) * w.x + graph.GraphInward(b) * w.y + graph.GraphInward(c) * w.z).normalized : Vector3.zero;
        internal int Region(HairSurfaceAnchor anchor)
        {
            if (!Find(anchor, out int a, out int b, out int c, out var w)) return -1;
            int region = -1; float best = -1;
            void Consider(int vertex, float weight) { if (weight > best && graph.GraphRegion(vertex) >= 0) { region = graph.GraphRegion(vertex); best = weight; } }
            Consider(a, w.x); Consider(b, w.y); Consider(c, w.z); return region;
        }
    }
}
