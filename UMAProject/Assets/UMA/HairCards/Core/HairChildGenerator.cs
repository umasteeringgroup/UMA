using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    public static class HairChildGenerator
    {
        private const int MaximumNeighborCount = 4;
        private static readonly ProfilerMarker GenerationMarker = new ProfilerMarker("HairCards.GenerateChildren");

        internal sealed class GenerationWorkspace
        {
            internal readonly Dictionary<string, HairGuide> sourceGuides = new Dictionary<string, HairGuide>(StringComparer.Ordinal);
            private readonly List<Vector3> roots = new List<Vector3>();
            private readonly HairPointSpatialIndex rootIndex = new HairPointSpatialIndex();
            private readonly List<List<HairCurvePoint>> samples = new List<List<HairCurvePoint>>();
            private bool[] sampled = Array.Empty<bool>();
            private float[] cumulative = Array.Empty<float>();
            private bool indexed;

            internal void BeginGroup(HairGroup group, int guideCount)
            {
                sourceGuides.Clear();
                if (group.guides != null)
                    foreach (HairGuide guide in group.guides)
                        if (guide != null && !string.IsNullOrEmpty(guide.Id) && !sourceGuides.ContainsKey(guide.Id)) sourceGuides.Add(guide.Id, guide);
                if (sampled.Length < guideCount) Array.Resize(ref sampled, Mathf.NextPowerOfTwo(Mathf.Max(16, guideCount)));
                Array.Clear(sampled, 0, guideCount);
                while (samples.Count < guideCount) samples.Add(null);
                indexed = false;
            }

            internal List<HairCurvePoint> Samples(IReadOnlyList<HairEvaluatedCurve> guides, int index, int count)
            {
                if (!sampled[index])
                {
                    samples[index] ??= new List<HairCurvePoint>(count);
                    HairCurveUtility.ResampleInto(guides[index].points, count, samples[index], ref cumulative);
                    sampled[index] = true;
                }
                return samples[index];
            }

            internal int FindNearest(IReadOnlyList<HairEvaluatedCurve> guides, Vector3 root, Span<HairNearestPoint> result)
            {
                if (!indexed)
                {
                    roots.Clear();
                    for (int i = 0; i < guides.Count; i++) roots.Add(guides[i]?.points.Count > 0 ? guides[i].points[0].position : new Vector3(float.NaN, 0f, 0f));
                    rootIndex.Rebuild(roots);
                    indexed = true;
                }
                return rootIndex.QueryNearest(root, result);
            }

            internal void Clear()
            {
                sourceGuides.Clear(); sourceGuides.TrimExcess(); roots.Clear(); roots.Capacity = 0; rootIndex.Clear();
                samples.Clear(); samples.Capacity = 0; sampled = Array.Empty<bool>(); cumulative = Array.Empty<float>(); indexed = false;
            }
        }

        public static void Generate(
            HairGroomAsset groom,
            HairGroup group,
            IReadOnlyList<HairEvaluatedCurve> guides,
            HairLodSettings lod,
            HairEvaluationOptions options,
            HairEvaluationResult result)
        {
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            workspace.options = options ?? new HairEvaluationOptions();
            workspace.sourceMesh.Begin(groom?.SourceMesh);
            workspace.gravitySurface.Begin(workspace.options.gravityCollisionMesh != null ? workspace.options.gravityCollisionMesh : groom?.SourceMesh);
            try { Generate(groom, group, guides, lod, options, result, workspace); }
            finally { workspace.sourceMesh.End(); workspace.gravitySurface.End(); workspace.children.sourceGuides.Clear(); }
        }

        internal static void Generate(HairGroomAsset groom, HairGroup group,
            IReadOnlyList<HairEvaluatedCurve> guides, HairLodSettings lod, HairEvaluationOptions options,
            HairEvaluationResult result, HairEvaluationWorkspace workspace)
        {
            using var generationScope = GenerationMarker.Auto();
            if (group == null || guides == null || result == null) return;
            options ??= new HairEvaluationOptions();
            if (!options.includeGuideCards && !options.includeChildren) return;
            IReadOnlyList<HairModifierSettings> childModifiers = options.applyModifiers && options.includeChildren
                ? HairGroomEvaluator.ChildModifiers(group, options.soloLayerId) : null;
            float cardFraction = lod != null ? lod.cardFraction : 1f;
            int sampleCount = lod != null ? lod.ResolveSampleCount(group.profile) :
                Mathf.Max(2, group.profile != null ? group.profile.SamplesPerCard : 12);
            if (options.previewSampleCount > 1) sampleCount = Mathf.Min(sampleCount, options.previewSampleCount);
            int tubeSides = lod != null ? lod.maximumTubeSides : 12;
            GenerationWorkspace generation = workspace.children;
            generation.BeginGroup(group, guides.Count);
            Dictionary<string, HairGuide> sourceGuides = generation.sourceGuides;
            for (int guideIndex = 0; guideIndex < guides.Count; guideIndex++)
            {
                if (options.interactiveSampleLimit > 0 && result.curves.Count >= options.interactiveSampleLimit)
                {
                    result.warnings.Add("Interactive preview card limit reached; the release bake remains complete.");
                    return;
                }
                HairEvaluatedCurve guideCurve = guides[guideIndex];
                if (!sourceGuides.TryGetValue(guideCurve.parentGuideId, out HairGuide sourceGuide)) continue;
                float lodImportance = sourceGuide.lodImportance *
                                      SampleRootMap(groom, group, sourceGuide, HairMapKind.LodImportance, 1f,
                                          workspace.sourceMesh);
                float guideLodFraction = Mathf.Clamp01(cardFraction * Mathf.Lerp(0.25f, 1.5f,
                    Mathf.Clamp01(lodImportance)));

                bool includeGuide = options.includeGuideCards && sourceGuide.includeGuideCard &&
                                    group.children.includeGuideCard && KeepForLod(sourceGuide.seed, guideLodFraction);
                if (includeGuide)
                {
                    HairEvaluatedCurve card = result.RentCurve(guideCurve.points.Count);
                    string id = card.parentGuideId == sourceGuide.Id && !card.isChild && card.curveId != sourceGuide.Id
                        ? card.curveId : $"{sourceGuide.Id}:guide";
                    guideCurve.CopyTo(card, id);
                    card.isChild = false;
                    card.samplesPerCardOverride = sampleCount;
                    card.tubeSidesOverride = tubeSides;
                    result.curves.Add(card);
                    result.guideCurveCount++;
                }

                if (!options.includeChildren) continue;
                int childCount = sourceGuide.overrideChildCount ? sourceGuide.childCount : group.children.childrenPerGuide;
                float childMultiplier = SampleRootMap(groom, group, sourceGuide, HairMapKind.ChildCount, 1f,
                    workspace.sourceMesh);
                childCount = Mathf.Max(0, Mathf.RoundToInt(childCount * Mathf.Max(0f, childMultiplier)));
                float paintedClump = Mathf.Clamp01(SampleRootMap(groom, group, sourceGuide,
                    HairMapKind.Clump, group.children.clump, workspace.sourceMesh));
                for (int childIndex = 0; childIndex < childCount; childIndex++)
                {
                    if (options.interactiveSampleLimit > 0 && result.curves.Count >= options.interactiveSampleLimit)
                    {
                        result.warnings.Add("Interactive preview card limit reached; the release bake remains complete.");
                        return;
                    }
                    int childSeed = CombineSeed(group.children.seed, sourceGuide.seed, childIndex);
                    if (!KeepForLod(childSeed, guideLodFraction)) continue;
                    HairEvaluatedCurve child = CreateChild(group, guides, generation, guideIndex, sourceGuide,
                        childIndex, childSeed, sampleCount, paintedClump, result.RentCurve(sampleCount));
                    child.samplesPerCardOverride = sampleCount;
                    child.tubeSidesOverride = tubeSides;
                    if (options.applyModifiers)
                        HairGroomEvaluator.ApplyModifiers(childModifiers, child, HairModifierDomain.Children, groom, workspace);
                    if (child.points.Count < 2 || child.Length < 1e-6f)
                    {
                        result.rejectedCurveCount++;
                        continue;
                    }
                    result.curves.Add(child);
                    result.childCurveCount++;
                }
            }
        }

        private static HairEvaluatedCurve CreateChild(
            HairGroup group,
            IReadOnlyList<HairEvaluatedCurve> guides,
            GenerationWorkspace generation,
            int parentIndex,
            HairGuide sourceGuide,
            int childIndex,
            int seed,
            int sampleCount,
            float clump,
            HairEvaluatedCurve child)
        {
            HairEvaluatedCurve parent = guides[parentIndex];
            HairDeterministicRandom random = new HairDeterministicRandom(seed);
            Vector2 disk = random.NextInUnitDisk() * group.children.rootSpread;
            Vector3 rootTangent = HairCurveUtility.CalculateTangent(parent.points, 0);
            Vector3 rootNormal = Vector3.ProjectOnPlane(parent.rootNormal, rootTangent).normalized;
            if (rootNormal.sqrMagnitude < 1e-8f) rootNormal = Vector3.up;
            Vector3 rootSide = Vector3.Cross(rootTangent, rootNormal).normalized;
            if (rootSide.sqrMagnitude < 1e-8f) rootSide = Vector3.right;
            Vector3 rootOffset = rootSide * disk.x + rootNormal * disk.y;
            Vector3 targetRoot = parent.points[0].position + rootOffset;

            Span<Neighbor> neighborBuffer = stackalloc Neighbor[MaximumNeighborCount];
            int neighborCount = FindNeighbors(guides, generation, parentIndex, targetRoot,
                group.children.interpolation, neighborBuffer);
            ReadOnlySpan<Neighbor> neighbors = neighborBuffer.Slice(0, neighborCount);

            float lengthScale = 1f + random.NextSigned() * group.children.lengthVariation;
            float widthScale = 1f + random.NextSigned() * group.children.widthVariation;
            float rollOffset = random.NextSigned() * group.children.rollVariation * 180f;
            // Keep the stable identifier when this pooled slot still represents the same child.
            if (!child.isChild || child.parentGuideId != sourceGuide.Id || child.childOrdinal != childIndex)
                child.curveId = $"{sourceGuide.Id}:child:{childIndex}";
            child.childOrdinal = childIndex;
            child.parentGuideId = sourceGuide.Id; child.groupId = group.Id; child.isChild = true;
            child.seed = seed; child.groupColor = group.color;
            child.rootNormal = BlendRootNormal(guides, neighbors, parent.rootNormal);
            child.rootEmbedDepth = group.rootEmbedDepth; child.profile = group.profile; child.atlas = group.atlas;
            child.atlasRegionSelection = parent.atlasRegionSelection; child.atlasRegionIds = parent.atlasRegionIds;

            Vector3 weightedRoot = Vector3.zero;
            for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
            {
                Neighbor neighbor = neighbors[neighborIndex];
                weightedRoot += generation.Samples(guides, neighbor.index, sampleCount)[0].position * neighbor.weight;
            }
            for (int pointIndex = 0; pointIndex < sampleCount; pointIndex++)
            {
                Vector3 weightedPosition = Vector3.zero;
                float weightedWidth = 0f;
                float weightedBaseline = 0f, weightedProfileScale = 0f, weightedStiffness = 0f, weightedFreeze = 0f;
                bool hasBaseline = true;
                float weightedRoll = 0f;
                for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
                {
                    Neighbor neighbor = neighbors[neighborIndex];
                    HairCurvePoint sample = generation.Samples(guides, neighbor.index, sampleCount)[pointIndex];
                    float weight = neighbor.weight;
                    weightedPosition += sample.position * weight;
                    weightedWidth += sample.width * weight;
                    weightedBaseline += sample.widthBaseline * weight;
                    weightedProfileScale += sample.profileScale * weight;
                    weightedStiffness += sample.stiffness * weight;
                    weightedFreeze += sample.freeze * weight;
                    hasBaseline &= sample.widthBaseline >= 0f;
                    weightedRoll += sample.roll * weight;
                }

                float t = pointIndex / (sampleCount - 1f);
                Vector3 relative = (weightedPosition - weightedRoot) * lengthScale;
                Vector3 lateral = rootOffset * (1f - clump * t);
                child.points.Add(new HairCurvePoint(
                    targetRoot + relative + lateral - rootOffset,
                    hasBaseline ? weightedWidth * widthScale : Mathf.Max(0f, weightedWidth * widthScale),
                    weightedRoll + rollOffset * t,
                    hasBaseline ? weightedBaseline * widthScale : -1f,
                    weightedProfileScale * widthScale, weightedStiffness, weightedFreeze));
            }
            return child;
        }

        private static Vector3 BlendRootNormal(
            IReadOnlyList<HairEvaluatedCurve> guides,
            ReadOnlySpan<Neighbor> neighbors,
            Vector3 fallback)
        {
            Vector3 reference = fallback.sqrMagnitude > 1e-8f ? fallback.normalized : Vector3.up;
            Vector3 blended = Vector3.zero;
            for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
            {
                Neighbor neighbor = neighbors[neighborIndex];
                Vector3 normal = guides[neighbor.index].rootNormal;
                if (normal.sqrMagnitude < 1e-8f) normal = reference;
                else normal.Normalize();
                if (Vector3.Dot(normal, reference) < 0f) normal = -normal;
                blended += normal * neighbor.weight;
            }
            return blended.sqrMagnitude > 1e-8f ? blended.normalized : reference;
        }

        private static float SampleRootMap(HairGroomAsset groom, HairGroup group, HairGuide guide,
            HairMapKind kind, float fallback, HairGroomEvaluator.SourceMeshReadCache sourceMesh)
        {
            HairGrowthMap map = group.FindMap(kind);
            HairSurfaceAnchor root = guide.root;
            if (map == null || groom?.SourceMesh == null || !root.IsValid ||
                root.SubmeshIndex < 0 || root.SubmeshIndex >= groom.SourceMesh.subMeshCount) return fallback;
            IReadOnlyList<int> triangles = sourceMesh.Triangles(root.SubmeshIndex);
            int offset = root.TriangleIndex * 3;
            if (triangles == null || offset < 0 || offset + 2 >= triangles.Count) return fallback;
            Vector3 barycentric = root.Barycentric;
            return map.SampleVertex(triangles[offset]) * barycentric.x +
                   map.SampleVertex(triangles[offset + 1]) * barycentric.y +
                   map.SampleVertex(triangles[offset + 2]) * barycentric.z;
        }

        private static int FindNeighbors(
            IReadOnlyList<HairEvaluatedCurve> guides,
            GenerationWorkspace generation,
            int parentIndex,
            Vector3 childRoot,
            HairGuideInterpolationMode mode, Span<Neighbor> candidates)
        {
            if (mode == HairGuideInterpolationMode.ExplicitParent ||
                mode == HairGuideInterpolationMode.ClumpParent)
            {
                candidates[0] = new Neighbor { index = parentIndex, weight = 1f }; return 1;
            }

            Span<HairNearestPoint> nearest = stackalloc HairNearestPoint[MaximumNeighborCount];
            int count = generation.FindNearest(guides, childRoot,
                mode == HairGuideInterpolationMode.Nearest ? nearest.Slice(0, 1) : nearest);
            if (count == 0)
            { candidates[0] = new Neighbor { index = parentIndex, weight = 1f }; return 1; }
            for (int i = 0; i < count; i++) candidates[i] = new Neighbor { index = nearest[i].index, distanceSquared = nearest[i].distanceSquared };
            if (mode == HairGuideInterpolationMode.Nearest)
            { candidates[0] = new Neighbor { index = candidates[0].index, weight = 1f }; return 1; }

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                Neighbor neighbor = candidates[i];
                neighbor.weight = 1f / Mathf.Max(0.0001f, Mathf.Sqrt(neighbor.distanceSquared));
                candidates[i] = neighbor;
                total += neighbor.weight;
            }
            if (total <= 1e-8f)
            {
                candidates[0] = new Neighbor { index = parentIndex, weight = 1f }; return 1;
            }
            for (int i = 0; i < count; i++)
            {
                Neighbor neighbor = candidates[i];
                neighbor.weight /= total;
                candidates[i] = neighbor;
            }
            return count;
        }

        private static bool KeepForLod(int seed, float fraction)
        {
            if (fraction >= 0.99999f) return true;
            if (fraction <= 0f) return false;
            HairDeterministicRandom random = new HairDeterministicRandom(seed);
            return random.Next01() <= fraction;
        }

        private static int CombineSeed(int first, int second, int third)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + first;
                hash = hash * 31 + second;
                hash = hash * 31 + third;
                return hash;
            }
        }

        private struct Neighbor
        {
            public int index;
            public float distanceSquared;
            public float weight;
        }
    }
}
