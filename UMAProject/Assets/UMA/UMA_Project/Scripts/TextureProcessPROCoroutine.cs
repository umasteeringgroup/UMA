using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class TextureProcessPROCoroutine : TextureProcessBaseCoroutine
	{
		Renderer[] textureModuleList;
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
                var atlas = umaData.atlasList.atlas[atlasIndex];

                //Rendering Atlas
                int moduleCount = 0;

                //Process all necessary TextureModules
                for (int i = 0; i < atlas.atlasMaterialDefinitions.Count; i++)
                {
                    if (!atlas.atlasMaterialDefinitions[i].isRectShared)
                    {
                        moduleCount++;
                        moduleCount = moduleCount + atlas.atlasMaterialDefinitions[i].source.overlays.Length;
                    }
                }

                while (umaGenerator.textureMerge.textureModuleList.Count < moduleCount)
                {
                    Transform tempModule = UnityEngine.Object.Instantiate(umaGenerator.textureMerge.textureModule, new Vector3(0, 0, 3), Quaternion.identity) as Transform;
					var moduleRenderer = tempModule.GetComponent<Renderer>();
					moduleRenderer.sharedMaterial = UnityEngine.Object.Instantiate(umaGenerator.textureMerge.material) as Material;
					umaGenerator.textureMerge.textureModuleList.Add(moduleRenderer);
                }

                textureModuleList = umaGenerator.textureMerge.textureModuleList.ToArray();
                for (int i = 0; i < moduleCount; i++)
                {
					textureModuleList[i].transform.localEulerAngles = new Vector3(textureModuleList[i].transform.localEulerAngles.x, 180.0f, textureModuleList[i].transform.localEulerAngles.z);
					textureModuleList[i].transform.parent = umaGenerator.textureMerge.myTransform;
                    textureModuleList[i].name = "tempModule";
                    textureModuleList[i].gameObject.SetActive(true);
                }

                moduleCount = 0;

                var slotData = atlas.atlasMaterialDefinitions[0].source.slotData;
                var textureNameList = umaGenerator.textureNameList;
                if (slotData.textureNameList != null && slotData.textureNameList.Length > 0)
                {
                    textureNameList = slotData.textureNameList;
                }


                resultingTextures = new Texture[textureNameList.Length];
                Rect nullRect = new Rect(0, 0, 0, 0);

                for (int textureType = 0; textureType < textureNameList.Length; textureType++)
                {
                    if (string.IsNullOrEmpty(textureNameList[textureType])) continue;
                    if (atlas.atlasMaterialDefinitions[0].source.materialSample.HasProperty(textureNameList[textureType]))
                    {
                        for (int i = 0; i < atlas.atlasMaterialDefinitions.Count; i++)
                        {
                            UMAData.AtlasMaterialDefinition atlasElement = atlas.atlasMaterialDefinitions[i];
                            resolutionScale = atlas.resolutionScale * atlas.atlasMaterialDefinitions[i].source.slotData.overlayScale;

                            Vector2 offsetAdjust = new Vector2(umaGenerator.atlasResolution / 1024, umaGenerator.atlasResolution / 1024);

                            if (!atlasElement.isRectShared)
                            {
                                if (textureType == 0)
                                {
									textureModuleList[moduleCount].transform.localScale = new Vector3(atlasElement.atlasRegion.width / umaGenerator.atlasResolution, atlasElement.atlasRegion.height / umaGenerator.atlasResolution, 1);

									textureModuleList[moduleCount].transform.localPosition = new Vector3(Mathf.Lerp(-1, 1, (offsetAdjust.x + atlasElement.atlasRegion.x + atlasElement.atlasRegion.width * 0.5f) / umaGenerator.atlasResolution),
                                    Mathf.Lerp(-1, 1, (offsetAdjust.y + atlasElement.atlasRegion.y + atlasElement.atlasRegion.height * 0.5f) / umaGenerator.atlasResolution), 3.0f);
                                }

                                if (atlasElement.source.baseTexture.Length <= textureType)
                                {
                                    Debug.LogError(string.Format("UMA Texture Process PRO. Slot: {0} Overlay: {1}, doesn't have enough textures!", atlasElement.source.slotData.slotName, atlasElement.source.baseTexture[0].name));
                                    yield break;
                                }
                                if (atlasElement.source.baseTexture[textureType])
                                {
                                    atlasElement.source.baseTexture[textureType].filterMode = FilterMode.Point;
                                    atlasElement.source.baseTexture[0].filterMode = FilterMode.Point;
                                }
                                textureModuleList[moduleCount].sharedMaterial.SetTexture("_MainTex", atlasElement.source.baseTexture[textureType]);
                                textureModuleList[moduleCount].sharedMaterial.SetTexture("_ExtraTex", atlasElement.source.baseTexture[0]);
                                textureModuleList[moduleCount].sharedMaterial.SetColor("_Color", atlasElement.source.GetMultiplier(0, textureType));
                                textureModuleList[moduleCount].sharedMaterial.SetColor("_AdditiveColor", atlasElement.source.GetAdditive(0, textureType));
                                textureModuleList[moduleCount].name = atlasElement.source.baseTexture[textureType].name;
                                textureModuleList[moduleCount].enabled = true;

                                var tempModule = textureModuleList[moduleCount];
                                moduleCount++;

                                for (int i2 = 0; i2 < atlasElement.source.overlays.Length; i2++)
                                {
                                    if (atlasElement.source.overlays[i2].textureList[textureType] == null)
                                    {
                                        textureModuleList[moduleCount].enabled = false;
                                        moduleCount++;
                                        continue;
                                    }

                                    if (atlasElement.source.rects[i2] != nullRect)
                                    {
										textureModuleList[moduleCount].transform.localScale = new Vector3((atlasElement.source.rects[i2].width / umaGenerator.atlasResolution) * resolutionScale, (atlasElement.source.rects[i2].height / umaGenerator.atlasResolution) * resolutionScale, 1);
										textureModuleList[moduleCount].transform.localPosition = new Vector3(Mathf.Lerp(-1, 1, (offsetAdjust.x + atlasElement.atlasRegion.x + atlasElement.source.rects[i2].x * resolutionScale + atlasElement.source.rects[i2].width * 0.5f * resolutionScale) / umaGenerator.atlasResolution),
                                        Mathf.Lerp(-1, 1, (offsetAdjust.y + atlasElement.atlasRegion.y + atlasElement.source.rects[i2].y * resolutionScale + atlasElement.source.rects[i2].height * 0.5f * resolutionScale) / umaGenerator.atlasResolution), tempModule.transform.localPosition.z - 0.1f - 0.1f * i2);
                                    }
                                    else
                                    {
										textureModuleList[moduleCount].transform.localScale = tempModule.transform.localScale;
										textureModuleList[moduleCount].transform.localPosition = new Vector3(tempModule.transform.localPosition.x, tempModule.transform.localPosition.y, tempModule.transform.localPosition.z - 0.1f - 0.1f * i2);
                                    }

                                    atlasElement.source.overlays[i2].textureList[textureType].filterMode = FilterMode.Point;
                                    atlasElement.source.overlays[i2].textureList[0].filterMode = FilterMode.Point;

                                    textureModuleList[moduleCount].sharedMaterial.SetTexture("_MainTex", atlasElement.source.overlays[i2].textureList[textureType]);
                                    textureModuleList[moduleCount].sharedMaterial.SetTexture("_ExtraTex", atlasElement.source.overlays[i2].textureList[0]);
                                    textureModuleList[moduleCount].sharedMaterial.SetColor("_Color", atlasElement.source.GetMultiplier(i2 + 1, textureType));
                                    textureModuleList[moduleCount].sharedMaterial.SetColor("_AdditiveColor", atlasElement.source.GetAdditive(i2 + 1, textureType));

                                    textureModuleList[moduleCount].name = atlasElement.source.overlays[i2].textureList[textureType].name;

                                    textureModuleList[moduleCount].enabled = true;
                                    moduleCount++;
                                }
                                //							yield return null;
                            }
                        }

                        //last element for this textureType
                        moduleCount = 0;

                        umaGenerator.textureMerge.gameObject.SetActive(true);

                        destinationTexture = new RenderTexture(Mathf.FloorToInt(atlas.cropResolution.x), Mathf.FloorToInt(atlas.cropResolution.y), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                        destinationTexture.filterMode = FilterMode.Point;
                        renderCamera = umaGenerator.textureMerge.myCamera;
                        Vector3 tempPosition = renderCamera.transform.position;

                        renderCamera.orthographicSize = atlas.cropResolution.y / umaGenerator.atlasResolution;
                        renderCamera.transform.position = tempPosition + (-Vector3.right * (1 - atlas.cropResolution.x / umaGenerator.atlasResolution)) + (-Vector3.up * (1 - renderCamera.orthographicSize));

                        renderCamera.targetTexture = destinationTexture;
                        renderCamera.Render();
                        renderCamera.transform.position = tempPosition;
                        renderCamera.gameObject.SetActive(false);
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
                            tempTexture.wrapMode = TextureWrapMode.Repeat;
                            tempTexture.filterMode = FilterMode.Bilinear;
                            resultingTextures[textureType] = tempTexture;
                        }
                        else
                        {
                            destinationTexture.filterMode = FilterMode.Bilinear;
                            destinationTexture.wrapMode = TextureWrapMode.Repeat;
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
                }

                atlas.resultingAtlasList = resultingTextures;
                atlas.materialSample = UnityEngine.Object.Instantiate(atlas.atlasMaterialDefinitions[0].source.materialSample) as Material;
                atlas.materialSample.name = atlas.atlasMaterialDefinitions[0].source.materialSample.name;
                for (int finalTextureType = 0; finalTextureType < textureNameList.Length; finalTextureType++)
                {
                    if (string.IsNullOrEmpty(textureNameList[finalTextureType])) continue;
                    if (atlas.materialSample.HasProperty(textureNameList[finalTextureType]))
                    {
                        atlas.materialSample.SetTexture(textureNameList[finalTextureType], resultingTextures[finalTextureType]);
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
