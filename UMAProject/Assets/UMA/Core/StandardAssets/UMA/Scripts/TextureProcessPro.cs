#undef DEBUG
using UnityEngine;
using System;
using CopyTextureSupport = UnityEngine.Rendering.CopyTextureSupport;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;


namespace UMA
{
    /// <summary>
    /// Texture processing coroutine using rendertextures for atlas building.
    /// </summary>
    [Serializable]
    public class TextureProcessPRO
    {
        static readonly string[,] tintProperties = new string[,]
        {
            {"_Tint0_0","_Tint0_1","_Tint0_2","_Tint0_3" },
            {"_Tint1_0","_Tint1_1","_Tint1_2","_Tint1_3" },
            {"_Tint2_0","_Tint2_1","_Tint2_2","_Tint2_3" },
            {"_Tint3_0","_Tint3_1","_Tint3_2","_Tint3_3" }
        };
        static readonly string[,] addProperties = new string[,]
        {
            {"_Add0_0","_Add0_1","_Add0_2","_Add0_3" },
            {"_Add1_0","_Add1_1","_Add1_2","_Add1_3" },
            {"_Add2_0","_Add2_1","_Add2_2","_Add2_3" },
            {"_Add3_0","_Add3_1","_Add3_2","_Add3_3" }
        };

        static string[] alphaMaskProperties = { "_AlphaMask", "_AlphaMask1", "_AlphaMask2", "_AlphaMask3", "_AlphaMask4", "_AlphaMask5", "_AlphaMask6", "_AlphaMask7" };
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
        public void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator)
        {
            umaData = _umaData;
            umaGenerator = _umaGenerator;
            if (umaData.atlasResolutionScale <= 0)
            {
                umaData.atlasResolutionScale = 1f;
            }
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

            if (umaData.atlasResolutionScale <= 0)
            {
                umaData.atlasResolutionScale = 1f;
            }

            var textureMerge = umaGenerator.textureMerge;
            textureMerge.RefreshMaterials();
            if (textureMerge == null)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogError("TextureMerge is null!");
                }
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
                                    bool CopyRTtoTex = SupportsRTToTexture2D && (umaGenerator.convertRenderTexture || slotData.material.channels[textureChannelNumber].ConvertRenderTexture);
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

                                    if (ww == 0 || hh == 0)
                                    {
                                        continue;
                                    }

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

                                    //This draws all the rects
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

                                    RenderTexture.active = destinationTexture;
                                    textureMerge.DrawAllRects(destinationTexture, width, height, backgroundColor, umaGenerator.SharperFitTextures);

                                    //PostProcess
                                    textureMerge.PostProcess(destinationTexture, slotData.material.channels[textureChannelNumber].channelType);

                                    if (CopyRTtoTex)
                                    {
                                        #region Convert Render Textures
                                        if (umaGenerator.useAsyncConversion)
                                        {

                                            // Let it have the RenderTexture now.
                                            SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, destinationTexture);
                                            resultingTextures[textureChannelNumber] = destinationTexture;
                                            // Now asynchronously copy and reset it
                                            RenderTexToCPU rt2cpu = new RenderTexToCPU(destinationTexture, generatedMaterial, slotData.material.channels[textureChannelNumber].materialPropertyName, textureChannelNumber, umaGenerator);
                                            rt2cpu.DoAsyncCopy();
                                        }
                                        else
                                        {
                                            // copy the texture with mips to the Texture2D
                                            Texture2D tempTexture;                                            
                                            GraphicsFormat gf = GraphicsFormatUtility.GetGraphicsFormat(destinationTexture.format, false);
                                            TextureFormat texFmt = GraphicsFormatUtility.GetTextureFormat(gf);

                                            tempTexture = new Texture2D(destinationTexture.width, destinationTexture.height, texFmt, umaGenerator.convertMipMaps, true);
                                            var asyncAction = AsyncGPUReadback.Request(destinationTexture, 0);
                                            asyncAction.WaitForCompletion();

                                            tempTexture.SetPixelData(asyncAction.GetData<byte>(), 0);
                                            tempTexture.Apply();

                                            RenderTexture.ReleaseTemporary(destinationTexture);

                                            resultingTextures[textureChannelNumber] = tempTexture as Texture;
                                            SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, tempTexture);                                            
                                        }
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
                                    if (slotData.material.channels[textureChannelNumber].NonShaderTexture)
                                    {
                                        break;
                                    }

                                    var propIndex = generatedMaterial.material.shader.FindPropertyIndex(slotData.material.channels[textureChannelNumber].materialPropertyName);
                                    if (propIndex >= 0)
                                    {
                                        // get the type of the property (color, vector, float, etc.
                                        var propType = generatedMaterial.material.shader.GetPropertyType(propIndex);

                                        if (propType == UnityEngine.Rendering.ShaderPropertyType.Color)
                                        {
                                            generatedMaterial.material.SetColor(slotData.material.channels[textureChannelNumber].materialPropertyName, generatedMaterial.materialFragments[0].baseColor);
                                        }
                                        else
                                        {
                                            if (Debug.isDebugBuild)
                                            {
                                                Debug.LogWarning($"Material property {slotData.material.channels[textureChannelNumber].materialPropertyName} is not a color property in UMAMaterial { slotData.material.name }");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (Debug.isDebugBuild)
                                        {
                                            Debug.LogWarning("Material property " + slotData.material.channels[textureChannelNumber].materialPropertyName + " not found in shader " + generatedMaterial.material.shader.name);
                                        }
                                    }

                                    break;
                                }
                            case UMAMaterial.ChannelType.TintedTexture:
                                {
                                    UMAData.MaterialFragment fragment = null;

                                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                                    {
                                        var frag = generatedMaterial.materialFragments[i];
                                        if (frag.isRectShared)
                                        {
                                            continue;
                                        }

                                        fragment = frag;
                                    }

                                    if (fragment == null)
                                    {
                                        break;
                                    }

                                    for (int i = 0; i < slotData.OverlayCount; i++)
                                    {
                                        OverlayData overlay = slotData.GetOverlay(i);
                                        SetChannelTexture(umaData, textureChannelNumber, i, generatedMaterial.material, overlay);
                                    }

                                    bool isCompositor = generatedMaterial.material.HasProperty("_OverlayCount");

                                    if (textureChannelNumber == 0 && isCompositor)
                                    {
                                        /* set all the properties on the material */
                                        SetCompositingProperties(generatedMaterial, generatedMaterial.material, fragment);
                                        /*  We will revert these to arrays in the future
                                        generatedMaterial.material.SetColorArray("ColorTints", ColorTints);
                                        generatedMaterial.material.SetColorArray("ColorAdds", ColorAdds); */
                                    }
                                    else if (textureChannelNumber == 0)
                                    {
                                        generatedMaterial.material.color = fragment.baseColor;
                                    }

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

        public static void SetCompositingProperties(UMAData.GeneratedMaterial generatedMaterial, Material material, UMAData.MaterialFragment fragment)
        {
            if (fragment == null ||fragment.baseOverlay == null || fragment.baseOverlay.textureList == null || fragment.overlays == null)
            {
                return;
            }
            int numChannels = fragment.baseOverlay.textureList.Length;
            int numOverlays = 1 + fragment.overlays.Length;

            Color[] ColorTints = new Color[numChannels * numOverlays];
            Color[] ColorAdds = new Color[numChannels * numOverlays];

            var overlays = fragment.slotData.GetOverlayList();

            int i = 0;
            float referenceWidth = fragment.baseOverlay.textureList[0].width;
            float referenceHeight = fragment.baseOverlay.textureList[0].height;

            for (int ovl = 0; ovl < overlays.Count; ovl++)
            {
                OverlayData overlay = overlays[ovl];


                // apply tileable properties.
                // apply UV offset properties.
                // x,y,z,w = xuv, yuv, xwidth, ywidth
                
                string tileProperty = "_UseTiling"+ovl;
                if (ovl > 0 && material.HasProperty(tileProperty))
                {
                    float tiling = 0;
                    if (overlay.IsTextureTiled(0))
                    {
                        tiling = 1;
                    }
                    Debug.Log("Setting tiling " + tiling + " on " + material.name);
                    material.SetFloat(tileProperty, tiling);
                }
                else
                {
                    if (ovl > 0) 
                    {
                        Debug.Log($"No tiling property {tileProperty} on {material.name} for overlay {overlay.overlayName}");
                    }
                }
                string offsetProperty = "_UV_Offset" + ovl;
                if (material.HasProperty(offsetProperty))
                {
                    Vector4 uv = overlay.GetUV(referenceWidth,referenceHeight);
                    material.SetVector("_UV_Offset" + ovl, uv);
                    Debug.Log("Setting uv offset " + uv + " on " + material.name);
                }
                else
                {
                    List<string> props = new List<string>();
                    int properties = material.shader.GetPropertyCount();
                    for (int p = 0; p < properties; p++)
                    {
                        props.Add(material.shader.GetPropertyName(p));
                    }
                    Debug.Log("Properties on " + material.name + " are " + string.Join(",", props.ToArray()));
                    List<string> props2 = new List<string>();
                }

                for (int c = 0; c < numChannels; c++)
                {
                    // Some shaders use arrays, and some use hardcoded prop names.
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
                    // don't go out of bounds if someone goes crazy with overlays and channels
                    if (c < tintProperties.GetLength(1) && ovl < tintProperties.GetLength(0))
                    {
                        if (material.HasProperty(tintProperties[ovl, c]))
                        {
                            material.SetColor(tintProperties[ovl, c], ColorTints[i]);
                        }
                        if (material.HasProperty(addProperties[ovl, c]))
                        {
                            material.SetColor(addProperties[ovl, c], ColorAdds[i]);
                        }
                    }
                    i++;
                }
            }
            material.SetInt("_OverlayCount", numOverlays);
        }

        private static void SetChannelTexture(UMAData umaData, int textureChannelNumber, int overlayNumber, Material mat, OverlayData overlay0)
        {
#if DEBUG
            Debug.Log("Setting channel texture" + textureChannelNumber + " overlay " + overlayNumber + " on " + mat.name);
#endif
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
                if (umaMaterial.channels == null)
                {
#if DEBUG
                    Debug.LogWarning("Texture channels are null on " + umaMaterial+" are null on Overlay: "+overlay0.overlayName);
#endif
                    return;
                }

                if (textureChannelNumber < 0)
                {
#if DEBUG
                    Debug.LogWarning("Texture channel " + textureChannelNumber + " not found in material " + umaMaterial.name+ "  on Overlay:" +overlay0.overlayName);
#endif
                    return;
                }

                if (textureChannelNumber >= umaMaterial.channels.Length)
                {
#if DEBUG
                    Debug.LogWarning("Texture channel " + textureChannelNumber + " not found in material " + umaMaterial.name + "  on Overlay:" + overlay0.overlayName);
#endif
                    return;
                }
                try
                {
                materialPropertyName = umaMaterial.channels[textureChannelNumber].materialPropertyName;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Exception processing Texture channel " + textureChannelNumber + " in material " + umaMaterial.name +" " + ex.Message);
                    return;
                }
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
            // Debug.Log($"Set Material Texture {tempTexture.name} on Material {generatedMaterial.material.name} for slot {slotData.asset.name} textureType {textureType}");
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
