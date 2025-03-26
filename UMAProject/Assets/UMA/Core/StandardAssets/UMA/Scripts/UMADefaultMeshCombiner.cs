using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>
    /// Default mesh combiner for UMA UMAMeshdata from slots.
    /// </summary>
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
				umaData.CleanMesh(false);
				if ((umaData.rendererCount == umaData.generatedMaterials.rendererAssets.Count && umaData.AreRenderersEqual(umaData.generatedMaterials.rendererAssets)))
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
			for (int i = 0; i < umaData.rendererCount; i++)
			{
				Cloth cloth = renderers[i].GetComponent<Cloth>();
				if (cloth != null)
                {
                    DestroyImmediate(cloth,false); //Crashes if trying to use Destroy()
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

#if UMA_32BITBUFFERS
			newRenderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
			newRenderer.rootBone = rootBone;
			newRenderer.quality = SkinQuality.Auto;
			newRenderer.sharedMesh.name = i == 0 ? "UMAMesh" : ("UMAMesh " + i);

			if(rendererAsset != null)
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
				//Move umaMesh creation to with in the renderer loops
				//May want to make sure to set all it's buffers to null instead of creating a new UMAMeshData

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
					// fast track
					var tempMesh = SkinnedMeshCombiner.ShallowInstanceMesh(combinedMeshList[0].meshData, combinedMeshList[0].triangleMask );
					if (umaData.umaRecipe.BlendshapeSlots.ContainsKey(combinedMeshList[0].meshData.SlotName))
                    {

						var Blendshapes = SkinnedMeshCombiner.GetBlendshapeSources(tempMesh, umaData.umaRecipe);
						tempMesh.blendShapes = Blendshapes.ToArray();
                    }
					
					tempMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton,umaData);
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

					SkinnedMeshCombiner.CombineMeshes(umaMesh, combinedMeshList.ToArray(), umaData.blendShapeSettings,umaData.umaRecipe, currentRendererIndex );

					// Apply the modifiers before the UV is updated for the atlas.
                    if (updatedAtlas)
					{
						RecalculateUV(umaMesh);
					}
                    umaMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton,umaData);
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

				//Material[] materials = new Material[combinedMaterialList.Count];
				
				// allocate enough space to avoid extra allocations
				List<Material> materials = new List<Material>(combinedMaterialList.Count+2);

				var renderer = renderers[currentRendererIndex];
				var submeshes = new List<SubMeshDescriptor>();
				
				for(int i=0;i<combinedMaterialList.Count;i++)
                {
					if (i >= renderer.sharedMesh.subMeshCount) 
					{
						Debug.LogWarning("Submesh count mismatch between generated materials and renderer mesh. This can happen if you have overlays applied to a utility (non-mesh) slot somehow. This can cause the wrong materials to be applied to the mesh.");
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
                        // Apply shader property blocks to second pass material
                        UMAGeneratorPro.ApplyMaterialParameters(cm,umaData,secondPass);
                        // set textures based on overlay texture channels
                        CopyMaterialTextures(secondPass, cm.material, cm.umaMaterial);
						// set compositing parameters if needed
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
				//renderers[currentRendererIndex].sharedMaterials = materials;
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
                        meshData = modifier.Process( meshData);
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
						switch(slotData.UVSet)
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
					if (combineInstance.meshData.subMeshCount == 0) {
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
}
