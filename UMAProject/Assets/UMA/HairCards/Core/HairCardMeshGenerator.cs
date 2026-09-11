using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Caller-owned scratch buffers for sequential builds. Output meshes never alias these buffers.</summary>
    public sealed class HairMeshBuildWorkspace
    {
        internal readonly List<Vector3> vertices = new List<Vector3>();
        internal readonly List<Vector3> normals = new List<Vector3>();
        internal readonly List<Vector4> tangents = new List<Vector4>();
        internal readonly List<Vector2> uvs = new List<Vector2>();
        internal readonly List<Color> colors = new List<Color>();
        internal readonly List<HairCardMeshGenerator.MaterialBucket> buckets = new List<HairCardMeshGenerator.MaterialBucket>();
        internal readonly Stack<HairCardMeshGenerator.MaterialBucket> spareBuckets = new Stack<HairCardMeshGenerator.MaterialBucket>();
        internal readonly Dictionary<HairAtlasProfileAsset, int> materialLookup = new Dictionary<HairAtlasProfileAsset, int>();
        internal readonly List<HairCurvePoint> sampled = new List<HairCurvePoint>();
        internal float[] cumulative = Array.Empty<float>();
        internal Vector3[] curveTangents = Array.Empty<Vector3>(), sides = Array.Empty<Vector3>(), frameNormals = Array.Empty<Vector3>();
        private readonly Dictionary<(HairCardProfileAsset, int), float[]> profileWidths = new Dictionary<(HairCardProfileAsset, int), float[]>();
        private readonly Stack<float[]> spareWidths = new Stack<float[]>();
        internal bool inUse;

        internal void Begin()
        {
            if (inUse) throw new InvalidOperationException("A mesh workspace cannot be shared by concurrent builds.");
            inUse = true;
            vertices.Clear(); normals.Clear(); tangents.Clear(); uvs.Clear(); colors.Clear();
        }

        internal void End()
        {
            foreach (float[] widths in profileWidths.Values) spareWidths.Push(widths);
            profileWidths.Clear();
            foreach (HairCardMeshGenerator.MaterialBucket bucket in buckets)
            {
                bucket.material = null;
                bucket.atlas = null;
                bucket.triangles.Clear();
                spareBuckets.Push(bucket);
            }
            buckets.Clear(); materialLookup.Clear();
            inUse = false;
        }

        internal HairCardMeshGenerator.MaterialBucket RentBucket(HairAtlasProfileAsset atlas)
        {
            HairCardMeshGenerator.MaterialBucket bucket = spareBuckets.Count > 0
                ? spareBuckets.Pop() : new HairCardMeshGenerator.MaterialBucket();
            bucket.atlas = atlas;
            bucket.material = atlas != null ? atlas.material : null;
            return bucket;
        }

        internal void EnsureFrames(int count)
        {
            if (curveTangents.Length >= count) return;
            int capacity = Mathf.NextPowerOfTwo(count);
            Array.Resize(ref curveTangents, capacity);
            Array.Resize(ref sides, capacity);
            Array.Resize(ref frameNormals, capacity);
        }

        internal float[] ProfileWidths(HairCardProfileAsset profile, int count)
        {
            // Re-evaluate once per build/profile/resolution, so editing the AnimationCurve
            // takes effect immediately without thousands of repeated native Evaluate calls.
            var key = (profile, count);
            if (profileWidths.TryGetValue(key, out float[] widths)) return widths;
            widths = spareWidths.Count > 0 ? spareWidths.Pop() : Array.Empty<float>();
            if (widths.Length < count) Array.Resize(ref widths, Mathf.NextPowerOfTwo(count));
            bool hasProfile = profile != null;
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f);
                widths[i] = hasProfile ? profile.EvaluateWidth(t) : Mathf.Lerp(0.01f, 0f, t);
            }
            profileWidths.Add(key, widths);
            return widths;
        }

        public void Clear()
        {
            if (inUse) throw new InvalidOperationException("Cannot clear a workspace during a build.");
            vertices.Clear(); vertices.Capacity = 0; normals.Clear(); normals.Capacity = 0;
            tangents.Clear(); tangents.Capacity = 0; uvs.Clear(); uvs.Capacity = 0;
            colors.Clear(); colors.Capacity = 0; sampled.Clear(); sampled.Capacity = 0;
            buckets.Clear(); buckets.Capacity = 0; spareBuckets.Clear(); spareBuckets.TrimExcess();
            materialLookup.Clear(); materialLookup.TrimExcess();
            profileWidths.Clear(); profileWidths.TrimExcess(); spareWidths.Clear(); spareWidths.TrimExcess();
            cumulative = Array.Empty<float>();
            curveTangents = sides = frameNormals = Array.Empty<Vector3>();
        }
    }

    public static class HairCardMeshGenerator
    {
        private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("HairCards.BuildMesh");
        internal sealed class MaterialBucket
        {
            public Material material;
            public HairAtlasProfileAsset atlas;
            public readonly List<int> triangles = new List<int>();
        }

        public static HairCardMeshBuildResult Build(
            HairEvaluationResult evaluation,
            string meshName = "Generated Hair Cards") => Build(evaluation, meshName, false);

        public static HairCardMeshBuildResult Build(
            HairEvaluationResult evaluation, string meshName, bool includeEditingMetadata)
            => Build(evaluation, meshName, includeEditingMetadata, new HairMeshBuildWorkspace());

        public static HairCardMeshBuildResult Build(HairEvaluationResult evaluation, string meshName,
            bool includeEditingMetadata, HairMeshBuildWorkspace workspace)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            workspace.Begin();
            try { using (BuildMarker.Auto()) return BuildCore(evaluation, meshName, includeEditingMetadata, workspace, new HairCardMeshBuildResult()); }
            finally { workspace.End(); }
        }

        /// <summary>Opt-in mutable preview build. Reuses the caller-owned mesh, card spans and
        /// metadata lists; callers must not retain snapshots of this result across updates.
        /// Build still returns independent, immutable-by-convention outputs for baking/export.</summary>
        public static HairCardMeshBuildResult Update(HairEvaluationResult evaluation, string meshName,
            HairCardMeshBuildResult result, HairMeshBuildWorkspace workspace)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            workspace.Begin();
            try
            {
                using (BuildMarker.Auto())
                    return BuildCore(evaluation, meshName, true, workspace, result ?? new HairCardMeshBuildResult(), true);
            }
            finally { workspace.End(); }
        }

        private static HairCardMeshBuildResult BuildCore(HairEvaluationResult evaluation, string meshName,
            bool includeEditingMetadata, HairMeshBuildWorkspace workspace, HairCardMeshBuildResult result, bool dynamic = false)
        {
            Mesh mesh = result.mesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = string.IsNullOrWhiteSpace(meshName) ? "Generated Hair Cards" : meshName };
                if (dynamic) mesh.MarkDynamic();
            }
            else mesh.Clear();
            result.mesh = mesh;
            result.cardCount = result.vertexCount = result.triangleCount = result.degenerateTriangleCount = result.frameFlipCount = 0;
            result.materials.Clear(); result.atlases.Clear(); result.materialNames.Clear(); result.localUvs.Clear();
            result.secondPasses.Clear();
            if (evaluation == null || evaluation.curves.Count == 0)
            {
                mesh.Clear();
                mesh.bounds = new Bounds();
                result.cards.Clear();
                return result;
            }

            List<Vector3> vertices = workspace.vertices;
            List<Vector3> normals = workspace.normals;
            List<Vector4> tangents = workspace.tangents;
            List<Vector2> uvs = workspace.uvs;
            List<Color> colors = workspace.colors;
            List<MaterialBucket> buckets = workspace.buckets;

            for (int curveIndex = 0; curveIndex < evaluation.curves.Count; curveIndex++)
            {
                HairEvaluatedCurve curve = evaluation.curves[curveIndex];
                if (curve == null || curve.points.Count < 2) continue;
                HairCardProfileAsset profile = curve.profile;
                int sampleCount = curve.samplesPerCardOverride > 1
                    ? curve.samplesPerCardOverride
                    : profile != null ? profile.SamplesPerCard : 12;
                List<HairCurvePoint> sampled = workspace.sampled;
                HairCurveUtility.ResampleInto(curve.points, sampleCount, sampled, ref workspace.cumulative);
                EmbedCardRoot(curve, sampled);
                workspace.EnsureFrames(sampled.Count);
                HairAtlasRegion region = curve.atlas?.GetWeightedRegion(HashCurve(curve),
                    curve.atlasRegionSelection, curve.atlasRegionIds);
                int bucketIndex = GetMaterialBucket(curve.atlas, workspace);
                int firstVertex = vertices.Count;
                HairCardSpan span = null;
                if (includeEditingMetadata)
                {
                    span = result.cardCount < result.cards.Count ? result.cards[result.cardCount] : new HairCardSpan();
                    if (result.cardCount >= result.cards.Count) result.cards.Add(span);
                    span.curve = curve; span.uvSetId = region?.Id; span.vertexStart = vertices.Count;
                    span.submesh = bucketIndex; span.triangleStart = buckets[bucketIndex].triangles.Count / 3;
                }
                int flips;
                if (profile != null && profile.Shape == HairCardShape.TaperedTube)
                {
                    int sides = curve.tubeSidesOverride > 2
                        ? Mathf.Min(profile.TubeSides, curve.tubeSidesOverride)
                        : profile.TubeSides;
                    AppendTube(curve, sampled, profile, region, sides, vertices, normals, tangents, uvs,
                        colors, buckets[bucketIndex].triangles, workspace, out flips, ref result.degenerateTriangleCount);
                }
                else
                {
                    AppendRibbon(curve, sampled, profile, region, vertices, normals, tangents, uvs,
                        colors, buckets[bucketIndex].triangles, workspace, out flips, ref result.degenerateTriangleCount);
                }
                result.frameFlipCount += flips;
                result.cardCount++;
                if (span != null)
                {
                    span.vertexCount = vertices.Count - span.vertexStart;
                    span.triangleCount = buckets[bucketIndex].triangles.Count / 3 - span.triangleStart;
                }
                for (int vertex = firstVertex; vertex < vertices.Count; vertex++)
                {
                    if (includeEditingMetadata) result.localUvs.Add(uvs[vertex]);
                    uvs[vertex] = MapUv(region, uvs[vertex].x, uvs[vertex].y);
                }
            }

            if (result.cards.Count > result.cardCount) result.cards.RemoveRange(result.cardCount, result.cards.Count - result.cardCount);
            IndexFormat format = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            if (mesh.indexFormat != format) mesh.indexFormat = format;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            int secondPassCount = 0;
            foreach (MaterialBucket bucket in buckets)
                if (bucket.atlas != null && bucket.atlas.secondPassMaterial != null) secondPassCount++;
            mesh.subMeshCount = Mathf.Max(1, buckets.Count + secondPassCount);
            if (buckets.Count == 0)
            {
                mesh.SetTriangles(Array.Empty<int>(), 0, false);
            }
            else
            {
                for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    mesh.SetTriangles(buckets[bucketIndex].triangles, bucketIndex, false);
                    result.materials.Add(buckets[bucketIndex].material);
                    result.atlases.Add(buckets[bucketIndex].atlas);
                    result.secondPasses.Add(false);
                    result.materialNames.Add(buckets[bucketIndex].material != null
                        ? buckets[bucketIndex].material.name
                        : "Default Hair Material");
                }
                // Repeat only the draw descriptor, not vertices or index buffers. Keep primary
                // submesh indices stable for card picking, highlights, and UMA slot export.
                int passIndex = buckets.Count;
                for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    HairAtlasProfileAsset atlas = buckets[bucketIndex].atlas;
                    if (atlas == null || atlas.secondPassMaterial == null) continue;
                    mesh.SetSubMesh(passIndex++, mesh.GetSubMesh(bucketIndex), MeshUpdateFlags.DontRecalculateBounds);
                    result.materials.Add(atlas.secondPassMaterial);
                    result.atlases.Add(atlas);
                    result.secondPasses.Add(true);
                    result.materialNames.Add(atlas.secondPassMaterial.name + " (Second Pass)");
                }
            }
            mesh.RecalculateBounds();
            result.vertexCount = vertices.Count;
            int indexCount = 0;
            for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
            {
                indexCount += buckets[bucketIndex].triangles.Count;
            }
            result.triangleCount = indexCount / 3;
            return result;
        }

        private static void EmbedCardRoot(HairEvaluatedCurve curve, List<HairCurvePoint> sampled)
        {
            if (!float.IsFinite(curve.rootEmbedDepth) || curve.rootEmbedDepth <= 0f || sampled.Count < 2) return;
            Vector3 inward = -curve.rootNormal.normalized;
            if (!float.IsFinite(inward.x) || !float.IsFinite(inward.y) || !float.IsFinite(inward.z)) return;
            float depth = Mathf.Min(curve.rootEmbedDepth, 0.02f);
            // Samples are evenly spaced by original arc length. Apply only to this build's
            // scratch buffer: editing, constraints, children and repeated builds never accumulate inset.
            for (int i = 0; i < sampled.Count; i++)
            {
                float t = i / (sampled.Count - 1f);
                if (t >= 0.2f) break;
                HairCurvePoint point = sampled[i];
                point.position += inward * (depth * (1f - Mathf.SmoothStep(0f, 1f, t / 0.2f)));
                sampled[i] = point;
            }
        }

        private static void AppendRibbon(
            HairEvaluatedCurve curve,
            IReadOnlyList<HairCurvePoint> points,
            HairCardProfileAsset profile,
            HairAtlasRegion region,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            HairMeshBuildWorkspace workspace,
            out int flipCount,
            ref int degenerateCount)
        {
            int start = vertices.Count;
            Vector3[] curveTangents = workspace.curveTangents;
            Vector3[] sides = workspace.sides;
            Vector3[] frameNormals = workspace.frameNormals;
            HairCurveUtility.BuildRotationMinimizingFrames(points, curve.rootNormal, curveTangents,
                sides, frameNormals, out flipCount);
            bool hasProfile = profile != null;
            float defaultWidth = hasProfile ? profile.DefaultWidth : 0f;
            float[] widths = workspace.ProfileWidths(profile, points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                float t = i / (points.Count - 1f);
                float width = ResolveCardWidth(points[i], hasProfile, defaultWidth, widths[i]);
                Vector3 half = sides[i] * (width * 0.5f);
                vertices.Add(points[i].position - half);
                vertices.Add(points[i].position + half);
                normals.Add(frameNormals[i]);
                normals.Add(frameNormals[i]);
                Vector4 tangent = new Vector4(curveTangents[i].x, curveTangents[i].y, curveTangents[i].z, 1f);
                tangents.Add(tangent);
                tangents.Add(tangent);
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
                Color vertexColor = hasProfile ? profile.EvaluateVertexColor(i, points.Count, curve.groupColor) : curve.groupColor;
                colors.Add(vertexColor);
                colors.Add(vertexColor);
            }

            bool doubleSided = profile == null || profile.DoubleSided;
            for (int i = 0; i < points.Count - 1; i++)
            {
                int a = start + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                bool startCollapsed = (vertices[a] - vertices[b]).sqrMagnitude < 1e-14f;
                bool endCollapsed = (vertices[c] - vertices[d]).sqrMagnitude < 1e-14f;
                if (!startCollapsed)
                {
                    AddTriangle(vertices, triangles, a, c, b, ref degenerateCount);
                    if (doubleSided) AddTriangle(vertices, triangles, b, c, a, ref degenerateCount);
                }
                if (!endCollapsed)
                {
                    AddTriangle(vertices, triangles, b, c, d, ref degenerateCount);
                    if (doubleSided) AddTriangle(vertices, triangles, d, c, b, ref degenerateCount);
                }
            }
        }

        private static void AppendTube(
            HairEvaluatedCurve curve,
            IReadOnlyList<HairCurvePoint> points,
            HairCardProfileAsset profile,
            HairAtlasRegion region,
            int sidesPerRing,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            HairMeshBuildWorkspace workspace,
            out int flipCount,
            ref int degenerateCount)
        {
            int start = vertices.Count;
            Vector3[] curveTangents = workspace.curveTangents;
            Vector3[] sides = workspace.sides;
            Vector3[] frameNormals = workspace.frameNormals;
            HairCurveUtility.BuildRotationMinimizingFrames(points, curve.rootNormal, curveTangents,
                sides, frameNormals, out flipCount);
            int sidesCount = Mathf.Clamp(sidesPerRing, 3, 12);
            bool hasProfile = profile != null;
            float defaultWidth = hasProfile ? profile.DefaultWidth : 0f;
            float[] widths = workspace.ProfileWidths(profile, points.Count);
            // Ring angles are constant across the entire tube.
            Span<Vector2> circle = stackalloc Vector2[sidesCount];
            for (int i = 0; i < sidesCount; i++)
            {
                float angle = i / (float)sidesCount * Mathf.PI * 2f;
                circle[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            for (int ring = 0; ring < points.Count; ring++)
            {
                float t = ring / (points.Count - 1f);
                float radius = ResolveCardWidth(points[ring], hasProfile, defaultWidth, widths[ring]) * 0.5f;
                Color vertexColor = hasProfile ? profile.EvaluateVertexColor(ring, points.Count, curve.groupColor) : curve.groupColor;
                for (int sideIndex = 0; sideIndex < sidesCount; sideIndex++)
                {
                    float u = sideIndex / (float)sidesCount;
                    Vector3 radial = sides[ring] * circle[sideIndex].x + frameNormals[ring] * circle[sideIndex].y;
                    vertices.Add(points[ring].position + radial * radius);
                    normals.Add(radial.normalized);
                    tangents.Add(new Vector4(curveTangents[ring].x, curveTangents[ring].y,
                        curveTangents[ring].z, 1f));
                    uvs.Add(new Vector2(u, t));
                    colors.Add(vertexColor);
                }
            }

            for (int ring = 0; ring < points.Count - 1; ring++)
            {
                bool startCollapsed = (vertices[start + ring * sidesCount] -
                                       vertices[start + ring * sidesCount + 1]).sqrMagnitude < 1e-14f;
                bool endCollapsed = (vertices[start + (ring + 1) * sidesCount] -
                                     vertices[start + (ring + 1) * sidesCount + 1]).sqrMagnitude < 1e-14f;
                for (int sideIndex = 0; sideIndex < sidesCount; sideIndex++)
                {
                    int nextSide = (sideIndex + 1) % sidesCount;
                    int a = start + ring * sidesCount + sideIndex;
                    int b = start + ring * sidesCount + nextSide;
                    int c = start + (ring + 1) * sidesCount + sideIndex;
                    int d = start + (ring + 1) * sidesCount + nextSide;
                    if (!startCollapsed) AddTriangle(vertices, triangles, a, c, b, ref degenerateCount);
                    if (!endCollapsed) AddTriangle(vertices, triangles, b, c, d, ref degenerateCount);
                }
            }
        }

        public static float ResolveCardWidth(HairCurvePoint point, HairCardProfileAsset profile, float t)
        {
            float profileWidth = profile != null ? profile.EvaluateWidth(t) : Mathf.Lerp(0.01f, 0f, t);
            return ResolveCardWidth(point, profile != null, profile != null ? profile.DefaultWidth : 0f, profileWidth);
        }

        private static float ResolveCardWidth(HairCurvePoint point, bool hasProfile, float defaultWidth, float profileWidth)
        {
            if (point.widthBaseline >= 0f && hasProfile)
                return Mathf.Max(0f, profileWidth * point.profileScale + point.width - point.widthBaseline);
            // Direct API curves without guide metadata retain their explicit width scaling.
            if (point.width <= 0f) return profileWidth;
            if (!hasProfile) return point.width;
            return defaultWidth > 1e-6f ? point.width * profileWidth / defaultWidth : profileWidth;
        }

        private static int GetMaterialBucket(
            HairAtlasProfileAsset atlas,
            HairMeshBuildWorkspace workspace)
        {
            List<MaterialBucket> buckets = workspace.buckets;
            Dictionary<HairAtlasProfileAsset, int> lookup = workspace.materialLookup;
            if (atlas == null)
            {
                for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    if (buckets[bucketIndex].atlas == null) return bucketIndex;
                }
                int nullIndex = buckets.Count;
                buckets.Add(workspace.RentBucket(null));
                return nullIndex;
            }
            if (lookup.TryGetValue(atlas, out int existing)) return existing;
            int index = buckets.Count;
            buckets.Add(workspace.RentBucket(atlas));
            lookup.Add(atlas, index);
            return index;
        }

        public static void RefreshAtlasUvs(HairCardMeshBuildResult build, HairGroomAsset groom)
        {
            if (build?.mesh == null || groom == null || build.localUvs.Count != build.mesh.vertexCount) return;
            List<Vector2> uvs = new List<Vector2>(build.localUvs);
            Dictionary<string, HairGroup> groups = new Dictionary<string, HairGroup>();
            Dictionary<string, string[]> regionIds = new Dictionary<string, string[]>();
            foreach (HairGroup group in groom.Groups)
                if (group != null) { groups[group.Id] = group; regionIds[group.Id] = group.atlasRegionIds.ToArray(); }
            foreach (HairCardSpan span in build.cards)
            {
                if (!groups.TryGetValue(span.curve.groupId, out HairGroup group)) continue;
                span.curve.atlas = group.atlas;
                span.curve.atlasRegionSelection = group.atlasRegionSelection;
                span.curve.atlasRegionIds = regionIds[group.Id];
                HairAtlasRegion region = group.atlas?.GetWeightedRegion(HashCurve(span.curve),
                    group.atlasRegionSelection, group.atlasRegionIds);
                span.uvSetId = region?.Id;
                for (int i = span.vertexStart; i < span.vertexStart + span.vertexCount; i++)
                    uvs[i] = MapUv(region, build.localUvs[i].x, build.localUvs[i].y);
            }
            build.mesh.SetUVs(0, uvs);
        }

        private static Vector2 MapUv(HairAtlasRegion region, float u, float v)
        {
            if (region == null) return new Vector2(u, v);
            float mappedU = region.flipU ? 1f - u : u;
            float mappedV = region.flipV ? 1f - v : v;
            return new Vector2(
                region.uvRect.x + mappedU * region.uvRect.width,
                region.uvRect.y + mappedV * region.uvRect.height);
        }

        private static void AddTriangle(
            IReadOnlyList<Vector3> vertices,
            List<int> triangles,
            int a,
            int b,
            int c,
            ref int degenerateCount)
        {
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (cross.sqrMagnitude < 1e-14f)
            {
                degenerateCount++;
                return;
            }
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static uint HashCurve(HairEvaluatedCurve curve)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string value = curve.curveId ?? curve.parentGuideId ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                hash ^= (uint)curve.seed;
                hash *= 16777619u;
                return hash;
            }
        }
    }
}
