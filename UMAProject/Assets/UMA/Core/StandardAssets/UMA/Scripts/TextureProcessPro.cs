using UnityEngine;
using System.Collections;
using System;
using CopyTextureSupport = UnityEngine.Rendering.CopyTextureSupport;
using GraphicsDeviceType = UnityEngine.Rendering.GraphicsDeviceType;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// Texture processing coroutine using rendertextures for atlas building.
    /// </summary>
    [Serializable]
    public class TextureProcessPRO
    {
        static string[] alphaMaskProperties = {"_AlphaMask","_AlphaMask1","_AlphaMask2","_AlphaMask3", "_AlphaMask4", "_AlphaMask5", "_AlphaMask6", "_AlphaMask7" };
        static Dictionary<RenderTextureFormat, TextureFormat> TextureFormats = new Dictionary<RenderTextureFormat, TextureFormat>()
        {
            {RenderTextureFormat.ARGB32, TextureFormat.ARGB32 },
            {RenderTextureFormat.ARGB4444, TextureFormat.ARGB4444 },
            {RenderTextureFormat.BGRA32, TextureFormat.BGRA32 },
            {RenderTextureFormat.RFloat , TextureFormat.RFloat },
            {RenderTextureFormat.R8 , TextureFormat.R8 },
            {RenderTextureFormat.RG16 , TextureFormat.R16 },
            {RenderTextureFormat.RGB565 , TextureFormat.RGB565 },
            {RenderTextureFormat.RHalf , TextureFormat.RHalf },
            {RenderTextureFormat.RGFloat, TextureFormat.RGFloat},
            {RenderTextureFormat.RGHalf , TextureFormat.RGHalf },
            {RenderTextureFormat.ARGB1555 , TextureFormat.ARGB32 }
        };
        UMAData umaData;
        RenderTexture destinationTexture;
        Texture[] resultingTextures;
        UMAGeneratorBase umaGenerator;
        bool fastPath = false;

        public bool SupportsRTToTexture2D
        {
            get
            {
                return (CopyTextureSupport.RTToTexture & SystemInfo.copyTextureSupport) == CopyTextureSupport.RTToTexture;
            }
        }

        public static RenderTexture ResizeRenderTexture(RenderTexture source, int newWidth, int newHeight, FilterMode filter)
        {
            source.filterMode = filter;
            RenderTexture rt = new RenderTexture(newWidth, newHeight, 0, source.format, RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Point;

            RenderTexture bkup = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            RenderTexture.active = bkup;
            return rt;
        }




        /// <summary>
        /// Setup data for atlas building.
        /// </summary>
        /// <param name="_umaData">UMA data.</param>
        /// <param name="_umaGenerator">UMA generator.</param>
        public void ProcessTexture(UMAData _umaData, UMAGeneratorBase _umaGenerator)
        {
            umaData = _umaData;
            umaGenerator = _umaGenerator;
            if (umaGenerator is UMAGenerator)
            {
                fastPath = (umaGenerator as UMAGenerator).fastGeneration;
            }

            if (umaData.atlasResolutionScale <= 0) umaData.atlasResolutionScale = 1f;
            var textureMerge = umaGenerator.textureMerge;
            textureMerge.RefreshMaterials();
            if (textureMerge == null)
            {
                if (Debug.isDebugBuild)
                    Debug.LogError("TextureMerge is null!");
                // yield return null;
            }
            try
            {
                for (int atlasIndex = umaData.generatedMaterials.materials.Count - 1; atlasIndex >= 0; atlasIndex--)
                {
                    var generatedMaterial = umaData.generatedMaterials.materials[atlasIndex];

                    //Rendering Atlas
                    int moduleCount = 0;

                    //Process all necessary TextureModules
                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                    {
                        if (!generatedMaterial.materialFragments[i].isRectShared && !generatedMaterial.materialFragments[i].isNoTextures)
                        {
                            moduleCount++;
                            moduleCount = moduleCount + generatedMaterial.materialFragments[i].overlays.Length;
                        }
                    }
                    textureMerge.EnsureCapacity(moduleCount);

                    var slotData = generatedMaterial.materialFragments[0].slotData;
                    resultingTextures = new Texture[slotData.material.channels.Length];
                    for (int textureChannelNumber = slotData.material.channels.Length - 1; textureChannelNumber >= 0; textureChannelNumber--)
                    {
                        switch (slotData.material.channels[textureChannelNumber].channelType)
                        {
                            case UMAMaterial.ChannelType.Texture:
                            case UMAMaterial.ChannelType.DiffuseTexture:
                            case UMAMaterial.ChannelType.NormalMap:
                            case UMAMaterial.ChannelType.DetailNormalMap:
                                {
                                    bool CopyRTtoTex = SupportsRTToTexture2D && fastPath && (umaGenerator.convertRenderTexture || slotData.material.channels[textureChannelNumber].ConvertRenderTexture);
                                    if (CopyRTtoTex && !TextureFormats.ContainsKey(slotData.material.channels[textureChannelNumber].textureFormat))
                                    {
                                        CopyRTtoTex = false;
                                    }

                                    textureMerge.Reset();
                                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                                    {
                                        textureMerge.SetupModule(generatedMaterial, i, textureChannelNumber);
                                    }

                                    //last element for this textureType
                                    moduleCount = 0;

                                    int width = Mathf.FloorToInt(generatedMaterial.cropResolution.x);
                                    int height = Mathf.FloorToInt(generatedMaterial.cropResolution.y);

                                    if (width == 0 || height == 0)
                                    {
                                        continue;
                                    }

                                    float downSample = (slotData.material.channels[textureChannelNumber].DownSample == 0) ? 1f : (1f / slotData.material.channels[textureChannelNumber].DownSample);

                                    int ww = Mathf.FloorToInt(generatedMaterial.cropResolution.x * umaData.atlasResolutionScale * downSample);
                                    int hh = Mathf.FloorToInt(generatedMaterial.cropResolution.y * umaData.atlasResolutionScale * downSample);

                                    if (ww == 0 || hh == 0) continue;

                                    if (CopyRTtoTex)
                                    {
                                        destinationTexture = RenderTexture.GetTemporary(ww, hh, 0, slotData.material.channels[textureChannelNumber].textureFormat, RenderTextureReadWrite.Linear);
                                        if (destinationTexture.useMipMap != umaGenerator.convertMipMaps)
                                        {
                                            if (destinationTexture.IsCreated())
                                            {
                                                destinationTexture.Release();
                                            }
                                            destinationTexture.useMipMap = umaGenerator.convertMipMaps;
                                            if (destinationTexture.IsCreated())
                                            {
                                                destinationTexture.Create();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        destinationTexture = new RenderTexture(ww, hh, 0, slotData.material.channels[textureChannelNumber].textureFormat, RenderTextureReadWrite.Linear);
                                        destinationTexture.useMipMap = umaGenerator.convertMipMaps; // && !umaGenerator.convertRenderTexture;
                                    }
                                    destinationTexture.filterMode = FilterMode.Point;
                                    destinationTexture.name = slotData.material.name + " Chan " + textureChannelNumber + " frame: " + Time.frameCount;

                                    //This draw the rects
                                    Color backgroundColor;
                                    UMAMaterial.ChannelType channelType = slotData.material.channels[textureChannelNumber].channelType;

                                    if (slotData.material.MaskWithCurrentColor && (channelType == UMAMaterial.ChannelType.DiffuseTexture || channelType == UMAMaterial.ChannelType.Texture))
                                    {
                                        backgroundColor = slotData.material.maskMultiplier * textureMerge.camBackgroundColor;
                                    }
                                    else
                                    {
                                        backgroundColor = UMAMaterial.GetBackgroundColor(slotData.material.channels[textureChannelNumber].channelType);
                                    }

                                    textureMerge.DrawAllRects(destinationTexture, width, height, backgroundColor, umaGenerator.SharperFitTextures);

                                    //PostProcess
                                    textureMerge.PostProcess(destinationTexture, slotData.material.channels[textureChannelNumber].channelType);

                                    if (CopyRTtoTex)
                                    {
                                        #region Convert Render Textures
                                        // copy the texture with mips to the Texture2D
                                        Texture2D tempTexture;

                                        TextureFormat texFmt = TextureFormats[destinationTexture.format];
                                        tempTexture = new Texture2D(destinationTexture.width, destinationTexture.height, texFmt, umaGenerator.convertMipMaps, true);
                                        Graphics.CopyTexture(destinationTexture, tempTexture);
                                        RenderTexture.ReleaseTemporary(destinationTexture);
                                        //destinationTexture.Release();
                                        //UnityEngine.GameObject.DestroyImmediate(destinationTexture);
                                        resultingTextures[textureChannelNumber] = tempTexture as Texture;
                                        SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, tempTexture);
                                        #endregion
                                    }
                                    else
                                    {
                                        SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, destinationTexture);
                                        resultingTextures[textureChannelNumber] = destinationTexture;
                                    } 

                                    break;
                                }
                            case UMAMaterial.ChannelType.MaterialColor:
                                {
                                    if (slotData.material.channels[textureChannelNumber].NonShaderTexture) break;
                                    generatedMaterial.material.SetColor(slotData.material.channels[textureChannelNumber].materialPropertyName, generatedMaterial.materialFragments[0].baseColor);
                                    break;
                                }
                            case UMAMaterial.ChannelType.TintedTexture:
                                {
                                    UMAData.MaterialFragment fragment = null;

                                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                                    {
                                        var frag = generatedMaterial.materialFragments[i];
                                        if (frag.isRectShared) continue;

                                        fragment = frag;
                                    }

                                    if (fragment == null) break;

                                    //var overlay0 = fragment.slotData.GetOverlay(0);
                                    //SetChannelTexture(umaData, textureChannelNumber, 0, generatedMaterial.material, overlay0);

                                    for (int i=0;i<slotData.OverlayCount;i++)
                                    {
                                        OverlayData overlay = slotData.GetOverlay(i);
                                        SetChannelTexture(umaData, textureChannelNumber, i, generatedMaterial.material, overlay);
                                    }

                                    bool isCompositor = generatedMaterial.material.HasProperty("_OverlayCount");

                                    if (textureChannelNumber == 0 && isCompositor)
                                    {
                                        /* set all the properties on the material */
                                        int numChannels = fragment.baseOverlay.textureList.Length;
                                        int numOverlays = 1 + fragment.overlays.Length;

                                        Color[] ColorTints = new Color[numChannels * numOverlays];
                                        Color[] ColorAdds = new Color[numChannels * numOverlays];

                                        var overlays = fragment.slotData.GetOverlayList();

                                        int i = 0;
                                        foreach (var overlay in overlays)
                                        {
                                            for (int c = 0; c < numChannels; c++)
                                            {
                                                if (overlay != null)
                                                {
                                                    ColorTints[i] = overlay.GetColor(c);
                                                    ColorAdds[i] = overlay.GetAdditive(c);
                                                }
                                                else
                                                {
                                                    ColorTints[i] = Color.white;
                                                    ColorAdds[i] = OverlayColorData.EmptyAdditive;
                                                }
                                                i++;
                                            } 
                                        }

                                      
                                        generatedMaterial.material.SetInt("_OverlayCount", numOverlays);
                                        generatedMaterial.material.SetColorArray("ColorTints", ColorTints);
                                        generatedMaterial.material.SetColorArray("ColorAdds", ColorAdds);
                                    }
                                    else if (textureChannelNumber == 0)
                                    {
                                        Debug.Log("Setting base color");
                                        generatedMaterial.material.color = fragment.baseColor;
                                    }

                                    /*
                                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                                    {
                                        var fragment = generatedMaterial.materialFragments[i];
                                        if (fragment.isRectShared) continue;

                                        int numChannels = fragment.baseOverlay.textureList.Length;
                                        int numOverlays = 1 + fragment.overlays.Length;

                                        Material mat = generatedMaterial.material;

                                        var overlay0 = fragment.slotData.GetOverlay(0);
                                        //SetChannelTexture(textureChannelNumber, mat,overlay0)


                                        Color[] ColorTints = new Color[numChannels * numOverlays];
                                        Color[] ColorAdds  = new Color[numChannels * numOverlays];

                                        var baseOverrides = (umaData.GetTextureOverrides(overlay0.overlayName));

                                        if (overlay0.alphaMask != null)
                                        {
                                            if (mat.HasProperty(alphaMaskProperties[0]))
                                            {
                                                mat.SetTexture(alphaMaskProperties[0], overlay0.alphaMask);
                                            }
                                        }
                                        for (int j = 0; j < fragment.baseOverlay.textureList.Length; j++)
                                        {

                                            ColorTints[j] = overlay0.colorData.color;
                                            ColorAdds[j] = overlay0.colorData.Add;

                                            var theTex = fragment.baseOverlay.textureList[j];

                                            if (baseOverrides != null)
                                            {
                                                if (baseOverrides.ContainsKey(j))
                                                {
                                                    theTex = baseOverrides[j];
                                                }
                                            }

                                            if (theTex != null)
                                            {
                                                if (!slotData.material.channels[textureChannelNumber].NonShaderTexture)
                                                {
                                                    if (generatedMaterial.umaMaterial.translateSRP)
                                                    {
                                                        generatedMaterial.material.SetTexture(UMAUtils.TranslatedSRPTextureName(slotData.material.channels[j].materialPropertyName), theTex);
                                                    }
                                                    else
                                                    {
                                                        generatedMaterial.material.SetTexture(slotData.material.channels[j].materialPropertyName, theTex);
                                                    }
                                                }
                                                if (j == 0)
                                                {
                                                    generatedMaterial.material.color = fragment.baseColor;
                                                }
                                            }
                                        }

                                        int OverlayNumber = 1;
                                        foreach (var overlay in fragment.overlays)
                                        {
                                            var actoverlay = fragment.slotData.GetOverlay(OverlayNumber);
                                            var overlayOverrides = (umaData.GetTextureOverrides(actoverlay.overlayName));

                                            int colorbase = OverlayNumber * numChannels;

                                            if (actoverlay.alphaMask != null)
                                            {

                                                if (mat.HasProperty(alphaMaskProperties[OverlayNumber]))
                                                {
                                                    mat.SetTexture(alphaMaskProperties[OverlayNumber], actoverlay.alphaMask);
                                                }
                                            }


                                            for (int j = 0; j < overlay.textureList.Length; j++)
                                            {
                                                ColorTints[colorbase + j] = actoverlay.colorData.GetTint(j);
                                                ColorAdds[colorbase + j] = actoverlay.colorData.GetAdditive(j);

                                                var theTex = overlay.textureList[j];

                                                if (overlayOverrides != null)
                                                {
                                                    if (overlayOverrides.ContainsKey(j))
                                                    {
                                                        theTex = overlayOverrides[j];
                                                    }
                                                }

                                                if (theTex != null)
                                                {
                                                    if (!slotData.material.channels[textureChannelNumber].NonShaderTexture)
                                                    {
                                                        string materialPropertyName;
                                                        if (generatedMaterial.umaMaterial.translateSRP)
                                                        {
                                                            materialPropertyName = UMAUtils.TranslatedSRPTextureName(slotData.material.channels[j].materialPropertyName) + (OverlayNumber);
                                                        }
                                                        else
                                                        {
                                                            materialPropertyName = slotData.material.channels[j].materialPropertyName + (theTex);
                                                        }

                                                        // if the shader has a parameter for this extra texture, then set it.
                                                        // example, if the texture channel property name is "_MainTex", then the first additional overlay would be _MainTex1.
                                                        // The shader would need to be written in such a way as to do the combine in the shader itself.
                                                        if (generatedMaterial.material.HasProperty(materialPropertyName))
                                                        {
                                                            generatedMaterial.material.SetTexture(materialPropertyName, theTex);
                                                        }
                                                    }
                                                }
                                            }
                                            OverlayNumber++;
                                        }
                                        if (generatedMaterial.material.HasProperty("_OverlayCount"))
                                        {
                                            generatedMaterial.material.SetInt("_OverlayCount", numOverlays);
                                        }
                                        if (generatedMaterial.material.HasProperty("_ColorTints"))
                                        {
                                            generatedMaterial.material.SetColorArray("_ColorTints", ColorTints);
                                        }
                                        if (generatedMaterial.material.HasProperty("_ColorAdds"))
                                        {
                                            generatedMaterial.material.SetColorArray("_ColorAdds", ColorAdds);
                                        }
                                } */
                                    break;
                                }
                        }
                    }
                    generatedMaterial.resultingAtlasList = resultingTextures;
                }
            }
            finally
            {
                RenderTexture.active = null;
            }
        }
 
        private static void SetChannelTexture(UMAData umaData, int textureChannelNumber, int overlayNumber, Material mat, OverlayData overlay0)
        {
            var overrides = (umaData.GetTextureOverrides(overlay0.overlayName));
            var theTex = overlay0.GetTexture(textureChannelNumber);
            var overlayOverrides = (umaData.GetTextureOverrides(overlay0.overlayName));
            var umaMaterial = overlay0.asset.material;

            if (overlayOverrides != null)
            {
                if (overlayOverrides.ContainsKey(textureChannelNumber))
                {
                    theTex = overlayOverrides[textureChannelNumber];
                }
            }

            string materialPropertyName;
            if (umaMaterial.translateSRP)
            {
                materialPropertyName = UMAUtils.TranslatedSRPTextureName(overlay0.asset.material.channels[textureChannelNumber].materialPropertyName);
            }
            else
            {
                materialPropertyName = umaMaterial.channels[textureChannelNumber].materialPropertyName;
            }

            if (overlayNumber > 0)
            {
                materialPropertyName += overlayNumber.ToString();
            }

            

            // if the shader has a parameter for this extra texture, then set it.
            // example, if the texture channel property name is "_MainTex", then the first additional overlay would be _MainTex1.
            // The shader would need to be written in such a way as to do the combine in the shader itself.
            if (mat.HasProperty(materialPropertyName))
            {
                mat.SetTexture(materialPropertyName, theTex);
            }
            string alphaMaskName = "_AlphaMask" + overlayNumber.ToString();
            if (mat.HasProperty(alphaMaskName))
            {
                if (overlay0.alphaMask != null)
                {
                    mat.SetTexture(alphaMaskName, overlay0.alphaMask);
                }
                else
                {
                    mat.SetTexture(alphaMaskName, overlay0.GetTexture(0));
                }
            }
        }

        private static void SetMaterialTexture(UMAData.GeneratedMaterial generatedMaterial, SlotData slotData, int textureType, Texture tempTexture)
        {
            tempTexture.wrapMode = TextureWrapMode.Repeat;
            tempTexture.anisoLevel = slotData.material.AnisoLevel;
            tempTexture.mipMapBias = slotData.material.MipMapBias;
            tempTexture.filterMode = slotData.material.MatFilterMode;

            if (!slotData.material.channels[textureType].NonShaderTexture)
            {
                if (generatedMaterial.umaMaterial.translateSRP)
                {
                    generatedMaterial.material.SetTexture(UMAUtils.TranslatedSRPTextureName(slotData.material.channels[textureType].materialPropertyName), tempTexture);
                }
                else
                {
                    generatedMaterial.material.SetTexture(slotData.material.channels[textureType].materialPropertyName, tempTexture);
                }
            }
        }

        private bool IsOpenGL()
        {
            var graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion;
            return graphicsDeviceVersion.StartsWith("OpenGL");
        }
    }
}
