using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [Flags]
    public enum TexturePaintChannelMask
    {
        None = 0,
        Albedo = 1 << 0,
        Normal = 1 << 1,
        Metallic = 1 << 2,
        Roughness = 1 << 3,
        AmbientOcclusion = 1 << 4,
        Emission = 1 << 5,
        Custom = 1 << 6,
        All = Albedo | Normal | Metallic | Roughness | AmbientOcclusion | Emission | Custom
    }

    public enum TexturePaintExportScope { CurrentMaterial, AllMaterials }
    public enum TexturePaintExportContent
    {
        FlattenedComposite,
        [InspectorName("Runtime Overlay (Transparent)")] AuthoredOverlay
    }
    public enum TexturePaintOverwritePolicy { Fail, Overwrite, Versioned }
    public enum TexturePaintExportBitDepth { Eight, Sixteen, HalfFloat }

    [Serializable]
    public sealed class TexturePaintPackingComponent
    {
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        [Range(0, 3)] public int sourceComponent;
        public bool invert;
        [Range(0f, 1f)] public float defaultValue;
    }

    [Serializable]
    public sealed class TexturePaintPackingRule
    {
        public bool enabled = true;
        public string outputName = "Packed";
        public TexturePaintPackingComponent red = new TexturePaintPackingComponent { sourceComponent = 0 };
        public TexturePaintPackingComponent green = new TexturePaintPackingComponent { sourceComponent = 1 };
        public TexturePaintPackingComponent blue = new TexturePaintPackingComponent { sourceComponent = 2 };
        public TexturePaintPackingComponent alpha = new TexturePaintPackingComponent { sourceComponent = 3, defaultValue = 1f };
    }

    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Export Template", fileName = "Overlay Painter Export Template")]
    public sealed class TexturePaintExportTemplate : ScriptableObject
    {
        public const int CurrentVersion = 3;
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

        public static TexturePaintChannelMask ToMask(TexturePaintChannel channel) =>
            (TexturePaintChannelMask)(1 << (int)channel);
    }

    [Serializable]
    public sealed class TexturePaintExportRecord
    {
        public string surfaceId;
        public string texturePath;
        public TexturePaintChannel channel;
        public string materialProperty;
        public string overlayGuid;
        public string materialGuid;
    }

    public enum TexturePaintBindingStatus { Exact, Rebound, Reprojectable, Orphaned }

    [Serializable]
    public sealed class TexturePaintBindingReport
    {
        public string savedSurfaceId;
        public string currentSurfaceId;
        public string materialName;
        public TexturePaintBindingStatus status;
        public string message;
    }
}
