using System;
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
        SkinColorMask = 1 << 7,
        Thickness = 1 << 8,
        DetailMask = 1 << 9,
        NormalControl = 1 << 10,
        All = Albedo | Normal | Metallic | Roughness | AmbientOcclusion | Emission | Custom |
              SkinColorMask | Thickness | DetailMask | NormalControl
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
