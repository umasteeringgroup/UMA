using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public enum HairValidationSeverity
    {
        Info,
        Optimization,
        Warning,
        Error
    }

    public enum HairValidationCode
    {
        MissingGroom,
        MissingSourceMesh,
        SourceTopologyChanged,
        MissingGroup,
        EmptyGroup,
        MissingProfile,
        MissingAtlas,
        InvalidRoot,
        InvalidPoint,
        ZeroLengthSegment,
        ZeroLengthGuide,
        MissingHelper,
        DegenerateTriangle,
        FrameFlip,
        EmptyOutput,
        TriangleBudget,
        CardBudget
    }

    public sealed class HairValidationIssue
    {
        public HairValidationSeverity severity;
        public HairValidationCode code;
        public string message;
        public string groupId;
        public string guideId;
        public string helperId;
        public string fixId;
        public int count = 1;
    }

    public sealed class HairValidationReport
    {
        public readonly List<HairValidationIssue> issues = new List<HairValidationIssue>();
        public int guideCount;
        public int cardCount;
        public int vertexCount;
        public int triangleCount;

        public int ErrorCount => issues.FindAll(issue => issue.severity == HairValidationSeverity.Error).Count;
        public int WarningCount => issues.FindAll(issue => issue.severity == HairValidationSeverity.Warning).Count;
        public bool CanBake => ErrorCount == 0;

        public void Add(
            HairValidationSeverity severity,
            HairValidationCode code,
            string message,
            string groupId = null,
            string guideId = null,
            string helperId = null,
            string fixId = null,
            int count = 1)
        {
            issues.Add(new HairValidationIssue
            {
                severity = severity,
                code = code,
                message = message,
                groupId = groupId,
                guideId = guideId,
                helperId = helperId,
                fixId = fixId,
                count = Mathf.Max(1, count)
            });
        }
    }

    public sealed class HairValidationOptions
    {
        public int triangleBudget = 100000;
        public int cardBudget = 10000;
        public bool requireAtlas;
        public bool requireProfile = true;
    }

    public static class HairValidator
    {
        public static HairValidationReport Validate(
            HairGroomAsset groom,
            HairEvaluationResult evaluation = null,
            HairCardMeshBuildResult meshBuild = null,
            HairValidationOptions options = null)
        {
            options ??= new HairValidationOptions();
            HairValidationReport report = new HairValidationReport();
            if (groom == null)
            {
                report.Add(HairValidationSeverity.Error, HairValidationCode.MissingGroom,
                    "No HairGroomAsset is available.");
                return report;
            }

            if (groom.SourceMesh == null)
            {
                report.Add(HairValidationSeverity.Warning, HairValidationCode.MissingSourceMesh,
                    "No source scalp mesh is assigned. Cached guide roots can preview, but surface rebinding and weight transfer are unavailable.");
            }
            else if (!groom.SourceTopologyMatches())
            {
                report.Add(HairValidationSeverity.Error, HairValidationCode.SourceTopologyChanged,
                    "The source mesh topology no longer matches the groom binding signature.", fixId: "rebind-source");
            }

            if (groom.Groups == null || groom.Groups.Count == 0)
            {
                report.Add(HairValidationSeverity.Error, HairValidationCode.MissingGroup,
                    "The groom has no hair groups.", fixId: "create-coverage-group");
                return report;
            }

            for (int groupIndex = 0; groupIndex < groom.Groups.Count; groupIndex++)
            {
                HairGroup group = groom.Groups[groupIndex];
                if (group == null) continue;
                if (!group.enabled) continue;
                if (group.guides == null || group.guides.Count == 0)
                {
                    report.Add(HairValidationSeverity.Warning, HairValidationCode.EmptyGroup,
                        $"Group '{group.name}' has no guides.", group.Id, fixId: "generate-guides");
                }
                if (options.requireProfile && group.profile == null)
                {
                    report.Add(HairValidationSeverity.Error, HairValidationCode.MissingProfile,
                        $"Group '{group.name}' has no card profile.", group.Id, fixId: "assign-profile");
                }
                if (group.atlas == null)
                {
                    report.Add(options.requireAtlas ? HairValidationSeverity.Error : HairValidationSeverity.Warning,
                        HairValidationCode.MissingAtlas,
                        $"Group '{group.name}' has no atlas profile; generated cards use full-range UVs and the fallback material.",
                        group.Id, fixId: "assign-atlas");
                }
                ValidateGuides(group, report);
                ValidateConstraints(group, groom, report);
            }

            if (evaluation != null)
            {
                report.cardCount = evaluation.CardCount;
                if (evaluation.CardCount == 0)
                {
                    report.Add(HairValidationSeverity.Error, HairValidationCode.EmptyOutput,
                        "Evaluation produced no cards.", fixId: "inspect-groups");
                }
                if (evaluation.CardCount > options.cardBudget)
                {
                    report.Add(HairValidationSeverity.Optimization, HairValidationCode.CardBudget,
                        $"Card count {evaluation.CardCount:N0} exceeds the target {options.cardBudget:N0}.",
                        count: evaluation.CardCount - options.cardBudget);
                }
            }

            if (meshBuild != null)
            {
                report.vertexCount = meshBuild.vertexCount;
                report.triangleCount = meshBuild.triangleCount;
                if (meshBuild.degenerateTriangleCount > 0)
                {
                    report.Add(HairValidationSeverity.Error, HairValidationCode.DegenerateTriangle,
                        $"{meshBuild.degenerateTriangleCount:N0} degenerate triangles were rejected during meshing.",
                        count: meshBuild.degenerateTriangleCount);
                }
                if (meshBuild.frameFlipCount > 0)
                {
                    report.Add(HairValidationSeverity.Warning, HairValidationCode.FrameFlip,
                        $"{meshBuild.frameFlipCount:N0} possible card-frame flips were detected.",
                        count: meshBuild.frameFlipCount);
                }
                if (meshBuild.triangleCount > options.triangleBudget)
                {
                    report.Add(HairValidationSeverity.Optimization, HairValidationCode.TriangleBudget,
                        $"Triangle count {meshBuild.triangleCount:N0} exceeds the target {options.triangleBudget:N0}.",
                        count: meshBuild.triangleCount - options.triangleBudget);
                }
            }
            return report;
        }

        private static void ValidateGuides(HairGroup group, HairValidationReport report)
        {
            if (group.guides == null) return;
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide == null) continue;
                report.guideCount++;
                if (!guide.root.IsValid)
                {
                    report.Add(HairValidationSeverity.Warning, HairValidationCode.InvalidRoot,
                        $"Guide '{guide.name}' uses only its cached root pose.", group.Id, guide.Id,
                        fixId: "reproject-root");
                }
                if (guide.points == null || guide.points.Count < 2)
                {
                    report.Add(HairValidationSeverity.Error, HairValidationCode.ZeroLengthGuide,
                        $"Guide '{guide.name}' has fewer than two points.", group.Id, guide.Id,
                        fixId: "delete-guide");
                    continue;
                }
                float length = 0f;
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    if (point == null || !IsFinite(point.position) || !float.IsFinite(point.width) ||
                        !float.IsFinite(point.roll))
                    {
                        report.Add(HairValidationSeverity.Error, HairValidationCode.InvalidPoint,
                            $"Guide '{guide.name}' contains an invalid point.", group.Id, guide.Id,
                            fixId: "repair-guide");
                        continue;
                    }
                    if (pointIndex == 0) continue;
                    float segmentLength = Vector3.Distance(guide.points[pointIndex - 1].position, point.position);
                    length += segmentLength;
                    if (segmentLength < 1e-7f)
                    {
                        report.Add(HairValidationSeverity.Warning, HairValidationCode.ZeroLengthSegment,
                            $"Guide '{guide.name}' contains a zero-length segment.", group.Id, guide.Id,
                            fixId: "simplify-guide");
                    }
                }
                if (length < 1e-6f)
                {
                    report.Add(HairValidationSeverity.Error, HairValidationCode.ZeroLengthGuide,
                        $"Guide '{guide.name}' has zero usable length.", group.Id, guide.Id,
                        fixId: "delete-guide");
                }
            }
        }

        private static void ValidateConstraints(HairGroup group, HairGroomAsset groom, HairValidationReport report)
        {
            if (group.constraints == null) return;
            for (int constraintIndex = 0; constraintIndex < group.constraints.Count; constraintIndex++)
            {
                HairConstraintSettings constraint = group.constraints[constraintIndex];
                if (constraint == null || !constraint.enabled || string.IsNullOrEmpty(constraint.helperId)) continue;
                if (groom.FindHelper(constraint.helperId) != null) continue;
                report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper,
                    $"Constraint '{constraint.name}' in group '{group.name}' references a missing helper.",
                    group.Id, helperId: constraint.helperId, fixId: "repair-helper-reference");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
