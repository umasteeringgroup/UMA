using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            ValidateBuildPlanInputs(data);
            UpdateBuildPlanMeshHideMasks(data);
            EnsureBuildPlanSkeleton(data);
            BuildPlanActiveModifiers(data);
            UMAIncrementalMeshCombinePlan plan =
                CreateBuildPlan(data, atlasResolution);
            try
            {
                for (int rendererIndex = 0;
                     rendererIndex < plan.Renderers.Length;
                     rendererIndex++)
                {
                    BuildRendererPlan(
                        plan,
                        rendererIndex,
                        updatedAtlas);
                }
                EnsureBuildPlanRendererSkeleton(plan);
                CaptureBuildPlanMetadata(plan);
                return plan;
            }
            catch
            {
                plan.Dispose();
                throw;
            }
        }

        internal static void ValidateBuildPlanInputs(UMAData data)
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
        }

        internal static void UpdateBuildPlanMeshHideMasks(UMAData data)
        {
            data.umaRecipe.UpdateMeshHideMasks(data.currentLODLevel);
        }

        internal static void EnsureBuildPlanSkeleton(UMAData data)
        {
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
        }

        internal static void BuildPlanActiveModifiers(UMAData data)
        {
            data.BuildActiveModifiers();
        }

        internal static UMAIncrementalMeshCombinePlan CreateBuildPlan(
            UMAData data,
            int atlasResolution)
        {
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
            return plan;
        }

        internal void BuildRendererPlan(
            UMAIncrementalMeshCombinePlan plan,
            int rendererIndex,
            bool updatedAtlas)
        {
            UMAData data = plan.Data;
            UMARendererAsset rendererAsset =
                plan.RendererAssets[rendererIndex];
            var stagingRenderer = CreateStagingRenderer(
                data,
                rendererIndex,
                rendererAsset);
            try
            {
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
                    SetSlotUVAreas(materials, plan.AtlasResolution);
                }

                plan.Renderers[rendererIndex] =
                    new UMAIncrementalRendererPlan(
                        rendererIndex,
                        rendererAsset,
                        stagingRenderer,
                        materials,
                        sources,
                        clothProperties);
                plan.CaptureBuiltRendererMetadataAndRestore(
                    plan.Renderers[rendererIndex]);
            }
            catch
            {
                Mesh mesh = stagingRenderer.sharedMesh;
                stagingRenderer.sharedMesh = null;
                if (mesh != null)
                {
                    UMAUtils.DestroySceneObject(mesh);
                }
                UMAUtils.DestroySceneObject(stagingRenderer.gameObject);
                throw;
            }
        }

        internal static void EnsureBuildPlanRendererSkeleton(
            UMAIncrementalMeshCombinePlan plan)
        {
            EnsureIncrementalSkeleton(plan.Data, plan.Renderers);
        }

        internal static void CaptureBuildPlanMetadata(
            UMAIncrementalMeshCombinePlan plan)
        {
            plan.CaptureBuildMetadataAndRestore();
        }

        private static void EnsureIncrementalSkeleton(
            UMAData data,
            UMAIncrementalRendererPlan[] rendererPlans)
        {
            var batches =
                new SkinnedMeshCombinerMeshAPI.RendererBatch[
                    rendererPlans.Length];
            for (int i = 0; i < rendererPlans.Length; i++)
            {
                UMAIncrementalRendererPlan rendererPlan =
                    rendererPlans[i];
                batches[i] =
                    new SkinnedMeshCombinerMeshAPI.RendererBatch
                    {
                        Renderer = rendererPlan.StagingRenderer,
                        Sources = rendererPlan.Sources,
                        CurrentRendererIndex =
                            rendererPlan.RendererIndex,
                        RendererAsset = rendererPlan.RendererAsset,
                        HasRendererAssetOverride = true,
                        SkipSkeletonUpdate = true
                    };
            }
            SkinnedMeshCombinerMeshAPI
                .EnsureIncrementalBatchSkeleton(batches, data);
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

            for (int i = 0; i < plan.Renderers.Length; i++)
            {
                UMAIncrementalRendererPlan rendererPlan = plan.Renderers[i];
                for (int materialIndex = 0;
                     materialIndex < rendererPlan.Materials.Length;
                     materialIndex++)
                {
                    // Material assignment is deliberately performed before
                    // commit so it can be budgeted per renderer. Revalidate
                    // the source references here so an asset unload or
                    // mutation between preparation and publication still
                    // rolls the complete transaction back.
                    ValidateGeneratedMaterial(
                        rendererPlan.Materials[materialIndex],
                        materialIndex);
                }
                committedRenderers[i] =
                    rendererPlan.StagingRenderer;
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

        internal void ConfigureStagingRendererMaterials(
            UMAIncrementalMeshCombinePlan plan,
            UMAIncrementalRendererPlan rendererPlan)
        {
            SkinnedMeshRenderer renderer =
                rendererPlan.StagingRenderer;
            int rendererIndex = rendererPlan.RendererIndex;

            if (rendererPlan.IsEmpty)
            {
                ClearStagingRenderer(renderer);
                if (rendererIndex == 0)
                {
                    plan.SetPlannedOriginalMeshBounds(
                        new Bounds(Vector3.zero, Vector3.zero));
                }
            }
            else
            {
                AssignRendererMaterials(
                    plan.Data,
                    plan,
                    renderer,
                    rendererPlan.Materials);
                if (rendererIndex == 0 &&
                    renderer.sharedMesh != null)
                {
                    plan.SetPlannedOriginalMeshBounds(
                        renderer.sharedMesh.bounds);
                }
            }

            for (int materialIndex = 0;
                 materialIndex < rendererPlan.Materials.Length;
                 materialIndex++)
            {
                plan.SetPlannedMaterialRenderer(
                    rendererPlan.Materials[materialIndex],
                    rendererPlan.IsEmpty ? null : renderer);
            }
        }

        internal void ConfigureStagingRendererClothAndHierarchy(
            UMAIncrementalMeshCombinePlan plan,
            UMAIncrementalRendererPlan rendererPlan)
        {
            UMAData data = plan.Data;
            SkinnedMeshRenderer renderer =
                rendererPlan.StagingRenderer;
            int rendererIndex = rendererPlan.RendererIndex;
            if (!rendererPlan.IsEmpty)
            {
                SetupCloth(
                    data,
                    renderer,
                    rendererPlan.PreparedCloth,
                    rendererPlan.ClothProperties);
            }

            Transform rendererTransform = renderer.transform;
            rendererTransform.SetParent(data.transform, false);
            rendererTransform.localPosition = Vector3.zero;
            rendererTransform.localRotation = Quaternion.identity;
            rendererTransform.localScale = Vector3.one;
            renderer.gameObject.name =
                rendererIndex == 0
                    ? "UMARenderer"
                    : $"UMARenderer {rendererIndex}";
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
        private readonly Dictionary<
            SlotData,
            UMAIncrementalSlotMetadataState> slotMetadataByReference;
        private readonly Dictionary<
            UMAData.GeneratedMaterial,
            UMAIncrementalMaterialMetadataState>
            materialMetadataByReference;
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
            slotMetadataByReference =
                new Dictionary<
                    SlotData,
                    UMAIncrementalSlotMetadataState>(
                    ReferenceComparer<SlotData>.Instance);
            for (int i = 0; i < slotMetadata.Count; i++)
            {
                slotMetadataByReference.Add(
                    slotMetadata[i].Slot,
                    slotMetadata[i]);
            }
            materialMetadataByReference =
                new Dictionary<
                    UMAData.GeneratedMaterial,
                    UMAIncrementalMaterialMetadataState>(
                    ReferenceComparer<
                        UMAData.GeneratedMaterial>.Instance);
            for (int i = 0; i < materialMetadata.Count; i++)
            {
                UMAIncrementalMaterialMetadataState state =
                    materialMetadata[i];
                if (!materialMetadataByReference.ContainsKey(
                        state.Material))
                {
                    materialMetadataByReference.Add(
                        state.Material,
                        state);
                }
            }
            originalMeshBounds = data.originalMeshBounds;
            plannedOriginalMeshBounds = originalMeshBounds;
        }

        public void CaptureBuildMetadataAndRestore()
        {
            RestoreOriginalMetadata(false);
        }

        public void CaptureBuiltRendererMetadataAndRestore(
            UMAIncrementalRendererPlan rendererPlan)
        {
            for (int i = 0;
                 i < rendererPlan.Sources.Length;
                 i++)
            {
                SlotData slot =
                    rendererPlan.Sources[i].slotData;
                UMAIncrementalSlotMetadataState state =
                    FindSlotState(slot);
                if (state == null)
                {
                    continue;
                }
                state.PlannedUVArea = slot.UVArea;
                state.PlannedUVAreaUpdateFrame =
                    slot.uvAreaUpdateFrame;
                slot.UVArea = state.OriginalUVArea;
                slot.uvAreaUpdateFrame =
                    state.OriginalUVAreaUpdateFrame;
            }
            for (int i = 0;
                 i < rendererPlan.Materials.Length;
                 i++)
            {
                UMAData.GeneratedMaterial material =
                    rendererPlan.Materials[i];
                UMAIncrementalMaterialMetadataState state =
                    FindMaterialState(material);
                if (state == null)
                {
                    continue;
                }
                state.PlannedMaterialIndex =
                    material.materialIndex;
                material.materialIndex =
                    state.OriginalMaterialIndex;
            }
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

        public void CaptureScheduledSlotMetadata(
            UMAIncrementalRendererPlan rendererPlan,
            int[] sourceVertexOffsets)
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
                state.PlannedRenderer =
                    rendererPlan.RendererIndex;
                state.PlannedVertexOffset =
                    sourceVertexOffsets[sourceIndex];
                slot.skinnedMeshRenderer =
                    state.OriginalRenderer;
                slot.vertexOffset =
                    state.OriginalVertexOffset;
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
            if (slot == null)
            {
                return null;
            }
            slotMetadataByReference.TryGetValue(
                slot,
                out UMAIncrementalSlotMetadataState state);
            return state;
        }

        private UMAIncrementalMaterialMetadataState FindMaterialState(
            UMAData.GeneratedMaterial material)
        {
            if (material == null)
            {
                return null;
            }
            materialMetadataByReference.TryGetValue(
                material,
                out UMAIncrementalMaterialMetadataState state);
            return state;
        }

        private static List<UMAIncrementalSlotMetadataState>
            CaptureSlotMetadata(UMAData data)
        {
            var result =
                new List<UMAIncrementalSlotMetadataState>();
            var captured =
                new HashSet<SlotData>(
                    ReferenceComparer<SlotData>.Instance);
            SlotData[] recipeSlots =
                data.umaRecipe.slotDataList ?? Array.Empty<SlotData>();
            for (int i = 0; i < recipeSlots.Length; i++)
            {
                AddSlotState(
                    result,
                    captured,
                    recipeSlots[i]);
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
                        captured,
                        fragments[fragmentIndex]?.slotData);
                }
            }
            return result;
        }

        private static void AddSlotState(
            List<UMAIncrementalSlotMetadataState> states,
            HashSet<SlotData> captured,
            SlotData slot)
        {
            if (slot == null || !captured.Add(slot))
            {
                return;
            }
            states.Add(
                new UMAIncrementalSlotMetadataState(slot));
        }

        private sealed class ReferenceComparer<T> :
            IEqualityComparer<T>
            where T : class
        {
            public static readonly ReferenceComparer<T> Instance =
                new ReferenceComparer<T>();

            public bool Equals(T left, T right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(T value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
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
        public SkinnedMeshCombinerMeshAPI
            .IncrementalCombinePreparation Preparation { get; set; }
        public SkinnedMeshCombinerMeshAPI
            .RendererPreparationTimingSnapshot PreparationTimingBefore
            { get; set; }
        public SkinnedMeshCombinerMeshAPI.IncrementalBlendShapeLoader
            BlendShapeLoader { get; set; }
        public ClothSkinningCoefficient[] PreparedCloth { get; set; }
        public bool BaseMeshApplied { get; set; }
        internal UMAIncrementalBaseMeshApplicationStage
            BaseMeshApplicationStage { get; set; }
        public bool RendererFinalized { get; set; }
        internal UMAIncrementalRendererFinalizationStage
            FinalizationStage { get; set; }
        internal UMAIncrementalRendererSchedulingStage
            SchedulingStage { get; set; }

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
                Preparation?.Dispose();
            }
            catch (Exception exception)
            {
                firstException = exception;
            }
            finally
            {
                Preparation = null;
            }

            try
            {
                BlendShapeLoader?.Dispose();
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

    internal enum UMAIncrementalRendererFinalizationStage
    {
        NativeMesh,
        Materials,
        ClothAndHierarchy,
        Completed
    }

    internal enum UMAIncrementalBaseMeshApplicationStage
    {
        PrepareOutput,
        ApplyWritableMeshData,
        ApplySkinning,
        Completed
    }

    internal enum UMAIncrementalRendererSchedulingStage
    {
        PrepareMesh,
        CreateBlendShapeLoader,
        Completed
    }

    /// <summary>
    /// Per-avatar resumable state owned by
    /// <see cref="UMAIncrementalMeshCombiner"/>.
    /// </summary>
    public sealed class UMAIncrementalMeshCombineOperation :
        IUMAMeshCombineOperation,
        IUMAMeshCombineOperationDiagnostics
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
            ProcessRenderers,
            Commit,
            Completed,
            Cancelled,
            Failed
        }

        private enum BuildPlanStage
        {
            ValidateInputs,
            UpdateMeshHideMasks,
            EnsureSkeleton,
            BuildActiveModifiers,
            CreatePlan,
            BuildRenderers,
            EnsureRendererSkeleton,
            CaptureMetadata,
            Completed
        }

        private readonly UMAIncrementalMeshCombiner owner;
        private readonly bool updatedAtlas;
        private readonly UMAData data;
        private readonly int atlasResolution;
        private UMAIncrementalMeshCombinePlan plan;
        private readonly Queue<UMAMeshCombineStepTiming>
            completedTimings =
                new Queue<UMAMeshCombineStepTiming>();
        private OperationStage stage = OperationStage.BuildPlan;
        private BuildPlanStage buildPlanStage =
            BuildPlanStage.ValidateInputs;
        private int buildPlanRendererCursor;
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
                if (stage == OperationStage.ScheduleRenderers &&
                    plan?.Renderers != null &&
                    rendererCursor < plan.Renderers.Length)
                {
                    return
                        $"Renderer {rendererCursor}: " +
                        GetRendererSchedulingStepName(
                            plan.Renderers[rendererCursor]);
                }
                if (stage == OperationStage.ProcessRenderers &&
                    plan?.Renderers != null &&
                    plan.Renderers.Length > 0)
                {
                    for (int i = 0;
                         i < plan.Renderers.Length;
                         i++)
                    {
                        UMAIncrementalRendererPlan rendererPlan =
                            plan.Renderers[i];
                        SkinnedMeshCombinerMeshAPI
                            .IncrementalBlendShapeLoader loader =
                            rendererPlan.BlendShapeLoader;
                        if (rendererPlan.BaseMeshApplied &&
                            loader != null &&
                            !loader.IsComplete)
                        {
                            return
                                $"BlendShapes Renderer {i}: " +
                                $"{loader.CurrentShapeName} Frame {loader.CurrentFrameIndex}";
                        }
                        if (rendererPlan.BaseMeshApplied &&
                            !rendererPlan.RendererFinalized &&
                            (loader == null || loader.IsComplete))
                        {
                            return
                                $"Finalize Renderer {i}: " +
                                rendererPlan.FinalizationStage;
                        }
                    }
                }
                return stage.ToString();
            }
        }

        public string AtomicStepName
        {
            get
            {
                switch (stage)
                {
                    case OperationStage.BuildPlan:
                        return GetBuildPlanAtomicStepName();
                    case OperationStage.ScheduleRenderers:
                        return plan?.Renderers != null &&
                               rendererCursor < plan.Renderers.Length
                            ? GetRendererSchedulingStepName(
                                plan.Renderers[rendererCursor])
                            : "Complete Renderer Scheduling";
                    case OperationStage.ProcessRenderers:
                        return GetRendererAtomicStepName();
                    case OperationStage.Commit:
                        return "Commit Mesh";
                    case OperationStage.Completed:
                        return "Completed";
                    case OperationStage.Cancelled:
                        return "Cancelled";
                    case OperationStage.Failed:
                        return "Failed";
                    default:
                        return stage.ToString();
                }
            }
        }

        private string GetBuildPlanAtomicStepName()
        {
            switch (buildPlanStage)
            {
                case BuildPlanStage.ValidateInputs:
                    return "Build Plan: Validate Inputs";
                case BuildPlanStage.UpdateMeshHideMasks:
                    return "Build Plan: Mesh Hide Masks";
                case BuildPlanStage.EnsureSkeleton:
                    return "Build Plan: Ensure Skeleton";
                case BuildPlanStage.BuildActiveModifiers:
                    return "Build Plan: Active Modifiers";
                case BuildPlanStage.CreatePlan:
                    return "Build Plan: Create State";
                case BuildPlanStage.BuildRenderers:
                    return plan != null
                        ? $"Build Plan: Renderer {buildPlanRendererCursor}"
                        : "Build Plan: Renderer";
                case BuildPlanStage.EnsureRendererSkeleton:
                    return "Build Plan: Renderer Skeleton";
                case BuildPlanStage.CaptureMetadata:
                    return "Build Plan: Capture Metadata";
                case BuildPlanStage.Completed:
                    return "Build Plan: Complete";
                default:
                    return "Build Plan";
            }
        }

        private static string GetRendererSchedulingStepName(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan == null)
            {
                return "Prepare Renderer";
            }
            switch (rendererPlan.SchedulingStage)
            {
                case UMAIncrementalRendererSchedulingStage.PrepareMesh:
                    return rendererPlan.Preparation != null
                        ? rendererPlan.Preparation.AtomicStepName
                        : "Prepare Renderer: Begin";
                case UMAIncrementalRendererSchedulingStage
                    .CreateBlendShapeLoader:
                    return "Create BlendShape Loader";
                case UMAIncrementalRendererSchedulingStage.Completed:
                    return "Complete Renderer Scheduling";
                default:
                    return rendererPlan.SchedulingStage.ToString();
            }
        }

        private string GetRendererAtomicStepName()
        {
            UMAIncrementalRendererPlan[] renderers = plan?.Renderers;
            if (renderers == null || renderers.Length == 0)
            {
                return "Advance to Commit";
            }

            for (int offset = 0; offset < renderers.Length; offset++)
            {
                int index = (rendererCursor + offset) % renderers.Length;
                UMAIncrementalRendererPlan rendererPlan = renderers[index];
                if (rendererPlan.BaseMeshApplied)
                {
                    continue;
                }
                if (rendererPlan.IsEmpty ||
                    (rendererPlan.Pending != null &&
                     rendererPlan.Pending.IsCompleted))
                {
                    return GetBaseMeshApplicationStepName(
                        rendererPlan);
                }
            }

            for (int offset = 0; offset < renderers.Length; offset++)
            {
                int index = (rendererCursor + offset) % renderers.Length;
                UMAIncrementalRendererPlan rendererPlan = renderers[index];
                SkinnedMeshCombinerMeshAPI.IncrementalBlendShapeLoader loader =
                    rendererPlan.BlendShapeLoader;
                if (rendererPlan.BaseMeshApplied &&
                    loader != null &&
                    !loader.IsComplete &&
                    !loader.HasPendingPreparation)
                {
                    return loader.IsInitialized
                        ? "Add BlendShape Frame"
                        : "Complete BlendShape Worker Preparation";
                }
            }

            for (int offset = 0; offset < renderers.Length; offset++)
            {
                int index = (rendererCursor + offset) % renderers.Length;
                UMAIncrementalRendererPlan rendererPlan = renderers[index];
                SkinnedMeshCombinerMeshAPI.IncrementalBlendShapeLoader loader =
                    rendererPlan.BlendShapeLoader;
                if (rendererPlan.BaseMeshApplied &&
                    !rendererPlan.RendererFinalized &&
                    (loader == null || loader.IsComplete))
                {
                    return "Finalize Renderer: " +
                           rendererPlan.FinalizationStage;
                }
            }

            return HasPendingJobs
                ? "Poll Renderer Jobs"
                : "Advance Renderer Pipeline";
        }

        private static string GetBaseMeshApplicationStepName(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan.IsEmpty)
            {
                return "Apply Base Mesh: Empty Renderer";
            }
            switch (rendererPlan.BaseMeshApplicationStage)
            {
                case UMAIncrementalBaseMeshApplicationStage
                    .PrepareOutput:
                    return "Apply Base Mesh: Prepare Output";
                case UMAIncrementalBaseMeshApplicationStage
                    .ApplyWritableMeshData:
                    return "Apply Base Mesh: Writable MeshData";
                case UMAIncrementalBaseMeshApplicationStage
                    .ApplySkinning:
                    return "Apply Base Mesh: Skinning";
                case UMAIncrementalBaseMeshApplicationStage.Completed:
                    return "Apply Base Mesh: Complete";
                default:
                    return "Apply Base Mesh";
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
                    case OperationStage.ProcessRenderers:
                        return 0.35f +
                               0.55f *
                               GetRendererPipelineProgress();
                    case OperationStage.Commit:
                        return 0.9f;
                    case OperationStage.Completed:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }

        private float GetRendererPipelineProgress()
        {
            if (plan?.Renderers == null ||
                plan.Renderers.Length == 0)
            {
                return 1f;
            }

            double completedUnits = 0d;
            for (int i = 0; i < plan.Renderers.Length; i++)
            {
                UMAIncrementalRendererPlan rendererPlan =
                    plan.Renderers[i];
                if (rendererPlan.BaseMeshApplied)
                {
                    completedUnits += 1d;
                }

                SkinnedMeshCombinerMeshAPI
                    .IncrementalBlendShapeLoader loader =
                    rendererPlan.BlendShapeLoader;
                if (loader == null || loader.IsComplete)
                {
                    completedUnits += 1d;
                }
                else if (loader.TotalFrameCount > 0)
                {
                    completedUnits +=
                        (double)loader.AppliedFrameCount /
                        loader.TotalFrameCount;
                }

                if (rendererPlan.RendererFinalized)
                {
                    completedUnits += 1d;
                }
            }
            return (float)(completedUnits /
                           (plan.Renderers.Length * 3d));
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
                        .IncrementalCombinePreparation preparation =
                            plan.Renderers[i]?.Preparation;
                    if (preparation != null &&
                        preparation.HasPendingJobs)
                    {
                        return true;
                    }
                    SkinnedMeshCombinerMeshAPI
                        .IncrementalBlendShapeLoader loader =
                        plan.Renderers[i]?.BlendShapeLoader;
                    if (loader != null &&
                        loader.HasOutstandingPreparation)
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

        public bool TryDequeueCompletedTiming(
            out UMAMeshCombineStepTiming timing)
        {
            if (completedTimings.Count == 0)
            {
                timing = default;
                return false;
            }
            timing = completedTimings.Dequeue();
            return true;
        }

        public UMAMeshCombineStepResult Step(
            UMAMeshCombineTimeSlice timeSlice)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(UMAIncrementalMeshCombineOperation));
            }
            CaptureCompletedWorkerTimings();
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
                            AdvanceBuildPlan();
                        }
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.ScheduleRenderers:
                        if (rendererCursor < plan.Renderers.Length)
                        {
                            using (ScheduleRendererMarker.Auto())
                            {
                                if (AdvanceRendererScheduling(
                                        plan.Renderers[rendererCursor]))
                                {
                                    rendererCursor++;
                                }
                            }
                        }
                        if (rendererCursor >= plan.Renderers.Length)
                        {
                            stage = OperationStage.ProcessRenderers;
                            rendererCursor = 0;
                        }
                        return UMAMeshCombineStepResult.InProgress();

                    case OperationStage.ProcessRenderers:
                        return AdvanceRendererPipeline();

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

        private void AdvanceBuildPlan()
        {
            switch (buildPlanStage)
            {
                case BuildPlanStage.ValidateInputs:
                    UMAIncrementalMeshCombiner
                        .ValidateBuildPlanInputs(data);
                    buildPlanStage =
                        BuildPlanStage.UpdateMeshHideMasks;
                    return;

                case BuildPlanStage.UpdateMeshHideMasks:
                    UMAIncrementalMeshCombiner
                        .UpdateBuildPlanMeshHideMasks(data);
                    buildPlanStage =
                        BuildPlanStage.EnsureSkeleton;
                    return;

                case BuildPlanStage.EnsureSkeleton:
                    UMAIncrementalMeshCombiner
                        .EnsureBuildPlanSkeleton(data);
                    buildPlanStage =
                        BuildPlanStage.BuildActiveModifiers;
                    return;

                case BuildPlanStage.BuildActiveModifiers:
                    UMAIncrementalMeshCombiner
                        .BuildPlanActiveModifiers(data);
                    buildPlanStage =
                        BuildPlanStage.CreatePlan;
                    return;

                case BuildPlanStage.CreatePlan:
                    plan = UMAIncrementalMeshCombiner
                        .CreateBuildPlan(data, atlasResolution);
                    buildPlanRendererCursor = 0;
                    buildPlanStage =
                        plan.Renderers.Length > 0
                            ? BuildPlanStage.BuildRenderers
                            : BuildPlanStage.EnsureRendererSkeleton;
                    return;

                case BuildPlanStage.BuildRenderers:
                    owner.BuildRendererPlan(
                        plan,
                        buildPlanRendererCursor,
                        updatedAtlas);
                    buildPlanRendererCursor++;
                    if (buildPlanRendererCursor >=
                        plan.Renderers.Length)
                    {
                        buildPlanStage =
                            BuildPlanStage.EnsureRendererSkeleton;
                    }
                    return;

                case BuildPlanStage.EnsureRendererSkeleton:
                    UMAIncrementalMeshCombiner
                        .EnsureBuildPlanRendererSkeleton(plan);
                    buildPlanStage =
                        BuildPlanStage.CaptureMetadata;
                    return;

                case BuildPlanStage.CaptureMetadata:
                    UMAIncrementalMeshCombiner
                        .CaptureBuildPlanMetadata(plan);
                    buildPlanStage = BuildPlanStage.Completed;
                    return;

                case BuildPlanStage.Completed:
                    stage = OperationStage.ScheduleRenderers;
                    rendererCursor = 0;
                    return;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported build-plan stage {buildPlanStage}.");
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

        private UMAMeshCombineStepResult AdvanceRendererPipeline()
        {
            UMAIncrementalRendererPlan[] renderers =
                plan.Renderers;
            if (renderers.Length == 0)
            {
                stage = OperationStage.Commit;
                return UMAMeshCombineStepResult.InProgress();
            }

            // Apply any completed base mesh first. This lets the main thread
            // begin consuming renderer A while renderer B's jobs continue.
            using (PollJobsMarker.Auto())
            {
                for (int offset = 0;
                     offset < renderers.Length;
                     offset++)
                {
                    int index =
                        (rendererCursor + offset) %
                        renderers.Length;
                    UMAIncrementalRendererPlan rendererPlan =
                        renderers[index];
                    if (rendererPlan.BaseMeshApplied)
                    {
                        continue;
                    }
                    if (!rendererPlan.IsEmpty &&
                        rendererPlan.Pending == null)
                    {
                        throw new InvalidOperationException(
                            $"Renderer {rendererPlan.RendererIndex} has no pending mesh build after scheduling completed.");
                    }
                    if (!rendererPlan.IsEmpty &&
                        !rendererPlan.Pending.IsCompleted)
                    {
                        continue;
                    }

                    using (ApplyBaseMeshMarker.Auto())
                    {
                        AdvanceBaseMeshApplication(
                            rendererPlan);
                    }
                    if (rendererPlan
                            .BaseMeshApplicationStage ==
                        UMAIncrementalBaseMeshApplicationStage
                            .Completed)
                    {
                        rendererPlan.BaseMeshApplied = true;
                    }
                    rendererCursor =
                        (index + 1) % renderers.Length;
                    return UMAMeshCombineStepResult.InProgress();
                }
            }

            // Consume one ready blendshape frame from any renderer instead of
            // stalling behind the first renderer whose worker is not ready.
            for (int offset = 0;
                 offset < renderers.Length;
                 offset++)
            {
                int index =
                    (rendererCursor + offset) %
                    renderers.Length;
                UMAIncrementalRendererPlan rendererPlan =
                    renderers[index];
                SkinnedMeshCombinerMeshAPI
                    .IncrementalBlendShapeLoader loader =
                    rendererPlan.BlendShapeLoader;
                if (!rendererPlan.BaseMeshApplied ||
                    loader == null ||
                    loader.IsComplete ||
                    loader.HasPendingPreparation)
                {
                    continue;
                }

                UMAMeshCombineStepResult result;
                using (ApplyBlendShapeMarker.Auto())
                {
                    result =
                        ApplyBlendShapeFrame(rendererPlan);
                }
                rendererCursor =
                    (index + 1) % renderers.Length;
                return result.Status ==
                       UMAMeshCombineStatus.Completed
                    ? UMAMeshCombineStepResult.InProgress()
                    : result;
            }

            // Finalization remains a main-thread atomic unit, but is now
            // budgeted independently per detached renderer.
            for (int offset = 0;
                 offset < renderers.Length;
                 offset++)
            {
                int index =
                    (rendererCursor + offset) %
                    renderers.Length;
                UMAIncrementalRendererPlan rendererPlan =
                    renderers[index];
                SkinnedMeshCombinerMeshAPI
                    .IncrementalBlendShapeLoader loader =
                    rendererPlan.BlendShapeLoader;
                if (!rendererPlan.BaseMeshApplied ||
                    rendererPlan.RendererFinalized ||
                    (loader != null && !loader.IsComplete))
                {
                    continue;
                }

                using (FinalizeRendererMarker.Auto())
                {
                    if (FinalizeRenderer(rendererPlan))
                    {
                        rendererPlan.RendererFinalized = true;
                    }
                }
                rendererCursor =
                    (index + 1) % renderers.Length;
                return UMAMeshCombineStepResult.InProgress();
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].RendererFinalized)
                {
                    return HasPendingJobs
                        ? UMAMeshCombineStepResult
                            .WaitingForAsync()
                        : UMAMeshCombineStepResult
                            .InProgress();
                }
            }

            stage = OperationStage.Commit;
            return UMAMeshCombineStepResult.InProgress();
        }

        private void CaptureCompletedWorkerTimings()
        {
            if (plan?.Renderers == null)
            {
                return;
            }
            for (int i = 0; i < plan.Renderers.Length; i++)
            {
                SkinnedMeshCombinerMeshAPI.IncrementalBlendShapeLoader
                    loader = plan.Renderers[i]?.BlendShapeLoader;
                if (loader != null &&
                    loader.TryConsumeInitializationTicks(
                        out long elapsedTicks))
                {
                    completedTimings.Enqueue(
                        new UMAMeshCombineStepTiming(
                            "BlendShape Plan and Buffer Preparation (Worker)",
                            elapsedTicks));
                }
            }
        }

        internal void RunSynchronously()
        {
            while (true)
            {
                if (stage == OperationStage.ProcessRenderers)
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

        private bool AdvanceRendererScheduling(
            UMAIncrementalRendererPlan rendererPlan)
        {
            switch (rendererPlan.SchedulingStage)
            {
                case UMAIncrementalRendererSchedulingStage.PrepareMesh:
                    if (!rendererPlan.IsEmpty)
                    {
                        if (rendererPlan.Preparation == null)
                        {
                            rendererPlan.PreparationTimingBefore =
                                SkinnedMeshCombinerMeshAPI
                                    .RendererPreparationTimingSnapshot
                                    .Capture();
                            rendererPlan.Preparation =
                                SkinnedMeshCombinerMeshAPI
                                    .BeginIncrementalCombinePreparation(
                                        new SkinnedMeshCombinerMeshAPI
                                            .RendererBatch
                                        {
                                            Renderer =
                                                rendererPlan.StagingRenderer,
                                            Sources =
                                                rendererPlan.Sources,
                                            CurrentRendererIndex =
                                                rendererPlan.RendererIndex,
                                            AtlasResolution =
                                                plan.AtlasResolution,
                                            RendererAsset =
                                                rendererPlan.RendererAsset,
                                            HasRendererAssetOverride =
                                                true,
                                            SkipSkeletonUpdate = true
                                        },
                                        plan.Data,
                                        plan.BakedBlendshapes,
                                        plan.Data.markDynamic,
                                        false,
                                        plan.BoundsRotation);
                            return false;
                        }
                        if (!rendererPlan.Preparation.Step())
                        {
                            return false;
                        }

                        rendererPlan.Pending =
                            rendererPlan.Preparation.TakeResult();
                        rendererPlan.Preparation.Dispose();
                        rendererPlan.Preparation = null;
                        EnqueueRendererPreparationTimings(
                            rendererPlan.PreparationTimingBefore,
                            SkinnedMeshCombinerMeshAPI
                                .RendererPreparationTimingSnapshot
                                .Capture());
                        var metadataStopwatch =
                            System.Diagnostics.Stopwatch.StartNew();
                        plan.CaptureScheduledSlotMetadata(
                            rendererPlan,
                            rendererPlan.Pending
                                .SourceVertexOffsets);
                        metadataStopwatch.Stop();
                        EnqueueCompletedTiming(
                            "Prepare: Capture Slot Metadata",
                            metadataStopwatch.ElapsedTicks);
                    }
                    rendererPlan.SchedulingStage =
                        UMAIncrementalRendererSchedulingStage
                            .CreateBlendShapeLoader;
                    return false;

                case UMAIncrementalRendererSchedulingStage
                    .CreateBlendShapeLoader:
                    if (!rendererPlan.IsEmpty)
                    {
                        rendererPlan.BlendShapeLoader =
                            rendererPlan.Pending
                                .CreateIncrementalBlendShapeLoader();
                    }
                    rendererPlan.SchedulingStage =
                        UMAIncrementalRendererSchedulingStage.Completed;
                    return true;

                case UMAIncrementalRendererSchedulingStage.Completed:
                    return true;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported renderer scheduling stage {rendererPlan.SchedulingStage}.");
            }
        }

        private void EnqueueRendererPreparationTimings(
            SkinnedMeshCombinerMeshAPI.RendererPreparationTimingSnapshot before,
            SkinnedMeshCombinerMeshAPI.RendererPreparationTimingSnapshot after)
        {
            long analyzeSources =
                after.AnalyzeSources - before.AnalyzeSources;
            long analyzeBlendShapes =
                after.AnalyzeBlendShapes - before.AnalyzeBlendShapes;
            long allocateMeshData =
                after.AllocateMeshData - before.AllocateMeshData;
            long buildBoneWeights =
                after.BuildBoneWeights - before.BuildBoneWeights;
            long copyPositions =
                after.CopyPositions - before.CopyPositions;
            long packNormalsTangents =
                after.PackNormalsTangents - before.PackNormalsTangents;
            long packColorUV =
                after.PackColorUV - before.PackColorUV;
            long packAdditionalUV =
                after.PackAdditionalUV - before.PackAdditionalUV;
            long modifierPreparation =
                after.ModifierPreparationAndSchedule -
                before.ModifierPreparationAndSchedule;
            long boundsScheduling =
                after.BoundsJobsSchedule - before.BoundsJobsSchedule;
            long indexScheduling =
                after.IndexJobsSchedule - before.IndexJobsSchedule;
            long uvRemap = after.UVRemap - before.UVRemap;
            long total = after.Total - before.Total;

            EnqueueCompletedTiming(
                "Prepare: Source Analysis",
                analyzeSources);
            EnqueueCompletedTiming(
                "Prepare: BlendShape Analysis",
                analyzeBlendShapes);
            EnqueueCompletedTiming(
                "Prepare: MeshData Allocation",
                allocateMeshData);
            EnqueueCompletedTiming(
                "Prepare: Bone Mapping and Weights",
                buildBoneWeights);
            EnqueueCompletedTiming(
                "Prepare: Position Copy",
                copyPositions);
            EnqueueCompletedTiming(
                "Prepare: Normal/Tangent Packing",
                packNormalsTangents);
            EnqueueCompletedTiming(
                "Prepare: Color/UV Packing",
                packColorUV);
            EnqueueCompletedTiming(
                "Prepare: Additional UV Packing",
                packAdditionalUV);
            EnqueueCompletedTiming(
                "Prepare: Bone/Modifier Jobs",
                modifierPreparation);
            EnqueueCompletedTiming(
                "Prepare: Bounds Jobs",
                boundsScheduling);
            EnqueueCompletedTiming(
                "Prepare: Index/Mask Jobs",
                indexScheduling);
            EnqueueCompletedTiming(
                "Prepare: UV Transform Jobs",
                uvRemap);

            long measured =
                analyzeSources +
                analyzeBlendShapes +
                allocateMeshData +
                buildBoneWeights +
                copyPositions +
                packNormalsTangents +
                packColorUV +
                packAdditionalUV +
                modifierPreparation +
                boundsScheduling +
                indexScheduling +
                uvRemap;
            EnqueueCompletedTiming(
                "Prepare: Other Setup and Allocation",
                Math.Max(0L, total - measured));
        }

        private void EnqueueCompletedTiming(
            string stepName,
            long elapsedTicks)
        {
            completedTimings.Enqueue(
                new UMAMeshCombineStepTiming(
                    stepName,
                    Math.Max(0L, elapsedTicks)));
        }

        private static void AdvanceBaseMeshApplication(
            UMAIncrementalRendererPlan rendererPlan)
        {
            if (rendererPlan.IsEmpty)
            {
                rendererPlan.BaseMeshApplicationStage =
                    UMAIncrementalBaseMeshApplicationStage
                        .Completed;
                return;
            }
            if (rendererPlan.Pending == null ||
                !rendererPlan.Pending.IsCompleted)
            {
                throw new InvalidOperationException(
                    $"Renderer {rendererPlan.RendererIndex} was applied before its native jobs completed.");
            }

            Mesh mesh = rendererPlan.StagingRenderer.sharedMesh;
            switch (rendererPlan.BaseMeshApplicationStage)
            {
                case UMAIncrementalBaseMeshApplicationStage
                    .PrepareOutput:
                    rendererPlan.Pending.PrepareOutputMesh();
                    rendererPlan.BaseMeshApplicationStage =
                        UMAIncrementalBaseMeshApplicationStage
                            .ApplyWritableMeshData;
                    return;

                case UMAIncrementalBaseMeshApplicationStage
                    .ApplyWritableMeshData:
                    rendererPlan.Pending
                        .ApplyIncrementalWritableMeshData(mesh);
                    rendererPlan.BaseMeshApplicationStage =
                        UMAIncrementalBaseMeshApplicationStage
                            .ApplySkinning;
                    return;

                case UMAIncrementalBaseMeshApplicationStage
                    .ApplySkinning:
                    rendererPlan.PreparedCloth =
                        rendererPlan.Pending
                            .ApplyIncrementalBaseMeshSkinning(
                                mesh);
                    rendererPlan.BaseMeshApplicationStage =
                        UMAIncrementalBaseMeshApplicationStage
                            .Completed;
                    return;

                case UMAIncrementalBaseMeshApplicationStage
                    .Completed:
                    return;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported base-mesh application stage {rendererPlan.BaseMeshApplicationStage}.");
            }
        }

        private bool FinalizeRenderer(
            UMAIncrementalRendererPlan rendererPlan)
        {
            switch (rendererPlan.FinalizationStage)
            {
                case UMAIncrementalRendererFinalizationStage
                    .NativeMesh:
                    if (!rendererPlan.IsEmpty)
                    {
                        rendererPlan.PreparedCloth =
                            rendererPlan.Pending
                                .FinalizePreparedRendererWithoutBlendShapes(
                                rendererPlan.StagingRenderer.sharedMesh);
                    }
                    rendererPlan.DisposePending();
                    rendererPlan.FinalizationStage =
                        UMAIncrementalRendererFinalizationStage
                            .Materials;
                    return false;

                case UMAIncrementalRendererFinalizationStage
                    .Materials:
                    owner.ConfigureStagingRendererMaterials(
                        plan,
                        rendererPlan);
                    rendererPlan.FinalizationStage =
                        UMAIncrementalRendererFinalizationStage
                            .ClothAndHierarchy;
                    return false;

                case UMAIncrementalRendererFinalizationStage
                    .ClothAndHierarchy:
                    owner
                        .ConfigureStagingRendererClothAndHierarchy(
                            plan,
                            rendererPlan);
                    rendererPlan.FinalizationStage =
                        UMAIncrementalRendererFinalizationStage
                            .Completed;
                    return true;

                case UMAIncrementalRendererFinalizationStage
                    .Completed:
                    return true;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported renderer finalization stage {rendererPlan.FinalizationStage}.");
            }
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
