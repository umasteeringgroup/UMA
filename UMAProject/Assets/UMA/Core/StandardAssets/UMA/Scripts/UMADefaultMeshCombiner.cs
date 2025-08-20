using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

namespace UMA
{
    public class UMADefaultMeshCombiner : UMAMeshCombiner
    {
        protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
        protected List<UMAData.GeneratedMaterial> combinedMaterialList;

        UMAData umaData;
        int atlasResolution;
        private UMAClothProperties clothProperties;
        int currentRendererIndex;
        SkinnedMeshRenderer[] renderers;

        protected void EnsureUMADataSetup(UMAData umaData)
        {
            if (umaData.umaRecipe != null)
            {
                umaData.umaRecipe.UpdateMeshHideMasks();
            }

            #region SetupSkeleton
            // First, ensure that the skeleton is setup, and if not,
            // then generate the root, global and set it up.
            if (umaData.umaRoot == null)
            {
                umaData.SetupSkeleton();
            }
            else
            {
                umaData.CheckSkeletonSetup();
            }
            #endregion
            if (umaData.umaRoot != null)
            {
                if (umaData.force32bit && UMAAssetIndexer.Instance.Generator.Use32BitBuffers == false)
                {
                    int rendererCount = umaData.RendererCount;
                    for (int i = 0; i < rendererCount; i++)
                    {
                        var renderer = umaData.GetRenderer(i);
                        if (renderer.sharedMesh != null && renderer.sharedMesh.indexFormat != UnityEngine.Rendering.IndexFormat.UInt32)
                        {
                            renderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        }
                    }
                }
                umaData.CleanMesh(false);

                if ((umaData.RendererCount == umaData.generatedMaterials.rendererAssets.Count && umaData.AreRenderersEqual(umaData.generatedMaterials.rendererAssets)))
                {
                    renderers = umaData.GetRenderers();
                    umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
                }
                else
                {
                    var oldRenderers = umaData.GetRenderers();
                    var globalTransform = umaData.GetGlobalTransform();

                    renderers = new SkinnedMeshRenderer[umaData.generatedMaterials.rendererAssets.Count];

                    for (int i = 0; i < umaData.generatedMaterials.rendererAssets.Count; i++)
                    {
                        if (oldRenderers != null && oldRenderers.Length > i)
                        {
                            renderers[i] = oldRenderers[i];
                            if (umaData.generatedMaterials.rendererAssets[i] != null)
                            {
                                umaData.generatedMaterials.rendererAssets[i].ApplySettingsToRenderer(renderers[i]);
                            }
                            else
                            {
                                umaData.ResetRendererSettings(i);
                                if (umaData.defaultRendererAsset != null)
                                {
                                    umaData.defaultRendererAsset.ApplySettingsToRenderer(renderers[i]);
                                }
                            }

                            continue;
                        }
                        UMARendererAsset rendererAsset = umaData.generatedMaterials.rendererAssets[i];
                        if (rendererAsset == null)
                        {
                            rendererAsset = umaData.defaultRendererAsset;
                        }

                        renderers[i] = MakeRenderer(i, umaData, globalTransform, rendererAsset);
                    }

                    if (oldRenderers != null)
                    {
                        for (int i = umaData.generatedMaterials.rendererAssets.Count; i < oldRenderers.Length; i++)
                        {
                            DestroyImmediate(oldRenderers[i].gameObject);
                            //For cloth, be aware of issue: 845868
                            //https://issuetracker.unity3d.com/issues/cloth-repeatedly-destroying-objects-with-cloth-components-causes-a-crash-in-unity-cloth-updatenormals
                        }
                    }
                    umaData.SetRenderers(renderers);
                    umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
                }
                return;
            }

            //Clear out old cloth components
            for (int i = 0; i < umaData.RendererCount; i++)
            {
                Cloth cloth = renderers[i].GetComponent<Cloth>();
                if (cloth != null)
                {
                    DestroyImmediate(cloth, false); //Crashes if trying to use Destroy()
                }
            }
        }

        private SkinnedMeshRenderer MakeRenderer(int i, UMAData umaData, Transform rootBone, UMARendererAsset rendererAsset = null)
        {
            GameObject newSMRGO = new GameObject(i == 0 ? "UMARenderer" : ("UMARenderer " + i));
            newSMRGO.transform.parent = umaData.transform;
            newSMRGO.transform.localPosition = Vector3.zero;
            newSMRGO.transform.localRotation = Quaternion.Euler(0, 0, 0f);
            newSMRGO.transform.localScale = Vector3.one;
            newSMRGO.gameObject.layer = umaData.gameObject.layer;

            var newRenderer = newSMRGO.AddComponent<SkinnedMeshRenderer>();
            newRenderer.enabled = false;
            newRenderer.sharedMesh = new Mesh();
            if (umaData.markDynamic)
            {
                newRenderer.sharedMesh.MarkDynamic();
            }

            if (umaData.force32bit)
            {
                newRenderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            else
            {
                newRenderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            }
            newRenderer.rootBone = rootBone;
            newRenderer.quality = SkinQuality.Auto;
            newRenderer.sharedMesh.name = i == 0 ? "UMAMesh" : ("UMAMesh " + i);

            if (rendererAsset != null)
            {
                rendererAsset.ApplySettingsToRenderer(newRenderer);
            }

            return newRenderer;
        }

        /// <summary>
        /// Updates the UMA mesh and skeleton to match current slots.
        /// </summary>
        /// <param name="updatedAtlas">If set to <c>true</c> atlas has changed.</param>
        /// <param name="umaData">UMA data.</param>
        /// <param name="atlasResolution">Atlas resolution.</param>
        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)
        {
            this.umaData = umaData;
            this.atlasResolution = atlasResolution;

            combinedMeshList = new List<SkinnedMeshCombiner.CombineInstance>(umaData.umaRecipe.slotDataList.Length);
            combinedMaterialList = new List<UMAData.GeneratedMaterial>();

            EnsureUMADataSetup(umaData);
            umaData.skeleton.BeginSkeletonUpdate();


            umaData.BuildActiveModifiers();


            for (currentRendererIndex = 0; currentRendererIndex < umaData.generatedMaterials.rendererAssets.Count; currentRendererIndex++)
            {
                int subMeshIndex = 0;

                combinedMeshList.Clear();
                combinedMaterialList.Clear();
                clothProperties = null;

                BuildCombineInstances();
                if (combinedMeshList.Count == 0)
                {
                    continue;
                }

                // Ensure the skeleton contains all bones needed by all slots we’re about to combine
                if (umaData != null && umaData.skeleton != null)
                {
                    umaData.skeleton.BeginSkeletonUpdate();
                    for (int ci = 0; ci < combinedMeshList.Count; ci++)
                    {
                        var cb = combinedMeshList[ci];
                        if (cb.meshData != null && cb.meshData.umaBones != null)
                        {
                            var bones = cb.meshData.umaBones;
                            for (int b = 0; b < bones.Length; b++)
                                umaData.skeleton.EnsureBone(bones[b]);
                        }
                    }
                    umaData.skeleton.EnsureBoneHierarchy();
                    umaData.skeleton.EndSkeletonUpdate();
                }

#if UNITY_2022_2_OR_NEWER
                bool useMeshAPI = UMASettings.UseMeshAPICombiner;
                if (useMeshAPI)
                {
                    // New MeshAPI path
                    var bakedBlendshapes = BuildBakedBlendshapeDict(umaData.blendShapeSettings);
                    var clothCoeffs = SkinnedMeshCombinerMeshAPI.CombineIntoRenderer(
                        renderers[currentRendererIndex],
                        combinedMeshList.ToArray(),
                        umaData,
                        currentRendererIndex,
                        atlasResolution,
                        bakedBlendshapes,
                        umaData.markDynamic,
                        umaData.markNotReadable
                    );

                    // Apply/clear Cloth like legacy path
                    var cloth = renderers[currentRendererIndex].GetComponent<Cloth>();
                    if (clothProperties != null)
                    {
                        if (cloth == null) cloth = renderers[currentRendererIndex].gameObject.GetComponent<Cloth>();
                        if (cloth == null) cloth = renderers[currentRendererIndex].gameObject.AddComponent<Cloth>();
                        if (clothCoeffs != null && clothCoeffs.Length > 0) cloth.coefficients = clothCoeffs;
                        clothProperties.ApplyValues(cloth);
                    }
                    else
                    {
                        UMAUtils.DestroySceneObject(cloth);
                    }
                }
                else
#endif
                {
                    // Legacy UMA path
                    if (combinedMeshList.Count == 1)
                    {
                        var tempMesh = SkinnedMeshCombiner.ShallowInstanceMesh(combinedMeshList[0].meshData, combinedMeshList[0].triangleMask);
                        if (umaData.umaRecipe.BlendshapeSlots.ContainsKey(combinedMeshList[0].meshData.SlotName))
                        {
                            var Blendshapes = SkinnedMeshCombiner.GetBlendshapeSources(tempMesh, umaData.umaRecipe);
                            tempMesh.blendShapes = Blendshapes.ToArray();
                        }

                        tempMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton, umaData);
                        var inst = combinedMeshList[0];
                        inst.slotData.vertexOffset = 0;
                        inst.slotData.submeshIndex = 0;
                        inst.slotData.skinnedMeshRenderer = currentRendererIndex;
                    }
                    else
                    {
                        UMAMeshData umaMesh = new UMAMeshData();
                        umaMesh.SlotName = "CombinedMesh";
                        umaMesh.subMeshCount = 0;
                        umaMesh.vertexCount = 0;

                        SkinnedMeshCombiner.CombineMeshes(umaMesh, combinedMeshList.ToArray(), umaData.blendShapeSettings, umaData.umaRecipe, currentRendererIndex);

                        if (updatedAtlas)
                        {
                            RecalculateUV(umaMesh);
                        }
                        umaMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton, umaData);
                    }
                    var cloth = renderers[currentRendererIndex].GetComponent<Cloth>();
                    if (clothProperties != null)
                    {
                        if (cloth != null)
                        {
                            clothProperties.ApplyValues(cloth);
                        }
                    }
                    else
                    {
                        UMAUtils.DestroySceneObject(cloth);
                    }
                }

                // Materials assignment (same for both paths)
                List<Material> materials = new List<Material>(combinedMaterialList.Count + 2);
                var renderer = renderers[currentRendererIndex];
                var submeshes = new List<SubMeshDescriptor>();

                for (int i = 0; i < combinedMaterialList.Count; i++)
                {
                    if (i >= renderer.sharedMesh.subMeshCount)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning("Submesh count mismatch between generated materials and renderer mesh. This can happen if you have overlays applied to a utility (non-mesh) slot somehow. This can cause the wrong materials to be applied to the mesh.");
#endif
                        break;
                    }
                    var cm = combinedMaterialList[i];
                    materials.Add(cm.material);
                    submeshes.Add(renderer.sharedMesh.GetSubMesh(i));

                    for (int k = 0; k < cm.materialFragments.Count; k++)
                    {
                        var matfrag = cm.materialFragments[k];
                        matfrag.slotData.submeshIndex = subMeshIndex;
                    }

                    subMeshIndex++;

                    if (cm.umaMaterial.secondPass != null)
                    {
                        Material secondPass = Instantiate(cm.umaMaterial.secondPass);
                        cm.secondPassMaterial = secondPass;
                        UMAGeneratorPro.ApplyMaterialParameters(cm, umaData, secondPass);
                        CopyMaterialTextures(secondPass, cm.material, cm.umaMaterial);
                        if (cm.material.HasProperty("_OverlayCount"))
                        {
                            SetCompositingParameters(secondPass, cm);
                        }
                        materials.Add(secondPass);
                        submeshes.Add(renderer.sharedMesh.GetSubMesh(i));
                        subMeshIndex++;
                    }
                    combinedMaterialList[i].skinnedMeshRenderer = renderers[currentRendererIndex];
                }
                renderers[currentRendererIndex].sharedMaterials = materials.ToArray();
                renderers[currentRendererIndex].sharedMesh.SetSubMeshes(submeshes.ToArray(), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                renderers[currentRendererIndex].sharedMesh.UploadMeshData(umaData.markNotReadable);
            }

            umaData.umaRecipe.ClearDNAConverters();
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slotData = umaData.umaRecipe.slotDataList[i];
                if (slotData != null && !slotData.isBlendShapeSource)
                {
                    umaData.umaRecipe.AddDNAUpdater(slotData.asset.slotDNA);
                }
            }

            umaData.firstBake = false;
        }

        // Build a dict of blendshapes to bake (name -> value), based on UMA BlendShapeSettings.
        private static Dictionary<string, float> BuildBakedBlendshapeDict(BlendShapeSettings settings)
        {
            var dict = new Dictionary<string, float>();
            if (settings == null || settings.blendShapes == null) return dict;
            foreach (var kv in settings.blendShapes)
            {
                if (kv.Value.isBaked)
                {
                    dict[kv.Key] = kv.Value.value;
                }
            }
            return dict;
        }

        public static void SetCompositingParameters(Material secondPass, UMAData.GeneratedMaterial cm)
        {
            // if this is a compositing shader, there is only one material fragment.
            if (cm.materialFragments.Count == 1)
            {
                TextureProcessPRO.SetCompositingProperties(cm, secondPass, cm.materialFragments[0]);
            }
        }

        public static void CopyMaterialTextures(Material secondPass, Material material, UMAMaterial uMAMaterial)
        {
            for (int i = 0; i < uMAMaterial.channels.Length; i++)
            {
                UMAMaterial.MaterialChannel channel = uMAMaterial.channels[i];
                var texture = material.GetTexture(channel.materialPropertyName);
                if (texture != null)
                {
                    secondPass.SetTexture(channel.materialPropertyName, texture);
                }
            }
        }

        protected UMAMeshData ApplyMeshModifiers(UMAData umaData, UMAMeshData meshData, SlotData slotData)
        {
            if (slotData.meshModifiers != null)
            {
                foreach (var modifier in slotData.meshModifiers)
                {
                    if (modifier != null)
                    {
                        // Need an override to apply the mesh modifiers to the 
                        meshData = modifier.Process(meshData);
                    }
                }
            }
            return meshData;
        }

        protected void BuildCombineInstances()
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;

            //Since BuildCombineInstances is called within a renderer loop, use a variable to keep track of the materialIndex per renderer
            int rendererMaterialIndex = 0;

            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
                UMARendererAsset rendererAsset = umaData.GetRendererAsset(currentRendererIndex);
                var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
                if (generatedMaterial.rendererAsset != rendererAsset)
                {
                    continue;
                }

                combinedMaterialList.Add(generatedMaterial);
                generatedMaterial.materialIndex = materialIndex;

                for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
                {
                    var materialDefinition = generatedMaterial.materialFragments[materialDefinitionIndex];
                    var slotData = materialDefinition.slotData;
                    combineInstance = new SkinnedMeshCombiner.CombineInstance();
                    if (umaData.VertexOverrides.ContainsKey(slotData.slotName))
                    {
                        combineInstance.meshData = slotData.asset.meshData.ShallowCopy(umaData.VertexOverrides[slotData.slotName]);
                        combineInstance.meshData.SlotName = slotData.slotName;
                    }
                    else
                    {
                        combineInstance.meshData = slotData.asset.meshData.ShallowCopy(null);
                        combineInstance.meshData.SlotName = slotData.slotName;
                    }
                    // UV is remapped. Update the MeshData.
                    if (slotData.UVRemapped)
                    {
                        switch (slotData.UVSet)
                        {
                            case 1:
                                combineInstance.meshData.uv = slotData.asset.meshData.uv2;
                                break;
                            case 2:
                                combineInstance.meshData.uv = slotData.asset.meshData.uv3;
                                break;
                            case 3:
                                combineInstance.meshData.uv = slotData.asset.meshData.uv4;
                                break;
                        }
                    }
                    combineInstance.meshData = ApplyMeshModifiers(umaData, combineInstance.meshData, slotData);
                    // save a copy of the slotData so we can add
                    // the vertex offsets, submeshindex to it.
                    combineInstance.slotData = slotData;

                    //New MeshHiding
                    if (slotData.meshHideMask != null)
                    {
                        combineInstance.triangleMask = slotData.meshHideMask;
                    }

                    combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
                    if (combineInstance.meshData.subMeshCount == 0)
                    {
                        continue;
                    }
                    for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
                    {
                        combineInstance.targetSubmeshIndices[i] = -1;
                    }
                    combineInstance.targetSubmeshIndices[slotData.asset.subMeshIndex] = rendererMaterialIndex;
                    combinedMeshList.Add(combineInstance);

                    if (slotData.asset.SlotAtlassed != null)
                    {
                        slotData.asset.SlotAtlassed.Invoke(umaData, slotData, generatedMaterial.material, materialDefinition.atlasRegion);
                    }
                    if (rendererAsset != null && rendererAsset.ClothProperties != null)
                    {
                        clothProperties = rendererAsset.ClothProperties;
                    }
                }
                rendererMaterialIndex++;
            }
        }

        protected void RecalculateUV(UMAMeshData umaMesh)
        {
            int idx = 0;
            //Handle Atlassed Verts
            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
                var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];

                if (generatedMaterial.rendererAsset != umaData.GetRendererAsset(currentRendererIndex))
                {
                    continue;
                }

                if (!generatedMaterial.umaMaterial.IsGeneratedTextures)
                //if (generatedMaterial.umaMaterial.materialType != UMAMaterial.MaterialType.Atlas)
                {
                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                    {
                        UMAData.MaterialFragment fragment = generatedMaterial.materialFragments[i];
                        int vertexCount = fragment.slotData.asset.meshData.vertices.Length;
                        idx += vertexCount;
                    }
                    continue;
                }

                for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
                {
                    var fragment = generatedMaterial.materialFragments[materialDefinitionIndex];
                    var tempAtlasRect = fragment.atlasRegion;
                    int vertexCount = fragment.slotData.asset.meshData.vertices.Length;
                    float atlasXMin = tempAtlasRect.xMin / atlasResolution;
                    float atlasXMax = tempAtlasRect.xMax / atlasResolution;
                    float atlasXRange = atlasXMax - atlasXMin;
                    float atlasYMin = tempAtlasRect.yMin / atlasResolution;
                    float atlasYMax = tempAtlasRect.yMax / atlasResolution;
                    float atlasYRange = atlasYMax - atlasYMin;

                    // code below is for UVs remap based on rel pos in the atlas
                    if (fragment.isRectShared && fragment.slotData.useAtlasOverlay)
                    {
                        OverlayData foundRect = null;
                        for (int i = 0; i < fragment.overlayList.Count; i++)
                        {
                            OverlayData szname = fragment.overlayList[i];
                            if (fragment.slotData.slotName != null && szname.overlayName != null && szname.overlayName.Contains(fragment.slotData.slotName))
                            {
                                foundRect = szname;
                                break;
                            }
                        }
                        if (foundRect != null && foundRect.rect != Rect.zero)
                        {
                            var size = foundRect.rect.size * generatedMaterial.resolutionScale;
                            var offsetX = foundRect.rect.x * generatedMaterial.resolutionScale.x;
                            var offsetY = foundRect.rect.y * generatedMaterial.resolutionScale.x;

                            atlasXMin += (offsetX / generatedMaterial.cropResolution.x);
                            atlasXRange = size.x / generatedMaterial.cropResolution.x;

                            atlasYMin += (offsetY / generatedMaterial.cropResolution.y);
                            atlasYRange = size.y / generatedMaterial.cropResolution.y;
                        }
                    }

                    var sd = fragment.slotData;
                    sd.UVArea.Set(atlasXMin, atlasYMin, atlasXRange, atlasYRange);

                    while (vertexCount-- > 0)
                    {
                        umaMesh.uv[idx].x = atlasXMin + atlasXRange * umaMesh.uv[idx].x;
                        umaMesh.uv[idx].y = atlasYMin + atlasYRange * umaMesh.uv[idx].y;
                        idx++;
                    }
                }
            }
        }
    }
}
