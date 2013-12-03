using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
    public class UMADefaultMeshCombiner : UMAMeshCombiner
    {
		UMAGenerator umaGenerator;	
		Matrix4x4 tempMatrix;

        protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
        protected List<Material> combinedMaterialList;
		

        public override void UpdateUMAMesh(bool updatedAtlas, UMAGenerator umaGenerator)
        {
            this.umaGenerator = umaGenerator;
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

            if (umaGenerator.umaData.firstBake)
            {
                umaGenerator.umaData.myRenderer.sharedMesh = new Mesh();
            }
            else
            {
                umaGenerator.umaData.cleanMesh(false);
            }

            var boneMap = new Dictionary<Transform, Transform>();
            SkinnedMeshCombiner.CombineMeshes(umaGenerator.umaData.myRenderer, combinedMeshList.ToArray(), boneMap);

            if (updatedAtlas)
            {
                RecalculateUV();
            }

            umaGenerator.umaData.umaRecipe.ClearDNAConverters();
            for (int i = 0; i < umaGenerator.umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slotData = umaGenerator.umaData.umaRecipe.slotDataList[i];
                if (slotData != null)
                {

                    umaGenerator.umaData.EnsureBoneData(slotData.umaBoneData, boneMap);

                    umaGenerator.umaData.umaRecipe.AddDNAUpdater(slotData.slotDNA);
                }
            }

            umaGenerator.umaData.myRenderer.quality = SkinQuality.Bone4;
            umaGenerator.umaData.myRenderer.useLightProbes = true;
            umaGenerator.umaData.myRenderer.sharedMaterials = combinedMaterialList.ToArray();
            //umaGenerator.umaData.myRenderer.sharedMesh.RecalculateBounds();
            umaGenerator.umaData.myRenderer.sharedMesh.name = "UMAMesh";

            umaGenerator.umaData.firstBake = false;
        }

        protected void CombineByShader()
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;

            for (int atlasIndex = 0; atlasIndex < umaGenerator.umaData.atlasList.atlas.Count; atlasIndex++)
            {
                combinedMaterialList.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].materialSample);

                for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
                {

                    combineInstance = new SkinnedMeshCombiner.CombineInstance();

                    combineInstance.destMesh = new int[1];
                    combineInstance.mesh = umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh;
                    combineInstance.bones = umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.bones;

                    combineInstance.destMesh[0] = atlasIndex;
                    combinedMeshList.Add(combineInstance);
                }
            }
        }

        protected void CombineByMaterial()
        {
            SlotData[] slots = umaGenerator.umaData.umaRecipe.slotDataList;
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
                        combineInstance.destMesh = new int[1];
                        combineInstance.mesh = slots[slotIndex].meshRenderer.sharedMesh;
                        combineInstance.bones = slots[slotIndex].meshRenderer.bones;

                        combineInstance.destMesh[0] = indexCount;
                        combinedMeshList.Add(combineInstance);

                        Material tempMaterial = Instantiate(slots[slotIndex].materialSample) as Material;
                        tempMaterial.name = slots[slotIndex].slotName;
                        for (int textureType = 0; textureType < umaGenerator.textureNameList.Length; textureType++)
                        {
                            if (tempMaterial.HasProperty(umaGenerator.textureNameList[textureType]))
                            {
                                slots[slotIndex].GetOverlay(0).textureList[textureType].filterMode = FilterMode.Bilinear;
                                tempMaterial.SetTexture(umaGenerator.textureNameList[textureType], slots[slotIndex].GetOverlay(0).textureList[textureType]);
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
                                        combineInstance.destMesh = new int[1];
                                        combineInstance.mesh = slots[slotIndex2].meshRenderer.sharedMesh;
                                        combineInstance.bones = slots[slotIndex2].meshRenderer.bones;

                                        combineInstance.destMesh[0] = indexCount;
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

            for (int atlasIndex = 0; atlasIndex < umaGenerator.umaData.atlasList.atlas.Count; atlasIndex++)
            {
                for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
                {
                    tempAtlasRect.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].atlasRegion);
                    meshVertexAmount.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh.vertexCount);
                }
            }

            Vector2[] originalUVs = umaGenerator.umaData.myRenderer.sharedMesh.uv;
            Vector2[] atlasUVs = new Vector2[originalUVs.Length];

            int rectIndex = 0;
            int vertTracker = 0;

            for (int i = 0; i < atlasUVs.Length; i++)
            {

                atlasUVs[i].x = Mathf.Lerp(tempAtlasRect[rectIndex].xMin / umaGenerator.atlasResolution, tempAtlasRect[rectIndex].xMax / umaGenerator.atlasResolution, originalUVs[i].x);
                atlasUVs[i].y = Mathf.Lerp(tempAtlasRect[rectIndex].yMin / umaGenerator.atlasResolution, tempAtlasRect[rectIndex].yMax / umaGenerator.atlasResolution, originalUVs[i].y);

                if (i >= (meshVertexAmount[rectIndex] + vertTracker) - 1)
                {
                    vertTracker = vertTracker + meshVertexAmount[rectIndex];
                    rectIndex++;
                }
            }
            umaGenerator.umaData.myRenderer.sharedMesh.uv = atlasUVs;
        }	

    }
}
