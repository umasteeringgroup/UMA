using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Cached painted-surface fields. Distances follow mesh edges; coincident UV-seam
    /// vertices are welded for connectivity only. No source mesh or painted map is modified.</summary>
    public sealed class HairSurfaceFields
    {
        private static readonly ProfilerMarker Marker = new ProfilerMarker("HairCards.SurfaceFields");
        private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        private readonly List<int[]> triangles = new List<int[]>();
        private readonly List<float> growth = new List<float>(), density = new List<float>();
        private float[] distances = Array.Empty<float>();
        private Vector3[] inward = Array.Empty<Vector3>();
        private int[] regions = Array.Empty<int>();
        private int textureStamp;
        private int distanceStamp;
        private HairTextureField textureField;
        private bool[] shadingSafe = Array.Empty<bool>();
        public int Revision { get; private set; }
        public IReadOnlyList<float> Distances => distances;

        public void Prepare(Mesh mesh, HairGroup group)
        {
            var source = new HairGroomEvaluator.SourceMeshReadCache();
            source.Begin(mesh);
            try { Prepare(source, group); } finally { source.End(); }
        }

        internal void Prepare(HairGroomEvaluator.SourceMeshReadCache source, HairGroup group)
        {
            if (!source.IsValid)
            {
                textureField = null; textureStamp = 0;
                if (vertices.Count > 0)
                {
                    vertices.Clear(); normals.Clear(); growth.Clear(); density.Clear(); triangles.Clear();
                    distances = Array.Empty<float>(); inward = Array.Empty<Vector3>(); regions = Array.Empty<int>(); Revision++;
                }
                return;
            }
            var positions = source.Vertices; var ns = source.Normals;
            HairGrowthMap g = group.FindMap(HairMapKind.GrowthArea), d = group.FindMap(HairMapKind.Density);
            int stamp = 17;
            foreach (var map in group.maps) unchecked { stamp = stamp * 31 + (map?.TextureRevision ?? 0); }
            int shapeStamp; unchecked { shapeStamp = (g?.TextureRevision ?? 0) * 31 + (d?.TextureRevision ?? 0); }
            bool same = shapeStamp == distanceStamp && vertices.Count == positions.Count && normals.Count == ns.Count && triangles.Count == source.SubmeshCount;
            for (int i = 0; same && i < positions.Count; i++)
                same = vertices[i].Equals(positions[i]) && growth[i] == Safe(g?.SampleVertex(i) ?? 0f) && density[i] == Safe(d?.SampleVertex(i) ?? 1f);
            for (int i = 0; same && i < ns.Count; i++) same = normals[i].Equals(ns[i]);
            for (int s = 0; same && s < triangles.Count; s++)
            {
                var indices = source.Triangles(s);
                same = indices != null && triangles[s].Length == indices.Count;
                for (int i = 0; same && i < indices.Count; i++) same = triangles[s][i] == indices[i];
            }
            if (same)
            {
                // Style-only texture edits invalidate mask/neighbor caches, not the
                // expensive, unchanged growth-distance graph.
                if (stamp != textureStamp) { textureStamp = stamp; Revision++; }
                return;
            }
            using var scope = Marker.Auto();
            vertices.Clear(); normals.Clear(); growth.Clear(); density.Clear(); triangles.Clear();
            for (int i = 0; i < positions.Count; i++)
            { vertices.Add(positions[i]); growth.Add(Safe(g?.SampleVertex(i) ?? 0f)); density.Add(Safe(d?.SampleVertex(i) ?? 1f)); }
            for (int i = 0; i < ns.Count; i++) normals.Add(ns[i]);
            for (int s = 0; s < source.SubmeshCount; s++)
            {
                var indices = source.Triangles(s); int[] copy = new int[indices?.Count ?? 0];
                for (int i = 0; i < copy.Length; i++) copy[i] = indices[i]; triangles.Add(copy);
            }
            textureStamp = stamp; distanceStamp = shapeStamp;
            BuildDistances();
            shadingSafe = Array.Empty<bool>();
            if (g?.UsesTexture == true || d?.UsesTexture == true)
            {
                shadingSafe = new bool[vertices.Count]; Array.Fill(shadingSafe, true);
                bool Covered(HairGrowthMap map, int sub, int tri, int a, int b, int c)
                {
                    if (map == null) return true;
                    var tile = map.UsesTexture ? map.texture.Find(sub, tri) : null;
                    if (tile != null) return !Array.Exists(tile.pixels, value => !float.IsFinite(value) || value <= .001f);
                    return map.BaseVertex(a) > .001f && map.BaseVertex(b) > .001f && map.BaseVertex(c) > .001f;
                }
                for (int sub = 0; sub < triangles.Count; sub++)
                    for (int offset = 0; offset + 2 < triangles[sub].Length; offset += 3)
                    {
                        var ts = triangles[sub]; int a = ts[offset], b = ts[offset + 1], c = ts[offset + 2];
                        if (!Covered(g, sub, offset / 3, a, b, c) || !Covered(d, sub, offset / 3, a, b, c))
                            shadingSafe[a] = shadingSafe[b] = shadingSafe[c] = false;
                    }
            }
            textureField = g?.UsesTexture == true && g.texture.tiles.Count > 0 ? new HairTextureField(positions, ns, triangles, g) : null;
            Revision++;
        }

        private static float Safe(float value) => float.IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        public float Sample(HairGrowthMap map, HairSurfaceAnchor anchor, float fallback = 1f)
        {
            if (map == null || !Indices(anchor, out int a, out int b, out int c)) return fallback;
            Vector3 bc = anchor.Barycentric;
            float value = map.SampleTriangle(anchor.SubmeshIndex, anchor.TriangleIndex, a, b, c, bc);
            return float.IsFinite(value) ? value : fallback;
        }
        public float Distance(HairSurfaceAnchor anchor)
        {
            if (textureField != null) return textureField.Distance(anchor);
            if (!Indices(anchor, out int a, out int b, out int c)) return 1000f;
            var bc = anchor.Barycentric;
            return distances[a] * bc.x + distances[b] * bc.y + distances[c] * bc.z;
        }
        public Vector3 Inward(HairSurfaceAnchor anchor)
        {
            if (textureField != null) return textureField.Inward(anchor);
            if (!Indices(anchor, out int a, out int b, out int c)) return Vector3.zero;
            var bc = anchor.Barycentric;
            return (inward[a] * bc.x + inward[b] * bc.y + inward[c] * bc.z).normalized;
        }
        public int Region(HairSurfaceAnchor anchor)
        {
            if (textureField != null) return textureField.Region(anchor);
            if (!Indices(anchor, out int a, out int b, out int c)) return -1;
            var bc = anchor.Barycentric;
            return regions[bc.x >= bc.y && bc.x >= bc.z ? a : bc.y >= bc.z ? b : c];
        }
        private bool Indices(HairSurfaceAnchor anchor, out int a, out int b, out int c)
        {
            a = b = c = 0;
            if (!anchor.IsValid || (uint)anchor.SubmeshIndex >= triangles.Count) return false;
            var indices = triangles[anchor.SubmeshIndex]; int start = anchor.TriangleIndex * 3;
            if (start < 0 || start + 2 >= indices.Length) return false;
            a = indices[start]; b = indices[start + 1]; c = indices[start + 2];
            return (uint)a < vertices.Count && (uint)b < vertices.Count && (uint)c < vertices.Count;
        }
        // Vertex-color shading cannot represent a texture edge inside a triangle. Keep
        // its influence strictly inside painted faces instead of staining the forehead.
        public float PaintedDensity(int vertex) => (uint)vertex < growth.Count &&
            (shadingSafe.Length == 0 || shadingSafe[vertex]) ? growth[vertex] * density[vertex] : 0f;

        internal void PrepareGraph(List<Vector3> positions, List<Vector3> ns, List<float> values, int[] indices, List<int> borders)
        {
            vertices.AddRange(positions); normals.AddRange(ns); growth.AddRange(values); triangles.Add(indices);
            BuildDistances(false, borders);
        }
        internal float GraphDistance(int i) => distances[i];
        internal Vector3 GraphInward(int i) => inward[i];
        internal int GraphRegion(int i) => regions[i];

        private void BuildDistances(bool seedMeshBorders = true, List<int> borders = null)
        {
            int count = vertices.Count;
            distances = new float[count]; inward = new Vector3[count]; regions = new int[count];
            var adjacency = new List<int>[count]; var canonical = new int[count];
            var weld = new Dictionary<Vector3, int>(); var edges = new Dictionary<(int, int), int>();
            var maxGrowth = new float[count];
            for (int i = 0; i < count; i++)
            {
                adjacency[i] = new List<int>(); distances[i] = 1000f; regions[i] = -1;
                if (!weld.TryGetValue(vertices[i], out int owner)) { owner = i; weld.Add(vertices[i], i); }
                canonical[i] = owner; maxGrowth[owner] = Mathf.Max(maxGrowth[owner], growth[i]);
                if (owner != i) { adjacency[i].Add(owner); adjacency[owner].Add(i); }
            }
            void Edge(int a, int b)
            {
                if ((uint)a >= count || (uint)b >= count) return;
                a = canonical[a]; b = canonical[b]; if (a == b) return;
                var key = a < b ? (a, b) : (b, a);
                if (edges.TryGetValue(key, out int uses)) edges[key] = uses + 1;
                else { edges.Add(key, 1); adjacency[a].Add(b); adjacency[b].Add(a); }
            }
            foreach (var indices in triangles)
                for (int i = 0; i + 2 < indices.Length; i += 3)
                { Edge(indices[i], indices[i + 1]); Edge(indices[i + 1], indices[i + 2]); Edge(indices[i + 2], indices[i]); }
            var queue = new DistanceQueue();
            void Seed(int v) { if (distances[v] == 0f) return; distances[v] = 0f; queue.Push(0f, v); }
            // Empty body/face interiors already have distance zero. Only their frontier
            // can propagate into hair; queuing every empty texel made high detail costly.
            for (int i = 0; i < count; i++) if (maxGrowth[canonical[i]] <= .001f) distances[i] = 0f;
            for (int i = 0; i < count; i++)
                if (canonical[i] == i && distances[i] == 0f)
                    foreach (int other in adjacency[i]) if (maxGrowth[canonical[other]] > .001f) { queue.Push(0f, i); break; }
            if (seedMeshBorders)
                foreach (var edge in edges) if (edge.Value == 1) { Seed(edge.Key.Item1); Seed(edge.Key.Item2); }
            if (borders != null) foreach (int vertex in borders) Seed(vertex);
            while (queue.Count > 0)
            {
                var item = queue.Pop();
                if (item.distance != distances[item.vertex]) continue;
                foreach (int other in adjacency[item.vertex])
                {
                    float candidate = item.distance + Vector3.Distance(vertices[item.vertex], vertices[other]);
                    if (candidate >= distances[other]) continue;
                    distances[other] = candidate; queue.Push(candidate, other);
                }
            }
            int region = 0; var flood = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (regions[i] >= 0 || maxGrowth[canonical[i]] <= 0.001f) continue;
                regions[i] = region; flood.Enqueue(i);
                while (flood.Count > 0)
                {
                    int v = flood.Dequeue();
                    foreach (int other in adjacency[v])
                        if (regions[other] < 0 && maxGrowth[canonical[other]] > 0.001f)
                        { regions[other] = region; flood.Enqueue(other); }
                }
                region++;
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 gradient = Vector3.zero;
                foreach (int other in adjacency[i])
                {
                    Vector3 edge = vertices[other] - vertices[i];
                    if (edge.sqrMagnitude > 1e-12f) gradient += edge * ((distances[other] - distances[i]) / edge.sqrMagnitude);
                }
                inward[i] = (normals.Count == count ? Vector3.ProjectOnPlane(gradient, normals[i]) : gradient).normalized;
            }
            // Copy the gradient across zero-length seam links.
            for (int i = 0; i < count; i++) if (canonical[i] != i) inward[i] = inward[canonical[i]];
        }

        // An allocation-free-per-entry binary heap. Stale entries are discarded on pop;
        // ties retain the former distance/vertex ordering for deterministic fields.
        private sealed class DistanceQueue
        {
            private readonly List<(float distance, int vertex)> items = new List<(float, int)>();
            public int Count => items.Count;
            private static bool Before((float distance, int vertex) a, (float distance, int vertex) b) =>
                a.distance < b.distance || (a.distance == b.distance && a.vertex < b.vertex);
            public void Push(float distance, int vertex)
            {
                var value = (distance, vertex); int i = items.Count; items.Add(value);
                while (i > 0)
                { int parent = (i - 1) / 2; if (!Before(value, items[parent])) break; items[i] = items[parent]; i = parent; }
                items[i] = value;
            }
            public (float distance, int vertex) Pop()
            {
                var result = items[0]; int last = items.Count - 1; var value = items[last]; items.RemoveAt(last);
                if (last == 0) return result;
                int i = 0;
                while (i * 2 + 1 < last)
                {
                    int child = i * 2 + 1;
                    if (child + 1 < last && Before(items[child + 1], items[child])) child++;
                    if (!Before(items[child], value)) break;
                    items[i] = items[child]; i = child;
                }
                items[i] = value; return result;
            }
        }
    }
}
