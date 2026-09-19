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
        UMAGeneratorBase umaGenerator;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void StaticInitializeOnLoad()
        {
            TextureFormats = new Dictionary<RenderTextureFormat, TextureFormat>()
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
        }
        public bool SupportsRTToTexture2D
        {
            get
            {
                return (CopyTextureSupport.RTToTexture & SystemInfo.copyTextureSupport) == CopyTextureSupport.RTToTexture;
            }
        }

        private static bool TryGetTextureFormat(RenderTextureFormat renderTextureFormat, out TextureFormat textureFormat)
        {
            if (TextureFormats.TryGetValue(renderTextureFormat, out textureFormat))
            {
                return true;
            }

            try
            {
                GraphicsFormat graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(renderTextureFormat, false);
                textureFormat = GraphicsFormatUtility.GetTextureFormat(graphicsFormat);
                return true;
            }
            catch
            {
                textureFormat = TextureFormat.ARGB32;
                return false;
            }
        }

        private static bool IsExistingTextureChannelType(UMAMaterial.ChannelType channelType)
        {
            switch (channelType)
            {
                case UMAMaterial.ChannelType.Texture:
                case UMAMaterial.ChannelType.DiffuseTexture:
                case UMAMaterial.ChannelType.NormalMap:
                case UMAMaterial.ChannelType.DetailNormalMap:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldUseExistingTextureForChannel(UMAMaterial umaMaterial, UMAMaterial.MaterialChannel channel)
        {
            if (umaMaterial == null)
            {
                return false;
            }

            return umaMaterial.IsGeneratedTextures &&
                   channel.UseExistingTextureForChannel &&
                   !channel.NonShaderTexture &&
                   IsExistingTextureChannelType(channel.channelType);
        }

        private static void SetExistingTexturesForChannel(UMAData umaData, UMAData.GeneratedMaterial generatedMaterial, UMAMaterial umaMaterial, int textureChannelNumber)
        {
            if (generatedMaterial == null || generatedMaterial.material == null || generatedMaterial.materialFragments == null)
            {
                return;
            }

            for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
            {
                UMAData.MaterialFragment fragment = generatedMaterial.materialFragments[i];
                if (fragment == null || fragment.isNoTextures)
                {
                    continue;
                }

                if (fragment.overlayData != null && fragment.overlayData.Length > 0)
                {
                    for (int overlayNumber = 0; overlayNumber < fragment.overlayData.Length; overlayNumber++)
                    {
                        SetChannelTexture(umaData, textureChannelNumber, overlayNumber, generatedMaterial.material, fragment.overlayData[overlayNumber], umaMaterial, false, true);
                    }
                }
                else if (fragment.slotData != null)
                {
                    for (int overlayNumber = 0; overlayNumber < fragment.slotData.OverlayCount; overlayNumber++)
                    {
                        SetChannelTexture(umaData, textureChannelNumber, overlayNumber, generatedMaterial.material, fragment.slotData.GetOverlay(overlayNumber), umaMaterial, false, true);
                    }
                }
            }
        }

        private static void ReleasePreviousGeneratedTexture(Texture[] previousResults, int textureChannelNumber)
        {
            if (previousResults == null || textureChannelNumber < 0 || textureChannelNumber >= previousResults.Length)
            {
                return;
            }

            Texture tempTexture = previousResults[textureChannelNumber];
            previousResults[textureChannelNumber] = null;

            if (tempTexture == null)
            {
                return;
            }
            if (UMAGeneratedResourceCache.IsManagedResource(tempTexture)) return;

            RenderTexture tempRenderTexture = tempTexture as RenderTexture;
            if (tempRenderTexture != null)
            {
                var entityId = tempRenderTexture.GetUmaObjectId();
                if (!RenderTexToCPU.renderTexturesToCPU.ContainsKey(entityId) && RenderTexToCPU.SafeToFree(tempRenderTexture))
                {
                    UMARenderTextureTracker.Untrack(tempRenderTexture);
                    if (tempRenderTexture.IsCreated())
                    {
                        tempRenderTexture.Release();
                    }
                    RenderTexToCPU.renderTexturesCleanedUMAData++;
                    UMAUtils.DestroySceneObject(tempRenderTexture);
                }
                return;
            }

            UMAUtils.DestroySceneObject(tempTexture);
        }

        public static RenderTexture ResizeRenderTexture(RenderTexture source, int newWidth, int newHeight, FilterMode filter)
        {
            source.filterMode = filter;
            RenderTexture rt = new RenderTexture(newWidth, newHeight, 0, source.format, RenderTextureReadWrite.Linear);
            rt.name = string.Format("{0} | Resized {1}x{2}", source.name, newWidth, newHeight);
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
            long lookupStart = System.Diagnostics.Stopwatch.GetTimestamp();
            bool reuseTextures = UMAResourceReuse.TexturesEnabled(umaData);
            bool earlyLookup = reuseTextures && UMAResourceReuse.CanUseEarlyAtlasLookup(textureMerge);
            umaGenerator.atlasLookupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - lookupStart;
            textureMerge.RefreshMaterials();
            if (textureMerge == null)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogError("TextureMerge is null!");
                }
                // yield return null;
            }
            RenderTexture pendingTemporaryTexture = null;
            RenderTexture pendingPersistentTexture = null;
            Texture2D pendingTexture2D = null;
            UMAGeneratedResourceCache.Lease<UMACachedAtlas> pendingAtlasLease = null;
            UMAAtlasBinding[] previousBindingsToRelease = null;
            try
            {
                for (int atlasIndex = umaData.generatedMaterials.materials.Count - 1; atlasIndex >= 0; atlasIndex--)
                {
                    var generatedMaterial = umaData.generatedMaterials.materials[atlasIndex];
                    if (generatedMaterial == null) { continue; }
                    UMAResourceReuse.PrepareMaterialEdits(generatedMaterial);

                    // Prior result (for reuse)
                    var previousResults = generatedMaterial.resultingAtlasList;
                    var previousBindings = generatedMaterial.cachedAtlasBindings;
                    previousBindingsToRelease = previousBindings;

                    //Rendering Atlas
                    int moduleCount = 0;

                    //Process all necessary TextureModules
                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                    {
                        if (!generatedMaterial.materialFragments[i].isRectShared && !generatedMaterial.materialFragments[i].isNoTextures)
                        {
                            moduleCount++;
                            moduleCount = moduleCount + generatedMaterial.materialFragments[i].AdditionalOverlays.Length;
                        }
                    }
                    // Drawing materials are only needed on a cache miss.

                    var slotData = generatedMaterial.materialFragments[0].slotData;
                    var channels = slotData.material.channels;
                    bool materialUseMipMap = slotData.material.generateMipMaps;

                    // Each generated material owns its atlas array. Sharing this array between
                    // materials loses references to earlier atlases when the next material is built.
                    Texture[] resultingTextures = new Texture[channels.Length];
                    generatedMaterial.resultingAtlasList = resultingTextures;
                    generatedMaterial.cachedAtlasBindings = new UMAAtlasBinding[channels.Length];

                    for (int textureChannelNumber = channels.Length - 1; textureChannelNumber >= 0; textureChannelNumber--)
                    {
                        switch (channels[textureChannelNumber].channelType)
                        {
                            case UMAMaterial.ChannelType.Texture:
                            case UMAMaterial.ChannelType.DiffuseTexture:
                            case UMAMaterial.ChannelType.NormalMap:
                            case UMAMaterial.ChannelType.DetailNormalMap:
                                {
                                    UMAMaterial umaMaterial = generatedMaterial.umaMaterial != null ? generatedMaterial.umaMaterial : slotData.material;
                                    if (ShouldUseExistingTextureForChannel(umaMaterial, channels[textureChannelNumber]))
                                    {
                                        SetExistingTexturesForChannel(umaData, generatedMaterial, umaMaterial, textureChannelNumber);
                                        ReleasePreviousGeneratedTexture(previousResults, textureChannelNumber);
                                        break;
                                    }

                                    RenderTextureFormat channelTextureFormat = UMAMaterial.GetCompatibleChannelTextureFormat(channels[textureChannelNumber].textureFormat);
                                    bool CopyRTtoTex = SupportsRTToTexture2D && (umaGenerator.convertRenderTexture || channels[textureChannelNumber].ConvertRenderTexture);
                                    if (CopyRTtoTex)
                                    {
                                        TextureFormat ignoredTextureFormat;
                                        if (!TryGetTextureFormat(channelTextureFormat, out ignoredTextureFormat))
                                        {
                                            CopyRTtoTex = false;
                                        }
                                    }

                                    int width = Mathf.FloorToInt(generatedMaterial.cropResolution.x);
                                    int height = Mathf.FloorToInt(generatedMaterial.cropResolution.y);

                                    if (width == 0 || height == 0)
                                    {
                                        continue;
                                    }

                                    float downSample = (channels[textureChannelNumber].DownSample == 0) ? 1f : (1f / channels[textureChannelNumber].DownSample);

                                    int ww = Mathf.FloorToInt(generatedMaterial.cropResolution.x * umaData.atlasResolutionScale * downSample);
                                    int hh = Mathf.FloorToInt(generatedMaterial.cropResolution.y * umaData.atlasResolutionScale * downSample);

                                    if (ww == 0 || hh == 0)
                                    {
                                        continue;
                                    }

                                    Color backgroundColor = default;
                                    UMAMaterial.ChannelType channelType = channels[textureChannelNumber].channelType;
                                    bool attemptedLookup = false;
                                    if (earlyLookup)
                                    {
                                        lookupStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                        try
                                        {
                                            if (UMAResourceReuse.TryDescribeAtlasInputs(umaData, textureMerge, generatedMaterial,
                                                slotData.material, textureChannelNumber, umaGenerator, width, height, ww, hh,
                                                channelTextureFormat, CopyRTtoTex, out var key))
                                            {
                                                attemptedLookup = true;
                                                pendingAtlasLease = AcquireAtlas(key);
                                                if (pendingAtlasLease.IsReady) umaGenerator.atlasEarlyHits++;
                                            }
                                        }
                                        catch (NotSupportedException exception)
                                        {
                                            attemptedLookup = true;
                                            umaData.resourceReuseStatus = "Atlas cache bypass: " + exception.Message;
                                            UMAGeneratedResourceCache.Shared.RecordBypass<UMACachedAtlas>(exception.Message);
                                        }
                                        finally { umaGenerator.atlasLookupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - lookupStart; }
                                    }

                                    if (pendingAtlasLease == null || !pendingAtlasLease.IsReady)
                                    {
                                        long preparationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                        textureMerge.EnsureCapacity(moduleCount);
                                        textureMerge.Reset();
                                        for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                                            textureMerge.SetupSlotAndOverlayStack(generatedMaterial, i, textureChannelNumber, umaData);
                                        backgroundColor = slotData.material.MaskWithCurrentColor &&
                                            (channelType == UMAMaterial.ChannelType.DiffuseTexture || channelType == UMAMaterial.ChannelType.Texture)
                                            ? slotData.material.maskMultiplier * textureMerge.camBackgroundColor : UMAMaterial.GetBackgroundColor(channelType);
                                        umaGenerator.atlasPreparationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - preparationStart;
                                        if (reuseTextures && !attemptedLookup)
                                        {
                                            lookupStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                            try
                                            {
                                                var key = UMAResourceReuse.DescribeAtlas(umaData, textureMerge, slotData.material,
                                                    textureChannelNumber, umaGenerator, width, height, ww, hh, channelTextureFormat, CopyRTtoTex, backgroundColor);
                                                pendingAtlasLease = AcquireAtlas(key);
                                            }
                                            catch (NotSupportedException exception)
                                            {
                                                umaData.resourceReuseStatus = "Atlas cache bypass: " + exception.Message;
                                                UMAGeneratedResourceCache.Shared.RecordBypass<UMACachedAtlas>(exception.Message);
                                            }
                                            finally { umaGenerator.atlasLookupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - lookupStart; }
                                        }
                                    }

                                    if (pendingAtlasLease != null && pendingAtlasLease.IsReady)
                                    {
                                        lookupStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                        Texture shared = pendingAtlasLease.Resource.Texture;
                                        resultingTextures[textureChannelNumber] = shared;
                                        // Sampling state is already part of the key and was set by
                                        // the producer. Do not rewrite a shared texture on every hit.
                                        if (!channels[textureChannelNumber].NonShaderTexture)
                                            generatedMaterial.material.SetTexture(channels[textureChannelNumber].materialPropertyName, shared);
                                        generatedMaterial.cachedAtlasBindings[textureChannelNumber] = new UMAAtlasBinding(pendingAtlasLease, generatedMaterial,
                                            textureChannelNumber, channels[textureChannelNumber].NonShaderTexture ? null : channels[textureChannelNumber].materialPropertyName);
                                        pendingAtlasLease = null;
                                        ReleaseReplacedGeneratedTexture(previousResults, textureChannelNumber, shared);
                                        umaData.resourceReuseStatus = "Reusing generated atlas";
                                        umaGenerator.atlasLookupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - lookupStart;
                                        continue;
                                    }

                                    long generationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                    try
                                    {
                                    if (CopyRTtoTex)
                                    {
                                        // Temporary RT for drawing; will be released after copy
                                        destinationTexture = RenderTexture.GetTemporary(ww, hh, 0, channelTextureFormat, RenderTextureReadWrite.Linear);
                                        pendingTemporaryTexture = destinationTexture;
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
                                        // Persistent RT (reused when possible)
                                        var prevTex = previousResults != null && textureChannelNumber < previousResults.Length
                                            ? previousResults[textureChannelNumber] as RenderTexture
                                            : null;

                                        if (prevTex != null && !UMAGeneratedResourceCache.IsManagedResource(prevTex) &&
                                            prevTex.width == ww && prevTex.height == hh &&
                                            prevTex.format == channelTextureFormat &&
                                            prevTex.useMipMap == materialUseMipMap)
                                        {
                                            destinationTexture = prevTex;
                                        }
                                        else
                                        {
                                            destinationTexture = new RenderTexture(ww, hh, 0, channelTextureFormat, RenderTextureReadWrite.Linear)
                                            {
                                                useMipMap = materialUseMipMap // && !umaGenerator.convertRenderTexture;
                                            };
                                            pendingPersistentTexture = destinationTexture;
                                        }
                                    }

                                    UMARenderTextureTracker.Track(
                                        destinationTexture,
                                        umaData,
                                        atlasIndex,
                                        textureChannelNumber,
                                        slotData.material != null ? slotData.material.name : null,
                                        channels[textureChannelNumber].materialPropertyName,
                                        CopyRTtoTex);
                                    destinationTexture.filterMode = FilterMode.Point;

                                    //This draws all the rects
                                    RenderTexture.active = destinationTexture;
                                    textureMerge.DrawAllRects(destinationTexture, width, height, backgroundColor, umaGenerator.SharperFitTextures);

                                    //PostProcess
                                    textureMerge.PostProcess(destinationTexture, channels[textureChannelNumber].channelType);

                                    /*

                                    */

                                    if (CopyRTtoTex)
                                    {
                                        #region Convert Render Textures
                                        if (umaGenerator.useAsyncConversion)
                                        {
                                            // Let it have the RenderTexture now.
                                            SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, destinationTexture);
                                            resultingTextures[textureChannelNumber] = destinationTexture;
                                            // Now asynchronously copy and reset it
                                            RenderTexToCPU rt2cpu;
                                            if (pendingAtlasLease != null)
                                            {
                                                var atlas = ScriptableObject.CreateInstance<UMACachedAtlas>();
                                                atlas.Initialize(destinationTexture, true);
                                                atlas.ConversionPending = true;
                                                pendingAtlasLease.Publish(atlas, destroy: UMACachedAtlas.DestroyAtlas);
                                                pendingTemporaryTexture = null; // ownership transferred, including failure paths
                                                var readbackHold = pendingAtlasLease.Retain();
                                                generatedMaterial.cachedAtlasBindings[textureChannelNumber] = new UMAAtlasBinding(pendingAtlasLease,
                                                    generatedMaterial, textureChannelNumber, channels[textureChannelNumber].NonShaderTexture ? null : channels[textureChannelNumber].materialPropertyName);
                                                pendingAtlasLease = null;
                                                try
                                                {
                                                    rt2cpu = new RenderTexToCPU(destinationTexture, generatedMaterial, channels[textureChannelNumber].materialPropertyName,
                                                        textureChannelNumber, umaGenerator, atlas.CompleteConversion,
                                                        () => { atlas.ConversionPending = false; readbackHold.Dispose(); });
                                                }
                                                catch { readbackHold.Dispose(); throw; }
                                            }
                                            else rt2cpu = new RenderTexToCPU(destinationTexture, generatedMaterial, channels[textureChannelNumber].materialPropertyName, textureChannelNumber, umaGenerator);
                                            pendingTemporaryTexture = null;
                                            rt2cpu.DoAsyncCopy();
                                        }
                                        else
                                        {
                                            // Reuse existing Texture2D if possible
                                            Texture2D tempTexture = null;
                                            TextureFormat texFmt;
                                            if (!TryGetTextureFormat(destinationTexture.format, out texFmt))
                                            {
                                                texFmt = TextureFormat.ARGB32;
                                            }

                                            var prevTex2D = previousResults != null && textureChannelNumber < previousResults.Length
                                                ? previousResults[textureChannelNumber] as Texture2D
                                                : null;

                                            bool requiresMipChain = umaGenerator.convertMipMaps && (ww > 1 || hh > 1);

                                            if (prevTex2D != null && !UMAGeneratedResourceCache.IsManagedResource(prevTex2D) &&
                                                prevTex2D.width == ww && prevTex2D.height == hh &&
                                                prevTex2D.format == texFmt &&
                                                (prevTex2D.mipmapCount > 1) == requiresMipChain)
                                            {
                                                tempTexture = prevTex2D;
                                            }
                                            else
                                            {
                                                tempTexture = new Texture2D(destinationTexture.width, destinationTexture.height, texFmt, umaGenerator.convertMipMaps, true);
                                                pendingTexture2D = tempTexture;
                                            }

                                            bool usedGpuCopy = false;
                                            if (SupportsRTToTexture2D)
                                            {
                                                try
                                                {
                                                    // Atlas drawing and post processing update mip 0. Generate the
                                                    // completed RT mip chain before copying it; otherwise lower mips
                                                    // can contain uninitialized (usually black) data.
                                                    if (umaGenerator.convertMipMaps && destinationTexture.useMipMap && destinationTexture.mipmapCount > 1)
                                                    {
                                                        if (RenderTexture.active == destinationTexture)
                                                        {
                                                            RenderTexture.active = null;
                                                        }
                                                        destinationTexture.GenerateMips();
                                                    }

                                                    Graphics.CopyTexture(destinationTexture, tempTexture);
                                                    usedGpuCopy = true;
                                                }
                                                catch { usedGpuCopy = false; }
                                            }

                                            if (!usedGpuCopy)
                                            {
                                                // Fallback: CPU readback, reuse Texture2D memory
                                                var asyncAction = AsyncGPUReadback.Request(destinationTexture, 0);
                                                asyncAction.WaitForCompletion();
                                                tempTexture.SetPixelData(asyncAction.GetData<byte>(), 0);
                                                tempTexture.Apply(umaGenerator.convertMipMaps);
                                            }

                                            UMARenderTextureTracker.ReleaseTemporary(destinationTexture);
                                            pendingTemporaryTexture = null;

                                            resultingTextures[textureChannelNumber] = tempTexture as Texture;
                                            SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, tempTexture);
                                            pendingTexture2D = null;
                                        }
                                        #endregion
                                    }
                                    else
                                    {
                                        SetMaterialTexture(generatedMaterial, slotData, textureChannelNumber, destinationTexture);
                                        resultingTextures[textureChannelNumber] = destinationTexture;
                                        pendingPersistentTexture = null;
                                    }

                                    if (pendingAtlasLease != null)
                                    {
                                        var atlas = ScriptableObject.CreateInstance<UMACachedAtlas>();
                                        atlas.Initialize(resultingTextures[textureChannelNumber], false);
                                        pendingAtlasLease.Publish(atlas, destroy: UMACachedAtlas.DestroyAtlas);
                                        generatedMaterial.cachedAtlasBindings[textureChannelNumber] = new UMAAtlasBinding(pendingAtlasLease, generatedMaterial,
                                            textureChannelNumber, channels[textureChannelNumber].NonShaderTexture ? null : channels[textureChannelNumber].materialPropertyName);
                                        pendingAtlasLease = null;
                                    }
                                    }
                                    finally { umaGenerator.atlasGenerationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - generationStart; }
                                    break;
                                }
                            case UMAMaterial.ChannelType.MaterialColor:
                                {
                                    if (channels[textureChannelNumber].NonShaderTexture)
                                    {
                                        break;
                                    }

                                    var propIndex = generatedMaterial.material.shader.FindPropertyIndex(channels[textureChannelNumber].materialPropertyName);
                                    if (propIndex >= 0)
                                    {
                                        // get the type of the property (color, vector, float, etc.
                                        var propType = generatedMaterial.material.shader.GetPropertyType(propIndex);

                                        if (propType == UnityEngine.Rendering.ShaderPropertyType.Color)
                                        {
                                            generatedMaterial.material.SetColor(channels[textureChannelNumber].materialPropertyName, generatedMaterial.materialFragments[0].baseColor);
                                        }
#if UNITY_EDITOR

                                        else
                                        {
                                            if (Debug.isDebugBuild)
                                            {
                                                Debug.LogWarning($"Material property {channels[textureChannelNumber].materialPropertyName} is not a color property in UMAMaterial {slotData.material.name}");
                                            }
                                        }
#endif
                                    }
#if UNITY_EDITOR

                                    else
                                    {
                                        if (Debug.isDebugBuild)
                                        {
                                            Debug.LogWarning("Material property " + channels[textureChannelNumber].materialPropertyName + " not found in shader " + generatedMaterial.material.shader.name);
                                        }
                                    }
#endif
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
                                    }
                                    else if (textureChannelNumber == 0)
                                    {
                                        generatedMaterial.material.color = fragment.baseColor;
                                    }

                                    break;
                                }
                        }

                        ReleaseReplacedGeneratedTexture(previousResults, textureChannelNumber, resultingTextures[textureChannelNumber]);
                    }
                    if (previousResults != null)
                    {
                        for (int textureChannelNumber = channels.Length; textureChannelNumber < previousResults.Length; textureChannelNumber++)
                        {
                            ReleasePreviousGeneratedTexture(previousResults, textureChannelNumber);
                        }
                    }
                    generatedMaterial.resultingAtlasList = resultingTextures;
                    if (previousBindings != null) foreach (var binding in previousBindings) binding?.Dispose();
                    previousBindingsToRelease = null;
                }
            }
            finally
            {
                pendingAtlasLease?.Dispose();
                if (previousBindingsToRelease != null) foreach (var binding in previousBindingsToRelease) binding?.Dispose();
                if (pendingTemporaryTexture != null)
                {
                    UMARenderTextureTracker.ReleaseTemporary(pendingTemporaryTexture);
                }
                if (pendingPersistentTexture != null)
                {
                    UMARenderTextureTracker.Untrack(pendingPersistentTexture);
                    if (pendingPersistentTexture.IsCreated())
                    {
                        pendingPersistentTexture.Release();
                    }
                    UMAUtils.DestroySceneObject(pendingPersistentTexture);
                }
                if (pendingTexture2D != null)
                {
                    UMAUtils.DestroySceneObject(pendingTexture2D);
                }
                RenderTexture.active = null;
            }
        }

        private static void ReleaseReplacedGeneratedTexture(Texture[] previousResults, int textureChannelNumber, Texture replacement)
        {
            if (previousResults == null || textureChannelNumber < 0 || textureChannelNumber >= previousResults.Length ||
                ReferenceEquals(previousResults[textureChannelNumber], replacement))
            {
                return;
            }

            ReleasePreviousGeneratedTexture(previousResults, textureChannelNumber);
        }

        public static void SetCompositingProperties(UMAData.GeneratedMaterial generatedMaterial, Material material, UMAData.MaterialFragment fragment)
        {
            if (fragment == null || fragment.baseOverlay == null || fragment.baseOverlay.textureList == null || fragment.AdditionalOverlays == null)
            {
                return;
            }
            int numChannels = fragment.baseOverlay.textureList.Length;
            int numOverlays = 1 + fragment.AdditionalOverlays.Length;

            var overlays = fragment.slotData.GetOverlayList();

            int i = 0;
            float referenceWidth = fragment.baseOverlay.textureList[0].width;
            float referenceHeight = fragment.baseOverlay.textureList[0].height;

            for (int ovl = 0; ovl < overlays.Count; ovl++)
            {
                OverlayData overlay = overlays[ovl];

                string tileProperty = "_UseTiling" + ovl;
                if (ovl > 0 && material.HasProperty(tileProperty))
                {
                    float tiling = (overlay != null && overlay.IsTextureTiled(0)) ? 1f : 0f;
                    material.SetFloat(tileProperty, tiling);
                }

                string offsetProperty = "_UV_Offset" + ovl;
                if (material.HasProperty(offsetProperty))
                {
                    Vector4 uv = overlay.GetUV(referenceWidth, referenceHeight);
                    material.SetVector(offsetProperty, uv);
                }

                for (int c = 0; c < numChannels; c++)
                {
                    // Calculate per-channel tint/add and set directly
                    Color tint = (overlay != null) ? overlay.GetColor(c) : Color.white;
                    Color add = (overlay != null) ? overlay.GetAdditive(c) : new Color(0, 0, 0, 0);

                    if (c < tintProperties.GetLength(1) && ovl < tintProperties.GetLength(0))
                    {
                        string tintProp = tintProperties[ovl, c];
                        if (material.HasProperty(tintProp))
                        {
                            material.SetColor(tintProp, tint);
                        }
                        string addProp = addProperties[ovl, c];
                        if (material.HasProperty(addProp))
                        {
                            material.SetColor(addProp, add);
                        }
                    }
                    i++;
                }
            }
            material.SetInt("_OverlayCount", numOverlays);
        }

        private static void SetChannelTexture(UMAData umaData, int textureChannelNumber, int overlayNumber, Material mat, OverlayData overlay0)
        {
            UMAMaterial umaMaterial = null;
            if (overlay0 != null && overlay0.asset != null)
            {
                umaMaterial = overlay0.asset.material;
            }

            SetChannelTexture(umaData, textureChannelNumber, overlayNumber, mat, overlay0, umaMaterial, true, false);
        }

        private static void SetChannelTexture(UMAData umaData, int textureChannelNumber, int overlayNumber, Material mat, OverlayData overlay0, UMAMaterial umaMaterial, bool appendOverlayNumber, bool skipNullTexture)
        {
#if DEBUG
            Debug.Log("Setting channel texture" + textureChannelNumber + " overlay " + overlayNumber + " on " + mat.name);
#endif
            if (overlay0 == null || mat == null)
            {
                return;
            }

            var theTex = overlay0.GetTexture(textureChannelNumber);
            var overlayOverrides = umaData != null ? umaData.GetTextureOverrides(overlay0.overlayName) : null;

            if (umaMaterial == null)
            {
                return;
            }

            if (overlayOverrides != null)
            {
                if (overlayOverrides.ContainsKey(textureChannelNumber))
                {
                    theTex = overlayOverrides[textureChannelNumber];
                }
            }

            if (skipNullTexture && theTex == null)
            {
                return;
            }

            string materialPropertyName = "";

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
#if !UNITY_EDITOR
            catch
            {
                // this is only here to stop compiler warnings
            }
#else
            catch (Exception ex)
            {
                Debug.LogWarning("Exception processing Texture channel " + textureChannelNumber + " in material " + umaMaterial.name + " " + ex.Message);
                return;
            }
#endif

            if (appendOverlayNumber && overlayNumber > 0)
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
                generatedMaterial.material.SetTexture(slotData.material.channels[textureType].materialPropertyName, tempTexture);
            }
        }

        private static UMAGeneratedResourceCache.Lease<UMACachedAtlas> AcquireAtlas(UMAGeneratedResourceKey key)
        {
            var cache = UMAGeneratedResourceCache.Shared;
            var lease = cache.Acquire<UMACachedAtlas>(key);
            if (lease.IsReady && (lease.Resource.Texture == null ||
                lease.Resource.Texture is RenderTexture rt && !rt.IsCreated()))
            {
                cache.Invalidate(key);
                lease.Dispose();
                lease = cache.Acquire<UMACachedAtlas>(key);
            }
            return lease;
        }

        private bool IsOpenGL()
        {
            var graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion;
            return graphicsDeviceVersion.StartsWith("OpenGL");
        }
    }
}
