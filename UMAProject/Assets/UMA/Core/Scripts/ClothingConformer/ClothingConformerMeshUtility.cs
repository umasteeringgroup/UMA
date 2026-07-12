using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>Math and topology helpers shared by the conformer runtime and editor tooling.</summary>
    public static class ClothingConformerMeshUtility
    {
        public static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            // Real-Time Collision Detection, Christer Ericson, section 5.1.5.
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                Vector3 bc = c - b;
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * bc;
            }

            float denominator = 1f / (va + vb + vc);
            float baryV = vb * denominator;
            float baryW = vc * denominator;
            return a + ab * baryV + ac * baryW;
        }

        public static Vector3 CalculateBarycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = point - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 0.00000001f) return new Vector3(1f, 0f, 0f);

            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            return new Vector3(1f - v - w, v, w);
        }

        /// <summary>
        /// Returns the side of a mapped surface on which the clothing originally sat.
        /// This remains correct even if an imported body slot has inward-wound triangles.
        /// </summary>
        public static float GetMappedClothingSide(float signedDistance, Vector3 clothingNormal, Vector3 mappedSurfaceNormal)
        {
            if (Mathf.Abs(signedDistance) > 0.000001f) return Mathf.Sign(signedDistance);
            if (clothingNormal.sqrMagnitude > 0.00000001f && mappedSurfaceNormal.sqrMagnitude > 0.00000001f)
                return Vector3.Dot(clothingNormal, mappedSurfaceNormal) >= 0f ? 1f : -1f;
            return 1f;
        }

        /// <summary>Keeps a recalculated normal on the same signed side as its bind-time normal.</summary>
        public static Vector3 OrientNormalToReference(Vector3 normal, Vector3 referenceNormal)
        {
            if (normal.sqrMagnitude < 0.00000001f) return Vector3.up;
            normal.Normalize();
            if (referenceNormal.sqrMagnitude > 0.00000001f && Vector3.Dot(normal, referenceNormal) < 0f)
                normal = -normal;
            return normal;
        }

        public static Vector3[] CalculateNormals(Vector3[] vertices, int[] triangles)
        {
            Vector3[] normals = new Vector3[vertices.Length];
            if (triangles == null) return normals;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!IsValidVertexIndex(a, vertices.Length) || !IsValidVertexIndex(b, vertices.Length) || !IsValidVertexIndex(c, vertices.Length))
                    continue;

                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += normal;
                normals[b] += normal;
                normals[c] += normal;
            }

            for (int i = 0; i < normals.Length; i++)
                normals[i] = normals[i].sqrMagnitude > 0.00000001f ? normals[i].normalized : Vector3.up;

            return normals;
        }

        public static Vector4[] CalculateTangents(Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] triangles)
        {
            Vector4[] tangents = new Vector4[vertices.Length];
            if (uv == null || uv.Length != vertices.Length || triangles == null)
            {
                for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(1f, 0f, 0f, 1f);
                return tangents;
            }

            Vector3[] tan1 = new Vector3[vertices.Length];
            Vector3[] tan2 = new Vector3[vertices.Length];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i1 = triangles[i];
                int i2 = triangles[i + 1];
                int i3 = triangles[i + 2];
                if (!IsValidVertexIndex(i1, vertices.Length) || !IsValidVertexIndex(i2, vertices.Length) || !IsValidVertexIndex(i3, vertices.Length))
                    continue;

                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];
                Vector3 v3 = vertices[i3];
                Vector2 w1 = uv[i1];
                Vector2 w2 = uv[i2];
                Vector2 w3 = uv[i3];
                float x1 = v2.x - v1.x;
                float x2 = v3.x - v1.x;
                float y1 = v2.y - v1.y;
                float y2 = v3.y - v1.y;
                float z1 = v2.z - v1.z;
                float z2 = v3.z - v1.z;
                float s1 = w2.x - w1.x;
                float s2 = w3.x - w1.x;
                float t1 = w2.y - w1.y;
                float t2 = w3.y - w1.y;
                float divisor = s1 * t2 - s2 * t1;
                if (Mathf.Abs(divisor) < 0.00000001f) continue;
                float r = 1f / divisor;
                Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);
                tan1[i1] += sdir; tan1[i2] += sdir; tan1[i3] += sdir;
                tan2[i1] += tdir; tan2[i2] += tdir; tan2[i3] += tdir;
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals != null && i < normals.Length ? normals[i] : Vector3.up;
                Vector3 tangent = tan1[i];
                tangent = (tangent - normal * Vector3.Dot(normal, tangent)).normalized;
                if (tangent.sqrMagnitude < 0.00000001f) tangent = Vector3.right;
                float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), tan2[i]) < 0f ? -1f : 1f;
                tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, handedness);
            }
            return tangents;
        }

        public static List<int>[] BuildAdjacency(int vertexCount, int[] triangles)
        {
            List<int>[] adjacency = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) adjacency[i] = new List<int>();
            if (triangles == null) return adjacency;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdge(adjacency, triangles[i], triangles[i + 1]);
                AddEdge(adjacency, triangles[i + 1], triangles[i + 2]);
                AddEdge(adjacency, triangles[i + 2], triangles[i]);
            }
            return adjacency;
        }

        /// <summary>
        /// Finds near-coincident vertices that are not already connected by a triangle edge.
        /// Those are normally UV or hard-normal split copies of the same geometric vertex.
        /// </summary>
        public static int[] BuildWeldedVertexGroups(Vector3[] vertices, int[] triangles, float tolerance)
        {
            if (vertices == null || vertices.Length == 0) return Array.Empty<int>();
            int[] parents = new int[vertices.Length];
            int[] ranks = new int[vertices.Length];
            for (int i = 0; i < parents.Length; i++) parents[i] = i;

            HashSet<long> connectedEdges = new HashSet<long>();
            if (triangles != null)
            {
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    AddEdgeKey(connectedEdges, triangles[i], triangles[i + 1]);
                    AddEdgeKey(connectedEdges, triangles[i + 1], triangles[i + 2]);
                    AddEdgeKey(connectedEdges, triangles[i + 2], triangles[i]);
                }
            }

            float cellSize = Mathf.Max(0.000001f, tolerance);
            float toleranceSq = cellSize * cellSize;
            Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3Int cell = ToWeldCell(vertices[i], cellSize);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    List<int> candidates;
                    if (!cells.TryGetValue(cell + new Vector3Int(x, y, z), out candidates)) continue;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        int candidate = candidates[candidateIndex];
                        if (connectedEdges.Contains(EdgeKey(i, candidate))) continue;
                        if ((vertices[i] - vertices[candidate]).sqrMagnitude <= toleranceSq)
                            Union(parents, ranks, i, candidate);
                    }
                }

                List<int> values;
                if (!cells.TryGetValue(cell, out values))
                {
                    values = new List<int>();
                    cells.Add(cell, values);
                }
                values.Add(i);
            }

            Dictionary<int, int> groupSizes = new Dictionary<int, int>();
            for (int i = 0; i < parents.Length; i++)
            {
                int root = Find(parents, i);
                int size;
                groupSizes.TryGetValue(root, out size);
                groupSizes[root] = size + 1;
            }
            Dictionary<int, int> groupIds = new Dictionary<int, int>();
            int nextGroup = 0;
            int[] groups = new int[vertices.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                int root = Find(parents, i);
                int size = groupSizes[root];
                if (size < 2)
                {
                    groups[i] = -1;
                    continue;
                }
                int group;
                if (!groupIds.TryGetValue(root, out group))
                {
                    group = nextGroup++;
                    groupIds.Add(root, group);
                }
                groups[i] = group;
            }
            return groups;
        }

        public static void Smooth(Vector3[] vertices, int[] triangles, ClothingConformerSettings settings, bool[] affected)
        {
            if (vertices == null || vertices.Length == 0 || settings == null || settings.smoothingIterations < 1) return;
            Smooth(vertices, BuildAdjacency(vertices.Length, triangles), settings, affected);
        }

        public static void Smooth(Vector3[] vertices, List<int>[] adjacency, ClothingConformerSettings settings, bool[] affected)
        {
            if (vertices == null || vertices.Length == 0 || adjacency == null || adjacency.Length != vertices.Length ||
                settings == null || settings.smoothingIterations < 1)
                return;
            Vector3[] original = (Vector3[])vertices.Clone();
            Vector3[] scratch = new Vector3[vertices.Length];
            Vector3[] correction = new Vector3[vertices.Length];

            for (int iteration = 0; iteration < settings.smoothingIterations; iteration++)
            {
                if (settings.smoothingAlgorithm == SmoothingAlgorithm.Taubin)
                {
                    LaplacianPass(vertices, scratch, adjacency, settings.smoothingStrength, affected);
                    Array.Copy(scratch, vertices, vertices.Length);
                    LaplacianPass(vertices, scratch, adjacency, -0.53f * settings.smoothingStrength, affected);
                    Array.Copy(scratch, vertices, vertices.Length);
                    continue;
                }

                LaplacianPass(vertices, scratch, adjacency, settings.smoothingStrength, affected);
                if (settings.smoothingAlgorithm == SmoothingAlgorithm.Laplacian)
                {
                    Array.Copy(scratch, vertices, vertices.Length);
                    continue;
                }

                // HC smoothing: keep a smoothed estimate of the displacement from the source shape.
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!ShouldMove(i, affected))
                    {
                        correction[i] = Vector3.zero;
                        continue;
                    }
                    correction[i] = scratch[i] - (settings.hcAlpha * original[i] + (1f - settings.hcAlpha) * vertices[i]);
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!ShouldMove(i, affected))
                    {
                        scratch[i] = vertices[i];
                        continue;
                    }
                    Vector3 neighborCorrection = Vector3.zero;
                    List<int> neighbors = adjacency[i];
                    for (int n = 0; n < neighbors.Count; n++) neighborCorrection += correction[neighbors[n]];
                    if (neighbors.Count > 0) neighborCorrection /= neighbors.Count;
                    scratch[i] -= settings.hcBeta * correction[i] + (1f - settings.hcBeta) * neighborCorrection;
                }
                Array.Copy(scratch, vertices, vertices.Length);
            }
        }

        public static int ComputeTopologyHash(string[] slotNames, int vertexCount, int[] triangles)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + vertexCount;
                if (slotNames != null)
                {
                    for (int i = 0; i < slotNames.Length; i++)
                        hash = hash * 31 + StableStringHash(slotNames[i]);
                }
                if (triangles != null)
                {
                    for (int i = 0; i < triangles.Length; i++) hash = hash * 31 + triangles[i];
                }
                return hash;
            }
        }

        private static void LaplacianPass(Vector3[] source, Vector3[] destination, List<int>[] adjacency, float strength, bool[] affected)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (!ShouldMove(i, affected) || adjacency[i].Count == 0)
                {
                    destination[i] = source[i];
                    continue;
                }
                Vector3 average = Vector3.zero;
                List<int> neighbors = adjacency[i];
                for (int n = 0; n < neighbors.Count; n++) average += source[neighbors[n]];
                average /= neighbors.Count;
                destination[i] = Vector3.LerpUnclamped(source[i], average, strength);
            }
        }

        private static bool ShouldMove(int index, bool[] affected)
        {
            return affected == null || index >= affected.Length || affected[index];
        }

        private static void AddEdge(List<int>[] adjacency, int a, int b)
        {
            if (!IsValidVertexIndex(a, adjacency.Length) || !IsValidVertexIndex(b, adjacency.Length) || a == b) return;
            if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
            if (!adjacency[b].Contains(a)) adjacency[b].Add(a);
        }

        private static Vector3Int ToWeldCell(Vector3 point, float cellSize)
        {
            return new Vector3Int(Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.y / cellSize), Mathf.FloorToInt(point.z / cellSize));
        }

        private static void AddEdgeKey(HashSet<long> edges, int a, int b)
        {
            if (a >= 0 && b >= 0) edges.Add(EdgeKey(a, b));
        }

        private static long EdgeKey(int a, int b)
        {
            int low = Mathf.Min(a, b);
            int high = Mathf.Max(a, b);
            return ((long)low << 32) | (uint)high;
        }

        private static int Find(int[] parents, int value)
        {
            int parent = parents[value];
            if (parent == value) return value;
            parents[value] = Find(parents, parent);
            return parents[value];
        }

        private static void Union(int[] parents, int[] ranks, int a, int b)
        {
            int rootA = Find(parents, a);
            int rootB = Find(parents, b);
            if (rootA == rootB) return;
            if (ranks[rootA] < ranks[rootB]) parents[rootA] = rootB;
            else if (ranks[rootA] > ranks[rootB]) parents[rootB] = rootA;
            else
            {
                parents[rootB] = rootA;
                ranks[rootA]++;
            }
        }

        private static bool IsValidVertexIndex(int index, int length)
        {
            return index >= 0 && index < length;
        }

        private static int StableStringHash(string value)
        {
            unchecked
            {
                int hash = 23;
                if (value == null) return hash;
                for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash;
            }
        }
    }

    /// <summary>Uniform-grid triangle lookup. It avoids an O(clothing vertices * body triangles) bind pass.</summary>
    public sealed class ClothingConformerSpatialIndex
    {
        private readonly Vector3[] vertices;
        private readonly int[] triangles;
        private readonly float cellSize;
        private readonly Dictionary<Vector3Int, List<int>> triangleCells = new Dictionary<Vector3Int, List<int>>();
        private readonly Dictionary<Vector3Int, List<int>> vertexCells = new Dictionary<Vector3Int, List<int>>();

        public ClothingConformerSpatialIndex(Vector3[] vertices, int[] triangles, float cellSize)
        {
            this.vertices = vertices ?? Array.Empty<Vector3>();
            this.triangles = triangles ?? Array.Empty<int>();
            this.cellSize = Mathf.Max(0.001f, cellSize);
            Build();
        }

        public List<int> QueryTriangles(Vector3 point, float radius)
        {
            List<int> result = new List<int>();
            HashSet<int> seen = new HashSet<int>();
            QueryTriangles(point, radius, result, seen);
            return result;
        }

        public void QueryTriangles(Vector3 point, float radius, List<int> results, HashSet<int> seen)
        {
            if (results == null || seen == null) return;
            results.Clear();
            seen.Clear();
            int range = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(radius, cellSize) / cellSize));
            Vector3Int center = ToCell(point);
            for (int x = -range; x <= range; x++)
            for (int y = -range; y <= range; y++)
            for (int z = -range; z <= range; z++)
            {
                List<int> cell;
                if (!triangleCells.TryGetValue(center + new Vector3Int(x, y, z), out cell)) continue;
                for (int i = 0; i < cell.Count; i++) if (seen.Add(cell[i])) results.Add(cell[i]);
            }

            // A very small radius can miss a thin body surface. Returning all triangles only in
            // that exceptional case keeps binding correct while the normal case stays accelerated.
            if (results.Count == 0)
            {
                for (int i = 0; i < triangles.Length / 3; i++) results.Add(i);
            }
        }

        public void FindNearestVertices(Vector3 point, int count, float radius, List<int> results)
        {
            FindNearestVertices(point, count, radius, results, new HashSet<int>());
        }

        public void FindNearestVertices(Vector3 point, int count, float radius, List<int> results, HashSet<int> seen)
        {
            if (results == null || seen == null) return;
            results.Clear();
            if (vertices.Length == 0 || count < 1) return;
            seen.Clear();
            Vector3Int center = ToCell(point);
            int maxRange = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(radius, cellSize) / cellSize));
            for (int range = 0; range <= maxRange && results.Count < count; range++)
            {
                for (int x = -range; x <= range; x++)
                for (int y = -range; y <= range; y++)
                for (int z = -range; z <= range; z++)
                {
                    List<int> cell;
                    if (!vertexCells.TryGetValue(center + new Vector3Int(x, y, z), out cell)) continue;
                    for (int i = 0; i < cell.Count; i++)
                    {
                        if (seen.Add(cell[i])) AddNearestVertex(results, cell[i], count, point);
                    }
                }
            }
            if (results.Count == 0)
            {
                for (int i = 0; i < vertices.Length; i++) AddNearestVertex(results, i, count, point);
            }
        }

        private void AddNearestVertex(List<int> results, int candidate, int count, Vector3 point)
        {
            float candidateDistanceSq = (vertices[candidate] - point).sqrMagnitude;
            int insertIndex = results.Count;
            for (int i = 0; i < results.Count; i++)
            {
                if (candidateDistanceSq < (vertices[results[i]] - point).sqrMagnitude)
                {
                    insertIndex = i;
                    break;
                }
            }
            if (insertIndex >= count) return;
            results.Insert(insertIndex, candidate);
            if (results.Count > count) results.RemoveAt(count);
        }

        private void Build()
        {
            for (int i = 0; i < vertices.Length; i++) Add(vertexCells, ToCell(vertices[i]), i);
            for (int triangle = 0; triangle * 3 + 2 < triangles.Length; triangle++)
            {
                int offset = triangle * 3;
                int a = triangles[offset];
                int b = triangles[offset + 1];
                int c = triangles[offset + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) continue;
                Vector3 min = Vector3.Min(vertices[a], Vector3.Min(vertices[b], vertices[c]));
                Vector3 max = Vector3.Max(vertices[a], Vector3.Max(vertices[b], vertices[c]));
                Vector3Int minCell = ToCell(min);
                Vector3Int maxCell = ToCell(max);
                for (int x = minCell.x; x <= maxCell.x; x++)
                for (int y = minCell.y; y <= maxCell.y; y++)
                for (int z = minCell.z; z <= maxCell.z; z++) Add(triangleCells, new Vector3Int(x, y, z), triangle);
            }
        }

        private Vector3Int ToCell(Vector3 point)
        {
            return new Vector3Int(Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.y / cellSize), Mathf.FloorToInt(point.z / cellSize));
        }

        private static void Add(Dictionary<Vector3Int, List<int>> cells, Vector3Int cell, int value)
        {
            List<int> values;
            if (!cells.TryGetValue(cell, out values))
            {
                values = new List<int>();
                cells.Add(cell, values);
            }
            values.Add(value);
        }
    }
}
