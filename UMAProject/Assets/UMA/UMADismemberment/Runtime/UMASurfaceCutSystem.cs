using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Dismemberment
{
    /// <summary>
    /// Builds tapered, atlas-attached surface cuts between two points on a posed UMA renderer.
    /// Mouse routes project onto the visible surface; non-camera callers retain a topology
    /// fallback. Metric spacing seeds independent surface-fluid sources along either route.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    [RequireComponent(typeof(UMARuntimeSurfaceDecalController))]
    public sealed class UMASurfaceCutSystem : MonoBehaviour
    {
        [SerializeField] private UMASurfaceCutProfile defaultProfile;
        [Range(0f, 1f), SerializeField] private float facingThreshold = 0.05f;
        [SerializeField] private LayerMask raycastLayers = ~0;

        private DynamicCharacterAvatar avatar;
        private UMARuntimeSurfaceDecalController surfaceDecals;
        private UMASurfaceCutProfile ownedCutProfile;
        private UMASurfaceFluidProfile ownedBleedProfile;
        private uint bleedPatternSequence;

        internal const int MaximumBleedSources = 128;

        private sealed class PathData
        {
            public Vector3[] positions;
            public Vector3[] normals;
            public Vector2[] uv;
            public float[] cumulativeDistance;
            public float length;
        }

        private readonly struct HeapEntry
        {
            public readonly int vertex;
            public readonly float distance;
            public HeapEntry(int vertex, float distance)
            {
                this.vertex = vertex;
                this.distance = distance;
            }
        }

        private sealed class MinHeap
        {
            private readonly List<HeapEntry> values = new List<HeapEntry>();
            public int Count => values.Count;

            public void Push(HeapEntry entry)
            {
                int index = values.Count;
                values.Add(entry);
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (values[parent].distance <= entry.distance) break;
                    values[index] = values[parent];
                    index = parent;
                }
                values[index] = entry;
            }

            public HeapEntry Pop()
            {
                HeapEntry result = values[0];
                int lastIndex = values.Count - 1;
                HeapEntry tail = values[lastIndex];
                values.RemoveAt(lastIndex);
                if (values.Count == 0) return result;
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= values.Count) break;
                    int right = left + 1;
                    int child = right < values.Count &&
                        values[right].distance < values[left].distance ? right : left;
                    if (values[child].distance >= tail.distance) break;
                    values[index] = values[child];
                    index = child;
                }
                values[index] = tail;
                return result;
            }
        }

        private void OnEnable()
        {
            avatar = GetComponent<DynamicCharacterAvatar>();
            surfaceDecals = GetComponent<UMARuntimeSurfaceDecalController>();
        }

        private void OnDestroy()
        {
            DestroyOwned(ownedCutProfile);
            DestroyOwned(ownedBleedProfile);
            ownedCutProfile = null;
            ownedBleedProfile = null;
        }

        public bool TryGetSurfacePoint(Ray ray, out SurfaceCutPoint point)
        {
            point = default;
            if (avatar == null) avatar = GetComponent<DynamicCharacterAvatar>();
            SkinnedMeshRenderer[] renderers = avatar?.umaData?.GetRenderers();
            if (renderers == null) return false;

            float closest = float.PositiveInfinity;
            Vector3 direction = ray.direction.normalized;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Mesh shared = renderer != null ? renderer.sharedMesh : null;
                if (shared == null || !renderer.enabled ||
                    (raycastLayers.value & (1 << renderer.gameObject.layer)) == 0) continue;

                Mesh baked = new Mesh { name = "UMA Surface Cut Raycast" };
                try
                {
                    renderer.BakeMesh(baked);
                    Vector3[] positions = baked.vertices;
                    Vector3[] normals = baked.normals;
                    Vector2[] uv = shared.uv;
                    if (positions == null || uv == null || positions.Length != uv.Length) continue;
                    for (int submesh = 0; submesh < shared.subMeshCount; submesh++)
                    {
                        int[] triangles = shared.GetTriangles(submesh);
                        for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                        {
                            int ia = triangles[triangle];
                            int ib = triangles[triangle + 1];
                            int ic = triangles[triangle + 2];
                            if ((uint)ia >= (uint)positions.Length ||
                                (uint)ib >= (uint)positions.Length ||
                                (uint)ic >= (uint)positions.Length) continue;
                            Vector3 a = renderer.transform.TransformPoint(positions[ia]);
                            Vector3 b = renderer.transform.TransformPoint(positions[ib]);
                            Vector3 c = renderer.transform.TransformPoint(positions[ic]);
                            Vector3 geometricNormal = Vector3.Cross(b - a, c - a).normalized;
                            if (Vector3.Dot(geometricNormal, -direction) < facingThreshold) continue;
                            if (!RayTriangle(ray.origin, direction, a, b, c,
                                out float distance, out Vector3 barycentric) ||
                                distance >= closest) continue;

                            Vector3 normal = geometricNormal;
                            if (normals != null && normals.Length == positions.Length)
                            {
                                Vector3 localNormal = normals[ia] * barycentric.x +
                                    normals[ib] * barycentric.y + normals[ic] * barycentric.z;
                                normal = renderer.transform.TransformDirection(localNormal).normalized;
                            }
                            closest = distance;
                            point = new SurfaceCutPoint(renderer, submesh, ia, ib, ic,
                                barycentric, ray.origin + direction * distance, normal,
                                uv[ia] * barycentric.x + uv[ib] * barycentric.y +
                                uv[ic] * barycentric.z);
                        }
                    }
                }
                finally { DestroyOwned(baked); }
            }
            return point.IsValid;
        }

        public bool TryCreateCut(SurfaceCutPoint start, SurfaceCutPoint end,
            out SurfaceCutResult result, out string error)
        {
            return TryCreateCut(start, end, defaultProfile, out result, out error);
        }

        public bool TryCreateCut(SurfaceCutPoint start, SurfaceCutPoint end,
            UMASurfaceCutProfile profile, out SurfaceCutResult result, out string error)
        {
            return TryCreateCutInternal(start, end, profile, null, default, default,
                out result, out error);
        }

        /// <summary>
        /// Creates a cut by projecting the straight screen-space drag onto the posed mesh.
        /// This is the preferred path for mouse input because it matches the drag preview and
        /// does not expose the renderer's triangle-edge topology in the finished cut.
        /// </summary>
        public bool TryCreateProjectedCut(SurfaceCutPoint start, SurfaceCutPoint end,
            Camera camera, Vector2 screenStart, Vector2 screenEnd,
            UMASurfaceCutProfile profile, out SurfaceCutResult result, out string error)
        {
            return TryCreateCutInternal(start, end, profile, camera, screenStart, screenEnd,
                out result, out error);
        }

        private bool TryCreateCutInternal(SurfaceCutPoint start, SurfaceCutPoint end,
            UMASurfaceCutProfile profile, Camera projectionCamera, Vector2 screenStart,
            Vector2 screenEnd, out SurfaceCutResult result, out string error)
        {
            result = default;
            error = null;
            if (!start.IsValid || !end.IsValid)
            {
                error = "Both surface-cut points must be valid mesh hits.";
                return false;
            }
            if (start.Renderer != end.Renderer || start.SubmeshIndex != end.SubmeshIndex)
            {
                error = "A surface cut must remain on one generated renderer and material. " +
                    "Place both points on the same body or armor surface.";
                return false;
            }
            profile = profile != null ? profile : ResolveDefaultCutProfile();
            if (surfaceDecals == null)
                surfaceDecals = GetComponent<UMARuntimeSurfaceDecalController>();
            if (surfaceDecals == null)
            {
                error = "UMARuntimeSurfaceDecalController is unavailable.";
                return false;
            }

            Mesh shared = start.Renderer.sharedMesh;
            Mesh baked = new Mesh { name = "UMA Surface Cut Path" };
            try
            {
                start.Renderer.BakeMesh(baked);
                start = RefreshSurfacePoint(start, shared, baked);
                end = RefreshSurfacePoint(end, shared, baked);
                PathData path;
                bool pathBuilt;
                if (projectionCamera != null)
                    pathBuilt = TryBuildProjectedPath(start, end, shared, baked,
                        projectionCamera, screenStart, screenEnd, out path, out error);
                else
                    pathBuilt = TryBuildPath(start, end, shared, baked, out path, out error);
                if (!pathBuilt)
                    return false;
                if (path.length < 0.002f)
                {
                    error = "Surface cuts must be at least two millimeters long.";
                    return false;
                }
                Mesh cutMesh = BuildCutMesh(start.Renderer, start.SubmeshIndex, shared, baked,
                    path, profile.widthMeters * 0.5f, out error);
                if (cutMesh == null) return false;
                uint bleedSeed = CreateBleedSeed(path, profile.bleedSpacingSeed,
                    ++bleedPatternSequence);
                BuildBleedSources(path, profile.bleedSpacingMeters,
                    profile.bleedSpacingVariation, profile.bleedEndInset, bleedSeed,
                    profile.bleedSpeedVariation, profile.bleedSizeVariation,
                    out float[] distances, out Vector3[] positions, out Vector3[] normals,
                    out float[] speedMultipliers, out float[] sizeMultipliers);
                int bleedCount = distances.Length;
                Mesh bleedMesh = bleedCount > 0
                    ? InstantiateMesh(cutMesh, "UMA Surface Cut Bleed Sources") : null;

                RuntimeDecalHandle cutHandle = surfaceDecals.AddSurfaceCut(start.Renderer,
                    start.SubmeshIndex, cutMesh, profile, path.length);
                if (!cutHandle.IsValid)
                {
                    DestroyOwned(bleedMesh);
                    error = LastSurfaceDiagnostic("The cut could not be bound to the UMA atlas.");
                    return false;
                }

                RuntimeDecalHandle bleedHandle = default;
                if (bleedCount > 0 && bleedMesh != null)
                {
                    bleedHandle = surfaceDecals.StartBleedFromSurfaceCut(start.Renderer,
                        start.SubmeshIndex, bleedMesh, ResolveBleedProfile(profile), distances,
                        positions, normals, speedMultipliers, sizeMultipliers);
                    if (!bleedHandle.IsValid) bleedCount = 0;
                }
                result = new SurfaceCutResult(cutHandle, bleedHandle, bleedCount, path.length);
                return true;
            }
            finally { DestroyOwned(baked); }
        }

        private bool TryBuildProjectedPath(SurfaceCutPoint start, SurfaceCutPoint end,
            Mesh shared, Mesh baked, Camera camera, Vector2 screenStart, Vector2 screenEnd,
            out PathData path, out string error)
        {
            path = null;
            error = null;
            Vector3[] localPositions = baked.vertices;
            Vector3[] localNormals = baked.normals;
            Vector2[] uv = shared.uv;
            int[] triangles = shared.GetTriangles(start.SubmeshIndex);
            if (localPositions == null || uv == null || localPositions.Length != uv.Length ||
                triangles == null || triangles.Length == 0)
            {
                error = "The hit renderer does not contain compatible posed vertices and UVs.";
                return false;
            }

            Transform rendererTransform = start.Renderer.transform;
            var world = new Vector3[localPositions.Length];
            var worldNormals = new Vector3[localPositions.Length];
            bool hasNormals = localNormals != null &&
                localNormals.Length == localPositions.Length;
            for (int i = 0; i < localPositions.Length; i++)
            {
                world[i] = rendererTransform.TransformPoint(localPositions[i]);
                worldNormals[i] = hasNormals
                    ? rendererTransform.TransformDirection(localNormals[i]).normalized
                    : Vector3.up;
            }

            // Four pixels is dense enough to keep the committed atlas ribbon visually aligned
            // with the preview while keeping a one-shot cut inexpensive on high-poly avatars.
            int segmentCount = Mathf.Clamp(
                Mathf.CeilToInt(Vector2.Distance(screenStart, screenEnd) / 4f), 1, 128);
            var positions = new List<Vector3>(segmentCount + 1) { start.WorldPosition };
            var normals = new List<Vector3>(segmentCount + 1) { start.WorldNormal };
            var routeUv = new List<Vector2>(segmentCount + 1) { start.AtlasUV };
            float continuityLimit = Mathf.Max(0.05f,
                Vector3.Distance(start.WorldPosition, end.WorldPosition) * 6f / segmentCount);

            for (int sample = 1; sample < segmentCount; sample++)
            {
                float t = sample / (float)segmentCount;
                Ray ray = camera.ScreenPointToRay(Vector2.Lerp(screenStart, screenEnd, t));
                if (!TryRaycastBakedSubmesh(ray, triangles, world, worldNormals, uv,
                    out Vector3 position, out Vector3 normal, out Vector2 atlasUv))
                {
                    error = $"The straight cut left the selected visible surface near " +
                        $"{t:P0}. Keep the entire preview line on one body or armor surface.";
                    return false;
                }
                if (Vector3.Distance(positions[positions.Count - 1], position) > continuityLimit)
                {
                    error = "The straight cut crossed a surface depth discontinuity. Keep the " +
                        "entire preview line on one continuous body or armor surface.";
                    return false;
                }
                AddProjectedPathPoint(position, normal, atlasUv, positions, normals, routeUv);
            }

            if (Vector3.Distance(positions[positions.Count - 1], end.WorldPosition) >
                continuityLimit)
            {
                error = "The straight cut endpoint is not continuous with the sampled surface.";
                return false;
            }
            AddProjectedPathPoint(end.WorldPosition, end.WorldNormal, end.AtlasUV,
                positions, normals, routeUv);
            return TryFinalizePath(positions, normals, routeUv, out path, out error);
        }

        private bool TryRaycastBakedSubmesh(Ray ray, int[] triangles, Vector3[] world,
            Vector3[] worldNormals, Vector2[] uv, out Vector3 position, out Vector3 normal,
            out Vector2 atlasUv)
        {
            position = default;
            normal = default;
            atlasUv = default;
            float closest = float.PositiveInfinity;
            Vector3 direction = ray.direction.normalized;
            bool found = false;
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int ia = triangles[triangle];
                int ib = triangles[triangle + 1];
                int ic = triangles[triangle + 2];
                if ((uint)ia >= (uint)world.Length || (uint)ib >= (uint)world.Length ||
                    (uint)ic >= (uint)world.Length) continue;
                Vector3 a = world[ia];
                Vector3 b = world[ib];
                Vector3 c = world[ic];
                Vector3 geometricNormal = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(geometricNormal, -direction) < facingThreshold) continue;
                if (!RayTriangle(ray.origin, direction, a, b, c,
                    out float distance, out Vector3 barycentric) || distance >= closest) continue;
                closest = distance;
                position = ray.origin + direction * distance;
                normal = (worldNormals[ia] * barycentric.x +
                    worldNormals[ib] * barycentric.y +
                    worldNormals[ic] * barycentric.z).normalized;
                atlasUv = uv[ia] * barycentric.x + uv[ib] * barycentric.y +
                    uv[ic] * barycentric.z;
                found = true;
            }
            return found;
        }

        private static void AddProjectedPathPoint(Vector3 position, Vector3 normal,
            Vector2 atlasUv, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> routeUv)
        {
            if (positions.Count > 0 &&
                Vector3.Distance(positions[positions.Count - 1], position) < 0.0001f) return;
            positions.Add(position);
            normals.Add(normal);
            routeUv.Add(atlasUv);
        }

        private static bool TryFinalizePath(List<Vector3> positions, List<Vector3> normals,
            List<Vector2> routeUv, out PathData path, out string error)
        {
            path = null;
            error = null;
            if (positions.Count < 2)
            {
                error = "The selected points produced an empty surface route.";
                return false;
            }
            var cumulative = new float[positions.Count];
            for (int i = 1; i < positions.Count; i++)
            {
                float worldStep = Vector3.Distance(positions[i - 1], positions[i]);
                if (worldStep < 0.05f && Vector2.Distance(routeUv[i - 1], routeUv[i]) > 0.5f)
                {
                    error = "The cut crosses an atlas seam. Choose two points on the same " +
                        "visible UV island so the cut will not streak across the atlas.";
                    return false;
                }
                cumulative[i] = cumulative[i - 1] + worldStep;
            }
            path = new PathData
            {
                positions = positions.ToArray(),
                normals = normals.ToArray(),
                uv = routeUv.ToArray(),
                cumulativeDistance = cumulative,
                length = cumulative[cumulative.Length - 1]
            };
            return true;
        }

        private static SurfaceCutPoint RefreshSurfacePoint(SurfaceCutPoint point,
            Mesh shared, Mesh baked)
        {
            Vector3[] positions = baked.vertices;
            Vector3[] normals = baked.normals;
            Vector2[] uv = shared.uv;
            int ia = point.VertexA;
            int ib = point.VertexB;
            int ic = point.VertexC;
            if ((uint)ia >= (uint)positions.Length || (uint)ib >= (uint)positions.Length ||
                (uint)ic >= (uint)positions.Length || uv == null || uv.Length != positions.Length)
                return point;
            Vector3 barycentric = point.Barycentric;
            Transform transform = point.Renderer.transform;
            Vector3 localPosition = positions[ia] * barycentric.x +
                positions[ib] * barycentric.y + positions[ic] * barycentric.z;
            Vector3 worldPosition = transform.TransformPoint(localPosition);
            Vector3 worldNormal = point.WorldNormal;
            if (normals != null && normals.Length == positions.Length)
            {
                Vector3 localNormal = normals[ia] * barycentric.x +
                    normals[ib] * barycentric.y + normals[ic] * barycentric.z;
                worldNormal = transform.TransformDirection(localNormal).normalized;
            }
            Vector2 atlasUv = uv[ia] * barycentric.x + uv[ib] * barycentric.y +
                uv[ic] * barycentric.z;
            return new SurfaceCutPoint(point.Renderer, point.SubmeshIndex, ia, ib, ic,
                barycentric, worldPosition, worldNormal, atlasUv);
        }

        private string LastSurfaceDiagnostic(string fallback)
        {
            IReadOnlyList<string> values = surfaceDecals.Diagnostics;
            return values.Count > 0 ? values[values.Count - 1] : fallback;
        }

        private UMASurfaceCutProfile ResolveDefaultCutProfile()
        {
            if (defaultProfile != null) return defaultProfile;
            if (ownedCutProfile != null) return ownedCutProfile;
            ownedCutProfile = ScriptableObject.CreateInstance<UMASurfaceCutProfile>();
            ownedCutProfile.name = "Runtime Surface Cut";
            ownedCutProfile.hideFlags = HideFlags.HideAndDontSave;
            return ownedCutProfile;
        }

        private UMASurfaceFluidProfile ResolveBleedProfile(UMASurfaceCutProfile profile)
        {
            if (profile.bleedProfile != null) return profile.bleedProfile;
            if (ownedBleedProfile != null) return ownedBleedProfile;
            ownedBleedProfile = ScriptableObject.CreateInstance<UMASurfaceFluidProfile>();
            ownedBleedProfile.name = "Runtime Surface Cut Blood";
            ownedBleedProfile.hideFlags = HideFlags.HideAndDontSave;
            ownedBleedProfile.color = new Color(0.22f, 0.001f, 0.002f, 0.98f);
            ownedBleedProfile.emissionDuration = 3.5f;
            ownedBleedProfile.emissionRate = 0.00055f;
            ownedBleedProfile.emissionRadiusMeters = 0.0014f;
            ownedBleedProfile.mobileLifetime = 24f;
            ownedBleedProfile.holdingDuration = 180f;
            ownedBleedProfile.fadeDuration = 300f;
            ownedBleedProfile.fallSpeedMetersPerSecond = 0.07f;
            ownedBleedProfile.maximumTravelMeters = 0.8f;
            ownedBleedProfile.viscosity = 0.56f;
            ownedBleedProfile.adhesion = 0.48f;
            ownedBleedProfile.lateralSpread = 0.016f;
            ownedBleedProfile.pooling = 0.35f;
            ownedBleedProfile.trailDepositionPerMeter = 6f;
            ownedBleedProfile.evaporation = 0.003f;
            ownedBleedProfile.depositedTrailAlpha = 0.98f;
            return ownedBleedProfile;
        }

        private static bool TryBuildPath(SurfaceCutPoint start, SurfaceCutPoint end,
            Mesh shared, Mesh baked, out PathData path, out string error)
        {
            path = null;
            error = null;
            Vector3[] localPositions = baked.vertices;
            Vector3[] localNormals = baked.normals;
            Vector2[] uv = shared.uv;
            int[] triangles = shared.GetTriangles(start.SubmeshIndex);
            if (localPositions == null || uv == null || localPositions.Length != uv.Length ||
                triangles == null || triangles.Length == 0)
            {
                error = "The hit renderer does not contain compatible posed vertices and UVs.";
                return false;
            }

            Transform transform = start.Renderer.transform;
            var world = new Vector3[localPositions.Length];
            var worldNormals = new Vector3[localPositions.Length];
            for (int i = 0; i < localPositions.Length; i++)
            {
                world[i] = transform.TransformPoint(localPositions[i]);
                worldNormals[i] = localNormals != null && localNormals.Length == localPositions.Length
                    ? transform.TransformDirection(localNormals[i]).normalized : Vector3.up;
            }

            var adjacency = new List<int>[world.Length];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdge(adjacency, triangles[i], triangles[i + 1]);
                AddEdge(adjacency, triangles[i + 1], triangles[i + 2]);
                AddEdge(adjacency, triangles[i + 2], triangles[i]);
            }

            var distances = new float[world.Length];
            var previous = new int[world.Length];
            for (int i = 0; i < distances.Length; i++)
            {
                distances[i] = float.PositiveInfinity;
                previous[i] = -1;
            }
            var heap = new MinHeap();
            int[] starts = { start.VertexA, start.VertexB, start.VertexC };
            for (int i = 0; i < starts.Length; i++)
            {
                int vertex = starts[i];
                float distance = Vector3.Distance(start.WorldPosition, world[vertex]);
                if (distance >= distances[vertex]) continue;
                distances[vertex] = distance;
                heap.Push(new HeapEntry(vertex, distance));
            }

            while (heap.Count > 0)
            {
                HeapEntry entry = heap.Pop();
                if (entry.distance > distances[entry.vertex] + 0.000001f) continue;
                List<int> neighbors = adjacency[entry.vertex];
                if (neighbors == null) continue;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    float candidate = entry.distance +
                        Vector3.Distance(world[entry.vertex], world[neighbor]);
                    if (candidate >= distances[neighbor]) continue;
                    distances[neighbor] = candidate;
                    previous[neighbor] = entry.vertex;
                    heap.Push(new HeapEntry(neighbor, candidate));
                }
            }

            int[] ends = { end.VertexA, end.VertexB, end.VertexC };
            int bestEnd = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < ends.Length; i++)
            {
                int vertex = ends[i];
                float candidate = distances[vertex] +
                    Vector3.Distance(world[vertex], end.WorldPosition);
                if (candidate >= bestDistance) continue;
                bestDistance = candidate;
                bestEnd = vertex;
            }
            if (bestEnd < 0 || float.IsInfinity(bestDistance))
            {
                error = "No connected surface route exists between the selected points. " +
                    "The points may lie on separate UV or geometry islands.";
                return false;
            }

            var vertexPath = new List<int>();
            for (int vertex = bestEnd; vertex >= 0; vertex = previous[vertex])
                vertexPath.Add(vertex);
            vertexPath.Reverse();
            var positions = new List<Vector3> { start.WorldPosition };
            var normals = new List<Vector3> { start.WorldNormal };
            var routeUv = new List<Vector2> { start.AtlasUV };
            for (int i = 0; i < vertexPath.Count; i++)
            {
                int vertex = vertexPath[i];
                if (Vector3.Distance(positions[positions.Count - 1], world[vertex]) < 0.0001f)
                    continue;
                positions.Add(world[vertex]);
                normals.Add(worldNormals[vertex]);
                routeUv.Add(uv[vertex]);
            }
            if (Vector3.Distance(positions[positions.Count - 1], end.WorldPosition) >= 0.0001f)
            {
                positions.Add(end.WorldPosition);
                normals.Add(end.WorldNormal);
                routeUv.Add(end.AtlasUV);
            }
            if (positions.Count < 2)
            {
                error = "The selected points produced an empty surface route.";
                return false;
            }

            var cumulative = new float[positions.Count];
            for (int i = 1; i < positions.Count; i++)
            {
                float worldStep = Vector3.Distance(positions[i - 1], positions[i]);
                if (worldStep < 0.05f && Vector2.Distance(routeUv[i - 1], routeUv[i]) > 0.5f)
                {
                    error = "The shortest cut crosses an atlas seam. Choose two points on the " +
                        "same visible UV island so the cut will not streak across the atlas.";
                    return false;
                }
                cumulative[i] = cumulative[i - 1] + worldStep;
            }
            path = new PathData
            {
                positions = positions.ToArray(),
                normals = normals.ToArray(),
                uv = routeUv.ToArray(),
                cumulativeDistance = cumulative,
                length = cumulative[cumulative.Length - 1]
            };
            return true;
        }

        private static Mesh BuildCutMesh(SkinnedMeshRenderer renderer, int submesh,
            Mesh shared, Mesh baked, PathData path, float halfWidth, out string error)
        {
            error = null;
            Vector3[] localPositions = baked.vertices;
            Vector2[] uv = shared.uv;
            int[] sourceTriangles = shared.GetTriangles(submesh);
            Transform transform = renderer.transform;
            var world = new Vector3[localPositions.Length];
            for (int i = 0; i < world.Length; i++)
                world[i] = transform.TransformPoint(localPositions[i]);

            var vertices = new List<Vector3>();
            var coordinates = new List<Vector2>();
            var triangles = new List<int>();
            float selectionRadius = Mathf.Max(halfWidth * 2.5f, 0.003f);
            for (int triangle = 0; triangle + 2 < sourceTriangles.Length; triangle += 3)
            {
                int ia = sourceTriangles[triangle];
                int ib = sourceTriangles[triangle + 1];
                int ic = sourceTriangles[triangle + 2];
                EvaluatePath(path, world[ia], out float da, out _, out _);
                EvaluatePath(path, world[ib], out float db, out _, out _);
                EvaluatePath(path, world[ic], out float dc, out _, out _);
                Vector3 center = (world[ia] + world[ib] + world[ic]) / 3f;
                EvaluatePath(path, center, out float dm, out _, out _);
                float longestEdge = Mathf.Max(Vector3.Distance(world[ia], world[ib]),
                    Mathf.Max(Vector3.Distance(world[ib], world[ic]),
                        Vector3.Distance(world[ic], world[ia])));
                float vertexDistance = Mathf.Min(da, Mathf.Min(db, dc));
                if (dm > selectionRadius &&
                    vertexDistance > selectionRadius + longestEdge) continue;

                int baseVertex = vertices.Count;
                AddCutVertex(path, world[ia], uv[ia], vertices, coordinates);
                AddCutVertex(path, world[ib], uv[ib], vertices, coordinates);
                AddCutVertex(path, world[ic], uv[ic], vertices, coordinates);
                triangles.Add(baseVertex);
                triangles.Add(baseVertex + 1);
                triangles.Add(baseVertex + 2);
            }
            if (triangles.Count == 0)
            {
                error = "No atlas triangles overlap the requested metric cut width.";
                return null;
            }
            var mesh = new Mesh { name = "UMA Surface Cut Atlas Ribbon" };
            if (vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, coordinates);
            mesh.SetUVs(1, coordinates);
            mesh.SetTriangles(triangles, 0, false);
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static void AddCutVertex(PathData path, Vector3 worldPosition, Vector2 atlasUv,
            List<Vector3> vertices, List<Vector2> coordinates)
        {
            EvaluatePath(path, worldPosition, out _, out float signedDistance,
                out float alongDistance);
            vertices.Add(new Vector3(atlasUv.x * 2f - 1f, atlasUv.y * 2f - 1f, 0f));
            coordinates.Add(new Vector2(signedDistance, alongDistance));
        }

        private static void EvaluatePath(PathData path, Vector3 point, out float distance,
            out float signedDistance, out float alongDistance)
        {
            distance = float.PositiveInfinity;
            signedDistance = 0f;
            alongDistance = 0f;
            for (int segment = 0; segment + 1 < path.positions.Length; segment++)
            {
                Vector3 a = path.positions[segment];
                Vector3 b = path.positions[segment + 1];
                Vector3 delta = b - a;
                float lengthSquared = delta.sqrMagnitude;
                float t = lengthSquared > 0.00000001f
                    ? Mathf.Clamp01(Vector3.Dot(point - a, delta) / lengthSquared) : 0f;
                Vector3 nearest = a + delta * t;
                Vector3 offset = point - nearest;
                float candidate = offset.magnitude;
                if (candidate >= distance) continue;
                Vector3 normal = Vector3.Slerp(path.normals[segment],
                    path.normals[segment + 1], t).normalized;
                Vector3 tangent = delta.normalized;
                Vector3 side = Vector3.Cross(normal, tangent).normalized;
                distance = candidate;
                signedDistance = Vector3.Dot(offset, side) < 0f ? -candidate : candidate;
                alongDistance = path.cumulativeDistance[segment] + delta.magnitude * t;
            }
        }

        private static void BuildBleedSources(PathData path, float spacingMeters,
            float spacingVariation, float inset, uint seed, float speedVariation,
            float sizeVariation, out float[] distances, out Vector3[] positions,
            out Vector3[] normals, out float[] speedMultipliers,
            out float[] sizeMultipliers)
        {
            distances = CalculateBleedDistances(path.length, spacingMeters,
                spacingVariation, inset, seed);
            CalculateBleedVariations(distances.Length, speedVariation, sizeVariation, seed,
                out speedMultipliers, out sizeMultipliers);
            positions = new Vector3[distances.Length];
            normals = new Vector3[distances.Length];
            for (int i = 0; i < distances.Length; i++)
            {
                EvaluateAtDistance(path, distances[i], out positions[i], out normals[i]);
            }
        }

        internal static float[] CalculateBleedDistances(float lengthMeters,
            float spacingMeters, float spacingVariation, float endInset, uint seed)
        {
            lengthMeters = Mathf.Max(0f, lengthMeters);
            spacingMeters = Mathf.Max(0f, spacingMeters);
            if (lengthMeters <= 0f || spacingMeters <= 0f) return Array.Empty<float>();

            spacingVariation = Mathf.Clamp(spacingVariation, 0f, 0.95f);
            float insetDistance = lengthMeters * Mathf.Clamp(endInset, 0f, 0.45f);
            float rangeStart = insetDistance;
            float rangeEnd = lengthMeters - insetDistance;
            float usableLength = rangeEnd - rangeStart;
            if (usableLength <= 0.000001f) return Array.Empty<float>();
            if (usableLength <= spacingMeters)
                return new[] { (rangeStart + rangeEnd) * 0.5f };

            // Preserve whole-cut coverage when an unusually dense profile would exceed the
            // safety cap. At that point even spacing is preferable to filling only one end.
            float cappedSpacing = usableLength / MaximumBleedSources;
            if (spacingMeters < cappedSpacing)
            {
                spacingMeters = cappedSpacing;
                spacingVariation = 0f;
            }

            int estimatedCount = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(
                MaximumBleedSources, usableLength / spacingMeters)), 1,
                MaximumBleedSources);
            var result = new List<float>(estimatedCount);
            uint state = seed != 0 ? seed : 0xA341316Cu;
            float cursor = rangeStart + NextBleedSpacing(spacingMeters,
                spacingVariation, ref state) * 0.5f;
            while (cursor < rangeEnd && result.Count < MaximumBleedSources)
            {
                result.Add(cursor);
                cursor += NextBleedSpacing(spacingMeters, spacingVariation, ref state);
            }
            if (result.Count == 0) result.Add((rangeStart + rangeEnd) * 0.5f);
            return result.ToArray();
        }

        private static float NextBleedSpacing(float spacingMeters, float variation,
            ref uint state)
        {
            float random01 = NextBleedRandom01(ref state);
            return spacingMeters * Mathf.Lerp(1f - variation, 1f + variation, random01);
        }

        internal static void CalculateBleedVariations(int count, float speedVariation,
            float sizeVariation, uint seed, out float[] speedMultipliers,
            out float[] sizeMultipliers)
        {
            count = Mathf.Max(0, count);
            speedVariation = Mathf.Clamp(speedVariation, 0f, 0.95f);
            sizeVariation = Mathf.Clamp(sizeVariation, 0f, 0.95f);
            speedMultipliers = new float[count];
            sizeMultipliers = new float[count];
            uint speedState = (seed ^ 0x9E3779B9u) | 1u;
            uint sizeState = (seed ^ 0x85EBCA6Bu) | 1u;
            for (int i = 0; i < count; i++)
            {
                speedMultipliers[i] = Mathf.Lerp(1f - speedVariation,
                    1f + speedVariation, NextBleedRandom01(ref speedState));
                sizeMultipliers[i] = Mathf.Lerp(1f - sizeVariation,
                    1f + sizeVariation, NextBleedRandom01(ref sizeState));
            }
        }

        private static float NextBleedRandom01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216f;
        }

        private static uint CreateBleedSeed(PathData path, int profileSeed, uint sequence)
        {
            uint hash = 2166136261u;
            AddBleedSeedValue(ref hash, unchecked((uint)profileSeed));
            AddBleedSeedValue(ref hash, sequence);
            AddBleedSeedValue(ref hash,
                unchecked((uint)Mathf.RoundToInt(path.length * 10000f)));
            if (path.uv != null && path.uv.Length > 0)
            {
                Vector2 first = path.uv[0];
                Vector2 last = path.uv[path.uv.Length - 1];
                AddBleedSeedValue(ref hash,
                    unchecked((uint)Mathf.RoundToInt(first.x * 65535f)));
                AddBleedSeedValue(ref hash,
                    unchecked((uint)Mathf.RoundToInt(first.y * 65535f)));
                AddBleedSeedValue(ref hash,
                    unchecked((uint)Mathf.RoundToInt(last.x * 65535f)));
                AddBleedSeedValue(ref hash,
                    unchecked((uint)Mathf.RoundToInt(last.y * 65535f)));
            }
            return hash != 0 ? hash : 0xA341316Cu;
        }

        private static void AddBleedSeedValue(ref uint hash, uint value)
        {
            hash ^= value;
            hash *= 16777619u;
        }

        private static void EvaluateAtDistance(PathData path, float distance,
            out Vector3 position, out Vector3 normal)
        {
            distance = Mathf.Clamp(distance, 0f, path.length);
            for (int segment = 0; segment + 1 < path.positions.Length; segment++)
            {
                float a = path.cumulativeDistance[segment];
                float b = path.cumulativeDistance[segment + 1];
                if (distance > b && segment + 2 < path.positions.Length) continue;
                float t = b > a ? Mathf.InverseLerp(a, b, distance) : 0f;
                position = Vector3.Lerp(path.positions[segment], path.positions[segment + 1], t);
                normal = Vector3.Slerp(path.normals[segment], path.normals[segment + 1], t).normalized;
                return;
            }
            position = path.positions[path.positions.Length - 1];
            normal = path.normals[path.normals.Length - 1];
        }

        private static void AddEdge(List<int>[] adjacency, int a, int b)
        {
            if ((uint)a >= (uint)adjacency.Length || (uint)b >= (uint)adjacency.Length) return;
            adjacency[a] ??= new List<int>(6);
            adjacency[b] ??= new List<int>(6);
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 barycentric)
        {
            distance = 0f;
            barycentric = default;
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < 0.0000001f) return false;
            float inverse = 1f / determinant;
            Vector3 t = origin - a;
            float u = Vector3.Dot(t, p) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(t, ab);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(ac, q) * inverse;
            if (distance < 0f) return false;
            barycentric = new Vector3(1f - u - v, u, v);
            return true;
        }

        private static Mesh InstantiateMesh(Mesh source, string name)
        {
            Mesh result = Instantiate(source);
            result.name = name;
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
