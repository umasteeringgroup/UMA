using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [Serializable]
    public sealed class TexturePaintStrokeRecord
    {
        public string id = Guid.NewGuid().ToString("N");
        public string createdUtc;
        public string historyGroupKey;
        public TexturePaintTool tool;
        public TexturePaintChannel channel;
        public List<StrokeSample> samples = new List<StrokeSample>();
    }

    public enum TexturePaintLayerKind
    {
        Paint,
        Fill,
        Spline,
        Group
    }

    public enum TexturePaintFillProjection
    {
        Flat,
        Triplanar
    }

    public enum TexturePaintTriplanarBlend
    {
        Hard,
        CrossFade
    }

    [Serializable]
    public sealed class TexturePaintFillSettings
    {
        [HideInInspector] public int generatorRevision;
        public TexturePaintBrushSource source = TexturePaintBrushSource.Color;
        public Texture2D sourceTexture;
        public OverlayDataAsset sourceOverlay;
        public Color color = Color.white;
        public TexturePaintFillProjection projection = TexturePaintFillProjection.Flat;
        public Vector2 tiling = Vector2.one;
        public TexturePaintTriplanarBlend triplanarBlend = TexturePaintTriplanarBlend.CrossFade;
        [Range(0f, 0.49f)] public float blendOffset;
        [Range(0.5f, 32f)] public float blendSharpness = 4f;

        public TexturePaintFillSettings Clone()
        {
            return (TexturePaintFillSettings)MemberwiseClone();
        }

        public void Normalize()
        {
            if (Mathf.Abs(tiling.x) < 0.0001f) tiling.x = 1f;
            if (Mathf.Abs(tiling.y) < 0.0001f) tiling.y = 1f;
            blendOffset = Mathf.Clamp(blendOffset, 0f, 0.49f);
            blendSharpness = Mathf.Clamp(blendSharpness, 0.5f, 32f);
        }
    }

    [Serializable]
    public sealed class TexturePaintLayerSettings
    {
        public TexturePaintTool tool = TexturePaintTool.Paint;
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        public TexturePaintBrushSource source = TexturePaintBrushSource.Color;
        public TexturePaintSourceMode destination = TexturePaintSourceMode.SourceOverlay;
        public BrushPreset brush;
        public BrushPreset.Shape brushShape = BrushPreset.Shape.Circle;
        public float brushSize = 0.05f;
        [Range(0f, 1f)] public float brushHardness = 0.7f;
        [Range(0f, 1f)] public float brushFlow = 1f;
        public float brushSpacing = 0.2f;
        public float brushRotation;
        public bool brushAlignToStroke;
        public Texture2D brushStamp;
        public Texture2D sourceTexture;
        public OverlayDataAsset sourceOverlay;
        public Color color = Color.white;
        [Range(0f, 1f)] public float strength = 1f;
        public bool limitStrokeCoverage;
        public bool mirrorX;
        public float stabilization;
        public float directionSmoothing = 0.35f;
        public float projectionDepth = 0.5f;
        public float normalAngleLimit = 90f;
        public bool paintBackfaces;
        public bool pressureAffectsFlow = true;
        public bool pressureAffectsSize;

        public TexturePaintLayerSettings Clone()
        {
            return (TexturePaintLayerSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class TexturePaintLayerChannelSettings
    {
        public TexturePaintChannel channel;
        public bool enabled = true;
        public bool locked;
        [Range(0f, 1f)] public float contribution = 1f;
        [Range(0f, 1f)] public float opacity = 1f;
        public TexturePaintBlendMode blendMode = TexturePaintBlendMode.Normal;

        public TexturePaintLayerChannelSettings Clone()
        {
            return new TexturePaintLayerChannelSettings
            {
                channel = channel,
                enabled = enabled,
                locked = locked,
                contribution = contribution,
                opacity = opacity,
                blendMode = blendMode
            };
        }
    }

    public enum TexturePaintLayerEffectKind
    {
        Stroke,
        InnerShadow,
        OuterShadow,
        InnerGlow,
        OuterGlow,
        ColorOverlay
    }

    [Serializable]
    public sealed class TexturePaintLayerEffectSettings
    {
        public TexturePaintLayerEffectKind kind;
        public bool enabled;
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        public Color color = Color.black;
        [Range(0.5f, 256f)] public float width = 8f;
        [Range(0f, 1f)] public float smoothness = 0.25f;
        public AnimationCurve curve = DefaultCurve();
        public Vector2 offset;
        public TexturePaintBlendMode blendMode = TexturePaintBlendMode.Normal;
        [Range(0f, 1f)] public float level = 1f;

        public TexturePaintLayerEffectSettings Clone()
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = kind,
                enabled = enabled,
                channel = channel,
                color = color,
                width = width,
                smoothness = smoothness,
                curve = CloneCurve(curve),
                offset = offset,
                blendMode = blendMode,
                level = level
            };
        }

        public void Normalize(TexturePaintLayerEffectKind expectedKind)
        {
            kind = expectedKind;
            width = Mathf.Clamp(width, 0.5f, 256f);
            smoothness = Mathf.Clamp01(smoothness);
            level = Mathf.Clamp01(level);
            curve ??= DefaultCurve();
        }

        internal static AnimationCurve DefaultCurve()
        {
            return AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null) return DefaultCurve();
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
        }
    }

    [Serializable]
    public sealed class TexturePaintLayerEffects
    {
        public TexturePaintLayerEffectSettings stroke = Create(
            TexturePaintLayerEffectKind.Stroke, Color.black, 2f);
        public TexturePaintLayerEffectSettings innerShadow = Create(
            TexturePaintLayerEffectKind.InnerShadow, new Color(0f, 0f, 0f, 0.75f), 8f);
        public TexturePaintLayerEffectSettings outerShadow = Create(
            TexturePaintLayerEffectKind.OuterShadow, new Color(0f, 0f, 0f, 0.75f), 8f);
        public TexturePaintLayerEffectSettings innerGlow = Create(
            TexturePaintLayerEffectKind.InnerGlow, Color.white, 8f);
        public TexturePaintLayerEffectSettings outerGlow = Create(
            TexturePaintLayerEffectKind.OuterGlow, Color.white, 8f);
        public TexturePaintLayerEffectSettings colorOverlay = Create(
            TexturePaintLayerEffectKind.ColorOverlay, Color.white, 1f);

        public bool HasEnabled =>
            stroke?.enabled == true || innerShadow?.enabled == true || outerShadow?.enabled == true ||
            innerGlow?.enabled == true || outerGlow?.enabled == true || colorOverlay?.enabled == true;

        public TexturePaintLayerEffects Clone()
        {
            return new TexturePaintLayerEffects
            {
                stroke = stroke?.Clone(),
                innerShadow = innerShadow?.Clone(),
                outerShadow = outerShadow?.Clone(),
                innerGlow = innerGlow?.Clone(),
                outerGlow = outerGlow?.Clone(),
                colorOverlay = colorOverlay?.Clone()
            };
        }

        public void Normalize()
        {
            stroke ??= Create(TexturePaintLayerEffectKind.Stroke, Color.black, 2f);
            innerShadow ??= Create(TexturePaintLayerEffectKind.InnerShadow,
                new Color(0f, 0f, 0f, 0.75f), 8f);
            outerShadow ??= Create(TexturePaintLayerEffectKind.OuterShadow,
                new Color(0f, 0f, 0f, 0.75f), 8f);
            innerGlow ??= Create(TexturePaintLayerEffectKind.InnerGlow, Color.white, 8f);
            outerGlow ??= Create(TexturePaintLayerEffectKind.OuterGlow, Color.white, 8f);
            colorOverlay ??= Create(TexturePaintLayerEffectKind.ColorOverlay, Color.white, 1f);
            stroke.Normalize(TexturePaintLayerEffectKind.Stroke);
            innerShadow.Normalize(TexturePaintLayerEffectKind.InnerShadow);
            outerShadow.Normalize(TexturePaintLayerEffectKind.OuterShadow);
            innerGlow.Normalize(TexturePaintLayerEffectKind.InnerGlow);
            outerGlow.Normalize(TexturePaintLayerEffectKind.OuterGlow);
            colorOverlay.Normalize(TexturePaintLayerEffectKind.ColorOverlay);
        }

        public TexturePaintLayerEffectSettings Get(TexturePaintLayerEffectKind kind)
        {
            Normalize();
            return kind switch
            {
                TexturePaintLayerEffectKind.Stroke => stroke,
                TexturePaintLayerEffectKind.InnerShadow => innerShadow,
                TexturePaintLayerEffectKind.OuterShadow => outerShadow,
                TexturePaintLayerEffectKind.InnerGlow => innerGlow,
                TexturePaintLayerEffectKind.OuterGlow => outerGlow,
                _ => colorOverlay
            };
        }

        public int MaximumReach(TexturePaintChannel channel)
        {
            Normalize();
            float reach = 0f;
            IncludeReach(stroke, channel, ref reach);
            IncludeReach(innerShadow, channel, ref reach);
            IncludeReach(outerShadow, channel, ref reach);
            IncludeReach(innerGlow, channel, ref reach);
            IncludeReach(outerGlow, channel, ref reach);
            return Mathf.CeilToInt(reach) + (reach > 0f ? 2 : 0);
        }

        public bool RequiresDistanceField(TexturePaintChannel channel)
        {
            Normalize();
            return EnabledFor(stroke, channel) || EnabledFor(innerShadow, channel) ||
                EnabledFor(outerShadow, channel) || EnabledFor(innerGlow, channel) ||
                EnabledFor(outerGlow, channel);
        }

        public static bool EnabledFor(TexturePaintLayerEffectSettings effect,
            TexturePaintChannel channel)
        {
            return effect?.enabled == true && effect.channel == channel;
        }

        private static TexturePaintLayerEffectSettings Create(TexturePaintLayerEffectKind kind,
            Color color, float width)
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = kind,
                color = color,
                width = width,
                curve = TexturePaintLayerEffectSettings.DefaultCurve()
            };
        }

        private static void IncludeReach(TexturePaintLayerEffectSettings effect,
            TexturePaintChannel channel, ref float reach)
        {
            if (!EnabledFor(effect, channel)) return;
            reach = Mathf.Max(reach, effect.width + Mathf.Max(
                Mathf.Abs(effect.offset.x), Mathf.Abs(effect.offset.y)));
        }
    }

    [Serializable]
    public sealed class TexturePaintSplineSettings
    {
        public TexturePaintTool tool = TexturePaintTool.Paint;
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        public TexturePaintBrushSource source = TexturePaintBrushSource.Color;
        public TexturePaintSourceMode destination = TexturePaintSourceMode.SourceOverlay;
        public BrushPreset brush;
        public BrushPreset.Shape brushShape = BrushPreset.Shape.Circle;
        public float brushSize = 0.05f;
        [Range(0f, 1f)] public float brushHardness = 0.7f;
        [Range(0f, 1f)] public float brushFlow = 1f;
        public float brushSpacing = 0.2f;
        public float brushRotation;
        public Texture2D brushStamp;
        public Texture2D sourceTexture;
        public OverlayDataAsset sourceOverlay;
        public Color color = Color.white;
        [Range(0f, 1f)] public float strength = 1f;
        public bool limitStrokeCoverage;
        public bool mirrorX;
        public float stabilization;
        public float directionSmoothing = 0.35f;
        public float projectionDepth = 0.5f;
        public float normalAngleLimit = 90f;
        public bool paintBackfaces;
        public bool pressureAffectsFlow = true;
        public bool pressureAffectsSize;
        public TexturePaintPathMode pathMode = TexturePaintPathMode.Ribbon;
        public TexturePaintPathOrientation orientation = TexturePaintPathOrientation.FollowPath;
        public TexturePaintPathCap startCap = TexturePaintPathCap.Round;
        public TexturePaintPathCap endCap = TexturePaintPathCap.Round;
        public int radialSymmetry = 1;
        public Vector3 symmetryAxis = Vector3.up;

        public TexturePaintSplineSettings Clone()
        {
            return (TexturePaintSplineSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class TexturePaintPixelData
    {
        public int width;
        public int height;
        public TextureFormat textureFormat = TextureFormat.RGBA32;
        public bool linear = true;
        public int uncompressedByteCount;
        public string storageKey;
        public string checksum;
        public string recoveryBlobKey;
        public TextAsset dataAsset;
        public byte[] compressedBytes;

        public bool HasData => width > 0 && height > 0 &&
            ((compressedBytes != null && compressedBytes.Length > 0) || dataAsset != null);

        public byte[] GetCompressedBytes()
        {
            if (compressedBytes != null && compressedBytes.Length > 0) return compressedBytes;
            return dataAsset != null ? dataAsset.bytes : null;
        }
    }

    [Serializable]
    public sealed class TexturePaintDocumentChannel
    {
        public TexturePaintChannel channel;
        public string materialProperty;
        public string sourceKeyword;
        public int umaChannelIndex = -1;
        public RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGB32;
        public bool sRGB;
        public TexturePaintPixelData pixels = new TexturePaintPixelData();
    }

    [Serializable]
    public sealed class TexturePaintDocumentLayerChannel
    {
        public TexturePaintChannel channel;
        public TexturePaintLayerChannelSettings settings = new TexturePaintLayerChannelSettings();
        public TexturePaintPixelData pixels = new TexturePaintPixelData();
    }

    [Serializable]
    public sealed class TexturePaintDocumentLayer
    {
        public string id = Guid.NewGuid().ToString("N");
        public string logicalLayerId;
        public string paintTargetId;
        public string parentId;
        public string name = "Paint Layer";
        public TexturePaintLayerKind kind = TexturePaintLayerKind.Paint;
        public bool visible = true;
        [Range(0f, 1f)] public float opacity = 1f;
        public TexturePaintBlendMode blendMode = TexturePaintBlendMode.Normal;
        public TexturePaintLayerEffects effects = new TexturePaintLayerEffects();
        public TexturePaintChannel fillChannel = TexturePaintChannel.Albedo;
        public Color fillColor = Color.white;
        public TexturePaintFillSettings fillSettings;
        public TexturePaintLayerSettings paintSettings;
        public TexturePaintSpline spline;
        public TexturePaintSplineSettings splineSettings;
        public string pluginId;
        public string pluginVersion;
        public string pluginParametersJson;
        public string proceduralGroupKey;
        public List<TexturePaintDocumentLayerChannel> channels = new List<TexturePaintDocumentLayerChannel>();
        public List<TexturePaintMask> masks = new List<TexturePaintMask>();
        public List<TexturePaintStrokeRecord> strokes = new List<TexturePaintStrokeRecord>();
    }

    [Serializable]
    public sealed class TexturePaintDocumentSurface
    {
        public string stableId;
        public string materialName;
        public string umaMaterialGuid;
        public string meshSignature;
        public string topologySignature;
        public string uvSignature;
        public string materialSignature;
        public bool orphaned;
        public string orphanReason;
        public List<string> slotNames = new List<string>();
        public int fallbackRendererIndex;
        public int fallbackSubmeshIndex;
        public int activeLayer = -1;
        public List<TexturePaintDocumentChannel> baseChannels = new List<TexturePaintDocumentChannel>();
        public List<TexturePaintStrokeRecord> baseStrokes = new List<TexturePaintStrokeRecord>();
        public List<TexturePaintDocumentLayer> layers = new List<TexturePaintDocumentLayer>();
    }

    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Document", fileName = "Overlay Painter Document")]
    public sealed class TexturePaintDocument : ScriptableObject
    {
        public const int CurrentSchemaVersion = 10;

        public int schemaVersion = CurrentSchemaVersion;
        public string documentId = Guid.NewGuid().ToString("N");
        public string revisionId = Guid.NewGuid().ToString("N");
        public string avatarName;
        public string avatarGlobalObjectId;
        public TexturePaintLaunchContext launchContext;
        public string createdUtc;
        public string lastSavedUtc;
        public bool recoverySnapshot;
        [HideInInspector] public string recoveryContextKey;
        [HideInInspector] public string editorStateJson;
        public List<TexturePaintDocumentSurface> surfaces = new List<TexturePaintDocumentSurface>();
        public List<TexturePaintMask> globalMasks = new List<TexturePaintMask>();

        public TexturePaintDocumentSurface FindSurface(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < surfaces.Count; i++)
                if (!surfaces[i].orphaned && string.Equals(surfaces[i].stableId, stableId, StringComparison.Ordinal)) return surfaces[i];
            return null;
        }

        public void Migrate()
        {
            int loadedSchemaVersion = schemaVersion;
            if (schemaVersion <= 0) schemaVersion = 1;
            if (string.IsNullOrEmpty(documentId)) documentId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(revisionId)) revisionId = Guid.NewGuid().ToString("N");
            if (surfaces == null) surfaces = new List<TexturePaintDocumentSurface>();
            if (globalMasks == null) globalMasks = new List<TexturePaintMask>();
            for (int surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
            {
                TexturePaintDocumentSurface surface = surfaces[surfaceIndex];
                if (surface == null) continue;
                surface.slotNames ??= new List<string>();
                surface.baseChannels ??= new List<TexturePaintDocumentChannel>();
                surface.baseStrokes ??= new List<TexturePaintStrokeRecord>();
                surface.layers ??= new List<TexturePaintDocumentLayer>();
                for (int channelIndex = 0; channelIndex < surface.baseChannels.Count; channelIndex++)
                {
                    TexturePaintDocumentChannel channel = surface.baseChannels[channelIndex];
                    if (channel != null) channel.pixels ??= new TexturePaintPixelData();
                }
                for (int layerIndex = 0; layerIndex < surface.layers.Count; layerIndex++)
                {
                    TexturePaintDocumentLayer layer = surface.layers[layerIndex];
                    if (layer == null) continue;
                    layer.channels ??= new List<TexturePaintDocumentLayerChannel>();
                    layer.masks ??= new List<TexturePaintMask>();
                    layer.strokes ??= new List<TexturePaintStrokeRecord>();
                    layer.effects ??= new TexturePaintLayerEffects();
                    layer.effects.Normalize();
                    for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                    {
                        TexturePaintDocumentLayerChannel channel = layer.channels[channelIndex];
                        if (channel != null) channel.pixels ??= new TexturePaintPixelData();
                    }
                    if (layer.kind == TexturePaintLayerKind.Fill)
                    {
                        layer.fillSettings ??= new TexturePaintFillSettings
                        {
                            source = TexturePaintBrushSource.Color,
                            color = layer.fillColor
                        };
                        layer.fillSettings.Normalize();
                        layer.fillColor = layer.fillSettings.color;
                    }
                    if (loadedSchemaVersion < 3)
                    {
                        for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                        {
                            TexturePaintLayerChannelSettings settings = layer.channels[channelIndex]?.settings;
                            if (settings != null) settings.contribution = 1f;
                        }
                    }
                }
            }
            schemaVersion = CurrentSchemaVersion;
        }
    }
}
