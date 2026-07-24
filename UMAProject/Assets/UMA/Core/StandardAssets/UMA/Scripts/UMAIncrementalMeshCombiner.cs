using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UMA.Dynamics;

namespace UMA
{
    /// <summary>
    /// Opt-in mesh combiner that prepares renderer meshes as resumable
    /// operations. Existing mesh combiners and direct synchronous callers are
    /// unaffected.
    /// </summary>
    [AddComponentMenu("UMA/UMA Incremental Mesh Combiner")]
    public sealed class UMAIncrementalMeshCombiner :
        UMAMeshCombiner,
        IUMAMultiStepMeshCombiner
    {
        public IUMAMeshCombineOperation BeginUpdateUMAMesh(
            bool updatedAtlas,
            UMAData umaData,
            int atlasResolution)
        {
            if (umaData == null)
            {
                throw new ArgumentNullException(nameof(umaData));
            }
            if (atlasResolution <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(atlasResolution),
                    atlasResolution,
                    "Atlas resolution must be greater than zero.");
            }

            return new UMAIncrementalMeshCombineOperation(
                this,
                updatedAtlas,
                umaData,
                atlasResolution);
        }

        /// <summary>
        /// Preserves the inherited synchronous contract for editor utilities
        /// and callers outside UMAGeneratorBuiltin.Work.
        /// </summary>
        public override void UpdateUMAMesh(
            bool updatedAtlas,
            UMAData umaData,
            int atlasResolution)
        {
            using (var operation = (UMAIncrementalMeshCombineOperation)
                   BeginUpdateUMAMesh(updatedAtlas, umaData, atlasResolution))
            {
                operation.RunSynchronously();
            }
        }

        internal UMAIncrementalMeshCombinePlan BuildPlan(
            bool updatedAtlas,
            UMAData data,
            int atlasResolution)
        {
            if (data.umaRecipe == null)
            {
                throw new InvalidOperationException(
                    "Incremental mesh generation requires an initialized UMA recipe.");
            }
            if (data.generatedMaterials == null ||
                data.generatedMaterials.rendererAssets == null)
            {
                throw new InvalidOperationException(
                    "Incremental mesh generation requires generated renderer materials.");
            }

            data.umaRecipe.UpdateMeshHideMasks(data.currentLODLevel);
            if (data.umaRoot == null)
            {
                data.SetupSkeleton();
            }
            else
            {
                data.CheckSkeletonSetup();
            }
            if (data.skeleton == null)
            {
                throw new InvalidOperationException(
                    "Incremental mesh generation could not initialize the UMA skeleton.");
            }

            data.BuildActiveModifiers();

            var rendererAssets = data.generatedMaterials.rendererAssets.ToArray();
            var previousRenderers = data.GetRenderers();
            var rendererPlans =
                new UMAIncrementalRendererPlan[rendererAssets.Length];
            var bakedBlendshapes =
                BuildBakedBlendshapeDictionary(data.blendShapeSettings);
            Quaternion boundsRotation = data.umaRecipe.raceData != null &&
                                        data.umaRecipe.raceData.FixupRotations
                ? SkinnedMeshCombinerMeshAPI.FixupRotation
                : Quaternion.identity;

            var plan = new UMAIncrementalMeshCombinePlan(
                data,
                atlasResolution,
                previousRenderers,
                rendererAssets,
                rendererPlans,
                bakedBlendshapes,
                boundsRotation);

            try
            {
                for (int rendererIndex = 0;
                     rendererIndex < rendererAssets.Length;
                     rendererIndex++)
                {
                    UMARendererAsset rendererAsset = rendererAssets[rendererIndex];
                    var stagingRenderer = CreateStagingRenderer(
                        data,
                        rendererIndex,
                        rendererAsset);
                    var materials = FilterMaterials(data, rendererAsset);
                    UMAClothProperties clothProperties;
                    SkinnedMeshCombiner.CombineInstance[] sources =
                        BuildCombineInstances(
                            data,
                            materials,
                            rendererIndex,
                            out clothProperties);

                    if (updatedAtlas)
                    {
                        SetSlotUVAreas(materials, atlasResolution);
                    }

                    rendererPlans[rendererIndex] =
                        new UMAIncrementalRendererPlan(
                            rendererIndex,
                            rendererAsset,
                            stagingRenderer,
                            materials,
                            sources,
                            clothProperties);
                }

                plan.CaptureBuildMetadataAndRestore();
                return plan;
            }
            catch
            {
                plan.Dispose();
                throw;
            }
        }

        private SkinnedMeshRenderer CreateStagingRenderer(
            UMAData data,
            int rendererIndex,
            UMARendererAsset rendererAsset)
        {
            var rendererObject = new GameObject(
                $"UMA Incremental Staging Renderer {rendererIndex}");
            rendererObject.transform.SetParent(transform, false);
            rendererObject.layer = data.gameObject.layer;

            var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.rootBone = data.GetGlobalTransform();
            renderer.quality = SkinQuality.Auto;
            renderer.sharedMesh = new Mesh
            {
                name = rendererIndex == 0
                    ? "UMAMesh"
                    : $"UMAMesh {rendererIndex}",
                indexFormat = data.force32bit
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            if (data.markDynamic)
            {
                renderer.sharedMesh.MarkDynamic();
            }

            UMARendererAsset settings =
                rendererAsset != null ? rendererAsset : data.defaultRendererAsset;
            if (settings != null)
            {
                settings.ApplySettingsToRenderer(renderer);
            }
            else
            {
                UMARendererAsset.ResetRenderer(renderer);
            }
            return renderer;
        }

        private static UMAData.GeneratedMaterial[] FilterMaterials(
            UMAData data,
            UMARendererAsset rendererAsset)
        {
            var filtered = new List<UMAData.GeneratedMaterial>(16);
            List<UMAData.GeneratedMaterial> materials =
                data.generatedMaterials.materials;
            for (int i = 0; i < materials.Count; i++)
            {
                UMAData.GeneratedMaterial material = materials[i];
                if (material != null && material.rendererAsset == rendererAsset)
                {
                    filtered.Add(material);
                }
            }
            return filtered.ToArray();
        }

        private static SkinnedMeshCombiner.CombineInstance[]
            BuildCombineInstances(
                UMAData data,
                UMAData.GeneratedMaterial[] materials,
                int rendererIndex,
                out UMAClothProperties clothProperties)
        {
            clothProperties = null;
            var instances = new List<SkinnedMeshCombiner.CombineInstance>(
                Math.Max(4, data.umaRecipe.slotDataList.Length));

            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                UMAData.GeneratedMaterial generatedMaterial =
                    materials[materialIndex];
                ValidateGeneratedMaterial(generatedMaterial, materialIndex);
                generatedMaterial.materialIndex = materialIndex;

                for (int fragmentIndex = 0;
                     fragmentIndex < generatedMaterial.materialFragments.Count;
                     fragmentIndex++)
                {
                    UMAData.MaterialFragment fragment =
                        generatedMaterial.materialFragments[fragmentIndex];
                    if (fragment == null)
                    {
                        throw new InvalidOperationException(
                            $"Generated material {materialIndex} contains a null fragment at index {fragmentIndex}.");
                    }

                    SlotData slot = fragment.slotData;
                    UMAMeshData sourceMesh = slot?.asset?.meshData;
                    if (slot == null ||
                        slot.asset == null ||
                        UMAMeshData.IsNullOrEmptyMeshData(sourceMesh))
                    {
                        continue;
                    }

                    bool hasVertexOverride =
                        data.VertexOverrides.ContainsKey(slot.slotName);
                    bool needsCopy = hasVertexOverride || slot.UVRemapped;
                    UMAMeshData meshData = needsCopy
                        ? sourceMesh.ShallowCopy(
                            hasVertexOverride
                                ? data.VertexOverrides[slot.slotName]
                                : null)
                        : sourceMesh;
                    meshData.SlotName = slot.slotName;

                    if (slot.UVRemapped)
                    {
                        switch (slot.UVSet)
                        {
                            case 1:
                                meshData.uv = sourceMesh.uv2;
                                break;
                            case 2:
                                meshData.uv = sourceMesh.uv3;
                                break;
                            case 3:
                                meshData.uv = sourceMesh.uv4;
                                break;
                        }
                    }

                    bool modifiersRunInJobs =
                        SkinnedMeshCombinerMeshAPI
                            .SupportsJobifiedMeshModifiers(slot);
                    if (!modifiersRunInJobs)
                    {
                        meshData = ApplyManagedMeshModifiers(meshData, slot);
                    }

                    int subMeshCount = meshData.subMeshCount;
                    if (subMeshCount == 0)
                    {
                        continue;
                    }
                    if ((uint)slot.asset.subMeshIndex >=
                        (uint)subMeshCount)
                    {
                        throw new InvalidOperationException(
                            $"Slot '{slot.slotName}' maps material submesh {slot.asset.subMeshIndex}, but its mesh only has {subMeshCount} submeshes.");
                    }

                    var targetSubmeshes = new int[subMeshCount];
                    for (int i = 0; i < targetSubmeshes.Length; i++)
                    {
                        targetSubmeshes[i] = -1;
                    }
                    targetSubmeshes[slot.asset.subMeshIndex] = materialIndex;

                    instances.Add(new SkinnedMeshCombiner.CombineInstance
                    {
                        meshData = meshData,
                        slotData = slot,
                        triangleMask = slot.meshHideMask,
                        targetSubmeshIndices = targetSubmeshes,
                        applyMeshModifiersInJobs = modifiersRunInJobs
                    });

                    if (slot.asset.SlotAtlassed != null)
                    {
                        slot.asset.SlotAtlassed.Invoke(
                            data,
                            slot,
                            generatedMaterial.material,
                            fragment.atlasRegion);
                    }

                    if (generatedMaterial.rendererAsset?.ClothProperties != null)
                    {
                        clothProperties =
                            generatedMaterial.rendererAsset.ClothProperties;
                    }
                }
            }

            return instances.ToArray();
        }

        private static UMAMeshData ApplyManagedMeshModifiers(
            UMAMeshData meshData,
            SlotData slot)
        {
            if (slot.meshModifiers == null)
            {
                return meshData;
            }
            for (int i = 0; i < slot.meshModifiers.Count; i++)
            {
                MeshModifier.Modifier modifier = slot.meshModifiers[i];
                if (modifier != null)
                {
                    meshData = modifier.Process(meshData);
                }
            }
            return meshData;
        }

        private static void ValidateGeneratedMaterial(
            UMAData.GeneratedMaterial material,
            int materialIndex)
        {
            if (material == null)
            {
                throw new InvalidOperationException(
                    $"Generated material {materialIndex} is null.");
            }
            if (material.umaMaterial == null)
            {
                throw new InvalidOperationException(
                    $"Generated material {materialIndex} has no UMA material definition.");
            }
            if (material.materialFragments == null)
            {
                throw new InvalidOperationException(
                    $"Generated material {materialIndex} has no fragment collection.");
            }
            if (material.material == null &&
                material.umaMaterial.material == null)
            {
                throw new InvalidOperationException(
                    $"Generated material {materialIndex} has no usable first-pass material.");
            }
        }

        private static Dictionary<string, float>
            BuildBakedBlendshapeDictionary(BlendShapeSettings settings)
        {
            var result = new Dictionary<string, float>();
            if (settings?.blendShapes == null)
            {
                return result;
            }
            foreach (KeyValuePair<string, BlendShapeData> entry
                     in settings.blendShapes)
            {
                if (entry.Value != null && entry.Value.isBaked)
                {
                    result[entry.Key] = entry.Value.value;
                }
            }
            return result;
        }

        private static void SetSlotUVAreas(
            UMAData.GeneratedMaterial[] materials,
            int atlasResolution)
        {
            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                UMAData.GeneratedMaterial material = materials[materialIndex];
                for (int fragmentIndex = 0;
                     fragmentIndex < material.materialFragments.Count;
                     fragmentIndex++)
                {
                    UMAData.MaterialFragment fragment =
                        material.materialFragments[fragmentIndex];
                    SlotData slot = fragment?.slotData;
                    if (slot?.asset == null ||
                        UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                    {
                        continue;
                    }

                    Rect rect = fragment.atlasRegion;
                    float xMin = rect.xMin / atlasResolution;
                    float yMin = rect.yMin / atlasResolution;
                    float xRange = rect.width / atlasResolution;
                    float yRange = rect.height / atlasResolution;
                    if (fragment.isRectShared && slot.useAtlasOverlay)
                    {
                        OverlayData sharedOverlay = null;
                        for (int overlayIndex = 0;
                             overlayIndex < fragment.overlayList.Count;
                             overlayIndex++)
                        {
                            OverlayData overlay =
                                fragment.overlayList[overlayIndex];
                            if (slot.slotName != null &&
                                overlay.overlayName != null &&
                                overlay.overlayName.Contains(slot.slotName))
                            {
                                sharedOverlay = overlay;
                                break;
                            }
                        }
                        if (sharedOverlay != null &&
                            sharedOverlay.rect != Rect.zero)
                        {
                            Vector2 size =
                                sharedOverlay.rect.size *
                                material.resolutionScale;
                            float offsetX =
                                sharedOverlay.rect.x *
                                material.resolutionScale.x;
                            float offsetY =
                                sharedOverlay.rect.y *
                                material.resolutionScale.y;
                            xMin += offsetX / material.cropResolution.x;
                            yMin += offsetY / material.cropResolution.y;
                            xRange = size.x / material.cropResolution.x;
                            yRange = size.y / material.cropResolution.y;
                        }
                    }
                    slot.UVArea.Set(xMin, yMin, xRange, yRange);
                }
            }
        }

        internal void Commit(UMAIncrementalMeshCombinePlan plan)
        {
            UMAData data = plan.Data;
            var committedRenderers =
                new SkinnedMeshRenderer[plan.Renderers.Length];

            // Configure every detached renderer before changing UMAData's live
            // renderer references.
            for (int i = 0; i < plan.Renderers.Length; i++)
            {
                UMAIncrementalRendererPlan rendererPlan = plan.Renderers[i];
                SkinnedMeshRenderer renderer = rendererPlan.StagingRenderer;
                committedRenderers[i] = renderer;
                if (rendererPlan.IsEmpty)
                {
                    ClearStagingRenderer(renderer);
                    if (i == 0)
                    {
                        plan.SetPlannedOriginalMeshBounds(
                            new Bounds(
                                Vector3.zero,
                                Vector3.zero));
                    }
                    continue;
                }

                AssignRendererMaterials(
                    data,
                    plan,
                    renderer,
                    rendererPlan.Materials);
                SetupCloth(
                    data,
                    renderer,
                    rendererPlan.PreparedCloth,
                    rendererPlan.ClothProperties);
                if (i == 0 && renderer.sharedMesh != null)
                {
                    plan.SetPlannedOriginalMeshBounds(
                        renderer.sharedMesh.bounds);
                }
            }

            data.umaRecipe.ClearDNAConverters();
            SlotData[] slots = data.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot != null &&
                    !slot.isBlendShapeSource &&
                    !slot.isPlaceholderSlot &&
                    slot.asset != null)
                {
                    data.umaRecipe.AddDNAUpdater(slot.asset.slotDNA);
                }
            }

            for (int i = 0; i < committedRenderers.Length; i++)
            {
                Transform rendererTransform = committedRenderers[i].transform;
                rendererTransform.SetParent(data.transform, false);
                rendererTransform.localPosition = Vector3.zero;
                rendererTransform.localRotation = Quaternion.identity;
                rendererTransform.localScale = Vector3.one;
                committedRenderers[i].gameObject.name =
                    i == 0 ? "UMARenderer" : $"UMARenderer {i}";
            }

            for (int i = 0; i < plan.Renderers.Length; i++)
            {
                UMAIncrementalRendererPlan rendererPlan = plan.Renderers[i];
                for (int materialIndex = 0;
                     materialIndex < rendererPlan.Materials.Length;
                     materialIndex++)
                {
                    plan.SetPlannedMaterialRenderer(
                        rendererPlan.Materials[materialIndex],
                        rendererPlan.IsEmpty
                            ? null
                            : rendererPlan.StagingRenderer);
                }
            }
            plan.ApplyPlannedMetadata();
            data.SetRendererAssets(plan.RendererAssets);
            data.SetRenderers(committedRenderers);
            plan.MarkCommitted();

            data.firstBake = false;
            DestroyPreviousRenderers(
                plan.PreviousRenderers,
                committedRenderers);
            plan.FinalizeCommittedMetadata();
        }

        private static void AssignRendererMaterials(
            UMAData data,
            UMAIncrementalMeshCombinePlan plan,
            SkinnedMeshRenderer renderer,
            UMAData.GeneratedMaterial[] materials)
        {
            var materialBuffer = new List<Material>(materials.Length * 2);
            var submeshBuffer =
                new List<SubMeshDescriptor>(materials.Length * 2);
            Mesh mesh = renderer.sharedMesh;

            for (int i = 0; i < materials.Length; i++)
            {
                if (i >= mesh.subMeshCount)
                {
                    break;
                }
                UMAData.GeneratedMaterial generatedMaterial = materials[i];
                Material firstPass = generatedMaterial.material != null
                    ? generatedMaterial.material
                    : generatedMaterial.umaMaterial.material;
                if (firstPass == null)
                {
                    continue;
                }

                SubMeshDescriptor descriptor = mesh.GetSubMesh(i);
                int firstPassSubmesh = submeshBuffer.Count;
                materialBuffer.Add(firstPass);
                submeshBuffer.Add(descriptor);
                for (int fragmentIndex = 0;
                     fragmentIndex <
                     generatedMaterial.materialFragments.Count;
                     fragmentIndex++)
                {
                    SlotData slot = generatedMaterial
                        .materialFragments[fragmentIndex].slotData;
                    if (slot != null)
                    {
                        plan.SetPlannedSubmesh(
                            slot,
                            firstPassSubmesh);
                    }
                }

                if (generatedMaterial.umaMaterial.materialType ==
                    UMAMaterial.MaterialType.UseExistingTextures)
                {
                    UMAGeneratorPro.ApplyMaterialParameters(
                        generatedMaterial,
                        data,
                        firstPass);
                }

                if (generatedMaterial.umaMaterial.secondPass != null)
                {
                    Material secondPass =
                        generatedMaterial.secondPassMaterial;
                    if (secondPass == null ||
                        secondPass == firstPass ||
                        secondPass.shader !=
                        generatedMaterial.umaMaterial.secondPass.shader)
                    {
                        if (secondPass != null && secondPass != firstPass)
                        {
                            // Keep the previous generated material reference
                            // alive until the complete renderer transaction is
                            // committed. Rollback can then restore it.
                        }
                        secondPass = Instantiate(
                            generatedMaterial.umaMaterial.secondPass);
                        generatedMaterial.secondPassMaterial = secondPass;
                    }

                    UMAGeneratorPro.ApplyMaterialParameters(
                        generatedMaterial,
                        data,
                        secondPass);
                    UMAJobifiedMeshCombiner.CopyMaterialTextures(
                        secondPass,
                        generatedMaterial.material,
                        generatedMaterial.umaMaterial);
                    if (generatedMaterial.material != null &&
                        generatedMaterial.material.HasProperty(
                            "_OverlayCount"))
                    {
                        UMAJobifiedMeshCombiner.SetCompositingParameters(
                            secondPass,
                            generatedMaterial);
                    }
                    materialBuffer.Add(secondPass);
                    submeshBuffer.Add(descriptor);
                }
                else if (generatedMaterial.secondPassMaterial != null &&
                         generatedMaterial.secondPassMaterial != firstPass)
                {
                    generatedMaterial.secondPassMaterial = null;
                }
                plan.CaptureConfiguredMaterial(generatedMaterial);
            }

#if UNITY_2023_1_OR_NEWER
            renderer.SetSharedMaterials(materialBuffer);
#else
            renderer.sharedMaterials = materialBuffer.ToArray();
#endif
            mesh.SetSubMeshes(
                submeshBuffer,
                MeshUpdateFlags.DontRecalculateBounds |
                MeshUpdateFlags.DontValidateIndices);
            mesh.UploadMeshData(data.markNotReadable);
        }

        private static void SetupCloth(
            UMAData data,
            SkinnedMeshRenderer renderer,
            ClothSkinningCoefficient[] coefficients,
            UMAClothProperties properties)
        {
            Cloth existing = renderer.GetComponent<Cloth>();
            if (coefficients == null || coefficients.Length == 0)
            {
                if (existing != null)
                {
                    DestroyImmediate(existing, false);
                }
                return;
            }

            if (existing != null)
            {
                DestroyImmediate(existing, false);
            }
            Cloth cloth = renderer.gameObject.AddComponent<Cloth>();
            UMAPhysicsAvatar physicsAvatar =
                data.GetComponentInParent<UMAPhysicsAvatar>();
            if (physicsAvatar != null)
            {
                cloth.sphereColliders =
                    physicsAvatar.SphereColliders.ToArray();
                cloth.capsuleColliders =
                    physicsAvatar.CapsuleColliders.ToArray();
            }
            cloth.coefficients = coefficients;
            properties?.ApplyValues(cloth);
        }

        private static void ClearStagingRenderer(
            SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh != null)
            {
                mesh.Clear();
                mesh.ClearBlendShapes();
                mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
            }
            renderer.localBounds = new Bounds(Vector3.zero, Vector3.zero);
            renderer.sharedMaterials = Array.Empty<Material>();
            renderer.bones = Array.Empty<Transform>();
        }

        private static void DestroyPreviousRenderers(
            SkinnedMeshRenderer[] previous,
            SkinnedMeshRenderer[] replacements)
        {
            if (previous == null)
            {
                return;
            }
            for (int i = 0; i < previous.Length; i++)
            {
                SkinnedMeshRenderer renderer = previous[i];
                if (renderer == null ||
                    Array.IndexOf(replacements, renderer) >= 0)
                {
                    continue;
                }

                Mesh mesh = renderer.sharedMesh;
                if (mesh != null)
                {
                    UMAUtils.DestroySceneObject(mesh);
                }
                UMAUtils.DestroySceneObject(renderer.gameObject);
            }
        }
    }

    internal sealed class UMAIncrementalMeshCombinePlan : IDisposable
    {
        public UMAData Data { get; }
        public int AtlasResolution { get; }
        public SkinnedMeshRenderer[] PreviousRenderers { get; }
        public UMARendererAsset[] RendererAssets { get; }
        public UMAIncrementalRendererPlan[] Renderers { get; }
        public Dictionary<string, float> BakedBlendshapes { get; }
        public Quaternion BoundsRotation { get; }
        public bool IsCommitted { get; private set; }

        private readonly List<UMAIncrementalSlotMetadataState>
            slotMetadata;
        private readonly List<UMAIncrementalMaterialMetadataState>
            materialMetadata;
        private readonly Bounds originalMeshBounds;
        private Bounds plannedOriginalMeshBounds;
        private bool disposed;
        private bool committedMetadataFinalized;

        public UMAIncrementalMeshCombinePlan(
            UMAData data,
            int atlasResolution,
            SkinnedMeshRenderer[] previousRenderers,
            UMARendererAsset[] rendererAssets,
            UMAIncrementalRendererPlan[] renderers,
            Dictionary<string, float> bakedBlendshapes,
            Quaternion boundsRotation)
        {
            Data = data;
            AtlasResolution = atlasResolution;
            PreviousRenderers = previousRenderers;
            RendererAssets = rendererAssets;
            Renderers = renderers;
            BakedBlendshapes = bakedBlendshapes;
            BoundsRotation = boundsRotation;
            slotMetadata = CaptureSlotMetadata(data);
            materialMetadata = CaptureMaterialMetadata(data);
            originalMeshBounds = data.originalMeshBounds;
            plannedOriginalMeshBounds = originalMeshBounds;
        }

        public void CaptureBuildMetadataAndRestore()
        {
            for (int i = 0; i < slotMetadata.Count; i++)
            {
                UMAIncrementalSlotMetadataState state = slotMetadata[i];
                state.PlannedUVArea = state.Slot.UVArea;
                state.PlannedUVAreaUpdateFrame =
                    state.Slot.uvAreaUpdateFrame;
            }
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                UMAIncrementalMaterialMetadataState state =
                    materialMetadata[i];
                state.PlannedMaterialIndex =
                    state.Material.materialIndex;
            }
            RestoreOriginalMetadata(false);
        }

        public void CaptureScheduledSlotMetadata(
            UMAIncrementalRendererPlan rendererPlan)
        {
            for (int sourceIndex = 0;
                 sourceIndex < rendererPlan.Sources.Length;
                 sourceIndex++)
            {
                SlotData slot =
                    rendererPlan.Sources[sourceIndex].slotData;
                UMAIncrementalSlotMetadataState state =
                    FindSlotState(slot);
                if (state == null)
                {
                    continue;
                }
                state.PlannedRenderer = slot.skinnedMeshRenderer;
                state.PlannedVertexOffset = slot.vertexOffset;
                slot.skinnedMeshRenderer = state.OriginalRenderer;
                slot.vertexOffset = state.OriginalVertexOffset;
            }
        }

        public void SetPlannedSubmesh(
            SlotData slot,
            int submeshIndex)
        {
            UMAIncrementalSlotMetadataState state =
                FindSlotState(slot);
            if (state != null)
            {
                state.PlannedSubmesh = submeshIndex;
            }
        }

        public void CaptureConfiguredMaterial(
            UMAData.GeneratedMaterial material)
        {
            UMAIncrementalMaterialMetadataState state =
                FindMaterialState(material);
            if (state == null)
            {
                return;
            }
            state.PlannedSecondPass =
                material.secondPassMaterial;
            material.secondPassMaterial =
                state.OriginalSecondPass;
        }

        public void SetPlannedMaterialRenderer(
            UMAData.GeneratedMaterial material,
            SkinnedMeshRenderer renderer)
        {
            UMAIncrementalMaterialMetadataState state =
                FindMaterialState(material);
            if (state != null)
            {
                state.PlannedRenderer = renderer;
            }
        }

        public void SetPlannedOriginalMeshBounds(Bounds bounds)
        {
            plannedOriginalMeshBounds = bounds;
        }

        public void ApplyPlannedMetadata()
        {
            for (int i = 0; i < slotMetadata.Count; i++)
            {
                UMAIncrementalSlotMetadataState state = slotMetadata[i];
                state.Slot.skinnedMeshRenderer =
                    state.PlannedRenderer;
                state.Slot.submeshIndex =
                    state.PlannedSubmesh;
                state.Slot.vertexOffset =
                    state.PlannedVertexOffset;
                state.Slot.UVArea = state.PlannedUVArea;
                state.Slot.uvAreaUpdateFrame =
                    state.PlannedUVAreaUpdateFrame;
            }
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                UMAIncrementalMaterialMetadataState state =
                    materialMetadata[i];
                state.Material.materialIndex =
                    state.PlannedMaterialIndex;
                state.Material.skinnedMeshRenderer =
                    state.PlannedRenderer;
                state.Material.secondPassMaterial =
                    state.PlannedSecondPass;
            }
            Data.originalMeshBounds =
                plannedOriginalMeshBounds;
        }

        public void MarkCommitted()
        {
            IsCommitted = true;
        }

        public void FinalizeCommittedMetadata()
        {
            if (committedMetadataFinalized)
            {
                return;
            }
            committedMetadataFinalized = true;
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                UMAIncrementalMaterialMetadataState state =
                    materialMetadata[i];
                if (state.OriginalSecondPass != null &&
                    state.OriginalSecondPass !=
                    state.PlannedSecondPass &&
                    state.OriginalSecondPass !=
                    state.Material.material)
                {
                    UMAUtils.DestroySceneObject(
                        state.OriginalSecondPass);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            Exception firstException = null;
            if (!IsCommitted)
            {
                try
                {
                    CaptureUnexpectedMaterialMutations();
                    RestoreOriginalMetadata(true);
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }
            }
            for (int i = 0; i < Renderers.Length; i++)
            {
                UMAIncrementalRendererPlan rendererPlan = Renderers[i];
                if (rendererPlan == null)
                {
                    continue;
                }
                try
                {
                    rendererPlan.DisposePending();
                }
                catch (Exception exception)
                {
                    if (firstException == null)
                    {
                        firstException = exception;
                    }
                }
                if (!IsCommitted)
                {
                    try
                    {
                        rendererPlan.DestroyStagingRenderer();
                    }
                    catch (Exception exception)
                    {
                        if (firstException == null)
                        {
                            firstException = exception;
                        }
                    }
                }
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }

        private void RestoreOriginalMetadata(
            bool destroyPlannedMaterials)
        {
            for (int i = 0; i < slotMetadata.Count; i++)
            {
                UMAIncrementalSlotMetadataState state = slotMetadata[i];
                state.Slot.skinnedMeshRenderer =
                    state.OriginalRenderer;
                state.Slot.submeshIndex =
                    state.OriginalSubmesh;
                state.Slot.vertexOffset =
                    state.OriginalVertexOffset;
                state.Slot.UVArea =
                    state.OriginalUVArea;
                state.Slot.uvAreaUpdateFrame =
                    state.OriginalUVAreaUpdateFrame;
            }
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                UMAIncrementalMaterialMetadataState state =
                    materialMetadata[i];
                state.Material.materialIndex =
                    state.OriginalMaterialIndex;
                state.Material.skinnedMeshRenderer =
                    state.OriginalRenderer;
                state.Material.secondPassMaterial =
                    state.OriginalSecondPass;

                if (destroyPlannedMaterials &&
                    state.PlannedSecondPass != null &&
                    state.PlannedSecondPass !=
                    state.OriginalSecondPass &&
                    state.PlannedSecondPass !=
                    state.Material.material)
                {
                    UMAUtils.DestroySceneObject(
                        state.PlannedSecondPass);
                    state.PlannedSecondPass =
                        state.OriginalSecondPass;
                }
            }
            Data.originalMeshBounds = originalMeshBounds;
        }

        private void CaptureUnexpectedMaterialMutations()
        {
            // Material configuration is a Unity-facing atomic unit and can
            // throw between creating a replacement second pass and the normal
            // capture point. Preserve that reference so rollback can destroy
            // it instead of leaking the staged material.
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                UMAIncrementalMaterialMetadataState state =
                    materialMetadata[i];
                Material current =
                    state.Material.secondPassMaterial;
                if (current != state.OriginalSecondPass)
                {
                    state.PlannedSecondPass = current;
                }
            }
        }

        private UMAIncrementalSlotMetadataState FindSlotState(
            SlotData slot)
        {
            for (int i = 0; i < slotMetadata.Count; i++)
            {
                if (ReferenceEquals(slotMetadata[i].Slot, slot))
                {
                    return slotMetadata[i];
                }
            }
            return null;
        }

        private UMAIncrementalMaterialMetadataState FindMaterialState(
            UMAData.GeneratedMaterial material)
        {
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                if (ReferenceEquals(
                        materialMetadata[i].Material,
                        material))
                {
                    return materialMetadata[i];
                }
            }
            return null;
        }

        private static List<UMAIncrementalSlotMetadataState>
            CaptureSlotMetadata(UMAData data)
        {
            var result =
                new List<UMAIncrementalSlotMetadataState>();
            SlotData[] recipeSlots =
                data.umaRecipe.slotDataList ?? Array.Empty<SlotData>();
            for (int i = 0; i < recipeSlots.Length; i++)
            {
                AddSlotState(result, recipeSlots[i]);
            }
            List<UMAData.GeneratedMaterial> materials =
                data.generatedMaterials.materials;
            for (int materialIndex = 0;
                 materialIndex < materials.Count;
                 materialIndex++)
            {
                List<UMAData.MaterialFragment> fragments =
                    materials[materialIndex]?.materialFragments;
                if (fragments == null)
                {
                    continue;
                }
                for (int fragmentIndex = 0;
                     fragmentIndex < fragments.Count;
                     fragmentIndex++)
                {
                    AddSlotState(
                        result,
                        fragments[fragmentIndex]?.slotData);
                }
            }
            return result;
        }

        private static void AddSlotState(
            List<UMAIncrementalSlotMetadataState> states,
            SlotData slot)
        {
            if (slot == null)
            {
                return;
            }
            for (int i = 0; i < states.Count; i++)
            {
                if (ReferenceEquals(states[i].Slot, slot))
                {
                    return;
                }
            }
            states.Add(
                new UMAIncrementalSlotMetadataState(slot));
        }

        private static List<UMAIncrementalMaterialMetadataState>
            CaptureMaterialMetadata(UMAData data)
        {
            var result =
                new List<UMAIncrementalMaterialMetadataState>();
            List<UMAData.GeneratedMaterial> materials =
                data.generatedMaterials.materials;
            for (int i = 0; i < materials.Count; i++)
            {
                UMAData.GeneratedMaterial material = materials[i];
                if (material != null)
                {
                    result.Add(
                        new UMAIncrementalMaterialMetadataState(
                            material));
                }
            }
            return result;
        }
    }

    internal sealed class UMAIncrementalSlotMetadataState
    {
        public SlotData Slot { get; }
        public int OriginalRenderer { get; }
        public int OriginalSubmesh { get; }
        public int OriginalVertexOffset { get; }
        public Rect OriginalUVArea { get; }
        public int OriginalUVAreaUpdateFrame { get; }
        public int PlannedRenderer;
        public int PlannedSubmesh;
        public int PlannedVertexOffset;
        public Rect PlannedUVArea;
        public int PlannedUVAreaUpdateFrame;

        public UMAIncrementalSlotMetadataState(SlotData slot)
        {
            Slot = slot;
            OriginalRenderer = slot.skinnedMeshRenderer;
            OriginalSubmesh = slot.submeshIndex;
            OriginalVertexOffset = slot.vertexOffset;
            OriginalUVArea = slot.UVArea;
            OriginalUVAreaUpdateFrame =
                slot.uvAreaUpdateFrame;
            PlannedRenderer = OriginalRenderer;
            PlannedSubmesh = OriginalSubmesh;
            PlannedVertexOffset = OriginalVertexOffset;
            PlannedUVArea = OriginalUVArea;
            PlannedUVAreaUpdateFrame =
                OriginalUVAreaUpdateFrame;
        }
    }

    internal sealed class UMAIncrementalMaterialMetadataState
    {
        public UMAData.GeneratedMaterial Material { get; }
        public int OriginalMaterialIndex { get; }
        public SkinnedMeshRenderer OriginalRenderer { get; }
        public Material OriginalSecondPass { get; }
        public int PlannedMaterialIndex;
        public SkinnedMeshRenderer PlannedRenderer;
        public Material PlannedSecondPass;

        public UMAIncrementalMaterialMetadataState(
            UMAData.GeneratedMaterial material)
        {
            Material = material;
            OriginalMaterialIndex = material.materialIndex;
            OriginalRenderer = material.skinnedMeshRenderer;
            OriginalSecondPass = material.secondPassMaterial;
            PlannedMaterialIndex = OriginalMaterialIndex;
            PlannedRenderer = OriginalRenderer;
            PlannedSecondPass = OriginalSecondPass;
        }
    }

    internal sealed class UMAIncrementalRendererPlan
    {
        public int RendererIndex { get; }
        public UMARendererAsset RendererAsset { get; }
        public SkinnedMeshRenderer StagingRenderer { get; }
        public UMAData.GeneratedMaterial[] Materials { get; }
        public SkinnedMeshCombiner.CombineInstance[] Sources { get; }
        public UMAClothProperties ClothProperties { get; }
        public bool IsEmpty => Sources.Length == 0;
        public SkinnedMeshCombinerMeshAPI.PendingCombine Pending { get; set; }
        public SkinnedMeshCombinerMeshAPI.IncrementalBlendShapeLoader
            BlendShapeLoader { get; set; }
        public ClothSkinningCoefficient[] PreparedCloth { get; set; }

        public UMAIncrementalRendererPlan(
            int rendererIndex,
            UMARendererAsset rendererAsset,
            SkinnedMeshRenderer stagingRenderer,
            UMAData.GeneratedMaterial[] materials,
            SkinnedMeshCombiner.CombineInstance[] sources,
            UMAClothProperties clothProperties)
        {
            RendererIndex = rendererIndex;
            RendererAsset = rendererAsset;
            StagingRenderer = stagingRenderer;
            Materials = materials;
            Sources = sources;
            ClothProperties = clothProperties;
        }

        public void DisposePending()
        {
            Exception firstException = null;
            try
            {
                BlendShapeLoader?.Dispose();
            }
            catch (Exception exception)
            {
                firstException = exception;
            }
            finally
            {
                BlendShapeLoader = null;
            }

            try
            {
                Pending?.Dispose();
            }
            catch (Exception exception)
            {
                if (firstException == null)
                {
                    firstException = exception;
                }
            }
            finally
            {
                Pending = null;
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }

        public void DestroyStagingRenderer()
        {
            if (StagingRenderer == null)
            {
                return;
            }
            Mesh mesh = StagingRenderer.sharedMesh;
            StagingRenderer.sharedMesh = null;
            if (mesh != null)
            {
                UMAUtils.DestroySceneObject(mesh);
            }
            UMAUtils.DestroySceneObject(StagingRenderer.gameObject);
        }
    }

    /// <summary>
    /// Per-avatar resumable state owned by
    /// <see cref="UMAIncrementalMeshCombiner"/>.
    /// </summary>
    public sealed class UMAIncrementalMeshCombineOperation :
        IUMAMeshCombineOperation
    {
        private static readonly ProfilerMarker BuildPlanMarker =
            new ProfilerMarker("UMA.IncrementalMesh.BuildPlan");
        private static readonly ProfilerMarker ScheduleRendererMarker =
            new ProfilerMarker("UMA.IncrementalMesh.ScheduleRenderer");
        private static readonly ProfilerMarker PollJobsMarker =
            new ProfilerMarker("UMA.IncrementalMesh.PollJobs");
        private static readonly ProfilerMarker ApplyBaseMeshMarker =
            new ProfilerMarker("UMA.IncrementalMesh.ApplyBaseMesh");
        private static readonly ProfilerMarker ApplyBlendShapeMarker =
            new ProfilerMarker("UMA.IncrementalMesh.AddBlendShapeFrame");
        private static readonly ProfilerMarker FinalizeRendererMarker =
            new ProfilerMarker("UMA.IncrementalMesh.FinalizeRenderer");
        private static readonly ProfilerMarker CommitMarker =
            new ProfilerMarker("UMA.IncrementalMesh.Commit");

        private enum OperationStage
        {
            BuildPlan,
            ScheduleRenderers,
            WaitForJobs,
            ApplyBaseMeshes,
            ApplyBlendShapes,
            FinalizeRenderers,
            Commit,
            Completed,
            Cancelled,
            Failed
        }

        private readonly UMAIncrementalMeshCombiner owner;
        private readonly bool updatedAtlas;
        private readonly UMAData data;
        private readonly int atlasResolution;
        private UMAIncrementalMeshCombinePlan plan;
        private OperationStage stage = OperationStage.BuildPlan;
        private int rendererCursor;
        private bool cancellationRequested;
        private bool disposed;

        internal UMAIncrementalMeshCombineOperation(
            UMAIncrementalMeshCombiner owner,
            bool updatedAtlas,
            UMAData data,
            int atlasResolution)
        {
            this.owner = owner;
            this.updatedAtlas = updatedAtlas;
            this.data = data;
            this.atlasResolution = atlasResolution;
        }

        public string StageName
        {
            get
            {
                if (stage == OperationStage.ApplyBlendShapes &&
                    plan?.Renderers != null &&
                    (uint)rendererCursor <
                    (uint)plan.Renderers.Length)
                {
                    SkinnedMeshCombinerMeshAPI
                        .IncrementalBlendShapeLoader loader =
                        plan.Renderers[rendererCursor]
                            .BlendShapeLoader;
                    if (loader != null && !loader.IsComplete)
                    {
                        return
                            $"BlendShapes Renderer {rendererCursor}: " +
                            $"{loader.CurrentShapeName} Frame {loader.CurrentFrameIndex}";
                    }
                }
                return stage.ToString();
            }
        }

        public float Progress
        {
            get
            {
                int rendererCount = plan?.Renderers?.Length ?? 0;
                switch (stage)
                {
                    case OperationStage.BuildPlan:
                        return 0f;
                    case OperationStage.ScheduleRenderers:
                        return rendererCount == 0
                            ? 0.25f
                            : 0.1f +
                              0.25f * rendererCursor / rendererCount;
                    case OperationStage.WaitForJobs:
                        return 0.45f;
                    case OperationStage.ApplyBaseMeshes:
                        return rendererCount == 0
                            ? 0.65f
                            : 0.5f +
                              0.15f * rendererCursor / rendererCount;
                    case OperationStage.ApplyBlendShapes:
                        return rendererCount == 0
                            ? 0.78f
                            : 0.66f +
                              0.12f * rendererCursor / rendererCount;
                    case OperationStage.FinalizeRenderers:
                        return rendererCount == 0
                            ? 0.85f
                            : 0.8f +
                              0.05f * rendererCursor / rendererCount;
                    case OperationStage.Commit:
                        return 0.9f;
                    case OperationStage.Completed:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }

        public bool HasPendingJobs
        {
            get
            {
                if (plan?.Renderers == null)
                {
                    return false;
                }
                for (int i = 0; i < plan.Renderers.Length; i++)
                {
                    SkinnedMeshCombinerMeshAPI.PendingCombine pending =
                        plan.Renderers[i]?.Pending;
                    if (pending != null && !pending.IsCompleted)
                    {
                        return true;
                    }
                    SkinnedMeshCombinerMeshAPI
                        .IncrementalBlendShapeLoader loader =
                        plan.Renderers[i]?.BlendShapeLoader;
                    if (loader != null &&
                        loader.HasPendingPreparation)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public UMAMeshCombineStatus Status
        {
            get
            {
                switch (stage)
                {
                    case OperationStage.WaitForJobs:
                        return HasPendingJobs
                            ? UMAMeshCombineStatus.WaitingForAsync
                            : UMAMeshCombineStatus.InProgress;
                    case OperationStage.ApplyBlendShapes:
                        return HasPendingJobs
                            ? UMAMeshCombineStatus.WaitingForAsync
                            : UMAMeshCombineStatus.InProgress;
                    case OperationStage.Completed:
                        return UMAMeshCombineStatus.Completed;
                    case OperationStage.Cancelled:
                        return UMAMeshCombineStatus.Cancelled;
                    case OperationStage.Failed:
                        return UMAMeshCombineStatus.Failed;
                    default:
                        return UMAMeshCombineStatus.InProgress;
                }
            }
        }

        public Exception Error { get; private set; }

        public UMAMeshCombineStepResult Step(
            UMAMeshCombineTimeSlice timeSlice)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(UMAIncrementalMeshCombineOperation));
            }
            if (stage == OperationStage.Completed)
            {
                return UMAMeshCombineStepResult.Completed();
            }
            if (stage == OperationStage.Cancelled)
            {
                return UMAMeshCombineStepResult.Cancelled();
            }
            if (stage == OperationStage.Failed)
            {
                return UMAMeshCombineStepResult.Failed(Error);
            }
            if (cancellationRequested)
            {
                if (HasPendingJobs)
                {
                    return UMAMeshCombineStepResult.WaitingForAsync();
                }
                stage = OperationStage.Cancelled;
                return UMAMeshCombineStepResult.Cancelled();
            }
            if (timeSlice.IsExpired)
            {
                return UMAMeshCombineStepResult.InProgress();
            }

            try
            {
                switch (stage)
                {
                    case OperationStage.BuildPlan:
                        using (BuildPlanMarker.Auto())
                        {
                            plan = owner.BuildPlan(
                                updatedAtlas,
                                data,
                                atlasResolution);
                        }
                        stage = OperationStage.ScheduleRenderers;
                        rendererCursor = 0;
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.ScheduleRenderers:
                        if (rendererCursor < plan.Renderers.Length)
                        {
                            using (ScheduleRendererMarker.Auto())
                            {
                                ScheduleRenderer(
                                    plan.Renderers[rendererCursor]);
                            }
                            rendererCursor++;
                        }
                        if (rendererCursor >= plan.Renderers.Length)
                        {
                            stage = OperationStage.WaitForJobs;
                        }
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.WaitForJobs:
                        using (PollJobsMarker.Auto())
                        {
                            if (HasPendingJobs)
                            {
                                return UMAMeshCombineStepResult.WaitingForAsync();
                            }
                        }
                        stage = OperationStage.ApplyBaseMeshes;
                        rendererCursor = 0;
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.ApplyBaseMeshes:
                        if (rendererCursor < plan.Renderers.Length)
                        {
                            using (ApplyBaseMeshMarker.Auto())
                            {
                                ApplyBaseMesh(
                                    plan.Renderers[rendererCursor]);
                            }
                            rendererCursor++;
                        }
                        if (rendererCursor >= plan.Renderers.Length)
                        {
                            stage = OperationStage.ApplyBlendShapes;
                            rendererCursor = 0;
                        }
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.ApplyBlendShapes:
                        if (rendererCursor < plan.Renderers.Length)
                        {
                            UMAMeshCombineStepResult blendShapeResult;
                            using (ApplyBlendShapeMarker.Auto())
                            {
                                blendShapeResult =
                                    ApplyBlendShapeFrame(
                                        plan.Renderers[rendererCursor]);
                            }
                            if (blendShapeResult.Status ==
                                UMAMeshCombineStatus.WaitingForAsync)
                            {
                                return blendShapeResult;
                            }
                            if (blendShapeResult.Status ==
                                UMAMeshCombineStatus.InProgress)
                            {
                                return blendShapeResult;
                            }
                            rendererCursor++;
                        }
                        if (rendererCursor >= plan.Renderers.Length)
                        {
                            stage = OperationStage.FinalizeRenderers;
                            rendererCursor = 0;
                        }
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.FinalizeRenderers:
                        if (rendererCursor < plan.Renderers.Length)
                        {
                            using (FinalizeRendererMarker.Auto())
                            {
                                FinalizeRenderer(
                                    plan.Renderers[rendererCursor]);
                            }
                            rendererCursor++;
                        }
                        if (rendererCursor >= plan.Renderers.Length)
                        {
                            stage = OperationStage.Commit;
                        }
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.Commit:
                        using (CommitMarker.Auto())
                        {
                            owner.Commit(plan);
                        }
                        stage = OperationStage.Completed;
                        return UMAMeshCombineStepResult.Completed();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported incremental mesh stage {stage}.");
                }
            }
            catch (Exception exception)
            {
                Error = exception;
                stage = OperationStage.Failed;
                return UMAMeshCombineStepResult.Failed(exception);
            }
        }

        public void Cancel()
        {
            if (cancellationRequested ||
                stage == OperationStage.Completed ||
                stage == OperationStage.Cancelled ||
                stage == OperationStage.Failed)
            {
                return;
            }
            cancellationRequested = true;
            if (plan?.Renderers != null)
            {
                for (int i = 0; i < plan.Renderers.Length; i++)
                {
                    plan.Renderers[i]?.BlendShapeLoader
                        ?.CancelPreparation();
                }
            }
        }

        internal void RunSynchronously()
        {
            while (true)
            {
                if (stage == OperationStage.WaitForJobs ||
                    stage == OperationStage.ApplyBlendShapes)
                {
                    CompleteOutstandingJobs();
                }

                UMAMeshCombineStepResult result =
                    Step(UMAMeshCombineTimeSlice.Unlimited);
                switch (result.Status)
                {
                    case UMAMeshCombineStatus.Completed:
                        return;
                    case UMAMeshCombineStatus.Failed:
                        throw result.Error ?? Error ??
                              new InvalidOperationException(
                                  "Incremental mesh generation failed without an error.");
                    case UMAMeshCombineStatus.Cancelled:
                        throw new OperationCanceledException(
                            "Incremental mesh generation was cancelled.");
                }
            }
        }

        private void ScheduleRenderer(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan.IsEmpty)
            {
                return;
            }
            rendererPlan.Pending =
                SkinnedMeshCombinerMeshAPI.PrepareIncrementalCombine(
                    new SkinnedMeshCombinerMeshAPI.RendererBatch
                    {
                        Renderer = rendererPlan.StagingRenderer,
                        Sources = rendererPlan.Sources,
                        CurrentRendererIndex =
                            rendererPlan.RendererIndex,
                        AtlasResolution = plan.AtlasResolution,
                        RendererAsset =
                            rendererPlan.RendererAsset,
                        HasRendererAssetOverride = true,
                        SkipSkeletonUpdate = false
                    },
                    plan.Data,
                    plan.BakedBlendshapes,
                    plan.Data.markDynamic,
                    false,
                    plan.BoundsRotation);
            rendererPlan.BlendShapeLoader =
                rendererPlan.Pending
                    .CreateIncrementalBlendShapeLoader();
            plan.CaptureScheduledSlotMetadata(rendererPlan);
        }

        private static void ApplyBaseMesh(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan.IsEmpty)
            {
                return;
            }
            if (rendererPlan.Pending == null ||
                !rendererPlan.Pending.IsCompleted)
            {
                throw new InvalidOperationException(
                    $"Renderer {rendererPlan.RendererIndex} was applied before its native jobs completed.");
            }

            rendererPlan.PreparedCloth =
                rendererPlan.Pending.ApplyPreparedBaseMesh(
                    rendererPlan.StagingRenderer.sharedMesh);
        }

        private static void FinalizeRenderer(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan.IsEmpty)
            {
                return;
            }

            rendererPlan.PreparedCloth =
                rendererPlan.Pending
                    .FinalizePreparedRendererWithoutBlendShapes(
                    rendererPlan.StagingRenderer.sharedMesh);
            rendererPlan.DisposePending();
        }

        private static UMAMeshCombineStepResult ApplyBlendShapeFrame(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan.IsEmpty ||
                rendererPlan.BlendShapeLoader == null ||
                rendererPlan.BlendShapeLoader.IsComplete)
            {
                return UMAMeshCombineStepResult.Completed();
            }
            return rendererPlan.BlendShapeLoader.Step(
                rendererPlan.StagingRenderer.sharedMesh);
        }

        private void CompleteOutstandingJobs()
        {
            if (plan?.Renderers == null)
            {
                return;
            }
            for (int i = 0; i < plan.Renderers.Length; i++)
            {
                plan.Renderers[i]?.Pending?.CompleteJobs();
                plan.Renderers[i]?.BlendShapeLoader
                    ?.CompletePreparation();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            plan?.Dispose();
            plan = null;
        }
    }
}
