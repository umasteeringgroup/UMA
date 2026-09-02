using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
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
