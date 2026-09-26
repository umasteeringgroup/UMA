using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UMA
{
    public static partial class UMAResourceReuse
    {
        // Scratch contains values/IDs only, never asset or avatar references. Resource
        // cache entries own a frozen copy on misses; warm signature lookups allocate nothing.
        internal sealed class AtlasSignature
        {
            private byte[] buffer = new byte[4096];
            private int length;
            private long token;
            private static long nextToken;
            private readonly UMAGeneratedResourceKey probe = new UMAGeneratedResourceKey();
            internal void Reset() { length = 0; token = ++nextToken; }
            internal unsafe void Add<T>(T value) where T : unmanaged
            {
                int size = sizeof(T);
                if (length + size > buffer.Length) Array.Resize(ref buffer, Math.Max(length + size, buffer.Length * 2));
                MemoryMarshal.Write(buffer.AsSpan(length, size), ref value);
                length += size;
            }
            internal void Text(string value)
            {
                if (value == null) { Add(-1); return; }
                int count = System.Text.Encoding.UTF8.GetByteCount(value);
                Add(count);
                if (length + count > buffer.Length) Array.Resize(ref buffer, Math.Max(length + count, buffer.Length * 2));
                System.Text.Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, length);
                length += count;
            }
            internal UMAGeneratedResourceKey Key(string kind = "UMA.Atlas.inputs.v2")
            {
                probe.SetProbe(kind, buffer, length);
                return probe;
            }
            internal void Asset(UnityEngine.Object asset, bool input = false)
            {
                Add(asset != null);
                if (asset == null) return;
                var dependency = dependencies.GetOrCreateValue(asset);
                // Main/mask textures and shaders repeat within a request. No callbacks
                // execute while describing it, so reuse their exact bytes within this
                // signature only. The next request always rechecks live input state.
                if (dependency.AtlasSignatureToken == token && dependency.AtlasSignatureInput == input)
                {
                    int count = dependency.AtlasSignatureLength;
                    if (length + count > buffer.Length) Array.Resize(ref buffer, Math.Max(length + count, buffer.Length * 2));
                    buffer.AsSpan(dependency.AtlasSignatureStart, count).CopyTo(buffer.AsSpan(length, count));
                    length += count;
                    return;
                }
                dependency = GetDependency(asset, dependency);
                int start = length;
                Add(dependency.Number); Add(dependency.Revision);
#if UNITY_EDITOR
                Add(dependency.EditorHash);
#endif
                if (asset is Texture texture)
                {
                    var contents = TextureContentsHash(texture);
                    if (input && NeedsTextureRevision(texture, dependency, contents))
                        throw new NotSupportedException("Runtime input texture needs a dependency revision: " + texture.name);
                    Add(texture.updateCount); Add(contents);
                    Add(texture.width); Add(texture.height); Add((int)texture.graphicsFormat);
                    Add((int)texture.wrapModeU); Add((int)texture.wrapModeV); Add((int)texture.wrapModeW);
                    Add((int)texture.filterMode); Add(texture.anisoLevel);
                }
                dependency.AtlasSignatureToken = token;
                dependency.AtlasSignatureInput = input;
                dependency.AtlasSignatureStart = start;
                dependency.AtlasSignatureLength = length - start;
            }
        }

        private static readonly AtlasSignature atlasSignature = new AtlasSignature();
        private static readonly int mainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int extraTextureId = Shader.PropertyToID("_ExtraTex");

        internal static bool IsStandardAtlasShader(Shader shader)
        {
            return shader != null && GetShaderLayout(shader).StandardAtlas;
        }

        // An early hit is safe for ordinary UMA alpha-compositing. Custom shaders,
        // advanced blending and cutouts retain the resolved-command description path.
        internal static bool CanUseEarlyAtlasLookup(TextureMerge merge)
        {
            if (merge == null || merge.GetType() != typeof(TextureMerge) || merge.material == null ||
                !IsStandardAtlasShader(merge.material.shader)) return false;
            if (merge.material.shaderKeywords.Length != 0) return false;
            if (merge.material.GetTextureScale(mainTextureId) != Vector2.one ||
                merge.material.GetTextureOffset(mainTextureId) != Vector2.zero ||
                merge.material.GetTextureScale(extraTextureId) != Vector2.one ||
                merge.material.GetTextureOffset(extraTextureId) != Vector2.zero) return false;
            for (int i = 0; i < merge.material.passCount; i++)
                if (!merge.material.GetShaderPassEnabled(merge.material.GetPassName(i))) return false;
            return true;
        }

        internal static bool TryDescribeAtlasInputs(UMAData data, TextureMerge merge,
            UMAData.GeneratedMaterial generated, UMAMaterial material, int channel,
            UMAGeneratorBase generator, int width, int height, int outputWidth, int outputHeight,
            RenderTextureFormat format, bool convert, out UMAGeneratedResourceKey key)
        {
            key = null;
            if (data.AtlasUpdated != null && data.AtlasUpdated.HasListeners)
                throw new NotSupportedException("AtlasUpdated callbacks require private atlases.");
            var d = atlasSignature;
            d.Reset();
            d.Add(textureInputEpoch);
            d.Add(width); d.Add(height); d.Add(outputWidth); d.Add(outputHeight); d.Add((int)format);
            d.Add(convert); d.Add(generator.qualityTextures.Mips(convert ? generator.convertMipMaps : material.generateMipMaps));
            d.Add((int)generator.qualityTextures.EffectiveCompression(convert, format, outputWidth, outputHeight));
            d.Add(generator.useAsyncConversion); d.Add(generator.SharperFitTextures);
            d.Add((int)QualitySettings.activeColorSpace); d.Add((int)SystemInfo.graphicsDeviceType);
            d.Add(generator.qualityTextures.Anisotropy(material)); d.Add(generator.qualityTextures.MipBias(material)); d.Add((int)generator.qualityTextures.Filter(material));
            var channelType = material.channels[channel].channelType;
            d.Add((int)channelType); d.Add(material.MaskWithCurrentColor); d.Add(material.maskMultiplier);
            d.Add(generated.resolutionScale);
            d.Add(generated.materialFragments.Count);
            bool hasBase = false;
            foreach (var fragment in generated.materialFragments)
            {
                d.Add(fragment.isRectShared);
                if (fragment.isRectShared) continue;
                if (fragment.isNoTextures || fragment.baseOverlay == null || fragment.overlayData == null ||
                    channel >= fragment.baseOverlay.textureList.Length) return false;
                var sourceMaterial = fragment.umaMaterial != null ? fragment.umaMaterial : fragment.slotData.material;
                if (sourceMaterial?.channels == null || channel >= sourceMaterial.channels.Length) return false;
                var type = sourceMaterial.channels[channel].channelType;
                d.Add((int)type);
                hasBase = true;
                Shader shader;
                switch (type)
                {
                    case UMAMaterial.ChannelType.DiffuseTexture: shader = merge.diffuseShader; break;
                    case UMAMaterial.ChannelType.NormalMap: shader = merge.normalShader; break;
                    case UMAMaterial.ChannelType.DetailNormalMap: shader = merge.detailNormalShader; break;
                    case UMAMaterial.ChannelType.Texture: shader = merge.dataShader; break;
                    default: return false;
                }
                if (!IsStandardAtlasShader(shader)) return false;
                d.Asset(shader);
                d.Add(fragment.atlasRegion); d.Add(fragment.sourceUVRect); d.Add(fragment.slotData.overlayScale);
                int additional = fragment.AdditionalOverlays?.Length ?? 0;
                d.Add(additional);
                if (fragment.overlayData.Length < additional + 1) return false;
                for (int i = 0; i <= additional; i++)
                {
                    var overlay = i == 0 ? fragment.baseOverlay : fragment.AdditionalOverlays[i - 1];
                    bool present = overlay != null && channel < overlay.textureList.Length && (i == 0 || overlay.textureList[channel] != null);
                    d.Add(present);
                    if (!present) continue;
                    var instance = fragment.overlayData[i];
                    if (instance == null) return false;
                    if (i > 0)
                    {
                        if (overlay.overlayType != OverlayDataAsset.OverlayType.Normal ||
                            instance.GetOverlayBlend(channel) != OverlayDataAsset.OverlayBlend.Normal) return false;
                        var shaders = type == UMAMaterial.ChannelType.DiffuseTexture ? merge.DiffuseBlendModeShaders :
                            type == UMAMaterial.ChannelType.NormalMap ? merge.NormalBlendModeShaders : merge.DataBlendModeShaders;
                        var overlayShader = type == UMAMaterial.ChannelType.DetailNormalMap ? merge.detailNormalShader :
                            shaders != null && shaders.Count > 0 ? shaders[0].Combiner : null;
                        if (!IsStandardAtlasShader(overlayShader)) return false;
                        d.Asset(overlayShader);
                        if (fragment.rects == null || i - 1 >= fragment.rects.Length) return false;
                        d.Add(fragment.rects[i - 1]);
                        d.Add(instance.instanceTransformed);
                        if (instance.instanceTransformed)
                        {
                            d.Add(instance.Rotation); d.Add(instance.Scale); d.Add(instance.Translate);
                            d.Asset(fragment.overlayData[0].GetTexture(0), true);
                        }
                    }
                    d.Asset(overlay.textureList[channel], true); d.Asset(overlay.alphaTexture, true);
                    d.Add(fragment.GetMultiplier(i, channel)); d.Add(fragment.GetAdditive(i, channel));
                    d.Add(instance.TransparentMultiplier);
                    if (instance.TransparentMultiplier.a > 0 && (type == UMAMaterial.ChannelType.Texture || type == UMAMaterial.ChannelType.DiffuseTexture))
                    {
                        var prefill = merge.transparentPrefillShader;
                        if (prefill != null && prefill.name != "Hidden/UMA/TransparentPrefill") return false;
                        d.Asset(prefill);
                    }
                }
            }
            // With no base draw, the legacy compositor's background can depend on its
            // previous channel. Leave that unusual layout to the resolved-command path.
            if (!hasBase) return false;
            var processes = channelType == UMAMaterial.ChannelType.DiffuseTexture ? merge.diffusePostProcesses :
                channelType == UMAMaterial.ChannelType.NormalMap ? merge.normalPostProcesses :
                channelType == UMAMaterial.ChannelType.DetailNormalMap ? merge.detailNormalPostProcesses : merge.dataPostProcesses;
            d.Add(processes.Count);
            foreach (var process in processes)
            {
                if (process == null || process.GetType() != typeof(UMAPostProcess)) return false;
                var shader = process.shader;
                if (shader == null || !GetShaderLayout(shader).StandardPostprocess) return false;
                d.Asset(shader);
            }
            key = d.Key();
            return true;
        }
    }
}
