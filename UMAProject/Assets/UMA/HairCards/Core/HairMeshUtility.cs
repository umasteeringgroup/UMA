using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards
{
    public readonly struct HairMeshRaycastHit
    {
        public readonly int TriangleIndex;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Vector3 Barycentric;
        public readonly float Distance;

        public HairMeshRaycastHit(int triangleIndex, Vector3 point, Vector3 normal,
            Vector3 barycentric, float distance)
        {
            TriangleIndex = triangleIndex;
            Point = point;
            Normal = normal;
            Barycentric = barycentric;
            Distance = distance;
        }
    }

    /// <summary>
    /// Preview scenes do not consistently register colliders with a queryable physics scene. This
    /// small BVH keeps hair-authoring hover and strokes independent of editor physics while avoiding
    /// a full triangle scan for every mouse-move event.
    /// </summary>
    public sealed class HairMeshRaycaster
    {
        private const int LeafTriangleCount = 8;
        private const float IntersectionEpsilon = 0.0000001f;

        private struct Node
        {
            public Vector3 min;
            public Vector3 max;
            public int left;
            public int right;
            public int start;
            public int count;
        }

        private sealed class CentroidComparer : IComparer<int>
        {
            public Vector3[] centroids;
            public int axis;

            public int Compare(int left, int right)
            {
                float difference = centroids[left][axis] - centroids[right][axis];
                if (difference < 0f) return -1;
                if (difference > 0f) return 1;
                return left.CompareTo(right);
            }
        }

        private Vector3[] vertices = Array.Empty<Vector3>();
        private Vector3[] vertexNormals = Array.Empty<Vector3>();
        private int[] triangleVertices = Array.Empty<int>();
        private Vector3[] triangleMinimums = Array.Empty<Vector3>();
        private Vector3[] triangleMaximums = Array.Empty<Vector3>();
        private Vector3[] triangleCentroids = Array.Empty<Vector3>();
        private int[] triangleOrder = Array.Empty<int>();
        private Node[] nodes = Array.Empty<Node>();
        private int[] traversalStack = Array.Empty<int>();
        private readonly CentroidComparer centroidComparer = new CentroidComparer();

        public int TriangleCount => triangleVertices.Length / 3;

        public bool MatchesGeometry(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals, IReadOnlyList<int> indices)
        {
            if (vertices.Length != positions.Count || vertexNormals.Length != normals.Count || triangleVertices.Length != indices.Count) return false;
            for (int i = 0; i < positions.Count; i++) if (!vertices[i].Equals(positions[i])) return false;
            for (int i = 0; i < normals.Count; i++) if (!vertexNormals[i].Equals(normals[i])) return false;
            for (int i = 0; i < indices.Count; i++) if (triangleVertices[i] != indices[i]) return false;
            return true;
        }

        public HairMeshRaycaster(Mesh mesh)
        {
            Rebuild(mesh);
        }

        public void Rebuild(Mesh mesh)
        {
            vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
            vertexNormals = mesh != null ? mesh.normals : Array.Empty<Vector3>();
            List<int> flattened = new List<int>();
            if (mesh != null)
            {
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] indices = mesh.GetTriangles(submesh, true);
                    flattened.AddRange(indices);
                }
            }
            triangleVertices = flattened.ToArray();
            int triangleCount = TriangleCount;
            triangleMinimums = new Vector3[triangleCount];
            triangleMaximums = new Vector3[triangleCount];
            triangleCentroids = new Vector3[triangleCount];
            triangleOrder = new int[triangleCount];
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int index = triangle * 3;
                int a = triangleVertices[index];
                int b = triangleVertices[index + 1];
                int c = triangleVertices[index + 2];
                if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length ||
                    (uint)c >= (uint)vertices.Length)
                {
                    triangleMinimums[triangle] = Vector3.zero;
                    triangleMaximums[triangle] = Vector3.zero;
                    triangleCentroids[triangle] = Vector3.zero;
                }
                else
                {
                    Vector3 minimum = Vector3.Min(vertices[a], Vector3.Min(vertices[b], vertices[c]));
                    Vector3 maximum = Vector3.Max(vertices[a], Vector3.Max(vertices[b], vertices[c]));
                    triangleMinimums[triangle] = minimum;
                    triangleMaximums[triangle] = maximum;
                    triangleCentroids[triangle] = (minimum + maximum) * 0.5f;
                }
                triangleOrder[triangle] = triangle;
            }

            if (triangleCount == 0)
            {
                nodes = Array.Empty<Node>();
                traversalStack = Array.Empty<int>();
                return;
            }

            List<Node> buildNodes = new List<Node>(triangleCount * 2);
            centroidComparer.centroids = triangleCentroids;
            BuildNode(buildNodes, 0, triangleCount);
            nodes = buildNodes.ToArray();
            traversalStack = new int[nodes.Length];
        }

        public bool Raycast(Ray ray, out HairMeshRaycastHit hit)
        {
            hit = default;
            if (nodes.Length == 0 || ray.direction.sqrMagnitude < IntersectionEpsilon) return false;
            ray.direction = ray.direction.normalized;
            float closest = float.MaxValue;
            int closestTriangle = -1;
            Vector3 closestBarycentric = Vector3.zero;
            int stackCount = 0;
            traversalStack[stackCount++] = 0;
            while (stackCount > 0)
            {
                Node node = nodes[traversalStack[--stackCount]];
                if (!IntersectsBounds(ray, node.min, node.max, closest)) continue;
                if (node.count > 0)
                {
                    for (int ordered = node.start; ordered < node.start + node.count; ordered++)
                    {
                        int triangle = triangleOrder[ordered];
                        int index = triangle * 3;
                        int a = triangleVertices[index];
                        int b = triangleVertices[index + 1];
                        int c = triangleVertices[index + 2];
                        if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length ||
                            (uint)c >= (uint)vertices.Length ||
                            !IntersectsTriangle(ray, vertices[a], vertices[b], vertices[c],
                                out float distance, out Vector3 barycentric) || distance >= closest) continue;
                        closest = distance;
                        closestTriangle = triangle;
                        closestBarycentric = barycentric;
                    }
                    continue;
                }
                if (node.left >= 0) traversalStack[stackCount++] = node.left;
                if (node.right >= 0) traversalStack[stackCount++] = node.right;
            }

            if (closestTriangle < 0) return false;
            int triangleOffset = closestTriangle * 3;
            Vector3 first = vertices[triangleVertices[triangleOffset]];
            Vector3 second = vertices[triangleVertices[triangleOffset + 1]];
            Vector3 third = vertices[triangleVertices[triangleOffset + 2]];
            Vector3 normal = Vector3.Cross(second - first, third - first);
            // Cross-product magnitude is area; its square is not a distance epsilon.
            // Small, valid scalp triangles used to fall back to the camera direction here.
            float normalSquare = normal.sqrMagnitude;
            normal = float.IsFinite(normalSquare) && normalSquare > 0f
                ? normal / Mathf.Sqrt(normalSquare) : -ray.direction;
            if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;
            hit = new HairMeshRaycastHit(closestTriangle, ray.GetPoint(closest), normal,
                closestBarycentric, closest);
            return true;
        }

        public bool TryGetSurfaceNormal(int triangleIndex, Vector3 barycentric, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (!TryGetTriangleVertices(triangleIndex, out int a, out int b, out int c)) return false;
            if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length || (uint)c >= (uint)vertices.Length) return false;
            if (vertexNormals.Length == vertices.Length)
                normal = vertexNormals[a] * barycentric.x + vertexNormals[b] * barycentric.y + vertexNormals[c] * barycentric.z;
            float square = normal.sqrMagnitude;
            if (!float.IsFinite(square) || square <= 0f)
            {
                normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                square = normal.sqrMagnitude;
            }
            if (!float.IsFinite(square) || square <= 0f) return false;
            normal /= Mathf.Sqrt(square);
            return true;
        }

        public bool TryGetTriangleVertices(int triangleIndex, out int a, out int b, out int c)
        {
            int offset = triangleIndex * 3;
            if (offset < 0 || offset + 2 >= triangleVertices.Length)
            {
                a = b = c = -1;
                return false;
            }
            a = triangleVertices[offset];
            b = triangleVertices[offset + 1];
            c = triangleVertices[offset + 2];
            return true;
        }

        // BVH closest-surface query. Winding normal is deliberately not flipped toward the query:
        // gravity needs the signed side of an outward-facing scalp/body surface.
        public bool ClosestPoint(Vector3 point, out HairMeshRaycastHit hit, float maximumDistance = float.PositiveInfinity)
        {
            hit = default;
            if (nodes.Length == 0) return false;
            float bestSquare = maximumDistance * maximumDistance;
            int bestTriangle = -1;
            Vector3 bestPoint = default, bestNormal = default;
            int stackCount = 0;
            traversalStack[stackCount++] = 0;
            while (stackCount > 0)
            {
                Node node = nodes[traversalStack[--stackCount]];
                if (BoundsDistanceSquared(point, node) > bestSquare) continue;
                if (node.count == 0)
                {
                    int near = node.left, far = node.right;
                    if (near >= 0 && far >= 0 && BoundsDistanceSquared(point, nodes[near]) > BoundsDistanceSquared(point, nodes[far]))
                    { int swap = near; near = far; far = swap; }
                    if (far >= 0) traversalStack[stackCount++] = far;
                    if (near >= 0) traversalStack[stackCount++] = near;
                    continue;
                }
                for (int index = node.start; index < node.start + node.count; index++)
                {
                    int triangle = triangleOrder[index];
                    int offset = triangle * 3;
                    int a = triangleVertices[offset], b = triangleVertices[offset + 1], c = triangleVertices[offset + 2];
                    if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length) continue;
                    Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    if (normal.sqrMagnitude < 1e-16f) continue;
                    Vector3 nearest = HairMeshUtility.ClosestPointOnTriangle(point, vertices[a], vertices[b], vertices[c]);
                    float square = (nearest - point).sqrMagnitude;
                    if (square > bestSquare) continue;
                    bestSquare = square; bestTriangle = triangle; bestPoint = nearest; bestNormal = normal.normalized;
                }
            }
            if (bestTriangle < 0) return false;
            hit = new HairMeshRaycastHit(bestTriangle, bestPoint, bestNormal, Vector3.zero, Mathf.Sqrt(bestSquare));
            return true;
        }

        private static float BoundsDistanceSquared(Vector3 point, Node node) =>
            (point - Vector3.Max(node.min, Vector3.Min(node.max, point))).sqrMagnitude;

        private int BuildNode(List<Node> buildNodes, int start, int count)
        {
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            Vector3 centroidMinimum = minimum;
            Vector3 centroidMaximum = maximum;
            for (int ordered = start; ordered < start + count; ordered++)
            {
                int triangle = triangleOrder[ordered];
                minimum = Vector3.Min(minimum, triangleMinimums[triangle]);
                maximum = Vector3.Max(maximum, triangleMaximums[triangle]);
                centroidMinimum = Vector3.Min(centroidMinimum, triangleCentroids[triangle]);
                centroidMaximum = Vector3.Max(centroidMaximum, triangleCentroids[triangle]);
            }

            int nodeIndex = buildNodes.Count;
            buildNodes.Add(default);
            Vector3 centroidSize = centroidMaximum - centroidMinimum;
            if (count <= LeafTriangleCount || centroidSize.sqrMagnitude < IntersectionEpsilon)
            {
                buildNodes[nodeIndex] = new Node
                {
                    min = minimum, max = maximum, left = -1, right = -1, start = start, count = count
                };
                return nodeIndex;
            }

            centroidComparer.axis = centroidSize.x >= centroidSize.y && centroidSize.x >= centroidSize.z
                ? 0 : centroidSize.y >= centroidSize.z ? 1 : 2;
            Array.Sort(triangleOrder, start, count, centroidComparer);
            int leftCount = count / 2;
            int left = BuildNode(buildNodes, start, leftCount);
            int right = BuildNode(buildNodes, start + leftCount, count - leftCount);
            buildNodes[nodeIndex] = new Node
            {
                min = minimum, max = maximum, left = left, right = right, start = 0, count = 0
            };
            return nodeIndex;
        }

        private static bool IntersectsBounds(Ray ray, Vector3 minimum, Vector3 maximum,
            float maximumDistance)
        {
            float near = 0f;
            float far = maximumDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                float direction = ray.direction[axis];
                float origin = ray.origin[axis];
                if (Mathf.Abs(direction) < IntersectionEpsilon)
                {
                    if (origin < minimum[axis] || origin > maximum[axis]) return false;
                    continue;
                }
                float inverse = 1f / direction;
                float first = (minimum[axis] - origin) * inverse;
                float second = (maximum[axis] - origin) * inverse;
                if (first > second) (first, second) = (second, first);
                near = Mathf.Max(near, first);
                far = Mathf.Min(far, second);
                if (near > far) return false;
            }
            return far >= 0f;
        }

        private static bool IntersectsTriangle(Ray ray, Vector3 first, Vector3 second, Vector3 third,
            out float distance, out Vector3 barycentric)
        {
            distance = 0f;
            barycentric = Vector3.zero;
            Vector3 edgeOne = second - first;
            Vector3 edgeTwo = third - first;
            Vector3 cross = Vector3.Cross(ray.direction, edgeTwo);
            float determinant = Vector3.Dot(edgeOne, cross);
            if (Mathf.Abs(determinant) < IntersectionEpsilon) return false;
            float inverse = 1f / determinant;
            Vector3 fromFirst = ray.origin - first;
            float secondWeight = Vector3.Dot(fromFirst, cross) * inverse;
            if (secondWeight < 0f || secondWeight > 1f) return false;
            Vector3 sideCross = Vector3.Cross(fromFirst, edgeOne);
            float thirdWeight = Vector3.Dot(ray.direction, sideCross) * inverse;
            if (thirdWeight < 0f || secondWeight + thirdWeight > 1f) return false;
            distance = Vector3.Dot(edgeTwo, sideCross) * inverse;
            if (distance <= IntersectionEpsilon) return false;
            barycentric = new Vector3(1f - secondWeight - thirdWeight, secondWeight, thirdWeight);
            return true;
        }
    }

    public static class HairMeshUtility
    {
        public static string ComputeTopologySignature(Mesh mesh)
        {
            if (mesh == null) return string.Empty;
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Mix(ref hash, mesh.vertexCount);
                Mix(ref hash, mesh.subMeshCount);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    SubMeshDescriptor descriptor = mesh.GetSubMesh(submesh);
                    Mix(ref hash, descriptor.indexStart);
                    Mix(ref hash, descriptor.indexCount);
                    Mix(ref hash, descriptor.baseVertex);
                    Mix(ref hash, (int)descriptor.topology);
                    int[] indices = mesh.GetIndices(submesh, false);
                    for (int index = 0; index < indices.Length; index++)
                    {
                        Mix(ref hash, indices[index]);
                    }
                }
                return hash.ToString("x16");
            }
        }

        public static bool TryEvaluateAnchor(
            Mesh mesh,
            HairSurfaceAnchor anchor,
            out Vector3 localPosition,
            out Vector3 localNormal)
        {
            localPosition = anchor.CachedLocalPosition;
            localNormal = anchor.CachedLocalNormal.sqrMagnitude > 1e-8f
                ? anchor.CachedLocalNormal.normalized
                : Vector3.up;
            if (mesh == null || !anchor.IsValid || anchor.SubmeshIndex < 0 ||
                anchor.SubmeshIndex >= mesh.subMeshCount)
            {
                return false;
            }

            int[] triangles;
            Vector3[] vertices;
            Vector3[] normals;
            try
            {
                triangles = mesh.GetTriangles(anchor.SubmeshIndex, true);
                vertices = mesh.vertices;
                normals = mesh.normals;
            }
            catch (Exception)
            {
                return false;
            }

            int triangleOffset = anchor.TriangleIndex * 3;
            if (triangleOffset < 0 || triangleOffset + 2 >= triangles.Length) return false;
            int i0 = triangles[triangleOffset];
            int i1 = triangles[triangleOffset + 1];
            int i2 = triangles[triangleOffset + 2];
            if ((uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length ||
                (uint)i2 >= (uint)vertices.Length)
            {
                return false;
            }

            Vector3 barycentric = anchor.Barycentric;
            localPosition = vertices[i0] * barycentric.x + vertices[i1] * barycentric.y +
                            vertices[i2] * barycentric.z;
            if (normals != null && normals.Length == vertices.Length)
            {
                localNormal = normals[i0] * barycentric.x + normals[i1] * barycentric.y +
                              normals[i2] * barycentric.z;
            }
            else
            {
                localNormal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
            }
            localNormal = localNormal.sqrMagnitude > 1e-8f ? localNormal.normalized : Vector3.up;
            localPosition += localNormal * anchor.NormalOffset;
            return true;
        }

        public static Vector3 Barycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
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
            if (Mathf.Abs(denominator) < 1e-10f) return new Vector3(1f, 0f, 0f);
            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            return new Vector3(1f - v - w, v, w);
        }

        public static bool TryFindClosestSurface(Mesh mesh, string sourceMeshId, Vector3 localPoint,
            out HairSurfaceAnchor anchor)
        {
            anchor = default;
            if (mesh == null) return false;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            float bestSquare = float.MaxValue;
            int bestSubmesh = -1;
            int bestTriangle = -1;
            Vector3 bestPoint = localPoint;
            Vector3 bestBarycentric = Vector3.right;
            int bestA = 0, bestB = 0, bestC = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh, true);
                for (int offset = 0, triangle = 0; offset + 2 < indices.Length; offset += 3, triangle++)
                {
                    int a = indices[offset];
                    int b = indices[offset + 1];
                    int c = indices[offset + 2];
                    Vector3 point = ClosestPointOnTriangle(localPoint, vertices[a], vertices[b], vertices[c]);
                    float square = (point - localPoint).sqrMagnitude;
                    if (square >= bestSquare) continue;
                    bestSquare = square;
                    bestSubmesh = submesh;
                    bestTriangle = triangle;
                    bestPoint = point;
                    bestBarycentric = Barycentric(point, vertices[a], vertices[b], vertices[c]);
                    bestA = a;
                    bestB = b;
                    bestC = c;
                }
            }
            if (bestSubmesh < 0) return false;
            Vector3 normal = normals != null && normals.Length == vertices.Length
                ? (normals[bestA] * bestBarycentric.x + normals[bestB] * bestBarycentric.y +
                   normals[bestC] * bestBarycentric.z).normalized
                : Vector3.Cross(vertices[bestB] - vertices[bestA], vertices[bestC] - vertices[bestA]).normalized;
            anchor = HairSurfaceAnchor.Create(sourceMeshId, bestSubmesh, bestTriangle, bestBarycentric,
                0f, bestPoint, normal);
            return true;
        }

        public static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
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
                return a + ab * v;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + ac * w;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + (c - b) * w;
            }

            float denominator = 1f / (va + vb + vc);
            float barycentricV = vb * denominator;
            float barycentricW = vc * denominator;
            return a + ab * barycentricV + ac * barycentricW;
        }

        private static void Mix(ref ulong hash, int value)
        {
            unchecked
            {
                uint data = (uint)value;
                for (int byteIndex = 0; byteIndex < 4; byteIndex++)
                {
                    hash ^= data & 0xff;
                    hash *= 1099511628211UL;
                    data >>= 8;
                }
            }
        }
    }
}
