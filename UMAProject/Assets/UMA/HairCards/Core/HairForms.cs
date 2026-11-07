using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    // Append only: serialized surface generation remains value zero.
    public enum HairPopulationSource { Scalp, GridPanels, Braid, PaintedScalp, Bun }

    [Serializable]
    public sealed class HairFormSettings
    {
        public HairBunPart bunPart;
        public List<string> helperIds = new List<string>();
        [Min(.0001f)] public float cardWidth = .012f;
        [Range(0f, 1f)] public float jitter = .35f;
        [Range(0f, .5f)] public float lengthVariation;
        [Range(0f, .1f)] public float rootStartVariation;
        [Range(0f, .02f)] public float thickness = .002f;
        [Range(0f, .02f)] public float flyaways = .001f;
        [Range(4, 255)] public int segments = 32;
        public bool smooth = true;
        public bool adaptiveSamples = true;
        [Range(.0001f, .005f)] public float shapeError = .0005f;
        public bool flipFacing;
        // Opt-in: independent bun/extension forms must not disappear with an empty scalp map.
        public bool usePaintedRootDensity;
        public bool preserveLodCoverage;
        [Range(.001f, .2f)] public float braidRadius = .025f;
        [Range(.05f, 1f)] public float braidDepth = .45f;
        [Range(.1f, 20f)] public float repeats = 5f;
        [Range(.05f, .65f)] public float bundleRadius = .3f;
        [Range(.01f, 1f)] public float tipScale = .3f;
        [Range(-180f, 180f)] public float phase;
        public bool reverseWeave;
        public void EnsureIntegrity()
        {
            helperIds ??= new List<string>();
            cardWidth = Finite(cardWidth, .0001f, 1f); jitter = Finite(jitter, 0f, 1f);
            lengthVariation = Finite(lengthVariation, 0, .5f);
            rootStartVariation = Finite(rootStartVariation, 0, .1f);
            thickness = Finite(thickness, 0f, .1f); flyaways = Finite(flyaways, 0f, .1f);
            segments = Mathf.Clamp(segments, 4, 255);
            shapeError = Finite(shapeError, .0001f, .005f);
            braidRadius = Finite(braidRadius, .001f, .2f); braidDepth = Finite(braidDepth, .05f, 1f);
            repeats = Finite(repeats, .1f, 20f); bundleRadius = Finite(bundleRadius, .05f, .65f);
            tipScale = Finite(tipScale, .01f, 1f); phase = Finite(phase, -180f, 180f);
        }
        private static float Finite(float v, float min, float max) => float.IsFinite(v) ? Mathf.Clamp(v, min, max) : min;
    }

    /// <summary>Source-local row-major surface: columns across the panel, rows from root to tip.
    /// Helpers already store source-local points. Never apply LocalToSource a second time.</summary>
    public static class HairFormUtility
    {
        public static bool ValidGrid(HairHelper helper) => helper != null && helper.type == HairHelperType.GuideGrid &&
            helper.gridColumns >= 2 && helper.gridRows >= 2 && helper.points.Count == helper.gridColumns * helper.gridRows;

        public static Vector3 GridPoint(HairHelper grid, float u, float v, bool smooth = true)
        {
            if (!ValidGrid(grid)) return grid?.position ?? Vector3.zero;
            float x = Mathf.Clamp01(u) * (grid.gridColumns - 1), y = Mathf.Clamp01(v) * (grid.gridRows - 1);
            int ix = Mathf.Min(Mathf.FloorToInt(x), grid.gridColumns - 2), iy = Mathf.Min(Mathf.FloorToInt(y), grid.gridRows - 2);
            Vector3 Row(int row)
            {
                row = Mathf.Clamp(row, 0, grid.gridRows - 1);
                Vector3 At(int col) => grid.points[row * grid.gridColumns + Mathf.Clamp(col, 0, grid.gridColumns - 1)];
                Vector3 a = At(ix), b = At(ix + 1);
                return smooth ? Cubic(ix > 0 ? At(ix - 1) : 2 * a - b, a, b,
                    ix + 2 < grid.gridColumns ? At(ix + 2) : 2 * b - a, x - ix) : Vector3.Lerp(a, b, x - ix);
            }
            Vector3 p = Row(iy), q = Row(iy + 1);
            return smooth ? Cubic(iy > 0 ? Row(iy - 1) : 2 * p - q, p, q,
                iy + 2 < grid.gridRows ? Row(iy + 2) : 2 * q - p, y - iy) : Vector3.Lerp(p, q, y - iy);
        }

        public static Vector3 GridNormal(HairHelper grid, float u, float v, bool smooth)
        {
            Vector3 across = GridPoint(grid, u + .001f, v, smooth) - GridPoint(grid, u - .001f, v, smooth);
            Vector3 along = GridPoint(grid, u, v + .001f, smooth) - GridPoint(grid, u, v - .001f, smooth);
            Vector3 n = Vector3.Cross(along, across);
            return n.sqrMagnitude > 1e-14f ? n.normalized : Vector3.up;
        }

        public static void CreateGrid(HairHelper helper, int columns, int rows, Vector3 origin, Vector3 across, Vector3 along)
        {
            helper.type = HairHelperType.GuideGrid;
            helper.gridColumns = Mathf.Clamp(columns, 2, 16); helper.gridRows = Mathf.Clamp(rows, 2, 32);
            helper.points.Clear(); helper.position = origin;
            for (int row = 0; row < helper.gridRows; row++)
                for (int col = 0; col < helper.gridColumns; col++)
                    helper.points.Add(origin + across * (col / (helper.gridColumns - 1f) - .5f) + along * (row / (helper.gridRows - 1f)));
        }

        public static void ResizeGrid(HairHelper helper, int columns, int rows)
        {
            if (!ValidGrid(helper)) return;
            columns = Mathf.Clamp(columns, 2, 16); rows = Mathf.Clamp(rows, 2, 32);
            var points = new List<Vector3>(columns * rows);
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
                points.Add(GridPoint(helper, c / (columns - 1f), r / (rows - 1f)));
            helper.points = points; helper.gridColumns = columns; helper.gridRows = rows;
        }

        public static void CoilRail(HairHelper helper)
        {
            float radius = Mathf.Clamp(float.IsFinite(helper.coilRadius) ? helper.coilRadius : .05f, .005f, .2f);
            float turns = Mathf.Clamp(float.IsFinite(helper.coilTurns) ? helper.coilTurns : 1.5f, .5f, 3f);
            float rise = Mathf.Clamp(float.IsFinite(helper.coilRise) ? helper.coilRise : .035f, -.1f, .1f);
            int count = Mathf.CeilToInt(turns * 16) + 1;
            helper.points.Clear();
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f), angle = turns * t * 2 * Mathf.PI;
                float r = radius * Mathf.Lerp(1, .2f, t);
                helper.points.Add(helper.position + helper.rotation * new Vector3(Mathf.Cos(angle) * r, rise * t, Mathf.Sin(angle) * r));
            }
        }

        public static void Reverse(HairHelper helper)
        {
            if (ValidGrid(helper))
            {
                // Reverse both axes: change root edge without turning the facing inside out.
                helper.points.Reverse();
            }
            else
            {
                helper.points.Reverse();
                (helper.rail.root, helper.rail.tip) = (helper.rail.tip, helper.rail.root);
            }
            if (helper.points.Count > 0) helper.position = helper.points[0];
        }

        public static Vector3 RailPoint(IReadOnlyList<Vector3> points, float t, bool smooth)
        {
            if (points == null || points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];
            float f = Mathf.Clamp01(t) * (points.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(f), points.Count - 2);
            Vector3 a = points[i], b = points[i + 1];
            return smooth ? Cubic(i > 0 ? points[i - 1] : 2 * a - b, a, b,
                i + 2 < points.Count ? points[i + 2] : 2 * b - a, f - i) : Vector3.Lerp(a, b, f - i);
        }
        public static Vector3 RailTangent(IReadOnlyList<Vector3> points, float t, bool smooth)
        {
            if (points == null || points.Count < 2) return Vector3.up;
            float f = Mathf.Clamp01(t) * (points.Count - 1); int i = Mathf.Min(Mathf.FloorToInt(f), points.Count - 2);
            Vector3 a = points[i], b = points[i+1]; float u = f - i;
            if (!smooth) return b - a;
            Vector3 m0 = (b - (i > 0 ? points[i-1] : 2*a-b)) * .5f;
            Vector3 m1 = ((i+2 < points.Count ? points[i+2] : 2*b-a) - a) * .5f;
            return m0 * (3*u*u - 4*u + 1) + (b-a) * (-6*u*u+6*u) + m1 * (3*u*u-2*u);
        }
        private static Vector3 Cubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
            => .5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * (t * t) + (-a + 3 * b - 3 * c + d) * (t * t * t));

        // A three-strand plait, not three helices. At equal lateral positions the second
        // harmonic gives opposite depths, producing alternating over/under crossings.
        public static Vector2 Weave(float phase) => new Vector2(Mathf.Cos(phase), Mathf.Sin(2f * phase));
    }

    internal sealed class HairFormWorkspace
    {
        private readonly List<Vector3> rail = new List<Vector3>(), sides = new List<Vector3>(), normals = new List<Vector3>();
        private readonly List<HairCurvePoint> path = new List<HairCurvePoint>(), sampled = new List<HairCurvePoint>();
        private readonly List<Vector3> panelRows = new List<Vector3>(), panelAcross = new List<Vector3>();
        private readonly List<Vector3> rowControls = new List<Vector3>();
        private Vector3 panelOrigin;
        private float[] cumulative = Array.Empty<float>();
        private readonly HairRailWorkspace spline = new HairRailWorkspace();
        internal void Clear()
        {
            spline.Clear();
            rail.Clear(); rail.Capacity = 0; sides.Clear(); sides.Capacity = 0; normals.Clear(); normals.Capacity = 0;
            path.Clear(); path.Capacity = 0; sampled.Clear(); sampled.Capacity = 0; cumulative = Array.Empty<float>();
            panelRows.Clear(); panelRows.Capacity = 0; panelAcross.Clear(); panelAcross.Capacity = 0;
            rowControls.Clear(); rowControls.Capacity = 0;
        }

        internal void Generate(HairGroomAsset groom, HairGroup group, HairGenerationStage stage, HairLodSettings lod,
            bool final, HairEvaluationResult result, HairEvaluationWorkspace workspace, List<HairEvaluatedCurve> output)
        {
            var settings = stage.form;
            var source = stage.source == HairPopulationSource.Bun ?
                (settings.bunPart == HairBunPart.SurroundingBraid ? HairPopulationSource.Braid : HairPopulationSource.GridPanels) : stage.source;
            var valid = new List<HairHelper>(); var ids = new HashSet<string>();
            foreach (string id in settings.helperIds)
            {
                if (string.IsNullOrEmpty(id) || !ids.Add(id)) continue;
                var helper = groom.FindHelper(id);
                if (stage.source == HairPopulationSource.Bun)
                {
                    if (helper?.type == HairHelperType.Bun) workspace.buns.Append(groom, helper, settings.bunPart, valid);
                    else result.warnings.Add($"{stage.name}: assign a Bun helper.");
                    continue;
                }
                if (helper == null || (source == HairPopulationSource.GridPanels ? !HairFormUtility.ValidGrid(helper) :
                    (helper.type != HairHelperType.BraidRail && helper.type != HairHelperType.CurveRail) || helper.points.Count < 2))
                { result.warnings.Add($"{stage.name}: missing or invalid form helper. Choose a grid panel or braid rail in its properties."); continue; }
                bool finite = true; foreach (var p in helper.points) finite &= float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
                if (finite) valid.Add(helper); else result.warnings.Add($"{helper.name}: invalid control point; skipped.");
            }
            if (valid.Count == 0) { result.warnings.Add($"{stage.name}: add and assign a {(source == HairPopulationSource.Braid ? "braid rail" : "grid panel")} to generate hair."); return; }
            string[] regions = group.atlasRegionIds?.ToArray() ?? Array.Empty<string>();
            for (int h = 0; h < valid.Count; h++)
            {
                var helper = valid[h];
                float braidRadius = settings.braidRadius * helper.braidScale;
                int count = stage.count / valid.Count + (h < stage.count % valid.Count ? 1 : 0);
                if (count == 0) continue;
                int samples = source == HairPopulationSource.Braid ? Mathf.Clamp(Mathf.CeilToInt(settings.repeats * 24) + 1, 65, 513) : Mathf.Max(65, helper.gridRows * 4 + 1);
                if (source == HairPopulationSource.Braid)
                {
                    spline.Prepare(groom, helper, workspace.sourceMesh, result.warnings);
                    PrepareRail(helper, samples, settings.smooth);
                }
                for (int i = 0; i < count; i++)
                {
                    // Identity/randomness are based on full-density ordinals, never LOD density.
                    int seed = Hash(helper.Id) ^ stage.seed ^ unchecked(i * 73856093);
                    var random = new HairDeterministicRandom(seed);
                    float keep = random.Next01();
                    bool coverage = settings.preserveLodCoverage && source == HairPopulationSource.GridPanels;
                    // Low-discrepancy ordering spreads survivors across the panel instead of
                    // randomly losing contiguous strips. Stable ordinals give nested LODs.
                    if (coverage) keep = (float)((i * .6180339887498949 + .5) % 1.0);
                    bool retain = source == HairPopulationSource.Braid ? i < 3 : coverage && i == count / 2;
                    if (final && lod != null && (lod.cardFraction <= 0 || (keep > lod.cardFraction && !retain))) continue;
                    if (final && workspace.options.interactiveSampleLimit > 0 && output.Count + result.curves.Count >= workspace.options.interactiveSampleLimit) break;
                    var curve = result.RentCurve(samples);
                    curve.curveId = stage.Id + ":" + helper.Id + ":" + i;
                    curve.parentGuideId = helper.Id; curve.groupId = group.Id; curve.generationStageId = stage.Id;
                    curve.clumpId = helper.Id + ":" + (i % 3); curve.clumpSeed = seed ^ (i * 73856093);
                    curve.isChild = true; curve.seed = seed; curve.childOrdinal = i; curve.groupColor = group.color;
                    curve.maskValue = 1; curve.hairlineDistance = 0; curve.rootEmbedDepth = 0;
                    curve.profile = group.profile; curve.atlas = group.atlas; curve.atlasRegionSelection = group.atlasRegionSelection; curve.atlasRegionIds = regions;
                    curve.samplesPerCardOverride = Mathf.Min(settings.segments + 1, lod != null && !lod.useProfileSamples ? lod.samplesPerCard : settings.segments + 1);
                    if (workspace.options.previewSampleCount > 1) curve.samplesPerCardOverride = Mathf.Min(curve.samplesPerCardOverride, workspace.options.previewSampleCount);
                    curve.tubeSidesOverride = 0;
                    curve.ribbonReduction = new HairRibbonReductionSettings { enabled = settings.adaptiveSamples,
                        shapeError = settings.shapeError, facingError = 10f, maximumSamples = settings.segments + 1 };
                    float u = (i + .5f + random.NextSigned() * .45f * settings.jitter) / count;
                    float offset = random.Next01() * settings.thickness;
                    float angle = random.Next01() * 2 * Mathf.PI;
                    float radius = settings.bundleRadius * braidRadius * Mathf.Sqrt(Mathf.Lerp(.7f, 1f, random.Next01()));
                    float fly = random.Next01() < .15f ? settings.flyaways : 0;
                    float width = settings.cardWidth * Mathf.Lerp(.8f, 1.2f, random.Next01());
                    if (coverage && final && lod != null)
                        width *= Mathf.Min(2f, 1f / Mathf.Sqrt(Mathf.Max(.001f, lod.cardFraction)));
                    if (source == HairPopulationSource.Braid) width *= helper.braidScale;
                    float length = 1 - random.Next01() * settings.lengthVariation;
                    var rootVariation = new HairDeterministicRandom(seed ^ 0x36a34117);
                    float rootStart = rootVariation.Next01() * settings.rootStartVariation;
                    if (source == HairPopulationSource.GridPanels) PreparePanel(helper, u, settings.smooth);
                    for (int p = 0; p < samples; p++)
                    {
                        float t = p / (samples - 1f);
                        Vector3 point, normal;
                        if (source == HairPopulationSource.GridPanels)
                        {
                            t = rootStart > 0f ? Mathf.Lerp(rootStart, length, t) : t * length;
                            point = panelOrigin + HairFormUtility.RailPoint(panelRows, t, settings.smooth);
                            Vector3 across = HairFormUtility.RailPoint(panelAcross, t, settings.smooth);
                            Vector3 along = HairFormUtility.RailTangent(panelRows, t, settings.smooth);
                            normal = Vector3.Cross(along, across).normalized * (settings.flipFacing ? -1 : 1);
                        }
                        else
                        {
                            float phase = t * settings.repeats * Mathf.PI * 2 * (settings.reverseWeave ? -1 : 1) + (i % 3) * Mathf.PI * 2 / 3 + settings.phase * Mathf.Deg2Rad;
                            Vector2 weave = HairFormUtility.Weave(phase);
                            float taper = Mathf.Lerp(1f, settings.tipScale, Mathf.SmoothStep(0, 1, t));
                            normal = (sides[p] * Mathf.Cos(angle) + normals[p] * Mathf.Sin(angle)).normalized;
                            point = rail[p] + taper * (sides[p] * weave.x * braidRadius + normals[p] * weave.y * braidRadius * settings.braidDepth + normal * (radius + fly * Mathf.Sin(t * Mathf.PI)));
                            if (settings.flipFacing) normal = -normal;
                        }
                        float scale = group.profile != null && group.profile.DefaultWidth > 1e-7f ? width / group.profile.DefaultWidth : 1f;
                        curve.points.Add(new HairCurvePoint(point, width, 0, width, scale) { facingNormal = normal, facingWeight = 1 });
                    }
                    StabilizeFacing(curve);
                    if (source == HairPopulationSource.GridPanels)
                    {
                        // Offset only after resolving the continuous surface-facing hemisphere.
                        // Near a folded grid derivative, offsetting the raw normal first would
                        // jump the centerline between opposite sides of the panel.
                        for (int p = 0; p < curve.points.Count; p++)
                        {
                            var point = curve.points[p];
                            float t = rootStart > 0f ? Mathf.Lerp(rootStart, length, p / (curve.points.Count - 1f)) : length * p / (curve.points.Count - 1f);
                            point.position += point.facingNormal * (offset + fly * Mathf.Sin(t * Mathf.PI));
                            curve.points[p] = point;
                        }
                        StabilizeFacing(curve);
                    }
                    curve.rootNormal = curve.points[0].facingNormal;
                    // A form may begin away from skin (bun or extension). Its anchor is only for
                    // skinning/pose transfer; do not teleport its authored root onto the scalp.
                    curve.rootAnchor = default;
                    if (workspace.sourceMesh.TryFindClosestSurface(groom.SourceMeshId, curve.points[0].position, out var anchor)) curve.rootAnchor = anchor;
                    if (settings.usePaintedRootDensity)
                    {
                        var fields = workspace.populations.Fields(group);
                        float density = curve.rootAnchor.IsValid ? Mathf.Clamp01(fields.Sample(group.FindMap(HairMapKind.GrowthArea), curve.rootAnchor, 0f)) : 0f;
                        // Separate random stream: changing a mask never changes surviving curves or LOD identity.
                        var rootRandom = new HairDeterministicRandom(seed ^ 0x6ac690c5);
                        if (density <= 0f || rootRandom.Next01() >= density) continue;
                        curve.maskValue = density;
                    }
                    output.Add(curve);
                }
            }
        }
        private void PrepareRail(HairHelper helper, int samples, bool smooth)
        {
            path.Clear();
            for (int i = 0; i < 513; i++) path.Add(new HairCurvePoint(spline.Sample(i / 512f, smooth), 0, 0));
            HairCurveUtility.ResampleInto(path, samples, sampled, ref cumulative);
            rail.Clear(); sides.Clear(); normals.Clear();
            Vector3 lastTangent = Vector3.zero, side = Vector3.zero;
            for (int i = 0; i < samples; i++)
            {
                Vector3 tangent = sampled[Mathf.Min(samples - 1, i + 1)].position - sampled[Mathf.Max(0, i - 1)].position;
                tangent = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : (lastTangent.sqrMagnitude > 0 ? lastTangent : Vector3.up);
                if (i == 0) side = Vector3.ProjectOnPlane(spline.frame * Vector3.right, tangent).normalized;
                else side = Quaternion.FromToRotation(lastTangent, tangent) * side;
                if (side.sqrMagnitude < .1f) side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < .9f ? Vector3.up : Vector3.forward).normalized;
                side = Vector3.ProjectOnPlane(side, tangent).normalized;
                rail.Add(sampled[i].position); sides.Add(side); normals.Add(Vector3.Cross(tangent, side).normalized); lastTangent = tangent;
            }
        }
        private void PreparePanel(HairHelper helper, float u, bool smooth)
        {
            // A card has constant U. Factor the tensor surface into two row curves once,
            // rather than evaluating the entire grid five times for every curve point.
            panelRows.Clear(); panelAcross.Clear(); panelOrigin = helper.points[0];
            for (int row = 0; row < helper.gridRows; row++)
            {
                rowControls.Clear();
                for (int column = 0; column < helper.gridColumns; column++) rowControls.Add(helper.points[row * helper.gridColumns + column] - panelOrigin);
                panelRows.Add(HairFormUtility.RailPoint(rowControls, u, smooth));
                panelAcross.Add(HairFormUtility.RailTangent(rowControls, u, smooth));
            }
        }
        private static void StabilizeFacing(HairEvaluatedCurve curve)
        {
            Vector3 previousTangent = Vector3.zero, previousNormal = Vector3.zero;
            for (int i = 0; i < curve.points.Count; i++)
            {
                var point = curve.points[i]; Vector3 tangent = HairCurveUtility.CalculateTangent(curve.points, i);
                Vector3 desired = Vector3.ProjectOnPlane(point.facingNormal, tangent).normalized;
                Vector3 transported = i > 0 ? Quaternion.FromToRotation(previousTangent, tangent) * previousNormal : desired;
                if (desired.sqrMagnitude < .1f) desired = transported;
                // A folded/near-singular grid derivative has two equivalent ribbon axes.
                // Keep the transported hemisphere so a collapsed strip cannot flip its UVs.
                if (i > 0 && Vector3.Dot(desired, transported) < 0) desired = -desired;
                point.facingNormal = desired; curve.points[i] = point;
                previousTangent = tangent; previousNormal = desired;
            }
        }
        private static int Hash(string text)
        {
            unchecked { int hash = (int)2166136261; foreach (char c in text ?? string.Empty) hash = (hash ^ c) * 16777619; return hash; }
        }
    }
}
