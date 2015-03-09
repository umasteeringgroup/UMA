using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class TextureProcessPROCoroutine : TextureProcessBaseCoroutine
	{
		UMAData umaData;
		RenderTexture destinationTexture;
		Texture[] resultingTextures;
        UMAGeneratorBase umaGenerator;
		Camera renderCamera;

        public override void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator)
	    {
			umaData = _umaData;
			umaGenerator = _umaGenerator;
			if (umaData.atlasResolutionScale <= 0) umaData.atlasResolutionScale = 1f;
        }

	    protected override void Start()
	    {

		}

        protected override IEnumerator workerMethod()
        {
			var textureMerge = umaGenerator.textureMerge;
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

				textureMerge.EnsureCapacity(moduleCount);

                var slotData = atlas.atlasMaterialDefinitions[0].source.slotData;
                var textureNameList = umaGenerator.textureNameList;
                if (slotData.textureNameList != null && slotData.textureNameList.Length > 0)
                {
                    textureNameList = slotData.textureNameList;
                }

                resultingTextures = new Texture[textureNameList.Length];
                for (int textureType = textureNameList.Length-1; textureType >= 0; textureType--)
                {
                    if (string.IsNullOrEmpty(textureNameList[textureType])) continue;
                    if (atlas.atlasMaterialDefinitions[0].source.materialSample.HasProperty(textureNameList[textureType]))
                    {
						textureMerge.Reset();
						for (int i = 0; i < atlas.atlasMaterialDefinitions.Count; i++)
                        {
							textureMerge.SetupModule(atlas, i, textureType);
                        }

                        //last element for this textureType
                        moduleCount = 0;

                        umaGenerator.textureMerge.gameObject.SetActive(true);

						int width = Mathf.FloorToInt(atlas.cropResolution.x);
						int height = Mathf.FloorToInt(atlas.cropResolution.y);
						destinationTexture = new RenderTexture(Mathf.FloorToInt(atlas.cropResolution.x * umaData.atlasResolutionScale), Mathf.FloorToInt(atlas.cropResolution.y * umaData.atlasResolutionScale), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                        destinationTexture.filterMode = FilterMode.Point;
                        renderCamera = umaGenerator.textureMerge.myCamera;
                        renderCamera.targetTexture = destinationTexture;
						renderCamera.orthographicSize = height >> 1;
						var camTransform = renderCamera.GetComponent<Transform>();
						camTransform.localPosition = new Vector3(width >> 1, height >> 1, 3);
						camTransform.localRotation = Quaternion.Euler(0, 180, 180);
						renderCamera.Render();
                        renderCamera.gameObject.SetActive(false);
                        renderCamera.targetTexture = null;

						if (umaGenerator.convertRenderTexture)
                        {
							#region Convert Render Textures
							yield return 25;
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
							#endregion
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
