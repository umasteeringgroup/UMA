using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Cached painted-surface fields. Distances follow mesh edges; coincident UV-seam
    /// vertices are welded for connectivity only. No source mesh or painted map is modified.</summary>
    public sealed class HairSurfaceFields
    {
        private static readonly ProfilerMarker Marker = new ProfilerMarker("HairCards.SurfaceFields");
        private static readonly ProfilerMarker DistanceMarker = new ProfilerMarker("HairCards.SurfaceDistances");
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
                textureField = null; textureStamp = 0; CancelDeferredField();
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
            CancelDeferredField();
            if (g?.UsesTexture != true || g.texture.tiles.Count == 0) textureField = null;
            else if (!DeferTextureField) textureField = new HairTextureField(positions, ns, triangles, g);
            else
            {
                // Until the texel field is ready, Distance/Inward/Region use the per-vertex
                // field built above, which already reflects this paint.
                textureField = null;
                Vector3[] fieldPositions = vertices.ToArray(), fieldNormals = normals.ToArray();
                int[][] fieldTriangles = triangles.ToArray();
                HairGrowthMap fieldMap = SnapshotForField(g);
                queuedField = () => new HairTextureField(fieldPositions, fieldNormals, fieldTriangles, fieldMap);
                queuedFieldVersion = fieldVersion;
                StartQueuedField();
            }
            Revision++;
        }

        /// <summary>When set, a texture-resolution field (millions of graph vertices for a
        /// detailed growth map) is built on a worker thread instead of blocking Prepare.
        /// Interactive previews only: the owner must call <see cref="CompleteDeferredField"/>
        /// from the main thread and re-evaluate when it returns true.</summary>
        internal bool DeferTextureField { get; set; }
        internal bool HasDeferredField => fieldTask != null || queuedField != null;
        private Task<HairTextureField> fieldTask;
        private Func<HairTextureField> queuedField;
        private int fieldVersion, fieldTaskVersion, queuedFieldVersion;

        // Stale results are dropped by version; a running build cannot be interrupted, so at
        // most one runs and only the newest request waits behind it.
        private void CancelDeferredField() { fieldVersion++; queuedField = null; }

        private void StartQueuedField()
        {
            if (queuedField == null || (fieldTask != null && !fieldTask.IsCompleted)) return;
            Func<HairTextureField> build = queuedField;
            fieldTaskVersion = queuedFieldVersion; queuedField = null;
            fieldTask = Task.Run(build);
        }

        /// <summary>Main thread only. Installs a finished deferred field when it still matches
        /// the latest Prepare; returns true when results changed and callers should re-evaluate.</summary>
        internal bool CompleteDeferredField()
        {
            if (fieldTask == null || !fieldTask.IsCompleted) return false;
            Task<HairTextureField> finished = fieldTask; fieldTask = null;
            bool applied = false;
            if (finished.IsFaulted) Debug.LogException(finished.Exception?.GetBaseException());
            else if (finished.Status == TaskStatus.RanToCompletion && fieldTaskVersion == fieldVersion)
            {
                textureField = finished.Result; Revision++; applied = true;
            }
            StartQueuedField();
            return applied;
        }

        // The worker reads only this copy; painting keeps editing the live map meanwhile.
        private static HairGrowthMap SnapshotForField(HairGrowthMap map)
        {
            var texture = new HairTextureMap { resolution = map.texture.resolution, topology = map.texture.topology };
            texture.tiles.Capacity = map.texture.tiles.Count;
            foreach (var tile in map.texture.tiles)
            {
                if (tile == null) continue;
                texture.tiles.Add(new HairTextureMap.Tile
                {
                    submesh = tile.submesh, triangle = tile.triangle, a = tile.a, b = tile.b, c = tile.c,
                    pixels = tile.pixels != null ? (float[])tile.pixels.Clone() : null
                });
            }
            return new HairGrowthMap
            {
                kind = map.kind, defaultValue = map.defaultValue, storage = map.storage, texture = texture,
                values = map.values != null ? (float[])map.values.Clone() : null
            };
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
            using var scope = DistanceMarker.Auto();
            int count = vertices.Count;
            distances = new float[count]; inward = new Vector3[count]; regions = new int[count];
            // Plain arrays: these loops touch millions of texture-graph vertices, and the
            // editor usually runs Debug-compiled code where every List indexer is a call.
            Vector3[] points = vertices.ToArray();
            var canonical = new int[count];
            var maxGrowth = new float[count];
            // Weld exactly coincident positions (Vector3 equality) with an open-addressed
            // table; the first vertex at a position owns it.
            int tableSize = 16;
            while (tableSize < count * 2) tableSize <<= 1;
            var weld = new int[tableSize]; int mask = tableSize - 1;
            for (int i = 0; i < tableSize; i++) weld[i] = -1;
            for (int i = 0; i < count; i++)
            {
                distances[i] = 1000f; regions[i] = -1;
                Vector3 p = points[i];
                int slot = WeldHash(p) & mask, owner;
                while (true)
                {
                    owner = weld[slot];
                    if (owner < 0) { weld[slot] = owner = i; break; }
                    Vector3 q = points[owner];
                    if (SameCoordinate(p.x, q.x) && SameCoordinate(p.y, q.y) && SameCoordinate(p.z, q.z)) break;
                    slot = (slot + 1) & mask;
                }
                canonical[i] = owner;
                float value = growth[i]; if (value > maxGrowth[owner]) maxGrowth[owner] = value;
            }
            weld = null;
            // Empty body/face interiors already have distance zero. Only their frontier
            // can propagate into hair; queuing every empty texel made high detail costly.
            for (int i = 0; i < count; i++) if (maxGrowth[canonical[i]] <= .001f) distances[i] = 0f;

            // Compressed adjacency: count, fill, then sort and de-duplicate each vertex's
            // neighbors. Texture-resolution graphs reach millions of vertices, where a list
            // per vertex plus an edge dictionary cost seconds on every growth paint stroke.
            var start = new int[count + 1];
            for (int i = 0; i < count; i++)
            {
                int owner = canonical[i];
                if (owner != i) { start[i + 1]++; start[owner + 1]++; }
            }
            for (int t = 0; t < triangles.Count; t++)
            {
                int[] indices = triangles[t];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                    bool v0 = (uint)i0 < (uint)count, v1 = (uint)i1 < (uint)count, v2 = (uint)i2 < (uint)count;
                    int c0 = v0 ? canonical[i0] : -1, c1 = v1 ? canonical[i1] : -1, c2 = v2 ? canonical[i2] : -1;
                    if (v0 && v1 && c0 != c1) { start[c0 + 1]++; start[c1 + 1]++; }
                    if (v1 && v2 && c1 != c2) { start[c1 + 1]++; start[c2 + 1]++; }
                    if (v2 && v0 && c2 != c0) { start[c2 + 1]++; start[c0 + 1]++; }
                }
            }
            for (int i = 0; i < count; i++) start[i + 1] += start[i];
            var neighbors = new int[start[count]];
            var fill = new int[count];
            Array.Copy(start, fill, count);
            for (int i = 0; i < count; i++)
            {
                int owner = canonical[i];
                if (owner != i) { neighbors[fill[i]++] = owner; neighbors[fill[owner]++] = i; }
            }
            for (int t = 0; t < triangles.Count; t++)
            {
                int[] indices = triangles[t];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                    bool v0 = (uint)i0 < (uint)count, v1 = (uint)i1 < (uint)count, v2 = (uint)i2 < (uint)count;
                    int c0 = v0 ? canonical[i0] : -1, c1 = v1 ? canonical[i1] : -1, c2 = v2 ? canonical[i2] : -1;
                    if (v0 && v1 && c0 != c1) { neighbors[fill[c0]++] = c1; neighbors[fill[c1]++] = c0; }
                    if (v1 && v2 && c1 != c2) { neighbors[fill[c1]++] = c2; neighbors[fill[c2]++] = c1; }
                    if (v2 && v0 && c2 != c0) { neighbors[fill[c2]++] = c0; neighbors[fill[c0]++] = c2; }
                }
            }
            fill = null;

            var queue = new DistanceQueue(count);
            // Compact in place, keeping each neighbor's first occurrence in insertion order.
            // That order matches the former per-vertex lists, so gradient sums and therefore
            // every field value stay bit-identical. A face edge used by one triangle is a mesh
            // border, which only exists between welded (canonical) vertices.
            int write = 0;
            int[] uses = seedMeshBorders ? new int[16] : null;
            for (int v = 0; v < count; v++)
            {
                int from = start[v], to = start[v + 1], first = write;
                start[v] = write;
                if (uses != null && uses.Length < to - from) uses = new int[Mathf.NextPowerOfTwo(to - from)];
                for (int k = from; k < to; k++)
                {
                    int other = neighbors[k], found = -1;
                    for (int j = first; j < write; j++) if (neighbors[j] == other) { found = j; break; }
                    if (found >= 0) { if (uses != null) uses[found - first]++; continue; }
                    if (uses != null) uses[write - first] = 1;
                    neighbors[write++] = other;
                }
                if (uses == null) continue;
                for (int j = first; j < write; j++)
                {
                    int other = neighbors[j];
                    if (uses[j - first] != 1 || v >= other || canonical[v] != v || canonical[other] != other) continue;
                    if (distances[v] != 0f) { distances[v] = 0f; queue.Push(0f, v); }
                    if (distances[other] != 0f) { distances[other] = 0f; queue.Push(0f, other); }
                }
            }
            start[count] = write;

            for (int i = 0; i < count; i++)
                if (canonical[i] == i && distances[i] == 0f)
                    for (int k = start[i], end = start[i + 1]; k < end; k++)
                        if (maxGrowth[canonical[neighbors[k]]] > .001f) { queue.Push(0f, i); break; }
            if (borders != null)
                for (int b = 0; b < borders.Count; b++)
                {
                    int vertex = borders[b];
                    if (distances[vertex] != 0f) { distances[vertex] = 0f; queue.Push(0f, vertex); }
                }
            while (queue.Count > 0)
            {
                queue.Pop(out float distance, out int vertex);
                if (distance != distances[vertex]) continue;
                Vector3 position = points[vertex];
                for (int k = start[vertex], end = start[vertex + 1]; k < end; k++)
                {
                    int other = neighbors[k];
                    Vector3 target = points[other];
                    // Same arithmetic as Vector3.Distance, inlined.
                    float dx = position.x - target.x, dy = position.y - target.y, dz = position.z - target.z;
                    float candidate = distance + (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (candidate >= distances[other]) continue;
                    distances[other] = candidate; queue.Push(candidate, other);
                }
            }
            int region = 0; var flood = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (regions[i] >= 0 || maxGrowth[canonical[i]] <= 0.001f) continue;
                int head = 0, tail = 0;
                regions[i] = region; flood[tail++] = i;
                while (head < tail)
                {
                    int v = flood[head++];
                    for (int k = start[v], end = start[v + 1]; k < end; k++)
                    {
                        int other = neighbors[k];
                        if (regions[other] < 0 && maxGrowth[canonical[other]] > 0.001f)
                        { regions[other] = region; flood[tail++] = other; }
                    }
                }
                region++;
            }
            bool projectNormals = normals.Count == count;
            // Keep Unity's Vector3 operators here: inlining them changes float rounding in the
            // editor's Mono, and inward directions feed card placement.
            for (int i = 0; i < count; i++)
            {
                Vector3 gradient = Vector3.zero, position = points[i];
                for (int k = start[i], end = start[i + 1]; k < end; k++)
                {
                    Vector3 edge = points[neighbors[k]] - position;
                    if (edge.sqrMagnitude > 1e-12f) gradient += edge * ((distances[neighbors[k]] - distances[i]) / edge.sqrMagnitude);
                }
                inward[i] = (projectNormals ? Vector3.ProjectOnPlane(gradient, normals[i]) : gradient).normalized;
            }
            // Copy the gradient across zero-length seam links.
            for (int i = 0; i < count; i++) if (canonical[i] != i) inward[i] = inward[canonical[i]];
        }

        // float.Equals semantics (0 == -0, NaN equals NaN), matching Vector3 dictionary welds.
        private static bool SameCoordinate(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));
        private static int CoordinateBits(float value) =>
            value == 0f ? 0 : float.IsNaN(value) ? int.MinValue : BitConverter.SingleToInt32Bits(value);
        private static int WeldHash(Vector3 p)
        {
            unchecked
            {
                uint h = (uint)CoordinateBits(p.x) * 0x9E3779B1u;
                h = (h ^ (uint)CoordinateBits(p.y)) * 0x85EBCA77u;
                h = (h ^ (uint)CoordinateBits(p.z)) * 0xC2B2AE3Du;
                return (int)(h ^ (h >> 15));
            }
        }

        // Binary min-heap over parallel arrays. Stale entries are discarded on pop; ties keep
        // the former distance/vertex ordering for deterministic fields.
        private sealed class DistanceQueue
        {
            private float[] keys; private int[] values;
            public int Count { get; private set; }
            public DistanceQueue(int capacity)
            {
                capacity = Math.Max(16, capacity);
                keys = new float[capacity]; values = new int[capacity];
            }
            public void Push(float distance, int vertex)
            {
                if (Count == keys.Length)
                {
                    Array.Resize(ref keys, keys.Length * 2); Array.Resize(ref values, values.Length * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    float pk = keys[parent];
                    if (!(distance < pk || (distance == pk && vertex < values[parent]))) break;
                    keys[i] = pk; values[i] = values[parent]; i = parent;
                }
                keys[i] = distance; values[i] = vertex;
            }
            public void Pop(out float distance, out int vertex)
            {
                distance = keys[0]; vertex = values[0];
                int last = --Count;
                if (last == 0) return;
                float key = keys[last]; int value = values[last];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= last) break;
                    int right = child + 1;
                    if (right < last && (keys[right] < keys[child] || (keys[right] == keys[child] && values[right] < values[child]))) child = right;
                    float ck = keys[child];
                    if (!(ck < key || (ck == key && values[child] < value))) break;
                    keys[i] = ck; values[i] = values[child]; i = child;
                }
                keys[i] = key; values[i] = value;
            }
        }
    }
}
