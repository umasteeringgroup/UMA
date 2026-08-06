using UnityEngine;

namespace UMA.TexturePaint
{
    public static class TexturePaintBaker
    {
        public static Texture2D Bake(TextureSet set, TexturePaintChannel channel)
        {
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
            return Read(set?.GetVisibleTexture(channel), set != null ? set.Name + "_" + channel : channel.ToString(),
                resolution, bitDepth, IsLinearChannel(channel));
        }

        public static Texture2D Bake(TexturePhysicalChannelGroup group, int resolution,
            TexturePaintExportBitDepth bitDepth)
        {
            return Read(group?.packed, group?.materialProperty?.TrimStart('_') ?? "Packed", resolution, bitDepth, true);
        }

#if UNITY_EDITOR
        public static Texture2D Bake(TextureSet set, TexturePaintMaterialChannelCapability channel,
            int resolution, TexturePaintExportBitDepth bitDepth)
        {
            if (set == null || channel == null || !channel.isTexture) return null;
            bool linear = channel.output.colorSpace != UMAMaterial.TextureChannelColorSpace.SRGB;
            if (!string.IsNullOrEmpty(channel.materialProperty) &&
                set.physicalChannelGroups.TryGetValue(channel.materialProperty,
                    out TexturePhysicalChannelGroup physical))
                return Read(physical.packed, DisplayChannelName(channel), resolution, bitDepth, linear);

            foreach (TextureChannelTarget target in set.channels.Values)
                if (target != null && target.umaChannelIndex == channel.index && target.PreviewTexture != null)
                    return Read(target.PreviewTexture, DisplayChannelName(channel), resolution, bitDepth, linear);

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
            int width = resolution > 0 ? resolution : source.width;
            int height = resolution > 0 ? resolution : source.height;
            RenderTexture scaled = source;
            bool outputSRGB = !linear;
            // Working targets are deliberately linear. Route color exports through an sRGB
            // render target so the GPU applies the transfer function before ReadPixels/PNG.
            // This also keeps the method correct if a caller supplies an older sRGB target.
            if (width != source.width || height != source.height || source.sRGB != outputSRGB)
            {
                scaled = RenderTexture.GetTemporary(width, height, 0, source.format, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, scaled);
            }
            TextureFormat format = bitDepth switch
            {
                TexturePaintExportBitDepth.Sixteen => TextureFormat.RGBA64,
                TexturePaintExportBitDepth.HalfFloat => TextureFormat.RGBAHalf,
                _ => TextureFormat.RGBA32
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = scaled;
                Texture2D result = new Texture2D(width, height, format, mipChain, linear)
                { name = name, wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                result.Apply(mipChain, false);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
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
            => channel != TexturePaintChannel.Albedo && channel != TexturePaintChannel.Emission;
    }
}
