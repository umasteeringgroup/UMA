using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>UV-space geometry inputs shared by generators and model plugins.</summary>
    public sealed class ProceduralMeshMaps : IDisposable
    {
        public Texture2D position;
        public Texture2D worldNormal;
        public Texture2D curvature;
        public Texture2D ambientOcclusion;
        public Texture2D thickness;
        public Texture2D id;

        public void Dispose()
        {
            Destroy(position); Destroy(worldNormal); Destroy(curvature);
            Destroy(ambientOcclusion); Destroy(thickness); Destroy(id);
            position = worldNormal = curvature = ambientOcclusion = thickness = id = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }

    public static class ProceduralMeshMapBuilder
    {
        public static ProceduralMeshMaps Build(ReconstructedSurface surface, int width, int height,
            TexturePaintOperationContext operation = default)
        {
            if (surface?.mesh == null) throw new ArgumentNullException(nameof(surface));
            Mesh mesh = surface.mesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            if (vertices.Length == 0 || uv.Length != vertices.Length || triangles.Length < 3)
                throw new ArgumentException("Mesh maps require vertices, UV0, and triangles.", nameof(surface));
            if (normals.Length != vertices.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            Transform transform = surface.gameObject != null ? surface.gameObject.transform : null;
            float[] vertexCurvature = BuildVertexCurvature(normals, triangles);
            float[] vertexThickness = BuildVertexThickness(vertices, normals, mesh.bounds, transform);
            Color[] positions = new Color[width * height];
            Color[] worldNormals = Fill(width * height, new Color(0.5f, 0.5f, 1f, 0f));
            Color[] curvatures = new Color[width * height];
            Color[] ao = new Color[width * height];
            Color[] thickness = new Color[width * height];
            Color[] ids = new Color[width * height];

            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            {
                if ((triangle & 63) == 0)
                {
                    operation.ThrowIfCancellationRequested();
                    operation.Report(triangle / (float)Mathf.Max(1, triangles.Length / 3));
                }
                int offset = triangle * 3;
                int ia = triangles[offset], ib = triangles[offset + 1], ic = triangles[offset + 2];
                int island = surface.triangleIslands != null && triangle < surface.triangleIslands.Length
                    ? surface.triangleIslands[triangle] : -1;
                RasterizeTriangle(uv[ia], uv[ib], uv[ic], width, height, (x, y, barycentric) =>
                {
                    Vector3 localPosition = vertices[ia] * barycentric.x + vertices[ib] * barycentric.y + vertices[ic] * barycentric.z;
                    Vector3 localNormal = (normals[ia] * barycentric.x + normals[ib] * barycentric.y + normals[ic] * barycentric.z).normalized;
                    Vector3 worldPosition = transform != null ? transform.TransformPoint(localPosition) : localPosition;
                    Vector3 normal = transform != null ? transform.TransformDirection(localNormal).normalized : localNormal;
                    float curve = Mathf.Clamp01(vertexCurvature[ia] * barycentric.x + vertexCurvature[ib] * barycentric.y + vertexCurvature[ic] * barycentric.z);
                    float thick = Mathf.Max(0f, vertexThickness[ia] * barycentric.x + vertexThickness[ib] * barycentric.y + vertexThickness[ic] * barycentric.z);
                    int index = y * width + x;
                    positions[index] = new Color(worldPosition.x, worldPosition.y, worldPosition.z, 1f);
                    worldNormals[index] = Encode(normal, 1f);
                    curvatures[index] = new Color(curve, curve, curve, 1f);
                    float accessibility = Mathf.Clamp01(1f - curve * 4f);
                    ao[index] = new Color(accessibility, accessibility, accessibility, 1f);
                    thickness[index] = new Color(thick, thick, thick, 1f);
                    ids[index] = new Color(triangle, surface.index, island, 1f);
                });
            }

            operation.Report(1f);

            return new ProceduralMeshMaps
            {
                position = Create("World Position Map", width, height, positions, TextureFormat.RGBAFloat),
                worldNormal = Create("World Normal Map", width, height, worldNormals, TextureFormat.RGBAHalf),
                curvature = Create("Curvature Map", width, height, curvatures, TextureFormat.RHalf),
                ambientOcclusion = Create("Ambient Occlusion Map", width, height, ao, TextureFormat.RHalf),
                thickness = Create("Thickness Map", width, height, thickness, TextureFormat.RHalf),
                id = Create("Mesh ID Map", width, height, ids, TextureFormat.RGBAFloat)
            };
        }

        private static float[] BuildVertexCurvature(Vector3[] normals, int[] triangles)
        {
            var neighbors = new HashSet<int>[normals.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                AddNeighbor(neighbors, triangles[i], triangles[i + 1]);
                AddNeighbor(neighbors, triangles[i + 1], triangles[i + 2]);
                AddNeighbor(neighbors, triangles[i + 2], triangles[i]);
            }
            float[] result = new float[normals.Length];
            for (int vertex = 0; vertex < result.Length; vertex++)
            {
                HashSet<int> adjacent = neighbors[vertex];
                if (adjacent == null || adjacent.Count == 0) continue;
                float sum = 0f;
                foreach (int other in adjacent) sum += 1f - Mathf.Clamp(Vector3.Dot(normals[vertex].normalized, normals[other].normalized), -1f, 1f);
                result[vertex] = Mathf.Clamp01(sum / adjacent.Count * 2f);
            }
            return result;
        }

        private static void AddNeighbor(HashSet<int>[] neighbors, int a, int b)
        {
            (neighbors[a] ??= new HashSet<int>()).Add(b);
            (neighbors[b] ??= new HashSet<int>()).Add(a);
        }

        private static float[] BuildVertexThickness(Vector3[] vertices, Vector3[] normals, Bounds bounds, Transform transform)
        {
            float[] result = new float[vertices.Length];
            float scale = transform != null ? (Mathf.Abs(transform.lossyScale.x) + Mathf.Abs(transform.lossyScale.y) + Mathf.Abs(transform.lossyScale.z)) / 3f : 1f;
            for (int i = 0; i < vertices.Length; i++) result[i] = RayBoxExit(vertices[i], -normals[i].normalized, bounds) * scale;
            return result;
        }

        private static float RayBoxExit(Vector3 origin, Vector3 direction, Bounds bounds)
        {
            float exit = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = direction[axis];
                if (Mathf.Abs(d) < 0.000001f) continue;
                float boundary = d > 0f ? bounds.max[axis] : bounds.min[axis];
                float distance = (boundary - origin[axis]) / d;
                if (distance >= 0f) exit = Mathf.Min(exit, distance);
            }
            return float.IsInfinity(exit) ? 0f : Mathf.Max(0f, exit);
        }

        private static void RasterizeTriangle(Vector2 a, Vector2 b, Vector2 c, int width, int height, Action<int, int, Vector3> write)
        {
            Vector2 pa = Vector2.Scale(a, new Vector2(width - 1, height - 1));
            Vector2 pb = Vector2.Scale(b, new Vector2(width - 1, height - 1));
            Vector2 pc = Vector2.Scale(c, new Vector2(width - 1, height - 1));
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))), 0, width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))), 0, width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))), 0, height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))), 0, height - 1);
            float denominator = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
            if (Mathf.Abs(denominator) < 0.0000001f) return;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float wa = ((pb.y - pc.y) * (p.x - pc.x) + (pc.x - pb.x) * (p.y - pc.y)) / denominator;
                float wb = ((pc.y - pa.y) * (p.x - pc.x) + (pa.x - pc.x) * (p.y - pc.y)) / denominator;
                float wc = 1f - wa - wb;
                if (wa >= -0.0001f && wb >= -0.0001f && wc >= -0.0001f) write(x, y, new Vector3(wa, wb, wc));
            }
        }

        private static Color Encode(Vector3 value, float alpha) => new Color(value.x * 0.5f + 0.5f, value.y * 0.5f + 0.5f, value.z * 0.5f + 0.5f, alpha);
        private static Color[] Fill(int count, Color value) { Color[] pixels = new Color[count]; for (int i = 0; i < count; i++) pixels[i] = value; return pixels; }
        private static Texture2D Create(string name, int width, int height, Color[] pixels, TextureFormat format)
        {
            Texture2D texture = new Texture2D(width, height, format, false, true)
            { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }
    }
}
