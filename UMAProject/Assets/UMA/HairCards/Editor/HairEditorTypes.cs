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
