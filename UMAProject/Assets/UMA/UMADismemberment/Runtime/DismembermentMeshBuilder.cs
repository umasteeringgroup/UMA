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
        public const float DefaultSeamWeldTolerance = 0.0001f;

        public readonly float threshold;
        public readonly int existingCapSubmesh;
        public readonly bool generateCaps;
        public readonly bool requireClosedCaps;
        public readonly float capUvMetersPerTile;
        public readonly float seamWeldTolerance;
        public readonly DismembermentCapUvMode capUvMode;
        public readonly float centeredCapUvPadding;
        public readonly int detachedFallbackBoneIndex;

        public DismembermentMeshBuildOptions(float threshold, int existingCapSubmesh,
            bool generateCaps, bool requireClosedCaps, float capUvMetersPerTile,
            float seamWeldTolerance = DefaultSeamWeldTolerance,
            DismembermentCapUvMode capUvMode = DismembermentCapUvMode.MeterScaledTiled,
            float centeredCapUvPadding = UmaDismemberment.DefaultCenteredCapUvPadding,
            int detachedFallbackBoneIndex = -1)
        {
            this.threshold = Mathf.Clamp01(threshold);
            this.existingCapSubmesh = existingCapSubmesh;
            this.generateCaps = generateCaps;
            this.requireClosedCaps = requireClosedCaps;
            this.capUvMetersPerTile = Mathf.Max(0.0001f, capUvMetersPerTile);
            this.seamWeldTolerance = Mathf.Max(GeometryEpsilon, seamWeldTolerance);
            this.capUvMode = capUvMode == DismembermentCapUvMode.CenteredFit
                ? DismembermentCapUvMode.CenteredFit
                : DismembermentCapUvMode.MeterScaledTiled;
            this.centeredCapUvPadding = Mathf.Clamp(centeredCapUvPadding, 0.001f, 0.25f);
            this.detachedFallbackBoneIndex = detachedFallbackBoneIndex;
        }

        private const float GeometryEpsilon = 0.000001f;
    }

    internal sealed class DismembermentMeshBuildResult
    {
        public Mesh outerMesh;
        public Mesh detachedMesh;
        public int capSubmeshIndex = -1;
        public int boundaryLoopCount;
        public int capTriangleCount;

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

        private readonly struct SpatialCell : IEquatable<SpatialCell>
        {
            public readonly int x;
            public readonly int y;
            public readonly int z;

            public SpatialCell(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public bool Equals(SpatialCell other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object obj) => obj is SpatialCell other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = (hash * 397) ^ y;
                    return (hash * 397) ^ z;
                }
            }
        }

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
            public int innerFromCanonical;
            public int innerToCanonical;
            public int innerFromSource;
            public int innerToSource;
            public int outerFromCanonical;
            public int outerToCanonical;
            public int outerFromSource;
            public int outerToSource;
            public bool hasInnerDirection;
            public bool hasOuterDirection;
        }

        private sealed class BoundaryLoop
        {
            public readonly List<int> innerSourceIndices;
            public readonly List<int> outerSourceIndices;

            public BoundaryLoop(int capacity)
            {
                innerSourceIndices = new List<int>(capacity);
                outerSourceIndices = new List<int>(capacity);
            }
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
            Vector3[] sourceVertices = source.vertices;
            int[] canonicalVertices = options.generateCaps
                ? BuildCanonicalVertexMap(sourceVertices, options.seamWeldTolerance)
                : null;
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
                    if (canonicalVertices != null)
                    {
                        AddEdgeUse(edgeUses, canonicalVertices, a, b, isInner);
                        AddEdgeUse(edgeUses, canonicalVertices, b, c, isInner);
                        AddEdgeUse(edgeUses, canonicalVertices, c, a, isInner);
                    }
                }
            }

            if (!foundInner)
            {
                error = "No triangles exceeded the selected bone-weight threshold.";
                return DismembermentMeshBuildStatus.NoAffectedTriangles;
            }

            List<BoundaryLoop> boundaryLoops = null;
            if (options.generateCaps && foundOuter)
            {
                if (!TryBuildBoundaryLoops(edgeUses, out boundaryLoops, out error))
                {
                    if (options.requireClosedCaps)
                        return DismembermentMeshBuildStatus.InvalidSource;
                    boundaryLoops = new List<BoundaryLoop>();
                    error = string.Empty;
                }
            }
            boundaryLoops ??= new List<BoundaryLoop>();

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
            for (int loopIndex = 0; loopIndex < boundaryLoops.Count; loopIndex++)
            {
                BoundaryLoop loop = boundaryLoops[loopIndex];
                if (!TryAppendCap(loop.innerSourceIndices, sourceVertices, true,
                    options.capUvMetersPerTile, options.capUvMode,
                    options.centeredCapUvPadding,
                    innerCapVertices, innerCapTriangles, out string capError) ||
                    !TryAppendCap(loop.outerSourceIndices, sourceVertices, false,
                        options.capUvMetersPerTile, options.capUvMode,
                        options.centeredCapUvPadding,
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
                    outerTriangles, outerCapVertices, null, -1);
                detachedMesh = BuildOutputMesh(source, source.name + " Detached",
                    innerTriangles, innerCapVertices, includedBones,
                    ResolveFallbackBoneIndex(includedBones,
                        options.detachedFallbackBoneIndex));
                result = new DismembermentMeshBuildResult
                {
                    outerMesh = outerMesh,
                    detachedMesh = detachedMesh,
                    capSubmeshIndex = capSubmesh,
                    boundaryLoopCount = boundaryLoops.Count,
                    capTriangleCount = innerCapTriangles.Count / 3
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

        private static int ResolveFallbackBoneIndex(bool[] includedBones, int requested)
        {
            if ((uint)requested < (uint)includedBones.Length && includedBones[requested])
                return requested;
            for (int i = 0; i < includedBones.Length; i++)
                if (includedBones[i]) return i;
            return -1;
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
            // Unity 6.3 returns non-owning views into the Mesh here. They must not be
            // disposed by callers; doing so can deallocate the Mesh's native storage.
            NativeArray<byte> bonesPerVertex = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> allWeights = mesh.GetAllBoneWeights();
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

        private static int[] BuildCanonicalVertexMap(Vector3[] vertices, float tolerance)
        {
            // UMA combines independently-authored slots and preserves UV/normal splits, so a
            // visually continuous body or armor seam can contain several vertex indices. Weld
            // positions only for boundary topology; cap attributes and weights still come from
            // the original vertex on each side of the cut.
            tolerance = Mathf.Max(GeometryEpsilon, tolerance);
            float toleranceSquared = tolerance * tolerance;
            float distanceTieEpsilon = Mathf.Max(toleranceSquared * 0.0001f, 1e-20f);
            var canonicalPositions = new List<Vector3>(vertices.Length);
            var buckets = new Dictionary<SpatialCell, List<int>>();
            var canonical = new int[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                Vector3 position = vertices[vertex];
                SpatialCell cell = GetSpatialCell(position, tolerance);
                int best = -1;
                float bestDistance = toleranceSquared;
                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            var neighbor = new SpatialCell(cell.x + x, cell.y + y, cell.z + z);
                            if (!buckets.TryGetValue(neighbor, out List<int> candidates)) continue;
                            for (int candidateIndex = 0; candidateIndex < candidates.Count;
                                candidateIndex++)
                            {
                                int candidate = candidates[candidateIndex];
                                float distance = (canonicalPositions[candidate] - position).sqrMagnitude;
                                if (distance > toleranceSquared ||
                                    distance > bestDistance + distanceTieEpsilon) continue;
                                if (best >= 0 && Mathf.Abs(distance - bestDistance) <=
                                    distanceTieEpsilon &&
                                    candidate > best) continue;
                                best = candidate;
                                bestDistance = distance;
                            }
                        }
                    }
                }

                if (best < 0)
                {
                    best = canonicalPositions.Count;
                    canonicalPositions.Add(position);
                    if (!buckets.TryGetValue(cell, out List<int> cellVertices))
                    {
                        cellVertices = new List<int>();
                        buckets.Add(cell, cellVertices);
                    }
                    cellVertices.Add(best);
                }
                canonical[vertex] = best;
            }
            return canonical;
        }

        private static SpatialCell GetSpatialCell(Vector3 position, float cellSize)
        {
            return new SpatialCell(
                Mathf.FloorToInt(position.x / cellSize),
                Mathf.FloorToInt(position.y / cellSize),
                Mathf.FloorToInt(position.z / cellSize));
        }

        private static void AddEdgeUse(Dictionary<EdgeKey, EdgeUse> uses,
            int[] canonicalVertices, int fromSource, int toSource, bool inner)
        {
            int fromCanonical = canonicalVertices[fromSource];
            int toCanonical = canonicalVertices[toSource];
            if (fromCanonical == toCanonical) return;
            EdgeKey key = new EdgeKey(fromCanonical, toCanonical);
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
                    use.innerFromCanonical = fromCanonical;
                    use.innerToCanonical = toCanonical;
                    use.innerFromSource = fromSource;
                    use.innerToSource = toSource;
                    use.hasInnerDirection = true;
                }
            }
            else
            {
                use.outerCount++;
                if (!use.hasOuterDirection)
                {
                    use.outerFromCanonical = fromCanonical;
                    use.outerToCanonical = toCanonical;
                    use.outerFromSource = fromSource;
                    use.outerToSource = toSource;
                    use.hasOuterDirection = true;
                }
            }
        }

        private static bool TryBuildBoundaryLoops(Dictionary<EdgeKey, EdgeUse> uses,
            out List<BoundaryLoop> loops, out string error)
        {
            loops = new List<BoundaryLoop>();
            error = string.Empty;
            var boundary = new List<DirectedEdge>();
            var directed = new HashSet<long>();
            var adjacency = new Dictionary<int, List<int>>();
            var innerSourceByCanonical = new Dictionary<int, int>();
            var outerSourceByCanonical = new Dictionary<int, int>();
            foreach (KeyValuePair<EdgeKey, EdgeUse> pair in uses)
            {
                EdgeUse use = pair.Value;
                if (use.innerCount == 0 || use.outerCount == 0 || !use.hasInnerDirection ||
                    !use.hasOuterDirection) continue;
                boundary.Add(new DirectedEdge(use.innerFromCanonical, use.innerToCanonical));
                directed.Add(DirectedKey(use.innerFromCanonical, use.innerToCanonical));
                AddNeighbor(adjacency, pair.Key.first, pair.Key.second);
                AddNeighbor(adjacency, pair.Key.second, pair.Key.first);
                AddRepresentative(innerSourceByCanonical, use.innerFromCanonical,
                    use.innerFromSource);
                AddRepresentative(innerSourceByCanonical, use.innerToCanonical,
                    use.innerToSource);
                AddRepresentative(outerSourceByCanonical, use.outerFromCanonical,
                    use.outerFromSource);
                AddRepresentative(outerSourceByCanonical, use.outerToCanonical,
                    use.outerToSource);
            }
            if (boundary.Count == 0)
            {
                error = "No geometric cut boundary was found between the detached and remaining " +
                    "triangles. The surfaces may use unmatched slot borders or a seam weld " +
                    "tolerance that is too small.";
                return false;
            }

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
                var boundaryLoop = new BoundaryLoop(loop.Count);
                for (int vertex = 0; vertex < loop.Count; vertex++)
                {
                    int canonical = loop[vertex];
                    if (!innerSourceByCanonical.TryGetValue(canonical, out int innerSource) ||
                        !outerSourceByCanonical.TryGetValue(canonical, out int outerSource))
                    {
                        error = $"Cut boundary vertex {canonical} is missing a source vertex on " +
                            "one side of the cut.";
                        return false;
                    }
                    boundaryLoop.innerSourceIndices.Add(innerSource);
                    boundaryLoop.outerSourceIndices.Add(outerSource);
                }
                loops.Add(boundaryLoop);
            }
            return true;
        }

        private static void AddRepresentative(Dictionary<int, int> representatives,
            int canonical, int source)
        {
            if (!representatives.TryGetValue(canonical, out int existing) || source < existing)
                representatives[canonical] = source;
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
            float metersPerTile, DismembermentCapUvMode uvMode, float centeredPadding,
            List<CapVertex> destinationVertices, List<int> destinationTriangles, out string error)
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

            List<Vector2> centeredUvs = null;
            if (uvMode == DismembermentCapUvMode.CenteredFit &&
                !TryCreateCenteredCapUvs(polygon, centeredPadding, out centeredUvs))
            {
                error = "projected boundary cannot be centered in UV space";
                return false;
            }

            int capBase = destinationVertices.Count;
            for (int i = 0; i < ordered.Count; i++)
            {
                Vector2 uv = centeredUvs != null ? centeredUvs[i] : polygon[i] / metersPerTile;
                destinationVertices.Add(new CapVertex(ordered[i], desiredNormal,
                    new Vector4(tangent.x, tangent.y, tangent.z, 1f), uv));
            }
            for (int i = 0; i < localTriangles.Count; i++)
                destinationTriangles.Add(capBase + localTriangles[i]);
            return true;
        }

        private static bool TryCreateCenteredCapUvs(IReadOnlyList<Vector2> polygon, float padding,
            out List<Vector2> uvs)
        {
            uvs = null;
            float twiceArea = 0f;
            Vector2 weightedCenter = Vector2.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                float cross = current.x * next.y - next.x * current.y;
                twiceArea += cross;
                weightedCenter += (current + next) * cross;
            }
            if (Mathf.Abs(twiceArea) <= GeometryEpsilon) return false;

            Vector2 center = weightedCenter / (3f * twiceArea);
            float maximumExtent = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 offset = polygon[i] - center;
                maximumExtent = Mathf.Max(maximumExtent,
                    Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)));
            }
            if (maximumExtent <= GeometryEpsilon) return false;

            padding = Mathf.Clamp(padding, 0.001f, 0.25f);
            float scale = (0.5f - padding) / maximumExtent;
            uvs = new List<Vector2>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 uv = Vector2.one * 0.5f + (polygon[i] - center) * scale;
                uv.x = Mathf.Clamp(uv.x, padding, 1f - padding);
                uv.y = Mathf.Clamp(uv.y, padding, 1f - padding);
                uvs.Add(uv);
            }
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
            List<List<int>> submeshTriangles, List<CapVertex> capVertices,
            bool[] retainedBones, int fallbackBoneIndex)
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
            CopyBoneWeights(source, mesh, capVertices, outputVertexCount, retainedBones,
                fallbackBoneIndex, submeshTriangles);

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
                if (sourceValues.Count != source.vertexCount)
                {
                    if (channel != 0) continue;
                    sourceValues.Clear();
                    for (int vertex = 0; vertex < source.vertexCount; vertex++)
                        sourceValues.Add(Vector4.zero);
                }
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
            int outputVertexCount, bool[] retainedBones, int fallbackBoneIndex,
            List<List<int>> submeshTriangles)
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
                if (total != sourceWeights.Length)
                    throw new InvalidOperationException($"Mesh '{source.name}' has inconsistent " +
                        "bone-weight counts.");

                if (retainedBones == null)
                {
                    int capWeightCount = 0;
                    for (int i = 0; i < caps.Count; i++)
                        capWeightCount += sourceCounts[caps[i].sourceIndex];
                    outputCounts = new NativeArray<byte>(outputVertexCount, Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    outputWeights = new NativeArray<BoneWeight1>(total + capWeightCount,
                        Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < sourceCounts.Length; i++)
                        outputCounts[i] = sourceCounts[i];
                    NativeArray<BoneWeight1>.Copy(sourceWeights, outputWeights,
                        sourceWeights.Length);
                    int preservedWrite = sourceWeights.Length;
                    for (int i = 0; i < caps.Count; i++)
                    {
                        int sourceIndex = caps[i].sourceIndex;
                        byte count = sourceCounts[sourceIndex];
                        outputCounts[source.vertexCount + i] = count;
                        for (int weight = offsets[sourceIndex];
                            weight < offsets[sourceIndex + 1]; weight++)
                            outputWeights[preservedWrite++] = sourceWeights[weight];
                    }
                    destination.SetBoneWeights(outputCounts, outputWeights);
                    return;
                }

                if ((uint)fallbackBoneIndex >= (uint)retainedBones.Length ||
                    !retainedBones[fallbackBoneIndex])
                    throw new InvalidOperationException("Detached weight sanitization has no " +
                        "valid fallback bone.");

                bool[] renderedSourceVertices = FindRenderedSourceVertices(source.vertexCount,
                    submeshTriangles);
                int sanitizedWeightCount = 0;
                outputCounts = new NativeArray<byte>(outputVertexCount, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                for (int vertex = 0; vertex < outputVertexCount; vertex++)
                {
                    int sourceIndex = vertex < source.vertexCount
                        ? vertex : caps[vertex - source.vertexCount].sourceIndex;
                    bool rendered = vertex >= source.vertexCount ||
                        renderedSourceVertices[sourceIndex];
                    float retainedTotal = rendered ? SumRetainedWeights(sourceIndex, offsets,
                        sourceWeights, retainedBones) : 0f;
                    int retainedCount = retainedTotal > GeometryEpsilon
                        ? CountRetainedWeights(sourceIndex, offsets, sourceWeights, retainedBones)
                        : 0;
                    if (retainedCount == 0) retainedCount = 1;
                    outputCounts[vertex] = (byte)retainedCount;
                    sanitizedWeightCount += retainedCount;
                }

                outputWeights = new NativeArray<BoneWeight1>(sanitizedWeightCount,
                    Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int write = 0;
                for (int vertex = 0; vertex < outputVertexCount; vertex++)
                {
                    int sourceIndex = vertex < source.vertexCount
                        ? vertex : caps[vertex - source.vertexCount].sourceIndex;
                    bool rendered = vertex >= source.vertexCount ||
                        renderedSourceVertices[sourceIndex];
                    float retainedTotal = rendered ? SumRetainedWeights(sourceIndex, offsets,
                        sourceWeights, retainedBones) : 0f;
                    if (retainedTotal <= GeometryEpsilon)
                    {
                        outputWeights[write++] = new BoneWeight1
                        {
                            boneIndex = fallbackBoneIndex,
                            weight = 1f
                        };
                        continue;
                    }
                    for (int weightIndex = offsets[sourceIndex];
                        weightIndex < offsets[sourceIndex + 1]; weightIndex++)
                    {
                        BoneWeight1 weight = sourceWeights[weightIndex];
                        ValidateWeightBoneIndex(weight.boneIndex, retainedBones.Length,
                            source.name);
                        if (!retainedBones[weight.boneIndex] || weight.weight <= 0f) continue;
                        weight.weight /= retainedTotal;
                        outputWeights[write++] = weight;
                    }
                }
                destination.SetBoneWeights(outputCounts, outputWeights);
            }
            finally
            {
                // sourceCounts/sourceWeights are non-owning Mesh views in Unity 6.3.
                if (outputCounts.IsCreated) outputCounts.Dispose();
                if (outputWeights.IsCreated) outputWeights.Dispose();
            }
        }

        private static bool[] FindRenderedSourceVertices(int sourceVertexCount,
            List<List<int>> submeshTriangles)
        {
            var rendered = new bool[sourceVertexCount];
            for (int submesh = 0; submesh < submeshTriangles.Count; submesh++)
            {
                List<int> triangles = submeshTriangles[submesh];
                for (int i = 0; i < triangles.Count; i++)
                {
                    int vertex = triangles[i];
                    if ((uint)vertex < (uint)sourceVertexCount) rendered[vertex] = true;
                }
            }
            return rendered;
        }

        private static int CountRetainedWeights(int sourceIndex, int[] offsets,
            NativeArray<BoneWeight1> weights, bool[] retainedBones)
        {
            int count = 0;
            for (int i = offsets[sourceIndex]; i < offsets[sourceIndex + 1]; i++)
            {
                BoneWeight1 weight = weights[i];
                ValidateWeightBoneIndex(weight.boneIndex, retainedBones.Length, "source mesh");
                if (retainedBones[weight.boneIndex] && weight.weight > 0f) count++;
            }
            return count;
        }

        private static float SumRetainedWeights(int sourceIndex, int[] offsets,
            NativeArray<BoneWeight1> weights, bool[] retainedBones)
        {
            float total = 0f;
            for (int i = offsets[sourceIndex]; i < offsets[sourceIndex + 1]; i++)
            {
                BoneWeight1 weight = weights[i];
                ValidateWeightBoneIndex(weight.boneIndex, retainedBones.Length, "source mesh");
                if (retainedBones[weight.boneIndex] && weight.weight > 0f)
                    total += weight.weight;
            }
            return total;
        }

        private static void ValidateWeightBoneIndex(int boneIndex, int boneCount,
            string meshName)
        {
            if ((uint)boneIndex >= (uint)boneCount)
                throw new InvalidOperationException($"Mesh '{meshName}' contains a weight for " +
                    $"out-of-range bone {boneIndex}.");
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
