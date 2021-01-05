using UnityEngine;
using System.Collections;
using System;

namespace UMA
{
    /// <summary>
    /// Texture processing coroutine using rendertextures for atlas building.
    /// </summary>
    [Serializable]
    public class TextureProcessPROCoroutine : TextureProcessBaseCoroutine
    {
        UMAData umaData;
        RenderTexture destinationTexture;
        Texture[] resultingTextures;
        UMAGeneratorBase umaGenerator;
        bool fastPath = false;

        /// <summary>
        /// Setup data for atlas building.
        /// </summary>
        /// <param name="_umaData">UMA data.</param>
        /// <param name="_umaGenerator">UMA generator.</param>
        public override void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator)
        {
            umaData = _umaData;
            umaGenerator = _umaGenerator;
            if (umaGenerator is UMAGenerator)
            {
                fastPath = (umaGenerator as UMAGenerator).fastGeneration;
            }
            if (umaData.atlasResolutionScale <= 0) umaData.atlasResolutionScale = 1f;
        }

        protected override void Start()
        {

        }

        public static RenderTexture ResizeRenderTexture(RenderTexture source, int newWidth, int newHeight, FilterMode filter)
        {
            source.filterMode = filter;
            RenderTexture rt = new RenderTexture(newWidth, newHeight, 0, source.format, RenderTextureReadWrite.Linear);
            rt.name = "Resized Render Texture..." + Time.frameCount;

            rt.filterMode = FilterMode.Point;

            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            return rt;
        }

        protected override IEnumerator workerMethod()
        {
            var textureMerge = umaGenerator.textureMerge;
            textureMerge.RefreshMaterials(); 
            if (textureMerge == null)
            {
                if (Debug.isDebugBuild)
                    Debug.LogError("TextureMerge is null!");
                yield return null;
            }

            for (int atlasIndex = umaData.generatedMaterials.materials.Count - 1; atlasIndex >= 0; atlasIndex--)
            {
                var generatedMaterial = umaData.generatedMaterials.materials[atlasIndex];

                //Rendering Atlas
                int moduleCount = 0;

                //Process all necessary TextureModules
                for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                {
                    if (!generatedMaterial.materialFragments[i].isRectShared)
                    {
                        moduleCount++;
                        moduleCount = moduleCount + generatedMaterial.materialFragments[i].overlays.Length;
                    }
                }
                textureMerge.EnsureCapacity(moduleCount);

                var slotData = generatedMaterial.materialFragments[0].slotData;
                resultingTextures = new Texture[slotData.material.channels.Length];
                for (int textureType = slotData.material.channels.Length - 1; textureType >= 0; textureType--)
                {
                    switch (slotData.material.channels[textureType].channelType)
                    {
                        case UMAMaterial.ChannelType.Texture:
                        case UMAMaterial.ChannelType.DiffuseTexture:
                        case UMAMaterial.ChannelType.NormalMap:
                        case UMAMaterial.ChannelType.DetailNormalMap:
                        {
                            textureMerge.Reset();
                            for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                            {
                                textureMerge.SetupModule(generatedMaterial, i, textureType);
                            }

                            //last element for this textureType
                            moduleCount = 0;

                            int width = Mathf.FloorToInt(generatedMaterial.cropResolution.x);
                            int height = Mathf.FloorToInt(generatedMaterial.cropResolution.y);

                            if (width == 0 || height == 0)
                            {
                                continue;
                            }

                            //this should be restricted to >= 1 but 0 was allowed before and projects may have the umaMaterial value serialized to 0.
                            float downSample = (slotData.material.channels[textureType].DownSample == 0) ? 1f : (1f / slotData.material.channels[textureType].DownSample);

                            destinationTexture = new RenderTexture(Mathf.FloorToInt(generatedMaterial.cropResolution.x * umaData.atlasResolutionScale * downSample), Mathf.FloorToInt(generatedMaterial.cropResolution.y * umaData.atlasResolutionScale * downSample), 0, slotData.material.channels[textureType].textureFormat, RenderTextureReadWrite.Linear);
                            destinationTexture.filterMode = FilterMode.Point;
                            destinationTexture.useMipMap = umaGenerator.convertMipMaps && !umaGenerator.convertRenderTexture;
                            destinationTexture.name = slotData.material.name + " Chan " + textureType + " frame: " + Time.frameCount;
                            
                            //Draw all the Rects here

                            Color backgroundColor;
                            UMAMaterial.ChannelType channelType = slotData.material.channels[textureType].channelType;

                            if (slotData.material.MaskWithCurrentColor && (channelType == UMAMaterial.ChannelType.DiffuseTexture || channelType == UMAMaterial.ChannelType.Texture || channelType == UMAMaterial.ChannelType.TintedTexture))
                            {
                                backgroundColor = slotData.material.maskMultiplier * textureMerge.camBackgroundColor;
                            }
                            else
                            {
                                backgroundColor = UMAMaterial.GetBackgroundColor(slotData.material.channels[textureType].channelType);
                            }


                            textureMerge.DrawAllRects(destinationTexture, width, height, backgroundColor, umaGenerator.SharperFitTextures);
                            
                            //PostProcess
                            textureMerge.PostProcess(destinationTexture, slotData.material.channels[textureType].channelType);

                            if (umaGenerator.convertRenderTexture || slotData.material.channels[textureType].ConvertRenderTexture)
                            {
                                #region Convert Render Textures
                                if (!fastPath) yield return 25;
                                Texture2D tempTexture;

                                tempTexture = new Texture2D(destinationTexture.width, destinationTexture.height, TextureFormat.ARGB32, umaGenerator.convertMipMaps, true);

                                int xblocks = destinationTexture.width / 512;
                                int yblocks = destinationTexture.height / 512;
                                if (xblocks == 0 || yblocks == 0 || fastPath)
                                {
                                    RenderTexture.active = destinationTexture;
                                    //Debug.Log("CVT-FP Activated " + destinationTexture.name);
                                    tempTexture.ReadPixels(new Rect(0, 0, destinationTexture.width, destinationTexture.height), 0, 0, umaGenerator.convertMipMaps);
                                    RenderTexture.active = null;
                                   // Debug.Log("CVT-FP Cleared " + destinationTexture.name);
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
                                               // Debug.Log("CVT-SP OGL Activated " + destinationTexture.name+" "+x+" "+y);
                                                tempTexture.ReadPixels(new Rect(x * 512, y * 512, 512, 512), x * 512, y * 512, umaGenerator.convertMipMaps);
                                                RenderTexture.active = null;
                                               // Debug.Log("CVT-SP OGL Cleared " + destinationTexture.name + " " + x + " " + y);
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
                                               // Debug.Log("CVT-SP NOTOGL Activated " + destinationTexture.name + " " + x + " " + y);
                                                RenderTexture.active = destinationTexture;
                                                tempTexture.ReadPixels(new Rect(x * 512, destinationTexture.height - 512 - y * 512, 512, 512), x * 512, y * 512, umaGenerator.convertMipMaps);
                                                RenderTexture.active = null;
                                             //   Debug.Log("CVT-SP NOTOGL Cleared " + destinationTexture.name + " " + x + " " + y);
                                                yield return 8;
                                            }
                                        }
                                    }
                                }


                                resultingTextures[textureType] = tempTexture as Texture;

                                RenderTexture.active = null;
                                //Debug.Log("CVT Final Cleared " + destinationTexture.name);

                                destinationTexture.Release();
                                UnityEngine.GameObject.DestroyImmediate(destinationTexture);
                                if (!fastPath) yield return 6;
                                tempTexture = resultingTextures[textureType] as Texture2D;
                                tempTexture.Apply();
                                tempTexture.wrapMode = TextureWrapMode.Repeat;
                                tempTexture.anisoLevel = slotData.material.AnisoLevel;
                                tempTexture.mipMapBias = slotData.material.MipMapBias;
                                tempTexture.filterMode = slotData.material.MatFilterMode;
                                if (slotData.material.channels[textureType].Compression != UMAMaterial.CompressionSettings.None)
                                {
                                    tempTexture.Compress(slotData.material.channels[textureType].Compression == UMAMaterial.CompressionSettings.HighQuality);
                                }
                                resultingTextures[textureType] = tempTexture;
                                if (!slotData.material.channels[textureType].NonShaderTexture)
                                {
                                    generatedMaterial.material.SetTexture(slotData.material.channels[textureType].materialPropertyName, tempTexture);
                                }
                                #endregion
                            }
                            else
                            {
                                destinationTexture.anisoLevel = slotData.material.AnisoLevel;
                                destinationTexture.mipMapBias = slotData.material.MipMapBias;
                                destinationTexture.filterMode = slotData.material.MatFilterMode;
                                destinationTexture.wrapMode = TextureWrapMode.Repeat;
                                resultingTextures[textureType] = destinationTexture;
                                if (!slotData.material.channels[textureType].NonShaderTexture)
                                {
                                    generatedMaterial.material.SetTexture(slotData.material.channels[textureType].materialPropertyName, destinationTexture);
                                }
                            }

                            break;
                        }
                        case UMAMaterial.ChannelType.MaterialColor:
                        {
                            if (slotData.material.channels[textureType].NonShaderTexture) break;
                            generatedMaterial.material.SetColor(slotData.material.channels[textureType].materialPropertyName, generatedMaterial.materialFragments[0].baseColor);
                            break;
                        }
                        case UMAMaterial.ChannelType.TintedTexture:
                        {
                            for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                            {
                                var fragment = generatedMaterial.materialFragments[i];
                                if (fragment.isRectShared) continue;
                                for (int j = 0; j < fragment.baseOverlay.textureList.Length; j++)
                                {
                                    if (fragment.baseOverlay.textureList[j] != null)
                                    {
                                        if (!slotData.material.channels[textureType].NonShaderTexture)
                                        {
                                            generatedMaterial.material.SetTexture(slotData.material.channels[j].materialPropertyName, fragment.baseOverlay.textureList[j]);
                                        }
                                        if (j == 0)
                                        {
                                            generatedMaterial.material.color = fragment.baseColor;
                                        }
                                    }
                                }
                                foreach (var overlay in fragment.overlays)
                                {
                                    if (generatedMaterial.textureNameList == null)
                                    for (int j = 0; j < overlay.textureList.Length; j++)
                                    {
                                        if (overlay.textureList[j] != null)
                                        {
                                            if (!slotData.material.channels[textureType].NonShaderTexture)
                                            {
                                                generatedMaterial.material.SetTexture(slotData.material.channels[j].materialPropertyName, overlay.textureList[j]);
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
                generatedMaterial.resultingAtlasList = resultingTextures;
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
