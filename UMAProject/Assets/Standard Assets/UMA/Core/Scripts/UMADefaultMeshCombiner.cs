using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
    public class UMADefaultMeshCombiner : UMAMeshCombiner
    {
        protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
        protected List<Material> combinedMaterialList;

        UMAData umaData;
        string[] textureNameList;
        int atlasResolution;

		protected void EnsureUMADataSetup(UMAData umaData)
		{
			if (umaData.umaRoot == null)
			{
				GameObject newRoot = new GameObject("Root");
				newRoot.transform.parent = umaData.transform;
				newRoot.transform.localPosition = Vector3.zero;
				newRoot.transform.localRotation = Quaternion.Euler(0f, 0, 90f);
				umaData.umaRoot = newRoot;

				GameObject newGlobal = new GameObject("Global");
				newGlobal.transform.parent = newRoot.transform;
				newGlobal.transform.localPosition = Vector3.zero;
				newGlobal.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);

				umaData.skeleton = new UMASkeleton(newGlobal.transform);

				var newRenderer = umaData.umaRoot.AddComponent<SkinnedMeshRenderer>();
				newRenderer.rootBone = newGlobal.transform;
				umaData.myRenderer = newRenderer;
				umaData.myRenderer.enabled = false;
				umaData.myRenderer.sharedMesh = new Mesh();
			}
			else
			{
				umaData.cleanMesh(false);
			}
		}

        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, string[] textureNameList, int atlasResolution)
        {
            this.umaData = umaData;
            this.textureNameList = textureNameList;
            this.atlasResolution = atlasResolution;

            combinedMeshList = new List<SkinnedMeshCombiner.CombineInstance>();
            combinedMaterialList = new List<Material>();

            if (updatedAtlas)
            {
                CombineByShader();
            }
            else
            {
                CombineByMaterial();
            }

			EnsureUMADataSetup(umaData);
			umaData.skeleton.BeginSkeletonUpdate();

			UMAMeshData umaMesh = new UMAMeshData();
			umaMesh.ClaimSharedBuffers();
			SkinnedMeshCombiner.CombineMeshes(umaMesh, combinedMeshList.ToArray(), umaData.myRenderer.rootBone, umaData.skeleton);

            if (updatedAtlas)
            {
				RecalculateUV(umaMesh);
            }

			umaMesh.ApplyDataToUnityMesh(umaData.myRenderer);
			umaMesh.ReleaseSharedBuffers();

            umaData.umaRecipe.ClearDNAConverters();
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slotData = umaData.umaRecipe.slotDataList[i];
                if (slotData != null)
                {
//                    umaData.EnsureBoneData(slotData.umaBoneData, slotData.animatedBones, boneMap);

					umaData.umaRecipe.AddDNAUpdater(slotData.asset.slotDNA);
                }
            }

            umaData.myRenderer.quality = SkinQuality.Bone4;
            //umaData.myRenderer.useLightProbes = true;
            var materials = combinedMaterialList.ToArray();
            umaData.myRenderer.sharedMaterials = materials;
            //umaData.myRenderer.sharedMesh.RecalculateBounds();
            umaData.myRenderer.sharedMesh.name = "UMAMesh";

            umaData.firstBake = false;

            //FireSlotAtlasNotification(umaData, materials);
        }

		//private void FireSlotAtlasNotification(UMAData umaData, Material[] materials)
		//{
		//    for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
		//    {
		//        for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
		//        {
		//            var materialDefinition = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex];
		//            var slotData = materialDefinition.source.slotData;
		//            if (slotData.SlotAtlassed != null)
		//            {
		//                slotData.SlotAtlassed.Invoke(umaData, slotData, materials[atlasIndex], materialDefinition.atlasRegion);
		//            }
		//        }
		//    }
		//    SlotData[] slots = umaData.umaRecipe.slotDataList;
		//    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
		//    {
		//        var slotData = slots[slotIndex];
		//        if (slotData == null) continue;
		//        if (slotData.textureNameList.Length == 1 && string.IsNullOrEmpty(slotData.textureNameList[0]))
		//        {
		//            if (slotData.SlotAtlassed != null)
		//            {
		//                slotData.SlotAtlassed.Invoke(umaData, slotData, materials[atlasIndex], materialDefinition.atlasRegion);
		//            }
		//        }
		//    }
		//}

        protected void CombineByShader()
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;

            for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
            {
                combinedMaterialList.Add(umaData.atlasList.atlas[atlasIndex].materialSample);

                for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
                {
					var materialDefinition = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex];
					var slotData = materialDefinition.source.slotData;
                    combineInstance = new SkinnedMeshCombiner.CombineInstance();
					combineInstance.meshData = slotData.asset.meshData;
					combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
					for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
					{
						combineInstance.targetSubmeshIndices[i] = -1;
					}
					combineInstance.targetSubmeshIndices[slotData.asset.subMeshIndex] = atlasIndex;
                    combinedMeshList.Add(combineInstance);

					if (slotData.asset.SlotAtlassed != null)
					{
						slotData.asset.SlotAtlassed.Invoke(umaData, slotData, umaData.atlasList.atlas[atlasIndex].materialSample, materialDefinition.atlasRegion);
					}
                }
            }


            SlotData[] slots = umaData.umaRecipe.slotDataList;
            int indexCount = 0;
            List<Material> sourceMaterials = null;
            int atlassedMaterials = combinedMaterialList.Count;
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
				if (slots[slotIndex] == null) continue;
				if (slots[slotIndex].asset.textureNameList.Length == 1 && string.IsNullOrEmpty(slots[slotIndex].asset.textureNameList[0]))
                {
                    combineInstance = new SkinnedMeshCombiner.CombineInstance();
					combineInstance.meshData = slots[slotIndex].asset.meshData;
                    combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
                    for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
                    {
                        combineInstance.targetSubmeshIndices[i] = -1;
                    }

                    bool contains = false;
					Material slotMaterial = null;
                    if (sourceMaterials != null)
                    {
                        for (int i = 0; i < sourceMaterials.Count; i++)
                        {
                            if (slots[slotIndex].materialSample == sourceMaterials[i])
                            {
								slotMaterial = combinedMaterialList[i + atlassedMaterials];
								combineInstance.targetSubmeshIndices[slots[slotIndex].asset.subMeshIndex] = i + atlassedMaterials;
                                contains = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        sourceMaterials = new List<Material>(slots.Length);
                    }
                    if (!contains)
                    {
                        sourceMaterials.Add(slots[slotIndex].materialSample);
						slotMaterial = new Material(slots[slotIndex].materialSample);
                        combinedMaterialList.Add(slotMaterial);
						combineInstance.targetSubmeshIndices[slots[slotIndex].asset.subMeshIndex] = combinedMaterialList.Count - 1;
                    }
					if (slots[slotIndex].asset.SlotAtlassed != null)
					{
						slots[slotIndex].asset.SlotAtlassed.Invoke(umaData, slots[slotIndex], slotMaterial, new Rect(0, 0, 1, 1));
					}

                    combinedMeshList.Add(combineInstance);
                    indexCount++;

                }

            }

        }

        protected void CombineByMaterial()
        {
            SlotData[] slots = umaData.umaRecipe.slotDataList;
            bool[] shareMaterial = new bool[slots.Length];

            SkinnedMeshCombiner.CombineInstance combineInstance;

            int indexCount = 0;
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                if (slots[slotIndex] != null)
                {
                    if (!shareMaterial[slotIndex])
                    {
                        combineInstance = new SkinnedMeshCombiner.CombineInstance();
						combineInstance.meshData = slots[slotIndex].asset.meshData;
						combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
						for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
						{
							combineInstance.targetSubmeshIndices[i] = -1;
						}

						combineInstance.targetSubmeshIndices[slots[slotIndex].asset.subMeshIndex] = indexCount;
                        combinedMeshList.Add(combineInstance);

                        Material tempMaterial = Instantiate(slots[slotIndex].materialSample) as Material;
                        tempMaterial.name = slots[slotIndex].slotName;
                        for (int textureType = 0; textureType < textureNameList.Length; textureType++)
                        {
                            if (tempMaterial.HasProperty(textureNameList[textureType]))
                            {
								slots[slotIndex].GetOverlay(0).asset.textureList[textureType].filterMode = FilterMode.Bilinear;
								tempMaterial.SetTexture(textureNameList[textureType], slots[slotIndex].GetOverlay(0).asset.textureList[textureType]);
                            }
                        }
                        combinedMaterialList.Add(tempMaterial);


                        shareMaterial[slotIndex] = true;

                        for (int slotIndex2 = slotIndex; slotIndex2 < slots.Length; slotIndex2++)
                        {
                            if (slots[slotIndex2] != null)
                            {
                                if (slotIndex2 != slotIndex && !shareMaterial[slotIndex2])
                                {
									if (slots[slotIndex].GetOverlay(0).asset.textureList[0].name == slots[slotIndex2].GetOverlay(0).asset.textureList[0].name)
                                    {
                                        combineInstance = new SkinnedMeshCombiner.CombineInstance();
										combineInstance.meshData = slots[slotIndex2].asset.meshData;
										combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
										for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
										{
											combineInstance.targetSubmeshIndices[i] = -1;
										}

										combineInstance.targetSubmeshIndices[slots[slotIndex2].asset.subMeshIndex] = indexCount;
                                        combinedMeshList.Add(combineInstance);

                                        shareMaterial[slotIndex2] = true;
                                    }
                                }
                            }
                        }
                        indexCount++;

                    }
                }
                else
                {
                    shareMaterial[slotIndex] = true;
                }
            }
        }

		protected void RecalculateUV(UMAMeshData umaMesh)
        {
            int idx = 0;
            //Handle Atlassed Verts
            for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
            {
                for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
                {
                    var tempAtlasRect = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].atlasRegion;
					int vertexCount = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.asset.meshData.vertices.Length;
					float atlasXMin = tempAtlasRect.xMin / atlasResolution;
					float atlasXMax = tempAtlasRect.xMax / atlasResolution;
					float atlasYMin = tempAtlasRect.yMin / atlasResolution;
					float atlasYMax = tempAtlasRect.yMax / atlasResolution;
					while (vertexCount-- > 0)
                    {
						umaMesh.uv[idx].x = Mathf.Lerp(atlasXMin, atlasXMax, umaMesh.uv[idx].x);
						umaMesh.uv[idx].y = Mathf.Lerp(atlasYMin, atlasYMax, umaMesh.uv[idx].y);
                        idx++;
                    }

                }
            }
        }
	}
}
