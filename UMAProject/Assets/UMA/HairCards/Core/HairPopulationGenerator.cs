using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    public sealed class HairPopulationInfo
    {
        public string stageId, name;
        public int inputCount, requestedCount, surfaceRoots, outputCount, rejectedInfluences;
        public bool rootsReused, neighborsReused;
        public double milliseconds;
        public double interpolationMilliseconds, modifierMilliseconds;
    }

    internal sealed class HairPopulationWorkspace
    {
        internal sealed class StageCache
        {
            internal int fieldRevision = -1, count, seed;
            internal float spacing, uniformity;
            internal HairGuideGenerationResult roots;
            internal readonly HairPointSpatialIndex index = new HairPointSpatialIndex();
            internal readonly List<Vector3> parentRoots = new List<Vector3>();
            internal readonly List<List<HairCurvePoint>> samples = new List<List<HairCurvePoint>>();
            internal readonly List<HairEvaluatedCurve> output = new List<HairEvaluatedCurve>();
            internal float[] cumulative = Array.Empty<float>();
            internal int[] neighborCounts = Array.Empty<int>(), neighborIndices = Array.Empty<int>();
            internal float[] neighborWeights = Array.Empty<float>();
            internal readonly List<Vector3> influenceRoots = new List<Vector3>(), influenceNormals = new List<Vector3>();
            internal readonly List<string> influenceIds = new List<string>();
            internal readonly List<int> parentRegions = new List<int>();
            internal readonly List<float> parentParts = new List<float>(), partValues = new List<float>();
            internal int influenceRevision = -1, influenceNeighbors;
            internal float influenceRadius, influenceNormal;

            internal bool PrepareInfluences(IReadOnlyList<HairEvaluatedCurve> parents, HairGenerationStage settings,
                HairSurfaceFields fields, HairGrowthMap barrier, bool sameRoots)
            {
                bool same = sameRoots && influenceRevision == fields.Revision && influenceNeighbors == settings.neighbors &&
                    influenceRadius == settings.influenceRadius && influenceNormal == settings.minimumNormalDot &&
                    influenceRoots.Count == parents.Count && partValues.Count == (barrier?.values?.Length ?? 0);
                for (int i = 0; same && i < partValues.Count; i++) same = partValues[i] == barrier.values[i];
                for (int i = 0; same && i < parents.Count; i++)
                    same = influenceRoots[i].Equals(parents[i].points[0].position) && influenceNormals[i].Equals(parents[i].rootNormal) &&
                        influenceIds[i] == parents[i].curveId && parentRegions[i] == fields.Region(parents[i].rootAnchor) && parentParts[i] == fields.Sample(barrier, parents[i].rootAnchor);
                if (same) return true;
                influenceRevision = fields.Revision; influenceNeighbors = settings.neighbors;
                influenceRadius = settings.influenceRadius; influenceNormal = settings.minimumNormalDot;
                influenceRoots.Clear(); influenceNormals.Clear(); influenceIds.Clear(); parentRegions.Clear(); parentParts.Clear(); partValues.Clear();
                if (barrier?.values != null) partValues.AddRange(barrier.values);
                foreach (var parent in parents)
                {
                    influenceRoots.Add(parent.points[0].position); influenceNormals.Add(parent.rootNormal); influenceIds.Add(parent.curveId);
                    parentRegions.Add(fields.Region(parent.rootAnchor)); parentParts.Add(fields.Sample(barrier, parent.rootAnchor));
                }
                int count = roots.guides.Count;
                if (neighborCounts.Length < count)
                {
                    int capacity = Mathf.NextPowerOfTwo(Mathf.Max(16, count));
                    Array.Resize(ref neighborCounts, capacity); Array.Resize(ref neighborIndices, capacity * 8); Array.Resize(ref neighborWeights, capacity * 8);
                }
                Array.Fill(neighborCounts, -1); return false;
            }
        }
        private readonly Dictionary<string, HairSurfaceFields> fields = new Dictionary<string, HairSurfaceFields>();
        private readonly Dictionary<string, StageCache> stages = new Dictionary<string, StageCache>();
        private readonly HashSet<string> live = new HashSet<string>();
        private readonly List<string> removed = new List<string>();
        internal void Prune(HairGroomAsset groom)
        {
            live.Clear();
            foreach (var group in groom.Groups)
            {
                if (group == null) continue;
                live.Add(group.Id);
                foreach (var stage in group.generation.clumps) if (stage != null) live.Add(stage.Id);
                live.Add(group.generation.cards.Id);
            }
            removed.Clear(); foreach (var key in stages.Keys) if (!live.Contains(key)) removed.Add(key);
            foreach (var key in removed) stages.Remove(key);
            removed.Clear(); foreach (var key in fields.Keys) if (!live.Contains(key)) removed.Add(key);
            foreach (var key in removed) fields.Remove(key);
            removed.Clear(); live.Clear();
        }
        internal HairSurfaceFields Fields(HairGroup group)
        {
            if (!fields.TryGetValue(group.Id, out var value)) fields.Add(group.Id, value = new HairSurfaceFields());
            return value;
        }
        internal StageCache Stage(HairGenerationStage stage)
        {
            if (!stages.TryGetValue(stage.Id, out var value)) stages.Add(stage.Id, value = new StageCache());
            return value;
        }
        internal void Clear() { fields.Clear(); stages.Clear(); live.Clear(); removed.Clear(); }
    }

    public static class HairPopulationGenerator
    {
        private static readonly ProfilerMarker Marker = new ProfilerMarker("HairCards.GeneratePopulation");
        internal static void Generate(HairGroomAsset groom, HairGroup group, IReadOnlyList<HairEvaluatedCurve> guides,
            HairLodSettings lod, HairEvaluationOptions options, HairEvaluationResult result, HairEvaluationWorkspace workspace)
        {
            if (!options.includeChildren || guides.Count == 0) return;
            IReadOnlyList<HairEvaluatedCurve> source = guides;
            HairSurfaceFields fields = workspace.populations.Fields(group);
            foreach (var stage in group.generation.clumps)
            {
                if (stage == null || !stage.enabled) continue;
                source = GenerateStage(groom, group, stage, source, fields, result, workspace, false, lod);
                result.generatedGuides.AddRange(source);
                if (options.previewGenerationStageId == stage.Id)
                { result.curves.AddRange(source); result.childCurveCount += source.Count; return; }
            }
            var cards = group.generation.cards;
            if (!cards.enabled) return;
            var output = GenerateStage(groom, group, cards, source, fields, result, workspace, true, lod);
            var childModifiers = options.applyModifiers ? HairGroomEvaluator.ChildModifiers(group, options.soloLayerId, options.EditLayerFor(group)) : null;
            HairGroomEvaluator.ApplyPopulationModifiers(childModifiers, output, HairModifierDomain.Children, groom, workspace);
            foreach (var curve in output)
            {
                result.curves.Add(curve); result.childCurveCount++;
            }
        }

        private static IReadOnlyList<HairEvaluatedCurve> GenerateStage(HairGroomAsset groom, HairGroup group, HairGenerationStage settings,
            IReadOnlyList<HairEvaluatedCurve> parents, HairSurfaceFields fields, HairEvaluationResult result,
            HairEvaluationWorkspace workspace, bool final, HairLodSettings lod)
        {
            using var scope = Marker.Auto();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var cache = workspace.populations.Stage(settings); cache.output.Clear();
            var info = new HairPopulationInfo { stageId = settings.Id, name = settings.name, inputCount = parents.Count, requestedCount = settings.count };
            result.populations.Add(info);
            if (parents.Count == 0) return cache.output;
            bool reuse = cache.roots != null && cache.fieldRevision == fields.Revision && cache.count == settings.count &&
                cache.seed == settings.seed && cache.spacing == settings.minimumSpacing && cache.uniformity == settings.uniformity;
            if (!reuse)
            {
                cache.roots = HairGuideGenerator.Generate(groom, group, new HairGuideGenerationSettings
                {
                    guideCount = settings.count, pointsPerGuide = 2, defaultLength = 0.1f,
                    minimumRootSpacing = settings.minimumSpacing, rootUniformity = settings.uniformity, seed = settings.seed,
                    maximumAttemptsPerGuide = 12
                });
                cache.fieldRevision = fields.Revision; cache.count = settings.count; cache.seed = settings.seed;
                cache.spacing = settings.minimumSpacing; cache.uniformity = settings.uniformity;
            }
            info.rootsReused = reuse; info.surfaceRoots = cache.roots.guides.Count;
            if (info.surfaceRoots == 0)
            { result.warnings.Add($"{settings.name}: no roots. Paint Growth / Density, or reduce minimum spacing."); return cache.output; }
            cache.parentRoots.Clear();
            while (cache.samples.Count < parents.Count) cache.samples.Add(new List<HairCurvePoint>());
            for (int i = 0; i < parents.Count; i++)
            {
                cache.parentRoots.Add(parents[i].points[0].position);
                HairCurveUtility.ResampleInto(parents[i].points, settings.shapeSamples, cache.samples[i], ref cache.cumulative);
            }
            cache.index.Rebuild(cache.parentRoots);
            HairGrowthMap barrier = string.IsNullOrEmpty(settings.partMapId) ? null : group.maps.Find(m => m?.Id == settings.partMapId);
            if (!string.IsNullOrEmpty(settings.partMapId) && barrier == null)
            {
                result.warnings.Add($"{settings.name}: missing Part Regions map. Choose a replacement; generation is paused to avoid blending across an unknown part.");
                return cache.output;
            }
            info.neighborsReused = cache.PrepareInfluences(parents, settings, fields, barrier, reuse);
            int samples = settings.shapeSamples;
            Span<HairNearestPoint> candidates = stackalloc HairNearestPoint[64];
            Span<int> neighbors = stackalloc int[8]; Span<float> weights = stackalloc float[8];
            Span<Quaternion> transport = stackalloc Quaternion[8];
            Span<float> clumpWeights = stackalloc float[32], rootWeights = stackalloc float[32];
            for (int p = 0; p < samples; p++)
            {
                float t = p / (samples - 1f);
                rootWeights[p] = HairGuideShapeUtility.RootInfluence(t, settings.rootInfluence);
                clumpWeights[p] = Mathf.Clamp01(settings.clumpAlongStrand.Evaluate(t)) * settings.clump * rootWeights[p];
            }
            int cardSamples = lod != null ? lod.ResolveSampleCount(group.profile) : group.profile != null ? group.profile.SamplesPerCard : 12;
            if (workspace.options.previewSampleCount > 1) cardSamples = Mathf.Min(cardSamples, workspace.options.previewSampleCount);
            float fraction = final && lod != null ? lod.cardFraction : 1f;
            long interpolationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int ordinal = 0; ordinal < cache.roots.guides.Count; ordinal++)
            {
                var root = cache.roots.guides[ordinal].root;
                int seed = unchecked(settings.seed * 486187739 + ordinal * 16777619);
                var random = new HairDeterministicRandom(seed);
                float lodValue = random.Next01(), edgeValue = random.Next01();
                float borderDistance = fields.Distance(root);
                var hairline = settings.hairline;
                float interior = hairline.enabled ? Mathf.SmoothStep(0f, 1f, borderDistance / Mathf.Max(0.001f, hairline.distance)) : 1f;
                if (edgeValue > Mathf.Lerp(Mathf.Clamp01(hairline.edgeDensity), 1f, interior) || lodValue > fraction) continue;
                if (final && workspace.options.interactiveSampleLimit > 0 && cache.output.Count + result.curves.Count >= workspace.options.interactiveSampleLimit) break;
                int n = cache.neighborCounts[ordinal], firstNeighbor = ordinal * 8;
                if (n >= 0)
                {
                    for (int i = 0; i < n; i++) { neighbors[i] = cache.neighborIndices[firstNeighbor + i]; weights[i] = cache.neighborWeights[firstNeighbor + i]; }
                }
                else
                {
                    int candidateCount = cache.index.QueryNearest(root.CachedLocalPosition, candidates);
                    n = 0; float total = 0f; int rootRegion = fields.Region(root); float rootPart = fields.Sample(barrier, root);
                    for (int i = 0; i < candidateCount && n < settings.neighbors; i++)
                    {
                        var candidate = candidates[i]; var parent = parents[candidate.index]; int parentRegion = cache.parentRegions[candidate.index];
                        if (candidate.distanceSquared > settings.influenceRadius * settings.influenceRadius ||
                            Vector3.Dot(root.CachedLocalNormal, parent.rootNormal) < settings.minimumNormalDot ||
                            (rootRegion >= 0 && parentRegion >= 0 && rootRegion != parentRegion) ||
                            (barrier != null && Mathf.Abs(rootPart - cache.parentParts[candidate.index]) > 0.5f)) continue;
                        float weight = 1f / Mathf.Max(0.0001f, Mathf.Sqrt(candidate.distanceSquared));
                        neighbors[n] = candidate.index; weights[n++] = weight; total += weight;
                    }
                    cache.neighborCounts[ordinal] = n;
                    for (int i = 0; i < n; i++)
                    {
                        weights[i] /= total; cache.neighborIndices[firstNeighbor + i] = neighbors[i]; cache.neighborWeights[firstNeighbor + i] = weights[i];
                    }
                }
                if (n == 0) { info.rejectedInfluences++; continue; }
                for (int i = 0; i < n; i++) transport[i] = Quaternion.SlerpUnclamped(Quaternion.identity,
                    Quaternion.FromToRotation(parents[neighbors[i]].rootNormal, root.CachedLocalNormal), settings.surfaceConform);
                var center = parents[neighbors[0]]; var centerSamples = cache.samples[neighbors[0]];
                var parentRandom = new HairDeterministicRandom(center.seed ^ settings.seed);
                float variation = Mathf.Lerp(random.NextSigned(), parentRandom.NextSigned(), settings.parentCoherence);
                float lengthScale = (1f + variation * settings.lengthVariation) * Mathf.Lerp(hairline.edgeLength, 1f, interior);
                float widthScale = (1f + Mathf.Lerp(random.NextSigned(), parentRandom.NextSigned(), settings.parentCoherence) * settings.widthVariation) * Mathf.Lerp(hairline.edgeWidth, 1f, interior);
                float tilt = Mathf.Lerp(random.NextSigned(), parentRandom.NextSigned(), settings.parentCoherence) * settings.tiltVariation;
                bool flyaway = random.Next01() < settings.flyawayFraction;
                var curve = result.RentCurve(samples);
                if (curve.generationStageId != settings.Id || curve.childOrdinal != ordinal)
                    curve.curveId = settings.Id + ":" + ordinal;
                curve.childOrdinal = ordinal; curve.generationStageId = settings.Id; curve.parentGuideId = center.parentGuideId;
                curve.clumpId = center.curveId; curve.clumpSeed = center.seed; curve.seed = seed; curve.rootAnchor = root;
                curve.groupId = group.Id; curve.isChild = true; curve.rootNormal = root.CachedLocalNormal;
                curve.rootEmbedDepth = group.rootEmbedDepth; curve.profile = group.profile; curve.atlas = group.atlas;
                curve.atlasRegionSelection = center.atlasRegionSelection; curve.atlasRegionIds = center.atlasRegionIds;
                curve.groupColor = group.color; curve.maskValue = 0f; curve.hairlineDistance = borderDistance;
                for (int i = 0; i < n; i++) curve.maskValue += parents[neighbors[i]].maskValue * weights[i];
                curve.samplesPerCardOverride = final ? cardSamples : samples; curve.tubeSidesOverride = lod?.maximumTubeSides ?? 12;
                curve.ribbonReduction = center.ribbonReduction;
                Vector3 inward = fields.Inward(root);
                Vector3 rootSeparation = root.CachedLocalPosition - centerSamples[0].position;
                for (int p = 0; p < samples; p++)
                {
                    float t = p / (samples - 1f);
                    Vector3 offset = Vector3.zero, facing = Vector3.zero; float width = 0f, baseline = 0f, scale = 0f, stiffness = 0f, freeze = 0f, roll = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var points = cache.samples[neighbors[i]]; var point = points[p]; float w = weights[i];
                        offset += transport[i] * (point.position - points[0].position) * w;
                        width += point.width * w; baseline += point.widthBaseline * w; scale += point.profileScale * w;
                        stiffness += point.stiffness * w; freeze += point.freeze * w;
                        roll += Mathf.DeltaAngle(centerSamples[p].roll, point.roll) * w;
                        facing += transport[i] * (point.facingWeight > 0f ? point.facingNormal : parents[neighbors[i]].rootNormal) * w;
                    }
                    Vector3 face = facing.normalized;
                    if (interior < 1f && hairline.alignFacing > 0f) face = Vector3.Slerp(face, root.CachedLocalNormal, (1f - interior) * hairline.alignFacing);
                    curve.points.Add(new HairCurvePoint(root.CachedLocalPosition + offset * lengthScale,
                        width * widthScale, centerSamples[p].roll + roll + tilt * t,
                        baseline < 0f ? -1f : baseline * widthScale, scale * widthScale, stiffness, freeze)
                    { facingNormal = face, facingWeight = 1f });
                }
                HairGroomEvaluator.CaptureModifierShape(curve, workspace);
                float strandLength = curve.Length;
                for (int p = 1; p < samples; p++)
                {
                    float t = p / (samples - 1f); var point = curve.points[p];
                    float influence = rootWeights[p];
                    float clump = clumpWeights[p] * (flyaway ? 0.25f : 1f);
                    clump *= 1f - settings.splitTips * Mathf.SmoothStep(0f, 1f, (t - .7f) / .3f);
                    // At zero clump retain blended flow at the new scalp root. At full clump
                    // follow a CURVED parent's centerline, not a group-wide average tip.
                    Vector3 target = centerSamples[0].position + (centerSamples[p].position - centerSamples[0].position) * lengthScale;
                    target += rootSeparation * settings.clumpSpread;
                    point.position = Vector3.Lerp(point.position, target, clump);
                    point.position += inward * ((1f - interior) * hairline.inwardLean * strandLength * t * influence);
                    if (flyaway) point.position += HairStrandNoise.Sample(t * 3f, seed) * (settings.flyawayAmplitude * t * influence);
                    curve.points[p] = point;
                }
                HairGroomEvaluator.FinishModifierShape(curve, workspace);
                cache.output.Add(curve);
            }
            long modifierStart = System.Diagnostics.Stopwatch.GetTimestamp();
            info.interpolationMilliseconds = (modifierStart - interpolationStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (workspace.options.applyModifiers && workspace.options.EditLayerFor(group) == null)
                HairGroomEvaluator.ApplyPopulationModifiers(settings.modifiers, cache.output, HairModifierDomain.Children, groom, workspace, true);
            info.modifierMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - modifierStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            info.outputCount = cache.output.Count;
            info.milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (info.rejectedInfluences > 0)
                result.warnings.Add($"{settings.name}: {info.rejectedInfluences:N0} roots have no compatible guide. Add guides there, increase Influence Radius, or review Part Regions.");
            return cache.output;
        }
    }

    /// <summary>Continuous deterministic value noise in strand arc length, independent of tessellation.</summary>
    public static class HairStrandNoise
    {
        public static Vector3 Sample(float coordinate, int seed)
        {
            int cell = Mathf.FloorToInt(coordinate); float t = coordinate - cell;
            t = t * t * t * (t * (t * 6f - 15f) + 10f);
            Vector3 Value(int i)
            {
                var rng = new HairDeterministicRandom(unchecked(seed ^ i * 73856093));
                return new Vector3(rng.NextSigned(), rng.NextSigned(), rng.NextSigned());
            }
            return Vector3.LerpUnclamped(Value(cell), Value(cell + 1), t);
        }
    }
}
