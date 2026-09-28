using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    public readonly struct HairNearestPoint
    {
        public readonly int index;
        public readonly float distanceSquared;
        public HairNearestPoint(int index, float distanceSquared)
        { this.index = index; this.distanceSquared = distanceSquared; }
    }

    /// <summary>
    /// Rebuildable, exact point k-d tree. Queries allocate nothing and return distance/index order,
    /// matching an ascending-index brute-force search even for coincident roots or mesh seams.
    /// Rebuild snapshots the input; callers own invalidation when positions change.
    /// </summary>
    public sealed class HairPointSpatialIndex
    {
        private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("HairCards.UpdatePointIndex");
        private struct Node
        {
            internal int point, left, right, minimumIndex;
            internal Vector3 minimum, maximum;
        }
        private sealed class AxisComparer : IComparer<int>
        {
            internal Vector3[] points;
            internal int axis;
            public int Compare(int a, int b)
            {
                int order = points[a][axis].CompareTo(points[b][axis]);
                return order != 0 ? order : a.CompareTo(b);
            }
        }
        private Vector3[] points = Array.Empty<Vector3>();
        private int[] order = Array.Empty<int>();
        private Node[] nodes = Array.Empty<Node>();
        private readonly AxisComparer comparer = new AxisComparer();
        private int root = -1, nodeCount, sourceCount = -1;
        public int Count => nodeCount;
        public int LastQueryVisitedNodes { get; private set; }

        public void Rebuild(IReadOnlyList<Vector3> source)
        {
            using var rebuildScope = RebuildMarker.Auto();
            int count = source?.Count ?? 0;
            // Exact comparisons also detect same-Mesh in-place edits; never rely on vertex count,
            // object identity, approximate Vector3 equality, or an asset dirty flag for validity.
            bool unchanged = count == sourceCount;
            for (int i = 0; unchanged && i < count; i++)
            { Vector3 value = source[i]; unchanged = points[i].x == value.x && points[i].y == value.y && points[i].z == value.z; }
            if (unchanged) return;
            sourceCount = count;
            if (points.Length < count)
            {
                int capacity = Mathf.NextPowerOfTwo(Mathf.Max(16, count));
                Array.Resize(ref points, capacity); Array.Resize(ref order, capacity); Array.Resize(ref nodes, capacity);
            }
            int valid = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = source[i]; points[i] = point;
                if (Finite(point)) order[valid++] = i;
            }
            comparer.points = points;
            nodeCount = 0;
            root = valid > 0 ? Build(0, valid) : -1;
        }

        public void Clear()
        {
            points = Array.Empty<Vector3>(); order = Array.Empty<int>(); nodes = Array.Empty<Node>();
            comparer.points = null; root = -1; nodeCount = 0; sourceCount = -1;
        }

        public bool TryFindNearest(Vector3 point, out int index, out float squareDistance)
        {
            Span<HairNearestPoint> nearest = stackalloc HairNearestPoint[1];
            int count = QueryNearest(point, nearest);
            index = count > 0 ? nearest[0].index : -1;
            squareDistance = count > 0 ? nearest[0].distanceSquared : float.PositiveInfinity;
            return count > 0;
        }

        public int QueryNearest(Vector3 point, Span<HairNearestPoint> results)
        {
            LastQueryVisitedNodes = 0;
            int count = 0;
            if (root >= 0 && results.Length > 0 && Finite(point)) Query(root, point, results, ref count);
            return count;
        }

        private int Build(int start, int count)
        {
            Vector3 minimum = points[order[start]], maximum = minimum;
            int minimumIndex = order[start];
            for (int i = start + 1; i < start + count; i++)
            {
                Vector3 point = points[order[i]];
                minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
                minimumIndex = Mathf.Min(minimumIndex, order[i]);
            }
            Vector3 extent = maximum - minimum;
            comparer.axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
            int median = start + count / 2;
            SelectMedian(start, start + count - 1, median);
            int index = nodeCount++;
            Node node = new Node { point = order[median], minimum = minimum, maximum = maximum, minimumIndex = minimumIndex, left = -1, right = -1 };
            if (median > start) node.left = Build(start, median - start);
            if (median + 1 < start + count) node.right = Build(median + 1, start + count - median - 1);
            nodes[index] = node;
            return index;
        }

        private void SelectMedian(int left, int right, int target)
        {
            int iterations = 0;
            while (left < right)
            {
                // Bound adversarial quickselect behavior without allocating another comparer.
                if (++iterations > 32) { Array.Sort(order, left, right - left + 1, comparer); return; }
                int middle = left + (right - left) / 2;
                if (comparer.Compare(order[left], order[middle]) > 0) Swap(left, middle);
                if (comparer.Compare(order[left], order[right]) > 0) Swap(left, right);
                if (comparer.Compare(order[middle], order[right]) > 0) Swap(middle, right);
                Swap(middle, right);
                int pivot = order[right], split = left;
                for (int i = left; i < right; i++)
                    if (comparer.Compare(order[i], pivot) < 0) Swap(i, split++);
                Swap(split, right);
                if (split == target) return;
                if (target < split) right = split - 1; else left = split + 1;
            }
        }

        private void Swap(int a, int b) { int value = order[a]; order[a] = order[b]; order[b] = value; }
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static float BoundDistance(Vector3 point, Node node) =>
            (point - Vector3.Max(node.minimum, Vector3.Min(node.maximum, point))).sqrMagnitude;

        private void Query(int index, Vector3 point, Span<HairNearestPoint> results, ref int count)
        {
            Node node = nodes[index];
            if (count == results.Length)
            {
                HairNearestPoint worst = results[count - 1];
                float bound = BoundDistance(point, node);
                if (bound > worst.distanceSquared || (bound == worst.distanceSquared && node.minimumIndex >= worst.index)) return;
            }
            LastQueryVisitedNodes++;
            float square = (point - points[node.point]).sqrMagnitude;
            int insertion = count;
            for (int i = 0; i < count; i++)
                if (square < results[i].distanceSquared || (square == results[i].distanceSquared && node.point < results[i].index))
                { insertion = i; break; }
            if (insertion < results.Length)
            {
                count = Mathf.Min(count + 1, results.Length);
                for (int i = count - 1; i > insertion; i--) results[i] = results[i - 1];
                results[insertion] = new HairNearestPoint(node.point, square);
            }
            int first = node.left, second = node.right;
            if (first >= 0 && second >= 0)
            {
                float a = BoundDistance(point, nodes[first]), b = BoundDistance(point, nodes[second]);
                if (b < a || (b == a && nodes[second].minimumIndex < nodes[first].minimumIndex))
                { int swap = first; first = second; second = swap; }
            }
            if (first >= 0) Query(first, point, results, ref count);
            if (second >= 0) Query(second, point, results, ref count);
        }
    }

    /// <summary>Reusable uniform-grid acceleration for interactive surface brushes.</summary>
    public sealed class HairVertexSpatialIndex
    {
        private readonly struct Cell : IEquatable<Cell>
        {
            public readonly int x;
            public readonly int y;
            public readonly int z;

            public Cell(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public bool Equals(Cell other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object obj) => obj is Cell other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((x * 397) ^ y) * 397 ^ z; }
            }
        }

        private readonly Vector3[] vertices;
        private readonly float cellSize;
        private readonly Dictionary<Cell, List<int>> cells = new Dictionary<Cell, List<int>>();

        public HairVertexSpatialIndex(Mesh mesh, float preferredCellSize = 0f)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            vertices = mesh.vertices;
            cellSize = preferredCellSize > 1e-6f
                ? preferredCellSize
                : Mathf.Max(1e-5f, mesh.bounds.size.magnitude / 64f);
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                Cell cell = ToCell(vertices[vertex]);
                if (!cells.TryGetValue(cell, out List<int> bucket))
                {
                    bucket = new List<int>();
                    cells.Add(cell, bucket);
                }
                bucket.Add(vertex);
            }
        }

        public void QuerySphere(Vector3 center, float radius, List<int> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            float safeRadius = Mathf.Max(0f, radius);
            Cell minimum = ToCell(center - Vector3.one * safeRadius);
            Cell maximum = ToCell(center + Vector3.one * safeRadius);
            float squareRadius = safeRadius * safeRadius;
            for (int z = minimum.z; z <= maximum.z; z++)
            for (int y = minimum.y; y <= maximum.y; y++)
            for (int x = minimum.x; x <= maximum.x; x++)
            {
                if (!cells.TryGetValue(new Cell(x, y, z), out List<int> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    int vertex = bucket[i];
                    if ((vertices[vertex] - center).sqrMagnitude <= squareRadius) results.Add(vertex);
                }
            }
        }

        private Cell ToCell(Vector3 point)
        {
            return new Cell(Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.y / cellSize),
                Mathf.FloorToInt(point.z / cellSize));
        }
    }
}
