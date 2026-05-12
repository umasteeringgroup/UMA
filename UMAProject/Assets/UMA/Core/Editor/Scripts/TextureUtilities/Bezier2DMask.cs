using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.TextureUtilities
{
    public sealed class Bezier2DMask
    {
        public int minX;
        public int maxX;
        public int minY;
        public int maxY;
        public int boxWidth;
        public int boxHeight;
        public int insidePixelCount;
        public byte[] strengths;

        public float GetStrength(int x, int y)
        {
            if (strengths == null || x < minX || x > maxX || y < minY || y > maxY)
            {
                return 0f;
            }

            int index = ((y - minY) * boxWidth) + (x - minX);
            return strengths[index] * (1f / 255f);
        }

        public static Bezier2DMask Build(IList<Bezier2DPoint> points, int textureWidth, int textureHeight, float falloffDistancePixels, int samplesPerSegment)
        {
            Bezier2DMask mask = new Bezier2DMask();
            List<Vector2> boundaryPixels = Bezier2DPath.SampleNormalized(points, true, samplesPerSegment);
            if (boundaryPixels.Count < 3 || textureWidth <= 0 || textureHeight <= 0)
            {
                mask.strengths = new byte[0];
                return mask;
            }

            for (int pointIndex = 0; pointIndex < boundaryPixels.Count; pointIndex++)
            {
                boundaryPixels[pointIndex] = Bezier2DPath.NormalizedToPixel(boundaryPixels[pointIndex], textureWidth, textureHeight);
            }

            float minXFloat = textureWidth;
            float maxXFloat = 0f;
            float minYFloat = textureHeight;
            float maxYFloat = 0f;
            for (int pointIndex = 0; pointIndex < boundaryPixels.Count; pointIndex++)
            {
                Vector2 point = boundaryPixels[pointIndex];
                minXFloat = Mathf.Min(minXFloat, point.x);
                maxXFloat = Mathf.Max(maxXFloat, point.x);
                minYFloat = Mathf.Min(minYFloat, point.y);
                maxYFloat = Mathf.Max(maxYFloat, point.y);
            }

            float padding = Mathf.Max(1f, falloffDistancePixels) + 1f;
            mask.minX = Mathf.Clamp(Mathf.FloorToInt(minXFloat - padding), 0, textureWidth - 1);
            mask.maxX = Mathf.Clamp(Mathf.CeilToInt(maxXFloat + padding), 0, textureWidth - 1);
            mask.minY = Mathf.Clamp(Mathf.FloorToInt(minYFloat - padding), 0, textureHeight - 1);
            mask.maxY = Mathf.Clamp(Mathf.CeilToInt(maxYFloat + padding), 0, textureHeight - 1);
            mask.boxWidth = Mathf.Max(0, mask.maxX - mask.minX + 1);
            mask.boxHeight = Mathf.Max(0, mask.maxY - mask.minY + 1);
            mask.strengths = new byte[mask.boxWidth * mask.boxHeight];

            if (mask.boxWidth == 0 || mask.boxHeight == 0)
            {
                return mask;
            }

            float clampedFalloff = Mathf.Max(0f, falloffDistancePixels);
            for (int y = mask.minY; y <= mask.maxY; y++)
            {
                for (int x = mask.minX; x <= mask.maxX; x++)
                {
                    Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                    if (!IsPointInsidePolygon(pixelCenter, boundaryPixels))
                    {
                        continue;
                    }

                    float strength = clampedFalloff <= 0.001f ? 1f : GetBoundaryFalloffStrength(pixelCenter, boundaryPixels, clampedFalloff);
                    if (strength <= 0f)
                    {
                        continue;
                    }

                    int strengthIndex = ((y - mask.minY) * mask.boxWidth) + (x - mask.minX);
                    mask.strengths[strengthIndex] = (byte)Mathf.Clamp(Mathf.RoundToInt(strength * 255f), 0, 255);
                    mask.insidePixelCount++;
                }
            }

            return mask;
        }

        public static bool IsPointInsidePolygon(Vector2 point, IList<Vector2> polygon)
        {
            bool inside = false;
            int count = polygon == null ? 0 : polygon.Count;
            if (count < 3)
            {
                return false;
            }

            int previousIndex = count - 1;
            for (int pointIndex = 0; pointIndex < count; pointIndex++)
            {
                Vector2 current = polygon[pointIndex];
                Vector2 previous = polygon[previousIndex];
                bool crosses = (current.y > point.y) != (previous.y > point.y);
                if (crosses)
                {
                    float intersectionX = ((previous.x - current.x) * (point.y - current.y) / (previous.y - current.y)) + current.x;
                    if (point.x < intersectionX)
                    {
                        inside = !inside;
                    }
                }

                previousIndex = pointIndex;
            }

            return inside;
        }

        public static float GetBoundaryFalloffStrength(Vector2 point, IList<Vector2> boundaryPixels, float falloffDistance)
        {
            if (boundaryPixels == null || boundaryPixels.Count == 0)
            {
                return 0f;
            }

            if (falloffDistance <= 0.001f)
            {
                return 1f;
            }

            float maxDistanceSquared = falloffDistance * falloffDistance;
            float minDistanceSquared = maxDistanceSquared;
            int count = boundaryPixels.Count;
            for (int pointIndex = 0; pointIndex < count; pointIndex++)
            {
                Vector2 start = boundaryPixels[pointIndex];
                Vector2 end = boundaryPixels[(pointIndex + 1) % count];
                float distanceSquared = Bezier2DPath.DistancePointToSegmentSquared(point, start, end);
                if (distanceSquared < minDistanceSquared)
                {
                    minDistanceSquared = distanceSquared;
                }
            }

            return Mathf.Clamp01(Mathf.Sqrt(minDistanceSquared) / falloffDistance);
        }
    }
}
