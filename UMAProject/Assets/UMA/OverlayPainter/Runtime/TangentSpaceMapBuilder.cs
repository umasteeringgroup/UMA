using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public sealed class TangentSpaceMaps : IDisposable
    {
        public Texture2D vertexNormals;
        public Texture2D tangents;
        public Texture2D seamLookup;
        internal Action release;
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (release != null)
            {
                release();
                release = null;
                vertexNormals = null; tangents = null; seamLookup = null;
                return;
            }
            Destroy(vertexNormals); Destroy(tangents); Destroy(seamLookup);
            vertexNormals = null; tangents = null; seamLookup = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }

    public static class TangentSpaceMapBuilder
    {
        private sealed class CacheEntry
        {
            public Texture2D normals;
            public Texture2D tangents;
            public Texture2D seams;
            public int references;
        }

        private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        public static int CachedMapCount => cache.Count;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ClearCache();
        }

        public static void ClearCache()
        {
            foreach (CacheEntry entry in cache.Values)
            {
                if (entry == null)
                {
                    continue;
                }
                Destroy(entry.normals);
                Destroy(entry.tangents);
                Destroy(entry.seams);
            }
            cache.Clear();
        }

        public static TangentSpaceMaps Build(Mesh mesh, int width, int height, int seamWidth = 2,
            TexturePaintOperationContext operation = default)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            operation.ThrowIfCancellationRequested();
            string key = BuildCacheKey(mesh, width, height, seamWidth);
            if (!cache.TryGetValue(key, out CacheEntry entry))
            {
                operation.ThrowIfCancellationRequested();
                TangentSpaceMaps built = BuildUncached(mesh, width, height, seamWidth, operation);
                entry = new CacheEntry { normals = built.vertexNormals, tangents = built.tangents, seams = built.seamLookup };
                built.vertexNormals = null; built.tangents = null; built.seamLookup = null;
                cache.Add(key, entry);
            }
            entry.references++;
            return new TangentSpaceMaps
            {
                vertexNormals = entry.normals,
                tangents = entry.tangents,
                seamLookup = entry.seams,
                release = () => Release(key)
            };
        }

        private static TangentSpaceMaps BuildUncached(Mesh mesh, int width, int height, int seamWidth,
            TexturePaintOperationContext operation)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            Texture2D normalMap = null;
            Texture2D tangentMap = null;
            bool gpuBuilt = TryBuildVectorMapsGPU(mesh, width, height, out normalMap, out tangentMap);
            Color[] normalPixels = gpuBuilt ? null : Fill(width * height, new Color(0.5f, 0.5f, 1f, 0f));
            Color[] tangentPixels = gpuBuilt ? null : Fill(width * height, new Color(1f, 0.5f, 0.5f, 1f));
            Color[] seamPixels = new Color[width * height];
            if (!gpuBuilt)
            for (int tri = 0; tri < triangles.Length; tri += 3)
            {
                if ((tri & 255) == 0)
                {
                    operation.ThrowIfCancellationRequested();
                    operation.Report(tri / (float)Mathf.Max(1, triangles.Length) * 0.8f);
                }
                int ia = triangles[tri], ib = triangles[tri + 1], ic = triangles[tri + 2];
                RasterizeTriangle(uv[ia], uv[ib], uv[ic], width, height, (x, y, barycentric) =>
                {
                    Vector3 n = (normals[ia] * barycentric.x + normals[ib] * barycentric.y + normals[ic] * barycentric.z).normalized;
                    Vector4 t4 = tangents.Length == vertices.Length
                        ? tangents[ia] * barycentric.x + tangents[ib] * barycentric.y + tangents[ic] * barycentric.z
                        : new Vector4(1f, 0f, 0f, 1f);
                    Vector3 t = new Vector3(t4.x, t4.y, t4.z).normalized;
                    int index = y * width + x;
                    normalPixels[index] = Encode(n, 1f);
                    tangentPixels[index] = Encode(t, t4.w >= 0f ? 1f : 0f);
                });
            }
            BuildSeamLookup(vertices, uv, width, height, seamWidth, seamPixels, operation);
            operation.Report(1f);
            return new TangentSpaceMaps
            {
                vertexNormals = gpuBuilt ? normalMap : CreateTexture("Vertex Normal Map", width, height, normalPixels, TextureFormat.RGBAHalf),
                tangents = gpuBuilt ? tangentMap : CreateTexture("Vertex Tangent Map", width, height, tangentPixels, TextureFormat.RGBAHalf),
                seamLookup = CreateTexture("UV Seam Lookup", width, height, seamPixels, TextureFormat.RGBAFloat)
            };
        }

        private static bool TryBuildVectorMapsGPU(Mesh mesh, int width, int height,
            out Texture2D normalMap, out Texture2D tangentMap)
        {
            normalMap = null;
            tangentMap = null;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || SystemInfo.supportedRenderTargetCount < 2)
                return false;
            Shader shader = Shader.Find("Hidden/UMA/TexturePaint/UVTangentMaps");
            if (shader == null || !shader.isSupported) return false;
            Material material = null;
            RenderTexture normalRT = null;
            RenderTexture tangentRT = null;
            CommandBuffer command = null;
            try
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                normalRT = CreateMapTarget("Texture Paint GPU Vertex Normals", width, height);
                tangentRT = CreateMapTarget("Texture Paint GPU Vertex Tangents", width, height);
                command = new CommandBuffer { name = "Texture Paint Build Tangent Maps" };
                command.SetRenderTarget(normalRT);
                command.ClearRenderTarget(false, true, new Color(0.5f, 0.5f, 1f, 0f));
                command.SetRenderTarget(tangentRT);
                command.ClearRenderTarget(false, true, new Color(1f, 0.5f, 0.5f, 1f));
                var colors = new[] { new RenderTargetIdentifier(normalRT), new RenderTargetIdentifier(tangentRT) };
                command.SetRenderTarget(colors, new RenderTargetIdentifier(BuiltinRenderTextureType.None));
                command.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                Graphics.ExecuteCommandBuffer(command);
                normalMap = ReadMapTarget(normalRT, "Vertex Normal Map");
                tangentMap = ReadMapTarget(tangentRT, "Vertex Tangent Map");
                return true;
            }
            catch (Exception)
            {
                Destroy(normalMap); Destroy(tangentMap);
                normalMap = null; tangentMap = null;
                return false;
            }
            finally
            {
                command?.Release();
                if (normalRT != null) { normalRT.Release(); Destroy(normalRT); }
                if (tangentRT != null) { tangentRT.Release(); Destroy(tangentRT); }
                Destroy(material);
            }
        }

        private static RenderTexture CreateMapTarget(string name, int width, int height)
        {
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0)
            {
                sRGB = false,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            var target = new RenderTexture(descriptor) { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            target.Create();
            return target;
        }

        private static Texture2D ReadMapTarget(RenderTexture source, string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            texture.Apply(false, false);
            RenderTexture.active = previous;
            return texture;
        }

        private static void Release(string key)
        {
            if (!cache.TryGetValue(key, out CacheEntry entry)) return;
            entry.references--;
            if (entry.references > 0) return;
            Destroy(entry.normals); Destroy(entry.tangents); Destroy(entry.seams);
            cache.Remove(key);
        }

        private static string BuildCacheKey(Mesh mesh, int width, int height, int seamWidth)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                void Add(int value) { hash ^= (uint)value; hash *= 1099511628211UL; }
                Vector3[] vertices = mesh.vertices; Vector3[] normals = mesh.normals; Vector4[] tangents = mesh.tangents;
                Vector2[] uv = mesh.uv; int[] triangles = mesh.triangles;
                Add(vertices.Length); Add(normals.Length); Add(tangents.Length); Add(uv.Length); Add(triangles.Length);
                Add(width); Add(height); Add(seamWidth);
                for (int i = 0; i < vertices.Length; i++) { Add(vertices[i].x.GetHashCode()); Add(vertices[i].y.GetHashCode()); Add(vertices[i].z.GetHashCode()); }
                for (int i = 0; i < normals.Length; i++) { Add(normals[i].x.GetHashCode()); Add(normals[i].y.GetHashCode()); Add(normals[i].z.GetHashCode()); }
                for (int i = 0; i < tangents.Length; i++) { Add(tangents[i].x.GetHashCode()); Add(tangents[i].y.GetHashCode()); Add(tangents[i].z.GetHashCode()); Add(tangents[i].w.GetHashCode()); }
                for (int i = 0; i < uv.Length; i++) { Add(uv[i].x.GetHashCode()); Add(uv[i].y.GetHashCode()); }
                for (int i = 0; i < triangles.Length; i++) Add(triangles[i]);
                return hash.ToString("X16") + "|" + width + "x" + height + "|" + seamWidth;
            }
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }

        private static void RasterizeTriangle(Vector2 a, Vector2 b, Vector2 c, int width, int height, Action<int, int, Vector3> write)
        {
            Vector2 pa = new Vector2(a.x * (width - 1), a.y * (height - 1));
            Vector2 pb = new Vector2(b.x * (width - 1), b.y * (height - 1));
            Vector2 pc = new Vector2(c.x * (width - 1), c.y * (height - 1));
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

        private static void BuildSeamLookup(Vector3[] vertices, Vector2[] uv, int width, int height, int seamWidth,
            Color[] pixels, TexturePaintOperationContext operation)
        {
            Dictionary<Vector3Int, List<int>> coincident = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if ((i & 1023) == 0) operation.ThrowIfCancellationRequested();
                Vector3 v = vertices[i];
                Vector3Int key = new Vector3Int(Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
                if (!coincident.TryGetValue(key, out List<int> list)) coincident.Add(key, list = new List<int>());
                list.Add(i);
            }
            foreach (List<int> list in coincident.Values)
            {
                operation.ThrowIfCancellationRequested();
                if (list.Count < 2) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    int partner = -1;
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (i != j && Vector2.Distance(uv[list[i]], uv[list[j]]) > 0.0001f) { partner = list[j]; break; }
                    }
                    if (partner < 0) continue;
                    int cx = Mathf.RoundToInt(uv[list[i]].x * (width - 1));
                    int cy = Mathf.RoundToInt(uv[list[i]].y * (height - 1));
                    for (int oy = -seamWidth; oy <= seamWidth; oy++)
                    for (int ox = -seamWidth; ox <= seamWidth; ox++)
                    {
                        int x = cx + ox, y = cy + oy;
                        if ((uint)x >= (uint)width || (uint)y >= (uint)height) continue;
                        pixels[y * width + x] = new Color(uv[partner].x, uv[partner].y, 0f, 1f);
                    }
                }
            }
        }

        private static Color Encode(Vector3 value, float alpha) => new Color(value.x * 0.5f + 0.5f, value.y * 0.5f + 0.5f, value.z * 0.5f + 0.5f, alpha);
        private static Color[] Fill(int count, Color color) { Color[] values = new Color[count]; for (int i = 0; i < count; i++) values[i] = color; return values; }
        private static Texture2D CreateTexture(string name, int width, int height, Color[] pixels, TextureFormat format)
        {
            Texture2D texture = new Texture2D(width, height, format, false, true) { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }
    }
}
