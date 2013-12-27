using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class TextureProcessPROCoroutine : TextureProcessBaseCoroutine
	{
		Transform[] textureModuleList;
		UMAData umaData;
		RenderTexture destinationTexture;
		Texture[] resultingTextures;
        UMAGeneratorBase umaGenerator;
		float resolutionScale;
		Camera renderCamera;

        public override void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator)
	    {
			umaData = _umaData;
			umaGenerator = _umaGenerator;
        }

	    protected override void Start()
	    {

		}

        protected override IEnumerator workerMethod()
        {
            for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
            {
                //Rendering Atlas
                int moduleCount = 0;

                //Process all necessary TextureModules
                for (int i = 0; i < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; i++)
                {
                    if (!umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i].isRectShared)
                    {
                        moduleCount++;
                        moduleCount = moduleCount + umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i].source.overlays.Length;
                    }
                }

                while (umaGenerator.textureMerge.textureModuleList.Count < moduleCount)
                {
                    Transform tempModule = UnityEngine.Object.Instantiate(umaGenerator.textureMerge.textureModule, new Vector3(0, 0, 3), Quaternion.identity) as Transform;
                    tempModule.gameObject.renderer.sharedMaterial = UnityEngine.Object.Instantiate(umaGenerator.textureMerge.material) as Material;
                    umaGenerator.textureMerge.textureModuleList.Add(tempModule);
                }

                textureModuleList = umaGenerator.textureMerge.textureModuleList.ToArray();
                for (int i = 0; i < moduleCount; i++)
                {
                    textureModuleList[i].localEulerAngles = new Vector3(textureModuleList[i].localEulerAngles.x, 180.0f, textureModuleList[i].localEulerAngles.z);
                    textureModuleList[i].parent = umaGenerator.textureMerge.myTransform;
                    textureModuleList[i].name = "tempModule";
                    textureModuleList[i].gameObject.SetActive(true);
                }

                moduleCount = 0;

                resultingTextures = new Texture[umaGenerator.textureNameList.Length];
                Rect nullRect = new Rect(0, 0, 0, 0);

                for (int textureType = 0; textureType < umaGenerator.textureNameList.Length; textureType++)
                {

                    if (umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[0].source.materialSample.HasProperty(umaGenerator.textureNameList[textureType]))
                    {
                        for (int i = 0; i < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; i++)
                        {
                            UMAData.AtlasMaterialDefinition atlasElement = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i];
                            resolutionScale = umaData.atlasList.atlas[atlasIndex].resolutionScale * umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[i].source.slotData.overlayScale;

                            Vector2 offsetAdjust = new Vector2(umaGenerator.atlasResolution / 1024, umaGenerator.atlasResolution / 1024);

                            if (!atlasElement.isRectShared)
                            {
                                if (textureType == 0)
                                {
                                    textureModuleList[moduleCount].localScale = new Vector3(atlasElement.atlasRegion.width / umaGenerator.atlasResolution, atlasElement.atlasRegion.height / umaGenerator.atlasResolution, 1);

                                    textureModuleList[moduleCount].localPosition = new Vector3(Mathf.Lerp(-1, 1, (offsetAdjust.x + atlasElement.atlasRegion.x + atlasElement.atlasRegion.width * 0.5f) / umaGenerator.atlasResolution),
                                    Mathf.Lerp(-1, 1, (offsetAdjust.y + atlasElement.atlasRegion.y + atlasElement.atlasRegion.height * 0.5f) / umaGenerator.atlasResolution), 3.0f);
                                }

                                //							Material tempMaterial = UnityEngine.Object.Instantiate(umaGenerator.textureMerge.material) as Material;
                                //							textureModuleList[moduleCount].renderer.material = tempMaterial;

                                if (atlasElement.source.baseTexture[textureType])
                                {
                                    atlasElement.source.baseTexture[textureType].filterMode = FilterMode.Point;
                                    atlasElement.source.baseTexture[0].filterMode = FilterMode.Point;
                                }
                                textureModuleList[moduleCount].renderer.sharedMaterial.SetTexture("_MainTex", atlasElement.source.baseTexture[textureType]);
                                textureModuleList[moduleCount].renderer.sharedMaterial.SetTexture("_ExtraTex", atlasElement.source.baseTexture[0]);
                                textureModuleList[moduleCount].renderer.sharedMaterial.SetColor("_Color", atlasElement.source.GetMultiplier(0, textureType));
                                textureModuleList[moduleCount].renderer.sharedMaterial.SetColor("_AdditiveColor", atlasElement.source.GetAdditive(0, textureType));
                                textureModuleList[moduleCount].name = atlasElement.source.baseTexture[textureType].name;

                                Transform tempModule = textureModuleList[moduleCount];
                                moduleCount++;

                                for (int i2 = 0; i2 < atlasElement.source.overlays.Length; i2++)
                                {

                                    if (atlasElement.source.rects[i2] != nullRect)
                                    {
                                        textureModuleList[moduleCount].localScale = new Vector3((atlasElement.source.rects[i2].width / umaGenerator.atlasResolution) * resolutionScale, (atlasElement.source.rects[i2].height / umaGenerator.atlasResolution) * resolutionScale, 1);
                                        textureModuleList[moduleCount].localPosition = new Vector3(Mathf.Lerp(-1, 1, (offsetAdjust.x + atlasElement.atlasRegion.x + atlasElement.source.rects[i2].x * resolutionScale + atlasElement.source.rects[i2].width * 0.5f * resolutionScale) / umaGenerator.atlasResolution),
                                        Mathf.Lerp(-1, 1, (offsetAdjust.y + atlasElement.atlasRegion.y + atlasElement.source.rects[i2].y * resolutionScale + atlasElement.source.rects[i2].height * 0.5f * resolutionScale) / umaGenerator.atlasResolution), tempModule.localPosition.z - 0.1f - 0.1f * i2);
                                    }
                                    else
                                    {
                                        textureModuleList[moduleCount].localScale = tempModule.localScale;
                                        textureModuleList[moduleCount].localPosition = new Vector3(tempModule.localPosition.x, tempModule.localPosition.y, tempModule.localPosition.z - 0.1f - 0.1f * i2);
                                    }

                                    //								Material tempGenMaterial = umaGenerator.textureMerge.GenerateMaterial(umaGenerator.textureMerge.material);
                                    //								textureModuleList[moduleCount].renderer.material = tempGenMaterial;

                                    atlasElement.source.overlays[i2].textureList[textureType].filterMode = FilterMode.Point;
                                    atlasElement.source.overlays[i2].textureList[0].filterMode = FilterMode.Point;

                                    textureModuleList[moduleCount].renderer.sharedMaterial.SetTexture("_MainTex", atlasElement.source.overlays[i2].textureList[textureType]);
                                    textureModuleList[moduleCount].renderer.sharedMaterial.SetTexture("_ExtraTex", atlasElement.source.overlays[i2].textureList[0]);
                                    textureModuleList[moduleCount].renderer.sharedMaterial.SetColor("_Color", atlasElement.source.GetMultiplier(i2 + 1, textureType));
                                    textureModuleList[moduleCount].renderer.sharedMaterial.SetColor("_AdditiveColor", atlasElement.source.GetAdditive(i2 + 1, textureType));

                                    textureModuleList[moduleCount].name = atlasElement.source.overlays[i2].textureList[textureType].name;

                                    moduleCount++;
                                }
                                //							yield return null;
                            }
                        }

                        //last element for this textureType
                        moduleCount = 0;

                        umaGenerator.textureMerge.gameObject.SetActive(true);

                        destinationTexture = new RenderTexture(Mathf.FloorToInt(umaData.atlasList.atlas[atlasIndex].cropResolution.x), Mathf.FloorToInt(umaData.atlasList.atlas[atlasIndex].cropResolution.y), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                        destinationTexture.filterMode = FilterMode.Point;
                        renderCamera = umaGenerator.textureMerge.myCamera;
                        Vector3 tempPosition = renderCamera.transform.position;

                        renderCamera.orthographicSize = umaData.atlasList.atlas[atlasIndex].cropResolution.y / umaGenerator.atlasResolution;
                        renderCamera.transform.position = tempPosition + (-Vector3.right * (1 - umaData.atlasList.atlas[atlasIndex].cropResolution.x / umaGenerator.atlasResolution)) + (-Vector3.up * (1 - renderCamera.orthographicSize));

                        renderCamera.targetTexture = destinationTexture;
                        renderCamera.Render();
                        renderCamera.transform.position = tempPosition;
                        renderCamera.active = false;
                        renderCamera.targetTexture = null;
                        yield return 25;

                        if (umaGenerator.convertRenderTexture)
                        {
                            Texture2D tempTexture;
                            tempTexture = new Texture2D(destinationTexture.width, destinationTexture.height, TextureFormat.ARGB32, umaGenerator.convertMipMaps);
                            int xblocks = destinationTexture.width / 512;
                            int yblocks = destinationTexture.height / 512;
                            if (xblocks == 0 || yblocks == 0)
                            {
                                RenderTexture.active = destinationTexture;
                                tempTexture.ReadPixels(new Rect(0, 0, destinationTexture.width, destinationTexture.height), 0, 0, umaGenerator.convertMipMaps);
                                RenderTexture.active = null;
                            }
                            else
                            {
                                // figures that ReadPixels works differently on OpenGL and DirectX, someday this code will break because Unity fixes this bug!
                                if (IsOpenGL())
                                {
                                    for (int x = 0; x < xblocks; x++)
                                    {
                                        for (int y = 0; y < yblocks; y++)
                                        {
                                            RenderTexture.active = destinationTexture;
                                            tempTexture.ReadPixels(new Rect(x * 512, y * 512, 512, 512), x * 512, y * 512, umaGenerator.convertMipMaps);
                                            RenderTexture.active = null;
                                            yield return 8;
                                        }
                                    }
                                }
                                else
                                {
                                    for (int x = 0; x < xblocks; x++)
                                    {
                                        for (int y = 0; y < yblocks; y++)
                                        {
                                            RenderTexture.active = destinationTexture;
                                            tempTexture.ReadPixels(new Rect(x * 512, destinationTexture.height - 512 - y * 512, 512, 512), x * 512, y * 512, umaGenerator.convertMipMaps);
                                            RenderTexture.active = null;
                                            yield return 8;
                                        }
                                    }
                                }
                            }
                            resultingTextures[textureType] = tempTexture as Texture;

                            renderCamera.targetTexture = null;
                            RenderTexture.active = null;

                            destinationTexture.Release();
                            UnityEngine.GameObject.DestroyImmediate(destinationTexture);
                            umaGenerator.textureMerge.gameObject.SetActive(false);
                            yield return 6;
                            tempTexture = resultingTextures[textureType] as Texture2D;
                            tempTexture.Apply();
                            resultingTextures[textureType] = tempTexture;
                        }
                        else
                        {
                            destinationTexture.filterMode = FilterMode.Bilinear;
                            resultingTextures[textureType] = destinationTexture;
                        }
                        umaGenerator.textureMerge.gameObject.SetActive(false);
                    }
                    else
                    {

                    }
                }

                for (int textureModuleIndex = 0; textureModuleIndex < textureModuleList.Length; textureModuleIndex++)
                {
                    textureModuleList[textureModuleIndex].gameObject.SetActive(false);
                    //				UnityEngine.Object.DestroyImmediate(textureModuleList[textureModuleIndex].gameObject.renderer.material);
                    //				UnityEngine.Object.DestroyImmediate(textureModuleList[textureModuleIndex].gameObject);
                }

                umaData.atlasList.atlas[atlasIndex].resultingAtlasList = resultingTextures;
                umaData.atlasList.atlas[atlasIndex].materialSample = UnityEngine.Object.Instantiate(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[0].source.materialSample) as Material;
                umaData.atlasList.atlas[atlasIndex].materialSample.name = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[0].source.materialSample.name;
                for (int finalTextureType = 0; finalTextureType < umaGenerator.textureNameList.Length; finalTextureType++)
                {
                    if (umaData.atlasList.atlas[atlasIndex].materialSample.HasProperty(umaGenerator.textureNameList[finalTextureType]))
                    {
                        umaData.atlasList.atlas[atlasIndex].materialSample.SetTexture(umaGenerator.textureNameList[finalTextureType], resultingTextures[finalTextureType]);
                    }
                }
            }
        }

        private bool IsOpenGL()
        {
            var graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion;
            return graphicsDeviceVersion.StartsWith("OpenGL");
        }


		
		
	    protected override void Stop()
	    {
			
		}
	}
}