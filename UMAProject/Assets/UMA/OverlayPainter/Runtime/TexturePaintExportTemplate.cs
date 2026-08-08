using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Export Template", fileName = "Overlay Painter Export Template")]
    public sealed class TexturePaintExportTemplate : ScriptableObject
    {
        public const int CurrentVersion = 5;
        public int version = CurrentVersion;
        public string outputFolder = "Assets/UMA/OverlayPainter/Generated";
        public string filenamePattern = "{avatar}_{material}_{channel}_{resolution}";
        public TexturePaintExportScope scope = TexturePaintExportScope.AllMaterials;
        [Tooltip("Flattened Composite includes the reconstructed character textures. Authored Overlay exports only visible painter layers and creates a runtime alpha mask.")]
        public TexturePaintExportContent content = TexturePaintExportContent.FlattenedComposite;
        public TexturePaintChannelMask channels = TexturePaintChannelMask.All;
        public TexturePaintOverwritePolicy overwritePolicy = TexturePaintOverwritePolicy.Versioned;
        [Tooltip("0 preserves the painted texture's native resolution.")]
        public int resolution;
        public TexturePaintExportBitDepth bitDepth = TexturePaintExportBitDepth.Eight;
        [Range(0, 64)] public int padding = 8;
        public TexturePaintChannelMask invertedChannels;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
        [Tooltip("Legacy diagnostic output. Release exports always emit physical UMAMaterial channels.")]
        public bool exportLogicalChannels;
        public bool exportMaterialPacking = true;
        public List<TexturePaintPackingRule> customPacking = new List<TexturePaintPackingRule>();
        public bool createOrUpdateOverlay = true;
        [Tooltip("Replace the persistent source overlay and its textures instead of creating new assets. Requires explicit confirmation at export time.")]
        public bool overwriteSourceOverlay;
        [Tooltip("Legacy option retained for template compatibility. Release export does not create material overrides.")]
        public bool createMaterialOverride;
        [Tooltip("Legacy option retained for template compatibility. Release export never changes recipes or avatars.")]
        public bool updateRecipeReferences;
        public bool markAddressable;

        public bool Includes(TexturePaintChannel channel) => (channels & ToMask(channel)) != 0;
        public bool Inverts(TexturePaintChannel channel) => (invertedChannels & ToMask(channel)) != 0;

        public void Migrate()
        {
            if (version < 4)
            {
                const TexturePaintChannelMask legacyAll = TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.Normal | TexturePaintChannelMask.Metallic |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.AmbientOcclusion |
                    TexturePaintChannelMask.Emission | TexturePaintChannelMask.Custom;
                if ((channels & legacyAll) == legacyAll)
                    channels |= TexturePaintChannelMask.SkinColorMask |
                                TexturePaintChannelMask.Thickness |
                                TexturePaintChannelMask.DetailMask;
            }
            if (version < 5 && (channels & TexturePaintChannelMask.Normal) != 0)
                channels |= TexturePaintChannelMask.NormalControl;
            version = CurrentVersion;
        }

        public static TexturePaintChannelMask ToMask(TexturePaintChannel channel) =>
            (TexturePaintChannelMask)(1 << (int)channel);
    }
}