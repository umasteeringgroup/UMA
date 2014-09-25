using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class UMAGeneratorCoroutine : WorkerCoroutine
	{
		TextureProcessBaseCoroutine textureProcessCoroutine;
			
		MaxRectsBinPack packTexture;
		
		List<UMAData.MaterialDefinition> materialDefinitionList;
		UMAData.MaterialDefinition[] orderedMaterialDefinition;
		
		List<UMAData.AtlasMaterialDefinition> atlasMaterialDefinitionList;
		
		float atlasResolutionScale;
		int mipMapAdjust;

        UMAGeneratorBase umaGenerator;
        UMAData umaData;
		Texture[] backUpTexture;

        public void Prepare(UMAGeneratorBase _umaGenerator, UMAData _umaData, TextureProcessBaseCoroutine textureProcessCoroutine)
	    {
			umaGenerator = _umaGenerator;
            umaData = _umaData;
            this.textureProcessCoroutine = textureProcessCoroutine;
	   }

	    protected override void Start()
	    {
			backUpTexture = umaData.backUpTextures();
			umaData.cleanTextures();
			
			materialDefinitionList = new List<UMAData.MaterialDefinition>();
			
			//Update atlas area can be handled here
			UMAData.MaterialDefinition tempMaterialDefinition = new UMAData.MaterialDefinition();
			
			SlotData[] slots = umaData.umaRecipe.slotDataList;
            for(int i = 0; i < slots.Length; i++)
            {
				if (slots[i] == null) continue;
                if (slots[i].textureNameList.Length == 1 && string.IsNullOrEmpty(slots[i].textureNameList[0]))
                {
                    continue;
                }
                if (slots[i].GetOverlay(0) != null)
                {
					tempMaterialDefinition = new UMAData.MaterialDefinition();
					tempMaterialDefinition.baseTexture = slots[i].GetOverlay(0).textureList;
					tempMaterialDefinition.baseColor = slots[i].GetOverlay(0).color;
					tempMaterialDefinition.materialSample = slots[i].materialSample;
                    int overlays = 0;
                    for (int overlayCounter = 0; overlayCounter < slots[i].OverlayCount; overlayCounter++)
                    {
                        var overlay = slots[i].GetOverlay(overlayCounter);
                        if (overlay != null)
                        {
                            overlays++;
                            if (overlay.useAdvancedMasks)
                            {
                                overlay.EnsureChannels(slots[i].textureNameList.Length);
                            }
                        }
                    }

                    tempMaterialDefinition.overlays = new UMAData.textureData[overlays - 1];
					tempMaterialDefinition.overlayColors = new Color32[tempMaterialDefinition.overlays.Length];
		            tempMaterialDefinition.rects = new Rect[tempMaterialDefinition.overlays.Length];
		            tempMaterialDefinition.channelMask = new Color32[tempMaterialDefinition.overlays.Length+1][];
		            tempMaterialDefinition.channelAdditiveMask = new Color32[tempMaterialDefinition.overlays.Length+1][];
		            tempMaterialDefinition.channelMask[0] = slots[i].GetOverlay(0).channelMask;
		            tempMaterialDefinition.channelAdditiveMask[0] = slots[i].GetOverlay(0).channelAdditiveMask;                
					tempMaterialDefinition.slotData = slots[i];

                    int overlayID = 0;
                    for (int overlayCounter = 0; overlayCounter < slots[i].OverlayCount - 1; overlayCounter++)
                    {
                        var overlay = slots[i].GetOverlay(overlayCounter + 1);
                        if (overlay == null) continue;
						tempMaterialDefinition.overlays[overlayID] = new UMAData.textureData();
						tempMaterialDefinition.rects[overlayID] = overlay.rect;
						tempMaterialDefinition.overlays[overlayID].textureList = overlay.textureList;
						tempMaterialDefinition.overlayColors[overlayID] = overlay.color;
						tempMaterialDefinition.channelMask[overlayID+1] = overlay.channelMask;
		                tempMaterialDefinition.channelAdditiveMask[overlayID+1] = overlay.channelAdditiveMask;
                        overlayID++;
					}
					
					materialDefinitionList.Add(tempMaterialDefinition);
				}
			}
			
			packTexture = new MaxRectsBinPack(umaGenerator.atlasResolution,umaGenerator.atlasResolution,false);
		}

	    protected override IEnumerator workerMethod()
	    {	
			
			orderedMaterialDefinition = new UMAData.MaterialDefinition[materialDefinitionList.Count];
			for(int i = 0; i < materialDefinitionList.Count; i++){
				orderedMaterialDefinition[i] = materialDefinitionList[i];
			}
				
			OrderMaterialDefinition();
			
			//resolutionAdjust code
	        atlasResolutionScale = umaData.atlasResolutionScale == 0f ? 1f : umaData.atlasResolutionScale;
			mipMapAdjust = Mathf.FloorToInt(Mathf.Log(1/(atlasResolutionScale),2));
		
			
			umaData.atlasList = new UMAData.AtlasList();
			umaData.atlasList.atlas = new List<UMAData.AtlasElement>();

			GenerateAtlasData();
			CalculateRects();
			if(umaGenerator.AtlasCrop){
				OptimizeAtlas();		
			}

            textureProcessCoroutine.Prepare(umaData, umaGenerator);
            yield return 6;
            yield return textureProcessCoroutine;
			
			CleanBackUpTextures();
			UpdateUV();
	    }

	    protected override void Stop()
	    {

	    }
		
		private void OrderMaterialDefinition(){
			//Ordering List based on textureSize, for atlas calculation
			for(int i = 0; i < orderedMaterialDefinition.Length; i++){
				int LargestIndex = i;
				
				for(int i2 = i; i2 < orderedMaterialDefinition.Length; i2++){

					if(orderedMaterialDefinition[LargestIndex].baseTexture[0].width*orderedMaterialDefinition[LargestIndex].baseTexture[0].height < orderedMaterialDefinition[i2].baseTexture[0].width*orderedMaterialDefinition[i2].baseTexture[0].height){
						LargestIndex = i2;					
					}
					
					if(i2 == orderedMaterialDefinition.Length-1){
						UMAData.MaterialDefinition tempMaterialDefinition = orderedMaterialDefinition[i];
						orderedMaterialDefinition[i] = orderedMaterialDefinition[LargestIndex];
						orderedMaterialDefinition[LargestIndex] = tempMaterialDefinition;
					}
				}
			}	
		}
		
		private void CleanBackUpTextures(){
			for(int textureIndex = 0; textureIndex < backUpTexture.Length; textureIndex++){		
				if(backUpTexture[textureIndex] != null){
					Texture tempTexture = backUpTexture[textureIndex];
					if(tempTexture is RenderTexture){
						RenderTexture tempRenderTexture = tempTexture as RenderTexture;
						tempRenderTexture.Release();
						UnityEngine.Object.Destroy(tempRenderTexture);
						tempRenderTexture = null;
					}else{
						UnityEngine.Object.Destroy(tempTexture);
					}
					backUpTexture[textureIndex] = null;
				}				
			}
		}
		
		private void GenerateAtlasData(){
			for(int i = 0; i < orderedMaterialDefinition.Length; i++){		
				atlasMaterialDefinitionList = new List<UMAData.AtlasMaterialDefinition>();
				UMAData.AtlasElement atlasElement = new UMAData.AtlasElement();	
				UMAData.AtlasMaterialDefinition tempAtlasMaterialDefinition = new UMAData.AtlasMaterialDefinition();
					
				//This guarantee not including on atlas duplicated textures
				if(orderedMaterialDefinition[i] != null){
					tempAtlasMaterialDefinition.source = orderedMaterialDefinition[i];
					atlasMaterialDefinitionList.Add(tempAtlasMaterialDefinition);
					
					for(int i2 = i; i2 < orderedMaterialDefinition.Length; i2++){
						//Look for same shader
						
						if(orderedMaterialDefinition[i2] != null){
							if(i2 != i){
								tempAtlasMaterialDefinition = new UMAData.AtlasMaterialDefinition();
								
								if(orderedMaterialDefinition[i].materialSample.shader == orderedMaterialDefinition[i2].materialSample.shader){				
									tempAtlasMaterialDefinition.source = orderedMaterialDefinition[i2];
									atlasMaterialDefinitionList.Add(tempAtlasMaterialDefinition);
									orderedMaterialDefinition[i2] = null;
								}
							}
						}
		
						if(i2 == orderedMaterialDefinition.Length-1 && atlasMaterialDefinitionList.Count > 0){
							//All slots sharing same shader are on same atlasElement
							atlasElement.atlasMaterialDefinitions = atlasMaterialDefinitionList;
							atlasElement.shader = atlasMaterialDefinitionList[0].source.materialSample.shader;
							atlasElement.materialSample = atlasMaterialDefinitionList[0].source.materialSample;
                            
							umaData.atlasList.atlas.Add(atlasElement);
						}
					
					}
					
					orderedMaterialDefinition[i] = null;
				}
			}	
		}
		
		
		private void CalculateRects(){
			Rect nullRect = new Rect(0,0,0,0);
			UMAData.AtlasList umaAtlasList = umaData.atlasList;

			
			for(int atlasIndex = 0; atlasIndex < umaAtlasList.atlas.Count; atlasIndex++){
				
				umaAtlasList.atlas[atlasIndex].cropResolution = new Vector2(umaGenerator.atlasResolution,umaGenerator.atlasResolution);
				umaAtlasList.atlas[atlasIndex].resolutionScale = atlasResolutionScale;
				umaAtlasList.atlas[atlasIndex].mipmap = mipMapAdjust;
				packTexture.Init(umaGenerator.atlasResolution,umaGenerator.atlasResolution,false);
				bool textureFit = true;
				
				for(int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex++){
					UMAData.AtlasMaterialDefinition tempMaterialDef = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex];
					
					if(tempMaterialDef.atlasRegion == nullRect){
						
						tempMaterialDef.atlasRegion = packTexture.Insert(Mathf.FloorToInt(tempMaterialDef.source.baseTexture[0].width*umaAtlasList.atlas[atlasIndex].resolutionScale*tempMaterialDef.source.slotData.overlayScale),Mathf.FloorToInt(tempMaterialDef.source.baseTexture[0].height*umaAtlasList.atlas[atlasIndex].resolutionScale*tempMaterialDef.source.slotData.overlayScale),MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestLongSideFit);
						tempMaterialDef.isRectShared = false;
						umaAtlasList.atlas[atlasIndex].shader = tempMaterialDef.source.materialSample.shader;
						
						if(tempMaterialDef.atlasRegion == nullRect){
							textureFit = false;
							
							if(umaGenerator.fitAtlas){
								Debug.LogWarning("Atlas resolution is too small, Textures will be reduced.");
							}else{
								Debug.LogError("Atlas resolution is too small, not all textures will fit.");
							}
						}

						for(int atlasElementIndex2 = atlasElementIndex; atlasElementIndex2 < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex2++){
							if(atlasElementIndex != atlasElementIndex2){
								if(tempMaterialDef.source.baseTexture[0] == umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].source.baseTexture[0]){	
									umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].atlasRegion = tempMaterialDef.atlasRegion;
									umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].isRectShared = true;
								}
							}
						}
												
					}
					
					if(!textureFit && umaGenerator.fitAtlas){
						//Reset calculation and reduce texture sizes
						textureFit = true;
						atlasElementIndex = -1;
						umaAtlasList.atlas[atlasIndex].resolutionScale = umaAtlasList.atlas[atlasIndex].resolutionScale * 0.5f;
						umaAtlasList.atlas[atlasIndex].mipmap ++;
						
						packTexture.Init(umaGenerator.atlasResolution,umaGenerator.atlasResolution,false);					
						for(int atlasElementIndex2 = 0; atlasElementIndex2 < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex2++){
							umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].atlasRegion = nullRect;
						}
					}
				}
			}
		}
		
		private void OptimizeAtlas(){
			UMAData.AtlasList umaAtlasList = umaData.atlasList;
			for(int atlasIndex = 0; atlasIndex < umaAtlasList.atlas.Count; atlasIndex++){
				Vector2 usedArea = new Vector2(0,0);
				for(int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex++){
					if(umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.xMax > usedArea.x){
						usedArea.x = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.xMax;
					}
					
					if(umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.yMax > usedArea.y){
						usedArea.y = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.yMax;
					}
				}
				
				Vector2 tempResolution = new Vector2(umaGenerator.atlasResolution,umaGenerator.atlasResolution);
				
				bool done = false;
				while(!done){
					if(tempResolution.x*0.5f >= usedArea.x){
						tempResolution = new Vector2(tempResolution.x*0.5f,tempResolution.y);
					}else{
						done = true;
					}				
				}
		
				done = false;
				while(!done){
					
					if(tempResolution.y*0.5f >= usedArea.y){
						tempResolution = new Vector2(tempResolution.x,tempResolution.y*0.5f);
					}else{
						done = true;
					}				
				}
				
				umaAtlasList.atlas[atlasIndex].cropResolution = tempResolution;
			}		
		}
		
		private void UpdateUV(){
			UMAData.AtlasList umaAtlasList = umaData.atlasList;
			
			for(int atlasIndex = 0; atlasIndex < umaAtlasList.atlas.Count; atlasIndex++){			
				Vector2 finalAtlasAspect = new Vector2(umaGenerator.atlasResolution/umaAtlasList.atlas[atlasIndex].cropResolution.x,umaGenerator.atlasResolution/umaAtlasList.atlas[atlasIndex].cropResolution.y);
						
				for(int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex++){
					Rect tempRect = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion;
					tempRect.xMin = tempRect.xMin*finalAtlasAspect.x;
					tempRect.xMax = tempRect.xMax*finalAtlasAspect.x;			
					tempRect.yMin = tempRect.yMin*finalAtlasAspect.y;
					tempRect.yMax = tempRect.yMax*finalAtlasAspect.y;
					umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion = tempRect;
				}
			}		
		}
	}
}
