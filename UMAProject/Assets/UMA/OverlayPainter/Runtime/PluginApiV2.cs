using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint
{
    public static class TexturePaintPluginApi
    {
        public const int CurrentVersion = 2;
        public const int MinimumVersion = 2;
    }

    [Flags]
    public enum TexturePaintPluginCapability
    {
        None = 0,
        Brush = 1 << 0,
        Filter = 1 << 1,
        Generator = 1 << 2,
        Baker = 1 << 3,
        Importer = 1 << 4,
        Exporter = 1 << 5,
        ReadsMeshMaps = 1 << 6,
        LongRunning = 1 << 7,
        ProceduralMask = 1 << 8
    }

    public enum TexturePaintPluginParameterType { Float, Integer, Boolean, Color, String, Texture, Enum }
    public enum TexturePaintPluginColorSpace { Linear, SRGB, Data }
    public enum TexturePaintPluginBlend { Replace, Normal, Add, Multiply }

    [Serializable]
    public sealed class TexturePaintPluginParameterDefinition
    {
        public string id;
        public string displayName;
        public string description;
        public TexturePaintPluginParameterType type;
        public float minimum;
        public float maximum = 1f;
        public float defaultNumber;
        public bool defaultBoolean;
        public Color defaultColor = Color.white;
        public string defaultText;
        public string[] enumOptions = Array.Empty<string>();
    }

    [Serializable]
    public sealed class TexturePaintPluginParameterValue
    {
        public string id;
        public float number;
        public bool boolean;
        public Color color = Color.white;
        public string text;
        public Texture2D texture;
    }

    [Serializable]
    public sealed class TexturePaintPluginParameterSet
    {
        public List<TexturePaintPluginParameterValue> values = new List<TexturePaintPluginParameterValue>();

        public TexturePaintPluginParameterValue Get(string id, bool create = false)
        {
            for (int i = 0; i < values.Count; i++) if (values[i]?.id == id) return values[i];
            if (!create) return null;
            TexturePaintPluginParameterValue value = new TexturePaintPluginParameterValue { id = id };
            values.Add(value); return value;
        }

        public float Float(string id, float fallback = 0f) => Get(id)?.number ?? fallback;
        public int Integer(string id, int fallback = 0) => Mathf.RoundToInt(Get(id)?.number ?? fallback);
        public bool Boolean(string id, bool fallback = false) => Get(id)?.boolean ?? fallback;
        public Color Color(string id, Color fallback) => Get(id)?.color ?? fallback;
        public string String(string id, string fallback = "") => Get(id)?.text ?? fallback;
        public Texture2D Texture(string id) => Get(id)?.texture;
    }

    [Serializable]
    public sealed class TexturePaintPluginProfile
    {
        public string pluginId;
        public TexturePaintPluginParameterSet parameters = new TexturePaintPluginParameterSet();
    }

    [Serializable]
    public sealed class TexturePaintPluginDescriptor
    {
        public string id;
        public string displayName;
        public string description;
        public string pluginVersion = "1.0.0";
        public int apiVersion = TexturePaintPluginApi.CurrentVersion;
        public TexturePaintPluginCapability capabilities;
        public TexturePaintChannelMask declaredChannels = TexturePaintChannelMask.None;
        public List<TexturePaintPluginParameterDefinition> parameters = new List<TexturePaintPluginParameterDefinition>();

        public bool Declares(TexturePaintChannel channel) =>
            (declaredChannels & TexturePaintExportTemplate.ToMask(channel)) != 0;
    }

    public interface ITexturePaintExtensionV2
    {
        TexturePaintPluginDescriptor Descriptor { get; }
    }

    public struct TexturePaintBrushSampleV2
    {
        public Color color;
        public float opacityMultiplier;
        public float sizeMultiplier;
        public float rotationOffset;
        public bool skip;
    }

    public sealed class TexturePaintBrushContextV2
    {
        public string surfaceId { get; internal set; }
        public TexturePaintChannel channel { get; internal set; }
        public TexturePaintPluginParameterSet parameters { get; internal set; }
        public CancellationToken cancellationToken { get; internal set; }
    }

    public interface ITexturePaintBrushV2 : ITexturePaintExtensionV2
    {
        void OnStrokeStart(TexturePaintBrushContextV2 context);
        void EvaluateSample(TexturePaintBrushContextV2 context, StrokeSample input, ref TexturePaintBrushSampleV2 output);
        void OnStrokeEnd(TexturePaintBrushContextV2 context, bool committed);
    }

    public sealed class TexturePaintReadOnlyImage
    {
        private readonly Color[] pixels;
        public string surfaceId { get; }
        public TexturePaintChannel channel { get; }
        public int width { get; }
        public int height { get; }
        public bool sRGB { get; }

        internal TexturePaintReadOnlyImage(string surfaceId, TexturePaintChannel channel, int width, int height,
            bool sRGB, Color[] pixels)
        {
            this.surfaceId = surfaceId; this.channel = channel; this.width = width; this.height = height;
            this.sRGB = sRGB; this.pixels = pixels ?? Array.Empty<Color>();
        }

        public Color GetPixel(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return Color.clear;
            return pixels[y * width + x];
        }

        public Color GetPixelBilinear(float u, float v)
        {
            float x = Mathf.Clamp01(u) * Mathf.Max(0, width - 1), y = Mathf.Clamp01(v) * Mathf.Max(0, height - 1);
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), x1 = Mathf.Min(width - 1, x0 + 1), y1 = Mathf.Min(height - 1, y0 + 1);
            return Color.Lerp(Color.Lerp(GetPixel(x0, y0), GetPixel(x1, y0), x - x0),
                Color.Lerp(GetPixel(x0, y1), GetPixel(x1, y1), x - x0), y - y0);
        }

        public Color[] CopyPixels() => (Color[])pixels.Clone();
    }

    public sealed class TexturePaintReadContextV2
    {
        private readonly Dictionary<string, TexturePaintReadOnlyImage> images;
        public IReadOnlyList<string> surfaceIds { get; }

        internal TexturePaintReadContextV2(Dictionary<string, TexturePaintReadOnlyImage> images, List<string> surfaceIds)
        { this.images = images; this.surfaceIds = surfaceIds; }

        public TexturePaintReadOnlyImage Get(string surfaceId, TexturePaintChannel channel)
        {
            images.TryGetValue(Key(surfaceId, channel), out TexturePaintReadOnlyImage image); return image;
        }

        internal static string Key(string surfaceId, TexturePaintChannel channel) => (surfaceId ?? string.Empty) + "|" + (int)channel;
    }

    internal sealed class TexturePaintPluginTileCommand
    {
        public string surfaceId;
        public TexturePaintChannel channel;
        public RectInt rect;
        public Color[] pixels;
        public TexturePaintPluginColorSpace colorSpace;
        public TexturePaintPluginBlend blend;
        public float opacity;
    }

    public sealed class TexturePaintCommandContextV2
    {
        private readonly TexturePaintPluginDescriptor descriptor;
        private readonly List<TexturePaintPluginTileCommand> commands = new List<TexturePaintPluginTileCommand>();
        private readonly object commandLock = new object();
        private long queuedBytes;
        private bool sealedForCommit;
        public TexturePaintReadContextV2 source { get; }
        public TexturePaintPluginParameterSet parameters { get; }
        public CancellationToken cancellationToken { get; }
        public IProgress<float> progress { get; }
        public long commandMemoryBudgetBytes { get; }

        internal TexturePaintCommandContextV2(TexturePaintPluginDescriptor descriptor, TexturePaintReadContextV2 source,
            TexturePaintPluginParameterSet parameters, CancellationToken token, IProgress<float> progress,
            long commandMemoryBudgetBytes)
        {
            this.descriptor = JsonUtility.FromJson<TexturePaintPluginDescriptor>(JsonUtility.ToJson(descriptor));
            this.source = source; this.parameters = parameters ?? new TexturePaintPluginParameterSet();
            cancellationToken = token; this.progress = progress; this.commandMemoryBudgetBytes = commandMemoryBudgetBytes;
        }

        public void WriteTile(string surfaceId, TexturePaintChannel channel, RectInt rect, IReadOnlyList<Color> pixels,
            TexturePaintPluginColorSpace colorSpace, TexturePaintPluginBlend blend = TexturePaintPluginBlend.Normal,
            float opacity = 1f)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (commandLock)
            {
                if (sealedForCommit) throw new InvalidOperationException("Plugin command context is sealed.");
                if (!descriptor.Declares(channel)) throw new InvalidOperationException($"Plugin '{descriptor.id}' did not declare channel {channel}.");
                if (rect.width <= 0 || rect.height <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
                int count = checked(rect.width * rect.height);
                if (pixels == null || pixels.Count != count) throw new ArgumentException("Tile pixel count must equal rect width × height.", nameof(pixels));
                if (commands.Count >= 4096) throw new InvalidOperationException("Plugin command count exceeded 4096.");
                long bytes = count * 16L;
                if (queuedBytes + bytes > commandMemoryBudgetBytes) throw new InvalidOperationException("Plugin command memory budget exceeded.");
                if (!IsFinite(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be finite.");
                Color[] copy = new Color[count];
                for (int i = 0; i < count; i++)
                {
                    Color pixel = pixels[i];
                    if (!IsFinite(pixel.r) || !IsFinite(pixel.g) || !IsFinite(pixel.b) || !IsFinite(pixel.a))
                        throw new ArgumentException("Tile pixels must contain only finite values.", nameof(pixels));
                    copy[i] = pixel;
                }
                commands.Add(new TexturePaintPluginTileCommand
                {
                    surfaceId = surfaceId, channel = channel, rect = rect, pixels = copy,
                    colorSpace = colorSpace, blend = blend, opacity = Mathf.Clamp01(opacity)
                });
                queuedBytes += bytes;
            }
        }

        internal IReadOnlyList<TexturePaintPluginTileCommand> SealAndSnapshot()
        {
            lock (commandLock)
            {
                sealedForCommit = true;
                return commands.ToArray();
            }
        }

        internal TexturePaintPluginDescriptor Descriptor => descriptor;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public interface ITexturePaintCommandExtensionV2 : ITexturePaintExtensionV2
    {
        Task ExecuteAsync(TexturePaintCommandContextV2 context);
    }
    public interface ITexturePaintFilterV2 : ITexturePaintCommandExtensionV2 { }
    public interface ITexturePaintGeneratorV2 : ITexturePaintCommandExtensionV2 { }

    public sealed class TexturePaintPluginArtifact
    {
        public string name;
        public string extension;
        public string mimeType;
        public byte[] bytes;
    }

    public interface ITexturePaintBakerV2 : ITexturePaintExtensionV2
    {
        Task<TexturePaintPluginArtifact> BakeAsync(TexturePaintReadContextV2 context,
            TexturePaintPluginParameterSet parameters, IProgress<float> progress, CancellationToken token);
    }

    public interface ITexturePaintImporterV2 : ITexturePaintExtensionV2
    {
        Task ImportAsync(TexturePaintPluginArtifact source, TexturePaintCommandContextV2 context);
    }

    public interface ITexturePaintExporterV2 : ITexturePaintExtensionV2
    {
        Task<TexturePaintPluginArtifact> ExportAsync(TexturePaintReadContextV2 context,
            TexturePaintPluginParameterSet parameters, IProgress<float> progress, CancellationToken token);
    }

    public readonly struct TexturePaintProceduralMaskSampleV2
    {
        public readonly string surfaceId;
        public readonly int surfaceIndex;
        public readonly int triangleIndex;
        public readonly int uvIsland;
        public readonly Vector2 uv;
        public readonly Vector3 worldPosition;

        public TexturePaintProceduralMaskSampleV2(string surfaceId, int surfaceIndex, int triangleIndex,
            int uvIsland, Vector2 uv, Vector3 worldPosition)
        {
            this.surfaceId = surfaceId; this.surfaceIndex = surfaceIndex; this.triangleIndex = triangleIndex;
            this.uvIsland = uvIsland; this.uv = uv; this.worldPosition = worldPosition;
        }
    }

    public enum TexturePaintPluginDiagnosticSeverity { Info, Warning, Error }

    public sealed class TexturePaintPluginDiagnostic
    {
        public DateTime timestampUtc;
        public string pluginId;
        public TexturePaintPluginDiagnosticSeverity severity;
        public string message;
        public string exception;
        public double durationMilliseconds;
        public int commandCount;
        public long dirtyPixels;
    }
}
