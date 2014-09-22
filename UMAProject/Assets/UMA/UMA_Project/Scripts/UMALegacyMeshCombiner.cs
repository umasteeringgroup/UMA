using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
    public class UMALegacyMeshCombiner : UMAMeshCombiner
    {
		Matrix4x4 tempMatrix;
		
		List<CombineInstance> combinedGroupedList;
		List<CombineInstance> combinedMeshList;
		List<Material> combinedMaterialList;
		List<SlotData> combinedSlotList;
        UMAData umaData;
        string[] textureNameList;
        int atlasResolution;

        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, string[] textureNameList, int atlasResolution)
        {
            this.umaData = umaData;
            this.textureNameList = textureNameList;
            this.atlasResolution = atlasResolution;

			combinedGroupedList = new List<CombineInstance>();
			combinedMeshList = new List<CombineInstance>();
	        combinedMaterialList = new List<Material>();
			combinedSlotList = new List<SlotData>();
			
	        if (updatedAtlas)
	        {
	            CombineByShader();
	        }
	        else
	        {
				CombineByMaterial();
	        }
			
			
			Matrix4x4[] bindposesArray = umaData.myRenderer.sharedMesh.bindposes;
			
			
			tempMatrix = Matrix4x4.identity;

			
			int boneArraySize = 0;
			int boneArrayIndex = 0;
		
			for(int i = 0; i < combinedSlotList.Count; i++){
				boneArraySize = boneArraySize + combinedSlotList[i].boneWeights.Length;
			}

			BoneWeight[] boneWeightsArray = new BoneWeight[boneArraySize];		
			
			for(int i = 0; i < combinedSlotList.Count; i++){
				
				BoneWeight[] tempBoneWeights = combinedSlotList[i].boneWeights;
				
				for(int boneWeightCount = 0; boneWeightCount < tempBoneWeights.Length; boneWeightCount++){              	
					boneWeightsArray[boneArrayIndex] = tempBoneWeights[boneWeightCount];
					boneArrayIndex++;
				}
			}
			
			Mesh newMesh = umaData.firstBake ? new Mesh() : umaData.myRenderer.sharedMesh;
			newMesh.CombineMeshes(combinedGroupedList.ToArray(),false,false);
	        
			for(int i = 0; i < combinedGroupedList.Count; i++){
				GameObject.DestroyImmediate(combinedGroupedList[i].mesh);
			}


			newMesh.boneWeights = boneWeightsArray;
	        newMesh.bindposes = bindposesArray;
	        umaData.myRenderer.sharedMesh = newMesh;
			
			if(updatedAtlas){
				RecalculateUV();
			}
			
			umaData.umaRecipe.ClearDNAConverters();
	        for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
	        {
	            SlotData slotData = umaData.umaRecipe.slotDataList[i];
				if(slotData != null){
	            	umaData.umaRecipe.AddDNAUpdater(slotData.slotDNA);
				}
	        }
			
			umaData.myRenderer.quality = SkinQuality.Bone4;
	        //umaData.myRenderer.useLightProbes = true;
	        umaData.myRenderer.sharedMaterials = combinedMaterialList.ToArray();
			//umaData.myRenderer.sharedMesh.RecalculateBounds();
	        umaData.myRenderer.sharedMesh.name = "UMAMesh";
			
			umaData.firstBake = false;			
	    }

		void CombineByShader(){
			
			for(int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++){
				combinedMaterialList.Add(umaData.atlasList.atlas[atlasIndex].materialSample);
				combinedMeshList.Clear();
				for(int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++){
					CombineInstance combineInstance = new CombineInstance();
					combineInstance.mesh = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh;
					combineInstance.transform = tempMatrix;
                    combineInstance.subMeshIndex = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.subMeshIndex;
		            combinedMeshList.Add(combineInstance);
					
					combinedSlotList.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData);
				}
				
				Mesh tempMesh = new Mesh();
				tempMesh.CombineMeshes(combinedMeshList.ToArray(), true, false);
				CombineInstance tempCombineInstance = new CombineInstance();
				tempCombineInstance.mesh = tempMesh;
				tempCombineInstance.transform = tempMatrix;
				combinedGroupedList.Add(tempCombineInstance);
			}
		}

		void CombineByMaterial()
	    {		
	        SlotData[] slots = umaData.umaRecipe.slotDataList;
	        bool[] shareMaterial = new bool[slots.Length];
			
			int indexCount = 0;
	        for(int slotIndex = 0; slotIndex < slots.Length; slotIndex++){
				combinedMeshList.Clear();
				
				if(slots[slotIndex] != null){
					if(!shareMaterial[slotIndex]){
						CombineInstance combineInstance = new CombineInstance();
			            combineInstance.mesh = slots[slotIndex].meshRenderer.sharedMesh;
                        combineInstance.subMeshIndex = slots[slotIndex].subMeshIndex;
			            combineInstance.transform = tempMatrix;
			            combinedMeshList.Add(combineInstance);
						combinedSlotList.Add(slots[slotIndex]);
						Material tempMaterial = UnityEngine.Object.Instantiate(slots[slotIndex].materialSample) as Material;
						tempMaterial.name = slots[slotIndex].slotName;
						for(int textureType = 0; textureType < textureNameList.Length; textureType++){
							if(tempMaterial.HasProperty(textureNameList[textureType])){
								slots[slotIndex].GetOverlay(0).textureList[textureType].filterMode = FilterMode.Bilinear;
								tempMaterial.SetTexture(textureNameList[textureType],slots[slotIndex].GetOverlay(0).textureList[textureType]);
							}
						}
						combinedMaterialList.Add(tempMaterial);
						
						
						shareMaterial[slotIndex] = true;
						
						for(int slotIndex2 = slotIndex; slotIndex2 < slots.Length; slotIndex2++){
							if(slots[slotIndex2] != null){
								if(slotIndex2 != slotIndex && !shareMaterial[slotIndex2]){
									if(slots[slotIndex].GetOverlay(0).textureList[0].name == slots[slotIndex2].GetOverlay(0).textureList[0].name){	
										combineInstance = new CombineInstance();
							            combineInstance.mesh = slots[slotIndex2].meshRenderer.sharedMesh;
                                        combineInstance.subMeshIndex = slots[slotIndex2].subMeshIndex;
							            combineInstance.transform = tempMatrix;
							            combinedMeshList.Add(combineInstance);
										combinedSlotList.Add(slots[slotIndex2]);
										shareMaterial[slotIndex2] = true;
									}
								}
							}
						}
						
						Mesh tempMesh = new Mesh();
						tempMesh.CombineMeshes(combinedMeshList.ToArray(), true, false);
						CombineInstance tempCombineInstance = new CombineInstance();
						tempCombineInstance.mesh = tempMesh;
						tempCombineInstance.transform = tempMatrix;
						combinedGroupedList.Add(tempCombineInstance);
						
						indexCount++;
						
					}
				}else{
					shareMaterial[slotIndex] = true;
				}
			}
		}
		
		void RecalculateUV(){
			List<Rect> tempAtlasRect = new List<Rect>();
			List<int> meshVertexAmount = new List<int>();
			
			for(int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++){
				for(int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++){
					tempAtlasRect.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].atlasRegion);
					meshVertexAmount.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh.vertexCount);
				}
			}

			Vector2[] originalUVs = umaData.myRenderer.sharedMesh.uv;
	        Vector2[] atlasUVs = new Vector2[originalUVs.Length];
			
	        int rectIndex = 0;
	        int vertTracker = 0;
			
			for(int i = 0; i < atlasUVs.Length; i++ ) {
				
				atlasUVs[i].x = Mathf.Lerp( tempAtlasRect[rectIndex].xMin/atlasResolution, tempAtlasRect[rectIndex].xMax/atlasResolution, originalUVs[i].x );
	            atlasUVs[i].y = Mathf.Lerp( tempAtlasRect[rectIndex].yMin/atlasResolution, tempAtlasRect[rectIndex].yMax/atlasResolution, originalUVs[i].y );            
				
				if(originalUVs[i].x > 1 || originalUVs[i].y > 1){
					Debug.Log(i);	
				}
				
	            if(i >= (meshVertexAmount[rectIndex] + vertTracker) - 1) {
					vertTracker = vertTracker + meshVertexAmount[rectIndex];
	                rectIndex++;
	            }
	        }
			umaData.myRenderer.sharedMesh.uv = atlasUVs;	
		}
		
		
	}
}