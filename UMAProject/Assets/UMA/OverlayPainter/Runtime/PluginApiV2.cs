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
        GpuAccelerated = 1 << 8
    }

    public enum TexturePaintPluginParameterType
    {
        Float,
        Integer,
        Boolean,
        Color,
        String,
        Texture,
        Enum,
        /// <summary>
        /// Presentation-only collapsible section. Appended to preserve serialized values.
        /// </summary>
        Header,
        /// <summary>Editable scalar transfer curve. Appended to preserve serialized values.</summary>
        Curve,
        /// <summary>Sprite asset captured as immutable cropped pixels before execution.</summary>
        Sprite,
        /// <summary>Ordered, editable fabric-stripe definitions.</summary>
        StripeList,
        /// <summary>Unity font asset used by text-producing plugins.</summary>
        Font,
        /// <summary>Multi-line string rendered as an expanding text area by the shared editor.</summary>
        MultilineString
    }
    public enum TexturePaintPluginColorSpace { Linear, SRGB, Data }
    public enum TexturePaintPluginBlend { Replace, Normal, Add, Multiply }
    public enum TexturePaintStripeDirection { Vertical, Horizontal }

    [Serializable]
    public sealed class TexturePaintStripeDefinition
    {
        public bool enabled = true;
        public TexturePaintStripeDirection direction;
        [Range(0f, 1f)] public float position = 0.5f;
        [Range(0.001f, 1f)] public float width = 0.1f;
        [Range(0f, 0.5f)] public float softness = 0.01f;
        [Range(0f, 1f)] public float opacity = 1f;
        public Color color = Color.white;

        public TexturePaintStripeDefinition Clone() =>
            (TexturePaintStripeDefinition)MemberwiseClone();
    }

    [Flags]
    public enum TexturePaintPluginTarget
    {
        None = 0,
        LayerContent = 1 << 0,
        LayerMask = 1 << 1,
        All = LayerContent | LayerMask
    }

    public enum TexturePaintMeshMap
    {
        WorldPosition,
        WorldNormal,
        SignedCurvature,
        AmbientOcclusion,
        Thickness,
        SurfaceId
    }

    [Flags]
    public enum TexturePaintMeshMapMask
    {
        None = 0,
        WorldPosition = 1 << 0,
        WorldNormal = 1 << 1,
        SignedCurvature = 1 << 2,
        AmbientOcclusion = 1 << 3,
        Thickness = 1 << 4,
        SurfaceId = 1 << 5,
        All = WorldPosition | WorldNormal | SignedCurvature | AmbientOcclusion | Thickness | SurfaceId
    }

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
        public AnimationCurve defaultCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public List<TexturePaintStripeDefinition> defaultStripes =
            new List<TexturePaintStripeDefinition>();
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
        public Sprite sprite;
        public Font font;
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public List<TexturePaintStripeDefinition> stripes =
            new List<TexturePaintStripeDefinition>();
    }

    [Serializable]
    public sealed class TexturePaintPluginParameterSet
    {
        public List<TexturePaintPluginParameterValue> values = new List<TexturePaintPluginParameterValue>();
        [NonSerialized] private Dictionary<string, TexturePaintPluginParameterValue> valueLookup;
        [NonSerialized] private int valueLookupCount = -1;

        /// <summary>
        /// Replaces every stored value with an independent copy of the descriptor defaults.
        /// Asset parameters intentionally reset to null because parameter definitions do not
        /// retain scene or project object references.
        /// </summary>
        public void ResetToDefaults(TexturePaintPluginDescriptor descriptor) =>
            ResetToDefaults(descriptor?.parameters);

        public void ResetToDefaults(
            IReadOnlyList<TexturePaintPluginParameterDefinition> definitions)
        {
            values ??= new List<TexturePaintPluginParameterValue>();
            values.Clear();
            valueLookup = null;
            valueLookupCount = -1;
            if (definitions == null) return;

            for (int i = 0; i < definitions.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.id) ||
                    definition.type == TexturePaintPluginParameterType.Header)
                    continue;

                values.Add(new TexturePaintPluginParameterValue
                {
                    id = definition.id,
                    number = definition.defaultNumber,
                    boolean = definition.defaultBoolean,
                    color = definition.defaultColor,
                    text = definition.defaultText,
                    texture = null,
                    sprite = null,
                    font = null,
                    curve = CloneCurve(definition.defaultCurve),
                    stripes = CloneStripes(definition.defaultStripes)
                });
            }
        }

        public TexturePaintPluginParameterValue Get(string id, bool create = false)
        {
            values ??= new List<TexturePaintPluginParameterValue>();
            EnsureValueLookup();
            string key = id ?? string.Empty;
            if (valueLookup.TryGetValue(key, out TexturePaintPluginParameterValue existing))
                return existing;
            if (!create) return null;
            TexturePaintPluginParameterValue value = new TexturePaintPluginParameterValue { id = id };
            values.Add(value);
            valueLookup[key] = value;
            valueLookupCount = values.Count;
            return value;
        }

        private void EnsureValueLookup()
        {
            if (valueLookup != null && valueLookupCount == values.Count) return;
            valueLookup ??= new Dictionary<string, TexturePaintPluginParameterValue>(
                StringComparer.Ordinal);
            valueLookup.Clear();
            for (int i = 0; i < values.Count; i++)
            {
                TexturePaintPluginParameterValue value = values[i];
                if (value == null) continue;
                string key = value.id ?? string.Empty;
                if (!valueLookup.ContainsKey(key)) valueLookup.Add(key, value);
            }
            valueLookupCount = values.Count;
        }

        public float Float(string id, float fallback = 0f) => Get(id)?.number ?? fallback;
        public int Integer(string id, int fallback = 0) => Mathf.RoundToInt(Get(id)?.number ?? fallback);
        public bool Boolean(string id, bool fallback = false) => Get(id)?.boolean ?? fallback;
        public Color Color(string id, Color fallback) => Get(id)?.color ?? fallback;
        public string String(string id, string fallback = "") => Get(id)?.text ?? fallback;
        public Texture2D Texture(string id) => Get(id)?.texture;
        public Sprite Sprite(string id) => Get(id)?.sprite;
        public Font Font(string id) => Get(id)?.font;
        public AnimationCurve Curve(string id, AnimationCurve fallback = null) =>
            Get(id)?.curve ?? fallback;
        public List<TexturePaintStripeDefinition> Stripes(string id)
        {
            TexturePaintPluginParameterValue value = Get(id, true);
            value.stripes ??= new List<TexturePaintStripeDefinition>();
            return value.stripes;
        }

        public TexturePaintPluginParameterSet Clone()
        {
            var clone = new TexturePaintPluginParameterSet();
            if (values == null) return clone;
            for (int i = 0; i < values.Count; i++)
            {
                TexturePaintPluginParameterValue source = values[i];
                if (source == null) continue;
                clone.values.Add(new TexturePaintPluginParameterValue
                {
                    id = source.id, number = source.number, boolean = source.boolean,
                    color = source.color, text = source.text, texture = source.texture,
                    sprite = source.sprite, font = source.font, curve = CloneCurve(source.curve),
                    stripes = CloneStripes(source.stripes)
                });
            }
            return clone;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null) return null;
            var curve = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return curve;
        }

        public static List<TexturePaintStripeDefinition> CloneStripes(
            IReadOnlyList<TexturePaintStripeDefinition> source)
        {
            var result = new List<TexturePaintStripeDefinition>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null) result.Add(source[i].Clone());
            return result;
        }
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
        public TexturePaintChannelMask readChannels = TexturePaintChannelMask.None;
        public TexturePaintMeshMapMask requiredMeshMaps = TexturePaintMeshMapMask.None;
        public TexturePaintPluginTarget supportedTargets = TexturePaintPluginTarget.LayerContent;
        [Min(0)] public int channelSnapshotMaximumResolution;
        public List<TexturePaintPluginParameterDefinition> parameters = new List<TexturePaintPluginParameterDefinition>();

        public bool Declares(TexturePaintChannel channel) =>
            (declaredChannels & TexturePaintExportTemplate.ToMask(channel)) != 0;

        public bool Reads(TexturePaintChannel channel) =>
            (ResolvedReadChannels & TexturePaintExportTemplate.ToMask(channel)) != 0;

        public TexturePaintChannelMask ResolvedReadChannels =>
            readChannels == TexturePaintChannelMask.None ? declaredChannels : readChannels;

        public bool Requires(TexturePaintMeshMap map) =>
            (ResolvedMeshMaps & ToMask(map)) != 0;

        public TexturePaintMeshMapMask ResolvedMeshMaps =>
            (capabilities & TexturePaintPluginCapability.ReadsMeshMaps) == 0
                ? TexturePaintMeshMapMask.None
                : requiredMeshMaps == TexturePaintMeshMapMask.None
                    ? TexturePaintMeshMapMask.All
                    : requiredMeshMaps;

        public static TexturePaintMeshMapMask ToMask(TexturePaintMeshMap map) =>
            (TexturePaintMeshMapMask)(1 << (int)map);
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

    public abstract class TexturePaintReadOnlyPixels
    {
        private readonly Color[] pixels;
        public int width { get; }
        public int height { get; }
        public bool sRGB { get; }

        internal TexturePaintReadOnlyPixels(int width, int height, bool sRGB, Color[] pixels)
        {
            this.width = width; this.height = height;
            this.sRGB = sRGB; this.pixels = pixels ?? Array.Empty<Color>();
        }

        public Color GetPixel(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return Color.clear;
            return pixels[y * width + x];
        }

        public Color GetPixelBilinear(float u, float v)
        {
            if (width <= 0 || height <= 0 || pixels.Length == 0) return Color.clear;

            // Texture coordinates address texel centers at (pixel + 0.5) / size. The previous
            // width - 1 mapping placed those coordinates between texels, so every same-resolution
            // plugin sample performed four reads and unintentionally softened edges by half a
            // pixel. This mapping matches GPU texture sampling and has a one-read fast path for
            // the overwhelmingly common pixel-center case.
            float x = Mathf.Clamp01(u) * width - 0.5f;
            float y = Mathf.Clamp01(v) * height - 0.5f;
            int rawX0 = Mathf.FloorToInt(x), rawY0 = Mathf.FloorToInt(y);
            float tx = x - rawX0, ty = y - rawY0;
            int x0 = Mathf.Clamp(rawX0, 0, width - 1);
            int y0 = Mathf.Clamp(rawY0, 0, height - 1);
            int x1 = Mathf.Clamp(rawX0 + 1, 0, width - 1);
            int y1 = Mathf.Clamp(rawY0 + 1, 0, height - 1);
            Color c00 = pixels[y0 * width + x0];
            if ((tx <= 0.000001f || x0 == x1) && (ty <= 0.000001f || y0 == y1))
                return c00;
            Color c10 = pixels[y0 * width + x1];
            Color lower = Color.LerpUnclamped(c00, c10, tx);
            if (ty <= 0.000001f || y0 == y1) return lower;
            Color c01 = pixels[y1 * width + x0];
            Color c11 = pixels[y1 * width + x1];
            return Color.LerpUnclamped(lower, Color.LerpUnclamped(c01, c11, tx), ty);
        }

        public Color[] CopyPixels() => (Color[])pixels.Clone();
    }

    public sealed class TexturePaintReadOnlyImage : TexturePaintReadOnlyPixels
    {
        public string surfaceId { get; }
        public TexturePaintChannel channel { get; }

        internal TexturePaintReadOnlyImage(string surfaceId, TexturePaintChannel channel, int width, int height,
            bool sRGB, Color[] pixels) : base(width, height, sRGB, pixels)
        {
            this.surfaceId = surfaceId;
            this.channel = channel;
        }
    }

    public sealed class TexturePaintReadOnlyMeshMap : TexturePaintReadOnlyPixels
    {
        public string surfaceId { get; }
        public TexturePaintMeshMap map { get; }

        internal TexturePaintReadOnlyMeshMap(string surfaceId, TexturePaintMeshMap map, int width, int height,
            Color[] pixels) : base(width, height, false, pixels)
        {
            this.surfaceId = surfaceId;
            this.map = map;
        }
    }

    /// <summary>Immutable grayscale snapshot of the selected destination layer's mask.</summary>
    public sealed class TexturePaintReadOnlyMask : TexturePaintReadOnlyPixels
    {
        public string surfaceId { get; }

        internal TexturePaintReadOnlyMask(string surfaceId, int width, int height, Color[] pixels)
            : base(width, height, false, pixels)
        {
            this.surfaceId = surfaceId;
        }
    }

    public sealed class TexturePaintReadOnlyParameterTexture : TexturePaintReadOnlyPixels
    {
        public string parameterId { get; }

        internal TexturePaintReadOnlyParameterTexture(string parameterId, int width, int height,
            bool sRGB, Color[] pixels) : base(width, height, sRGB, pixels)
        {
            this.parameterId = parameterId;
        }
    }

    public sealed class TexturePaintReadOnlyChannelInfo
    {
        public string surfaceId { get; }
        public TexturePaintChannel channel { get; }
        public int width { get; }
        public int height { get; }
        public bool sRGB { get; }

        internal TexturePaintReadOnlyChannelInfo(string surfaceId, TexturePaintChannel channel,
            int width, int height, bool sRGB)
        {
            this.surfaceId = surfaceId;
            this.channel = channel;
            this.width = width;
            this.height = height;
            this.sRGB = sRGB;
        }
    }

    public sealed class TexturePaintReadContextV2
    {
        private readonly Dictionary<string, TexturePaintReadOnlyImage> images;
        private readonly Dictionary<string, TexturePaintReadOnlyChannelInfo> channelInfo;
        private readonly Dictionary<string, TexturePaintReadOnlyMeshMap> meshMaps;
        private readonly Dictionary<string, TexturePaintReadOnlyParameterTexture> parameterTextures;
        private readonly Dictionary<string, TexturePaintReadOnlyMask> masks;
        public IReadOnlyList<string> surfaceIds { get; }

        internal TexturePaintReadContextV2(Dictionary<string, TexturePaintReadOnlyImage> images,
            Dictionary<string, TexturePaintReadOnlyChannelInfo> channelInfo,
            Dictionary<string, TexturePaintReadOnlyMeshMap> meshMaps,
            Dictionary<string, TexturePaintReadOnlyParameterTexture> parameterTextures,
            List<string> surfaceIds, Dictionary<string, TexturePaintReadOnlyMask> masks = null)
        {
            this.images = images ?? new Dictionary<string, TexturePaintReadOnlyImage>(StringComparer.Ordinal);
            this.channelInfo = channelInfo ??
                new Dictionary<string, TexturePaintReadOnlyChannelInfo>(StringComparer.Ordinal);
            this.meshMaps = meshMaps ?? new Dictionary<string, TexturePaintReadOnlyMeshMap>(StringComparer.Ordinal);
            this.parameterTextures = parameterTextures ??
                new Dictionary<string, TexturePaintReadOnlyParameterTexture>(StringComparer.Ordinal);
            this.masks = masks ?? new Dictionary<string, TexturePaintReadOnlyMask>(StringComparer.Ordinal);
            this.surfaceIds = surfaceIds != null
                ? (IReadOnlyList<string>)surfaceIds
                : Array.Empty<string>();
        }

        public TexturePaintReadOnlyImage Get(string surfaceId, TexturePaintChannel channel)
        {
            images.TryGetValue(Key(surfaceId, channel), out TexturePaintReadOnlyImage image); return image;
        }

        public TexturePaintReadOnlyChannelInfo GetChannelInfo(string surfaceId, TexturePaintChannel channel)
        {
            channelInfo.TryGetValue(Key(surfaceId, channel), out TexturePaintReadOnlyChannelInfo info);
            return info;
        }

        public TexturePaintReadOnlyMeshMap GetMeshMap(string surfaceId, TexturePaintMeshMap map)
        {
            meshMaps.TryGetValue(MeshKey(surfaceId, map), out TexturePaintReadOnlyMeshMap image);
            return image;
        }

        public TexturePaintReadOnlyParameterTexture GetParameterTexture(string parameterId)
        {
            parameterTextures.TryGetValue(parameterId ?? string.Empty,
                out TexturePaintReadOnlyParameterTexture image);
            return image;
        }

        public TexturePaintReadOnlyMask GetMask(string surfaceId)
        {
            masks.TryGetValue(surfaceId ?? string.Empty, out TexturePaintReadOnlyMask mask);
            return mask;
        }

        internal static string Key(string surfaceId, TexturePaintChannel channel) => (surfaceId ?? string.Empty) + "|" + (int)channel;
        internal static string MeshKey(string surfaceId, TexturePaintMeshMap map) =>
            (surfaceId ?? string.Empty) + "|mesh|" + (int)map;
    }

    internal sealed class TexturePaintPluginTileCommand
    {
        public string surfaceId;
        public TexturePaintChannel channel;
        public RectInt rect;
        public Color[] pixels;
        public Color32[] compactPixels;
        public TexturePaintPluginColorSpace colorSpace;
        public TexturePaintPluginBlend blend;
        public float opacity;
        public TexturePaintPluginTarget target = TexturePaintPluginTarget.LayerContent;

        public Color GetPixel(int index) => compactPixels != null ? compactPixels[index] : pixels[index];
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
        public TexturePaintPluginTarget target { get; }

        public TexturePaintReadOnlyMeshMap GetMeshMap(string surfaceId, TexturePaintMeshMap map) =>
            source.GetMeshMap(surfaceId, map);

        public TexturePaintReadOnlyParameterTexture GetTextureParameter(string parameterId) =>
            source.GetParameterTexture(parameterId);

        internal TexturePaintCommandContextV2(TexturePaintPluginDescriptor descriptor, TexturePaintReadContextV2 source,
            TexturePaintPluginParameterSet parameters, CancellationToken token, IProgress<float> progress,
            long commandMemoryBudgetBytes,
            TexturePaintPluginTarget target = TexturePaintPluginTarget.LayerContent)
        {
            this.descriptor = JsonUtility.FromJson<TexturePaintPluginDescriptor>(JsonUtility.ToJson(descriptor));
            this.source = source; this.parameters = parameters ?? new TexturePaintPluginParameterSet();
            cancellationToken = token; this.progress = progress; this.commandMemoryBudgetBytes = commandMemoryBudgetBytes;
            this.target = target;
        }

        public void WriteTile(string surfaceId, TexturePaintChannel channel, RectInt rect, IReadOnlyList<Color> pixels,
            TexturePaintPluginColorSpace colorSpace, TexturePaintPluginBlend blend = TexturePaintPluginBlend.Normal,
            float opacity = 1f)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (commandLock)
            {
                if (sealedForCommit) throw new InvalidOperationException("Plugin command context is sealed.");
                if (!descriptor.Declares(channel) && !(target == TexturePaintPluginTarget.LayerMask &&
                        channel == TexturePaintChannel.Custom))
                    throw new InvalidOperationException($"Plugin '{descriptor.id}' did not declare channel {channel}.");
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
                    colorSpace = colorSpace, blend = blend, opacity = Mathf.Clamp01(opacity), target = target
                });
                queuedBytes += bytes;
            }
        }

        public void WriteTileCompact(string surfaceId, TexturePaintChannel channel, RectInt rect,
            IReadOnlyList<Color32> pixels, TexturePaintPluginColorSpace colorSpace,
            TexturePaintPluginBlend blend = TexturePaintPluginBlend.Normal, float opacity = 1f)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (commandLock)
            {
                if (sealedForCommit) throw new InvalidOperationException("Plugin command context is sealed.");
                if (!descriptor.Declares(channel) && !(target == TexturePaintPluginTarget.LayerMask &&
                        channel == TexturePaintChannel.Custom))
                    throw new InvalidOperationException($"Plugin '{descriptor.id}' did not declare channel {channel}.");
                if (rect.width <= 0 || rect.height <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
                int count = checked(rect.width * rect.height);
                if (pixels == null || pixels.Count != count)
                    throw new ArgumentException("Tile pixel count must equal rect width × height.", nameof(pixels));
                if (commands.Count >= 4096) throw new InvalidOperationException("Plugin command count exceeded 4096.");
                long bytes = count * 4L;
                if (queuedBytes + bytes > commandMemoryBudgetBytes)
                    throw new InvalidOperationException("Plugin command memory budget exceeded.");
                if (!IsFinite(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be finite.");
                Color32[] copy = new Color32[count];
                for (int i = 0; i < count; i++) copy[i] = pixels[i];
                commands.Add(new TexturePaintPluginTileCommand
                {
                    surfaceId = surfaceId, channel = channel, rect = rect, compactPixels = copy,
                    colorSpace = colorSpace, blend = blend, opacity = Mathf.Clamp01(opacity), target = target
                });
                queuedBytes += bytes;
            }
        }

        /// <summary>
        /// Queues a compact tile without cloning its storage. The caller relinquishes ownership
        /// and must not read or modify the array after this call. Built-in generators use this
        /// path to avoid copying every generated 2K/4K channel before the GPU upload.
        /// </summary>
        public void WriteTileCompactOwned(string surfaceId, TexturePaintChannel channel,
            RectInt rect, Color32[] pixels, TexturePaintPluginColorSpace colorSpace,
            TexturePaintPluginBlend blend = TexturePaintPluginBlend.Normal, float opacity = 1f)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (commandLock)
            {
                if (sealedForCommit) throw new InvalidOperationException("Plugin command context is sealed.");
                if (!descriptor.Declares(channel) && !(target == TexturePaintPluginTarget.LayerMask &&
                        channel == TexturePaintChannel.Custom))
                    throw new InvalidOperationException($"Plugin '{descriptor.id}' did not declare channel {channel}.");
                if (rect.width <= 0 || rect.height <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
                int count = checked(rect.width * rect.height);
                if (pixels == null || pixels.Length != count)
                    throw new ArgumentException("Tile pixel count must equal rect width × height.", nameof(pixels));
                if (commands.Count >= 4096) throw new InvalidOperationException("Plugin command count exceeded 4096.");
                long bytes = count * 4L;
                if (queuedBytes + bytes > commandMemoryBudgetBytes)
                    throw new InvalidOperationException("Plugin command memory budget exceeded.");
                if (!IsFinite(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be finite.");
                commands.Add(new TexturePaintPluginTileCommand
                {
                    surfaceId = surfaceId, channel = channel, rect = rect, compactPixels = pixels,
                    colorSpace = colorSpace, blend = blend, opacity = Mathf.Clamp01(opacity), target = target
                });
                queuedBytes += bytes;
            }
        }


        public void WriteMaskTileCompact(string surfaceId, RectInt rect,
            IReadOnlyList<Color32> pixels, TexturePaintPluginBlend blend = TexturePaintPluginBlend.Replace,
            float opacity = 1f)
        {
            if (target != TexturePaintPluginTarget.LayerMask)
                throw new InvalidOperationException("Mask output requires a Layer Mask execution context.");
            WriteTileCompact(surfaceId, TexturePaintChannel.Custom, rect, pixels,
                TexturePaintPluginColorSpace.Data, blend, opacity);
        }

        public void WriteMaskTileCompactOwned(string surfaceId, RectInt rect,
            Color32[] pixels, TexturePaintPluginBlend blend = TexturePaintPluginBlend.Replace,
            float opacity = 1f)
        {
            if (target != TexturePaintPluginTarget.LayerMask)
                throw new InvalidOperationException("Mask output requires a Layer Mask execution context.");
            WriteTileCompactOwned(surfaceId, TexturePaintChannel.Custom, rect, pixels,
                TexturePaintPluginColorSpace.Data, blend, opacity);
        }

        public void WriteMaskTile(string surfaceId, RectInt rect,
            IReadOnlyList<Color> pixels, TexturePaintPluginBlend blend = TexturePaintPluginBlend.Replace,
            float opacity = 1f)
        {
            if (target != TexturePaintPluginTarget.LayerMask)
                throw new InvalidOperationException("Mask output requires a Layer Mask execution context.");
            WriteTile(surfaceId, TexturePaintChannel.Custom, rect, pixels,
                TexturePaintPluginColorSpace.Data, blend, opacity);
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

    /// <summary>
    /// Optional fast path for procedural generators whose kernel follows Overlay Painter's
    /// standard mesh-map and parameter bindings. The host uses ExecuteAsync as a CPU fallback
    /// when the configured compute shader or named kernel is unavailable.
    /// </summary>
    public interface ITexturePaintGpuGeneratorV2 : ITexturePaintCommandExtensionV2
    {
        string GpuKernelName { get; }
    }

    /// <summary>
    /// Lets parameterized commands narrow immutable channel snapshots for one execution. The
    /// returned mask must be a subset of the descriptor's declared read-channel contract.
    /// </summary>
    public interface ITexturePaintDynamicChannelUsageV2
    {
        TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters);
    }

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
