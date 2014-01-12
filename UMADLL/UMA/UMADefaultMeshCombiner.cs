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

            if (umaData.firstBake)
            {
                umaData.myRenderer.sharedMesh = new Mesh();
            }
            else
            {
                umaData.cleanMesh(false);
            }

            var boneMap = new Dictionary<Transform, Transform>();
            SkinnedMeshCombiner.CombineMeshes(umaData.myRenderer, combinedMeshList.ToArray(), boneMap);

            if (updatedAtlas)
            {
                RecalculateUV();
            }

            umaData.umaRecipe.ClearDNAConverters();
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slotData = umaData.umaRecipe.slotDataList[i];
                if (slotData != null)
                {

                    umaData.EnsureBoneData(slotData.umaBoneData, boneMap);

                    umaData.umaRecipe.AddDNAUpdater(slotData.slotDNA);
                }
            }

            umaData.myRenderer.quality = SkinQuality.Bone4;
            umaData.myRenderer.useLightProbes = true;
            umaData.myRenderer.sharedMaterials = combinedMaterialList.ToArray();
            //umaData.myRenderer.sharedMesh.RecalculateBounds();
            umaData.myRenderer.sharedMesh.name = "UMAMesh";

            umaData.firstBake = false;
        }

        protected void CombineByShader()
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;

            for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
            {
                combinedMaterialList.Add(umaData.atlasList.atlas[atlasIndex].materialSample);

                for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
                {

                    combineInstance = new SkinnedMeshCombiner.CombineInstance();
					combineInstance.mesh = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh;
					combineInstance.destMesh = new int[combineInstance.mesh.subMeshCount];
					for (int i = 0; i < combineInstance.mesh.subMeshCount; i++)
					{
						combineInstance.destMesh[i] = -1;
					}

                    combineInstance.bones = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.bones;
                    combineInstance.destMesh[umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.subMeshIndex] = atlasIndex;
                    combinedMeshList.Add(combineInstance);
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
						combineInstance.mesh = slots[slotIndex].meshRenderer.sharedMesh;
						combineInstance.destMesh = new int[combineInstance.mesh.subMeshCount];
						for (int i = 0; i < combineInstance.mesh.subMeshCount; i++)
						{
							combineInstance.destMesh[i] = -1;
						}

                        combineInstance.bones = slots[slotIndex].meshRenderer.bones;
                        combineInstance.destMesh[slots[slotIndex].subMeshIndex] = indexCount;
                        combinedMeshList.Add(combineInstance);

                        Material tempMaterial = Instantiate(slots[slotIndex].materialSample) as Material;
                        tempMaterial.name = slots[slotIndex].slotName;
                        for (int textureType = 0; textureType < textureNameList.Length; textureType++)
                        {
                            if (tempMaterial.HasProperty(textureNameList[textureType]))
                            {
                                slots[slotIndex].GetOverlay(0).textureList[textureType].filterMode = FilterMode.Bilinear;
                                tempMaterial.SetTexture(textureNameList[textureType], slots[slotIndex].GetOverlay(0).textureList[textureType]);
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
                                    if (slots[slotIndex].GetOverlay(0).textureList[0].name == slots[slotIndex2].GetOverlay(0).textureList[0].name)
                                    {
                                        combineInstance = new SkinnedMeshCombiner.CombineInstance();
										combineInstance.mesh = slots[slotIndex2].meshRenderer.sharedMesh;
										combineInstance.destMesh = new int[combineInstance.mesh.subMeshCount];
										for (int i = 0; i < combineInstance.mesh.subMeshCount; i++)
										{
											combineInstance.destMesh[i] = -1;
										}

                                        combineInstance.bones = slots[slotIndex2].meshRenderer.bones;

                                        combineInstance.destMesh[slots[slotIndex2].subMeshIndex] = indexCount;
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

        protected void RecalculateUV()
        {
            List<Rect> tempAtlasRect = new List<Rect>();
            List<int> meshVertexAmount = new List<int>();

            for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
            {
                for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
                {
                    tempAtlasRect.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].atlasRegion);
                    meshVertexAmount.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh.vertexCount);
                }
            }

            Vector2[] originalUVs = umaData.myRenderer.sharedMesh.uv;
            Vector2[] atlasUVs = new Vector2[originalUVs.Length];

            int rectIndex = 0;
            int vertTracker = 0;

            for (int i = 0; i < atlasUVs.Length; i++)
            {

                atlasUVs[i].x = Mathf.Lerp(tempAtlasRect[rectIndex].xMin / atlasResolution, tempAtlasRect[rectIndex].xMax / atlasResolution, originalUVs[i].x);
                atlasUVs[i].y = Mathf.Lerp(tempAtlasRect[rectIndex].yMin / atlasResolution, tempAtlasRect[rectIndex].yMax / atlasResolution, originalUVs[i].y);

                if (i >= (meshVertexAmount[rectIndex] + vertTracker) - 1)
                {
                    vertTracker = vertTracker + meshVertexAmount[rectIndex];
                    rectIndex++;
                }
            }
            umaData.myRenderer.sharedMesh.uv = atlasUVs;
        }	

    }
}
