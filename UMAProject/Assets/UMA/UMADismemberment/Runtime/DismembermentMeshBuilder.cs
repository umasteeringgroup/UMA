using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Dismemberment
{
    internal enum DismembermentMeshBuildStatus
    {
        Success,
        NoAffectedTriangles,
        InvalidSource
    }

    internal readonly struct DismembermentMeshBuildOptions
    {
        public readonly float threshold;
        public readonly int existingCapSubmesh;
        public readonly bool generateCaps;
        public readonly bool requireClosedCaps;
        public readonly float capUvMetersPerTile;

        public DismembermentMeshBuildOptions(float threshold, int existingCapSubmesh,
            bool generateCaps, bool requireClosedCaps, float capUvMetersPerTile)
        {
            this.threshold = Mathf.Clamp01(threshold);
            this.existingCapSubmesh = existingCapSubmesh;
            this.generateCaps = generateCaps;
            this.requireClosedCaps = requireClosedCaps;
            this.capUvMetersPerTile = Mathf.Max(0.0001f, capUvMetersPerTile);
        }
    }

    internal sealed class DismembermentMeshBuildResult
    {
        public Mesh outerMesh;
        public Mesh detachedMesh;
        public int capSubmeshIndex = -1;
        public int boundaryLoopCount;

        public void DestroyMeshes()
        {
            DestroyOwnedMesh(outerMesh);
            DestroyOwnedMesh(detachedMesh);
            outerMesh = null;
            detachedMesh = null;
        }

        private static void DestroyOwnedMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    /// <summary>
    /// Partitions a skinned mesh by accumulated subtree weight. Source vertices are retained so
    /// every authored stream stays exact; only index buffers and duplicated cap vertices differ.
    /// </summary>
    internal static class DismembermentMeshBuilder
    {
        private const float GeometryEpsilon = 0.000001f;

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int first;
            public readonly int second;

            public EdgeKey(int a, int b)
            {
                if (a < b) { first = a; second = b; }
                else { first = b; second = a; }
            }

            public bool Equals(EdgeKey other) => first == other.first && second == other.second;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (first * 397) ^ second; }
            }
        }

        private sealed class EdgeUse
        {
            public int innerCount;
            public int outerCount;
            public int innerFrom;
            public int innerTo;
            public bool hasInnerDirection;
        }

        private readonly struct DirectedEdge
        {
            public readonly int from;
            public readonly int to;

            public DirectedEdge(int from, int to)
            {
                this.from = from;
                this.to = to;
            }
        }

        private readonly struct CapVertex
        {
            public readonly int sourceIndex;
            public readonly Vector3 normal;
            public readonly Vector4 tangent;
            public readonly Vector2 uv;

            public CapVertex(int sourceIndex, Vector3 normal, Vector4 tangent, Vector2 uv)
            {
                this.sourceIndex = sourceIndex;
                this.normal = normal;
                this.tangent = tangent;
                this.uv = uv;
            }
        }

        internal static DismembermentMeshBuildStatus Build(Mesh source, bool[] includedBones,
            DismembermentMeshBuildOptions options, out DismembermentMeshBuildResult result,
            out string error)
        {
            result = null;
            error = string.Empty;
            if (source == null)
            {
                error = "The renderer has no mesh.";
                return DismembermentMeshBuildStatus.InvalidSource;
            }
            if (!source.isReadable)
            {
                error = $"Mesh '{source.name}' is not CPU-readable.";
                return DismembermentMeshBuildStatus.InvalidSource;
            }
            if (includedBones == null || includedBones.Length == 0)
            {
                error = "The target bone is not used by this renderer.";
                return DismembermentMeshBuildStatus.NoAffectedTriangles;
            }
            if (source.vertexCount == 0 || source.subMeshCount == 0)
            {
                error = $"Mesh '{source.name}' contains no renderable geometry.";
                return DismembermentMeshBuildStatus.InvalidSource;
            }
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    error = $"Mesh '{source.name}' submesh {submesh} is not triangle topology.";
                    return DismembermentMeshBuildStatus.InvalidSource;
                }
            }

            if (!TryCalculateIncludedWeights(source, includedBones, out float[] includedWeights,
                out error)) return DismembermentMeshBuildStatus.InvalidSource;

            int sourceSubmeshCount = source.subMeshCount;
            var innerTriangles = CreateTriangleLists(sourceSubmeshCount);
            var outerTriangles = CreateTriangleLists(sourceSubmeshCount);
            var edgeUses = new Dictionary<EdgeKey, EdgeUse>();
            bool foundInner = false;
            bool foundOuter = false;

            for (int submesh = 0; submesh < sourceSubmeshCount; submesh++)
            {
                var triangles = new List<int>();
                source.GetTriangles(triangles, submesh);
                if ((triangles.Count % 3) != 0)
                {
                    error = $"Mesh '{source.name}' submesh {submesh} has an invalid triangle index count.";
                    return DismembermentMeshBuildStatus.InvalidSource;
                }

                for (int triangle = 0; triangle < triangles.Count; triangle += 3)
                {
                    int a = triangles[triangle];
                    int b = triangles[triangle + 1];
                    int c = triangles[triangle + 2];
                    if ((uint)a >= (uint)includedWeights.Length ||
                        (uint)b >= (uint)includedWeights.Length ||
                        (uint)c >= (uint)includedWeights.Length)
                    {
                        error = $"Mesh '{source.name}' contains an out-of-range triangle index.";
                        return DismembermentMeshBuildStatus.InvalidSource;
                    }

                    // Preserve the legacy tool's inclusive behavior: a triangle follows the
                    // detached side when any of its vertices exceeds the configured threshold.
                    bool isInner = includedWeights[a] > options.threshold ||
                        includedWeights[b] > options.threshold || includedWeights[c] > options.threshold;
                    List<int> destination = isInner ? innerTriangles[submesh] : outerTriangles[submesh];
                    destination.Add(a);
                    destination.Add(b);
                    destination.Add(c);
                    foundInner |= isInner;
                    foundOuter |= !isInner;
                    AddEdgeUse(edgeUses, a, b, isInner);
                    AddEdgeUse(edgeUses, b, c, isInner);
                    AddEdgeUse(edgeUses, c, a, isInner);
                }
            }

            if (!foundInner)
            {
                error = "No triangles exceeded the selected bone-weight threshold.";
                return DismembermentMeshBuildStatus.NoAffectedTriangles;
            }

            List<List<int>> boundaryLoops = null;
            if (options.generateCaps && foundOuter)
            {
                if (!TryBuildBoundaryLoops(edgeUses, out boundaryLoops, out error))
                {
                    if (options.requireClosedCaps)
                        return DismembermentMeshBuildStatus.InvalidSource;
                    boundaryLoops = new List<List<int>>();
                    error = string.Empty;
                }
            }
            boundaryLoops ??= new List<List<int>>();

            int capSubmesh = options.existingCapSubmesh >= 0 &&
                options.existingCapSubmesh < sourceSubmeshCount
                ? options.existingCapSubmesh
                : boundaryLoops.Count > 0 ? sourceSubmeshCount : -1;
            int outputSubmeshCount = capSubmesh == sourceSubmeshCount
                ? sourceSubmeshCount + 1 : sourceSubmeshCount;
            EnsureTriangleListCount(innerTriangles, outputSubmeshCount);
            EnsureTriangleListCount(outerTriangles, outputSubmeshCount);

            var innerCapVertices = new List<CapVertex>();
            var outerCapVertices = new List<CapVertex>();
            var innerCapTriangles = new List<int>();
            var outerCapTriangles = new List<int>();
            Vector3[] sourceVertices = source.vertices;
            for (int loopIndex = 0; loopIndex < boundaryLoops.Count; loopIndex++)
            {
                List<int> loop = boundaryLoops[loopIndex];
                if (!TryAppendCap(loop, sourceVertices, true, options.capUvMetersPerTile,
                    innerCapVertices, innerCapTriangles, out string capError) ||
                    !TryAppendCap(loop, sourceVertices, false, options.capUvMetersPerTile,
                        outerCapVertices, outerCapTriangles, out capError))
                {
                    error = $"Could not triangulate cut loop {loopIndex}: {capError}";
                    return DismembermentMeshBuildStatus.InvalidSource;
                }
            }

            if (capSubmesh >= 0)
            {
                int innerBase = source.vertexCount;
                for (int i = 0; i < innerCapTriangles.Count; i++)
                    innerTriangles[capSubmesh].Add(innerBase + innerCapTriangles[i]);
                int outerBase = source.vertexCount;
                for (int i = 0; i < outerCapTriangles.Count; i++)
                    outerTriangles[capSubmesh].Add(outerBase + outerCapTriangles[i]);
            }

            Mesh outerMesh = null;
            Mesh detachedMesh = null;
            try
            {
                outerMesh = BuildOutputMesh(source, source.name + " Dismembered Source",
                    outerTriangles, outerCapVertices);
                detachedMesh = BuildOutputMesh(source, source.name + " Detached",
                    innerTriangles, innerCapVertices);
                result = new DismembermentMeshBuildResult
                {
                    outerMesh = outerMesh,
                    detachedMesh = detachedMesh,
                    capSubmeshIndex = capSubmesh,
                    boundaryLoopCount = boundaryLoops.Count
                };
                return DismembermentMeshBuildStatus.Success;
            }
            catch (Exception exception)
            {
                DestroyMesh(outerMesh);
                DestroyMesh(detachedMesh);
                error = $"Failed to construct sliced meshes: {exception.Message}";
                return DismembermentMeshBuildStatus.InvalidSource;
            }
        }

        private static List<List<int>> CreateTriangleLists(int count)
        {
            var result = new List<List<int>>(count);
            for (int i = 0; i < count; i++) result.Add(new List<int>());
            return result;
        }

        private static void EnsureTriangleListCount(List<List<int>> lists, int count)
        {
            while (lists.Count < count) lists.Add(new List<int>());
        }

        private static bool TryCalculateIncludedWeights(Mesh mesh, bool[] includedBones,
            out float[] includedWeights, out string error)
        {
            includedWeights = new float[mesh.vertexCount];
            error = string.Empty;
            NativeArray<byte> bonesPerVertex = default;
            NativeArray<BoneWeight1> allWeights = default;
            try
            {
                bonesPerVertex = mesh.GetBonesPerVertex();
                allWeights = mesh.GetAllBoneWeights();
                if (bonesPerVertex.Length != mesh.vertexCount)
                {
                    error = $"Mesh '{mesh.name}' has inconsistent modern bone-weight data.";
                    return false;
                }

                int offset = 0;
                for (int vertex = 0; vertex < bonesPerVertex.Length; vertex++)
                {
                    int count = bonesPerVertex[vertex];
                    float weight = 0f;
                    for (int influence = 0; influence < count; influence++)
                    {
                        if ((uint)offset >= (uint)allWeights.Length)
                        {
                            error = $"Mesh '{mesh.name}' has a truncated bone-weight buffer.";
                            return false;
                        }
                        BoneWeight1 boneWeight = allWeights[offset++];
                        if ((uint)boneWeight.boneIndex < (uint)includedBones.Length &&
                            includedBones[boneWeight.boneIndex]) weight += boneWeight.weight;
                    }
                    includedWeights[vertex] = Mathf.Clamp01(weight);
                }
                if (offset != allWeights.Length)
                {
                    error = $"Mesh '{mesh.name}' has unused entries in its bone-weight buffer.";
                    return false;
                }
                return true;
            }
            finally
            {
                if (bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
                if (allWeights.IsCreated) allWeights.Dispose();
            }
        }

        private static void AddEdgeUse(Dictionary<EdgeKey, EdgeUse> uses, int from, int to,
            bool inner)
        {
            EdgeKey key = new EdgeKey(from, to);
            if (!uses.TryGetValue(key, out EdgeUse use))
            {
                use = new EdgeUse();
                uses.Add(key, use);
            }
            if (inner)
            {
                use.innerCount++;
                if (!use.hasInnerDirection)
                {
                    use.innerFrom = from;
                    use.innerTo = to;
                    use.hasInnerDirection = true;
                }
            }
            else use.outerCount++;
        }

        private static bool TryBuildBoundaryLoops(Dictionary<EdgeKey, EdgeUse> uses,
            out List<List<int>> loops, out string error)
        {
            loops = new List<List<int>>();
            error = string.Empty;
            var boundary = new List<DirectedEdge>();
            var directed = new HashSet<long>();
            var adjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<EdgeKey, EdgeUse> pair in uses)
            {
                EdgeUse use = pair.Value;
                if (use.innerCount == 0 || use.outerCount == 0 || !use.hasInnerDirection) continue;
                boundary.Add(new DirectedEdge(use.innerFrom, use.innerTo));
                directed.Add(DirectedKey(use.innerFrom, use.innerTo));
                AddNeighbor(adjacency, pair.Key.first, pair.Key.second);
                AddNeighbor(adjacency, pair.Key.second, pair.Key.first);
            }
            if (boundary.Count == 0) return true;

            foreach (KeyValuePair<int, List<int>> vertex in adjacency)
            {
                vertex.Value.Sort();
                if (vertex.Value.Count != 2)
                {
                    error = $"Cut boundary is non-manifold at vertex {vertex.Key} " +
                        $"(degree {vertex.Value.Count}, expected 2).";
                    return false;
                }
            }

            var remaining = new HashSet<EdgeKey>();
            for (int i = 0; i < boundary.Count; i++)
                remaining.Add(new EdgeKey(boundary[i].from, boundary[i].to));

            while (remaining.Count > 0)
            {
                EdgeKey startEdge = FindSmallestEdge(remaining);
                int start = startEdge.first;
                int previous = -1;
                int current = start;
                var loop = new List<int>();
                int safety = remaining.Count + 2;
                do
                {
                    if (--safety < 0)
                    {
                        error = "Cut boundary traversal did not close.";
                        return false;
                    }
                    loop.Add(current);
                    List<int> neighbors = adjacency[current];
                    int next = neighbors[0] != previous ? neighbors[0] : neighbors[1];
                    EdgeKey traversed = new EdgeKey(current, next);
                    if (!remaining.Remove(traversed) && next != start)
                    {
                        error = "Cut boundary contains a branch or repeated edge.";
                        return false;
                    }
                    previous = current;
                    current = next;
                }
                while (current != start);

                if (loop.Count < 3)
                {
                    error = "Cut boundary contains fewer than three vertices.";
                    return false;
                }
                int forward = 0;
                int reverse = 0;
                for (int i = 0; i < loop.Count; i++)
                {
                    int next = loop[(i + 1) % loop.Count];
                    if (directed.Contains(DirectedKey(loop[i], next))) forward++;
                    if (directed.Contains(DirectedKey(next, loop[i]))) reverse++;
                }
                if (reverse > forward) loop.Reverse();
                loops.Add(loop);
            }
            return true;
        }

        private static long DirectedKey(int from, int to) => ((long)(uint)from << 32) | (uint)to;

        private static void AddNeighbor(Dictionary<int, List<int>> adjacency, int vertex, int neighbor)
        {
            if (!adjacency.TryGetValue(vertex, out List<int> neighbors))
            {
                neighbors = new List<int>(2);
                adjacency.Add(vertex, neighbors);
            }
            if (!neighbors.Contains(neighbor)) neighbors.Add(neighbor);
        }

        private static EdgeKey FindSmallestEdge(HashSet<EdgeKey> edges)
        {
            EdgeKey best = default;
            bool found = false;
            foreach (EdgeKey edge in edges)
            {
                if (!found || edge.first < best.first ||
                    edge.first == best.first && edge.second < best.second)
                {
                    best = edge;
                    found = true;
                }
            }
            return best;
        }

        private static bool TryAppendCap(List<int> sourceLoop, Vector3[] vertices, bool detachedSide,
            float metersPerTile, List<CapVertex> destinationVertices, List<int> destinationTriangles,
            out string error)
        {
            error = string.Empty;
            var ordered = new List<int>(sourceLoop);
            Vector3 boundaryNormal = CalculateNewellNormal(ordered, vertices);
            if (boundaryNormal.sqrMagnitude <= GeometryEpsilon)
            {
                error = "boundary is degenerate";
                return false;
            }
            boundaryNormal.Normalize();
            Vector3 desiredNormal = detachedSide ? -boundaryNormal : boundaryNormal;
            if (Vector3.Dot(CalculateNewellNormal(ordered, vertices), desiredNormal) < 0f)
                ordered.Reverse();

            Vector3 tangent = FindLoopTangent(ordered, vertices, desiredNormal);
            Vector3 bitangent = Vector3.Cross(desiredNormal, tangent).normalized;
            var polygon = new List<Vector2>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                Vector3 point = vertices[ordered[i]];
                polygon.Add(new Vector2(Vector3.Dot(point, tangent), Vector3.Dot(point, bitangent)));
            }
            if (SignedArea(polygon) < 0f)
            {
                ordered.Reverse();
                polygon.Reverse();
            }
            if (!TryTriangulatePolygon(polygon, out List<int> localTriangles))
            {
                error = "projected boundary is self-intersecting or degenerate";
                return false;
            }

            int capBase = destinationVertices.Count;
            for (int i = 0; i < ordered.Count; i++)
            {
                Vector2 uv = polygon[i] / metersPerTile;
                destinationVertices.Add(new CapVertex(ordered[i], desiredNormal,
                    new Vector4(tangent.x, tangent.y, tangent.z, 1f), uv));
            }
            for (int i = 0; i < localTriangles.Count; i++)
                destinationTriangles.Add(capBase + localTriangles[i]);
            return true;
        }

        private static Vector3 CalculateNewellNormal(List<int> loop, Vector3[] vertices)
        {
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 current = vertices[loop[i]];
                Vector3 next = vertices[loop[(i + 1) % loop.Count]];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }
            return normal;
        }

        private static Vector3 FindLoopTangent(List<int> loop, Vector3[] vertices, Vector3 normal)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 edge = vertices[loop[(i + 1) % loop.Count]] - vertices[loop[i]];
                edge -= normal * Vector3.Dot(edge, normal);
                if (edge.sqrMagnitude > GeometryEpsilon) return edge.normalized;
            }
            return Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.95f
                ? Vector3.Cross(Vector3.up, normal).normalized
                : Vector3.Cross(Vector3.right, normal).normalized;
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static bool TryTriangulatePolygon(IReadOnlyList<Vector2> polygon,
            out List<int> triangles)
        {
            triangles = new List<int>(Mathf.Max(0, polygon.Count - 2) * 3);
            if (polygon.Count < 3) return false;
            var remaining = new List<int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++) remaining.Add(i);
            int safety = polygon.Count * polygon.Count;
            while (remaining.Count > 3 && safety-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    if (Cross(polygon[previous], polygon[current], polygon[next]) <= GeometryEpsilon)
                        continue;
                    bool contains = false;
                    for (int p = 0; p < remaining.Count; p++)
                    {
                        int candidate = remaining[p];
                        if (candidate == previous || candidate == current || candidate == next) continue;
                        if (PointInTriangle(polygon[candidate], polygon[previous], polygon[current],
                            polygon[next]))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains) continue;
                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (clipped) continue;

                // A collinear boundary point does not change the covered polygon. Removing the
                // flattest one avoids producing degenerate cap triangles while preserving the
                // duplicate seam vertex for exact surface closure.
                int flattest = -1;
                float smallestCross = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    float cross = Mathf.Abs(Cross(polygon[previous], polygon[current], polygon[next]));
                    if (cross < smallestCross)
                    {
                        smallestCross = cross;
                        flattest = i;
                    }
                }
                if (flattest < 0 || smallestCross > GeometryEpsilon * 10f) return false;
                remaining.RemoveAt(flattest);
            }
            if (remaining.Count != 3 ||
                Mathf.Abs(Cross(polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]])) <=
                GeometryEpsilon) return false;
            triangles.Add(remaining[0]);
            triangles.Add(remaining[1]);
            triangles.Add(remaining[2]);
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return ab.x * ac.y - ab.y * ac.x;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(a, b, point);
            float bc = Cross(b, c, point);
            float ca = Cross(c, a, point);
            return ab >= -GeometryEpsilon && bc >= -GeometryEpsilon && ca >= -GeometryEpsilon;
        }

        private static Mesh BuildOutputMesh(Mesh source, string name,
            List<List<int>> submeshTriangles, List<CapVertex> capVertices)
        {
            int sourceVertexCount = source.vertexCount;
            int outputVertexCount = sourceVertexCount + capVertices.Count;
            Vector3[] sourcePositions = source.vertices;
            var positions = new List<Vector3>(outputVertexCount);
            positions.AddRange(sourcePositions);
            for (int i = 0; i < capVertices.Count; i++)
                positions.Add(sourcePositions[capVertices[i].sourceIndex]);

            var mesh = new Mesh
            {
                name = name,
                indexFormat = outputVertexCount > ushort.MaxValue
                    ? IndexFormat.UInt32 : source.indexFormat
            };
            mesh.SetVertices(positions);
            bool copiedNormals = CopyNormals(source, mesh, capVertices, outputVertexCount);
            bool copiedTangents = CopyTangents(source, mesh, capVertices, outputVertexCount);
            CopyColors(source, mesh, capVertices, outputVertexCount);
            CopyUVs(source, mesh, capVertices, outputVertexCount);
            mesh.bindposes = source.bindposes;
            CopyBoneWeights(source, mesh, capVertices, outputVertexCount);

            mesh.subMeshCount = submeshTriangles.Count;
            for (int submesh = 0; submesh < submeshTriangles.Count; submesh++)
                mesh.SetTriangles(submeshTriangles[submesh], submesh, false);
            if (!copiedNormals) mesh.RecalculateNormals();
            if (!copiedTangents && source.HasVertexAttribute(VertexAttribute.TexCoord0))
                mesh.RecalculateTangents();
            CopyBlendShapes(source, mesh, capVertices, outputVertexCount);
            mesh.bounds = CalculateUsedBounds(positions, submeshTriangles);
            return mesh;
        }

        private static bool CopyNormals(Mesh source, Mesh destination, List<CapVertex> caps,
            int outputVertexCount)
        {
            Vector3[] values = source.normals;
            if (values.Length != source.vertexCount) return false;
            var output = new List<Vector3>(outputVertexCount);
            output.AddRange(values);
            for (int i = 0; i < caps.Count; i++) output.Add(caps[i].normal);
            destination.SetNormals(output);
            return true;
        }

        private static bool CopyTangents(Mesh source, Mesh destination, List<CapVertex> caps,
            int outputVertexCount)
        {
            Vector4[] values = source.tangents;
            if (values.Length != source.vertexCount) return false;
            var output = new List<Vector4>(outputVertexCount);
            output.AddRange(values);
            for (int i = 0; i < caps.Count; i++) output.Add(caps[i].tangent);
            destination.SetTangents(output);
            return true;
        }

        private static void CopyColors(Mesh source, Mesh destination, List<CapVertex> caps,
            int outputVertexCount)
        {
            Color[] values = source.colors;
            if (values.Length != source.vertexCount) return;
            var output = new List<Color>(outputVertexCount);
            output.AddRange(values);
            for (int i = 0; i < caps.Count; i++) output.Add(values[caps[i].sourceIndex]);
            destination.SetColors(output);
        }

        private static void CopyUVs(Mesh source, Mesh destination, List<CapVertex> caps,
            int outputVertexCount)
        {
            for (int channel = 0; channel < 8; channel++)
            {
                var sourceValues = new List<Vector4>();
                source.GetUVs(channel, sourceValues);
                if (sourceValues.Count != source.vertexCount) continue;
                var output = new List<Vector4>(outputVertexCount);
                output.AddRange(sourceValues);
                for (int i = 0; i < caps.Count; i++)
                {
                    if (channel == 0)
                        output.Add(new Vector4(caps[i].uv.x, caps[i].uv.y, 0f, 0f));
                    else output.Add(sourceValues[caps[i].sourceIndex]);
                }
                destination.SetUVs(channel, output);
            }
        }

        private static void CopyBoneWeights(Mesh source, Mesh destination, List<CapVertex> caps,
            int outputVertexCount)
        {
            NativeArray<byte> sourceCounts = default;
            NativeArray<BoneWeight1> sourceWeights = default;
            NativeArray<byte> outputCounts = default;
            NativeArray<BoneWeight1> outputWeights = default;
            try
            {
                sourceCounts = source.GetBonesPerVertex();
                sourceWeights = source.GetAllBoneWeights();
                if (sourceCounts.Length != source.vertexCount) return;
                int[] offsets = new int[source.vertexCount + 1];
                int total = 0;
                for (int i = 0; i < sourceCounts.Length; i++)
                {
                    offsets[i] = total;
                    total += sourceCounts[i];
                }
                offsets[source.vertexCount] = total;
                int capWeightCount = 0;
                for (int i = 0; i < caps.Count; i++) capWeightCount += sourceCounts[caps[i].sourceIndex];
                outputCounts = new NativeArray<byte>(outputVertexCount, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                outputWeights = new NativeArray<BoneWeight1>(total + capWeightCount, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < sourceCounts.Length; i++) outputCounts[i] = sourceCounts[i];
                NativeArray<BoneWeight1>.Copy(sourceWeights, outputWeights, sourceWeights.Length);
                int write = sourceWeights.Length;
                for (int i = 0; i < caps.Count; i++)
                {
                    int sourceIndex = caps[i].sourceIndex;
                    byte count = sourceCounts[sourceIndex];
                    outputCounts[source.vertexCount + i] = count;
                    for (int weight = offsets[sourceIndex]; weight < offsets[sourceIndex + 1]; weight++)
                        outputWeights[write++] = sourceWeights[weight];
                }
                destination.SetBoneWeights(outputCounts, outputWeights);
            }
            finally
            {
                if (sourceCounts.IsCreated) sourceCounts.Dispose();
                if (sourceWeights.IsCreated) sourceWeights.Dispose();
                if (outputCounts.IsCreated) outputCounts.Dispose();
                if (outputWeights.IsCreated) outputWeights.Dispose();
            }
        }

        private static void CopyBlendShapes(Mesh source, Mesh destination, List<CapVertex> caps,
            int outputVertexCount)
        {
            int sourceVertexCount = source.vertexCount;
            var deltaVertices = new Vector3[sourceVertexCount];
            var deltaNormals = new Vector3[sourceVertexCount];
            var deltaTangents = new Vector3[sourceVertexCount];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string shapeName = source.GetBlendShapeName(shape);
                int frames = source.GetBlendShapeFrameCount(shape);
                for (int frame = 0; frame < frames; frame++)
                {
                    source.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals,
                        deltaTangents);
                    Vector3[] outputVertices = ExtendBlendShape(deltaVertices, caps, outputVertexCount);
                    Vector3[] outputNormals = ExtendBlendShape(deltaNormals, caps, outputVertexCount);
                    Vector3[] outputTangents = ExtendBlendShape(deltaTangents, caps, outputVertexCount);
                    destination.AddBlendShapeFrame(shapeName,
                        source.GetBlendShapeFrameWeight(shape, frame), outputVertices, outputNormals,
                        outputTangents);
                }
            }
        }

        private static Vector3[] ExtendBlendShape(Vector3[] source, List<CapVertex> caps,
            int outputVertexCount)
        {
            var output = new Vector3[outputVertexCount];
            Array.Copy(source, output, source.Length);
            for (int i = 0; i < caps.Count; i++)
                output[source.Length + i] = source[caps[i].sourceIndex];
            return output;
        }

        private static Bounds CalculateUsedBounds(IReadOnlyList<Vector3> vertices,
            List<List<int>> submeshes)
        {
            bool found = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            for (int submesh = 0; submesh < submeshes.Count; submesh++)
            {
                List<int> triangles = submeshes[submesh];
                for (int i = 0; i < triangles.Count; i++)
                {
                    int index = triangles[i];
                    if ((uint)index >= (uint)vertices.Count) continue;
                    if (!found)
                    {
                        bounds = new Bounds(vertices[index], Vector3.zero);
                        found = true;
                    }
                    else bounds.Encapsulate(vertices[index]);
                }
            }
            return bounds;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
