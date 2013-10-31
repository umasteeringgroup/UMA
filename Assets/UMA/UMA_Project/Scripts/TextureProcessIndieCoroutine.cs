using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class TextureProcessIndieCoroutine : WorkerCoroutine
{
	
	UMAData umaData;
	Texture2D[] resultingTextures;
	Color32[] destinationColorList;
	UMAGenerator umaGenerator;
	CopyTextureRectCoroutine copyTextureRectCoroutine;
    CopyAdditiveTextureRectCoroutine copyAdditiveTextureRectCoroutine;
    CopyColorizedTextureRectCoroutine copyColorizedTextureRectCoroutine;
    CopyColorizedAdditiveTextureRectCoroutine copyColorizedAdditiveTextureRectCoroutine;
    BlendTextureRectCoroutine blendTextureRectCoroutine;
    BlendAdditiveTextureRectCoroutine blendAdditiveTextureRectCoroutine;
    ColorizeAdditiveTextureRectCoroutine colorizeAdditiveTextureRectCoroutine;
    ColorizeTextureRectCoroutine colorizeTextureRectCoroutine;
	int atlasIndex;
	float resolutionScale;
	int mipmapScale;
	
    public void Prepare(UMAData _umaData,UMAGenerator _umaGenerator)
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
		
			resultingTextures = new Texture2D[umaGenerator.textureNameList.Length];
			destinationColorList = new Color32[Mathf.FloorToInt(umaData.atlasList.atlas[atlasIndex].cropResolution.x*umaData.atlasList.atlas[atlasIndex].cropResolution.y)];
			Rect nullRect = new Rect(0,0,0,0);
			
			for(int textureType = 0; textureType < umaGenerator.textureNameList.Length; textureType++){
				
				if(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[0].source.materialSample.HasProperty(umaGenerator.textureNameList[textureType])){
					for(int i = 0; i < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; i++){
						
						UMAData.AtlasMaterialDefinition atlasElement = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i];
						resolutionScale = umaData.atlasList.atlas[atlasIndex].resolutionScale * umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i].source.slotData.overlayScale;
						
						if(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i].source.slotData.overlayScale != 1.0f){
							mipmapScale = Mathf.FloorToInt(Mathf.Log(1/(resolutionScale),2));
						}else{
							mipmapScale = umaData.atlasList.atlas[atlasIndex].mipmap;
						}

						if(!atlasElement.isRectShared){
						
							Color32[] baseColorList = atlasElement.source.baseTexture[textureType].GetPixels32(mipmapScale);
	                   		Color32 baseColor = atlasElement.source.GetMultiplier(0, textureType);
							Color32 additiveColor = atlasElement.source.GetAdditive(0, textureType);
							
							if (baseColor.Equals(new Color32(255,255,255,255)))
			                {
		                        if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
		                        {
		                            copyTextureRectCoroutine.Prepare(destinationColorList, baseColorList, atlasElement.atlasRegion, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                            new Vector2(atlasElement.source.baseTexture[textureType].width * resolutionScale, atlasElement.source.baseTexture[textureType].height * resolutionScale), umaGenerator.maxPixels);
		
		                            yield return copyTextureRectCoroutine;
		                        }
		                        else
		                        {
		                            copyAdditiveTextureRectCoroutine.Prepare(destinationColorList, baseColorList, additiveColor, atlasElement.atlasRegion, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                            new Vector2(atlasElement.source.baseTexture[textureType].width * resolutionScale, atlasElement.source.baseTexture[textureType].height * resolutionScale), umaGenerator.maxPixels);
		
		                            yield return copyTextureRectCoroutine;
		                        }
		                    }
		                    else
		                    {
		                        if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
		                        {
		                            copyColorizedTextureRectCoroutine.Prepare(destinationColorList, baseColorList, baseColorList, baseColor, atlasElement.atlasRegion, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                            new Vector2(atlasElement.source.baseTexture[0].width * resolutionScale, atlasElement.source.baseTexture[0].height * resolutionScale), umaGenerator.maxPixels);
		
		                            yield return copyColorizedTextureRectCoroutine;
		                        }
		                        else
		                        {
									
		                            copyColorizedAdditiveTextureRectCoroutine.Prepare(destinationColorList, baseColorList, baseColorList, baseColor, additiveColor, atlasElement.atlasRegion, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                            new Vector2(atlasElement.source.baseTexture[0].width * resolutionScale, atlasElement.source.baseTexture[0].height * resolutionScale), umaGenerator.maxPixels);
		
		                            yield return copyColorizedAdditiveTextureRectCoroutine;
		                        }
							}
							
					
	
							for(int i2 = 0; i2 < atlasElement.source.overlays.Length; i2++){
								//Change baseColorList based on overlays
		
		
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
		                                blendTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, insertRect, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
		
		                                yield return null; //Because we are using an GetPixels32 above
		                                yield return blendTextureRectCoroutine;
		                            }
		                            else
		                            {
		                                blendAdditiveTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, additiveColor, insertRect, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
		
		                                yield return null; //Because we are using an GetPixels32 above
		                                yield return blendAdditiveTextureRectCoroutine;
		                            }
		                        }
		                        else						
			                    {
		                            if (additiveColor.Equals(new Color32(0, 0, 0, 0)))
		                            {
		                                colorizeTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, baseColor, insertRect, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
		
		                                yield return null; //Because we are using an GetPixels32 above
		                                yield return colorizeTextureRectCoroutine;
		                            }
		                            else
		                            {
		                                colorizeAdditiveTextureRectCoroutine.Prepare(destinationColorList, overlayColorList, maskColorList, baseColor, additiveColor, insertRect, umaData.atlasList.atlas[atlasIndex].cropResolution,
		                                new Vector2(atlasElement.source.overlays[i2].textureList[textureType].width * resolutionScale, atlasElement.source.overlays[i2].textureList[textureType].height * resolutionScale), umaGenerator.maxPixels);
		
		
		                                yield return null; //Because we are using an GetPixels32 above
		                                yield return colorizeAdditiveTextureRectCoroutine;
		                            }
			                    }
							}
						}
						
					}
					resultingTextures[textureType] = new Texture2D(Mathf.FloorToInt(umaData.atlasList.atlas[atlasIndex].cropResolution.x),Mathf.FloorToInt(umaData.atlasList.atlas[atlasIndex].cropResolution.y),TextureFormat.ARGB32,true);
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
			for(int finalTextureType = 0; finalTextureType < umaGenerator.textureNameList.Length; finalTextureType++){
				if(umaData.atlasList.atlas[atlasIndex].materialSample.HasProperty(umaGenerator.textureNameList[finalTextureType])){
					umaData.atlasList.atlas[atlasIndex].materialSample.SetTexture(umaGenerator.textureNameList[finalTextureType],resultingTextures[finalTextureType]);
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