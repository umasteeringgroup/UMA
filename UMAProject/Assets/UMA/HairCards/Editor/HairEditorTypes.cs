using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public enum HairWorkflowStep
    {
        Setup,
        Growth,
        Guides,
        Groom,
        Cards,
        Optimize,
        ValidateAndBake
    }

    public enum HairSceneTool
    {
        Select,
        PaintGrowth,
        PlaceGuide,
        DrawGuide,
        Comb,
        Grab,
        Smooth,
        Length,
        Cut,
        Width,
        Clump,
        Part,
        Freeze,
        Helper
    }

    public enum HairPreviewMode
    {
        Guides,
        GuidesAndChildren,
        Cards,
        GrowthMap,
        CardGroups,
        Wireframe
    }

    internal static class HairWorkflowState
    {
        internal static HairSceneTool DefaultTool(HairWorkflowStep step)
        {
            return step switch
            {
                HairWorkflowStep.Growth => HairSceneTool.PaintGrowth,
                HairWorkflowStep.Guides => HairSceneTool.Select,
                HairWorkflowStep.Groom => HairSceneTool.Comb,
                _ => HairSceneTool.Select
            };
        }

        internal static HairPreviewMode DefaultPreview(HairWorkflowStep step)
        {
            return step switch
            {
                HairWorkflowStep.Growth => HairPreviewMode.GrowthMap,
                HairWorkflowStep.Guides => HairPreviewMode.Guides,
                HairWorkflowStep.Groom => HairPreviewMode.GuidesAndChildren,
                _ => HairPreviewMode.Cards
            };
        }

        internal static bool IsToolAllowed(HairWorkflowStep step, HairSceneTool tool)
        {
            return step switch
            {
                HairWorkflowStep.Growth => tool == HairSceneTool.PaintGrowth || tool == HairSceneTool.Select,
                HairWorkflowStep.Guides => tool == HairSceneTool.Select || tool == HairSceneTool.PlaceGuide ||
                                           tool == HairSceneTool.DrawGuide,
                HairWorkflowStep.Groom => tool == HairSceneTool.Select || IsGroomTool(tool) ||
                                          tool == HairSceneTool.Helper,
                _ => tool == HairSceneTool.Select
            };
        }

        internal static HairWorkflowStep StepForTool(HairSceneTool tool, HairWorkflowStep current)
        {
            if (tool == HairSceneTool.PaintGrowth) return HairWorkflowStep.Growth;
            if (tool == HairSceneTool.PlaceGuide || tool == HairSceneTool.DrawGuide)
                return HairWorkflowStep.Guides;
            if (IsGroomTool(tool) || tool == HairSceneTool.Helper) return HairWorkflowStep.Groom;
            return current;
        }

        internal static bool IsGroomTool(HairSceneTool tool)
        {
            return tool >= HairSceneTool.Comb && tool <= HairSceneTool.Freeze;
        }
    }

    internal static class HairCurveBrushUtility
    {
        internal static bool TryClosestPoint(Ray ray, Vector3 segmentStart, Vector3 segmentEnd,
            out Vector3 segmentPoint, out float squareDistance)
        {
            segmentPoint = segmentStart;
            squareDistance = float.MaxValue;
            Vector3 rayDirection = ray.direction;
            float rayLengthSquare = rayDirection.sqrMagnitude;
            if (rayLengthSquare <= 1e-12f) return false;
            rayDirection /= Mathf.Sqrt(rayLengthSquare);

            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSquare = segment.sqrMagnitude;
            if (segmentLengthSquare <= 1e-12f)
            {
                float rayDistance = Mathf.Max(0f, Vector3.Dot(segmentStart - ray.origin, rayDirection));
                squareDistance = (segmentStart - ray.GetPoint(rayDistance)).sqrMagnitude;
                return true;
            }

            Vector3 originToStart = ray.origin - segmentStart;
            float raySegment = Vector3.Dot(rayDirection, segment);
            float rayOrigin = Vector3.Dot(rayDirection, originToStart);
            float segmentOrigin = Vector3.Dot(segment, originToStart);
            float denominator = segmentLengthSquare - raySegment * raySegment;
            float segmentT = denominator > 1e-12f
                ? (segmentOrigin - raySegment * rayOrigin) / denominator
                : 0f;

            if (segmentT < 0f)
                segmentT = 0f;
            else if (segmentT > 1f)
                segmentT = 1f;

            float closestRayDistance = Mathf.Max(0f,
                (raySegment * segmentT - rayOrigin));
            if (closestRayDistance <= 0f)
            {
                closestRayDistance = 0f;
                segmentT = Mathf.Clamp01(segmentOrigin / segmentLengthSquare);
            }

            segmentPoint = segmentStart + segment * segmentT;
            Vector3 rayPoint = ray.origin + rayDirection * closestRayDistance;
            squareDistance = (segmentPoint - rayPoint).sqrMagnitude;
            return true;
        }

        internal static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 segmentStart,
            Vector3 segmentEnd, out float segmentT)
        {
            Vector3 segment = segmentEnd - segmentStart;
            float squareLength = segment.sqrMagnitude;
            segmentT = squareLength > 1e-12f
                ? Mathf.Clamp01(Vector3.Dot(point - segmentStart, segment) / squareLength)
                : 0f;
            return segmentStart + segment * segmentT;
        }

        internal static Vector3 SamplePolyline(IReadOnlyList<Vector3> points, float normalizedPosition)
        {
            if (points == null || points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];
            float control = Mathf.Clamp01(normalizedPosition) * (points.Count - 1f);
            int left = Mathf.Min(Mathf.FloorToInt(control), points.Count - 1);
            int right = Mathf.Min(left + 1, points.Count - 1);
            return Vector3.LerpUnclamped(points[left], points[right], control - left);
        }

        internal static bool FillControlPointInfluences(IReadOnlyList<Vector3> displayedCurve,
            Vector3 center, float radius, float hardness, float[] influences)
        {
            return FillControlPointInfluences(displayedCurve, center, radius, hardness,
                Vector3.zero, false, influences);
        }

        internal static bool FillControlPointInfluences(IReadOnlyList<Vector3> displayedCurve,
            Vector3 center, float radius, float hardness, Vector3 projectionNormal,
            bool affectThroughDepth, float[] influences)
        {
            if (influences == null) return false;
            Array.Clear(influences, 0, influences.Length);
            if (displayedCurve == null || displayedCurve.Count < 2 || influences.Length < 2 ||
                radius <= 0f) return false;

            bool affected = false;
            int sampleDenominator = displayedCurve.Count - 1;
            float firstFalloff = HairBrushInteractionUtility.EvaluateFalloff(
                BrushDistance(displayedCurve[0] - center, projectionNormal, affectThroughDepth),
                radius, hardness);
            if (firstFalloff > 0f)
            {
                AccumulateInfluence(0f, firstFalloff, influences);
                affected = true;
            }
            for (int segmentEnd = 1; segmentEnd < displayedCurve.Count; segmentEnd++)
            {
                float pointFalloff = HairBrushInteractionUtility.EvaluateFalloff(
                    BrushDistance(displayedCurve[segmentEnd] - center, projectionNormal,
                        affectThroughDepth), radius, hardness);
                if (pointFalloff > 0f)
                {
                    AccumulateInfluence(segmentEnd / (float)sampleDenominator,
                        pointFalloff, influences);
                    affected = true;
                }
                ClosestPointForBrush(center, displayedCurve[segmentEnd - 1],
                    displayedCurve[segmentEnd], projectionNormal, affectThroughDepth,
                    out float segmentT, out float squareDistance);
                float falloff = HairBrushInteractionUtility.EvaluateFalloff(
                    Mathf.Sqrt(squareDistance), radius, hardness);
                if (falloff <= 0f) continue;
                float curvePosition = (segmentEnd - 1f + segmentT) / sampleDenominator;
                AccumulateInfluence(curvePosition, falloff, influences);
                affected = true;
            }
            return affected;
        }

        internal static Vector3 ClosestPointForBrush(Vector3 center, Vector3 segmentStart,
            Vector3 segmentEnd, Vector3 projectionNormal, bool affectThroughDepth,
            out float segmentT, out float squareDistance)
        {
            if (!affectThroughDepth || projectionNormal.sqrMagnitude <= 1e-12f)
            {
                Vector3 closest = ClosestPointOnSegment(center, segmentStart, segmentEnd, out segmentT);
                squareDistance = (closest - center).sqrMagnitude;
                return closest;
            }

            Vector3 normal = projectionNormal.normalized;
            Vector3 projectedStart = Vector3.ProjectOnPlane(segmentStart - center, normal);
            Vector3 projectedEnd = Vector3.ProjectOnPlane(segmentEnd - center, normal);
            Vector3 projectedClosest = ClosestPointOnSegment(Vector3.zero,
                projectedStart, projectedEnd, out segmentT);
            squareDistance = projectedClosest.sqrMagnitude;
            return Vector3.LerpUnclamped(segmentStart, segmentEnd, segmentT);
        }

        private static float BrushDistance(Vector3 offset, Vector3 projectionNormal,
            bool affectThroughDepth)
        {
            return affectThroughDepth && projectionNormal.sqrMagnitude > 1e-12f
                ? Vector3.ProjectOnPlane(offset, projectionNormal.normalized).magnitude
                : offset.magnitude;
        }

        private static void AccumulateInfluence(float normalizedCurvePosition, float falloff,
            float[] influences)
        {
            float control = Mathf.Clamp01(normalizedCurvePosition) * (influences.Length - 1f);
            int left = Mathf.Min(Mathf.FloorToInt(control), influences.Length - 1);
            int right = Mathf.Min(left + 1, influences.Length - 1);
            influences[left] = Mathf.Max(influences[left], falloff);
            influences[right] = Mathf.Max(influences[right], falloff);
        }
    }

    internal readonly struct HairSliceIntersection
    {
        internal readonly int SegmentEndIndex;
        internal readonly float SegmentT;
        internal readonly Vector3 PlanePoint;

        internal HairSliceIntersection(int segmentEndIndex, float segmentT, Vector3 planePoint)
        {
            SegmentEndIndex = segmentEndIndex;
            SegmentT = segmentT;
            PlanePoint = planePoint;
        }
    }

    internal static class HairSliceUtility
    {
        internal const float MinimumGesturePixels = 6f;
        internal const float GestureHitTolerancePixels = 10f;

        internal static bool TryCreateCameraPlane(Ray startRay, Ray endRay, out Plane plane)
        {
            plane = default;
            Vector3 startDirection = startRay.direction.normalized;
            Vector3 endDirection = endRay.direction.normalized;
            if (startDirection.sqrMagnitude <= 1e-12f || endDirection.sqrMagnitude <= 1e-12f)
                return false;

            Vector3 normal = Vector3.Cross(startDirection, endDirection);
            Vector3 point = (startRay.origin + endRay.origin) * 0.5f;
            if (normal.sqrMagnitude <= 1e-10f)
            {
                Vector3 across = endRay.origin - startRay.origin;
                Vector3 viewDirection = (startDirection + endDirection).normalized;
                normal = Vector3.Cross(viewDirection, across);
                point = startRay.origin;
            }
            if (normal.sqrMagnitude <= 1e-10f) return false;
            plane = new Plane(normal.normalized, point);
            return true;
        }

        internal static bool TryFindRootFirstIntersection(
            IReadOnlyList<Vector3> points,
            Plane plane,
            bool mirrorX,
            out HairSliceIntersection intersection)
        {
            return TryFindRootFirstIntersection(points, plane, mirrorX, null, out intersection);
        }

        internal static bool TryFindRootFirstIntersection(
            IReadOnlyList<Vector3> points,
            Plane plane,
            bool mirrorX,
            Predicate<Vector3> acceptsPlanePoint,
            out HairSliceIntersection intersection)
        {
            intersection = default;
            if (points == null || points.Count < 2) return false;
            Vector3 previous = mirrorX ? HairBrushInteractionUtility.MirrorX(points[0]) : points[0];
            float previousDistance = plane.GetDistanceToPoint(previous);
            const float epsilon = 1e-6f;
            for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                Vector3 current = mirrorX
                    ? HairBrushInteractionUtility.MirrorX(points[pointIndex])
                    : points[pointIndex];
                float currentDistance = plane.GetDistanceToPoint(current);
                bool coplanar = Mathf.Abs(previousDistance) <= epsilon &&
                                Mathf.Abs(currentDistance) <= epsilon;
                bool crosses = !coplanar &&
                               (Mathf.Abs(previousDistance) <= epsilon ||
                                Mathf.Abs(currentDistance) <= epsilon ||
                                Mathf.Sign(previousDistance) != Mathf.Sign(currentDistance));
                if (crosses)
                {
                    float denominator = previousDistance - currentDistance;
                    float t = Mathf.Abs(denominator) > epsilon
                        ? Mathf.Clamp01(previousDistance / denominator)
                        : 0f;
                    Vector3 planePoint = Vector3.LerpUnclamped(previous, current, t);
                    if (acceptsPlanePoint == null || acceptsPlanePoint(planePoint))
                    {
                        intersection = new HairSliceIntersection(pointIndex, t, planePoint);
                        return true;
                    }
                }
                previous = current;
                previousDistance = currentDistance;
            }
            return false;
        }

        internal static bool TruncateGuide(HairGroup group, HairGuide guide, float curvePosition)
        {
            if (group == null || guide?.points == null || guide.points.Count < 2 ||
                !float.IsFinite(curvePosition)) return false;
            int oldCount = guide.points.Count;
            float controlPosition = Mathf.Clamp(curvePosition * (oldCount - 1f), 0.001f,
                oldCount - 1f);
            int endIndex = Mathf.Clamp(Mathf.CeilToInt(controlPosition), 1, oldCount - 1);
            float segmentT = Mathf.Clamp(controlPosition - (endIndex - 1f), 0.001f, 1f);
            if (endIndex == oldCount - 1 && segmentT >= 0.99999f) return false;

            HairGuidePoint left = guide.points[endIndex - 1];
            HairGuidePoint right = guide.points[endIndex];
            if (left == null || right == null) return false;
            right.position = Vector3.LerpUnclamped(left.position, right.position, segmentT);
            right.width = Mathf.LerpUnclamped(left.width, right.width, segmentT);
            right.roll = Mathf.LerpAngle(left.roll, right.roll, segmentT);
            right.stiffness = Mathf.LerpUnclamped(left.stiffness, right.stiffness, segmentT);
            right.freeze = Mathf.LerpUnclamped(left.freeze, right.freeze, segmentT);
            right.profileScale = Mathf.LerpUnclamped(left.profileScale, right.profileScale, segmentT);

            if (group.sculptLayers != null)
            {
                for (int layerIndex = 0; layerIndex < group.sculptLayers.Count; layerIndex++)
                {
                    HairSculptLayer layer = group.sculptLayers[layerIndex];
                    HairGuideDelta delta = layer?.deltas?.Find(candidate =>
                        candidate != null && candidate.guideId == guide.Id);
                    if (delta == null) continue;
                    ResizeDelta(delta, oldCount);
                    delta.positionOffsets[endIndex] = Vector3.LerpUnclamped(
                        delta.positionOffsets[endIndex - 1], delta.positionOffsets[endIndex], segmentT);
                    delta.widthOffsets[endIndex] = Mathf.LerpUnclamped(
                        delta.widthOffsets[endIndex - 1], delta.widthOffsets[endIndex], segmentT);
                    delta.rollOffsets[endIndex] = Mathf.LerpUnclamped(
                        delta.rollOffsets[endIndex - 1], delta.rollOffsets[endIndex], segmentT);
                }
            }

            if (endIndex + 1 < oldCount)
                guide.points.RemoveRange(endIndex + 1, oldCount - endIndex - 1);
            if (group.sculptLayers != null)
            {
                for (int layerIndex = 0; layerIndex < group.sculptLayers.Count; layerIndex++)
                {
                    HairSculptLayer layer = group.sculptLayers[layerIndex];
                    HairGuideDelta delta = layer?.deltas?.Find(candidate =>
                        candidate != null && candidate.guideId == guide.Id);
                    ResizeDelta(delta, guide.points.Count);
                }
            }
            return true;
        }

        private static void ResizeDelta(HairGuideDelta delta, int count)
        {
            if (delta == null) return;
            if (delta.positionOffsets == null || delta.positionOffsets.Length != count)
                Array.Resize(ref delta.positionOffsets, count);
            if (delta.widthOffsets == null || delta.widthOffsets.Length != count)
                Array.Resize(ref delta.widthOffsets, count);
            if (delta.rollOffsets == null || delta.rollOffsets.Length != count)
                Array.Resize(ref delta.rollOffsets, count);
        }

        internal static bool IsOnFiniteGesture(Vector2 start, Vector2 end, Vector2 point,
            float tolerancePixels = GestureHitTolerancePixels)
        {
            Vector2 gesture = end - start;
            float squareLength = gesture.sqrMagnitude;
            if (squareLength < MinimumGesturePixels * MinimumGesturePixels) return false;
            float t = Vector2.Dot(point - start, gesture) / squareLength;
            if (t < 0f || t > 1f) return false;
            Vector2 closest = start + gesture * t;
            return (point - closest).sqrMagnitude <= tolerancePixels * tolerancePixels;
        }
    }

    internal static class HairBrushInteractionUtility
    {
        internal const float MinimumRadius = 0.001f;
        internal const float MaximumRadius = 0.5f;
        internal const float DefaultHardness = 0.75f;
        internal const float RadiusKeyScale = 1.12f;
        internal const float HardnessKeyStep = 0.05f;
        internal const float RadiusDragSensitivity = 0.012f;
        internal const float HardnessDragPixels = 180f;

        internal static float EvaluateFalloff(float distance, float radius, float hardness)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            float normalizedDistance = Mathf.Clamp01(distance / radius);
            float softStart = Mathf.Clamp01(hardness);
            return normalizedDistance <= softStart
                ? 1f
                : 1f - Mathf.InverseLerp(softStart, 1f, normalizedDistance);
        }

        internal static Vector3 MirrorX(Vector3 point)
        {
            point.x = -point.x;
            return point;
        }

        internal static float EvaluateMirroredFalloff(Vector3 point, Vector3 center, float radius,
            float hardness, bool mirrorX)
        {
            float falloff = EvaluateFalloff(Vector3.Distance(point, center), radius, hardness);
            if (!mirrorX) return falloff;
            float mirrored = EvaluateFalloff(Vector3.Distance(point, MirrorX(center)), radius, hardness);
            return Mathf.Max(falloff, mirrored);
        }

        internal static float RadiusFromModifierDrag(float startRadius, float horizontalPixels)
        {
            return Mathf.Clamp(startRadius * Mathf.Exp(horizontalPixels * RadiusDragSensitivity),
                MinimumRadius, MaximumRadius);
        }

        internal static float HardnessFromModifierDrag(float startHardness, float verticalPixels)
        {
            return Mathf.Clamp01(startHardness - verticalPixels / HardnessDragPixels);
        }

        internal static float StepRadius(float radius, float direction)
        {
            return Mathf.Clamp(radius * Mathf.Pow(RadiusKeyScale, direction), MinimumRadius, MaximumRadius);
        }

        internal static float StepHardness(float hardness, float direction)
        {
            return Mathf.Clamp01(hardness + direction * HardnessKeyStep);
        }

        internal static Vector3 ClampStrokeDelta(Vector3 delta, float radius)
        {
            float maximum = Mathf.Max(MinimumRadius, radius) * 0.5f;
            return delta.sqrMagnitude > maximum * maximum
                ? delta.normalized * maximum
                : delta;
        }
    }

    /// <summary>
    /// Shared shape constraints for interactive grooming. Positional brushes are deliberately
    /// inextensible; Length and Cut are the only tools allowed to change guide length.
    /// </summary>
    internal static class HairGuideShapeUtility
    {
        internal static void PreserveSegmentLengths(
            IReadOnlyList<Vector3> original,
            IList<Vector3> target)
        {
            if (original == null || target == null || original.Count < 2 ||
                target.Count != original.Count) return;

            target[0] = original[0];
            for (int pointIndex = 1; pointIndex < original.Count; pointIndex++)
            {
                Vector3 originalSegment = original[pointIndex] - original[pointIndex - 1];
                float segmentLength = originalSegment.magnitude;
                if (segmentLength <= 1e-8f)
                {
                    target[pointIndex] = target[pointIndex - 1];
                    continue;
                }

                Vector3 targetSegment = target[pointIndex] - target[pointIndex - 1];
                Vector3 direction = targetSegment.sqrMagnitude > 1e-12f
                    ? targetSegment.normalized
                    : originalSegment / segmentLength;
                target[pointIndex] = target[pointIndex - 1] + direction * segmentLength;
            }
        }

        internal static void RestoreReferenceSegmentLengths(
            IReadOnlyList<Vector3> reference,
            IReadOnlyList<Vector3> current,
            IList<Vector3> target)
        {
            if (reference == null || current == null || target == null || reference.Count < 2 ||
                current.Count != reference.Count || target.Count != reference.Count) return;

            target[0] = current[0];
            for (int pointIndex = 1; pointIndex < reference.Count; pointIndex++)
            {
                Vector3 referenceSegment = reference[pointIndex] - reference[pointIndex - 1];
                float segmentLength = referenceSegment.magnitude;
                Vector3 currentSegment = current[pointIndex] - current[pointIndex - 1];
                Vector3 direction = currentSegment.sqrMagnitude > 1e-12f
                    ? currentSegment.normalized
                    : referenceSegment.sqrMagnitude > 1e-12f
                        ? referenceSegment.normalized
                        : Vector3.up;
                target[pointIndex] = target[pointIndex - 1] + direction * segmentLength;
            }
        }

        internal static Vector3 StableGravityDirection(
            Vector3 gravity,
            Vector3 rootNormal,
            int seed,
            float separation)
        {
            Vector3 down = gravity.sqrMagnitude > 1e-12f ? gravity.normalized : Vector3.down;
            Vector3 outward = Vector3.ProjectOnPlane(rootNormal, down);
            if (outward.sqrMagnitude > 1e-12f) outward.Normalize();

            Vector3 reference = Mathf.Abs(Vector3.Dot(down, Vector3.up)) < 0.92f
                ? Vector3.up
                : Vector3.right;
            Vector3 tangent = Vector3.Cross(down, reference).normalized;
            Vector3 bitangent = Vector3.Cross(down, tangent).normalized;
            float angle = StableUnit(seed) * Mathf.PI * 2f;
            Vector3 jitter = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
            Vector3 fan = outward.sqrMagnitude > 1e-12f
                ? (outward * 0.82f + jitter * 0.18f).normalized
                : jitter;
            return (down + fan * (Mathf.Clamp01(separation) * 0.45f)).normalized;
        }

        private static float StableUnit(int seed)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return (value & 0x00ffffffu) / 16777216f;
            }
        }
    }

    internal static class HairPoseUtility
    {
        internal static bool TryCreateTriangleTransform(
            Vector3 sourceA, Vector3 sourceB, Vector3 sourceC,
            Vector3 posedA, Vector3 posedB, Vector3 posedC,
            Vector3 barycentric, out Matrix4x4 sourceToPose)
        {
            sourceToPose = Matrix4x4.identity;
            Vector3 sourceEdgeA = sourceB - sourceA;
            Vector3 sourceEdgeB = sourceC - sourceA;
            Vector3 posedEdgeA = posedB - posedA;
            Vector3 posedEdgeB = posedC - posedA;
            Vector3 sourceNormal = Vector3.Cross(sourceEdgeA, sourceEdgeB);
            Vector3 posedNormal = Vector3.Cross(posedEdgeA, posedEdgeB);
            if (sourceNormal.sqrMagnitude <= 1e-12f || posedNormal.sqrMagnitude <= 1e-12f)
                return false;

            float sourceNormalScale = Mathf.Max(1e-6f,
                (sourceEdgeA.magnitude + sourceEdgeB.magnitude) * 0.5f);
            float posedNormalScale = Mathf.Max(1e-6f,
                (posedEdgeA.magnitude + posedEdgeB.magnitude) * 0.5f);
            sourceNormal = sourceNormal.normalized * sourceNormalScale;
            posedNormal = posedNormal.normalized * posedNormalScale;
            Vector3 sourceRoot = sourceA * barycentric.x + sourceB * barycentric.y +
                                 sourceC * barycentric.z;
            Vector3 posedRoot = posedA * barycentric.x + posedB * barycentric.y +
                                posedC * barycentric.z;

            Matrix4x4 sourceFrame = Matrix4x4.identity;
            sourceFrame.SetColumn(0, new Vector4(sourceEdgeA.x, sourceEdgeA.y, sourceEdgeA.z, 0f));
            sourceFrame.SetColumn(1, new Vector4(sourceEdgeB.x, sourceEdgeB.y, sourceEdgeB.z, 0f));
            sourceFrame.SetColumn(2, new Vector4(sourceNormal.x, sourceNormal.y, sourceNormal.z, 0f));
            sourceFrame.SetColumn(3, new Vector4(sourceRoot.x, sourceRoot.y, sourceRoot.z, 1f));

            Matrix4x4 posedFrame = Matrix4x4.identity;
            posedFrame.SetColumn(0, new Vector4(posedEdgeA.x, posedEdgeA.y, posedEdgeA.z, 0f));
            posedFrame.SetColumn(1, new Vector4(posedEdgeB.x, posedEdgeB.y, posedEdgeB.z, 0f));
            posedFrame.SetColumn(2, new Vector4(posedNormal.x, posedNormal.y, posedNormal.z, 0f));
            posedFrame.SetColumn(3, new Vector4(posedRoot.x, posedRoot.y, posedRoot.z, 1f));

            if (Mathf.Abs(sourceFrame.determinant) <= 1e-12f) return false;
            sourceToPose = posedFrame * sourceFrame.inverse;
            return IsFinite(sourceToPose);
        }

        internal static Vector3 TransformNormal(Matrix4x4 transform, Vector3 normal)
        {
            Vector3 transformed = transform.inverse.transpose.MultiplyVector(normal);
            return transformed.sqrMagnitude > 1e-12f ? transformed.normalized : Vector3.up;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (!float.IsFinite(matrix[row, column])) return false;
            return true;
        }
    }
}
