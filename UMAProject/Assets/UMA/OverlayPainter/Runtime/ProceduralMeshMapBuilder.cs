using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
            Matrix4x4 localToWorld = transform != null ? transform.localToWorldMatrix : Matrix4x4.identity;
            Matrix4x4 normalToWorld = localToWorld.inverse.transpose;
            float[] vertexCurvature = BuildVertexSignedCurvature(vertices, normals, triangles);
            var geometry = new TexturePaintGeometryQueries(vertices, triangles, localToWorld);
            Color[] positions = new Color[width * height];
            Color[] worldNormals = Fill(width * height, new Color(0.5f, 0.5f, 1f, 0f));
            Color[] curvatures = new Color[width * height];
            Color[] ao = new Color[width * height];
            Color[] thickness = new Color[width * height];
            Color[] ids = new Color[width * height];
            float[] distances = new float[width * height];
            Array.Fill(distances, float.PositiveInfinity);

            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            {
                if ((triangle & 63) == 0)
                {
                    operation.ThrowIfCancellationRequested();
                    operation.Report(.2f * triangle / Mathf.Max(1, triangles.Length / 3));
                }
                int offset = triangle * 3;
                int ia = triangles[offset], ib = triangles[offset + 1], ic = triangles[offset + 2];
                int island = surface.triangleIslands != null && triangle < surface.triangleIslands.Length
                    ? surface.triangleIslands[triangle] : -1;
                RasterizeTriangle(uv[ia], uv[ib], uv[ic], width, height, distances, (x, y, barycentric) =>
                {
                    Vector3 localPosition = vertices[ia] * barycentric.x + vertices[ib] * barycentric.y + vertices[ic] * barycentric.z;
                    Vector3 localNormal = (normals[ia] * barycentric.x + normals[ib] * barycentric.y + normals[ic] * barycentric.z).normalized;
                    Vector3 worldPosition = localToWorld.MultiplyPoint3x4(localPosition);
                    Vector3 normal = normalToWorld.MultiplyVector(localNormal).normalized;
                    float curve = Mathf.Clamp(vertexCurvature[ia] * barycentric.x +
                        vertexCurvature[ib] * barycentric.y + vertexCurvature[ic] * barycentric.z,
                        -1f, 1f);
                    int index = y * width + x;
                    positions[index] = new Color(worldPosition.x, worldPosition.y, worldPosition.z, 1f);
                    worldNormals[index] = Encode(normal, 1f);
                    float encodedCurvature = curve * 0.5f + 0.5f;
                    curvatures[index] = new Color(encodedCurvature, encodedCurvature,
                        encodedCurvature, 1f);
                    ids[index] = new Color(triangle, surface.index, island, 1f);
                });
            }

            float extent = Mathf.Max(.0001f, geometry.Bounds.size.magnitude);
            float bias = Mathf.Max(1e-7f, extent * .00001f);
            // Bake at covered texels, not vertices: a large flat polygon can still sit under
            // an overhang. Chunking bounds cancellation latency and reports ordered progress.
            for (int start = 0; start < height; start += 8)
            {
                int end = Math.Min(height, start + 8);
                Parallel.For(start, end, new ParallelOptions { CancellationToken = operation.cancellationToken }, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        if (ids[i].a < .5f) continue;
                        Color p = positions[i], n = worldNormals[i];
                        Vector3 position = new Vector3(p.r, p.g, p.b);
                        Vector3 normal = new Vector3(n.r * 2 - 1, n.g * 2 - 1, n.b * 2 - 1);
                        float accessibility = geometry.Occlusion(position, normal, 32, extent * .25f, bias);
                        float thick = geometry.Thickness(position, normal, extent, bias);
                        ao[i] = new Color(accessibility, accessibility, accessibility, 1);
                        thickness[i] = new Color(thick, thick, thick, 1);
                    }
                });
                operation.Report(.2f + .8f * end / height);
            }

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

        internal static float[] BuildVertexSignedCurvature(Vector3[] vertices, Vector3[] normals,
            int[] triangles)
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
                Vector3 originNormal = normals[vertex].normalized;
                foreach (int other in adjacent)
                {
                    Vector3 edge = vertices[other] - vertices[vertex];
                    if (edge.sqrMagnitude <= 0.0000000001f) continue;
                    Vector3 normalDelta = normals[other].normalized - originNormal;
                    // dN/ds projected along the edge tangent is positive on a convex bend and
                    // negative on a concave bend for outward-facing mesh normals. Using bend angle
                    // instead of inverse edge length keeps the normalized map stable across scale.
                    sum += Vector3.Dot(normalDelta, edge.normalized);
                }
                result[vertex] = Mathf.Clamp(sum / adjacent.Count * 4f, -1f, 1f);
            }
            return result;
        }

        private static void AddNeighbor(HashSet<int>[] neighbors, int a, int b)
        {
            (neighbors[a] ??= new HashSet<int>()).Add(b);
            (neighbors[b] ??= new HashSet<int>()).Add(a);
        }

        internal static void RasterizeTriangle(Vector2 a, Vector2 b, Vector2 c, int width, int height,
            float[] distances, Action<int, int, Vector3> write, bool tightRows = false)
        {
            // UVs describe texel edges; samples below are at texel centers. Using width - 1
            // shrinks the mesh map and leaves valid geometry uncovered along island seams.
            Vector2 size = new Vector2(width, height);
            Vector2 pa = Vector2.Scale(a, size);
            Vector2 pb = Vector2.Scale(b, size);
            Vector2 pc = Vector2.Scale(c, size);
            // Supply a small guard band for bilinear filtering and higher-resolution output.
            // The destination geometry mask remains responsible for clipping the final layer.
            const float padding = 3f;
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)) - padding), 0, width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x)) + padding), 0, width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) - padding), 0, height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y)) + padding), 0, height - 1);
            float denominator = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
            if (Mathf.Abs(denominator) < 0.0000001f) return;
            for (int y = minY; y <= maxY; y++)
            {
                int rowMin = minX, rowMax = maxX;
                if (tightRows)
                {
                    // Only visit the triangle's horizontal strip plus its guard band. Thin,
                    // diagonal UV triangles otherwise scan most of the texture unnecessarily.
                    float left = float.PositiveInfinity, right = float.NegativeInfinity;
                    ClipEdge(pa, pb, y + .5f - padding, y + .5f + padding, ref left, ref right);
                    ClipEdge(pb, pc, y + .5f - padding, y + .5f + padding, ref left, ref right);
                    ClipEdge(pc, pa, y + .5f - padding, y + .5f + padding, ref left, ref right);
                    if (left > right) continue;
                    rowMin = Mathf.Max(minX, Mathf.FloorToInt(left - padding));
                    rowMax = Mathf.Min(maxX, Mathf.CeilToInt(right + padding));
                }
            for (int x = rowMin; x <= rowMax; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float wa = ((pb.y - pc.y) * (p.x - pc.x) + (pc.x - pb.x) * (p.y - pc.y)) / denominator;
                float wb = ((pc.y - pa.y) * (p.x - pc.x) + (pa.x - pc.x) * (p.y - pc.y)) / denominator;
                float wc = 1f - wa - wb;
                Vector3 barycentric = new Vector3(wa, wb, wc);
                float distance = 0f;
                if (wa < 0f || wb < 0f || wc < 0f)
                {
                    // Sample the nearest point on the triangle, never empty map pixels or
                    // extrapolated positions. Tiny and mirrored triangles work the same way.
                    distance = float.PositiveInfinity;
                    ClosestEdge(p, pa, pb, Vector3.right, Vector3.up, ref distance, ref barycentric);
                    ClosestEdge(p, pb, pc, Vector3.up, Vector3.forward, ref distance, ref barycentric);
                    ClosestEdge(p, pc, pa, Vector3.forward, Vector3.right, ref distance, ref barycentric);
                }
                int index = y * width + x;
                if (distance > padding * padding || distance > distances[index]) continue;
                // Padding must never overwrite a real sample from another triangle/island.
                distances[index] = distance;
                write(x, y, barycentric);
            }
            }
        }

        private static void ClipEdge(Vector2 a, Vector2 b, float low, float high, ref float left, ref float right)
        {
            float dy = b.y - a.y;
            if (Mathf.Abs(dy) < .000001f)
            {
                if (a.y < low || a.y > high) return;
                left = Mathf.Min(left, Mathf.Min(a.x, b.x)); right = Mathf.Max(right, Mathf.Max(a.x, b.x)); return;
            }
            float start = (low - a.y) / dy, end = (high - a.y) / dy;
            if (start > end) (start, end) = (end, start);
            start = Mathf.Max(0, start); end = Mathf.Min(1, end); if (start > end) return;
            float x0 = Mathf.LerpUnclamped(a.x, b.x, start), x1 = Mathf.LerpUnclamped(a.x, b.x, end);
            left = Mathf.Min(left, Mathf.Min(x0, x1)); right = Mathf.Max(right, Mathf.Max(x0, x1));
        }

        private static void ClosestEdge(Vector2 point, Vector2 a, Vector2 b,
            Vector3 weightsA, Vector3 weightsB, ref float distance, ref Vector3 barycentric)
        {
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            float candidate = (point - (a + edge * t)).sqrMagnitude;
            if (candidate >= distance) return;
            distance = candidate;
            barycentric = Vector3.LerpUnclamped(weightsA, weightsB, t);
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
