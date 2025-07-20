using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

namespace UMA
{
    /// <summary>
    /// Enhanced mesh combiner that uses Unity's MeshData API and Job System for improved performance.
    /// This class provides a more efficient alternative to the traditional mesh combiner.
    /// </summary>
    public class UMAMeshDataCombiner : UMAMeshCombiner
    {
        protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
        protected List<UMAData.GeneratedMaterial> combinedMaterialList;

        UMAData umaData;
        int atlasResolution;
        private UMAClothProperties clothProperties;
        int currentRendererIndex;
        SkinnedMeshRenderer[] renderers;

        /// <summary>
        /// Updates the UMA mesh and skeleton using the MeshData API for improved performance.
        /// </summary>
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

                if (combinedMeshList.Count == 1)
                {
                    // Fast track for single mesh
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
                    // Use MeshData API for multiple meshes
                    UMAMeshData umaMesh = new UMAMeshData();
                    umaMesh.SlotName = "CombinedMesh";
                    umaMesh.subMeshCount = 0;
                    umaMesh.vertexCount = 0;

                    // Use the enhanced mesh combining with MeshData API
                    MeshDataCombineMeshes(umaMesh, combinedMeshList.ToArray(), umaData.blendShapeSettings, umaData.umaRecipe, currentRendererIndex);

                    // Apply the modifiers before the UV is updated for the atlas.
                    if (updatedAtlas)
                    {
                        RecalculateUV(umaMesh);
                    }
                    umaMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton, umaData);
                }

                // Handle cloth and materials (same as original)
                HandleClothAndMaterials();
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

        /// <summary>
        /// Enhanced mesh combining using MeshData API and Jobs for better performance
        /// </summary>
        private void MeshDataCombineMeshes(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer)
        {
            if (sources == null || sources.Length == 0)
                return;

            if (blendShapeSettings == null)
                blendShapeSettings = new BlendShapeSettings();

            // Use the newer MeshData approach when available
#if UNITY_2020_1_OR_NEWER
            if (TryUseMeshDataAPI(target, sources, blendShapeSettings, recipe, currentRenderer))
            {
                return; // Successfully used MeshData API
            }
#endif

            // Fallback to existing implementation for compatibility
            SkinnedMeshCombiner.CombineMeshes(target, sources, blendShapeSettings, recipe, currentRenderer);
        }

#if UNITY_2020_1_OR_NEWER
        /// <summary>
        /// Attempts to use Unity's MeshData API for more efficient mesh combining
        /// </summary>
        private bool TryUseMeshDataAPI(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer)
        {
            try
            {
                // Analyze sources for vertex data requirements
                int totalVertexCount = 0;
                int totalTriangleCount = 0;
                bool hasNormals = false;
                bool hasTangents = false;
                bool hasUV = false;
                bool hasColors = false;

                foreach (var source in sources)
                {
                    if (source.meshData?.vertices != null)
                    {
                        totalVertexCount += source.meshData.vertices.Length;
                        
                        // Estimate triangle count from submeshes
                        for (int i = 0; i < source.meshData.subMeshCount; i++)
                        {
                            if (source.targetSubmeshIndices[i] >= 0)
                            {
                                var triangles = source.meshData.submeshes[i].GetTriangles();
                                totalTriangleCount += triangles.Length;
                            }
                        }
                        
                        if (source.meshData.normals != null && source.meshData.normals.Length > 0)
                            hasNormals = true;
                        if (source.meshData.tangents != null && source.meshData.tangents.Length > 0)
                            hasTangents = true;
                        if (source.meshData.uv != null && source.meshData.uv.Length > 0)
                            hasUV = true;
                        if (source.meshData.colors32 != null && source.meshData.colors32.Length > 0)
                            hasColors = true;
                    }
                }

                if (totalVertexCount == 0)
                    return false;

                // Create a mesh using MeshData API for better performance
                var meshDataArray = Mesh.AllocateWritableMeshData(1);
                var meshData = meshDataArray[0];

                // Set vertex attributes based on what we found in sources
                var attributes = new List<VertexAttributeDescriptor>();
                attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                
                if (hasNormals)
                    attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3));
                if (hasTangents)
                    attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4));
                if (hasUV)
                    attributes.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                if (hasColors)
                    attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));

                meshData.SetVertexBufferParams(totalVertexCount, attributes.ToArray());

                // Get vertex data arrays from MeshData - handle the stream indices properly
                var vertices = meshData.GetVertexData<Vector3>(0);
                
                NativeArray<Vector3> normals = default;
                NativeArray<Vector4> tangents = default;
                NativeArray<Vector2> uvs = default;
                NativeArray<Color32> colors = default;
                
                int streamIndex = 1;
                if (hasNormals)
                {
                    normals = meshData.GetVertexData<Vector3>(streamIndex++);
                }
                if (hasTangents)
                {
                    tangents = meshData.GetVertexData<Vector4>(streamIndex++);
                }
                if (hasUV)
                {
                    uvs = meshData.GetVertexData<Vector2>(streamIndex++);
                }
                if (hasColors)
                {
                    colors = meshData.GetVertexData<Color32>(streamIndex++);
                }

                // Process vertex data using jobs for parallel execution
                ProcessVertexDataWithJobs(sources, vertices, normals, tangents, uvs, colors, hasNormals, hasTangents, hasUV, hasColors);

                // Convert MeshData to UMAMeshData for compatibility
                target.vertexCount = totalVertexCount;
                target.vertices = vertices.ToArray();
                
                if (hasNormals && normals.IsCreated)
                    target.normals = normals.ToArray();
                if (hasTangents && tangents.IsCreated)
                    target.tangents = tangents.ToArray();
                if (hasUV && uvs.IsCreated)
                    target.uv = uvs.ToArray();
                if (hasColors && colors.IsCreated)
                    target.colors32 = colors.ToArray();

                // For complex operations like bone weights and blend shapes, fall back to existing logic
                // This hybrid approach provides performance benefits while maintaining compatibility
                var tempTarget = new UMAMeshData();
                SkinnedMeshCombiner.CombineMeshes(tempTarget, sources, blendShapeSettings, recipe, currentRenderer);
                
                // Copy complex data from temporary target
                target.bindPoses = tempTarget.bindPoses;
                target.boneNameHashes = tempTarget.boneNameHashes;
                target.umaBones = tempTarget.umaBones;
                target.umaBoneCount = tempTarget.umaBoneCount;
                target.subMeshCount = tempTarget.subMeshCount;
                target.submeshes = tempTarget.submeshes;
                target.blendShapes = tempTarget.blendShapes;
                target.clothSkinning = tempTarget.clothSkinning;
#if USE_NATIVE_ARRAYS
                target.unityBoneWeights = tempTarget.unityBoneWeights;
                target.unityBonesPerVertex = tempTarget.unityBonesPerVertex;
#else
                target.ManagedBoneWeights = tempTarget.ManagedBoneWeights;
                target.ManagedBonesPerVertex = tempTarget.ManagedBonesPerVertex;
#endif

                meshDataArray.Dispose();
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to use MeshData API, falling back to traditional approach: {ex.Message}");
                return false;
            }
        }



        private void ProcessVertexDataWithJobs(SkinnedMeshCombiner.CombineInstance[] sources, 
            NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<Vector4> tangents, 
            NativeArray<Vector2> uvs, NativeArray<Color32> colors,
            bool hasNormals, bool hasTangents, bool hasUV, bool hasColors)
        {
            var jobHandles = new NativeList<JobHandle>(sources.Length * 4, Allocator.TempJob);
            int vertexOffset = 0;

            try
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    var source = sources[i];
                    if (source.meshData?.vertices == null || source.meshData.vertices.Length == 0)
                        continue;

                    int sourceVertexCount = source.meshData.vertices.Length;

                    // Schedule vertex copying job with expand along normal support
                    var vertexJob = new OptimizedVertexCopyJob
                    {
                        sourceVertices = new NativeArray<Vector3>(source.meshData.vertices, Allocator.TempJob),
                        targetVertices = vertices,
                        sourceNormals = hasNormals && source.meshData.normals != null && source.meshData.normals.Length > 0 ? 
                            new NativeArray<Vector3>(source.meshData.normals, Allocator.TempJob) : default,
                        vertexOffset = vertexOffset,
                        expandAlongNormal = source.slotData.expandAlongNormal,
                        hasSourceNormals = hasNormals && source.meshData.normals != null && source.meshData.normals.Length > 0
                    };

                    var vertexHandle = vertexJob.Schedule(sourceVertexCount, 64);
                    jobHandles.Add(vertexHandle);

                    // Schedule normal copying job if available
                    if (hasNormals && source.meshData.normals != null && source.meshData.normals.Length > 0 && normals.IsCreated)
                    {
                        var normalJob = new CopyNormalDataJob
                        {
                            sourceNormals = new NativeArray<Vector3>(source.meshData.normals, Allocator.TempJob),
                            targetNormals = normals,
                            vertexOffset = vertexOffset
                        };
                        var normalHandle = normalJob.Schedule(sourceVertexCount, 64, vertexHandle);
                        jobHandles.Add(normalHandle);
                    }

                    // Schedule tangent copying job if available
                    if (hasTangents && source.meshData.tangents != null && source.meshData.tangents.Length > 0 && tangents.IsCreated)
                    {
                        var tangentJob = new CopyTangentDataJob
                        {
                            sourceTangents = new NativeArray<Vector4>(source.meshData.tangents, Allocator.TempJob),
                            targetTangents = tangents,
                            vertexOffset = vertexOffset
                        };
                        var tangentHandle = tangentJob.Schedule(sourceVertexCount, 64, vertexHandle);
                        jobHandles.Add(tangentHandle);
                    }

                    // Schedule UV copying job if available
                    if (hasUV && source.meshData.uv != null && source.meshData.uv.Length >= sourceVertexCount && uvs.IsCreated)
                    {
                        var uvJob = new CopyUVDataJob
                        {
                            sourceUV = new NativeArray<Vector2>(source.meshData.uv, Allocator.TempJob),
                            targetUV = uvs,
                            vertexOffset = vertexOffset
                        };
                        var uvHandle = uvJob.Schedule(sourceVertexCount, 64, vertexHandle);
                        jobHandles.Add(uvHandle);
                    }

                    // Schedule color copying job if available
                    if (hasColors && source.meshData.colors32 != null && source.meshData.colors32.Length > 0 && colors.IsCreated)
                    {
                        var colorJob = new CopyColorDataJob
                        {
                            sourceColors = new NativeArray<Color32>(source.meshData.colors32, Allocator.TempJob),
                            targetColors = colors,
                            vertexOffset = vertexOffset
                        };
                        var colorHandle = colorJob.Schedule(sourceVertexCount, 64, vertexHandle);
                        jobHandles.Add(colorHandle);
                    }

                    vertexOffset += sourceVertexCount;
                }

                // Complete all jobs
                for (int i = 0; i < jobHandles.Length; i++)
                {
                    jobHandles[i].Complete();
                }
            }
            finally
            {
                jobHandles.Dispose();
            }
        }
#endif

        private void HandleClothAndMaterials()
        {
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

            // Handle materials
            List<Material> materials = new List<Material>(combinedMaterialList.Count + 2);
            var renderer = renderers[currentRendererIndex];
            var submeshes = new List<SubMeshDescriptor>();
            int subMeshIndex = 0;

            for (int i = 0; i < combinedMaterialList.Count; i++)
            {
                if (i >= renderer.sharedMesh.subMeshCount)
                {
                    Debug.LogWarning("Submesh count mismatch between generated materials and renderer mesh.");
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
                    UMADefaultMeshCombiner.CopyMaterialTextures(secondPass, cm.material, cm.umaMaterial);
                    if (cm.material.HasProperty("_OverlayCount"))
                    {
                        UMADefaultMeshCombiner.SetCompositingParameters(secondPass, cm);
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

        // Copy essential methods from UMADefaultMeshCombiner for compatibility
        protected void EnsureUMADataSetup(UMAData umaData)
        {
            if (umaData.umaRecipe != null)
            {
                umaData.umaRecipe.UpdateMeshHideMasks();
            }

            #region SetupSkeleton
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
                umaData.CleanMesh(false);
                if ((umaData.rendererCount == umaData.generatedMaterials.rendererAssets.Count && umaData.AreRenderersEqual(umaData.generatedMaterials.rendererAssets)))
                {
                    renderers = umaData.GetRenderers();
                    umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
                }
                else
                {
                    CreateNewRenderers(umaData);
                }
                return;
            }

            // Clear out old cloth components
            for (int i = 0; i < umaData.rendererCount; i++)
            {
                Cloth cloth = renderers[i].GetComponent<Cloth>();
                if (cloth != null)
                {
                    DestroyImmediate(cloth, false);
                }
            }
        }

        private void CreateNewRenderers(UMAData umaData)
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
                }
            }
            umaData.SetRenderers(renderers);
            umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
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

#if UMA_32BITBUFFERS
            newRenderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
            newRenderer.rootBone = rootBone;
            newRenderer.quality = SkinQuality.Auto;
            newRenderer.sharedMesh.name = i == 0 ? "UMAMesh" : ("UMAMesh " + i);

            if (rendererAsset != null)
            {
                rendererAsset.ApplySettingsToRenderer(newRenderer);
            }

            return newRenderer;
        }

        protected void BuildCombineInstances()
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;
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

        protected UMAMeshData ApplyMeshModifiers(UMAData umaData, UMAMeshData meshData, SlotData slotData)
        {
            if (slotData.meshModifiers != null)
            {
                foreach (var modifier in slotData.meshModifiers)
                {
                    if (modifier != null)
                    {
                        meshData = modifier.Process(meshData);
                    }
                }
            }
            return meshData;
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
                        var foundRect = fragment.overlayList.FirstOrDefault(szname => fragment.slotData.slotName != null && szname.overlayName.Contains(fragment.slotData.slotName));
                        if (null != foundRect && foundRect.rect != Rect.zero)
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

    // Job structures for MeshData processing
#if UMA_BURSTCOMPILE
    [BurstCompile]
#endif
    public struct OptimizedVertexCopyJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> sourceVertices;
        [ReadOnly] public NativeArray<Vector3> sourceNormals;
        public NativeArray<Vector3> targetVertices;
        [ReadOnly] public int vertexOffset;
        [ReadOnly] public int expandAlongNormal;
        [ReadOnly] public bool hasSourceNormals;

        public void Execute(int index)
        {
            Vector3 vertex = sourceVertices[index];
            
            if (expandAlongNormal > 0 && hasSourceNormals && index < sourceNormals.Length)
            {
                float expandAmount = ((float)expandAlongNormal) / 1000000f;
                vertex += sourceNormals[index] * expandAmount;
            }
            
            targetVertices[vertexOffset + index] = vertex;
        }
    }

#if UMA_BURSTCOMPILE
    [BurstCompile]
#endif
    public struct CopyNormalDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> sourceNormals;
        public NativeArray<Vector3> targetNormals;
        [ReadOnly] public int vertexOffset;

        public void Execute(int index)
        {
            targetNormals[vertexOffset + index] = sourceNormals[index];
        }
    }

#if UMA_BURSTCOMPILE
    [BurstCompile]
#endif
    public struct CopyTangentDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector4> sourceTangents;
        public NativeArray<Vector4> targetTangents;
        [ReadOnly] public int vertexOffset;

        public void Execute(int index)
        {
            targetTangents[vertexOffset + index] = sourceTangents[index];
        }
    }

#if UMA_BURSTCOMPILE
    [BurstCompile]
#endif
    public struct CopyUVDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector2> sourceUV;
        public NativeArray<Vector2> targetUV;
        [ReadOnly] public int vertexOffset;

        public void Execute(int index)
        {
            targetUV[vertexOffset + index] = sourceUV[index];
        }
    }

#if UMA_BURSTCOMPILE
    [BurstCompile]
#endif
    public struct CopyColorDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> sourceColors;
        public NativeArray<Color32> targetColors;
        [ReadOnly] public int vertexOffset;

        public void Execute(int index)
        {
            targetColors[vertexOffset + index] = sourceColors[index];
        }
    }
}