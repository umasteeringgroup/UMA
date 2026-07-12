using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    /// <summary>
    /// Binds selected UMA clothing slots to the current body surface, then re-projects those
    /// vertices after body blendshape changes. Preview writes to a temporary clone of UMA's
    /// generated renderer mesh immediately, while VertexOverrides preserve the result for a
    /// later UMA rebuild without changing shared SlotDataAssets.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UMAClothingConformer : MonoBehaviour
    {
        public UMADynamicAvatar umaAvatar;
        public UMAData umaData;

        [Tooltip("Active UMA slot names to conform.")]
        public List<string> selectedSlotNames = new List<string>();

        [Tooltip("Optional body-surface slots. Leave empty to use every active non-clothing slot.")]
        public List<string> baseSlotNames = new List<string>();

        public ClothingConformerSettings settings = new ClothingConformerSettings();
        public ClothingBindData bindData;
        public List<ClothingBindData> bindDataAssets = new List<ClothingBindData>();

        [Tooltip("Apply conformed slot vertices to this UMA immediately and keep them synchronized in the Scene view.")]
        public bool preview = true;

        [SerializeField] private List<SlotOverrideState> originalOverrideStates = new List<SlotOverrideState>();
        [SerializeField] private List<Vector3> unboundVertexPositions = new List<Vector3>();
        [SerializeField, TextArea] private string lastStatus;

        private readonly List<ConformedSlotResult> conformedResults = new List<ConformedSlotResult>();
        private readonly List<PreviewMeshState> previewMeshStates = new List<PreviewMeshState>();
        private readonly List<RendererBlendShapeState> rendererBlendShapeStates = new List<RendererBlendShapeState>();
        private readonly Dictionary<ClothingBindData, List<int>[]> smoothingAdjacencyCache = new Dictionary<ClothingBindData, List<int>[]>();
        private UMAData subscribedUMAData;
        private int lastPreviewStateHash;
        private bool previewStateInitialized;
        private bool isConforming;

        public string LastStatus { get { return lastStatus; } }
        public IReadOnlyList<Vector3> UnboundVertexPositions { get { return unboundVertexPositions; } }
        public bool HasConformedResults { get { return conformedResults.Count > 0; } }

        private void Reset()
        {
            AutoDetectUMA();
        }

        private void OnEnable()
        {
            AutoDetectUMA();
            EnsureUMAEventSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromUMAEvents();
            RestorePreviewMeshes();
            smoothingAdjacencyCache.Clear();
        }

        private void OnValidate()
        {
            if (settings == null) settings = new ClothingConformerSettings();
            AutoDetectUMA();
        }

        private void Update()
        {
            EnsureUMAEventSubscription();
            if (!preview || !settings.livePreview || isConforming || bindDataAssets == null || bindDataAssets.Count == 0)
                return;

            if (!TryGetUMA(out UMAData data, out _)) return;
            int stateHash = CalculatePreviewStateHash(data);
            if (!previewStateInitialized)
            {
                previewStateInitialized = true;
                lastPreviewStateHash = stateHash;
                return;
            }
            if (stateHash == lastPreviewStateHash) return;

            lastPreviewStateHash = stateHash;
            ConformSelectedSlots();
        }

        /// <summary>Auto-fills UMA references when this component is placed on an avatar root.</summary>
        public void AutoDetectUMA()
        {
            if (umaAvatar == null) umaAvatar = GetComponent<UMADynamicAvatar>();
            if (umaData == null)
            {
                if (umaAvatar != null) umaData = umaAvatar.umaData;
                if (umaData == null) umaData = GetComponent<UMAData>();
            }
        }

        public List<SlotData> GetActiveSlots()
        {
            List<SlotData> slots = new List<SlotData>();
            if (!TryGetUMA(out UMAData data, out _)) return slots;

            SlotData[] recipeSlots = data.umaRecipe != null ? data.umaRecipe.GetAllSlots() : null;
            if (recipeSlots == null) return slots;
            for (int i = 0; i < recipeSlots.Length; i++)
            {
                SlotData slot = recipeSlots[i];
                if (slot != null && !slot.isDisabled && slot.asset != null &&
                    !UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                    slots.Add(slot);
            }
            return slots;
        }

        /// <summary>Creates one bind-data object for each selected slot.</summary>
        public void BindSelectedSlots()
        {
            BindSelectedSlots(null);
        }

        /// <summary>
        /// Bind with optional progress callback. Return true from the callback to cancel.
        /// This is public so an editor window can supply a cancellable progress UI without
        /// putting UnityEditor calls in the runtime path.
        /// </summary>
        public bool BindSelectedSlots(Func<float, string, bool> progressCallback)
        {
            if (!TryGetUMA(out UMAData data, out string error))
                return Fail(error);
            if (selectedSlotNames == null || selectedSlotNames.Count == 0)
                return Fail("Select at least one active clothing slot before binding.");

            List<SlotData> clothingSlots = ResolveSlots(data, selectedSlotNames);
            if (clothingSlots.Count == 0) return Fail("None of the selected clothing slots are active on this UMA.");
            List<SlotData> bodySlots = ResolveBaseSlots(data, clothingSlots);
            if (bodySlots.Count == 0) return Fail("No body surface slots were available. Select one or more base slots, or leave Base Slots empty.");

            Dictionary<int, RendererSnapshot> rendererCache = new Dictionary<int, RendererSnapshot>();
            SurfaceSnapshot baseSurface;
            if (!TryBuildSurface(data, bodySlots, rendererCache, out baseSurface, out error)) return Fail(error);
            if (baseSurface.triangles.Length == 0) return Fail("The selected body surface has no visible triangles to bind against.");

            ClothingConformerSpatialIndex spatialIndex = new ClothingConformerSpatialIndex(
                baseSurface.vertices, baseSurface.triangles, settings.maxSearchRadius);
            List<ClothingBindData> pending = new List<ClothingBindData>();
            unboundVertexPositions.Clear();
            int allVertexCount = 0;
            for (int i = 0; i < clothingSlots.Count; i++) allVertexCount += clothingSlots[i].asset.meshData.vertexCount;
            int completedVertices = 0;

            for (int slotIndex = 0; slotIndex < clothingSlots.Count; slotIndex++)
            {
                SlotData slot = clothingSlots[slotIndex];
                SlotSnapshot clothing;
                if (!TryBuildSlotSnapshot(data, slot, rendererCache, SlotSnapshotDetail.NormalAndTangent, out clothing, out error))
                    return Fail(error);

                ClothingBindData binding = ScriptableObject.CreateInstance<ClothingBindData>();
                binding.name = slot.slotName + "_Bind";
                binding.sourceSlotName = slot.slotName;
                binding.sourceSlotAsset = slot.asset;
                binding.clothingMeshOriginal = CreateSourceMesh(slot.asset);
                binding.sourceMaterial = slot.material;
                binding.originalUv = slot.asset.meshData.uv != null ? (Vector2[])slot.asset.meshData.uv.Clone() : null;
                binding.vertexCount = clothing.vertices.Length;
                binding.vertices = new BindVertexData[binding.vertexCount];
                binding.triangles = clothing.triangles;
                binding.weldedVertexGroups = settings.preserveWeldedSeams
                    ? ClothingConformerMeshUtility.BuildWeldedVertexGroups(clothing.vertices, clothing.triangles, settings.weldedSeamTolerance)
                    : null;
                binding.weldedSeamTolerance = settings.weldedSeamTolerance;
                binding.baseSlotNames = baseSurface.slotNames;
                binding.baseTopologyHash = baseSurface.topologyHash;
                binding.clothingTopologyHash = ClothingConformerMeshUtility.ComputeTopologyHash(
                    new[] { slot.slotName }, binding.vertexCount, binding.triangles);
                binding.sourceBounds = CalculateBounds(clothing.vertices);
                binding.umaVersion = Application.unityVersion;

                List<int> candidates = new List<int>();
                HashSet<int> candidateSet = new HashSet<int>();
                List<int> nearest = new List<int>(4);
                HashSet<int> nearestSet = new HashSet<int>();
                for (int vertexIndex = 0; vertexIndex < clothing.vertices.Length; vertexIndex++)
                {
                    if (progressCallback != null && vertexIndex % 32 == 0)
                    {
                        float progress = allVertexCount == 0 ? 1f : (float)completedVertices / allVertexCount;
                        if (progressCallback(progress, "Binding " + slot.slotName + " (" + vertexIndex + "/" + clothing.vertices.Length + ")"))
                            return Fail("Binding cancelled. Existing bind data was left unchanged.");
                    }

                    Vector3 position = clothing.vertices[vertexIndex];
                    BindVertexData vertex = new BindVertexData
                    {
                        localPosition = position,
                        localNormal = clothing.normals[vertexIndex],
                        localTangent = clothing.tangents[vertexIndex],
                        mappedTriangleIndex = -1,
                        barycentric = new Vector3(1f, 0f, 0f)
                    };

                    spatialIndex.QueryTriangles(position, settings.maxSearchRadius, candidates, candidateSet);
                    int bestTriangle = -1;
                    Vector3 closest = Vector3.zero;
                    float bestDistanceSq = float.PositiveInfinity;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        int triangleIndex = candidates[candidateIndex];
                        int triangleOffset = triangleIndex * 3;
                        if (triangleOffset + 2 >= baseSurface.triangles.Length) continue;
                        int a = baseSurface.triangles[triangleOffset];
                        int b = baseSurface.triangles[triangleOffset + 1];
                        int c = baseSurface.triangles[triangleOffset + 2];
                        Vector3 point = ClothingConformerMeshUtility.ClosestPointOnTriangle(
                            position, baseSurface.vertices[a], baseSurface.vertices[b], baseSurface.vertices[c]);
                        float distanceSq = (position - point).sqrMagnitude;
                        if (distanceSq >= bestDistanceSq) continue;
                        bestDistanceSq = distanceSq;
                        bestTriangle = triangleIndex;
                        closest = point;
                    }

                    if (bestTriangle >= 0 && bestDistanceSq <= settings.maxTriangleDistance * settings.maxTriangleDistance)
                    {
                        int triangleOffset = bestTriangle * 3;
                        int a = baseSurface.triangles[triangleOffset];
                        int b = baseSurface.triangles[triangleOffset + 1];
                        int c = baseSurface.triangles[triangleOffset + 2];
                        Vector3 barycentric = ClothingConformerMeshUtility.CalculateBarycentric(
                            closest, baseSurface.vertices[a], baseSurface.vertices[b], baseSurface.vertices[c]);
                        Vector3 normal = InterpolateNormal(baseSurface.normals, a, b, c, barycentric);
                        vertex.mappedTriangleIndex = bestTriangle;
                        vertex.barycentric = barycentric;
                        vertex.mappedNormal = normal;
                        vertex.signedDistance = Vector3.Dot(position - closest, normal);
                    }

                    spatialIndex.FindNearestVertices(position, 4, settings.maxSearchRadius, nearest, nearestSet);
                    if (nearest.Count > 0)
                    {
                        vertex.nearestBaseVertexIndices = nearest.ToArray();
                        vertex.nearestBaseVertexWeights = CalculateInverseDistanceWeights(baseSurface.vertices, nearest, position);
                        if (vertex.mappedTriangleIndex < 0)
                        {
                            Vector3 fallbackPosition;
                            Vector3 fallbackNormal;
                            GetWeightedSurfacePoint(baseSurface, vertex.nearestBaseVertexIndices,
                                vertex.nearestBaseVertexWeights, out fallbackPosition, out fallbackNormal);
                            if ((fallbackPosition - position).sqrMagnitude <= settings.maxTriangleDistance * settings.maxTriangleDistance)
                            {
                                vertex.mappedNormal = fallbackNormal;
                                vertex.signedDistance = Vector3.Dot(position - fallbackPosition, fallbackNormal);
                            }
                            else
                            {
                                vertex.nearestBaseVertexIndices = null;
                                vertex.nearestBaseVertexWeights = null;
                                unboundVertexPositions.Add(position);
                            }
                        }
                    }
                    else if (vertex.mappedTriangleIndex < 0)
                    {
                        unboundVertexPositions.Add(position);
                    }

                    binding.vertices[vertexIndex] = vertex;
                    completedVertices++;
                }
                binding.isComplete = true;
                pending.Add(binding);
            }

            if (progressCallback != null && progressCallback(1f, "Finalizing UMA clothing bindings"))
                return Fail("Binding cancelled. Existing bind data was left unchanged.");

            DestroyTransientBindData();
            bindDataAssets = pending;
            bindData = bindDataAssets.Count > 0 ? bindDataAssets[0] : null;
            previewStateInitialized = false;
            lastStatus = "Bound " + bindDataAssets.Count + " slot(s). " + unboundVertexPositions.Count + " vertices use no mapping.";
            Debug.Log("[UMAClothingConformer] " + lastStatus, this);
            return true;
        }

        /// <summary>Re-evaluates all stored mappings against the current baked UMA body surface.</summary>
        public void ConformSelectedSlots()
        {
            ConformSelectedSlots(null);
        }

        public bool ConformSelectedSlots(Func<float, string, bool> progressCallback)
        {
            if (isConforming) return false;
            isConforming = true;
            try
            {
                if (!TryGetUMA(out UMAData data, out string error)) return Fail(error);
                if (bindDataAssets == null || bindDataAssets.Count == 0) return Fail("Bind one or more clothing slots before conforming.");

                conformedResults.Clear();
                Dictionary<string, SurfaceSnapshot> surfaces = new Dictionary<string, SurfaceSnapshot>(bindDataAssets.Count);
                Dictionary<int, RendererSnapshot> rendererCache = new Dictionary<int, RendererSnapshot>();
                int total = bindDataAssets.Count;
                for (int bindIndex = 0; bindIndex < bindDataAssets.Count; bindIndex++)
                {
                    ClothingBindData binding = bindDataAssets[bindIndex];
                    if (binding == null || !binding.isComplete) continue;
                    if (progressCallback != null && progressCallback((float)bindIndex / total, "Conforming " + binding.sourceSlotName))
                        return Fail("Conform cancelled. No new preview was applied.");

                    SlotData slot = FindSlot(data, binding.sourceSlotName);
                    if (slot == null || slot.asset == null) 
                    {
                        Debug.LogWarning("[UMAClothingConformer] Slot '" + binding.sourceSlotName + "' is no longer active. Skipping it.", this);
                        continue;
                    }
                    if (binding.vertices == null || binding.vertexCount != binding.vertices.Length ||
                        slot.asset.meshData.vertexCount != binding.vertexCount)
                    {
                        Debug.LogWarning("[UMAClothingConformer] Slot '" + binding.sourceSlotName + "' no longer has the bound vertex count. Rebind it.", this);
                        continue;
                    }

                    string surfaceKey = JoinNames(binding.baseSlotNames);
                    SurfaceSnapshot surface;
                    if (!surfaces.TryGetValue(surfaceKey, out surface))
                    {
                        List<SlotData> bodySlots = ResolveSlots(data, binding.baseSlotNames);
                        if (!TryBuildSurface(data, bodySlots, rendererCache, out surface, out error))
                        {
                            Debug.LogWarning("[UMAClothingConformer] " + error, this);
                            continue;
                        }
                        surfaces.Add(surfaceKey, surface);
                    }
                    if (surface.topologyHash != binding.baseTopologyHash)
                    {
                        Debug.LogWarning("[UMAClothingConformer] The base topology for '" + binding.sourceSlotName +
                                         "' changed since binding. Rebind this slot before conforming.", this);
                        continue;
                    }

                    SlotSnapshot clothing;
                    if (!TryBuildSlotSnapshot(data, slot, rendererCache, SlotSnapshotDetail.Normals, out clothing, out error))
                    {
                        Debug.LogWarning("[UMAClothingConformer] " + error, this);
                        continue;
                    }
                    if (ClothingConformerMeshUtility.ComputeTopologyHash(new[] { slot.slotName }, clothing.vertices.Length, clothing.triangles) !=
                        binding.clothingTopologyHash)
                    {
                        Debug.LogWarning("[UMAClothingConformer] Clothing topology changed for '" + slot.slotName + "'. Rebind it.", this);
                        continue;
                    }

                    Vector3[] conformedRootVertices = new Vector3[binding.vertexCount];
                    Vector3[] surfacePoints = new Vector3[binding.vertexCount];
                    Vector3[] surfaceNormals = new Vector3[binding.vertexCount];
                    int[] weldedVertexGroups = GetOrCreateWeldedVertexGroups(binding);
                    bool[] moved = new bool[binding.vertexCount];
                    for (int vertexIndex = 0; vertexIndex < binding.vertexCount; vertexIndex++)
                    {
                        BindVertexData map = binding.vertices[vertexIndex];
                        Vector3 surfacePoint;
                        Vector3 surfaceNormal;
                        if (!TryEvaluateMapping(surface, map, out surfacePoint, out surfaceNormal))
                        {
                            conformedRootVertices[vertexIndex] = clothing.vertices[vertexIndex];
                            surfacePoints[vertexIndex] = clothing.vertices[vertexIndex];
                            surfaceNormals[vertexIndex] = clothing.normals[vertexIndex];
                            continue;
                        }

                        float clothingSide = ClothingConformerMeshUtility.GetMappedClothingSide(
                            map.signedDistance, map.localNormal, map.mappedNormal);
                        Vector3 target = surfacePoint + surfaceNormal * map.signedDistance +
                                         surfaceNormal * (settings.additionalNormalOffset * clothingSide);
                        conformedRootVertices[vertexIndex] = target;
                        surfacePoints[vertexIndex] = surfacePoint;
                        surfaceNormals[vertexIndex] = surfaceNormal;
                        moved[vertexIndex] = (target - clothing.vertices[vertexIndex]).sqrMagnitude > 0.00000001f;
                    }

                    ApplyWeldedSeamDisplacements(conformedRootVertices, binding.vertices, weldedVertexGroups);
                    if (settings.enableCollisionCorrection)
                        ApplyCollisionCorrection(conformedRootVertices, surfacePoints, surfaceNormals, binding.vertices);
                    ApplyWeldedSeamDisplacements(conformedRootVertices, binding.vertices, weldedVertexGroups);
                    if (settings.enableSmoothing)
                    {
                        bool[] smoothingMask = settings.smoothOnlyMovedVertices ? moved : null;
                        ClothingConformerMeshUtility.Smooth(conformedRootVertices,
                            GetOrCreateSmoothingAdjacency(binding, clothing.triangles), settings, smoothingMask);
                        if (settings.enableCollisionCorrection)
                            ApplyCollisionCorrection(conformedRootVertices, surfacePoints, surfaceNormals, binding.vertices);
                    }
                    ApplyWeldedSeamDisplacements(conformedRootVertices, binding.vertices, weldedVertexGroups);

                    Vector3[] rootNormals = ClothingConformerMeshUtility.CalculateNormals(conformedRootVertices, clothing.triangles);
                    Vector2[] uv = slot.asset.meshData.uv;
                    Vector4[] rootTangents = ClothingConformerMeshUtility.CalculateTangents(conformedRootVertices, rootNormals, uv, clothing.triangles);
                    ConformedSlotResult result = BuildConformedResult(data, slot, clothing, conformedRootVertices, rootNormals, rootTangents);
                    conformedResults.Add(result);
                }

                if (preview && conformedResults.Count > 0)
                {
#if UNITY_EDITOR
                    Undo.RecordObject(data, "Preview UMA clothing conformer");
#endif
                    CaptureRendererBlendShapeWeights(data);
                    CaptureOriginalOverrides(data, conformedResults);
                    for (int i = 0; i < conformedResults.Count; i++)
                        data.AddVertexOverride(conformedResults[i].slot.asset, conformedResults[i].baseVertices);
                    ApplyDirectPreview(data, conformedResults);
                    previewStateInitialized = true;
                    lastPreviewStateHash = CalculatePreviewStateHash(data);
                }

                if (progressCallback != null) progressCallback(1f, "Conform complete");
                lastStatus = "Conformed " + conformedResults.Count + " slot(s).";
                Debug.Log("[UMAClothingConformer] " + lastStatus, this);
                return conformedResults.Count > 0;
            }
            finally
            {
                isConforming = false;
            }
        }

        /// <summary>Restores the exact UMA slot overrides that existed before preview began.</summary>
        public void RevertChanges()
        {
            if (!TryGetUMA(out UMAData data, out string error))
            {
                Fail(error);
                return;
            }

            RestoreOriginalOverrides(data, true);
        }

        private void RestoreOriginalOverrides(UMAData data, bool discardConformResult)
        {

#if UNITY_EDITOR
            Undo.RecordObject(data, "Revert UMA clothing conformer preview");
#endif
            for (int i = 0; i < originalOverrideStates.Count; i++)
            {
                SlotOverrideState state = originalOverrideStates[i];
                if (state.slotAsset == null) continue;
                if (state.hadOverride && state.vertices != null)
                    data.AddVertexOverride(state.slotAsset, (Vector3[])state.vertices.Clone());
                else
                    data.RemoveVertexOverride(state.slotAsset);
            }
            originalOverrideStates.Clear();
            RestorePreviewMeshes();
            if (discardConformResult) conformedResults.Clear();
            previewStateInitialized = false;
            lastStatus = discardConformResult ? "Reverted clothing conformer preview changes." : "Preview disabled; conform result is still available to save.";
            Debug.Log("[UMAClothingConformer] " + lastStatus, this);
        }

        /// <summary>Turns scene preview on or off without discarding the latest conform result.</summary>
        public void SetPreview(bool enabled)
        {
            if (preview == enabled) return;
            preview = enabled;
            if (!preview && TryGetUMA(out UMAData data, out _)) RestoreOriginalOverrides(data, false);
            else if (conformedResults.Count > 0) ConformSelectedSlots();
        }

        private bool TryGetUMA(out UMAData data, out string error)
        {
            AutoDetectUMA();
            data = umaData;
            if (data == null)
            {
                error = "UMAClothingConformer needs a UMADynamicAvatar or UMAData on the same GameObject.";
                return false;
            }
            if (data.RendererCount == 0 || data.GetRenderers() == null)
            {
                error = "The UMA is not fully constructed yet. Build the avatar before binding or conforming clothing.";
                return false;
            }
            EnsureUMAEventSubscription();
            error = null;
            return true;
        }

        private void EnsureUMAEventSubscription()
        {
            AutoDetectUMA();
            if (subscribedUMAData == umaData) return;
            UnsubscribeFromUMAEvents();
            if (umaData == null) return;
            subscribedUMAData = umaData;
            subscribedUMAData.OnCharacterUpdated += OnUMACharacterUpdated;
        }

        private void UnsubscribeFromUMAEvents()
        {
            if (subscribedUMAData != null)
                subscribedUMAData.OnCharacterUpdated -= OnUMACharacterUpdated;
            subscribedUMAData = null;
        }

        private void OnUMACharacterUpdated(UMAData data)
        {
            if (data == null || data != subscribedUMAData) return;

            // UMA destroys and replaces generated meshes while rebuilding. Do not try to
            // restore a previous temporary clone at this point; it no longer belongs to UMA.
            ClearPreviewMeshStates();
            RestoreRendererBlendShapeWeights(data);
            previewStateInitialized = false;

            if (preview && bindDataAssets != null && bindDataAssets.Count > 0 && !isConforming)
                ConformSelectedSlots();
        }

        private void CaptureRendererBlendShapeWeights(UMAData data)
        {
            rendererBlendShapeStates.Clear();
            for (int rendererIndex = 0; rendererIndex < data.RendererCount; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = data.GetRenderer(rendererIndex);
                if (renderer == null || renderer.sharedMesh == null) continue;
                Mesh mesh = renderer.sharedMesh;
                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    rendererBlendShapeStates.Add(new RendererBlendShapeState
                    {
                        rendererIndex = rendererIndex,
                        shapeName = mesh.GetBlendShapeName(shapeIndex),
                        weight = renderer.GetBlendShapeWeight(shapeIndex)
                    });
                }
            }
        }

        private void RestoreRendererBlendShapeWeights(UMAData data)
        {
            for (int i = 0; i < rendererBlendShapeStates.Count; i++)
            {
                RendererBlendShapeState state = rendererBlendShapeStates[i];
                SkinnedMeshRenderer renderer = data.GetRenderer(state.rendererIndex);
                if (renderer == null || renderer.sharedMesh == null) continue;
                int shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(state.shapeName);
                if (shapeIndex >= 0) renderer.SetBlendShapeWeight(shapeIndex, state.weight);
            }
        }

        private void ApplyDirectPreview(UMAData data, List<ConformedSlotResult> results)
        {
            Dictionary<SkinnedMeshRenderer, PreviewMeshBuffers> buffersByRenderer =
                new Dictionary<SkinnedMeshRenderer, PreviewMeshBuffers>();
            for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
            {
                ConformedSlotResult result = results[resultIndex];
                SlotData slot = result.slot;
                if (slot == null) continue;
                SkinnedMeshRenderer renderer = data.GetRenderer(slot.skinnedMeshRenderer);
                if (renderer == null || renderer.sharedMesh == null) continue;
                PreviewMeshBuffers buffers;
                if (!buffersByRenderer.TryGetValue(renderer, out buffers))
                {
                    Mesh mesh = GetOrCreatePreviewMesh(renderer);
                    if (mesh == null) continue;
                    buffers = new PreviewMeshBuffers
                    {
                        mesh = mesh,
                        vertices = mesh.vertices,
                        normals = mesh.normals,
                        tangents = mesh.tangents
                    };
                    buffersByRenderer.Add(renderer, buffers);
                }

                int vertexOffset = slot.vertexOffset;
                if (vertexOffset < 0 || vertexOffset + result.baseVertices.Length > buffers.mesh.vertexCount)
                {
                    Debug.LogWarning("[UMAClothingConformer] Preview could not update '" + slot.slotName +
                                     "' because its renderer vertex range changed. Rebind the slot.", this);
                    continue;
                }

                for (int vertexIndex = 0; vertexIndex < result.baseVertices.Length; vertexIndex++)
                {
                    int meshVertex = vertexOffset + vertexIndex;
                    buffers.vertices[meshVertex] = result.baseVertices[vertexIndex];
                    if (buffers.normals != null && buffers.normals.Length == buffers.mesh.vertexCount &&
                        vertexIndex < result.localNormals.Length)
                        buffers.normals[meshVertex] = result.localNormals[vertexIndex];
                    if (buffers.tangents != null && buffers.tangents.Length == buffers.mesh.vertexCount &&
                        vertexIndex < result.localTangents.Length)
                        buffers.tangents[meshVertex] = result.localTangents[vertexIndex];
                }
            }

            foreach (PreviewMeshBuffers buffers in buffersByRenderer.Values)
            {
                buffers.mesh.vertices = buffers.vertices;
                if (buffers.normals != null && buffers.normals.Length == buffers.mesh.vertexCount) buffers.mesh.normals = buffers.normals;
                if (buffers.tangents != null && buffers.tangents.Length == buffers.mesh.vertexCount) buffers.mesh.tangents = buffers.tangents;
                buffers.mesh.RecalculateBounds();
            }
        }

        private Mesh GetOrCreatePreviewMesh(SkinnedMeshRenderer renderer)
        {
            for (int i = 0; i < previewMeshStates.Count; i++)
            {
                PreviewMeshState state = previewMeshStates[i];
                if (state.renderer != renderer) continue;
                if (renderer.sharedMesh == state.previewMesh) return state.previewMesh;

                // An external UMA rebuild replaced this renderer mesh before its event was observed.
                DestroyUnityObject(state.previewMesh);
                previewMeshStates.RemoveAt(i);
                break;
            }

            Mesh original = renderer.sharedMesh;
            if (original == null) return null;
            Mesh previewMesh = Instantiate(original);
            previewMesh.name = original.name + " (UMA Clothing Conformer Preview)";
            previewMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            renderer.sharedMesh = previewMesh;
            previewMeshStates.Add(new PreviewMeshState
            {
                renderer = renderer,
                originalMesh = original,
                previewMesh = previewMesh
            });
            return previewMesh;
        }

        private void RestorePreviewMeshes()
        {
            for (int i = previewMeshStates.Count - 1; i >= 0; i--)
            {
                PreviewMeshState state = previewMeshStates[i];
                if (state.renderer != null && state.renderer.sharedMesh == state.previewMesh)
                    state.renderer.sharedMesh = state.originalMesh;
                DestroyUnityObject(state.previewMesh);
            }
            previewMeshStates.Clear();
        }

        private void ClearPreviewMeshStates()
        {
            for (int i = 0; i < previewMeshStates.Count; i++)
            {
                PreviewMeshState state = previewMeshStates[i];
                if (state.previewMesh != null && (state.renderer == null || state.renderer.sharedMesh != state.previewMesh))
                    DestroyUnityObject(state.previewMesh);
            }
            previewMeshStates.Clear();
        }

        private List<SlotData> ResolveBaseSlots(UMAData data, List<SlotData> clothingSlots)
        {
            if (baseSlotNames != null && baseSlotNames.Count > 0) return ResolveSlots(data, baseSlotNames);
            List<SlotData> all = GetActiveSlots();
            List<SlotData> result = new List<SlotData>();
            if (!settings.useUnselectedSlotsAsBase) return result;
            for (int i = 0; i < all.Count; i++)
            {
                if (!clothingSlots.Contains(all[i])) result.Add(all[i]);
            }
            return result;
        }

        private static List<SlotData> ResolveSlots(UMAData data, IList<string> names)
        {
            List<SlotData> result = new List<SlotData>();
            if (data == null || names == null) return result;
            for (int i = 0; i < names.Count; i++)
            {
                SlotData slot = FindSlot(data, names[i]);
                if (slot != null && !result.Contains(slot)) result.Add(slot);
            }
            return result;
        }

        private static SlotData FindSlot(UMAData data, string name)
        {
            if (data == null || data.umaRecipe == null || string.IsNullOrEmpty(name)) return null;
            SlotData[] slots = data.umaRecipe.GetAllSlots();
            if (slots == null) return null;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot != null && slot.slotName == name) return slot;
            }
            return null;
        }

        private bool TryBuildSurface(UMAData data, List<SlotData> slots, Dictionary<int, RendererSnapshot> rendererCache,
            out SurfaceSnapshot surface, out string error)
        {
            surface = new SurfaceSnapshot();
            error = null;
            if (slots == null || slots.Count == 0)
            {
                error = "No active base slots could be resolved.";
                return false;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<string> names = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                SlotSnapshot slot;
                if (!TryBuildSlotSnapshot(data, slots[i], rendererCache, SlotSnapshotDetail.PositionsOnly, out slot, out error))
                    return false;
                int offset = vertices.Count;
                vertices.AddRange(slot.vertices);
                for (int triangle = 0; triangle < slot.triangles.Length; triangle++) triangles.Add(offset + slot.triangles[triangle]);
                names.Add(slots[i].slotName);
            }
            surface.vertices = vertices.ToArray();
            surface.triangles = triangles.ToArray();
            surface.normals = ClothingConformerMeshUtility.CalculateNormals(surface.vertices, surface.triangles);
            surface.slotNames = names.ToArray();
            surface.topologyHash = ClothingConformerMeshUtility.ComputeTopologyHash(surface.slotNames, surface.vertices.Length, surface.triangles);
            return true;
        }

        private bool TryBuildSlotSnapshot(UMAData data, SlotData slot, Dictionary<int, RendererSnapshot> rendererCache,
            SlotSnapshotDetail detail, out SlotSnapshot snapshot, out string error)
        {
            snapshot = new SlotSnapshot();
            error = null;
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                error = "A selected slot has no usable SlotDataAsset mesh.";
                return false;
            }
            int rendererIndex = slot.skinnedMeshRenderer;
            SkinnedMeshRenderer renderer = data.GetRenderer(rendererIndex);
            if (renderer == null || renderer.sharedMesh == null)
            {
                error = "Slot '" + slot.slotName + "' does not have a generated SkinnedMeshRenderer. Rebuild the UMA first.";
                return false;
            }
            RendererSnapshot rendererSnapshot;
            if (!rendererCache.TryGetValue(rendererIndex, out rendererSnapshot))
            {
                if (!TryBuildRendererSnapshot(renderer, out rendererSnapshot, out error)) return false;
                rendererCache.Add(rendererIndex, rendererSnapshot);
            }

            int start = slot.vertexOffset;
            int count = slot.asset.meshData.vertexCount;
            if (start < 0 || count <= 0 || start + count > rendererSnapshot.rootVertices.Length)
            {
                error = "Slot '" + slot.slotName + "' has an invalid generated vertex range. Rebuild the UMA before using the conformer.";
                return false;
            }
            snapshot.slot = slot;
            snapshot.renderer = renderer;
            snapshot.rendererSnapshot = rendererSnapshot;
            snapshot.startVertex = start;
            snapshot.vertices = CopyRange(rendererSnapshot.rootVertices, start, count);
            snapshot.triangles = ExtractSlotTriangles(rendererSnapshot.submeshTriangles, start, count, slot.asset.meshData);
            if (detail >= SlotSnapshotDetail.Normals)
                snapshot.normals = ClothingConformerMeshUtility.CalculateNormals(snapshot.vertices, snapshot.triangles);
            if (detail >= SlotSnapshotDetail.NormalAndTangent)
            {
                Vector2[] uv = slot.asset.meshData.uv;
                snapshot.tangents = ClothingConformerMeshUtility.CalculateTangents(snapshot.vertices, snapshot.normals, uv, snapshot.triangles);
            }
            return true;
        }

        private bool TryBuildRendererSnapshot(SkinnedMeshRenderer renderer, out RendererSnapshot snapshot, out string error)
        {
            snapshot = new RendererSnapshot();
            error = null;
            Mesh source = renderer.sharedMesh;
            if (source == null)
            {
                error = "A generated SkinnedMeshRenderer has no mesh.";
                return false;
            }
            Mesh baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                Vector3[] bakedVertices = baked.vertices;
                if (bakedVertices == null || bakedVertices.Length != source.vertexCount)
                {
                    error = "Unity could not bake a complete UMA renderer mesh.";
                    return false;
                }
                snapshot.renderer = renderer;
                snapshot.sourceMesh = source;
                snapshot.rootVertices = new Vector3[bakedVertices.Length];
                snapshot.blendedVertexDeltas = EvaluateBlendShapeDeltas(source, renderer);
                Matrix4x4[] skinMatrices = BuildSkinMatrices(renderer, source);
                Matrix4x4 rendererToRoot = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < bakedVertices.Length; i++)
                {
                    Vector3 unskinned = UnskinVertex(bakedVertices[i], skinMatrices, i);
                    snapshot.rootVertices[i] = rendererToRoot.MultiplyPoint3x4(unskinned);
                }
                snapshot.submeshTriangles = new int[baked.subMeshCount][];
                for (int i = 0; i < baked.subMeshCount; i++) snapshot.submeshTriangles[i] = baked.GetTriangles(i);
                return true;
            }
            catch (Exception exception)
            {
                error = "Unable to bake UMA renderer '" + renderer.name + "': " + exception.Message;
                return false;
            }
            finally
            {
                DestroyUnityObject(baked);
            }
        }

        private ConformedSlotResult BuildConformedResult(UMAData data, SlotData slot, SlotSnapshot snapshot,
            Vector3[] rootVertices, Vector3[] rootNormals, Vector4[] rootTangents)
        {
            int count = rootVertices.Length;
            Vector3[] baseVertices = new Vector3[count];
            Vector3[] localNormals = new Vector3[count];
            Vector4[] localTangents = new Vector4[count];
            Matrix4x4 rootToRenderer = snapshot.renderer.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            for (int i = 0; i < count; i++)
            {
                Vector3 rendererLocal = rootToRenderer.MultiplyPoint3x4(rootVertices[i]);
                Vector3 blendDelta = snapshot.rendererSnapshot.blendedVertexDeltas != null &&
                                     snapshot.startVertex + i < snapshot.rendererSnapshot.blendedVertexDeltas.Length
                    ? snapshot.rendererSnapshot.blendedVertexDeltas[snapshot.startVertex + i]
                    : Vector3.zero;
                // VertexOverrides are evaluated before Unity applies active blendshape frames.
                baseVertices[i] = rendererLocal - blendDelta;
                localNormals[i] = rootToRenderer.MultiplyVector(rootNormals[i]).normalized;
                Vector3 tangent = rootToRenderer.MultiplyVector(new Vector3(rootTangents[i].x, rootTangents[i].y, rootTangents[i].z)).normalized;
                localTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, rootTangents[i].w);
            }
            return new ConformedSlotResult
            {
                slot = slot,
                rootVertices = rootVertices,
                rootNormals = rootNormals,
                rootTangents = rootTangents,
                baseVertices = baseVertices,
                localNormals = localNormals,
                localTangents = localTangents,
                triangles = snapshot.triangles
            };
        }

        private bool TryEvaluateMapping(SurfaceSnapshot surface, BindVertexData map, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = map.mappedNormal.sqrMagnitude > 0.00000001f ? map.mappedNormal.normalized : Vector3.up;
            int triangleOffset = map.mappedTriangleIndex * 3;
            if (map.mappedTriangleIndex >= 0 && triangleOffset + 2 < surface.triangles.Length)
            {
                int a = surface.triangles[triangleOffset];
                int b = surface.triangles[triangleOffset + 1];
                int c = surface.triangles[triangleOffset + 2];
                if (a >= 0 && b >= 0 && c >= 0 && a < surface.vertices.Length && b < surface.vertices.Length && c < surface.vertices.Length)
                {
                    point = surface.vertices[a] * map.barycentric.x + surface.vertices[b] * map.barycentric.y + surface.vertices[c] * map.barycentric.z;
                    normal = InterpolateNormal(surface.normals, a, b, c, map.barycentric);
                    normal = ClothingConformerMeshUtility.OrientNormalToReference(normal, map.mappedNormal);
                    return true;
                }
            }
            if (map.HasNearestVertexFallback)
            {
                GetWeightedSurfacePoint(surface, map.nearestBaseVertexIndices, map.nearestBaseVertexWeights, out point, out normal);
                normal = ClothingConformerMeshUtility.OrientNormalToReference(normal, map.mappedNormal);
                return true;
            }
            return false;
        }

        private List<int>[] GetOrCreateSmoothingAdjacency(ClothingBindData binding, int[] triangles)
        {
            List<int>[] adjacency;
            if (smoothingAdjacencyCache.TryGetValue(binding, out adjacency) && adjacency != null &&
                adjacency.Length == binding.vertexCount)
                return adjacency;

            adjacency = ClothingConformerMeshUtility.BuildAdjacency(binding.vertexCount, triangles);
            smoothingAdjacencyCache[binding] = adjacency;
            return adjacency;
        }

        private int[] GetOrCreateWeldedVertexGroups(ClothingBindData binding)
        {
            if (!settings.preserveWeldedSeams || binding == null || binding.vertices == null) return null;
            if (binding.weldedVertexGroups != null && binding.weldedVertexGroups.Length == binding.vertexCount &&
                Mathf.Approximately(binding.weldedSeamTolerance, settings.weldedSeamTolerance))
                return binding.weldedVertexGroups;

            Vector3[] originalPositions = new Vector3[binding.vertexCount];
            for (int i = 0; i < originalPositions.Length; i++) originalPositions[i] = binding.vertices[i].localPosition;
            binding.weldedVertexGroups = ClothingConformerMeshUtility.BuildWeldedVertexGroups(
                originalPositions, binding.triangles, settings.weldedSeamTolerance);
            binding.weldedSeamTolerance = settings.weldedSeamTolerance;
#if UNITY_EDITOR
            EditorUtility.SetDirty(binding);
#endif
            return binding.weldedVertexGroups;
        }

        private static void ApplyWeldedSeamDisplacements(Vector3[] vertices, BindVertexData[] bindVertices, int[] weldedGroups)
        {
            if (vertices == null || bindVertices == null || weldedGroups == null ||
                vertices.Length != bindVertices.Length || weldedGroups.Length != vertices.Length)
                return;

            Dictionary<int, WeldedDisplacement> displacements = new Dictionary<int, WeldedDisplacement>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int group = weldedGroups[i];
                if (group < 0) continue;
                WeldedDisplacement displacement;
                if (!displacements.TryGetValue(group, out displacement)) displacement = new WeldedDisplacement();
                displacement.total += vertices[i] - bindVertices[i].localPosition;
                displacement.count++;
                displacements[group] = displacement;
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                int group = weldedGroups[i];
                WeldedDisplacement displacement;
                if (group < 0 || !displacements.TryGetValue(group, out displacement) || displacement.count == 0) continue;
                vertices[i] = bindVertices[i].localPosition + displacement.total / displacement.count;
            }
        }

        private void ApplyCollisionCorrection(Vector3[] vertices, Vector3[] surfacePoints, Vector3[] surfaceNormals,
            BindVertexData[] bindVertices)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = surfaceNormals[i].sqrMagnitude > 0.00000001f ? surfaceNormals[i].normalized : Vector3.up;
                BindVertexData bind = bindVertices != null && i < bindVertices.Length ? bindVertices[i] : default(BindVertexData);
                float clothingSide = ClothingConformerMeshUtility.GetMappedClothingSide(
                    bind.signedDistance, bind.localNormal, bind.mappedNormal);
                Vector3 outwardNormal = normal * clothingSide;
                float sideDistance = Vector3.Dot(vertices[i] - surfacePoints[i], outwardNormal);
                if (sideDistance >= settings.normalOffsetEpsilon) continue;
                float desiredPush = settings.normalOffsetEpsilon - sideDistance + settings.collisionPushDistance;
                float push = Mathf.Min(desiredPush, settings.maxCollisionDisplacement);
                vertices[i] += outwardNormal * push;
            }
        }

        private void CaptureOriginalOverrides(UMAData data, List<ConformedSlotResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                SlotDataAsset asset = results[i].slot.asset;
                if (asset == null || FindOverrideState(asset) >= 0) continue;
                Vector3[] previous;
                bool hadOverride = data.VertexOverrides.TryGetValue(asset.slotName, out previous);
                originalOverrideStates.Add(new SlotOverrideState
                {
                    slotAsset = asset,
                    hadOverride = hadOverride,
                    vertices = hadOverride && previous != null ? (Vector3[])previous.Clone() : null
                });
            }
        }

        private int FindOverrideState(SlotDataAsset asset)
        {
            for (int i = 0; i < originalOverrideStates.Count; i++)
                if (originalOverrideStates[i].slotAsset == asset) return i;
            return -1;
        }

        private int CalculatePreviewStateHash(UMAData data)
        {
            unchecked
            {
                int hash = 17;
                List<SlotData> slots = ResolveBaseSlots(data, ResolveSlots(data, selectedSlotNames));
                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    SlotData slot = slots[slotIndex];
                    SkinnedMeshRenderer renderer = data.GetRenderer(slot.skinnedMeshRenderer);
                    if (renderer == null || renderer.sharedMesh == null) continue;
                    for (int shape = 0; shape < renderer.sharedMesh.blendShapeCount; shape++)
                        hash = hash * 31 + renderer.GetBlendShapeWeight(shape).GetHashCode();
                    Transform[] bones = renderer.bones;
                    for (int bone = 0; bones != null && bone < bones.Length; bone++)
                    {
                        if (bones[bone] == null) continue;
                        hash = hash * 31 + bones[bone].localToWorldMatrix.GetHashCode();
                    }
                }
                return hash;
            }
        }

        private static int[] ExtractSlotTriangles(int[][] rendererTriangles, int start, int count, UMAMeshData fallback)
        {
            List<int> output = new List<int>();
            int end = start + count;
            if (rendererTriangles != null)
            {
                for (int submesh = 0; submesh < rendererTriangles.Length; submesh++)
                {
                    int[] triangles = rendererTriangles[submesh];
                    if (triangles == null) continue;
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int a = triangles[i];
                        int b = triangles[i + 1];
                        int c = triangles[i + 2];
                        if (a >= start && b >= start && c >= start && a < end && b < end && c < end)
                        {
                            output.Add(a - start);
                            output.Add(b - start);
                            output.Add(c - start);
                        }
                    }
                }
            }
            if (output.Count > 0 || fallback == null || fallback.submeshes == null) return output.ToArray();
            for (int i = 0; i < fallback.submeshes.Length; i++)
            {
                if (fallback.submeshes[i] == null) continue;
                int[] triangles = fallback.submeshes[i].GetBaseTriangles();
                if (triangles != null) output.AddRange(triangles);
            }
            return output.ToArray();
        }

        private static Vector3[] EvaluateBlendShapeDeltas(Mesh mesh, SkinnedMeshRenderer renderer)
        {
            Vector3[] result = new Vector3[mesh.vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (Mathf.Approximately(weight, 0f)) continue;
                int frameCount = mesh.GetBlendShapeFrameCount(shape);
                if (frameCount == 0) continue;
                int lowerFrame = 0;
                int upperFrame = 0;
                for (int frame = 0; frame < frameCount; frame++)
                {
                    if (mesh.GetBlendShapeFrameWeight(shape, frame) <= weight) lowerFrame = frame;
                    if (mesh.GetBlendShapeFrameWeight(shape, frame) >= weight)
                    {
                        upperFrame = frame;
                        break;
                    }
                    upperFrame = frame;
                }
                Vector3[] lower = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(shape, lowerFrame, lower, null, null);
                float lowerWeight = mesh.GetBlendShapeFrameWeight(shape, lowerFrame);
                if (lowerFrame == upperFrame)
                {
                    float scale = Mathf.Abs(lowerWeight) < 0.00001f ? 0f : weight / lowerWeight;
                    for (int i = 0; i < result.Length; i++) result[i] += lower[i] * scale;
                    continue;
                }
                Vector3[] upper = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(shape, upperFrame, upper, null, null);
                float upperWeight = mesh.GetBlendShapeFrameWeight(shape, upperFrame);
                float t = Mathf.InverseLerp(lowerWeight, upperWeight, weight);
                for (int i = 0; i < result.Length; i++) result[i] += Vector3.LerpUnclamped(lower[i], upper[i], t);
            }
            return result;
        }

        private static Matrix4x4[] BuildSkinMatrices(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            Matrix4x4[] matrices = new Matrix4x4[mesh.vertexCount];
            Matrix4x4[] bindPoses = mesh.bindposes;
            Transform[] bones = renderer.bones;
            if (bindPoses == null || bones == null) return matrices;

            NativeArray<byte> bonesPerVertex = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            if (bonesPerVertex.IsCreated && weights.IsCreated && bonesPerVertex.Length == mesh.vertexCount)
            {
                int weightOffset = 0;
                for (int vertexIndex = 0; vertexIndex < mesh.vertexCount; vertexIndex++)
                {
                    Matrix4x4 skin = Matrix4x4.zero;
                    int count = bonesPerVertex[vertexIndex];
                    for (int weightIndex = 0; weightIndex < count && weightOffset + weightIndex < weights.Length; weightIndex++)
                    {
                        BoneWeight1 weight = weights[weightOffset + weightIndex];
                        AddWeightedSkinMatrix(ref skin, renderer, bindPoses, bones, weight.boneIndex, weight.weight);
                    }
                    weightOffset += count;
                    matrices[vertexIndex] = skin;
                }
                bonesPerVertex.Dispose();
                weights.Dispose();
                return matrices;
            }
            if (bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
            if (weights.IsCreated) weights.Dispose();

            BoneWeight[] legacyWeights = mesh.boneWeights;
            for (int vertexIndex = 0; legacyWeights != null && vertexIndex < legacyWeights.Length && vertexIndex < matrices.Length; vertexIndex++)
            {
                BoneWeight weight = legacyWeights[vertexIndex];
                Matrix4x4 skin = Matrix4x4.zero;
                AddWeightedSkinMatrix(ref skin, renderer, bindPoses, bones, weight.boneIndex0, weight.weight0);
                AddWeightedSkinMatrix(ref skin, renderer, bindPoses, bones, weight.boneIndex1, weight.weight1);
                AddWeightedSkinMatrix(ref skin, renderer, bindPoses, bones, weight.boneIndex2, weight.weight2);
                AddWeightedSkinMatrix(ref skin, renderer, bindPoses, bones, weight.boneIndex3, weight.weight3);
                matrices[vertexIndex] = skin;
            }
            return matrices;
        }

        private static Vector3 UnskinVertex(Vector3 bakedVertex, Matrix4x4[] skinMatrices, int vertexIndex)
        {
            if (skinMatrices == null || vertexIndex < 0 || vertexIndex >= skinMatrices.Length) return bakedVertex;
            Matrix4x4 skin = skinMatrices[vertexIndex];
            if (Mathf.Abs(skin.determinant) < 0.000001f) return bakedVertex;
            return skin.inverse.MultiplyPoint3x4(bakedVertex);
        }

        private static void AddWeightedSkinMatrix(ref Matrix4x4 destination, SkinnedMeshRenderer renderer, Matrix4x4[] bindPoses,
            Transform[] bones, int boneIndex, float weight)
        {
            if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length || boneIndex >= bindPoses.Length || bones[boneIndex] == null) return;
            Matrix4x4 matrix = renderer.transform.worldToLocalMatrix * bones[boneIndex].localToWorldMatrix * bindPoses[boneIndex];
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    destination[row, column] += matrix[row, column] * weight;
        }

        private static Vector3 InterpolateNormal(Vector3[] normals, int a, int b, int c, Vector3 barycentric)
        {
            Vector3 normal = normals[a] * barycentric.x + normals[b] * barycentric.y + normals[c] * barycentric.z;
            return normal.sqrMagnitude > 0.00000001f ? normal.normalized : Vector3.up;
        }

        private static void GetWeightedSurfacePoint(SurfaceSnapshot surface, int[] indices, float[] weights,
            out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.zero;
            float weightSum = 0f;
            int count = Mathf.Min(indices == null ? 0 : indices.Length, weights == null ? 0 : weights.Length);
            for (int i = 0; i < count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= surface.vertices.Length) continue;
                point += surface.vertices[index] * weights[i];
                normal += surface.normals[index] * weights[i];
                weightSum += weights[i];
            }
            if (weightSum > 0f) point /= weightSum;
            normal = normal.sqrMagnitude > 0.00000001f ? normal.normalized : Vector3.up;
        }

        private static float[] CalculateInverseDistanceWeights(Vector3[] vertices, List<int> indices, Vector3 point)
        {
            float[] weights = new float[indices.Count];
            float total = 0f;
            for (int i = 0; i < indices.Count; i++)
            {
                float distance = Mathf.Max(0.000001f, (vertices[indices[i]] - point).magnitude);
                weights[i] = 1f / distance;
                total += weights[i];
            }
            if (total > 0f)
                for (int i = 0; i < weights.Length; i++) weights[i] /= total;
            return weights;
        }

        private static Vector3[] CopyRange(Vector3[] source, int start, int count)
        {
            Vector3[] copy = new Vector3[count];
            Array.Copy(source, start, copy, 0, count);
            return copy;
        }

        private static Bounds CalculateBounds(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(vertices[i]);
            return bounds;
        }

        private static Mesh CreateSourceMesh(SlotDataAsset asset)
        {
            if (asset == null || UMAMeshData.IsNullOrEmptyMeshData(asset.meshData)) return null;
            UMAMeshData data = asset.meshData;
            Mesh mesh = new Mesh { name = asset.slotName + "_Original" };
            mesh.vertices = data.vertices;
            if (data.normals != null && data.normals.Length == data.vertices.Length) mesh.normals = data.normals;
            if (data.tangents != null && data.tangents.Length == data.vertices.Length) mesh.tangents = data.tangents;
            if (data.uv != null && data.uv.Length == data.vertices.Length) mesh.uv = data.uv;
            mesh.triangles = ExtractSlotTriangles(null, 0, data.vertexCount, data);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void DestroyTransientBindData()
        {
            smoothingAdjacencyCache.Clear();
            if (bindDataAssets == null) return;
            for (int i = 0; i < bindDataAssets.Count; i++)
            {
                ClothingBindData item = bindDataAssets[i];
                if (item == null) continue;
#if UNITY_EDITOR
                if (AssetDatabase.Contains(item)) continue;
#endif
                if (item.clothingMeshOriginal != null) DestroyUnityObject(item.clothingMeshOriginal);
                DestroyUnityObject(item);
            }
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private bool Fail(string message)
        {
            lastStatus = message;
            Debug.LogWarning("[UMAClothingConformer] " + message, this);
            return false;
        }

        private static string JoinNames(string[] names)
        {
            return names == null ? string.Empty : string.Join("|", names);
        }

        private void OnDrawGizmosSelected()
        {
            if (unboundVertexPositions == null || unboundVertexPositions.Count == 0) return;
            Gizmos.color = Color.magenta;
            float radius = Mathf.Max(0.0025f, settings != null ? settings.normalOffsetEpsilon * 3f : 0.003f);
            for (int i = 0; i < unboundVertexPositions.Count; i++)
                Gizmos.DrawSphere(transform.TransformPoint(unboundVertexPositions[i]), radius);
        }

#if UNITY_EDITOR
        public bool SaveBindDataAssets(string folder)
        {
            if (bindDataAssets == null || bindDataAssets.Count == 0) return Fail("There is no bind data to save.");
            folder = NormalizeAssetFolder(folder);
            if (string.IsNullOrEmpty(folder)) return false;
            for (int i = 0; i < bindDataAssets.Count; i++)
            {
                ClothingBindData data = bindDataAssets[i];
                if (data == null || AssetDatabase.Contains(data)) continue;
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + data.sourceSlotName + "_Bind.asset");
                AssetDatabase.CreateAsset(data, path);
                if (data.clothingMeshOriginal != null) AssetDatabase.AddObjectToAsset(data.clothingMeshOriginal, data);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            lastStatus = "Saved clothing bind data assets.";
            return true;
        }

        public bool SaveConformedSlotsAsNewAssets(string folder)
        {
            if (conformedResults.Count == 0) return Fail("Conform at least one slot before saving geometry.");
            folder = NormalizeAssetFolder(folder);
            if (string.IsNullOrEmpty(folder)) return false;
            for (int i = 0; i < conformedResults.Count; i++)
            {
                ConformedSlotResult result = conformedResults[i];
                SlotDataAsset source = result.slot.asset;
                if (source == null) continue;
                SlotDataAsset saved = Instantiate(source);
                saved.meshData = source.meshData.DeepCopy();
                saved.name = source.slotName + "_Conformed";
                saved._oldSlotName = saved.name;
                saved.meshData.SlotName = saved.name;
                saved.meshData.vertices = (Vector3[])result.baseVertices.Clone();
                saved.meshData.normals = (Vector3[])result.localNormals.Clone();
                saved.meshData.tangents = (Vector4[])result.localTangents.Clone();
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + saved.name + ".asset");
                AssetDatabase.CreateAsset(saved, path);
                Undo.RegisterCreatedObjectUndo(saved, "Create conformed UMA SlotDataAsset");
                EditorUtility.SetDirty(saved);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            lastStatus = "Saved " + conformedResults.Count + " conformed SlotDataAsset(s).";
            return true;
        }

        public bool SaveConformedSlotsToBlendshape(string blendshapeName, string folderForCopies)
        {
            if (string.IsNullOrEmpty(blendshapeName)) return Fail("Enter a blendshape name before saving.");
            if (conformedResults.Count == 0) return Fail("Conform at least one slot before creating a blendshape.");
            if (!TryGetUMA(out UMAData data, out string error)) return Fail(error);
            folderForCopies = NormalizeAssetFolder(folderForCopies);
            if (string.IsNullOrEmpty(folderForCopies)) return false;

            Undo.RecordObject(data, "Save UMA clothing conformer blendshape");

            for (int i = 0; i < conformedResults.Count; i++)
            {
                ConformedSlotResult result = conformedResults[i];
                SlotData slot = result.slot;
                SlotDataAsset target = EnsureWritableSlotAsset(data, slot, folderForCopies);
                if (target == null) continue;
                Undo.RecordObject(target, "Save UMA clothing conformer blendshape");
                AddOrReplaceBlendshape(target, blendshapeName, result);
                EditorUtility.SetDirty(target);
            }
            data.Dirty(false, false, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            lastStatus = "Saved conform result as blendshape '" + blendshapeName + "'.";
            return true;
        }

        private SlotDataAsset EnsureWritableSlotAsset(UMAData data, SlotData slot, string folder)
        {
            if (slot == null || slot.asset == null) return null;
            int uses = 0;
            SlotData[] allSlots = data.umaRecipe.GetAllSlots();
            for (int i = 0; allSlots != null && i < allSlots.Length; i++)
                if (allSlots[i] != null && allSlots[i].asset == slot.asset) uses++;
            if (uses <= 1) return slot.asset;

            SlotDataAsset copy = Instantiate(slot.asset);
            copy.meshData = slot.asset.meshData.DeepCopy();
            copy.name = slot.asset.slotName + "_ConformerCopy";
            copy._oldSlotName = slot.asset.slotName;
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + copy.name + ".asset");
            AssetDatabase.CreateAsset(copy, path);
            Undo.RegisterCreatedObjectUndo(copy, "Create private UMA SlotDataAsset copy");
            slot.asset = copy;
            return copy;
        }

        private static void AddOrReplaceBlendshape(SlotDataAsset target, string name, ConformedSlotResult result)
        {
            UMAMeshData meshData = target.meshData;
            Vector3[] sourceVertices = meshData.vertices;
            Vector3[] sourceNormals = meshData.normals;
            Vector4[] sourceTangents = meshData.tangents;
            Vector3[] deltaVertices = new Vector3[result.baseVertices.Length];
            Vector3[] deltaNormals = new Vector3[result.localNormals.Length];
            Vector3[] deltaTangents = new Vector3[result.localTangents.Length];
            for (int i = 0; i < deltaVertices.Length; i++)
            {
                deltaVertices[i] = result.baseVertices[i] - sourceVertices[i];
                Vector3 normal = sourceNormals != null && i < sourceNormals.Length ? sourceNormals[i] : Vector3.zero;
                deltaNormals[i] = result.localNormals[i] - normal;
                Vector4 tangent = sourceTangents != null && i < sourceTangents.Length ? sourceTangents[i] : Vector4.zero;
                deltaTangents[i] = new Vector3(result.localTangents[i].x - tangent.x, result.localTangents[i].y - tangent.y, result.localTangents[i].z - tangent.z);
            }
            UMABlendShape shape = new UMABlendShape
            {
                shapeName = name,
                frames = new[]
                {
                    new UMABlendFrame
                    {
                        frameWeight = 100f,
                        deltaVertices = deltaVertices,
                        deltaNormals = deltaNormals,
                        deltaTangents = deltaTangents
                    }
                }
            };
            List<UMABlendShape> shapes = meshData.blendShapes != null
                ? new List<UMABlendShape>(meshData.blendShapes) : new List<UMABlendShape>();
            int existing = shapes.FindIndex(item => item != null && item.shapeName == name);
            if (existing >= 0) shapes[existing] = shape;
            else shapes.Add(shape);
            meshData.blendShapes = shapes.ToArray();
        }

        private static string NormalizeAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) folder = "Assets/UMA/ClothingConformer";
            folder = folder.Replace("\\", "/");
            if (!folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                Debug.LogError("[UMAClothingConformer] Assets must be saved under the project's Assets folder.");
                return null;
            }
            string absolute = System.IO.Path.Combine(Application.dataPath, folder.Substring("Assets".Length).TrimStart('/'));
            if (!System.IO.Directory.Exists(absolute)) System.IO.Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
            return folder;
        }
#endif

        [Serializable]
        private class SlotOverrideState
        {
            public SlotDataAsset slotAsset;
            public bool hadOverride;
            public Vector3[] vertices;
        }

        private class SurfaceSnapshot
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public int[] triangles;
            public string[] slotNames;
            public int topologyHash;
        }

        private class RendererSnapshot
        {
            public SkinnedMeshRenderer renderer;
            public Mesh sourceMesh;
            public Vector3[] rootVertices;
            public Vector3[] blendedVertexDeltas;
            public int[][] submeshTriangles;
        }

        private class SlotSnapshot
        {
            public SlotData slot;
            public SkinnedMeshRenderer renderer;
            public RendererSnapshot rendererSnapshot;
            public int startVertex;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public int[] triangles;
        }

        private enum SlotSnapshotDetail
        {
            PositionsOnly,
            Normals,
            NormalAndTangent
        }

        private class ConformedSlotResult
        {
            public SlotData slot;
            public Vector3[] rootVertices;
            public Vector3[] rootNormals;
            public Vector4[] rootTangents;
            public Vector3[] baseVertices;
            public Vector3[] localNormals;
            public Vector4[] localTangents;
            public int[] triangles;
        }

        private class PreviewMeshState
        {
            public SkinnedMeshRenderer renderer;
            public Mesh originalMesh;
            public Mesh previewMesh;
        }

        private class PreviewMeshBuffers
        {
            public Mesh mesh;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
        }

        private struct RendererBlendShapeState
        {
            public int rendererIndex;
            public string shapeName;
            public float weight;
        }

        private struct WeldedDisplacement
        {
            public Vector3 total;
            public int count;
        }
    }
}
