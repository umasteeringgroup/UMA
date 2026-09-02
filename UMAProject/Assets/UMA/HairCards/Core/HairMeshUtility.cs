using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards
{
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
