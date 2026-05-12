using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.TextureUtilities
{
    public sealed class Bezier2DPoint
    {
        public Vector2 position;
        public Vector2 inHandle;
        public Vector2 outHandle;

        public Bezier2DPoint(Vector2 position, Vector2 inHandle, Vector2 outHandle)
        {
            this.position = position;
            this.inHandle = inHandle;
            this.outHandle = outHandle;
        }

        public Bezier2DPoint Clone()
        {
            return new Bezier2DPoint(position, inHandle, outHandle);
        }
    }

    public struct Bezier2DPathSample
    {
        public Vector2 position;
        public float distanceAlong;

        public Bezier2DPathSample(Vector2 position, float distanceAlong)
        {
            this.position = position;
            this.distanceAlong = distanceAlong;
        }
    }

    public static class Bezier2DPath
    {
        public const float CircleKappa = 0.55228475f;

        public static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float clampedT = Mathf.Clamp01(t);
            float oneMinusT = 1f - clampedT;
            return (oneMinusT * oneMinusT * oneMinusT * p0)
                + (3f * oneMinusT * oneMinusT * clampedT * p1)
                + (3f * oneMinusT * clampedT * clampedT * p2)
                + (clampedT * clampedT * clampedT * p3);
        }

        public static List<Vector2> SampleNormalized(IList<Bezier2DPoint> points, bool closed, int samplesPerSegment)
        {
            List<Vector2> sampled = new List<Vector2>();
            int pointCount = points == null ? 0 : points.Count;
            int minPoints = closed ? 3 : 2;
            if (pointCount < minPoints)
            {
                return sampled;
            }

            int segmentCount = closed ? pointCount : pointCount - 1;
            int clampedSamples = Mathf.Max(2, samplesPerSegment);
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                Bezier2DPoint point = points[segmentIndex];
                Bezier2DPoint nextPoint = points[(segmentIndex + 1) % pointCount];
                if (segmentIndex == 0)
                {
                    AddIfDistinct(sampled, Clamp01(point.position));
                }

                for (int sampleIndex = 1; sampleIndex <= clampedSamples; sampleIndex++)
                {
                    float t = (float)sampleIndex / clampedSamples;
                    AddIfDistinct(sampled, Clamp01(EvaluateCubic(point.position, point.outHandle, nextPoint.inHandle, nextPoint.position, t)));
                }
            }

            if (closed)
            {
                RemoveDuplicateClosingPoint(sampled);
            }

            return sampled;
        }

        public static List<Bezier2DPathSample> SamplePixels(IList<Bezier2DPoint> points, bool closed, int textureWidth, int textureHeight, int samplesPerSegment)
        {
            List<Vector2> normalized = SampleNormalized(points, closed, samplesPerSegment);
            List<Bezier2DPathSample> sampled = new List<Bezier2DPathSample>(normalized.Count);
            float distance = 0f;
            Vector2 previous = Vector2.zero;
            for (int pointIndex = 0; pointIndex < normalized.Count; pointIndex++)
            {
                Vector2 pixelPoint = NormalizedToPixel(normalized[pointIndex], textureWidth, textureHeight);
                if (pointIndex > 0)
                {
                    distance += Vector2.Distance(previous, pixelPoint);
                }

                sampled.Add(new Bezier2DPathSample(pixelPoint, distance));
                previous = pixelPoint;
            }

            return sampled;
        }

        public static float GetSampledLength(IList<Bezier2DPathSample> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0f;
            }

            return samples[samples.Count - 1].distanceAlong;
        }

        public static Bounds GetPixelBounds(IList<Bezier2DPathSample> samples, float paddingPixels, int textureWidth, int textureHeight)
        {
            if (samples == null || samples.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            float minX = textureWidth;
            float minY = textureHeight;
            float maxX = 0f;
            float maxY = 0f;
            for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
            {
                Vector2 point = samples[sampleIndex].position;
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }

            minX = Mathf.Clamp(minX - paddingPixels, 0f, textureWidth - 1f);
            minY = Mathf.Clamp(minY - paddingPixels, 0f, textureHeight - 1f);
            maxX = Mathf.Clamp(maxX + paddingPixels, 0f, textureWidth - 1f);
            maxY = Mathf.Clamp(maxY + paddingPixels, 0f, textureHeight - 1f);

            Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
            Vector3 size = new Vector3(Mathf.Max(0f, maxX - minX), Mathf.Max(0f, maxY - minY), 0f);
            return new Bounds(center, size);
        }

        public static float DistancePointToSegmentSquared(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            return DistancePointToSegmentSquared(point, segmentStart, segmentEnd, out _);
        }

        public static float DistancePointToSegmentSquared(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd, out float segmentT)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 1e-6f)
            {
                segmentT = 0f;
                return (point - segmentStart).sqrMagnitude;
            }

            segmentT = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 closest = segmentStart + (segment * segmentT);
            return (point - closest).sqrMagnitude;
        }

        public static bool TryFindClosestSampleSegment(IList<Bezier2DPathSample> samples, Vector2 point, out float distanceSquared, out float distanceAlong)
        {
            return TryFindClosestSampleSegment(samples, point, out distanceSquared, out distanceAlong, out _);
        }

        public static bool TryFindClosestSampleSegment(IList<Bezier2DPathSample> samples, Vector2 point, out float distanceSquared, out float distanceAlong, out float signedDistance)
        {
            distanceSquared = float.MaxValue;
            distanceAlong = 0f;
            signedDistance = 0f;
            if (samples == null || samples.Count < 2)
            {
                return false;
            }

            for (int sampleIndex = 1; sampleIndex < samples.Count; sampleIndex++)
            {
                Bezier2DPathSample previous = samples[sampleIndex - 1];
                Bezier2DPathSample current = samples[sampleIndex];
                float segmentDistanceSquared = DistancePointToSegmentSquared(point, previous.position, current.position, out float segmentT);
                if (segmentDistanceSquared >= distanceSquared)
                {
                    continue;
                }

                distanceSquared = segmentDistanceSquared;
                distanceAlong = Mathf.Lerp(previous.distanceAlong, current.distanceAlong, segmentT);
                signedDistance = GetSignedDistanceToSegment(point, previous.position, current.position, segmentDistanceSquared, segmentT);
            }

            return true;
        }

        private static float GetSignedDistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd, float distanceSquared, float segmentT)
        {
            Vector2 segment = segmentEnd - segmentStart;
            if (segment.sqrMagnitude <= 1e-6f)
            {
                return Mathf.Sqrt(distanceSquared);
            }

            Vector2 closest = segmentStart + (segment * segmentT);
            Vector2 offset = point - closest;
            float cross = (segment.x * offset.y) - (segment.y * offset.x);
            return Mathf.Sqrt(distanceSquared) * (cross >= 0f ? 1f : -1f);
        }

        public static Vector2 NormalizedToPixel(Vector2 normalizedPoint, int textureWidth, int textureHeight)
        {
            return new Vector2(normalizedPoint.x * textureWidth, normalizedPoint.y * textureHeight);
        }

        public static Vector2 Clamp01(Vector2 point)
        {
            point.x = Mathf.Clamp01(point.x);
            point.y = Mathf.Clamp01(point.y);
            return point;
        }

        public static void ResetToDefaultCircle(List<Bezier2DPoint> points, int textureWidth, int textureHeight, float radiusScale)
        {
            points.Clear();
            if (textureWidth <= 0 || textureHeight <= 0)
            {
                return;
            }

            float radiusPixels = Mathf.Max(8f, Mathf.Min(textureWidth, textureHeight) * Mathf.Clamp(radiusScale, 0.01f, 0.49f));
            float radiusX = radiusPixels / Mathf.Max(1f, textureWidth);
            float radiusY = radiusPixels / Mathf.Max(1f, textureHeight);
            float handleX = radiusX * CircleKappa;
            float handleY = radiusY * CircleKappa;
            Vector2 center = new Vector2(0.5f, 0.5f);

            Vector2 right = Clamp01(new Vector2(center.x + radiusX, center.y));
            Vector2 top = Clamp01(new Vector2(center.x, center.y + radiusY));
            Vector2 left = Clamp01(new Vector2(center.x - radiusX, center.y));
            Vector2 bottom = Clamp01(new Vector2(center.x, center.y - radiusY));

            points.Add(new Bezier2DPoint(right, Clamp01(new Vector2(right.x, right.y - handleY)), Clamp01(new Vector2(right.x, right.y + handleY))));
            points.Add(new Bezier2DPoint(top, Clamp01(new Vector2(top.x + handleX, top.y)), Clamp01(new Vector2(top.x - handleX, top.y))));
            points.Add(new Bezier2DPoint(left, Clamp01(new Vector2(left.x, left.y + handleY)), Clamp01(new Vector2(left.x, left.y - handleY))));
            points.Add(new Bezier2DPoint(bottom, Clamp01(new Vector2(bottom.x - handleX, bottom.y)), Clamp01(new Vector2(bottom.x + handleX, bottom.y))));
        }

        public static void ResetToDefaultPath(List<Bezier2DPoint> points)
        {
            points.Clear();
            points.Add(new Bezier2DPoint(new Vector2(0.22f, 0.5f), new Vector2(0.16f, 0.5f), new Vector2(0.36f, 0.66f)));
            points.Add(new Bezier2DPoint(new Vector2(0.78f, 0.5f), new Vector2(0.64f, 0.34f), new Vector2(0.84f, 0.5f)));
        }

        private static void AddIfDistinct(List<Vector2> points, Vector2 point)
        {
            if (points.Count == 0 || (points[points.Count - 1] - point).sqrMagnitude > 1e-8f)
            {
                points.Add(point);
            }
        }

        private static void RemoveDuplicateClosingPoint(List<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            if ((points[0] - points[points.Count - 1]).sqrMagnitude <= 1e-8f)
            {
                points.RemoveAt(points.Count - 1);
            }
        }
    }
}
