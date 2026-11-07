using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    // Working Normal snapshots are decoded tangent-space RGB and include underlying height relief.
    internal static class SurfaceNormalDetail
    {
        public static float Curvature(TexturePaintReadOnlyImage normal, TexturePaintReadOnlyMeshMap islands,
            float u, float v, float radius = 4f)
        {
            if (normal == null) return 0f;
            float du = Mathf.Max(1f / normal.width, radius / 2048f);
            float dv = Mathf.Max(1f / normal.height, radius / 2048f);
            Color centerId = islands?.GetPixelBilinear(u, v) ?? Color.white;
            Vector2 center = Slope(normal.GetPixelBilinear(u, v));
            Vector2 Sample(float x, float y)
            {
                if (x < 0f || x > 1f || y < 0f || y > 1f) return center;
                if (islands != null)
                {
                    Color neighbor = islands.GetPixelBilinear(x, y);
                    if (centerId.a < .5f || neighbor.a < .5f ||
                        Mathf.Abs(centerId.g - neighbor.g) > .1f ||
                        Mathf.Abs(centerId.b - neighbor.b) > .1f) return center;
                }
                return Slope(normal.GetPixelBilinear(x, y));
            }
            return (Sample(u + du, v).x - Sample(u - du, v).x +
                    Sample(u, v + dv).y - Sample(u, v - dv).y) * .5f;
        }

        private static Vector2 Slope(Color encoded) =>
            new Vector2(encoded.r * 2f - 1f, encoded.g * 2f - 1f) /
            Mathf.Max(.2f, encoded.b * 2f - 1f);
    }
}
