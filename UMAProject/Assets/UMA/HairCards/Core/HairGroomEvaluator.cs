using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public static class HairGroomEvaluator
    {
        public static HairEvaluationResult Evaluate(HairGroomAsset groom, HairEvaluationOptions options = null)
        {
            HairEvaluationResult result = new HairEvaluationResult();
            if (groom == null)
            {
                result.warnings.Add("No HairGroomAsset was supplied.");
                return result;
            }

            groom.EnsureIntegrity();
            options ??= new HairEvaluationOptions();
            HairLodSettings lod = ResolveLod(groom, options.lodLevel);
            for (int groupIndex = 0; groupIndex < groom.Groups.Count; groupIndex++)
            {
                HairGroup group = groom.Groups[groupIndex];
                if (group == null || !group.enabled || !group.visible) continue;
                List<HairEvaluatedCurve> guides = BuildGuides(groom, group, options, result);
                HairChildGenerator.Generate(group, guides, lod, options, result);
            }
            return result;
        }

        private static List<HairEvaluatedCurve> BuildGuides(
            HairGroomAsset groom,
            HairGroup group,
            HairEvaluationOptions options,
            HairEvaluationResult result)
        {
            List<HairEvaluatedCurve> curves = new List<HairEvaluatedCurve>(group.guides?.Count ?? 0);
            if (group.guides == null) return curves;
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide == null || !guide.enabled || guide.points == null || guide.points.Count < 2) continue;
                HairEvaluatedCurve curve = new HairEvaluatedCurve
                {
                    curveId = guide.Id,
                    parentGuideId = guide.Id,
                    groupId = group.Id,
                    seed = guide.seed,
                    isChild = false,
                    groupColor = group.color,
                    rootNormal = guide.root.CachedLocalNormal,
                    profile = group.profile,
                    atlas = group.atlas
                };
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    curve.points.Add(new HairCurvePoint(point.position, point.width, point.roll));
                }

                if (groom.SourceMesh != null && HairMeshUtility.TryEvaluateAnchor(groom.SourceMesh, guide.root,
                        out Vector3 rootPosition, out Vector3 rootNormal))
                {
                    Vector3 offset = rootPosition - curve.points[0].position;
                    for (int i = 0; i < curve.points.Count; i++)
                    {
                        HairCurvePoint point = curve.points[i];
                        point.position += offset;
                        curve.points[i] = point;
                    }
                    curve.rootNormal = rootNormal;
                }

                if (options.applySculptLayers) ApplySculptLayers(group, guide, curve);
                if (options.applyModifiers) ApplyModifiers(group, curve, HairModifierDomain.Guides, groom);
                if (options.applyConstraints) ApplyConstraints(group, curve, groom);
                if (curve.Length < 1e-6f)
                {
                    result.rejectedCurveCount++;
                    result.warnings.Add($"Guide '{guide.name}' has zero usable length.");
                    continue;
                }
                curves.Add(curve);
            }
            return curves;
        }

        private static void ApplySculptLayers(HairGroup group, HairGuide guide, HairEvaluatedCurve curve)
        {
            if (group.sculptLayers == null) return;
            for (int layerIndex = 0; layerIndex < group.sculptLayers.Count; layerIndex++)
            {
                HairSculptLayer layer = group.sculptLayers[layerIndex];
                if (layer == null || !layer.visible || layer.opacity <= 0f || layer.deltas == null) continue;
                HairGuideDelta delta = layer.deltas.Find(candidate => candidate != null && candidate.guideId == guide.Id);
                if (delta == null) continue;
                for (int pointIndex = 0; pointIndex < curve.points.Count; pointIndex++)
                {
                    HairCurvePoint point = curve.points[pointIndex];
                    Vector3 positionOffset = delta.positionOffsets != null && pointIndex < delta.positionOffsets.Length
                        ? delta.positionOffsets[pointIndex]
                        : Vector3.zero;
                    float widthOffset = delta.widthOffsets != null && pointIndex < delta.widthOffsets.Length
                        ? delta.widthOffsets[pointIndex]
                        : 0f;
                    float rollOffset = delta.rollOffsets != null && pointIndex < delta.rollOffsets.Length
                        ? delta.rollOffsets[pointIndex]
                        : 0f;
                    if (layer.blendMode == HairSculptBlendMode.Override)
                    {
                        point.position = Vector3.Lerp(point.position, guide.points[pointIndex].position + positionOffset,
                            layer.opacity);
                        point.width = Mathf.Lerp(point.width,
                            Mathf.Max(0f, guide.points[pointIndex].width + widthOffset), layer.opacity);
                        point.roll = Mathf.LerpAngle(point.roll, guide.points[pointIndex].roll + rollOffset,
                            layer.opacity);
                    }
                    else
                    {
                        point.position += positionOffset * layer.opacity;
                        point.width = Mathf.Max(0f, point.width + widthOffset * layer.opacity);
                        point.roll += rollOffset * layer.opacity;
                    }
                    curve.points[pointIndex] = point;
                }
            }
        }

        private static void ApplyModifiers(
            HairGroup group,
            HairEvaluatedCurve curve,
            HairModifierDomain domain,
            HairGroomAsset groom)
        {
            if (group.modifiers == null) return;
            for (int modifierIndex = 0; modifierIndex < group.modifiers.Count; modifierIndex++)
            {
                HairModifierSettings modifier = group.modifiers[modifierIndex];
                if (modifier == null || !modifier.enabled || modifier.weight <= 0f ||
                    (modifier.domain != domain && modifier.domain != HairModifierDomain.GuidesAndChildren))
                {
                    continue;
                }

                switch (modifier.type)
                {
                    case HairModifierType.Resample:
                    {
                        int samples = Mathf.Clamp(Mathf.RoundToInt(modifier.amount), 2, 64);
                        List<HairCurvePoint> resampled = HairCurveUtility.Resample(curve.points, samples);
                        curve.points.Clear();
                        curve.points.AddRange(resampled);
                        break;
                    }
                    case HairModifierType.Length:
                        HairCurveUtility.ScaleLength(curve.points,
                            Mathf.Lerp(1f, Mathf.Max(0f, modifier.amount), modifier.weight));
                        break;
                    case HairModifierType.Width:
                        ApplyPerPoint(curve, modifier, (point, t, weight) =>
                        {
                            point.width = Mathf.Max(0f, point.width * Mathf.Lerp(1f, modifier.amount, weight));
                            return point;
                        });
                        break;
                    case HairModifierType.Smooth:
                        HairCurveUtility.Smooth(curve.points, Mathf.Clamp01(modifier.amount) * modifier.weight, 1, true);
                        break;
                    case HairModifierType.Lift:
                    case HairModifierType.Gravity:
                        ApplyPositionVector(curve, modifier,
                            modifier.type == HairModifierType.Gravity ? Physics.gravity.normalized : modifier.vector.normalized);
                        break;
                    case HairModifierType.Curl:
                    case HairModifierType.Wave:
                        ApplyWave(curve, modifier, modifier.type == HairModifierType.Curl);
                        break;
                    case HairModifierType.Noise:
                        ApplyNoise(curve, modifier);
                        break;
                    case HairModifierType.Twist:
                        ApplyPerPoint(curve, modifier, (point, t, weight) =>
                        {
                            point.roll += modifier.amount * weight;
                            return point;
                        });
                        break;
                    case HairModifierType.HelperFollow:
                        ApplyHelperFollow(curve, modifier, groom);
                        break;
                    case HairModifierType.PushOut:
                    case HairModifierType.Collision:
                        ApplyHelperCollision(curve, modifier, groom);
                        break;
                }
            }
        }

        private static void ApplyConstraints(HairGroup group, HairEvaluatedCurve curve, HairGroomAsset groom)
        {
            if (group.constraints == null) return;
            for (int constraintIndex = 0; constraintIndex < group.constraints.Count; constraintIndex++)
            {
                HairConstraintSettings constraint = group.constraints[constraintIndex];
                if (constraint == null || !constraint.enabled || constraint.weight <= 0f) continue;
                HairHelper helper = groom.FindHelper(constraint.helperId);
                if (helper == null) continue;
                switch (constraint.type)
                {
                    case HairConstraintType.FollowCurve:
                    case HairConstraintType.TrackTip:
                    case HairConstraintType.Attract:
                        ApplyConstraintAttraction(curve, constraint, helper);
                        break;
                    case HairConstraintType.Repel:
                    case HairConstraintType.Collision:
                        ApplyConstraintCollision(curve, constraint, helper);
                        break;
                }
            }
        }

        private static void ApplyPerPoint(
            HairEvaluatedCurve curve,
            HairModifierSettings modifier,
            Func<HairCurvePoint, float, float, HairCurvePoint> operation)
        {
            for (int i = 0; i < curve.points.Count; i++)
            {
                float t = curve.points.Count > 1 ? i / (curve.points.Count - 1f) : 0f;
                float ramp = modifier.rootToTip != null ? modifier.rootToTip.Evaluate(t) : 1f;
                curve.points[i] = operation(curve.points[i], t, Mathf.Clamp01(ramp * modifier.weight));
            }
        }

        private static void ApplyPositionVector(HairEvaluatedCurve curve, HairModifierSettings modifier, Vector3 direction)
        {
            if (direction.sqrMagnitude < 1e-8f) return;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                point.position += direction * modifier.amount * weight;
                return point;
            });
        }

        private static void ApplyWave(HairEvaluatedCurve curve, HairModifierSettings modifier, bool circular)
        {
            if (curve.points.Count < 2) return;
            Vector3 tangent = HairCurveUtility.CalculateTangent(curve.points, 0);
            Vector3 axisA = Vector3.Cross(tangent, curve.rootNormal).normalized;
            if (axisA.sqrMagnitude < 1e-8f) axisA = Vector3.right;
            Vector3 axisB = Vector3.Cross(tangent, axisA).normalized;
            float cycles = Mathf.Max(0.01f, Mathf.Abs(modifier.vector.x));
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                float phase = t * Mathf.PI * 2f * cycles + modifier.vector.y;
                Vector3 offset = axisA * Mathf.Sin(phase);
                if (circular) offset += axisB * Mathf.Cos(phase);
                point.position += offset * modifier.amount * weight;
                return point;
            });
        }

        private static void ApplyNoise(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            HairDeterministicRandom random = new HairDeterministicRandom(modifier.seed ^ curve.seed);
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                Vector3 noise = new Vector3(random.NextSigned(), random.NextSigned(), random.NextSigned());
                point.position += noise * modifier.amount * weight;
                return point;
            });
        }

        private static void ApplyHelperFollow(HairEvaluatedCurve curve, HairModifierSettings modifier, HairGroomAsset groom)
        {
            HairHelper helper = groom.FindHelper(modifier.helperId);
            if (helper == null || helper.points == null || helper.points.Count < 2) return;
            List<HairCurvePoint> helperCurve = new List<HairCurvePoint>(helper.points.Count);
            for (int i = 0; i < helper.points.Count; i++) helperCurve.Add(new HairCurvePoint(helper.points[i], 0f, 0f));
            List<HairCurvePoint> samples = HairCurveUtility.Resample(helperCurve, curve.points.Count);
            Vector3 sourceRoot = samples[0].position;
            Vector3 targetRoot = curve.points[0].position;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(t * (samples.Count - 1)), 0, samples.Count - 1);
                Vector3 target = targetRoot + samples[index].position - sourceRoot;
                point.position = Vector3.Lerp(point.position, target, weight);
                return point;
            });
        }

        private static void ApplyHelperCollision(HairEvaluatedCurve curve, HairModifierSettings modifier, HairGroomAsset groom)
        {
            HairHelper helper = groom.FindHelper(modifier.helperId);
            if (helper == null) return;
            for (int i = 1; i < curve.points.Count; i++)
            {
                HairCurvePoint point = curve.points[i];
                point.position = PushOutsideHelper(point.position, helper, modifier.amount * modifier.weight);
                curve.points[i] = point;
            }
        }

        private static void ApplyConstraintAttraction(
            HairEvaluatedCurve curve,
            HairConstraintSettings constraint,
            HairHelper helper)
        {
            if (helper.points == null || helper.points.Count == 0) return;
            List<HairCurvePoint> helperCurve = new List<HairCurvePoint>(helper.points.Count);
            for (int i = 0; i < helper.points.Count; i++) helperCurve.Add(new HairCurvePoint(helper.points[i], 0f, 0f));
            List<HairCurvePoint> samples = HairCurveUtility.Resample(helperCurve, curve.points.Count);
            for (int i = 0; i < curve.points.Count; i++)
            {
                float t = curve.points.Count > 1 ? i / (curve.points.Count - 1f) : 0f;
                float ramp = constraint.rootToTip != null ? constraint.rootToTip.Evaluate(t) : t;
                float weight = Mathf.Clamp01(ramp * constraint.weight);
                HairCurvePoint point = curve.points[i];
                point.position = Vector3.Lerp(point.position, samples[i].position + constraint.offset, weight);
                point.roll += constraint.twist * weight;
                curve.points[i] = point;
            }
        }

        private static void ApplyConstraintCollision(
            HairEvaluatedCurve curve,
            HairConstraintSettings constraint,
            HairHelper helper)
        {
            for (int i = 1; i < curve.points.Count; i++)
            {
                float t = i / (curve.points.Count - 1f);
                float ramp = constraint.rootToTip != null ? constraint.rootToTip.Evaluate(t) : 1f;
                HairCurvePoint point = curve.points[i];
                point.position = PushOutsideHelper(point.position, helper,
                    Mathf.Clamp01(ramp * constraint.weight));
                curve.points[i] = point;
            }
        }

        private static Vector3 PushOutsideHelper(Vector3 point, HairHelper helper, float weight)
        {
            if (helper.type != HairHelperType.Sphere && helper.type != HairHelperType.Repulsor &&
                helper.type != HairHelperType.VolumeTarget)
            {
                return point;
            }
            Vector3 delta = point - helper.position;
            float distance = delta.magnitude;
            if (distance >= helper.radius || helper.radius <= 0f) return point;
            Vector3 direction = distance > 1e-7f ? delta / distance : Vector3.up;
            return Vector3.Lerp(point, helper.position + direction * helper.radius, Mathf.Clamp01(weight));
        }

        private static HairLodSettings ResolveLod(HairGroomAsset groom, int level)
        {
            if (groom.Lods == null || groom.Lods.Count == 0) return null;
            HairLodSettings exact = groom.Lods.Find(candidate => candidate != null && candidate.level == level);
            return exact ?? groom.Lods[0];
        }
    }
}
