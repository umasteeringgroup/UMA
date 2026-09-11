using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    public static class HairGroomEvaluator
    {
        private static readonly ProfilerMarker EvaluationMarker = new ProfilerMarker("HairCards.Evaluate");
        private static readonly ProfilerMarker SourceReadMarker = new ProfilerMarker("HairCards.ReadSourceMesh");
        private static readonly ProfilerMarker ProjectionMarker = new ProfilerMarker("HairCards.SurfaceProjection");
        public static HairEvaluationResult Evaluate(HairGroomAsset groom, HairEvaluationOptions options = null)
        {
            return Evaluate(groom, options, new HairEvaluationWorkspace());
        }

        /// <summary>Reuses scratch storage only; returned curves remain owned by the caller.</summary>
        public static HairEvaluationResult Evaluate(HairGroomAsset groom, HairEvaluationOptions options,
            HairEvaluationWorkspace workspace)
            => EvaluateInternal(groom, options, workspace, null);

        /// <summary>Opt-in mutable preview evaluation. All curves in result are overwritten;
        /// do not retain its curves as snapshots. Evaluate continues to return independent results.</summary>
        public static HairEvaluationResult EvaluateInto(HairGroomAsset groom, HairEvaluationOptions options,
            HairEvaluationWorkspace workspace, HairEvaluationResult result)
            => EvaluateInternal(groom, options, workspace, result ?? new HairEvaluationResult());

        private static HairEvaluationResult EvaluateInternal(HairGroomAsset groom, HairEvaluationOptions options,
            HairEvaluationWorkspace workspace, HairEvaluationResult reusableResult)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (workspace.inUse) throw new InvalidOperationException("An evaluation workspace cannot be shared by concurrent evaluations.");
            workspace.inUse = true;
            options ??= new HairEvaluationOptions();
            workspace.options = options;
            workspace.sourceMesh.Begin(groom != null ? groom.SourceMesh : null);
            workspace.gravitySurface.Begin(options.gravityCollisionMesh != null ? options.gravityCollisionMesh : groom?.SourceMesh);
            try
            {
                reusableResult?.BeginUpdate();
                using (EvaluationMarker.Auto()) return EvaluateCore(groom, options, workspace, reusableResult);
            }
            finally
            {
                reusableResult?.EndUpdate();
                workspace.sourceMesh.End();
                workspace.gravitySurface.End(); workspace.options = null;
                workspace.groupGuides.Clear();
                workspace.children.sourceGuides.Clear();
                workspace.inUse = false;
            }
        }

        private static HairEvaluationResult EvaluateCore(HairGroomAsset groom, HairEvaluationOptions options,
            HairEvaluationWorkspace workspace, HairEvaluationResult reusableResult)
        {
            HairEvaluationResult result = reusableResult ?? new HairEvaluationResult();
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
                if (group == null || !group.enabled || (!options.includeHiddenGroups && !group.visible)) continue;
                List<HairEvaluatedCurve> guides = BuildGuides(groom, group, options, result, workspace);
                result.evaluatedGuides.AddRange(guides);
                HairChildGenerator.Generate(groom, group, guides, lod, options, result, workspace);
            }
            return result;
        }

        private static List<HairEvaluatedCurve> BuildGuides(
            HairGroomAsset groom,
            HairGroup group,
            HairEvaluationOptions options,
            HairEvaluationResult result,
            HairEvaluationWorkspace workspace)
        {
            SourceMeshReadCache sourceMesh = workspace.sourceMesh;
            List<HairEvaluatedCurve> curves = workspace.groupGuides;
            curves.Clear();
            if (group.guides == null) return curves;
            workspace.groupClumpTip = Vector3.zero;
            int clumpCount = 0;
            foreach (HairGuide sourceGuide in group.guides)
                if (sourceGuide != null && sourceGuide.enabled && sourceGuide.points.Count > 1)
                { workspace.groupClumpTip += sourceGuide.points[^1].position; clumpCount++; }
            if (clumpCount > 0) workspace.groupClumpTip /= clumpCount;
            string[] atlasRegionIds = group.atlasRegionIds?.ToArray() ?? Array.Empty<string>();
            List<SculptLayerLookup> sculptLayers = options.applySculptLayers || options.applyModifiers
                ? BuildSculptLayerLookups(group, options.soloLayerId)
                : null;
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide == null || !guide.enabled || guide.points == null || guide.points.Count < 2) continue;
                if (options.includedGuideIds != null && !options.includedGuideIds.Contains(guide.Id)) continue;
                HairEvaluatedCurve curve = result.RentCurve(guide.points.Count);
                curve.curveId = guide.Id; curve.parentGuideId = guide.Id; curve.groupId = group.Id;
                curve.seed = guide.seed; curve.isChild = false; curve.groupColor = group.color;
                curve.rootNormal = guide.root.CachedLocalNormal; curve.rootEmbedDepth = group.rootEmbedDepth;
                curve.profile = group.profile; curve.atlas = group.atlas;
                curve.atlasRegionSelection = group.atlasRegionSelection; curve.atlasRegionIds = atlasRegionIds;
                curve.samplesPerCardOverride = curve.tubeSidesOverride = 0;
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    curve.points.Add(new HairCurvePoint(point.position, point.width, point.roll,
                        guide.GetWidthBaseline(pointIndex), point.profileScale, point.stiffness, point.freeze));
                }

                if (options.evaluateSurfaceAnchors && sourceMesh.IsValid &&
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

                if (sculptLayers != null)
                    foreach (SculptLayerLookup layer in sculptLayers)
                    {
                        if (options.applySculptLayers) ApplySculptLayer(layer, guide, curve);
                        if (options.applyModifiers) ApplyModifiers(layer.Modifiers, curve, HairModifierDomain.Guides, groom, workspace);
                    }
                if (options.applyModifiers) ApplyModifiers(group, curve, HairModifierDomain.Guides, groom, workspace);
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

        private static List<SculptLayerLookup> BuildSculptLayerLookups(HairGroup group, string soloLayerId = null)
        {
            if (group.sculptLayers == null || group.sculptLayers.Count == 0) return null;
            bool soloThisGroup = !string.IsNullOrEmpty(soloLayerId) &&
                group.sculptLayers.Exists(layer => layer != null && layer.Id == soloLayerId);
            List<SculptLayerLookup> lookups = new List<SculptLayerLookup>(group.sculptLayers.Count);
            for (int layerIndex = 0; layerIndex < group.sculptLayers.Count; layerIndex++)
            {
                HairSculptLayer layer = group.sculptLayers[layerIndex];
                if (soloThisGroup && layer?.Id != soloLayerId) continue;
                if (layer == null || (!layer.visible && !soloThisGroup) || layer.opacity <= 0f || layer.deltas == null) continue;
                Dictionary<string, HairGuideDelta> deltas = new Dictionary<string, HairGuideDelta>(
                    layer.deltas.Count, StringComparer.Ordinal);
                for (int deltaIndex = 0; deltaIndex < layer.deltas.Count; deltaIndex++)
                {
                    HairGuideDelta delta = layer.deltas[deltaIndex];
                    if (delta == null || string.IsNullOrEmpty(delta.guideId) || deltas.ContainsKey(delta.guideId))
                        continue;
                    deltas.Add(delta.guideId, delta);
                }
                lookups.Add(new SculptLayerLookup(layer, deltas, EffectiveModifiers(layer)));
            }
            return lookups.Count > 0 ? lookups : null;
        }

        private static void ApplySculptLayer(
            SculptLayerLookup lookup,
            HairGuide guide,
            HairEvaluatedCurve curve)
        {
            HairSculptLayer layer = lookup.Layer;
            if (!lookup.Deltas.TryGetValue(guide.Id, out HairGuideDelta delta)) return;
            for (int pointIndex = 0; pointIndex < curve.points.Count; pointIndex++)
            {
                HairCurvePoint point = curve.points[pointIndex];
                // Earlier layer modifiers may have resampled the curve. Map subsequent
                // authored offsets back to the original guide's normalized point indices.
                float sample = pointIndex * (guide.points.Count - 1f) / (curve.points.Count - 1f);
                int a = Mathf.FloorToInt(sample), b = Mathf.Min(a + 1, guide.points.Count - 1);
                float fraction = sample - a;
                Vector3 positionOffset = Vector3.Lerp(Offset(delta.positionOffsets, a), Offset(delta.positionOffsets, b), fraction);
                float widthOffset = Mathf.Lerp(Offset(delta.widthOffsets, a), Offset(delta.widthOffsets, b), fraction);
                float rollOffset = Mathf.Lerp(Offset(delta.rollOffsets, a), Offset(delta.rollOffsets, b), fraction);
                if (layer.blendMode == HairSculptBlendMode.Override)
                {
                    point.position = Vector3.Lerp(point.position, Vector3.Lerp(guide.points[a].position, guide.points[b].position, fraction) + positionOffset,
                        layer.opacity);
                    point.width = Mathf.Lerp(point.width,
                        Mathf.Lerp(guide.points[a].width, guide.points[b].width, fraction) + widthOffset, layer.opacity);
                    point.roll = Mathf.LerpAngle(point.roll, Mathf.LerpAngle(guide.points[a].roll, guide.points[b].roll, fraction) + rollOffset,
                        layer.opacity);
                }
                else
                {
                    point.position += positionOffset * layer.opacity;
                    // Clamp the resolved card width in the mesher, not the old guide taper:
                    // a negative offset can legitimately narrow a now-wide profile tip.
                    point.width += widthOffset * layer.opacity;
                    point.roll += rollOffset * layer.opacity;
                }
                curve.points[pointIndex] = point;
            }
        }

        private static Vector3 Offset(Vector3[] values, int index) => values != null && index < values.Length ? values[index] : Vector3.zero;
        private static float Offset(float[] values, int index) => values != null && index < values.Length ? values[index] : 0f;

        private static IReadOnlyList<HairModifierSettings> EffectiveModifiers(HairSculptLayer layer)
        {
            if (layer.opacity >= 1f || layer.modifiers == null || layer.modifiers.Count == 0) return layer.modifiers;
            var modifiers = new List<HairModifierSettings>(layer.modifiers.Count);
            foreach (HairModifierSettings modifier in layer.modifiers)
                if (modifier != null) modifiers.Add(modifier.WithLayerOpacity(layer.opacity));
            return modifiers;
        }

        internal static IReadOnlyList<HairModifierSettings> ChildModifiers(HairGroup group, string soloLayerId)
        {
            var modifiers = new List<HairModifierSettings>();
            bool solo = !string.IsNullOrEmpty(soloLayerId) && group.sculptLayers.Exists(layer => layer != null && layer.Id == soloLayerId);
            foreach (HairSculptLayer layer in group.sculptLayers)
            {
                if (layer == null || (solo ? layer.Id != soloLayerId : !layer.visible) || layer.opacity <= 0f) continue;
                var effective = EffectiveModifiers(layer);
                if (effective != null) modifiers.AddRange(effective);
            }
            // Keep legacy runtime/API data evaluable until the editor explicitly migrates it.
            if (group.modifiers != null) modifiers.AddRange(group.modifiers);
            return modifiers;
        }

        private readonly struct SculptLayerLookup
        {
            internal readonly HairSculptLayer Layer;
            internal readonly Dictionary<string, HairGuideDelta> Deltas;
            internal readonly IReadOnlyList<HairModifierSettings> Modifiers;

            internal SculptLayerLookup(HairSculptLayer layer, Dictionary<string, HairGuideDelta> deltas, IReadOnlyList<HairModifierSettings> modifiers)
            {
                Layer = layer;
                Deltas = deltas;
                Modifiers = modifiers;
            }
        }

        /// <summary>
        /// A single evaluator pass can touch hundreds or thousands of roots. Reading Mesh.vertices,
        /// Mesh.normals, and submesh triangles for every root creates a native-to-managed copy each
        /// time, which dominates interactive grooming. Keep one immutable snapshot for the pass.
        /// </summary>
        internal sealed class SourceMeshReadCache
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<List<int>> triangles = new List<List<int>>();
            private readonly HairPointSpatialIndex vertexIndex = new HairPointSpatialIndex();
            private bool[] trianglesRead = Array.Empty<bool>();
            private Mesh mesh;
            private bool channelsRead, valid, indexed;
            private bool surfaceRead;
            private HairMeshRaycaster surface;
            private readonly List<int> surfaceIndices = new List<int>();
            private int submeshCount;

            internal bool IsValid { get { ReadChannels(); return valid; } }

            // Refresh once per pass, including in-place mesh edits with unchanged vertex counts.
            // The tree itself is retained when its exact position snapshot has not changed.
            internal void Begin(Mesh source)
            {
                mesh = source;
                channelsRead = valid = indexed = false;
                surfaceRead = false;
                submeshCount = mesh != null ? mesh.subMeshCount : 0;
                if (trianglesRead.Length < submeshCount) Array.Resize(ref trianglesRead, submeshCount);
                Array.Clear(trianglesRead, 0, submeshCount);
                while (triangles.Count < submeshCount) triangles.Add(new List<int>());
            }

            internal void End() { mesh = null; }

            internal void Clear()
            {
                End();
                vertices.Clear(); vertices.Capacity = 0;
                normals.Clear(); normals.Capacity = 0;
                triangles.Clear(); triangles.Capacity = 0;
                trianglesRead = Array.Empty<bool>();
                vertexIndex.Clear();
                surface = null; surfaceIndices.Clear(); surfaceIndices.Capacity = 0;
                channelsRead = valid = indexed = false;
            }

            private void ReadChannels()
            {
                if (channelsRead) return;
                channelsRead = true;
                vertices.Clear(); normals.Clear();
                if (mesh == null || !mesh.isReadable) return;
                using var readScope = SourceReadMarker.Auto();
                try
                {
                    mesh.GetVertices(vertices);
                    mesh.GetNormals(normals);
                    valid = true;
                }
                catch (Exception) { valid = false; }
            }

            internal HairMeshRaycaster Surface()
            {
                if (!IsValid) return null;
                if (!surfaceRead)
                {
                    surfaceRead = true;
                    surfaceIndices.Clear();
                    for (int submesh = 0; submesh < submeshCount; submesh++)
                    { var indices = Triangles(submesh); if (indices != null) surfaceIndices.AddRange(indices); }
                    if (surface == null) surface = new HairMeshRaycaster(mesh);
                    else if (!surface.MatchesGeometry(vertices, normals, surfaceIndices)) surface.Rebuild(mesh);
                }
                return surface;
            }

            internal IReadOnlyList<int> Triangles(int submesh)
            {
                if (mesh == null || !mesh.isReadable || (uint)submesh >= (uint)submeshCount) return null;
                if (!trianglesRead[submesh])
                {
                    trianglesRead[submesh] = true;
                    triangles[submesh].Clear();
                    try { mesh.GetTriangles(triangles[submesh], submesh, true); }
                    catch (Exception) { triangles[submesh].Clear(); }
                }
                return triangles[submesh];
            }

            internal bool TryProject(Vector3 point, Vector3 fallbackNormal, float offset, out Vector3 target)
            {
                target = point;
                if (!IsValid || vertices.Count == 0) return false;
                if (!indexed) { vertexIndex.Rebuild(vertices); indexed = true; }
                if (!vertexIndex.TryFindNearest(point, out int closest, out _)) return false;
                Vector3 normal = normals.Count == vertices.Count ? normals[closest] : fallbackNormal;
                target = vertices[closest] + normal.normalized * offset;
                return true;
            }

            internal bool TryEvaluateAnchor(HairSurfaceAnchor anchor, out Vector3 position,
                out Vector3 normal)
            {
                position = anchor.CachedLocalPosition;
                normal = anchor.CachedLocalNormal.sqrMagnitude > 1e-8f
                    ? anchor.CachedLocalNormal.normalized
                    : Vector3.up;
                if (!IsValid || !anchor.IsValid || anchor.SubmeshIndex < 0 ||
                    anchor.SubmeshIndex >= submeshCount) return false;

                IReadOnlyList<int> submeshTriangles = Triangles(anchor.SubmeshIndex);
                int triangleOffset = anchor.TriangleIndex * 3;
                if (submeshTriangles == null || triangleOffset < 0 ||
                    triangleOffset + 2 >= submeshTriangles.Count) return false;
                int i0 = submeshTriangles[triangleOffset];
                int i1 = submeshTriangles[triangleOffset + 1];
                int i2 = submeshTriangles[triangleOffset + 2];
                if ((uint)i0 >= (uint)vertices.Count || (uint)i1 >= (uint)vertices.Count ||
                    (uint)i2 >= (uint)vertices.Count) return false;

                Vector3 barycentric = anchor.Barycentric;
                position = vertices[i0] * barycentric.x + vertices[i1] * barycentric.y +
                           vertices[i2] * barycentric.z;
                if (normals.Count == vertices.Count)
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
            HairGroomAsset groom, HairEvaluationWorkspace workspace)
            => ApplyModifiers(group.modifiers, curve, domain, groom, workspace);

        internal static void ApplyModifiers(IReadOnlyList<HairModifierSettings> modifiers,
            HairEvaluatedCurve curve, HairModifierDomain domain, HairGroomAsset groom, HairEvaluationWorkspace workspace)
        {
            if (modifiers == null) return;
            for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
            {
                HairModifierSettings modifier = modifiers[modifierIndex];
                if (modifier == null || !modifier.enabled || modifier.weight <= 0f || !float.IsFinite(modifier.weight) || !float.IsFinite(modifier.amount) ||
                    (modifier.domain != domain && modifier.domain != HairModifierDomain.GuidesAndChildren))
                {
                    continue;
                }
                // Generated children already inherit every guide-domain operation. Reapplying a
                // combined-domain modifier doubles length/width, mirrors back, and settles twice.
                if (domain == HairModifierDomain.Children && modifier.domain == HairModifierDomain.GuidesAndChildren &&
                    modifier.type != HairModifierType.LodReduction) continue;
                bool preserveShapeLength = PreservesShapeLength(modifier.type);
                if (preserveShapeLength) CaptureModifierShape(curve, workspace);

                switch (modifier.type)
                {
                    case HairModifierType.Resample:
                    {
                        int samples = Mathf.Clamp(Mathf.RoundToInt(modifier.amount), 2, 64);
                        HairCurveUtility.ResampleInto(curve.points, samples, workspace.resampled, ref workspace.cumulative);
                        curve.points.Clear();
                        curve.points.AddRange(workspace.resampled);
                        break;
                    }
                    case HairModifierType.Simplify:
                    {
                        int samples = Mathf.Clamp(Mathf.RoundToInt(modifier.amount), 2, curve.points.Count);
                        HairCurveUtility.ResampleInto(curve.points, samples, workspace.resampled, ref workspace.cumulative);
                        curve.points.Clear();
                        curve.points.AddRange(workspace.resampled);
                        break;
                    }
                    case HairModifierType.Length:
                        Vector3 lengthRoot = curve.points[0].position;
                        for (int i = 1; i < curve.points.Count; i++)
                        {
                            HairCurvePoint point = curve.points[i];
                            float scale = Mathf.Lerp(1f, Mathf.Max(0f, modifier.amount),
                                Mathf.Clamp01(modifier.weight) * (1f - Mathf.Clamp01(point.freeze)));
                            point.position = lengthRoot + (point.position - lengthRoot) * scale;
                            curve.points[i] = point;
                        }
                        break;
                    case HairModifierType.Width:
                        ApplyPerPoint(curve, modifier, (point, t, weight) =>
                        {
                            float scale = Mathf.Max(0f, Mathf.Lerp(1f, modifier.amount, weight));
                            point.width *= scale;
                            if (point.widthBaseline >= 0f) point.widthBaseline *= scale;
                            point.profileScale *= scale;
                            return point;
                        });
                        break;
                    case HairModifierType.Smooth:
                        ApplySmooth(curve, modifier, workspace);
                        break;
                    case HairModifierType.Lift:
                        ApplyPositionVector(curve, modifier, modifier.vector.normalized);
                        break;
                    case HairModifierType.Gravity:
                        ApplyGravity(curve, modifier, workspace);
                        break;
                    case HairModifierType.FlowAlign:
                        // Children already inherit guide-domain deformation through interpolation.
                        // Applying a combined-domain align again would double their rotation.
                        if (domain != HairModifierDomain.Children || modifier.domain == HairModifierDomain.Children)
                            ApplyFlowAlign(curve, modifier);
                        break;
                    case HairModifierType.Clump:
                        ApplyClump(curve, modifier, groom.FindHelper(modifier.helperId)?.position ?? workspace.groupClumpTip);
                        break;
                    case HairModifierType.Part:
                        ApplyPart(curve, modifier, groom.SymmetryPlanePoint);
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
                        ApplyHelperFollow(curve, modifier, groom, workspace);
                        break;
                    case HairModifierType.PushOut:
                    case HairModifierType.Collision:
                        ApplyHelperCollision(curve, modifier, groom, workspace);
                        break;
                    case HairModifierType.TrimByMesh:
                        ApplyTrim(curve, modifier, workspace.sourceMesh.Surface());
                        break;
                    case HairModifierType.SurfaceProjection:
                        ApplySurfaceProjection(curve, modifier, workspace.sourceMesh);
                        break;
                    case HairModifierType.Mirror:
                        ApplyMirror(curve, modifier, groom);
                        break;
                    case HairModifierType.LodReduction:
                        int originalSamples = curve.samplesPerCardOverride > 0 ? curve.samplesPerCardOverride : curve.profile?.SamplesPerCard ?? curve.points.Count;
                        curve.samplesPerCardOverride = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(originalSamples,
                            Mathf.Max(2f, modifier.amount), Mathf.Clamp01(modifier.weight))), 2, Mathf.Max(2, originalSamples));
                        break;
                }
                if (preserveShapeLength) FinishModifierShape(curve, workspace);
            }
        }

        private static float Influence(HairModifierSettings modifier, float t)
        {
            float ramp = modifier.rootToTip != null ? modifier.rootToTip.Evaluate(t) : 1f;
            return float.IsFinite(ramp) && float.IsFinite(modifier.weight) ? Mathf.Clamp01(ramp * modifier.weight) : 0f;
        }

        private static float ShapeWeight(HairModifierSettings modifier, float t, float weight)
            => t <= 0f ? 0f : weight * HairGuideShapeUtility.RootInfluence(t,
                float.IsFinite(modifier.rootInfluence) ? modifier.rootInfluence : 0f);

        private static bool PreservesShapeLength(HairModifierType type) => type == HairModifierType.Smooth ||
            type == HairModifierType.FlowAlign || type == HairModifierType.Lift || type == HairModifierType.Clump ||
            type == HairModifierType.Part || type == HairModifierType.Curl || type == HairModifierType.Wave ||
            type == HairModifierType.Noise || type == HairModifierType.HelperFollow ||
            type == HairModifierType.Collision || type == HairModifierType.PushOut;

        private static void CaptureModifierShape(HairEvaluatedCurve curve, HairEvaluationWorkspace workspace)
        {
            workspace.modifierOriginal.Clear(); workspace.modifierTarget.Clear();
            while (workspace.modifierControls.Count < curve.points.Count) workspace.modifierControls.Add(new HairGuidePoint());
            for (int i = 0; i < curve.points.Count; i++)
            {
                HairCurvePoint point = curve.points[i];
                workspace.modifierOriginal.Add(point.position); workspace.modifierTarget.Add(point.position);
                workspace.modifierControls[i].freeze = Mathf.Clamp01(point.freeze);
                workspace.modifierControls[i].stiffness = Mathf.Clamp01(point.stiffness);
            }
        }

        private static void FinishModifierShape(HairEvaluatedCurve curve, HairEvaluationWorkspace workspace)
        {
            bool changed = false;
            for (int i = 0; i < curve.points.Count; i++)
            {
                Vector3 target = curve.points[i].position;
                if (!float.IsFinite(target.sqrMagnitude)) target = workspace.modifierOriginal[i];
                target = Vector3.Lerp(target, workspace.modifierOriginal[i], Mathf.Clamp01(curve.points[i].freeze));
                workspace.modifierTarget[i] = target;
                changed |= !target.Equals(workspace.modifierOriginal[i]);
            }
            if (changed) HairGuideShapeUtility.PreserveSegmentLengths(workspace.modifierOriginal,
                workspace.modifierTarget, workspace.modifierControls);
            WriteModifierShape(curve, workspace);
        }

        private static void WriteModifierShape(HairEvaluatedCurve curve, HairEvaluationWorkspace workspace)
        {
            for (int i = 0; i < curve.points.Count; i++)
            { HairCurvePoint point = curve.points[i]; point.position = workspace.modifierTarget[i]; curve.points[i] = point; }
        }

        private static void ApplySmooth(HairEvaluatedCurve curve, HairModifierSettings modifier, HairEvaluationWorkspace workspace)
        {
            for (int i = 1; i < curve.points.Count - 1; i++)
            {
                float t = i / (curve.points.Count - 1f);
                HairCurvePoint point = curve.points[i];
                point.position = Vector3.Lerp(point.position,
                    (workspace.modifierOriginal[i - 1] + workspace.modifierOriginal[i + 1]) * 0.5f,
                    ShapeWeight(modifier, t, Influence(modifier, t)) * Mathf.Clamp01(modifier.amount));
                curve.points[i] = point;
            }
        }

        private static void ApplyGravity(HairEvaluatedCurve curve, HairModifierSettings modifier, HairEvaluationWorkspace workspace)
        {
            if (curve.points.Count < 2 || modifier.amount <= 0f || !float.IsFinite(modifier.gravityStrength) || modifier.gravityStrength <= 0f) return;
            HairEvaluationOptions options = workspace.options;
            Matrix4x4 toSurface = options?.guideToSourcePose?.Invoke(curve.parentGuideId) ?? Matrix4x4.identity;
            Matrix4x4 toWorld = (options?.sourceToWorld ?? Matrix4x4.identity) * toSurface;
            if (!float.IsFinite(toWorld.determinant) || Mathf.Abs(toWorld.determinant) < 1e-12f) return;
            Matrix4x4 fromWorld = toWorld.inverse;
            Vector3 gravity = modifier.useWorldGravity ? options?.worldGravity ?? Physics.gravity : toWorld.MultiplyVector(modifier.gravityDirection);
            if (!float.IsFinite(gravity.sqrMagnitude) || gravity.sqrMagnitude <= 1e-12f) return;
            // Duration is an authored, deterministic settle from the input shape, never elapsed
            // editor time. Repeated preview/runtime builds cannot accumulate deformation.
            float duration = Mathf.Clamp(modifier.amount, 0f, 5f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(duration * 60f));
            float deltaTime = duration / steps;
            Vector3 targetDirection = HairGuideShapeUtility.StableGravityDirection(gravity,
                fromWorld.transpose.MultiplyVector(curve.rootNormal).normalized, curve.seed, modifier.gravitySeparation);
            HairMeshRaycaster surface = modifier.gravityCollision ? workspace.gravitySurface.Surface() : null;
            for (int step = 0; step < steps; step++)
            {
                CaptureModifierShape(curve, workspace);
                bool changed = false;
                for (int i = 1; i < curve.points.Count; i++)
                {
                    HairCurvePoint point = curve.points[i];
                    if (point.freeze >= 0.999f) continue;
                    Vector3 segment = workspace.modifierOriginal[i] - workspace.modifierOriginal[i - 1];
                    float length = segment.magnitude;
                    if (length <= 1e-8f) continue;
                    float t = i / (curve.points.Count - 1f);
                    float settle = HairGuideShapeUtility.GravityResponse(t, modifier.rootInfluence, point.stiffness,
                        point.freeze, modifier.gravityStrength * Influence(modifier, t), deltaTime);
                    if (settle <= 1e-7f) continue;
                    changed = true;
                    workspace.modifierTarget[i] = workspace.modifierTarget[i - 1] +
                        HairGuideShapeUtility.SettleSegment(segment, targetDirection, settle, toWorld, fromWorld);
                }
                if (!changed) return;
                HairGuideShapeUtility.PreserveSegmentLengths(workspace.modifierOriginal, workspace.modifierTarget, workspace.modifierControls);
                if (surface != null) HairGuideShapeUtility.ConstrainToSurface(workspace.modifierOriginal,
                    workspace.modifierTarget, workspace.modifierControls, surface, Mathf.Clamp(modifier.gravityClearance, 0f, 0.05f),
                    toSurface, toSurface.inverse);
                WriteModifierShape(curve, workspace);
            }
        }

        private static void ApplyTrim(HairEvaluatedCurve curve, HairModifierSettings modifier, HairMeshRaycaster mesh)
        {
            if (mesh == null || curve.points.Count < 2) return;
            float total = curve.Length, traversed = 0f;
            for (int i = 1; i < curve.points.Count; i++)
            {
                Vector3 start = curve.points[i - 1].position, segment = curve.points[i].position - start;
                float length = segment.magnitude;
                if (length > 1e-7f && mesh.Raycast(new Ray(start, segment / length), out HairMeshRaycastHit hit) && hit.Distance <= length)
                {
                    float intersection = Mathf.Max(0f, traversed + hit.Distance - Mathf.Max(0f, modifier.amount));
                    float blend = Influence(modifier, (traversed + hit.Distance) / Mathf.Max(total, 1e-7f));
                    if (blend <= 0f) return;
                    float cutLength = Mathf.Lerp(total, intersection, blend);
                    if (cutLength <= 1e-6f) return; // Keep the attachment rather than emit a root-only card.
                    float distance = 0f;
                    for (int cut = 1; cut < curve.points.Count; cut++)
                    {
                        HairCurvePoint a = curve.points[cut - 1], b = curve.points[cut];
                        float span = Vector3.Distance(a.position, b.position);
                        if (distance + span >= cutLength)
                        {
                            for (int pin = cut; pin < curve.points.Count; pin++) if (curve.points[pin].freeze >= 0.999f) return;
                            float t = span > 1e-8f ? (cutLength - distance) / span : 0f;
                            curve.points[cut] = new HairCurvePoint(Vector3.Lerp(a.position, b.position, t),
                                Mathf.Lerp(a.width, b.width, t), Mathf.LerpAngle(a.roll, b.roll, t),
                                Mathf.Lerp(a.widthBaseline, b.widthBaseline, t), Mathf.Lerp(a.profileScale, b.profileScale, t),
                                Mathf.Lerp(a.stiffness, b.stiffness, t), Mathf.Lerp(a.freeze, b.freeze, t));
                            curve.points.RemoveRange(cut + 1, curve.points.Count - cut - 1);
                            return;
                        }
                        distance += span;
                    }
                    return;
                }
                traversed += length;
            }
        }

        private static void ApplyClump(HairEvaluatedCurve curve, HairModifierSettings modifier, Vector3 clumpTip)
        {
            if (curve.points.Count < 2) return;
            Vector3 root = curve.points[0].position;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                Vector3 center = Vector3.Lerp(root, clumpTip, t);
                point.position = Vector3.Lerp(point.position, center, ShapeWeight(modifier, t, weight) * Mathf.Clamp01(modifier.amount));
                return point;
            });
        }

        private static void ApplyPart(HairEvaluatedCurve curve, HairModifierSettings modifier, Vector3 planePoint)
        {
            Vector3 normal = modifier.vector.sqrMagnitude > 1e-8f ? modifier.vector.normalized : Vector3.right;
            float side = Mathf.Sign(Vector3.Dot(curve.points[0].position - planePoint, normal));
            if (Mathf.Approximately(side, 0f)) side = 1f;
            ApplyPositionVector(curve, modifier, normal * side);
        }

        private static void ApplySurfaceProjection(HairEvaluatedCurve curve, HairModifierSettings modifier, SourceMeshReadCache mesh)
        {
            using var projectionScope = ProjectionMarker.Auto();
            if (!mesh.IsValid) return;
            for (int pointIndex = 1; pointIndex < curve.points.Count; pointIndex++)
            {
                HairCurvePoint point = curve.points[pointIndex];
                if (!mesh.TryProject(point.position, curve.rootNormal, modifier.amount, out Vector3 target)) continue;
                float t = pointIndex / (curve.points.Count - 1f);
                point.position = Vector3.Lerp(point.position, target, Influence(modifier, t) * (1f - Mathf.Clamp01(point.freeze)));
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
            float rootWeight = Influence(modifier, 0f);
            Vector3 reflectedNormal = curve.rootNormal - normal * (2f * Vector3.Dot(curve.rootNormal, normal));
            Vector3 blendedNormal = Vector3.Lerp(curve.rootNormal, reflectedNormal, rootWeight);
            if (rootWeight > 0f && blendedNormal.sqrMagnitude > 1e-12f) curve.rootNormal = blendedNormal.normalized;
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
                float influence = Influence(modifier, t);
                if (influence > 0f) curve.points[i] = operation(curve.points[i], t, influence);
            }
        }

        private static void ApplyFlowAlign(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            float directionSquare = modifier.vector.sqrMagnitude;
            if (curve.points.Count < 2 || !float.IsFinite(directionSquare) || directionSquare <= 1e-12f ||
                !float.IsFinite(modifier.amount) || !float.IsFinite(modifier.weight)) return;
            float strength = Mathf.Clamp01(modifier.amount) * Mathf.Clamp01(modifier.weight);
            if (strength <= 0f) return;
            Vector3 targetDirection = modifier.vector / Mathf.Sqrt(directionSquare);
            Vector3 previousSource = curve.points[0].position;
            // Rotate original segment directions, then reconstruct from the unchanged root.
            // Do not derive lengths from the already-deformed preceding point.
            for (int i = 1; i < curve.points.Count; i++)
            {
                HairCurvePoint point = curve.points[i];
                Vector3 sourceStart = previousSource;
                Vector3 segment = point.position - sourceStart;
                previousSource = point.position;
                float length = segment.magnitude;
                float t = i / (curve.points.Count - 1f);
                float ramp = modifier.rootToTip != null ? modifier.rootToTip.Evaluate(t) : 1f;
                float influence = float.IsFinite(ramp) ? ShapeWeight(modifier, t, Mathf.Clamp01(strength * ramp)) : 0f;
                if (influence <= 0f && curve.points[i - 1].position.Equals(sourceStart)) continue;
                if (length > 0f && float.IsFinite(length) && influence > 0f)
                {
                    Vector3 direction = segment / length;
                    Quaternion rotation = Quaternion.SlerpUnclamped(Quaternion.identity,
                        Quaternion.FromToRotation(direction, targetDirection), influence);
                    segment = (rotation * direction).normalized * length;
                }
                point.position = curve.points[i - 1].position + segment;
                curve.points[i] = point;
            }
        }

        private static void ApplyPositionVector(HairEvaluatedCurve curve, HairModifierSettings modifier, Vector3 direction)
        {
            if (!float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < 1e-8f) return;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                point.position += direction * modifier.amount * ShapeWeight(modifier, t, weight) * t;
                return point;
            });
        }

        private static void ApplyWave(HairEvaluatedCurve curve, HairModifierSettings modifier, bool circular)
        {
            if (curve.points.Count < 2) return;
            Vector3 tangent = HairCurveUtility.CalculateTangent(curve.points, 0);
            Vector3 axisA = Vector3.Cross(tangent, curve.rootNormal).normalized;
            if (axisA.sqrMagnitude < 1e-8f) axisA = Vector3.Cross(tangent,
                Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 axisB = Vector3.Cross(tangent, axisA).normalized;
            float cycles = Mathf.Max(0.01f, Mathf.Abs(modifier.vector.x));
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                float phase = t * Mathf.PI * 2f * cycles + modifier.vector.y;
                Vector3 offset = axisA * Mathf.Sin(phase);
                if (circular) offset += axisB * Mathf.Cos(phase);
                point.position += offset * modifier.amount * ShapeWeight(modifier, t, weight) * t;
                return point;
            });
        }

        private static void ApplyNoise(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            HairDeterministicRandom random = new HairDeterministicRandom(modifier.seed ^ curve.seed);
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                Vector3 noise = new Vector3(random.NextSigned(), random.NextSigned(), random.NextSigned());
                point.position += noise * modifier.amount * ShapeWeight(modifier, t, weight) * t;
                return point;
            });
        }

        private static void ApplyHelperFollow(HairEvaluatedCurve curve, HairModifierSettings modifier, HairGroomAsset groom, HairEvaluationWorkspace workspace)
        {
            HairHelper helper = groom.FindHelper(modifier.helperId);
            if (helper == null || helper.points == null || helper.points.Count < 2) return;
            List<HairCurvePoint> helperCurve = workspace.helperCurve;
            helperCurve.Clear();
            for (int i = 0; i < helper.points.Count; i++) helperCurve.Add(new HairCurvePoint(helper.points[i], 0f, 0f));
            HairCurveUtility.ResampleInto(helperCurve, curve.points.Count, workspace.resampled, ref workspace.cumulative);
            List<HairCurvePoint> samples = workspace.resampled;
            Vector3 sourceRoot = samples[0].position;
            Vector3 targetRoot = curve.points[0].position;
            ApplyPerPoint(curve, modifier, (point, t, weight) =>
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(t * (samples.Count - 1)), 0, samples.Count - 1);
                Vector3 target = targetRoot + samples[index].position - sourceRoot;
                point.position = Vector3.Lerp(point.position, target, ShapeWeight(modifier, t, weight) * Mathf.Clamp01(modifier.amount));
                return point;
            });
        }

        private static void ApplyHelperCollision(HairEvaluatedCurve curve, HairModifierSettings modifier, HairGroomAsset groom, HairEvaluationWorkspace workspace)
        {
            HairHelper helper = groom.FindHelper(modifier.helperId);
            if (helper == null) return;
            bool active = false;
            for (int i = 1; i < curve.points.Count; i++)
                active |= Influence(modifier, i / (curve.points.Count - 1f)) > 0f && curve.points[i].freeze < 0.999f;
            if (!active) return;
            // Solve a full clearance target first, alternating collision and inextensible
            // strand constraints. Blend toward that target once, not once per solver iteration.
            for (int iteration = 0; iteration < 32; iteration++)
            {
                bool moved = false;
                for (int i = 1; i < curve.points.Count; i++)
                {
                    if (curve.points[i].freeze >= 0.999f) continue;
                    for (int sample = 1; sample <= 4; sample++)
                    {
                        float t = sample * 0.25f;
                        Vector3 position = Vector3.Lerp(workspace.modifierTarget[i - 1], workspace.modifierTarget[i], t);
                        Vector3 correction = PushOutsideHelper(position, helper, 1f, modifier.amount) - position;
                        if (correction.sqrMagnitude <= 1e-12f) continue;
                        workspace.modifierTarget[i] += correction / t;
                        moved = true;
                    }
                }
                if (!moved) break;
                HairGuideShapeUtility.PreserveSegmentLengths(workspace.modifierOriginal, workspace.modifierTarget, workspace.modifierControls);
            }
            for (int i = 1; i < curve.points.Count; i++)
            {
                HairCurvePoint point = curve.points[i];
                float t = i / (curve.points.Count - 1f);
                float blend = ShapeWeight(modifier, t, Influence(modifier, t));
                point.position = Vector3.Lerp(workspace.modifierOriginal[i], workspace.modifierTarget[i], blend);
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

        private static Vector3 PushOutsideHelper(Vector3 point, HairHelper helper, float weight, float clearance = 0f)
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
            Vector3 target = localToWorld.MultiplyPoint3x4(projected);
            Vector3 pushDirection = (target - point).normalized;
            return Vector3.Lerp(point, target + pushDirection * Mathf.Max(0f, clearance), blend);
        }

        private static HairLodSettings ResolveLod(HairGroomAsset groom, int level)
        {
            if (groom.Lods == null || groom.Lods.Count == 0) return null;
            HairLodSettings exact = groom.Lods.Find(candidate => candidate != null && candidate.level == level);
            return exact ?? groom.Lods[0];
        }
    }
}
