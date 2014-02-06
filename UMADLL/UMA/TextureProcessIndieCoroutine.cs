using UnityEngine;
using System.Collections;
using System.Collections.Generic;



namespace UMA
{
    public class TextureProcessIndieCoroutine : TextureProcessBaseCoroutine
	{	
		UMAData umaData;
		Texture2D[] resultingTextures;
		Color32[] destinationColorList;
        UMAGeneratorBase umaGenerator;
		CopyTextureRectCoroutine copyTextureRectCoroutine;
	    CopyAdditiveTextureRectCoroutine copyAdditiveTextureRectCoroutine;
	    CopyColorizedTextureRectCoroutine copyColorizedTextureRectCoroutine;
	    CopyColorizedAdditiveTextureRectCoroutine copyColorizedAdditiveTextureRectCoroutine;
	    BlendTextureRectCoroutine blendTextureRectCoroutine;
	    BlendAdditiveTextureRectCoroutine blendAdditiveTextureRectCoroutine;
	    ColorizeAdditiveTextureRectCoroutine colorizeAdditiveTextureRectCoroutine;
	    ColorizeTextureRectCoroutine colorizeTextureRectCoroutine;
		float resolutionScale;
		int mipmapScale;
		
	    public override void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator)
	    {
			umaData = _umaData;
			umaGenerator = _umaGenerator;
	    }

	    protected override void Start()
	    {
			copyTextureRectCoroutine = new CopyTextureRectCoroutine();
	        copyAdditiveTextureRectCoroutine = new CopyAdditiveTextureRectCoroutine();
	        blendTextureRectCoroutine = new BlendTextureRectCoroutine();
	        blendAdditiveTextureRectCoroutine = new BlendAdditiveTextureRectCoroutine();
			colorizeTextureRectCoroutine = new ColorizeTextureRectCoroutine();
	        colorizeAdditiveTextureRectCoroutine = new ColorizeAdditiveTextureRectCoroutine();
	        copyColorizedTextureRectCoroutine = new CopyColorizedTextureRectCoroutine();
	        copyColorizedAdditiveTextureRectCoroutine = new CopyColorizedAdditiveTextureRectCoroutine();
	    }

	    protected override IEnumerator workerMethod()
	    {	
			
			for(int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex ++){

                var atlas = umaData.atlasList.atlas[atlasIndex];
                var slotData = atlas.atlasMaterialDefinitions[0].source.slotData;
                var textureNameList = umaGenerator.textureNameList;
                if (slotData.textureNameList != null && slotData.textureNameList.Length > 0)
                {
                    textureNameList = slotData.textureNameList;
                }
                
                resultingTextures = new Texture2D[textureNameList.Length];
				destinationColorList = new Color32[Mathf.FloorToInt(atlas.cropResolution.x*atlas.cropResolution.y)];
				Rect nullRect = new Rect(0,0,0,0);
				for(int textureType = 0; textureType < textureNameList.Length; textureType++){

                    if (string.IsNullOrEmpty(textureNameList[textureType])) continue;
					if(atlas.atlasMaterialDefinitions[0].source.materialSample.HasProperty(textureNameList[textureType])){
						for(int i = 0; i < atlas.atlasMaterialDefinitions.Count; i++){
							
							UMAData.AtlasMaterialDefinition atlasElement = atlas.atlasMaterialDefinitions[i];
							resolutionScale = atlas.resolutionScale * atlas.atlasMaterialDefinitions[i].source.slotData.overlayScale;
							
							if(atlas.atlasMaterialDefinitions[i].source.slotData.overlayScale != 1.0f){
								mipmapScale = Mathf.FloorToInt(Mathf.Log(1/(resolutionScale),2));
							}else{
								mipmapScale = atlas.mipmap;
							}

							if(!atlasElement.isRectShared){
							
								Color32[] baseColorList = atlasElement.source.baseTexture[textureType].GetPixels32(mipmapScale);
		                   		Color32 baseColor = atlasElement.source.GetMultiplier(0, textureType);
								Color32 additiveColor = atlasElement.source.GetAdditive(0, textureType);
								
								if (baseColor.Equals(new Color32(255,255,255,255)))
				                {
			                        if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
			                        {
			                            copyTextureRectCoroutine.Prepare(destinationColorList, baseColorList, atlasElement.atlasRegion, atlas.cropResolution,
			                            new Vector2(atlasElement.source.baseTexture[textureType].width * resolutionScale, atlasElement.source.baseTexture[textureType].height * resolutionScale), umaGenerator.maxPixels);
			
			                            yield return copyTextureRectCoroutine;
			                        }
			                        else
			                        {
			                            copyAdditiveTextureRectCoroutine.Prepare(destinationColorList, baseColorList, additiveColor, atlasElement.atlasRegion, atlas.cropResolution,
			                            new Vector2(atlasElement.source.baseTexture[textureType].width * resolutionScale, atlasElement.source.baseTexture[textureType].height * resolutionScale), umaGenerator.maxPixels);
			
			                            yield return copyTextureRectCoroutine;
			                        }
			                    }
			                    else
			                    {
			                        if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
			                        {
			                            copyColorizedTextureRectCoroutine.Prepare(destinationColorList, baseColorList, baseColorList, baseColor, atlasElement.atlasRegion, atlas.cropResolution,
			                            new Vector2(atlasElement.source.baseTexture[0].width * resolutionScale, atlasElement.source.baseTexture[0].height * resolutionScale), umaGenerator.maxPixels);
			
			                            yield return copyColorizedTextureRectCoroutine;
			                        }
			                        else
			                        {
										
			                            copyColorizedAdditiveTextureRectCoroutine.Prepare(destinationColorList, baseColorList, baseColorList, baseColor, additiveColor, atlasElement.atlasRegion, atlas.cropResolution,
			                            new Vector2(atlasElement.source.baseTexture[0].width * resolutionScale, atlasElement.source.baseTexture[0].height * resolutionScale), umaGenerator.maxPixels);
			
			                            yield return copyColorizedAdditiveTextureRectCoroutine;
			                        }
								}
								
						
		
								for(int i2 = 0; i2 < atlasElement.source.overlays.Length; i2++){
									//Change baseColorList based on overlays

                                    if (atlasElement.source.overlays[i2].textureList[textureType] == null)
                                    {
                                        continue;
                                    }
			
			                        baseColor = atlasElement.source.GetMultiplier(i2+1, textureType);
			                        additiveColor = atlasElement.source.GetAdditive(i2+1, textureType);
			                        
			                        Color32[] overlayColorList = atlasElement.source.overlays[i2].textureList[textureType].GetPixels32(mipmapScale);
									Color32[] maskColorList = atlasElement.source.overlays[i2].textureList[0].GetPixels32(mipmapScale);
									
									Rect insertRect;
									if(atlasElement.source.rects[i2] != nullRect){
										insertRect = new Rect(Mathf.FloorToInt(atlasElement.atlasRegion.x + atlasElement.source.rects[i2].x*resolutionScale),Mathf.FloorToInt(atlasElement.atlasRegion.y + atlasElement.source.rects[i2].y*resolutionScale),Mathf.FloorToInt(atlasElement.source.rects[i2].width*resolutionScale),Mathf.FloorToInt(atlasElement.source.rects[i2].height*resolutionScale));
									}else{
										insertRect = atlasElement.atlasRegion;
									}
			
			                        if( baseColor.Equals(new Color32(255,255,255,255) ) )
			                        {
			                            if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
			                            {
			                                blendTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, insertRect, atlas.cropResolution,
			                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
			
			                                yield return null; //Because we are using an GetPixels32 above
			                                yield return blendTextureRectCoroutine;
			                            }
			                            else
			                            {
			                                blendAdditiveTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, additiveColor, insertRect, atlas.cropResolution,
			                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
			
			                                yield return null; //Because we are using an GetPixels32 above
			                                yield return blendAdditiveTextureRectCoroutine;
			                            }
			                        }
			                        else						
				                    {
			                            if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
			                            {
			                                colorizeTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, baseColor, insertRect, atlas.cropResolution,
			                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
			
			                                yield return null; //Because we are using an GetPixels32 above
			                                yield return colorizeTextureRectCoroutine;
			                            }
			                            else
			                            {
			                                colorizeAdditiveTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, baseColor, additiveColor, insertRect, atlas.cropResolution,
			                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
			
			
			                                yield return null; //Because we are using an GetPixels32 above
			                                yield return colorizeAdditiveTextureRectCoroutine;
			                            }
				                    }
								}
							}
							
						}
						resultingTextures[textureType] = new Texture2D(Mathf.FloorToInt(atlas.cropResolution.x),Mathf.FloorToInt(atlas.cropResolution.y),TextureFormat.ARGB32,true);
						yield return null;
						
						resultingTextures[textureType].SetPixels32(destinationColorList);
						yield return null;
						
						resultingTextures[textureType].Apply();
						yield return null;
					}
				}
				

				umaData.atlasList.atlas[atlasIndex].resultingAtlasList = resultingTextures;
				umaData.atlasList.atlas[atlasIndex].materialSample = UnityEngine.Object.Instantiate(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[0].source.materialSample) as Material;
				umaData.atlasList.atlas[atlasIndex].materialSample.name = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[0].source.materialSample.name;
				for(int finalTextureType = 0; finalTextureType < textureNameList.Length; finalTextureType++){
                    if (string.IsNullOrEmpty(textureNameList[finalTextureType])) continue;
                    if (umaData.atlasList.atlas[atlasIndex].materialSample.HasProperty(textureNameList[finalTextureType]))
                    {
						umaData.atlasList.atlas[atlasIndex].materialSample.SetTexture(textureNameList[finalTextureType],resultingTextures[finalTextureType]);
					}
				}

			}

		}	

		
	    protected override void Stop()
	    {
			destinationColorList = null;
			resultingTextures = null;
	    }
	}
}