using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Default-combiner pipeline with unused bones baked into preserved bones.
    /// Bone matrices are read from <see cref="UMAImprovedSkeleton"/>'s post-DNA cache;
    /// mesh-only builds never copy that rest pose over the live animated hierarchy.
    /// </summary>
    public class UMADefaultBoneBakingMeshCombiner : UMADefaultMeshCombiner
    {
        private readonly Dictionary<int, int> mergedBones = new Dictionary<int, int>(128);
        private List<Matrix4x4> inverseTargetMatrixBuffer = new List<Matrix4x4>(128);
        private readonly List<MeshBuilder> rendererMeshes = new List<MeshBuilder>(2);
        private readonly List<Vector2[]> rendererUVSnapshots = new List<Vector2[]>(2);
        private UMAImprovedSkeleton bakingSkeleton;
        private Matrix4x4[] inverseTargetMatrices;
        private bool preserveExternalSkeleton;
        private bool preserveUVsForRigOnlyBuild;

        public bool dontCacheBoneWeights;

        public int CachedBoneWeights
        {
            get
            {
                int count = 0;
                for (int i = 0; i < rendererMeshes.Count; i++)
                    count += rendererMeshes[i].CachedBoneWeights;
                return count;
            }
        }

        public int CachedBoneWeightEntries
        {
            get
            {
                int count = 0;
                for (int i = 0; i < rendererMeshes.Count; i++)
                    count += rendererMeshes[i].CachedBoneWeightEntries;
                return count;
            }
        }

        public override void Preprocess(UMAData data)
        {
            // A shape change alters the matrices used to bake discarded bones.
            // Remember when this is the only reason the mesh is being rebuilt so the
            // existing atlas UVs can survive the otherwise rig-only update unchanged.
            preserveUVsForRigOnlyBuild = data.isShapeDirty && !data.isMeshDirty;
            data.isMeshDirty |= data.isShapeDirty;
        }

        protected override void EnsureSkeletonSetup(UMAData data)
        {
            preserveExternalSkeleton = data.HasExternalSkeletonRoot;

            if (data.umaRoot == null || data.skeleton == null || !data.skeleton.isValid())
            {
                if (data.umaRoot == null)
                    data.SetupSkeleton();
                else
                    data.CheckSkeletonSetup();
            }

            bakingSkeleton = data.skeleton as UMAImprovedSkeleton;
            if (bakingSkeleton != null)
                return;

            Transform skeletonRoot = data.GetGlobalTransform();

            if (skeletonRoot == null && data.umaRoot != null)
                skeletonRoot = data.umaRoot.transform.Find("Global");

            if (skeletonRoot == null)
                throw new InvalidOperationException("Bone baking requires a valid UMA skeleton root.");

            bakingSkeleton = new UMAImprovedSkeleton(skeletonRoot);
            data.skeleton = bakingSkeleton;
        }

        protected override void BeginMeshCombination()
        {
            bakingSkeleton = umaData.skeleton as UMAImprovedSkeleton;
            if (bakingSkeleton == null)
                throw new InvalidOperationException("Bone baking requires UMAImprovedSkeleton.");

            umaData.ResetAnimatedBones();
            bakingSkeleton.BeginSkeletonUpdate();

            PopulateSkeletonFromGeneratedMaterials();
            RebuildDNAConverters();

            // This updates only UMAImprovedSkeleton's cached transforms while the
            // skeleton is updating. It intentionally does not touch live Transforms.
            umaData.ResetToTPoseAndApplyDNA();

            if (preserveExternalSkeleton)
            {
                // An external hierarchy is owned by the caller. Never delete any of it;
                // bone baking safely degenerates to retaining those external bones.
                PreserveTransformHierarchy(umaData.GetGlobalTransform());
            }
            else
            {
                PreserveHumanoidBones();
                PreserveSlotAnimatedBones();
            }

            PreserveBone(bakingSkeleton.rootBoneHash);
        }

        protected override void PrepareSkeletonForRenderer(List<SkinnedMeshCombiner.CombineInstance> combineInstances)
        {
            // All active slot bones were loaded before DNA evaluation. In particular,
            // do not call EnsureBoneHierarchy here: it writes the cached rest pose into
            // the live Animator hierarchy.
        }

        protected override void CombineMeshesForRenderer(
            bool updatedAtlas,
            List<UMAData.GeneratedMaterial> materialsForRenderer,
            Dictionary<string, float> bakedBlendshapes)
        {
            MeshBuilder targetMesh = GetRendererMesh(currentRendererIndex);
            Vector2[] previousUVs = preserveUVsForRigOnlyBuild && !updatedAtlas
                ? CapturePreviousUVs(targetMesh, renderers[currentRendererIndex], currentRendererIndex)
                : null;
            var sources = CreateBakingSources(combinedMeshList);

            MergeSkeletons(sources, targetMesh);
            PopulateMatrices(sources, targetMesh, renderers[currentRendererIndex]);

            SkinnedMeshCombinerRetargeting.CombineMeshes(
                targetMesh,
                sources,
                inverseTargetMatrices,
                umaData.blendShapeSettings,
                uniformTargetPoses: true);

            if (!RestorePreviousUVs(targetMesh, previousUVs))
                RecalculateUV(targetMesh, materialsForRenderer, updatedAtlas);
            targetMesh.ReleaseBuffers();

            // Target transforms are preserved bones. Missing transforms may be created,
            // but existing animated transforms must remain untouched on a mesh-only build.
            targetMesh.ApplyDataToUnityMesh(
                renderers[currentRendererIndex],
                bakingSkeleton,
                ensureBoneHierarchy: false);

            ApplyBlendShapeWeights(renderers[currentRendererIndex]);
            renderers[currentRendererIndex].sharedMesh.name = currentRendererIndex == 0
                ? "UMAMesh"
                : "UMAMesh " + currentRendererIndex;
        }

        protected override void EndMeshCombination()
        {
            if (bakingSkeleton != null && bakingSkeleton.isUpdating)
                bakingSkeleton.EndSkeletonUpdate();

            preserveUVsForRigOnlyBuild = false;
        }

        private MeshBuilder GetRendererMesh(int rendererIndex)
        {
            while (rendererMeshes.Count <= rendererIndex)
                rendererMeshes.Add(new MeshBuilder());

            MeshBuilder mesh = rendererMeshes[rendererIndex];
            mesh.cacheBoneWeights = !dontCacheBoneWeights;
            return mesh;
        }

        private void PopulateSkeletonFromGeneratedMaterials()
        {
            var materials = umaData.generatedMaterials.materials;
            for (int m = 0; m < materials.Count; m++)
            {
                var material = materials[m];
                if (material?.materialFragments == null) continue;

                for (int f = 0; f < material.materialFragments.Count; f++)
                {
                    var slot = material.materialFragments[f]?.slotData;
                    var meshData = slot?.asset?.meshData;
                    if (meshData?.umaBones == null) continue;

                    for (int b = 0; b < meshData.umaBones.Length; b++)
                        bakingSkeleton.EnsureBone(meshData.umaBones[b]);
                }
            }
        }

        private void RebuildDNAConverters()
        {
            umaData.umaRecipe.ClearDNAConverters();
            var slots = umaData.umaRecipe.slotDataList;
            if (slots == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot != null && !slot.isBlendShapeSource && !slot.isPlaceholderSlot && slot.asset != null)
                    umaData.umaRecipe.AddDNAUpdater(slot.asset.slotDNA);
            }
        }

        private void PreserveHumanoidBones()
        {
            var tPose = umaData.umaRecipe.raceData?.TPose;
            if (tPose == null) return;

            tPose.DeSerialize();
            for (int i = 0; i < tPose.humanInfo.Length; i++)
                PreserveBone(UMAUtils.StringToHash(tPose.humanInfo[i].boneName));
        }

        private void PreserveSlotAnimatedBones()
        {
            var slots = umaData.umaRecipe.slotDataList;
            if (slots == null) return;

            for (int s = 0; s < slots.Length; s++)
            {
                var boneNames = slots[s]?.asset?.UnbakedAnimatedBones;
                if (boneNames == null) continue;

                for (int b = 0; b < boneNames.Length; b++)
                {
                    if (!string.IsNullOrEmpty(boneNames[b]))
                        PreserveBone(UMAUtils.StringToHash(boneNames[b]));
                }
            }
        }

        private void PreserveTransformHierarchy(Transform transform)
        {
            if (transform == null) return;

            PreserveBone(UMAUtils.StringToHash(transform.name));
            for (int i = 0; i < transform.childCount; i++)
                PreserveTransformHierarchy(transform.GetChild(i));
        }

        private void PreserveBone(int hash)
        {
            if (!bakingSkeleton.HasBone(hash))
                return;

            bakingSkeleton.SetAnimatedBone(hash);
            umaData.RegisterAnimatedBone(hash);
        }

        private SkinnedMeshCombinerRetargeting.CombineInstance[] CreateBakingSources(
            List<SkinnedMeshCombiner.CombineInstance> defaultSources)
        {
            var bakingSources = new SkinnedMeshCombinerRetargeting.CombineInstance[defaultSources.Count];
            int vertexOffset = 0;

            for (int i = 0; i < defaultSources.Count; i++)
            {
                var source = defaultSources[i];
                // The retargeting combiner may populate managed bone-weight fields.
                // Always isolate that work from the SlotDataAsset mesh.
                UMAMeshData meshCopy = source.meshData.ShallowCopy(null);
                meshCopy.SlotName = source.meshData.SlotName;
                if (umaData.umaRecipe.BlendshapeSlots.ContainsKey(meshCopy.SlotName))
                {
                    var blendShapeSources = SkinnedMeshCombiner.GetBlendshapeSources(meshCopy, umaData.umaRecipe);
                    meshCopy.blendShapes = blendShapeSources.ToArray();
                }

                bakingSources[i] = new SkinnedMeshCombinerRetargeting.CombineInstance
                {
                    meshData = meshCopy,
                    targetSubmeshIndices = source.targetSubmeshIndices,
                    triangleMask = source.triangleMask
                };

                if (source.slotData != null)
                {
                    source.slotData.vertexOffset = vertexOffset;
                    source.slotData.skinnedMeshRenderer = currentRendererIndex;
                    for (int sm = 0; sm < source.targetSubmeshIndices.Length; sm++)
                    {
                        if (source.targetSubmeshIndices[sm] >= 0)
                        {
                            source.slotData.submeshIndex = source.targetSubmeshIndices[sm];
                            break;
                        }
                    }
                }

                vertexOffset += meshCopy.vertices != null ? meshCopy.vertices.Length : meshCopy.vertexCount;
            }

            return bakingSources;
        }

        private void MergeSkeletons(
            SkinnedMeshCombinerRetargeting.CombineInstance[] sources,
            MeshBuilder targetMesh)
        {
            mergedBones.Clear();

            for (int s = 0; s < sources.Length; s++)
            {
                var meshData = sources[s].meshData;
                int sourceBoneCount = meshData.boneNameHashes != null ? meshData.boneNameHashes.Length : 0;
                sources[s].targetBoneIndices = new int[sourceBoneCount];

                for (int b = 0; b < sourceBoneCount; b++)
                {
                    int sourceHash = meshData.boneNameHashes[b];
                    int targetHash = bakingSkeleton.ResolvePreservedHash(sourceHash);
                    if (!mergedBones.TryGetValue(targetHash, out int targetIndex))
                    {
                        targetIndex = mergedBones.Count;
                        mergedBones.Add(targetHash, targetIndex);
                    }
                    sources[s].targetBoneIndices[b] = targetIndex;
                }
            }

            if (mergedBones.Count == 0)
                mergedBones.Add(bakingSkeleton.rootBoneHash, 0);

            targetMesh.PrepareBones(mergedBones.Count);
            foreach (var entry in mergedBones)
                targetMesh.boneNameHashes[entry.Value] = entry.Key;
        }

        private void PopulateMatrices(
            SkinnedMeshCombinerRetargeting.CombineInstance[] sources,
            MeshBuilder targetMesh,
            SkinnedMeshRenderer renderer)
        {
            Matrix4x4 rendererMatrix = renderer.transform.localToWorldMatrix;
            int missingSourceBones = 0;
            int missingBindPoses = 0;

            for (int s = 0; s < sources.Length; s++)
            {
                var meshData = sources[s].meshData;
                int sourceBoneCount = meshData.boneNameHashes != null ? meshData.boneNameHashes.Length : 0;
                sources[s].resolvedBoneMatrixes = new Matrix4x4[sourceBoneCount];

                for (int b = 0; b < sourceBoneCount; b++)
                {
                    bool hasBindPose = meshData.bindPoses != null && b < meshData.bindPoses.Length;
                    Matrix4x4 bindPose = hasBindPose ? meshData.bindPoses[b] : Matrix4x4.identity;
                    int sourceHash = meshData.boneNameHashes[b];

                    bool hasSourceBone = bakingSkeleton.HasBone(sourceHash);
                    Matrix4x4 sourceBoneMatrix = hasSourceBone
                        ? bakingSkeleton.GetLocalToWorldMatrix(sourceHash)
                        : rendererMatrix * bindPose.inverse;

                    if (!hasSourceBone) missingSourceBones++;
                    if (!hasBindPose) missingBindPoses++;

                    sources[s].resolvedBoneMatrixes[b] = sourceBoneMatrix * bindPose;
                }
            }

#if UNITY_EDITOR
            if (missingSourceBones > 0 || missingBindPoses > 0)
            {
                Debug.LogWarning(
                    $"[{nameof(UMADefaultBoneBakingMeshCombiner)}] Renderer {currentRendererIndex} used " +
                    $"fallback data for {missingSourceBones} missing source bones and {missingBindPoses} missing bind poses. " +
                    "Reimport legacy slot meshes to restore authored bone metadata.",
                    this);
            }
#endif

            ListHelper<Matrix4x4>.AllocateArray(
                ref inverseTargetMatrixBuffer,
                out inverseTargetMatrices,
                targetMesh.bonesCount);

            Matrix4x4 rendererMatrixInverse = rendererMatrix.inverse;
            for (int b = 0; b < targetMesh.bonesCount; b++)
            {
                Matrix4x4 boneMatrix = bakingSkeleton.GetLocalToWorldMatrix(targetMesh.boneNameHashes[b]);
                targetMesh.bindPoses[b] = boneMatrix.inverse * rendererMatrix;
                inverseTargetMatrices[b] = rendererMatrixInverse;
            }
        }

        private Vector2[] CapturePreviousUVs(MeshBuilder mesh, SkinnedMeshRenderer renderer, int rendererIndex)
        {
            Vector2[] sourceUVs = null;
            int sourceVertexCount = 0;

            if (mesh != null && mesh.has_uv && mesh.uv != null && mesh.vertexCount > 0 && mesh.uv.Length >= mesh.vertexCount)
            {
                sourceUVs = mesh.uv;
                sourceVertexCount = mesh.vertexCount;
            }
            else
            {
                Mesh unityMesh = renderer != null ? renderer.sharedMesh : null;
                if (unityMesh != null && unityMesh.isReadable && unityMesh.vertexCount > 0)
                {
                    Vector2[] unityUVs = unityMesh.uv;
                    if (unityUVs != null && unityUVs.Length == unityMesh.vertexCount)
                    {
                        sourceUVs = unityUVs;
                        sourceVertexCount = unityUVs.Length;
                    }
                }
            }

            if (sourceUVs == null || sourceVertexCount == 0)
                return null;

            while (rendererUVSnapshots.Count <= rendererIndex)
                rendererUVSnapshots.Add(null);

            Vector2[] snapshot = rendererUVSnapshots[rendererIndex];
            if (snapshot == null || snapshot.Length != sourceVertexCount)
            {
                snapshot = new Vector2[sourceVertexCount];
                rendererUVSnapshots[rendererIndex] = snapshot;
            }

            Array.Copy(sourceUVs, 0, snapshot, 0, sourceVertexCount);
            return snapshot;
        }

        private static bool RestorePreviousUVs(MeshBuilder mesh, Vector2[] previousUVs)
        {
            if (previousUVs == null || mesh == null || !mesh.has_uv || mesh.uv == null ||
                mesh.vertexCount != previousUVs.Length || mesh.uv.Length < mesh.vertexCount)
                return false;

            Array.Copy(previousUVs, 0, mesh.uv, 0, mesh.vertexCount);
            return true;
        }

        private void RecalculateUV(
            MeshBuilder mesh,
            List<UMAData.GeneratedMaterial> materialsForRenderer,
            bool updatedAtlas)
        {
            if (mesh.uv == null) return;

            int vertexIndex = 0;
            for (int materialIndex = 0; materialIndex < materialsForRenderer.Count; materialIndex++)
            {
                var generatedMaterial = materialsForRenderer[materialIndex];
                for (int fragmentIndex = 0; fragmentIndex < generatedMaterial.materialFragments.Count; fragmentIndex++)
                {
                    var fragment = generatedMaterial.materialFragments[fragmentIndex];
                    var slot = fragment?.slotData;
                    var sourceMesh = slot?.asset?.meshData;
                    if (sourceMesh == null || sourceMesh.vertices == null) continue;

                    int vertexCount = sourceMesh.vertices.Length;
                    if (!generatedMaterial.umaMaterial.IsGeneratedTextures)
                    {
                        vertexIndex += vertexCount;
                        continue;
                    }

                    Rect cachedUVArea = slot.UVArea;
                    bool useCachedUVArea = preserveUVsForRigOnlyBuild && !updatedAtlas && IsValidUVArea(cachedUVArea);
                    float atlasXMin;
                    float atlasYMin;
                    float atlasXRange;
                    float atlasYRange;

                    if (useCachedUVArea)
                    {
                        atlasXMin = cachedUVArea.xMin;
                        atlasYMin = cachedUVArea.yMin;
                        atlasXRange = cachedUVArea.width;
                        atlasYRange = cachedUVArea.height;
                    }
                    else
                    {
                        Rect atlasRect = fragment.atlasRegion;
                        atlasXMin = atlasRect.xMin / atlasResolution;
                        atlasYMin = atlasRect.yMin / atlasResolution;
                        atlasXRange = atlasRect.width / atlasResolution;
                        atlasYRange = atlasRect.height / atlasResolution;

                        if (fragment.isRectShared && slot.useAtlasOverlay && fragment.overlayList != null)
                        {
                            OverlayData sharedRect = null;
                            for (int o = 0; o < fragment.overlayList.Count; o++)
                            {
                                var overlay = fragment.overlayList[o];
                                if (overlay != null && slot.slotName != null && overlay.overlayName != null &&
                                    overlay.overlayName.Contains(slot.slotName))
                                {
                                    sharedRect = overlay;
                                    break;
                                }
                            }

                            if (sharedRect != null && sharedRect.rect != Rect.zero)
                            {
                                Vector2 size = sharedRect.rect.size * generatedMaterial.resolutionScale;
                                float offsetX = sharedRect.rect.x * generatedMaterial.resolutionScale.x;
                                float offsetY = sharedRect.rect.y * generatedMaterial.resolutionScale.x;
                                atlasXMin += offsetX / generatedMaterial.cropResolution.x;
                                atlasXRange = size.x / generatedMaterial.cropResolution.x;
                                atlasYMin += offsetY / generatedMaterial.cropResolution.y;
                                atlasYRange = size.y / generatedMaterial.cropResolution.y;
                            }
                        }

                        slot.UVArea.Set(atlasXMin, atlasYMin, atlasXRange, atlasYRange);
                    }

                    int end = Math.Min(vertexIndex + vertexCount, mesh.uv.Length);
                    while (vertexIndex < end)
                    {
                        Vector2 uv = mesh.uv[vertexIndex];
                        uv.x = atlasXMin + atlasXRange * uv.x;
                        uv.y = atlasYMin + atlasYRange * uv.y;
                        mesh.uv[vertexIndex] = uv;
                        vertexIndex++;
                    }
                }
            }
        }

        private static bool IsValidUVArea(Rect area)
        {
            return IsFinite(area.x) && IsFinite(area.y) && IsFinite(area.width) && IsFinite(area.height) &&
                area.width > Mathf.Epsilon && area.height > Mathf.Epsilon;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ApplyBlendShapeWeights(SkinnedMeshRenderer renderer)
        {
            var settings = umaData.blendShapeSettings;
            if (settings == null || settings.blendShapes == null || settings.ignoreBlendShapes || renderer?.sharedMesh == null)
                return;

            foreach (var entry in settings.blendShapes)
            {
                if (entry.Value == null) continue;
                int index = renderer.sharedMesh.GetBlendShapeIndex(entry.Key);
                if (index >= 0)
                    renderer.SetBlendShapeWeight(index, entry.Value.value * 100f);
            }
        }
    }
}
