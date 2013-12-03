using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
    public class UMALegacyMeshCombiner : UMAMeshCombiner
    {
		UMAGenerator umaGenerator;	
		Matrix4x4 tempMatrix;
		
		List<CombineInstance> combinedGroupedList;
		List<CombineInstance> combinedMeshList;
		List<Material> combinedMaterialList;
		List<SlotData> combinedSlotList;

        public override void UpdateUMAMesh(bool updatedAtlas, UMAGenerator umaGenerator)
        {
            this.umaGenerator = umaGenerator;

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
			
			
			Matrix4x4[] bindposesArray = umaGenerator.umaData.myRenderer.sharedMesh.bindposes;
			
			
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
			
			Mesh newMesh = umaGenerator.umaData.firstBake ? new Mesh() : umaGenerator.umaData.myRenderer.sharedMesh;
			newMesh.CombineMeshes(combinedGroupedList.ToArray(),false,false);
	        
			for(int i = 0; i < combinedGroupedList.Count; i++){
				GameObject.DestroyImmediate(combinedGroupedList[i].mesh);
			}


			newMesh.boneWeights = boneWeightsArray;
	        newMesh.bindposes = bindposesArray;
	        umaGenerator.umaData.myRenderer.sharedMesh = newMesh;
			
			if(updatedAtlas){
				RecalculateUV();
			}
			
			umaGenerator.umaData.umaRecipe.ClearDNAConverters();
	        for (int i = 0; i < umaGenerator.umaData.umaRecipe.slotDataList.Length; i++)
	        {
	            SlotData slotData = umaGenerator.umaData.umaRecipe.slotDataList[i];
				if(slotData != null){
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

		void CombineByShader(){
			
			for(int atlasIndex = 0; atlasIndex < umaGenerator.umaData.atlasList.atlas.Count; atlasIndex++){
				combinedMaterialList.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].materialSample);
				combinedMeshList.Clear();
				for(int materialDefinitionIndex = 0; materialDefinitionIndex < umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++){
					CombineInstance combineInstance = new CombineInstance();
					combineInstance.mesh = umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh;
					combineInstance.transform = tempMatrix;
		            combinedMeshList.Add(combineInstance);
					
					combinedSlotList.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData);
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
	        SlotData[] slots = umaGenerator.umaData.umaRecipe.slotDataList;
	        bool[] shareMaterial = new bool[slots.Length];
			
			int indexCount = 0;
	        for(int slotIndex = 0; slotIndex < slots.Length; slotIndex++){
				combinedMeshList.Clear();
				
				if(slots[slotIndex] != null){
					if(!shareMaterial[slotIndex]){
						CombineInstance combineInstance = new CombineInstance();
			            combineInstance.mesh = slots[slotIndex].meshRenderer.sharedMesh;
			            combineInstance.transform = tempMatrix;
			            combinedMeshList.Add(combineInstance);
						combinedSlotList.Add(slots[slotIndex]);
						Material tempMaterial = UnityEngine.Object.Instantiate(slots[slotIndex].materialSample) as Material;
						tempMaterial.name = slots[slotIndex].slotName;
						for(int textureType = 0; textureType < umaGenerator.textureNameList.Length; textureType++){
							if(tempMaterial.HasProperty(umaGenerator.textureNameList[textureType])){
								slots[slotIndex].GetOverlay(0).textureList[textureType].filterMode = FilterMode.Bilinear;
								tempMaterial.SetTexture(umaGenerator.textureNameList[textureType],slots[slotIndex].GetOverlay(0).textureList[textureType]);
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
			
			for(int atlasIndex = 0; atlasIndex < umaGenerator.umaData.atlasList.atlas.Count; atlasIndex++){
				for(int materialDefinitionIndex = 0; materialDefinitionIndex < umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++){
					tempAtlasRect.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].atlasRegion);
					meshVertexAmount.Add(umaGenerator.umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh.vertexCount);
				}
			}

			Vector2[] originalUVs = umaGenerator.umaData.myRenderer.sharedMesh.uv;
	        Vector2[] atlasUVs = new Vector2[originalUVs.Length];
			
	        int rectIndex = 0;
	        int vertTracker = 0;
			
			for(int i = 0; i < atlasUVs.Length; i++ ) {
				
				atlasUVs[i].x = Mathf.Lerp( tempAtlasRect[rectIndex].xMin/umaGenerator.atlasResolution, tempAtlasRect[rectIndex].xMax/umaGenerator.atlasResolution, originalUVs[i].x );
	            atlasUVs[i].y = Mathf.Lerp( tempAtlasRect[rectIndex].yMin/umaGenerator.atlasResolution, tempAtlasRect[rectIndex].yMax/umaGenerator.atlasResolution, originalUVs[i].y );            
				
				if(originalUVs[i].x > 1 || originalUVs[i].y > 1){
					Debug.Log(i);	
				}
				
	            if(i >= (meshVertexAmount[rectIndex] + vertTracker) - 1) {
					vertTracker = vertTracker + meshVertexAmount[rectIndex];
	                rectIndex++;
	            }
	        }
			umaGenerator.umaData.myRenderer.sharedMesh.uv = atlasUVs;	
		}
		
		
	}
}