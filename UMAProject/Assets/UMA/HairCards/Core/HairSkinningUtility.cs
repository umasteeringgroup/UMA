using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>Transfers source scalp bone weights to generated card vertices by closest source vertex.</summary>
    public static class HairSkinningUtility
    {
        private readonly struct Cell : System.IEquatable<Cell>
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
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + x;
                    hash = hash * 31 + y;
                    hash = hash * 31 + z;
                    return hash;
                }
            }
        }

        public static bool TransferClosestVertexWeights(Mesh generated, Mesh source, out string warning)
        {
            warning = string.Empty;
            if (generated == null || source == null)
            {
                warning = "A generated mesh and a source mesh are required for skinning.";
                return false;
            }

            BoneWeight[] sourceWeights = source.boneWeights;
            Matrix4x4[] bindPoses = source.bindposes;
            if (sourceWeights == null || sourceWeights.Length != source.vertexCount || bindPoses == null || bindPoses.Length == 0)
            {
                warning = "The source mesh has no compatible legacy four-influence bone weights.";
                return false;
            }

            Vector3[] sourceVertices = source.vertices;
            Vector3[] generatedVertices = generated.vertices;
            float cellSize = Mathf.Max(1e-5f,
                source.bounds.size.magnitude / Mathf.Max(8f, Mathf.Pow(sourceVertices.Length, 1f / 3f)));
            Dictionary<Cell, List<int>> grid = new Dictionary<Cell, List<int>>();
            for (int index = 0; index < sourceVertices.Length; index++)
            {
                Cell cell = ToCell(sourceVertices[index], cellSize);
                if (!grid.TryGetValue(cell, out List<int> indices))
                {
                    indices = new List<int>();
                    grid.Add(cell, indices);
                }
                indices.Add(index);
            }

            BoneWeight[] generatedWeights = new BoneWeight[generatedVertices.Length];
            for (int vertex = 0; vertex < generatedVertices.Length; vertex++)
            {
                int sourceIndex = FindClosest(generatedVertices[vertex], sourceVertices, grid, cellSize);
                generatedWeights[vertex] = sourceWeights[Mathf.Max(0, sourceIndex)];
            }
            generated.boneWeights = generatedWeights;
            generated.bindposes = bindPoses;
            return true;
        }

        private static int FindClosest(Vector3 point, IReadOnlyList<Vector3> vertices,
            Dictionary<Cell, List<int>> grid, float cellSize)
        {
            Cell center = ToCell(point, cellSize);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int radius = 0; radius <= 4 && best < 0; radius++)
            {
                for (int z = -radius; z <= radius; z++)
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    if (radius > 0 && Mathf.Abs(x) < radius && Mathf.Abs(y) < radius && Mathf.Abs(z) < radius)
                        continue;
                    Cell cell = new Cell(center.x + x, center.y + y, center.z + z);
                    if (!grid.TryGetValue(cell, out List<int> candidates)) continue;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        int candidate = candidates[i];
                        float distance = (vertices[candidate] - point).sqrMagnitude;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }
            if (best >= 0) return best;
            for (int i = 0; i < vertices.Count; i++)
            {
                float distance = (vertices[i] - point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        private static Cell ToCell(Vector3 point, float cellSize)
        {
            return new Cell(Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.y / cellSize),
                Mathf.FloorToInt(point.z / cellSize));
        }
    }
}
