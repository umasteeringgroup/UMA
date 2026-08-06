using UnityEngine;

namespace UMA.TexturePaint
{
    internal static class TexturePaintGeometryMask
    {
        public static Texture2D Build(ReconstructedSurface surface, int width, int height, string slotName,
            int uvIsland, TexturePaintMaskStack masks)
        {
            Texture2D result = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = $"{surface?.index} {slotName} Island {uvIsland} Geometry Mask",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            byte[] pixels = new byte[width * height];
            if (surface?.mesh == null)
            {
                result.LoadRawTextureData(pixels);
                result.Apply(false, false);
                return result;
            }

            int[] triangles = surface.mesh.triangles;
            Vector2[] uv = surface.mesh.uv;
            Vector3[] vertices = surface.mesh.vertices;
            int triangleCount = triangles.Length / 3;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int island = surface.triangleIslands != null && triangle < surface.triangleIslands.Length
                    ? surface.triangleIslands[triangle] : -1;
                if (uvIsland >= 0 && island != uvIsland) continue;
                string triangleSlot = surface.GetTriangleSlotName(triangle);
                if (!string.IsNullOrEmpty(slotName) && !string.Equals(slotName, triangleSlot, System.StringComparison.Ordinal)) continue;
                int offset = triangle * 3;
                int ia = triangles[offset], ib = triangles[offset + 1], ic = triangles[offset + 2];
                if ((uint)ia >= (uint)uv.Length || (uint)ib >= (uint)uv.Length || (uint)ic >= (uint)uv.Length) continue;
                Vector2 centerUV = (uv[ia] + uv[ib] + uv[ic]) / 3f;
                Vector3 centerWorld = surface.gameObject != null
                    ? surface.gameObject.transform.TransformPoint((vertices[ia] + vertices[ib] + vertices[ic]) / 3f)
                    : Vector3.zero;
                if (masks != null && !masks.AllowsStructural(surface.index, triangle, island, surface, centerUV, centerWorld)) continue;
                RasterizeTriangle(pixels, width, height, uv[ia], uv[ib], uv[ic]);
            }
            if (masks != null)
            {
                string surfaceId = surface.index.ToString();
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int pixel = y * width + x;
                    if (pixels[pixel] == 0) continue;
                    float value = masks.EvaluateTextureMasks(surface.index, surfaceId,
                        new Vector2((x + 0.5f) / width, (y + 0.5f) / height));
                    pixels[pixel] = (byte)Mathf.RoundToInt(value * 255f);
                }
            }
            result.LoadRawTextureData(pixels);
            result.Apply(false, false);
            return result;
        }

        internal static void RasterizeTriangle(byte[] pixels, int width, int height, Vector2 a, Vector2 b, Vector2 c)
        {
            int xMin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * width) - 1, 0, width - 1);
            int yMin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * height) - 1, 0, height - 1);
            int xMax = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * width) + 1, 0, width - 1);
            int yMax = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * height) + 1, 0, height - 1);
            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 0.000000001f) return;
            float orientation = area >= 0f ? 1f : -1f;
            Vector2 halfTexel = new Vector2(0.5f / width, 0.5f / height);
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                Vector2 point = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                if (InsideConservativeEdge(a, b, point, halfTexel, orientation) &&
                    InsideConservativeEdge(b, c, point, halfTexel, orientation) &&
                    InsideConservativeEdge(c, a, point, halfTexel, orientation))
                    pixels[y * width + x] = byte.MaxValue;
            }
        }

        private static bool InsideConservativeEdge(Vector2 a, Vector2 b, Vector2 point,
            Vector2 halfTexel, float orientation)
        {
            Vector2 edge = b - a;
            float edgeValue = orientation * Edge(a, b, point);
            float texelExtent = Mathf.Abs(edge.y) * halfTexel.x + Mathf.Abs(edge.x) * halfTexel.y;
            return edgeValue >= -texelExtent - 0.000000001f;
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            Vector2 ap = point - a;
            return ab.x * ap.y - ab.y * ap.x;
        }
    }
}
