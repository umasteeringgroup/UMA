using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public static class HairChildGenerator
    {
        private const int MaximumNeighborCount = 4;

        public static void Generate(
            HairGroomAsset groom,
            HairGroup group,
            IReadOnlyList<HairEvaluatedCurve> guides,
            HairLodSettings lod,
            HairEvaluationOptions options,
            HairEvaluationResult result)
        {
            if (group == null || guides == null || result == null) return;
            if (!options.includeGuideCards && !options.includeChildren) return;
            float cardFraction = lod != null ? lod.cardFraction : 1f;
            int sampleCount = lod != null ? lod.samplesPerCard : 12;
            int tubeSides = lod != null ? lod.maximumTubeSides : 12;
            Dictionary<string, HairGuide> sourceGuides = BuildGuideLookup(group);
            Dictionary<int, int[]> sourceTriangles = new Dictionary<int, int[]>();
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
                                          sourceTriangles);
                float guideLodFraction = Mathf.Clamp01(cardFraction * Mathf.Lerp(0.25f, 1.5f,
                    Mathf.Clamp01(lodImportance)));

                bool includeGuide = options.includeGuideCards && sourceGuide.includeGuideCard &&
                                    group.children.includeGuideCard && KeepForLod(sourceGuide.seed, guideLodFraction);
                if (includeGuide)
                {
                    HairEvaluatedCurve card = guideCurve.Clone($"{sourceGuide.Id}:guide");
                    card.isChild = false;
                    card.samplesPerCardOverride = sampleCount;
                    card.tubeSidesOverride = tubeSides;
                    result.curves.Add(card);
                    result.guideCurveCount++;
                }

                if (!options.includeChildren) continue;
                int childCount = sourceGuide.overrideChildCount ? sourceGuide.childCount : group.children.childrenPerGuide;
                float childMultiplier = SampleRootMap(groom, group, sourceGuide, HairMapKind.ChildCount, 1f,
                    sourceTriangles);
                childCount = Mathf.Max(0, Mathf.RoundToInt(childCount * Mathf.Max(0f, childMultiplier)));
                float paintedClump = Mathf.Clamp01(SampleRootMap(groom, group, sourceGuide,
                    HairMapKind.Clump, group.children.clump, sourceTriangles));
                for (int childIndex = 0; childIndex < childCount; childIndex++)
                {
                    if (options.interactiveSampleLimit > 0 && result.curves.Count >= options.interactiveSampleLimit)
                    {
                        result.warnings.Add("Interactive preview card limit reached; the release bake remains complete.");
                        return;
                    }
                    int childSeed = CombineSeed(group.children.seed, sourceGuide.seed, childIndex);
                    if (!KeepForLod(childSeed, guideLodFraction)) continue;
                    HairEvaluatedCurve child = CreateChild(group, guides, guideIndex, sourceGuide,
                        childIndex, childSeed, sampleCount, paintedClump);
                    child.samplesPerCardOverride = sampleCount;
                    child.tubeSidesOverride = tubeSides;
                    if (options.applyModifiers)
                        HairGroomEvaluator.ApplyModifiers(group, child, HairModifierDomain.Children, groom);
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
            int parentIndex,
            HairGuide sourceGuide,
            int childIndex,
            int seed,
            int sampleCount,
            float clump)
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

            List<Neighbor> neighbors = FindNeighbors(guides, parentIndex, group.children.interpolation);
            List<List<HairCurvePoint>> samples = new List<List<HairCurvePoint>>(neighbors.Count);
            for (int i = 0; i < neighbors.Count; i++)
            {
                samples.Add(HairCurveUtility.Resample(guides[neighbors[i].index].points, sampleCount));
            }

            float lengthScale = 1f + random.NextSigned() * group.children.lengthVariation;
            float widthScale = 1f + random.NextSigned() * group.children.widthVariation;
            float rollOffset = random.NextSigned() * group.children.rollVariation * 180f;
            HairEvaluatedCurve child = new HairEvaluatedCurve
            {
                curveId = $"{sourceGuide.Id}:child:{childIndex}",
                parentGuideId = sourceGuide.Id,
                groupId = group.Id,
                isChild = true,
                seed = seed,
                groupColor = group.color,
                rootNormal = parent.rootNormal,
                profile = group.profile,
                atlas = group.atlas,
                atlasRegionSelection = parent.atlasRegionSelection,
                atlasRegionIds = parent.atlasRegionIds
            };

            Vector3 weightedRoot = Vector3.zero;
            for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
            {
                weightedRoot += samples[neighborIndex][0].position * neighbors[neighborIndex].weight;
            }
            Vector3 targetRoot = parent.points[0].position + rootOffset;
            for (int pointIndex = 0; pointIndex < sampleCount; pointIndex++)
            {
                Vector3 weightedPosition = Vector3.zero;
                float weightedWidth = 0f;
                float weightedRoll = 0f;
                for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    HairCurvePoint sample = samples[neighborIndex][pointIndex];
                    float weight = neighbors[neighborIndex].weight;
                    weightedPosition += sample.position * weight;
                    weightedWidth += sample.width * weight;
                    weightedRoll += sample.roll * weight;
                }

                float t = pointIndex / (sampleCount - 1f);
                Vector3 relative = (weightedPosition - weightedRoot) * lengthScale;
                Vector3 lateral = rootOffset * (1f - clump * t);
                child.points.Add(new HairCurvePoint(
                    targetRoot + relative + lateral - rootOffset,
                    Mathf.Max(0f, weightedWidth * widthScale),
                    weightedRoll + rollOffset * t));
            }
            return child;
        }

        private static Dictionary<string, HairGuide> BuildGuideLookup(HairGroup group)
        {
            Dictionary<string, HairGuide> guides = new Dictionary<string, HairGuide>(
                group.guides?.Count ?? 0, StringComparer.Ordinal);
            if (group.guides == null) return guides;
            for (int index = 0; index < group.guides.Count; index++)
            {
                HairGuide guide = group.guides[index];
                if (guide == null || string.IsNullOrEmpty(guide.Id) || guides.ContainsKey(guide.Id)) continue;
                guides.Add(guide.Id, guide);
            }
            return guides;
        }

        private static float SampleRootMap(HairGroomAsset groom, HairGroup group, HairGuide guide,
            HairMapKind kind, float fallback, Dictionary<int, int[]> triangleCache)
        {
            HairGrowthMap map = group.FindMap(kind);
            HairSurfaceAnchor root = guide.root;
            if (map == null || groom?.SourceMesh == null || !root.IsValid ||
                root.SubmeshIndex < 0 || root.SubmeshIndex >= groom.SourceMesh.subMeshCount) return fallback;
            if (!triangleCache.TryGetValue(root.SubmeshIndex, out int[] triangles))
            {
                triangles = groom.SourceMesh.GetTriangles(root.SubmeshIndex, true);
                triangleCache.Add(root.SubmeshIndex, triangles);
            }
            int offset = root.TriangleIndex * 3;
            if (offset < 0 || offset + 2 >= triangles.Length) return fallback;
            Vector3 barycentric = root.Barycentric;
            return map.SampleVertex(triangles[offset]) * barycentric.x +
                   map.SampleVertex(triangles[offset + 1]) * barycentric.y +
                   map.SampleVertex(triangles[offset + 2]) * barycentric.z;
        }

        private static List<Neighbor> FindNeighbors(IReadOnlyList<HairEvaluatedCurve> guides, int parentIndex,
            HairGuideInterpolationMode mode)
        {
            if (mode == HairGuideInterpolationMode.Nearest ||
                mode == HairGuideInterpolationMode.ExplicitParent ||
                mode == HairGuideInterpolationMode.ClumpParent)
            {
                return new List<Neighbor> { new Neighbor { index = parentIndex, weight = 1f } };
            }
            Vector3 parentRoot = guides[parentIndex].points[0].position;
            List<Neighbor> candidates = new List<Neighbor>(guides.Count);
            for (int i = 0; i < guides.Count; i++)
            {
                if (guides[i].points.Count == 0) continue;
                float distance = Vector3.Distance(parentRoot, guides[i].points[0].position);
                candidates.Add(new Neighbor { index = i, distance = distance });
            }
            candidates.Sort((left, right) =>
            {
                int distanceOrder = left.distance.CompareTo(right.distance);
                return distanceOrder != 0 ? distanceOrder : left.index.CompareTo(right.index);
            });
            if (candidates.Count > MaximumNeighborCount)
            {
                candidates.RemoveRange(MaximumNeighborCount, candidates.Count - MaximumNeighborCount);
            }

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                Neighbor neighbor = candidates[i];
                neighbor.weight = 1f / Mathf.Max(0.0001f, neighbor.distance);
                if (neighbor.index == parentIndex) neighbor.weight *= 2f;
                candidates[i] = neighbor;
                total += neighbor.weight;
            }
            if (total <= 1e-8f)
            {
                candidates.Clear();
                candidates.Add(new Neighbor { index = parentIndex, weight = 1f });
                return candidates;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                Neighbor neighbor = candidates[i];
                neighbor.weight /= total;
                candidates[i] = neighbor;
            }
            return candidates;
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
            public float distance;
            public float weight;
        }
    }
}
