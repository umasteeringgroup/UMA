using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>
    /// A transient geometry restriction used while projecting or filling. It is deliberately not
    /// serialized and is unrelated to the editable grayscale mask owned by a layer.
    /// </summary>
    internal sealed class TexturePaintGeometrySelector
    {
        public string name = "Geometry Selection";
        public bool enabled = true;
        public TexturePaintGeometrySelectorKind kind;
        public int surfaceIndex = -1;
        public readonly List<int> triangleIndices = new List<int>();
        public readonly List<int> uvIslandIndices = new List<int>();

        public bool Allows(int candidateSurface, int triangleIndex, int uvIsland)
        {
            if (!enabled || kind == TexturePaintGeometrySelectorKind.None) return true;
            switch (kind)
            {
                case TexturePaintGeometrySelectorKind.Slot:
                    return surfaceIndex < 0 || surfaceIndex == candidateSurface;
                case TexturePaintGeometrySelectorKind.Polygon:
                    return triangleIndices.Contains(triangleIndex);
                case TexturePaintGeometrySelectorKind.UVIsland:
                    return uvIslandIndices.Contains(uvIsland);
                default:
                    return true;
            }
        }
    }

    internal sealed class TexturePaintGeometrySelection
    {
        private readonly List<TexturePaintGeometrySelector> selectors =
            new List<TexturePaintGeometrySelector>();
        public IReadOnlyList<TexturePaintGeometrySelector> Selectors => selectors;
        public TexturePaintGeometrySelection() { }
        public TexturePaintGeometrySelection(List<TexturePaintGeometrySelector> backing)
            => selectors = backing ?? new List<TexturePaintGeometrySelector>();

        public void Add(TexturePaintGeometrySelector selector)
        {
            if (selector != null) selectors.Add(selector);
        }

        public bool AllowsStructural(int surface, int triangle, int uvIsland, ReconstructedSurface reconstructed = null,
            Vector2 uv = default, Vector3 worldPosition = default)
        {
            for (int i = 0; i < selectors.Count; i++)
            {
                TexturePaintGeometrySelector selector = selectors[i];
                if (selector != null && !selector.Allows(surface, triangle, uvIsland)) return false;
            }
            return true;
        }
    }

    public static class UVIslandUtility
    {
        public static int[] BuildTriangleIslands(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<int>();
            int[] triangles = mesh.triangles;
            Vector2[] uv = mesh.uv;
            int triCount = triangles.Length / 3;
            int[] islands = new int[triCount];
            for (int i = 0; i < islands.Length; i++) islands[i] = -1;
            Dictionary<UVEdge, List<int>> edgeOwners = new Dictionary<UVEdge, List<int>>();
            for (int tri = 0; tri < triCount; tri++)
            {
                int a = triangles[tri * 3], b = triangles[tri * 3 + 1], c = triangles[tri * 3 + 2];
                AddEdge(edgeOwners, new UVEdge(uv[a], uv[b]), tri);
                AddEdge(edgeOwners, new UVEdge(uv[b], uv[c]), tri);
                AddEdge(edgeOwners, new UVEdge(uv[c], uv[a]), tri);
            }
            List<int>[] adjacency = new List<int>[triCount];
            foreach (List<int> owners in edgeOwners.Values)
            {
                if (owners.Count < 2) continue;
                for (int i = 0; i < owners.Count; i++)
                for (int j = i + 1; j < owners.Count; j++)
                {
                    (adjacency[owners[i]] ??= new List<int>()).Add(owners[j]);
                    (adjacency[owners[j]] ??= new List<int>()).Add(owners[i]);
                }
            }
            Queue<int> queue = new Queue<int>();
            int island = 0;
            for (int start = 0; start < triCount; start++)
            {
                if (islands[start] >= 0) continue;
                islands[start] = island;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    List<int> neighbors = adjacency[current];
                    if (neighbors == null) continue;
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        int next = neighbors[i];
                        if (islands[next] >= 0) continue;
                        islands[next] = island;
                        queue.Enqueue(next);
                    }
                }
                island++;
            }
            return islands;
        }

        private static void AddEdge(Dictionary<UVEdge, List<int>> edges, UVEdge edge, int triangle)
        {
            if (!edges.TryGetValue(edge, out List<int> owners)) edges.Add(edge, owners = new List<int>());
            owners.Add(triangle);
        }

        private readonly struct UVEdge : IEquatable<UVEdge>
        {
            private readonly Vector2Int a;
            private readonly Vector2Int b;
            public UVEdge(Vector2 one, Vector2 two)
            {
                Vector2Int q1 = Quantize(one), q2 = Quantize(two);
                if (q1.x < q2.x || (q1.x == q2.x && q1.y <= q2.y)) { a = q1; b = q2; }
                else { a = q2; b = q1; }
            }
            private static Vector2Int Quantize(Vector2 value) => new Vector2Int(Mathf.RoundToInt(value.x * 100000f), Mathf.RoundToInt(value.y * 100000f));
            public bool Equals(UVEdge other) => a == other.a && b == other.b;
            public override bool Equals(object obj) => obj is UVEdge other && Equals(other);
            public override int GetHashCode() { unchecked { return (a.GetHashCode() * 397) ^ b.GetHashCode(); } }
        }
    }
}
