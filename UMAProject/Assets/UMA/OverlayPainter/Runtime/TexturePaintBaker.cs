using UnityEngine;

namespace UMA.TexturePaint
{
    public static class TexturePaintBaker
    {
        public static Texture2D Bake(TextureSet set, TexturePaintChannel channel)
        {
            set?.ownerStore?.RefreshLayerLinks();
            RenderTexture source = set?.GetVisibleTexture(channel);
            return Read(source, set != null ? set.Name + "_" + channel : channel.ToString(), 0,
                TexturePaintExportBitDepth.Eight, IsLinearChannel(channel), true);
        }

        public static Texture2D Bake(TexturePhysicalChannelGroup group)
        {
            return Read(group?.packed, group?.materialProperty?.TrimStart('_') ?? "Packed", 0,
                TexturePaintExportBitDepth.Eight, true, true);
        }

        public static Texture2D Bake(TextureSet set, TexturePaintChannel channel, int resolution,
            TexturePaintExportBitDepth bitDepth)
        {
            set?.ownerStore?.RefreshLayerLinks();
            return Read(set?.GetVisibleTexture(channel), set != null ? set.Name + "_" + channel : channel.ToString(),
                resolution, bitDepth, IsLinearChannel(channel));
        }

        public static Texture2D Bake(TexturePhysicalChannelGroup group, int resolution,
            TexturePaintExportBitDepth bitDepth)
        {
            return Read(group?.packed, group?.materialProperty?.TrimStart('_') ?? "Packed", resolution, bitDepth, true);
        }

#if UNITY_EDITOR
        internal static Texture2D BakeRenderTexture(RenderTexture source, string name, int resolution,
            TexturePaintExportBitDepth bitDepth, bool linear)
        {
            return Read(source, name, resolution, bitDepth, linear);
        }

        public static Texture2D Bake(TextureSet set, TexturePaintMaterialChannelCapability channel,
            int resolution, TexturePaintExportBitDepth bitDepth)
        {
            if (set == null || channel == null || !channel.isTexture) return null;
            set.ownerStore?.RefreshLayerLinks();
            bool linear = channel.output.colorSpace != UMAMaterial.TextureChannelColorSpace.SRGB;
            if (!string.IsNullOrEmpty(channel.materialProperty) &&
                set.physicalChannelGroups.TryGetValue(channel.materialProperty,
                    out TexturePhysicalChannelGroup physical))
                return Read(physical.packed, DisplayChannelName(channel), resolution, bitDepth, linear);

            foreach (TextureChannelTarget target in set.channels.Values)
                if (target != null && target.umaChannelIndex == channel.index && target.PreviewTexture != null)
                    return Read(set.GetVisibleTexture(target.channel), DisplayChannelName(channel),
                        resolution, bitDepth, linear);

            return Read(channel.sourceTexture, DisplayChannelName(channel), resolution, bitDepth, linear);
        }

        private static string DisplayChannelName(TexturePaintMaterialChannelCapability channel)
        {
            string value = !string.IsNullOrEmpty(channel.sourceTextureName)
                ? channel.sourceTextureName
                : channel.materialProperty;
            return string.IsNullOrEmpty(value) ? "Channel" + channel.index : value.TrimStart('_');
        }
#endif

        private static Texture2D Read(RenderTexture source, string name, int resolution,
            TexturePaintExportBitDepth bitDepth, bool linear, bool mipChain = false)
        {
            if (source == null) return null;
            // EXR stores linear floating-point color. Its GPU formats have no sRGB
            // variant, even when an importer requests sRGB sampling.
            linear |= bitDepth == TexturePaintExportBitDepth.HalfFloat;
            int width = resolution > 0 ? resolution : source.width;
            int height = resolution > 0 ? resolution : source.height;
            RenderTexture scaled = source;
            // Read linear values before encoding. Floating-point and 16-bit render formats
            // have no hardware sRGB variant, so an sRGB RT request cannot reliably encode RGB.
            if (width != source.width || height != source.height || source.sRGB)
            {
                scaled = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, scaled);
            }
            TextureFormat format = bitDepth switch
            {
                TexturePaintExportBitDepth.Sixteen => TextureFormat.RGBA64,
                TexturePaintExportBitDepth.HalfFloat => TextureFormat.RGBAHalf,
                _ => TextureFormat.RGBA32
            };
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = scaled;
                Texture2D result = new Texture2D(width, height, format, mipChain, linear)
                { name = name, wrapMode = TextureWrapMode.Clamp };
                if (linear) result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                else
                {
                    readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                    readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    readback.Apply(false, false);
                    Color[] pixels = readback.GetPixels();
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                    result.SetPixels(pixels);
                }
                result.Apply(mipChain, false);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null) Object.DestroyImmediate(readback);
                if (scaled != source) RenderTexture.ReleaseTemporary(scaled);
            }
        }

        private static Texture2D Read(Texture source, string name, int resolution,
            TexturePaintExportBitDepth bitDepth, bool linear)
        {
            if (source == null) return null;
            int width = resolution > 0 ? resolution : source.width;
            int height = resolution > 0 ? resolution : source.height;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, temporary);
                return Read(temporary, name, 0, bitDepth, linear);
            }
            finally { RenderTexture.ReleaseTemporary(temporary); }
        }

        private static bool IsLinearChannel(TexturePaintChannel channel)
            => !TexturePaintChannelUtility.IsColor(channel);
    }
}
