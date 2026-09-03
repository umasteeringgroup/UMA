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
            SourceMeshReadCache sourceMesh = new SourceMeshReadCache(
                options.evaluateSurfaceAnchors ? groom.SourceMesh : null);
            for (int groupIndex = 0; groupIndex < groom.Groups.Count; groupIndex++)
            {
                HairGroup group = groom.Groups[groupIndex];
                if (group == null || !group.enabled || (!options.includeHiddenGroups && !group.visible)) continue;
                List<HairEvaluatedCurve> guides = BuildGuides(groom, group, options, result, sourceMesh);
                result.evaluatedGuides.AddRange(guides);
                HairChildGenerator.Generate(groom, group, guides, lod, options, result);
            }
            return result;
        }

        private static List<HairEvaluatedCurve> BuildGuides(
            HairGroomAsset groom,
            HairGroup group,
            HairEvaluationOptions options,
            HairEvaluationResult result,
            SourceMeshReadCache sourceMesh)
        {
            List<HairEvaluatedCurve> curves = new List<HairEvaluatedCurve>(group.guides?.Count ?? 0);
            if (group.guides == null) return curves;
            string[] atlasRegionIds = group.atlasRegionIds?.ToArray() ?? Array.Empty<string>();
            List<SculptLayerLookup> sculptLayers = options.applySculptLayers
                ? BuildSculptLayerLookups(group)
                : null;
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
                    atlas = group.atlas,
                    atlasRegionSelection = group.atlasRegionSelection,
                    atlasRegionIds = atlasRegionIds
                };
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    curve.points.Add(new HairCurvePoint(point.position, point.width, point.roll));
                }

                if (sourceMesh.IsValid &&
                    string.Equals(guide.root.SourceMeshId, groom.SourceMeshId, StringComparison.Ordinal) &&
                    sourceMesh.TryEvaluateAnchor(guide.root,
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

                if (sculptLayers != null) ApplySculptLayers(sculptLayers, guide, curve);
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

        private static List<SculptLayerLookup> BuildSculptLayerLookups(HairGroup group)
        {
            if (group.sculptLayers == null || group.sculptLayers.Count == 0) return null;
            List<SculptLayerLookup> lookups = new List<SculptLayerLookup>(group.sculptLayers.Count);
            for (int layerIndex = 0; layerIndex < group.sculptLayers.Count; layerIndex++)
            {
                HairSculptLayer layer = group.sculptLayers[layerIndex];
                if (layer == null || !layer.visible || layer.opacity <= 0f || layer.deltas == null) continue;
                Dictionary<string, HairGuideDelta> deltas = new Dictionary<string, HairGuideDelta>(
                    layer.deltas.Count, StringComparer.Ordinal);
                for (int deltaIndex = 0; deltaIndex < layer.deltas.Count; deltaIndex++)
                {
                    HairGuideDelta delta = layer.deltas[deltaIndex];
                    if (delta == null || string.IsNullOrEmpty(delta.guideId) || deltas.ContainsKey(delta.guideId))
                        continue;
                    deltas.Add(delta.guideId, delta);
                }
                lookups.Add(new SculptLayerLookup(layer, deltas));
            }
            return lookups.Count > 0 ? lookups : null;
        }

        private static void ApplySculptLayers(
            IReadOnlyList<SculptLayerLookup> layers,
            HairGuide guide,
            HairEvaluatedCurve curve)
        {
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                SculptLayerLookup lookup = layers[layerIndex];
                HairSculptLayer layer = lookup.Layer;
                if (!lookup.Deltas.TryGetValue(guide.Id, out HairGuideDelta delta)) continue;
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

        private readonly struct SculptLayerLookup
        {
            internal readonly HairSculptLayer Layer;
            internal readonly Dictionary<string, HairGuideDelta> Deltas;

            internal SculptLayerLookup(HairSculptLayer layer, Dictionary<string, HairGuideDelta> deltas)
            {
                Layer = layer;
                Deltas = deltas;
            }
        }

        /// <summary>
        /// A single evaluator pass can touch hundreds or thousands of roots. Reading Mesh.vertices,
        /// Mesh.normals, and submesh triangles for every root creates a native-to-managed copy each
        /// time, which dominates interactive grooming. Keep one immutable snapshot for the pass.
        /// </summary>
        private sealed class SourceMeshReadCache
        {
            private readonly Vector3[] vertices = Array.Empty<Vector3>();
            private readonly Vector3[] normals = Array.Empty<Vector3>();
            private readonly int[][] triangles = Array.Empty<int[]>();

            internal bool IsValid { get; }

            internal SourceMeshReadCache(Mesh mesh)
            {
                if (mesh == null) return;
                try
                {
                    vertices = mesh.vertices;
                    normals = mesh.normals;
                    triangles = new int[mesh.subMeshCount][];
                    for (int submesh = 0; submesh < triangles.Length; submesh++)
                        triangles[submesh] = mesh.GetTriangles(submesh, true);
                    IsValid = true;
                }
                catch (Exception)
                {
                    IsValid = false;
                }
            }

            internal bool TryEvaluateAnchor(HairSurfaceAnchor anchor, out Vector3 position,
                out Vector3 normal)
            {
                position = anchor.CachedLocalPosition;
                normal = anchor.CachedLocalNormal.sqrMagnitude > 1e-8f
                    ? anchor.CachedLocalNormal.normalized
                    : Vector3.up;
                if (!IsValid || !anchor.IsValid || anchor.SubmeshIndex < 0 ||
                    anchor.SubmeshIndex >= triangles.Length) return false;

                int[] submeshTriangles = triangles[anchor.SubmeshIndex];
                int triangleOffset = anchor.TriangleIndex * 3;
                if (submeshTriangles == null || triangleOffset < 0 ||
                    triangleOffset + 2 >= submeshTriangles.Length) return false;
                int i0 = submeshTriangles[triangleOffset];
                int i1 = submeshTriangles[triangleOffset + 1];
                int i2 = submeshTriangles[triangleOffset + 2];
                if ((uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length ||
                    (uint)i2 >= (uint)vertices.Length) return false;

                Vector3 barycentric = anchor.Barycentric;
                position = vertices[i0] * barycentric.x + vertices[i1] * barycentric.y +
                           vertices[i2] * barycentric.z;
                if (normals.Length == vertices.Length)
                {
                    normal = normals[i0] * barycentric.x + normals[i1] * barycentric.y +
                             normals[i2] * barycentric.z;
                }
                else
                {
                    normal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                }
                normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
                position += normal * anchor.NormalOffset;
                return true;
            }
        }

        internal static void ApplyModifiers(
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
                    case HairModifierType.Simplify:
                    {
                        int samples = Mathf.Clamp(Mathf.RoundToInt(modifier.amount), 2, curve.points.Count);
                        List<HairCurvePoint> simplified = HairCurveUtility.Resample(curve.points, samples);
                        curve.points.Clear();
                        curve.points.AddRange(simplified);
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
                    case HairModifierType.FlowAlign:
                        ApplyPositionVector(curve, modifier,
                            modifier.type == HairModifierType.Gravity ? Physics.gravity.normalized : modifier.vector.normalized);
                        break;
                    case HairModifierType.Clump:
                        ApplyClump(curve, modifier);
                        break;
                    case HairModifierType.Part:
                        ApplyPart(curve, modifier);
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
                    case HairModifierType.TrimByMesh:
                        ApplyHelperCollision(curve, modifier, groom);
                        break;
                    case HairModifierType.SurfaceProjection:
                        ApplySurfaceProjection(curve, modifier, groom.SourceMesh);
                        break;
                    case HairModifierType.Mirror:
                        ApplyMirror(curve, modifier, groom);
                        break;
                }
            }
        }

        private static void ApplyClump(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            if (curve.points.Count < 2) return;
            Vector3 root = curve.points[0].position;
            Vector3 tipAxis = (curve.points[curve.points.Count - 1].position - root).normalized;
            float length = curve.Length;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                Vector3 center = root + tipAxis * (length * t);
                point.position = Vector3.Lerp(point.position, center, weight * Mathf.Clamp01(modifier.amount));
                return point;
            });
        }

        private static void ApplyPart(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            Vector3 normal = modifier.vector.sqrMagnitude > 1e-8f ? modifier.vector.normalized : Vector3.right;
            float side = Mathf.Sign(Vector3.Dot(curve.points[0].position, normal));
            if (Mathf.Approximately(side, 0f)) side = 1f;
            ApplyPositionVector(curve, modifier, normal * side);
        }

        private static void ApplySurfaceProjection(HairEvaluatedCurve curve, HairModifierSettings modifier, Mesh mesh)
        {
            if (mesh == null) return;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (vertices.Length == 0) return;
            for (int pointIndex = 1; pointIndex < curve.points.Count; pointIndex++)
            {
                HairCurvePoint point = curve.points[pointIndex];
                int closest = 0;
                float closestSquare = float.MaxValue;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    float square = (vertices[vertex] - point.position).sqrMagnitude;
                    if (square >= closestSquare) continue;
                    closestSquare = square;
                    closest = vertex;
                }
                Vector3 normal = normals != null && normals.Length == vertices.Length ? normals[closest] : curve.rootNormal;
                Vector3 target = vertices[closest] + normal.normalized * modifier.amount;
                float t = pointIndex / (curve.points.Count - 1f);
                float ramp = modifier.rootToTip != null ? modifier.rootToTip.Evaluate(t) : 1f;
                point.position = Vector3.Lerp(point.position, target, Mathf.Clamp01(ramp * modifier.weight));
                curve.points[pointIndex] = point;
            }
        }

        private static void ApplyMirror(HairEvaluatedCurve curve, HairModifierSettings modifier, HairGroomAsset groom)
        {
            Vector3 normal = groom.SymmetryPlaneNormal;
            Vector3 planePoint = groom.SymmetryPlanePoint;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                float distance = Vector3.Dot(point.position - planePoint, normal);
                Vector3 mirrored = point.position - normal * (2f * distance);
                point.position = Vector3.Lerp(point.position, mirrored, weight);
                point.roll = Mathf.Lerp(point.roll, -point.roll, weight);
                return point;
            });
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
            List<HairCurvePoint> helperCurve = new List<HairCurvePoint>();
            if (helper.points != null)
                for (int i = 0; i < helper.points.Count; i++)
                    helperCurve.Add(new HairCurvePoint(helper.points[i], 0f, 0f));
            if (helperCurve.Count == 0)
            {
                helperCurve.Add(new HairCurvePoint(helper.position, 0f, 0f));
                helperCurve.Add(new HairCurvePoint(helper.position, 0f, 0f));
            }
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
            float blend = Mathf.Clamp01(weight);
            if (blend <= 0f) return point;
            Matrix4x4 localToWorld = Matrix4x4.TRS(helper.position, helper.rotation, helper.scale);
            Matrix4x4 worldToLocal = localToWorld.inverse;
            Vector3 local = worldToLocal.MultiplyPoint3x4(point);
            Vector3 projected;
            switch (helper.type)
            {
                case HairHelperType.Sphere:
                case HairHelperType.Repulsor:
                case HairHelperType.VolumeTarget:
                {
                    float distance = local.magnitude;
                    if (distance >= helper.radius || helper.radius <= 0f) return point;
                    Vector3 direction = distance > 1e-7f ? local / distance : Vector3.up;
                    projected = direction * helper.radius;
                    break;
                }
                case HairHelperType.Box:
                case HairHelperType.SculptCage:
                {
                    Vector3 half = helper.size * 0.5f;
                    if (Mathf.Abs(local.x) >= half.x || Mathf.Abs(local.y) >= half.y ||
                        Mathf.Abs(local.z) >= half.z) return point;
                    Vector3 faceDistance = new Vector3(half.x - Mathf.Abs(local.x),
                        half.y - Mathf.Abs(local.y), half.z - Mathf.Abs(local.z));
                    projected = local;
                    if (faceDistance.x <= faceDistance.y && faceDistance.x <= faceDistance.z)
                        projected.x = Mathf.Sign(Mathf.Approximately(local.x, 0f) ? 1f : local.x) * half.x;
                    else if (faceDistance.y <= faceDistance.z)
                        projected.y = Mathf.Sign(Mathf.Approximately(local.y, 0f) ? 1f : local.y) * half.y;
                    else projected.z = Mathf.Sign(Mathf.Approximately(local.z, 0f) ? 1f : local.z) * half.z;
                    break;
                }
                case HairHelperType.Capsule:
                {
                    float halfSegment = Mathf.Max(0f, helper.size.y * 0.5f - helper.radius);
                    Vector3 axisPoint = new Vector3(0f, Mathf.Clamp(local.y, -halfSegment, halfSegment), 0f);
                    Vector3 delta = local - axisPoint;
                    float distance = delta.magnitude;
                    if (distance >= helper.radius || helper.radius <= 0f) return point;
                    projected = axisPoint + (distance > 1e-7f ? delta / distance : Vector3.right) * helper.radius;
                    break;
                }
                case HairHelperType.Plane:
                    if (local.y >= 0f) return point;
                    projected = new Vector3(local.x, 0f, local.z);
                    break;
                default:
                    return point;
            }
            return Vector3.Lerp(point, localToWorld.MultiplyPoint3x4(projected), blend);
        }

        private static HairLodSettings ResolveLod(HairGroomAsset groom, int level)
        {
            if (groom.Lods == null || groom.Lods.Count == 0) return null;
            HairLodSettings exact = groom.Lods.Find(candidate => candidate != null && candidate.level == level);
            return exact ?? groom.Lods[0];
        }
    }
}
