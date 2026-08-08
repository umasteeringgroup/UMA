using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        public bool directUV;
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
        public Sprite sourceSprite;
        public OverlayDataAsset sourceOverlay;
        public bool invert;
        public Color color = Color.white;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
        public TexturePaintFillProjection projection = TexturePaintFillProjection.Flat;
        public Vector2 tiling = Vector2.one;
        public Vector2 offset;
        public float rotation;
        public bool useFirstChannelTransform;
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
        public TexturePaintBlendMode brushBlendMode = TexturePaintBlendMode.Normal;
        public bool brushMirrorStroke;
        public bool brushAlignToStroke;
        public Texture2D brushStamp;
        public Sprite brushStampSprite;
        public int brushRandomizationVersion = 1;
        public bool brushRandomRotation;
        [FormerlySerializedAs("brushRandomHeightVariation")]
        public bool brushRandomSizeVariation;
        [FormerlySerializedAs("brushRandomHeightShrink"), Range(0f, 1f)]
        public float brushRandomSizeShrink = 0.3f;
        [FormerlySerializedAs("brushRandomHeightGrow"), Range(0f, 1f)]
        public float brushRandomSizeGrow = 0.3f;
        public bool brushSplatter;
        [Range(0.01f, 2f)] public float brushSplatterDistance = 1f;
        public bool brushFade;
        public bool brushTaper;
        [Min(0f)] public float brushFadeTaperLength;
        public Texture2D sourceTexture;
        public Sprite sourceSprite;
        public OverlayDataAsset sourceOverlay;
        public Color color = Color.white;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
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
    public sealed class TexturePaintChannelSourceSettings
    {
        public TexturePaintBrushSource source = TexturePaintBrushSource.Texture;
        public Texture2D sourceTexture;
        public Sprite sourceSprite;
        public OverlayDataAsset sourceOverlay;
        public Color color = Color.white;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
        public bool invert;
        public Vector2 tiling = Vector2.one;
        public Vector2 offset;
        public float rotation;
        public TexturePaintFillProjection projection = TexturePaintFillProjection.Flat;
        public TexturePaintTriplanarBlend triplanarBlend = TexturePaintTriplanarBlend.CrossFade;
        public float blendOffset;
        public float blendSharpness = 4f;

        public TexturePaintChannelSourceSettings Clone()
        {
            return (TexturePaintChannelSourceSettings)MemberwiseClone();
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
        public TexturePaintChannelSourceSettings sourceSettings;

        public TexturePaintLayerChannelSettings Clone()
        {
            return new TexturePaintLayerChannelSettings
            {
                channel = channel,
                enabled = enabled,
                locked = locked,
                contribution = contribution,
                opacity = opacity,
                blendMode = blendMode,
                sourceSettings = sourceSettings?.Clone()
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
        ColorOverlay,
        EdgeFade,
        BevelEdge,
        ProceduralStitch,
        TextureOverlay
    }

    public enum TexturePaintLayerMaskTextureChannel
    {
        Luminance,
        Red,
        Green,
        Blue,
        Alpha
    }

    [Serializable]
    public sealed class TexturePaintLayerEffectSettings
    {
        public string id = Guid.NewGuid().ToString("N");
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
        [Range(0f, 1f)] public float edgeFadeStart = 0.75f;
        [Range(0f, 1f)] public float edgeFadeSize = 1f;
        public TexturePaintRibbonSide ribbonSide = TexturePaintRibbonSide.Both;
        public Color secondaryColor = Color.black;
        public TexturePaintRibbonBevelTone ribbonLeftTone = TexturePaintRibbonBevelTone.Light;
        public TexturePaintRibbonBevelTone ribbonRightTone = TexturePaintRibbonBevelTone.Dark;
        [Range(-256f, 256f)] public float ribbonLeftOffset;
        [Range(-256f, 256f)] public float ribbonRightOffset;
        public TexturePaintRibbonStitchRows stitchRows = TexturePaintRibbonStitchRows.Single;
        [Range(0.001f, 0.25f)] public float stitchThreadSize = 0.012f;
        [Range(0.01f, 1f)] public float stitchLength = 0.08f;
        [Range(0f, 0.45f)] public float stitchInset = 0.06f;
        public Texture2D texture1;
        public Texture2D texture2;
        public Vector2 textureTiling1 = Vector2.one;
        public Vector2 textureTiling2 = Vector2.one;
        public Vector2 textureOffset1;
        public Vector2 textureOffset2;
        public float textureRotation1;
        public float textureRotation2;
        [Range(0f, 1f)] public float textureOpacity1 = 1f;
        [Range(0f, 1f)] public float textureOpacity2 = 1f;
        public TexturePaintBlendMode secondaryBlendMode = TexturePaintBlendMode.Normal;

        public TexturePaintLayerEffectSettings Clone()
        {
            return new TexturePaintLayerEffectSettings
            {
                id = id,
                kind = kind,
                enabled = enabled,
                channel = channel,
                color = color,
                width = width,
                smoothness = smoothness,
                curve = CloneCurve(curve),
                offset = offset,
                blendMode = blendMode,
                level = level,
                edgeFadeStart = edgeFadeStart,
                edgeFadeSize = edgeFadeSize,
                ribbonSide = ribbonSide,
                secondaryColor = secondaryColor,
                ribbonLeftTone = ribbonLeftTone,
                ribbonRightTone = ribbonRightTone,
                ribbonLeftOffset = ribbonLeftOffset,
                ribbonRightOffset = ribbonRightOffset,
                stitchRows = stitchRows,
                stitchThreadSize = stitchThreadSize,
                stitchLength = stitchLength,
                stitchInset = stitchInset,
                texture1 = texture1,
                texture2 = texture2,
                textureTiling1 = textureTiling1,
                textureTiling2 = textureTiling2,
                textureOffset1 = textureOffset1,
                textureOffset2 = textureOffset2,
                textureRotation1 = textureRotation1,
                textureRotation2 = textureRotation2,
                textureOpacity1 = textureOpacity1,
                textureOpacity2 = textureOpacity2,
                secondaryBlendMode = secondaryBlendMode
            };
        }

        public void Normalize(TexturePaintLayerEffectKind expectedKind)
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            kind = expectedKind;
            width = Mathf.Clamp(width, 0.5f, 256f);
            smoothness = Mathf.Clamp01(smoothness);
            level = Mathf.Clamp01(level);
            offset.x = Mathf.Clamp(offset.x, -256f, 256f);
            offset.y = Mathf.Clamp(offset.y, -256f, 256f);
            edgeFadeStart = Mathf.Clamp01(edgeFadeStart);
            edgeFadeSize = Mathf.Clamp01(edgeFadeSize);
            ribbonLeftOffset = Mathf.Clamp(ribbonLeftOffset, -256f, 256f);
            ribbonRightOffset = Mathf.Clamp(ribbonRightOffset, -256f, 256f);
            stitchThreadSize = Mathf.Clamp(stitchThreadSize, 0.001f, 0.25f);
            stitchLength = Mathf.Clamp(stitchLength, 0.01f, 1f);
            stitchInset = Mathf.Clamp(stitchInset, 0f, 0.45f);
            NormalizeTiling(ref textureTiling1);
            NormalizeTiling(ref textureTiling2);
            textureOpacity1 = Mathf.Clamp01(textureOpacity1);
            textureOpacity2 = Mathf.Clamp01(textureOpacity2);
            curve ??= DefaultCurve();
        }

        private static void NormalizeTiling(ref Vector2 tiling)
        {
            if (Mathf.Abs(tiling.x) < 0.0001f) tiling.x = 1f;
            if (Mathf.Abs(tiling.y) < 0.0001f) tiling.y = 1f;
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
        [SerializeField] private List<TexturePaintLayerEffectSettings> stack = CreateDefaultStack();

        public List<TexturePaintLayerEffectSettings> Stack
        {
            get
            {
                stack ??= new List<TexturePaintLayerEffectSettings>();
                return stack;
            }
        }

        // Named accessors keep the scripting surface concise while the serialized list remains the
        // authoritative, ordered, multi-instance representation.
        public TexturePaintLayerEffectSettings stroke => GetOrCreate(TexturePaintLayerEffectKind.Stroke);
        public TexturePaintLayerEffectSettings innerShadow => GetOrCreate(TexturePaintLayerEffectKind.InnerShadow);
        public TexturePaintLayerEffectSettings outerShadow => GetOrCreate(TexturePaintLayerEffectKind.OuterShadow);
        public TexturePaintLayerEffectSettings innerGlow => GetOrCreate(TexturePaintLayerEffectKind.InnerGlow);
        public TexturePaintLayerEffectSettings outerGlow => GetOrCreate(TexturePaintLayerEffectKind.OuterGlow);
        public TexturePaintLayerEffectSettings colorOverlay => GetOrCreate(TexturePaintLayerEffectKind.ColorOverlay);
        public TexturePaintLayerEffectSettings edgeFade => GetOrCreate(TexturePaintLayerEffectKind.EdgeFade);
        public TexturePaintLayerEffectSettings bevelEdge => GetOrCreate(TexturePaintLayerEffectKind.BevelEdge);
        public TexturePaintLayerEffectSettings proceduralStitch => GetOrCreate(TexturePaintLayerEffectKind.ProceduralStitch);
        public TexturePaintLayerEffectSettings textureOverlay => GetOrCreate(TexturePaintLayerEffectKind.TextureOverlay);

        public bool HasEnabled
        {
            get
            {
                for (int i = 0; i < Stack.Count; i++)
                    if (Stack[i]?.enabled == true) return true;
                return false;
            }
        }

        public TexturePaintLayerEffects Clone()
        {
            var clone = new TexturePaintLayerEffects
            {
                stack = new List<TexturePaintLayerEffectSettings>(Stack.Count)
            };
            for (int i = 0; i < Stack.Count; i++)
                if (Stack[i] != null) clone.stack.Add(Stack[i].Clone());
            return clone;
        }

        public void Normalize()
        {
            for (int i = Stack.Count - 1; i >= 0; i--)
            {
                TexturePaintLayerEffectSettings effect = Stack[i];
                if (effect == null) { Stack.RemoveAt(i); continue; }
                effect.Normalize(effect.kind);
            }
        }

        public TexturePaintLayerEffectSettings Get(TexturePaintLayerEffectKind kind)
        {
            return GetOrCreate(kind);
        }

        public TexturePaintLayerEffectSettings GetFirst(TexturePaintLayerEffectKind kind,
            bool create = false)
        {
            for (int i = 0; i < Stack.Count; i++)
                if (Stack[i]?.kind == kind) return Stack[i];
            return create ? Add(kind) : null;
        }

        public TexturePaintLayerEffectSettings Add(TexturePaintLayerEffectKind kind)
        {
            TexturePaintLayerEffectSettings effect = CreateDefault(kind);
            Stack.Add(effect);
            return effect;
        }

        public bool Remove(string effectId)
        {
            int index = Stack.FindIndex(effect => string.Equals(effect?.id, effectId,
                StringComparison.Ordinal));
            if (index < 0) return false;
            Stack.RemoveAt(index);
            return true;
        }

        public bool Move(int fromIndex, int toIndex)
        {
            if ((uint)fromIndex >= (uint)Stack.Count || (uint)toIndex >= (uint)Stack.Count ||
                fromIndex == toIndex) return false;
            TexturePaintLayerEffectSettings effect = Stack[fromIndex];
            Stack.RemoveAt(fromIndex);
            Stack.Insert(toIndex, effect);
            return true;
        }

        public int MaximumReach(TexturePaintChannel channel)
        {
            Normalize();
            float reach = 0f;
            for (int i = 0; i < Stack.Count; i++)
                IncludeReach(Stack[i], channel, ref reach);
            return Mathf.CeilToInt(reach) + (reach > 0f ? 2 : 0);
        }

        public bool RequiresDistanceField(TexturePaintChannel channel)
        {
            Normalize();
            for (int i = 0; i < Stack.Count; i++)
                if (EnabledFor(Stack[i], channel) && IsDistanceEffect(Stack[i].kind)) return true;
            return false;
        }

        public static bool IsDistanceEffect(TexturePaintLayerEffectKind kind)
        {
            return kind == TexturePaintLayerEffectKind.Stroke ||
                kind == TexturePaintLayerEffectKind.InnerShadow ||
                kind == TexturePaintLayerEffectKind.OuterShadow ||
                kind == TexturePaintLayerEffectKind.InnerGlow ||
                kind == TexturePaintLayerEffectKind.OuterGlow;
        }

        public static bool EnabledFor(TexturePaintLayerEffectSettings effect,
            TexturePaintChannel channel)
        {
            return effect?.enabled == true && effect.channel == channel;
        }

        private static TexturePaintLayerEffectSettings Create(TexturePaintLayerEffectKind kind,
            Color color, float width, float level = 1f)
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = kind,
                color = color,
                width = width,
                level = level,
                curve = TexturePaintLayerEffectSettings.DefaultCurve()
            };
        }

        private TexturePaintLayerEffectSettings GetOrCreate(TexturePaintLayerEffectKind kind)
            => GetFirst(kind, true);

        private static List<TexturePaintLayerEffectSettings> CreateDefaultStack()
        {
            return new List<TexturePaintLayerEffectSettings>
            {
                Create(TexturePaintLayerEffectKind.OuterShadow, new Color(0f, 0f, 0f, 0.75f), 8f),
                Create(TexturePaintLayerEffectKind.OuterGlow, Color.white, 8f),
                Create(TexturePaintLayerEffectKind.Stroke, Color.black, 2f),
                Create(TexturePaintLayerEffectKind.InnerShadow, new Color(0f, 0f, 0f, 0.75f), 8f),
                Create(TexturePaintLayerEffectKind.InnerGlow, Color.white, 8f),
                Create(TexturePaintLayerEffectKind.ColorOverlay, Color.white, 1f),
                CreateTextureOverlay(),
                CreateEdgeFade(),
                CreateBevelEdge(),
                CreateProceduralStitch()
            };
        }

        private static TexturePaintLayerEffectSettings CreateDefault(TexturePaintLayerEffectKind kind)
        {
            return kind switch
            {
                TexturePaintLayerEffectKind.Stroke => Create(kind, Color.black, 2f),
                TexturePaintLayerEffectKind.InnerShadow => Create(kind,
                    new Color(0f, 0f, 0f, 0.75f), 8f),
                TexturePaintLayerEffectKind.OuterShadow => Create(kind,
                    new Color(0f, 0f, 0f, 0.75f), 8f),
                TexturePaintLayerEffectKind.InnerGlow => Create(kind, Color.white, 8f),
                TexturePaintLayerEffectKind.OuterGlow => Create(kind, Color.white, 8f),
                TexturePaintLayerEffectKind.ColorOverlay => Create(kind, Color.white, 1f),
                TexturePaintLayerEffectKind.EdgeFade => CreateEdgeFade(),
                TexturePaintLayerEffectKind.BevelEdge => CreateBevelEdge(),
                TexturePaintLayerEffectKind.ProceduralStitch => CreateProceduralStitch(),
                TexturePaintLayerEffectKind.TextureOverlay => CreateTextureOverlay(),
                _ => Create(kind, Color.white, 1f)
            };
        }

        private static TexturePaintLayerEffectSettings CreateEdgeFade()
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = TexturePaintLayerEffectKind.EdgeFade,
                edgeFadeStart = 0.75f,
                edgeFadeSize = 1f
            };
        }

        private static TexturePaintLayerEffectSettings CreateBevelEdge()
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = TexturePaintLayerEffectKind.BevelEdge,
                color = Color.white,
                secondaryColor = Color.black,
                width = 4f,
                smoothness = 0.35f,
                level = 0.6f,
                ribbonSide = TexturePaintRibbonSide.Both,
                ribbonLeftTone = TexturePaintRibbonBevelTone.Light,
                ribbonRightTone = TexturePaintRibbonBevelTone.Dark
            };
        }

        private static TexturePaintLayerEffectSettings CreateProceduralStitch()
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = TexturePaintLayerEffectKind.ProceduralStitch,
                color = Color.white,
                level = 1f,
                ribbonSide = TexturePaintRibbonSide.Both,
                stitchRows = TexturePaintRibbonStitchRows.Single,
                stitchThreadSize = 0.012f,
                stitchLength = 0.08f,
                stitchInset = 0.06f
            };
        }

        private static TexturePaintLayerEffectSettings CreateTextureOverlay()
        {
            return new TexturePaintLayerEffectSettings
            {
                kind = TexturePaintLayerEffectKind.TextureOverlay,
                color = Color.white,
                secondaryColor = Color.white,
                blendMode = TexturePaintBlendMode.Normal,
                secondaryBlendMode = TexturePaintBlendMode.Normal,
                textureTiling1 = Vector2.one,
                textureTiling2 = Vector2.one,
                textureOpacity1 = 1f,
                textureOpacity2 = 1f,
                level = 1f
            };
        }

        private static void IncludeReach(TexturePaintLayerEffectSettings effect,
            TexturePaintChannel channel, ref float reach)
        {
            if (!EnabledFor(effect, channel)) return;
            if (effect.kind == TexturePaintLayerEffectKind.Stroke)
            {
                // Stroke offset is radial. Dirty-region expansion must cover the farther end of
                // the band even when a large negative offset places it completely inside.
                float nearEdge = effect.offset.x;
                float farEdge = effect.offset.x + effect.width;
                reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(nearEdge), Mathf.Abs(farEdge)));
                return;
            }
            reach = Mathf.Max(reach, effect.width + Mathf.Max(
                Mathf.Abs(effect.offset.x), Mathf.Abs(effect.offset.y)));
        }
    }

    [Serializable]
    public sealed class TexturePaintLayerMaskNoiseSettings
    {
        public bool enabled;
        public int seed;
        public Vector2 tiling = new Vector2(4f, 4f);
        public Vector2 offset;
        [Range(1, 8)] public int octaves = 4;
        [Range(0f, 1f)] public float balance = 0.5f;
        [Range(0.01f, 8f)] public float contrast = 1f;
        public bool invert;
        [Range(0f, 1f)] public float opacity = 1f;
        public TexturePaintBlendMode combine = TexturePaintBlendMode.Multiply;

        public TexturePaintLayerMaskNoiseSettings Clone() => (TexturePaintLayerMaskNoiseSettings)MemberwiseClone();

        public void Normalize()
        {
            if (Mathf.Abs(tiling.x) < 0.0001f) tiling.x = 1f;
            if (Mathf.Abs(tiling.y) < 0.0001f) tiling.y = 1f;
            octaves = Mathf.Clamp(octaves, 1, 8);
            balance = Mathf.Clamp01(balance);
            contrast = Mathf.Clamp(contrast, 0.01f, 8f);
            opacity = Mathf.Clamp01(opacity);
        }
    }

    [Serializable]
    public sealed class TexturePaintLayerMaskTextureOverlaySettings
    {
        public bool enabled;
        public Texture2D texture;
        public TexturePaintLayerMaskTextureChannel sourceChannel = TexturePaintLayerMaskTextureChannel.Luminance;
        public Vector2 tiling = Vector2.one;
        public Vector2 offset;
        public float rotation;
        public bool invert;
        [Range(0f, 1f)] public float opacity = 1f;
        public TexturePaintBlendMode combine = TexturePaintBlendMode.Multiply;

        public TexturePaintLayerMaskTextureOverlaySettings Clone() =>
            (TexturePaintLayerMaskTextureOverlaySettings)MemberwiseClone();

        public void Normalize()
        {
            if (Mathf.Abs(tiling.x) < 0.0001f) tiling.x = 1f;
            if (Mathf.Abs(tiling.y) < 0.0001f) tiling.y = 1f;
            opacity = Mathf.Clamp01(opacity);
        }
    }

    [Serializable]
    public sealed class TexturePaintLayerMaskEffects
    {
        public TexturePaintLayerMaskNoiseSettings noise = new TexturePaintLayerMaskNoiseSettings();
        public TexturePaintLayerMaskTextureOverlaySettings textureOverlay =
            new TexturePaintLayerMaskTextureOverlaySettings();

        public bool HasEnabled => noise?.enabled == true || textureOverlay?.enabled == true;

        public TexturePaintLayerMaskEffects Clone()
        {
            return new TexturePaintLayerMaskEffects
            {
                noise = noise?.Clone() ?? new TexturePaintLayerMaskNoiseSettings(),
                textureOverlay = textureOverlay?.Clone() ?? new TexturePaintLayerMaskTextureOverlaySettings()
            };
        }

        public void Normalize()
        {
            noise ??= new TexturePaintLayerMaskNoiseSettings();
            textureOverlay ??= new TexturePaintLayerMaskTextureOverlaySettings();
            noise.Normalize();
            textureOverlay.Normalize();
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
        public TexturePaintBlendMode brushBlendMode = TexturePaintBlendMode.Normal;
        public bool brushMirrorStroke;
        public bool brushAlignToStroke;
        public Texture2D brushStamp;
        public Sprite brushStampSprite;
        public int brushRandomizationVersion = 1;
        public bool brushRandomRotation;
        [FormerlySerializedAs("brushRandomHeightVariation")]
        public bool brushRandomSizeVariation;
        [FormerlySerializedAs("brushRandomHeightShrink"), Range(0f, 1f)]
        public float brushRandomSizeShrink = 0.3f;
        [FormerlySerializedAs("brushRandomHeightGrow"), Range(0f, 1f)]
        public float brushRandomSizeGrow = 0.3f;
        public bool brushSplatter;
        [Range(0.01f, 2f)] public float brushSplatterDistance = 1f;
        public bool brushFade;
        public bool brushTaper;
        [Min(0f)] public float brushFadeTaperLength;
        public Texture2D sourceTexture;
        public Sprite sourceSprite;
        public Texture2D ribbonBeginningTexture;
        public Sprite ribbonBeginningSprite;
        public Texture2D ribbonEndTexture;
        public Sprite ribbonEndSprite;
        public OverlayDataAsset sourceOverlay;
        public Color color = Color.white;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
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
        public bool hasSourceSettings;
        public TexturePaintBrushSource source = TexturePaintBrushSource.Color;
        public Texture2D sourceTexture;
        public Sprite sourceSprite;
        public OverlayDataAsset sourceOverlay;
        public Color sourceColor = Color.white;
        public TexturePaintNormalConvention sourceNormalConvention = TexturePaintNormalConvention.OpenGL;
        public bool sourceInvert;
        public Vector2 sourceTiling = Vector2.one;
        public Vector2 sourceOffset;
        public float sourceRotation;
        public TexturePaintFillProjection sourceProjection = TexturePaintFillProjection.Flat;
        public TexturePaintTriplanarBlend sourceTriplanarBlend = TexturePaintTriplanarBlend.CrossFade;
        public float sourceBlendOffset;
        public float sourceBlendSharpness = 4f;
        public TexturePaintPixelData pixels = new TexturePaintPixelData();

        public void SetSourceSettings(TexturePaintChannelSourceSettings value)
        {
            hasSourceSettings = value != null;
            if (value == null) return;
            source = value.source;
            sourceTexture = value.sourceTexture;
            sourceSprite = value.sourceSprite;
            sourceOverlay = value.sourceOverlay;
            sourceColor = value.color;
            sourceNormalConvention = value.normalConvention;
            sourceInvert = value.invert;
            sourceTiling = value.tiling;
            sourceOffset = value.offset;
            sourceRotation = value.rotation;
            sourceProjection = value.projection;
            sourceTriplanarBlend = value.triplanarBlend;
            sourceBlendOffset = value.blendOffset;
            sourceBlendSharpness = value.blendSharpness;
        }

        public TexturePaintChannelSourceSettings GetSourceSettings()
        {
            if (!hasSourceSettings)
            {
                TexturePaintChannelSourceSettings legacy = settings?.sourceSettings;
                return LegacySourceIsAuthored(legacy) ? legacy.Clone() : null;
            }
            return new TexturePaintChannelSourceSettings
            {
                source = source,
                sourceTexture = sourceTexture,
                sourceSprite = sourceSprite,
                sourceOverlay = sourceOverlay,
                color = sourceColor,
                normalConvention = sourceNormalConvention,
                invert = sourceInvert,
                tiling = sourceTiling,
                offset = sourceOffset,
                rotation = sourceRotation,
                projection = sourceProjection,
                triplanarBlend = sourceTriplanarBlend,
                blendOffset = sourceBlendOffset,
                blendSharpness = sourceBlendSharpness
            };
        }

        public void MigrateLegacySourceSettings()
        {
            if (hasSourceSettings) return;
            TexturePaintChannelSourceSettings legacy = settings?.sourceSettings;
            if (LegacySourceIsAuthored(legacy)) SetSourceSettings(legacy);
        }

        private static bool LegacySourceIsAuthored(TexturePaintChannelSourceSettings legacy)
        {
            if (legacy == null) return false;
            return legacy.sourceTexture != null || legacy.sourceSprite != null ||
                legacy.sourceOverlay != null || legacy.source == TexturePaintBrushSource.Color ||
                legacy.invert || (legacy.tiling - Vector2.one).sqrMagnitude > 0.000001f ||
                legacy.offset.sqrMagnitude > 0.000001f || Mathf.Abs(legacy.rotation) > 0.000001f ||
                legacy.projection != TexturePaintFillProjection.Flat ||
                legacy.triplanarBlend != TexturePaintTriplanarBlend.CrossFade ||
                Mathf.Abs(legacy.blendOffset) > 0.000001f ||
                Mathf.Abs(legacy.blendSharpness - 4f) > 0.000001f;
        }
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
        public bool hasMask;
        [Range(0f, 1f)] public float maskBaseValue = 1f;
        public TexturePaintLayerMaskEffects maskEffects = new TexturePaintLayerMaskEffects();
        public TexturePaintChannelSourceSettings maskSourceSettings =
            TexturePaintLayerMask.DefaultSourceSettings();
        public TexturePaintChannel maskSourceChannel = TexturePaintChannel.Albedo;
        public TexturePaintPixelData maskPixels = new TexturePaintPixelData();
        public List<TexturePaintDocumentLayerChannel> channels = new List<TexturePaintDocumentLayerChannel>();
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
        public const int CurrentSchemaVersion = 19;

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
                    layer.strokes ??= new List<TexturePaintStrokeRecord>();
                    layer.effects ??= new TexturePaintLayerEffects();
                    layer.effects.Normalize();
                    layer.maskEffects ??= new TexturePaintLayerMaskEffects();
                    layer.maskEffects.Normalize();
                    layer.maskSourceSettings ??= TexturePaintLayerMask.DefaultSourceSettings();
                    if (loadedSchemaVersion < 16 && layer.hasMask)
                    {
                        float paintValue = layer.maskBaseValue < 0.5f ? 1f : 0f;
                        layer.maskSourceSettings.source = TexturePaintBrushSource.Color;
                        layer.maskSourceSettings.color = new Color(paintValue, paintValue, paintValue, 1f);
                    }
                    layer.maskPixels ??= new TexturePaintPixelData();
                    if (loadedSchemaVersion < 15)
                    {
                        layer.hasMask = false;
                    }
                    for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                    {
                        TexturePaintDocumentLayerChannel channel = layer.channels[channelIndex];
                        if (channel == null) continue;
                        channel.pixels ??= new TexturePaintPixelData();
                        if (loadedSchemaVersion < 14) channel.MigrateLegacySourceSettings();
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
