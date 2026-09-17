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
        MissingAtlasRegion,
        InvalidRoot,
        InvalidPoint,
        ZeroLengthSegment,
        ZeroLengthGuide,
        MissingHelper,
        DegenerateTriangle,
        FrameFlip,
        EmptyOutput,
        TriangleBudget,
        CardBudget,
        InvalidLod,
        UnreadableSource,
        MaterialPassMismatch,
        ScalpBindingMissing,
        MissingMap,
        InvalidBakeReference,
        InvalidCharacterBinding,
        SamplingLimit,
        InvalidTextureMap
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
        public int lodLevel = -1;
    }

    public sealed class HairLodValidationSummary
    {
        public int level;
        public int cardCount;
        public int vertexCount;
        public int triangleCount;
    }

    public sealed class HairValidationReport
    {
        public readonly List<HairValidationIssue> issues = new List<HairValidationIssue>();
        public int guideCount;
        public int cardCount;
        public int vertexCount;
        public int triangleCount;
        public bool isReleaseReport;
        public readonly List<HairLodValidationSummary> lods = new List<HairLodValidationSummary>();

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
            else if (!groom.SourceMesh.isReadable)
            {
                report.Add(HairValidationSeverity.Error, HairValidationCode.UnreadableSource,
                    "The source mesh is not readable. Enable Read/Write on its importer before validating or baking.", fixId: "rebind-source");
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
                bool hasFormSource = group.generation?.enabled == true &&
                    (group.generation.cards.source != HairPopulationSource.Scalp || group.generation.clumps.Exists(p => p != null && p.enabled && p.source != HairPopulationSource.Scalp));
                if (!hasFormSource && (group.guides == null || group.guides.Count == 0))
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
                else if (group.atlasRegionSelection == HairAtlasRegionSelectionMode.Selected &&
                         !HasValidSelectedAtlasRegion(group))
                {
                    report.Add(HairValidationSeverity.Error, HairValidationCode.MissingAtlasRegion,
                        $"Group '{group.name}' is set to Selected UV Areas, but none of its selected areas exist in atlas '{group.atlas.name}'.",
                        group.Id, fixId: "assign-atlas-region");
                }
                ValidateGuides(group, groom, report);
                foreach (var map in group.maps)
                    if (map?.storage == HairMapStorage.Texture &&
                        (map.texture == null || !map.texture.IsValid(groom.SourceTopologySignature, groom.SourceVertexCount)))
                        report.Add(HairValidationSeverity.Error, HairValidationCode.InvalidTextureMap,
                            $"Map '{map.DisplayName}' has invalid texels or belongs to another source topology. Restore its source or restore a saved map; baking is blocked to protect the painted data.", group.Id);
                ValidateConstraints(group, groom, report);
                ValidateModifierMaps(group, groom, report);
                if (group.generation?.enabled == true)
                {
                    void CheckForm(HairGenerationStage population)
                    {
                        if (population == null || !population.enabled || population.source == HairPopulationSource.Scalp || population.source == HairPopulationSource.PaintedScalp) return;
                        if (population.form.helperIds.Count == 0)
                            report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"'{population.name}' needs a form helper. Add a grid panel or braid rail from its tree actions.", group.Id);
                        foreach (string id in population.form.helperIds)
                        {
                            var helper = groom.FindHelper(id);
                            bool valid = population.source == HairPopulationSource.Bun ? helper?.type == HairHelperType.Bun : population.source == HairPopulationSource.GridPanels ? HairFormUtility.ValidGrid(helper) :
                                helper != null && (helper.type == HairHelperType.BraidRail || helper.type == HairHelperType.CurveRail) && helper.points.Count > 1;
                            if (!valid) report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"'{population.name}' has a missing or invalid form binding. Replace it in its properties.", group.Id, helperId: id);
                        }
                    }
                    foreach (var population in group.generation.clumps) CheckForm(population);
                    CheckForm(group.generation.cards);
                }
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
                if (meshBuild.samplingLimitedCardCount > 0)
                    report.Add(HairValidationSeverity.Optimization, HairValidationCode.SamplingLimit,
                        $"{meshBuild.samplingLimitedCardCount:N0} curl cards reached their sampling cap before meeting the shape/facing error target. Adjust Curl mesh detail or the LOD/profile cap.",
                        count: meshBuild.samplingLimitedCardCount);
                if (meshBuild.triangleCount > options.triangleBudget)
                {
                    report.Add(HairValidationSeverity.Optimization, HairValidationCode.TriangleBudget,
                        $"Triangle count {meshBuild.triangleCount:N0} exceeds the target {options.triangleBudget:N0}.",
                        count: meshBuild.triangleCount - options.triangleBudget);
                }
            }
            return report;
        }

        private static bool HasValidSelectedAtlasRegion(HairGroup group)
        {
            if (group?.atlas?.regions == null || group.atlasRegionIds == null) return false;
            for (int regionIndex = 0; regionIndex < group.atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = group.atlas.regions[regionIndex];
                if (region == null) continue;
                for (int selectedIndex = 0; selectedIndex < group.atlasRegionIds.Count; selectedIndex++)
                {
                    if (string.Equals(region.Id, group.atlasRegionIds[selectedIndex], StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private static void ValidateGuides(HairGroup group, HairGroomAsset groom, HairValidationReport report)
        {
            if (group.guides == null) return;
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide == null || !guide.enabled) continue;
                report.guideCount++;
                if (!guide.root.IsValid || !string.Equals(guide.root.SourceMeshId, groom.SourceMeshId,
                        StringComparison.Ordinal))
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
                    if (pointIndex == 0 || guide.points[pointIndex - 1] == null ||
                        !IsFinite(guide.points[pointIndex - 1].position)) continue;
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

        private static void ValidateModifierMaps(HairGroup group, HairGroomAsset groom, HairValidationReport report)
        {
            void Check(IReadOnlyList<HairModifierSettings> modifiers)
            {
                if (modifiers == null) return;
                foreach (var modifier in modifiers)
                {
                    if (modifier?.enabled == true && !string.IsNullOrEmpty(modifier.maskMapId) && !group.maps.Exists(m => m?.Id == modifier.maskMapId))
                        report.Add(HairValidationSeverity.Warning, HairValidationCode.MissingMap,
                            $"'{modifier.name}' references a missing painted mask and will have no effect. Select it and choose a map under Where this modifier applies.", group.Id);
                    if (modifier?.enabled == true && modifier.type == HairModifierType.Gather)
                    {
                        var helper = groom.FindHelper(modifier.helperId);
                        if (helper?.type != HairHelperType.Gather || !HairCoordinateUtility.IsInvertibleAffine(helper.LocalToSource))
                            report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"'{modifier.name}' needs a valid Gather ring with a nonzero scale.", group.Id, helperId: modifier.helperId);
                        if (modifier.gather.mode == HairGatherMode.PassThrough && !string.IsNullOrEmpty(modifier.gather.continuationHelperId))
                        {
                            var tail = groom.FindHelper(modifier.gather.continuationHelperId);
                            if (tail?.type != HairHelperType.CurveRail || tail.points.Count < 2)
                                report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"'{modifier.name}' needs a valid continuation curve rail, or None for a straight tail.", group.Id);
                        }
                    }
                }
            }
            Check(group.modifiers);
            foreach (var layer in group.sculptLayers) if (layer?.visible == true) Check(layer.modifiers);
            if (group.generation?.enabled != true) return;
            void Population(HairGenerationStage stage)
            {
                if (stage?.enabled != true) return;
                Check(stage.modifiers);
                if (stage.source == HairPopulationSource.PaintedScalp && !string.IsNullOrEmpty(stage.rootMapGroupId) && !groom.Groups.Exists(g => g?.Id == stage.rootMapGroupId))
                    report.Add(HairValidationSeverity.Error, HairValidationCode.MissingMap, $"'{stage.name}' needs a valid Growth map group.", group.Id);
                if (stage.source == HairPopulationSource.Bun) foreach (var id in stage.form.helperIds)
                {
                    var bun = groom.FindHelper(id);
                    if (bun?.type == HairHelperType.Bun && !string.IsNullOrEmpty(bun.bun.gatherHelperId) && groom.FindHelper(bun.bun.gatherHelperId)?.type != HairHelperType.Gather)
                        report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"Bun '{bun.name}' has a missing Gather parent. Reassign it or choose None.", group.Id, helperId: id);
                }
                if (stage.source == HairPopulationSource.Braid) foreach (var id in stage.form.helperIds)
                {
                    var rail = groom.FindHelper(id); if (!HairRailUtility.IsRail(rail)) continue;
                    void Target(string target, string label)
                    {
                        if (!HairRailUtility.IsTarget(groom.FindHelper(target)))
                            report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"Spline '{rail.name}' has a missing/invalid {label}. Choose a helper or switch to free placement.", group.Id, helperId: id);
                    }
                    if (!string.IsNullOrEmpty(rail.rail.parentHelperId)) Target(rail.rail.parentHelperId, "parent");
                    if (rail.rail.root.attachment == HairRailAttachment.Helper) Target(rail.rail.root.helperId, "root attachment");
                    if (rail.rail.tip.attachment == HairRailAttachment.Helper) Target(rail.rail.tip.helperId, "tip attachment");
                    bool surface = rail.rail.followSurface || rail.rail.root.attachment == HairRailAttachment.Surface || rail.rail.tip.attachment == HairRailAttachment.Surface;
                    var mesh = rail.rail.surfaceMesh != null ? rail.rail.surfaceMesh : groom.SourceMesh;
                    if (surface && (mesh == null || !mesh.isReadable || !HairCoordinateUtility.IsInvertibleAffine(rail.rail.SurfaceMatrix)))
                        report.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper, $"Spline '{rail.name}' needs a readable placement surface and a nonzero surface scale.", group.Id, helperId: id);
                }
                if (!string.IsNullOrEmpty(stage.partMapId) && !group.maps.Exists(m => m?.Id == stage.partMapId))
                    report.Add(HairValidationSeverity.Error, HairValidationCode.MissingMap,
                        $"'{stage.name}' references a missing Part Regions map. Choose a replacement under Root placement & guide influence.", group.Id);
            }
            foreach (var stage in group.generation.clumps) Population(stage);
            Population(group.generation.cards);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
