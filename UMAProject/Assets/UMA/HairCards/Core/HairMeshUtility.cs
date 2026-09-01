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
