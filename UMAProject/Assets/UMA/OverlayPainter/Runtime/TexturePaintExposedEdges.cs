using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Distance to physical rims and convex creases, independent of UV seams and
    /// split shading normals. Immutable after construction; safe for parallel mesh-map baking.</summary>
    public sealed class TexturePaintExposedEdges
    {
        private struct FaceEdge { public int a, b, count; public Vector3 normal, opposite; public bool convex; public float normalDot; }
        private struct Segment { public Vector3 a, b, center; public Bounds bounds; public int component; }
        private struct Node { public Bounds bounds; public int start, count, left, right, component; }
        private readonly Segment[] segments;
        private readonly List<Node> nodes = new();
        private readonly int[] triangleComponents;
        public int EdgeCount => segments.Length;

        public TexturePaintExposedEdges(Vector3[] vertices, int[] triangles, Matrix4x4 transform)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            float tolerance = Mathf.Max(1e-8f, bounds.size.magnitude * 1e-6f);
            var welded = new Dictionary<Vector3Int, int>();
            var positions = new List<Vector3>();
            int[] indices = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = (vertices[i] - bounds.min) / tolerance;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!welded.TryGetValue(key, out int index))
                { index = positions.Count; welded.Add(key, index); positions.Add(vertices[i]); }
                indices[i] = index;
            }
            int[] parents = new int[positions.Count];
            for (int i = 0; i < parents.Length; i++) parents[i] = i;
            int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
            void Join(int a, int b) { parents[Root(a)] = Root(b); }

            var edges = new Dictionary<(int, int), FaceEdge>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = indices[triangles[i]], b = indices[triangles[i + 1]], c = indices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (normal.sqrMagnitude < tolerance * tolerance * tolerance * tolerance) continue;
                // Vector3.Normalize zeros vectors shorter than 1e-5. Triangle cross
                // products represent area: thin, perfectly valid bevel faces routinely
                // fall below that threshold. We already rejected degenerate faces above.
                normal /= Mathf.Sqrt(normal.sqrMagnitude); Join(a, b); Join(b, c);
                Add(a, b, c, normal); Add(b, c, a, normal); Add(c, a, b, normal);
            }
            triangleComponents = new int[triangles.Length / 3];
            for (int i = 0; i < triangleComponents.Length; i++) triangleComponents[i] = Root(indices[triangles[i * 3]]);
            var result = new List<Segment>();
            var candidates = new List<FaceEdge>(edges.Values);
            bool[] selected = FindContinuousCreases(candidates, positions);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!selected[i]) continue;
                FaceEdge edge = candidates[i];
                Vector3 a = transform.MultiplyPoint3x4(positions[edge.a]);
                Vector3 b = transform.MultiplyPoint3x4(positions[edge.b]);
                var box = new Bounds(a, Vector3.zero); box.Encapsulate(b);
                result.Add(new Segment { a = a, b = b, center = (a + b) * .5f, bounds = box, component = Root(edge.a) });
            }
            segments = result.ToArray();
            if (segments.Length > 0) Build(0, segments.Length);

            void Add(int a, int b, int opposite, Vector3 normal)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out FaceEdge edge))
                    edge = new FaceEdge { a = a, b = b, normal = normal, opposite = positions[opposite] };
                else
                {
                    // Flat triangulation diagonals and smooth tessellation are not creases.
                    // Outward-facing convex faces place the other face behind their plane.
                    edge.normalDot = Vector3.Dot(edge.normal, normal);
                    edge.convex =
                        Vector3.Dot(edge.normal, positions[opposite] - positions[a]) < -tolerance &&
                        Vector3.Dot(normal, edge.opposite - positions[a]) < -tolerance;
                }
                edge.count++; edges[key] = edge;
            }
        }

        private static bool[] FindContinuousCreases(List<FaceEdge> edges, List<Vector3> positions)
        {
            var adjacent = new List<int>[positions.Count];
            var seeds = new bool[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                FaceEdge edge = edges[i];
                // Non-manifold intersections and concave folds cannot bridge an exposed rim.
                seeds[i] = edge.count == 1 || (edge.count == 2 && edge.convex && edge.normalDot < .8660254f);
                bool shallowConvex = edge.count == 2 && edge.convex && edge.normalDot < .9961947f; // 5 degrees
                if (!seeds[i] && !shallowConvex) continue;
                (adjacent[edge.a] ??= new List<int>()).Add(i);
                (adjacent[edge.b] ??= new List<int>()).Add(i);
            }
            var selected = (bool[])seeds.Clone();
            var path = new List<int>();
            var visited = new HashSet<int>();
            for (int seed = 0; seed < edges.Count; seed++)
            {
                if (!seeds[seed]) continue;
                Trace(seed, edges[seed].a);
                Trace(seed, edges[seed].b);
            }
            return selected;

            void Trace(int seed, int end)
            {
                int current = seed;
                path.Clear(); visited.Clear(); visited.Add(seed);
                while (true)
                {
                    FaceEdge edge = edges[current];
                    int other = edge.a == end ? edge.b : edge.a;
                    Vector3 incoming = UnitDirection(positions[end] - positions[other]);
                    int next = -1;
                    float best = .8f;
                    foreach (int candidate in adjacent[end])
                    {
                        if (candidate == current) continue;
                        FaceEdge continuation = edges[candidate];
                        int destination = continuation.a == end ? continuation.b : continuation.a;
                        float alignment = Vector3.Dot(incoming, UnitDirection(positions[destination] - positions[end]));
                        if (alignment > best) { best = alignment; next = candidate; }
                    }
                    if (next < 0 || !visited.Add(next)) return;
                    if (seeds[next])
                    {
                        // Require a detected edge at BOTH ends. Never grow unbounded wear
                        // along smooth tessellation just because it touches a sharp corner.
                        foreach (int bridge in path) selected[bridge] = true;
                        return;
                    }
                    path.Add(next);
                    FaceEdge nextEdge = edges[next];
                    end = nextEdge.a == end ? nextEdge.b : nextEdge.a;
                    current = next;
                }
            }

            Vector3 UnitDirection(Vector3 delta) => delta / Mathf.Max(1e-20f, Mathf.Sqrt(delta.sqrMagnitude));
        }

        public float Distance(Vector3 position, int triangle)
        {
            float distanceSquared = float.PositiveInfinity;
            if (nodes.Count > 0 && triangle >= 0 && triangle < triangleComponents.Length)
                Visit(0, position, triangleComponents[triangle], ref distanceSquared);
            return Mathf.Sqrt(distanceSquared);
        }

        private int Build(int start, int count)
        {
            Bounds bounds = segments[start].bounds;
            int component = segments[start].component;
            for (int i = start + 1; i < start + count; i++)
            { bounds.Encapsulate(segments[i].bounds); if (segments[i].component != component) component = -1; }
            int index = nodes.Count; nodes.Add(default);
            var node = new Node { bounds = bounds, start = start, count = count, component = component };
            if (count > 8)
            {
                Vector3 size = bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(segments, start, count, Comparer<Segment>.Create((a, b) => a.center[axis].CompareTo(b.center[axis])));
                int half = count / 2;
                node.left = Build(start, half); node.right = Build(start + half, count - half); node.count = 0;
            }
            nodes[index] = node; return index;
        }

        private void Visit(int index, Vector3 position, int component, ref float closest)
        {
            Node node = nodes[index];
            if ((node.component >= 0 && node.component != component) || node.bounds.SqrDistance(position) >= closest) return;
            if (node.count == 0)
            {
                int first = node.left, second = node.right;
                if (nodes[first].bounds.SqrDistance(position) > nodes[second].bounds.SqrDistance(position)) (first, second) = (second, first);
                Visit(first, position, component, ref closest); Visit(second, position, component, ref closest); return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                Segment edge = segments[i]; if (edge.component != component) continue;
                Vector3 delta = edge.b - edge.a;
                float t = Mathf.Clamp01(Vector3.Dot(position - edge.a, delta) / Mathf.Max(1e-20f, delta.sqrMagnitude));
                closest = Mathf.Min(closest, (position - edge.a - t * delta).sqrMagnitude);
            }
        }
    }
}
