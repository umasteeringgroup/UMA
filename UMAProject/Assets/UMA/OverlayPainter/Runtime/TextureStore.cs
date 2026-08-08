using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public sealed class EditableTextureTarget : IDisposable
    {
        public RenderTexture Front { get; private set; }
        public RenderTexture Back { get; private set; }
        public int Width => Front != null ? Front.width : 0;
        public int Height => Front != null ? Front.height : 0;
        public long Revision { get; private set; }

        /// <summary>
        /// Creates a linear working target. Paint, filtering, and layer compositing all operate in
        /// linear space; whether the finished channel is exported as sRGB belongs to
        /// <see cref="TextureChannelTarget.sRGB"/>, not to its UAV-backed working textures.
        /// </summary>
        public EditableTextureTarget(string name, int width, int height, RenderTextureFormat format, Texture source, Color clear)
        {
            Front = Create(name + " A", width, height, format);
            Back = Create(name + " B", width, height, format);
            Reset(source, clear);
        }

        public void Reset(Texture source, Color clear)
        {
            if (source != null)
            {
                Graphics.Blit(source, Front);
                Graphics.Blit(source, Back);
            }
            else
            {
                Clear(Front, clear); Clear(Back, clear);
            }
            Revision++;
        }

        public void CopyFrontToBack()
        {
            Graphics.CopyTexture(Front, Back);
            Revision++;
        }
        public void CopyFrontToBack(RectInt rect)
        {
            rect = Clamp(rect);
            if (rect.width <= 0 || rect.height <= 0) return;
            Graphics.CopyTexture(Front, 0, 0, rect.x, rect.y, rect.width, rect.height,
                Back, 0, 0, rect.x, rect.y);
            Revision++;
        }
        public void Swap() { RenderTexture temporary = Front; Front = Back; Back = temporary; Revision++; }

        public void SwapAndSynchronize(RectInt rect)
        {
            Swap();
            CopyFrontToBack(rect);
        }

        public void Dispose()
        {
            Destroy(Front); Destroy(Back); Front = null; Back = null;
        }

        internal static RenderTexture Create(string name, int width, int height, RenderTextureFormat format)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, format, 0)
            {
                enableRandomWrite = true,
                // Compute-shader UAV reads and writes do not perform sRGB transfer conversion.
                // Keeping every editable buffer linear prevents a second sRGB decode in preview
                // materials and makes every paint/composite operation use one color space.
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            RenderTexture texture = new RenderTexture(descriptor)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.Create();
            return texture;
        }

        private static void Clear(RenderTexture texture, Color color)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, color);
            RenderTexture.active = previous;
        }

        private RectInt Clamp(RectInt rect)
        {
            int xMin = Mathf.Clamp(rect.xMin, 0, Width), yMin = Mathf.Clamp(rect.yMin, 0, Height);
            int xMax = Mathf.Clamp(rect.xMax, xMin, Width), yMax = Mathf.Clamp(rect.yMax, yMin, Height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static void Destroy(RenderTexture texture)
        {
            if (texture == null) return;
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    public sealed class TextureChannelTarget : IDisposable
    {
        public TexturePaintChannel channel;
        public string materialProperty;
        public string sourceKeyword;
        public int umaChannelIndex = -1;
        public Texture sourceTexture;
        public TexturePaintNormalConvention sourceNormalConvention = TexturePaintNormalConvention.OpenGL;
        public bool sourceNormalIsUnityPacked;
        public EditableTextureTarget editable;
        public bool sRGB;
        public RenderTextureFormat format;
        public RenderTexture composite;
        public string physicalProperty;
        public int packedComponent = -1;
        public bool packedInverted;

        public RenderTexture Texture => editable?.Front;
        public RenderTexture PreviewTexture => composite != null ? composite : Texture;

        public Texture ResolveEditableSource(Texture source)
        {
            return channel == TexturePaintChannel.Normal
                ? TexturePaintSpriteSource.ResolveTexture(source, channel, sourceNormalConvention, false,
                    sourceNormalIsUnityPacked)
                : source;
        }

        public void ResetToSource(Texture source)
        {
            sourceTexture = source;
            editable?.Reset(ResolveEditableSource(source), TextureSet.DefaultColor(channel));
        }

        public void Dispose()
        {
            editable?.Dispose();
            if (composite == null) return;
            if (RenderTexture.active == composite) RenderTexture.active = null;
            composite.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(composite);
            else UnityEngine.Object.DestroyImmediate(composite);
            composite = null;
        }
    }

    public sealed class TexturePhysicalChannelGroup : IDisposable
    {
        public string materialProperty;
        public Texture source;
        public RenderTexture packed;
        public readonly TextureChannelTarget[] componentTargets = new TextureChannelTarget[4];
        public readonly int[] sourceComponents = { 0, 1, 2, 3 };
        public readonly bool[] inverted = new bool[4];
        public void Dispose()
        {
            if (packed == null) return;
            if (RenderTexture.active == packed) RenderTexture.active = null;
            packed.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(packed); else UnityEngine.Object.DestroyImmediate(packed);
            packed = null;
        }
    }

    public sealed class TextureSourceBinding
    {
        public string name;
        public OverlayData overlay;
        public readonly List<string> slotNames = new List<string>();
        public readonly Dictionary<TexturePaintChannel, Texture> textures = new Dictionary<TexturePaintChannel, Texture>();
    }

    /// <summary>
    /// An editable, grayscale layer mask. The value is replicated to RGB and alpha is kept at one
    /// so the same GPU painting/filtering path can edit masks without introducing coverage holes.
    /// </summary>
    public sealed class TexturePaintLayerMask : IDisposable
    {
        [Range(0f, 1f)] public float baseValue = 1f;
        public EditableTextureTarget target;
        public TexturePaintLayerMaskEffects effects = new TexturePaintLayerMaskEffects();
        public TexturePaintChannelSourceSettings sourceSettings = DefaultSourceSettings();
        public TexturePaintChannel sourceChannel = TexturePaintChannel.Albedo;

        public static TexturePaintChannelSourceSettings DefaultSourceSettings()
        {
            return new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Color,
                color = Color.white
            };
        }

        public float PaintValue => Mathf.Clamp01(sourceSettings?.color.grayscale ??
            (baseValue < 0.5f ? 1f : 0f));

        public void SetPaintValue(float value)
        {
            value = Mathf.Clamp01(value);
            sourceSettings = DefaultSourceSettings();
            sourceSettings.color = new Color(value, value, value, 1f);
            sourceChannel = TexturePaintChannel.Albedo;
        }

        public void NormalizePaintSource()
        {
            float value = PaintValue;
            Color grayscale = new Color(value, value, value, 1f);
            if (sourceSettings != null && sourceSettings.source == TexturePaintBrushSource.Color &&
                sourceSettings.sourceTexture == null && sourceSettings.sourceSprite == null &&
                sourceSettings.sourceOverlay == null && !sourceSettings.invert &&
                sourceChannel == TexturePaintChannel.Albedo && sourceSettings.color == grayscale)
                return;
            SetPaintValue(value);
        }

        public void Dispose()
        {
            target?.Dispose();
            target = null;
        }
    }

    public sealed class TexturePaintLayer : IDisposable
    {
        public string id = Guid.NewGuid().ToString("N");
        public string logicalLayerId;
        public string paintTargetId;
        public string parentId;
        public string name;
        public TexturePaintLayerKind kind = TexturePaintLayerKind.Paint;
        public bool visible = true;
        public float opacity = 1f;
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
        public readonly Dictionary<TexturePaintChannel, EditableTextureTarget> channels = new Dictionary<TexturePaintChannel, EditableTextureTarget>();
        public readonly Dictionary<TexturePaintChannel, TexturePaintLayerChannelSettings> channelSettings =
            new Dictionary<TexturePaintChannel, TexturePaintLayerChannelSettings>();
        public TexturePaintLayerMask layerMask;
        public readonly List<TexturePaintStrokeRecord> strokes = new List<TexturePaintStrokeRecord>();

        // Layer kind is the authoritative discriminator. Unity's inline serialization can
        // materialize an empty serializable spline object for a null field after domain reload;
        // payload presence must therefore never decide the editing mode.
        public bool IsSplineLayer => kind == TexturePaintLayerKind.Spline;

        public void NormalizeKindPayload()
        {
            effects ??= new TexturePaintLayerEffects();
            effects.Normalize();
            layerMask?.NormalizePaintSource();
            if (kind == TexturePaintLayerKind.Fill)
            {
                fillSettings ??= new TexturePaintFillSettings
                {
                    source = TexturePaintBrushSource.Color,
                    color = fillColor
                };
                fillSettings.Normalize();
                fillColor = fillSettings.color;
            }
            if (IsSplineLayer)
            {
                spline ??= new TexturePaintSpline { name = name, worldSpace = false };
                splineSettings ??= new TexturePaintSplineSettings();
                return;
            }
            spline = null;
            splineSettings = null;
            if (string.Equals(proceduralGroupKey, "texture-paint-spline:" + id, StringComparison.Ordinal))
                proceduralGroupKey = null;
        }

        public TexturePaintLayerChannelSettings GetChannelSettings(TexturePaintChannel channel, bool create = true)
        {
            if (channelSettings.TryGetValue(channel, out TexturePaintLayerChannelSettings result)) return result;
            if (!create) return null;
            result = new TexturePaintLayerChannelSettings
            {
                channel = channel,
                opacity = 1f,
                blendMode = blendMode
            };
            channelSettings[channel] = result;
            return result;
        }

        public bool TryGetFirstAuthoredChannel(out TexturePaintChannel channel)
        {
            foreach (TexturePaintChannel candidate in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (!channels.ContainsKey(candidate)) continue;
                channel = candidate;
                return true;
            }
            channel = TexturePaintChannel.Albedo;
            return false;
        }

        public void Dispose()
        {
            layerMask?.Dispose();
            layerMask = null;
            foreach (EditableTextureTarget target in channels.Values) target.Dispose();
            channels.Clear(); channelSettings.Clear(); strokes.Clear();
        }
    }

    /// <summary>
    /// Rasterizes a Fill layer once into its ordinary editable layer texture. The generated
    /// pixels are then cached, composited, saved, and restored exactly like painted pixels.
    /// </summary>
    public sealed class TexturePaintFillGenerator : IDisposable
    {
        public const int CurrentRevision = 6;
        private const int EdgePaddingPixels = 2;

        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        public bool IsAvailable => material != null;

        public TexturePaintFillGenerator(Shader shader)
        {
            shader ??= Shader.Find("Hidden/UMA/TexturePaint/FillGenerator");
            if (shader == null) return;
            material = new Material(shader)
            {
                name = "Texture Paint Fill Generator",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        public bool Render(TextureSet set, TexturePaintLayer layer, EditableTextureTarget target, Texture source,
            TexturePaintFillSettings settingsOverride = null)
        {
            if (material == null || set?.surface?.mesh == null ||
                (settingsOverride == null && layer?.fillSettings == null) ||
                target?.Front == null || source == null) return false;

            TexturePaintFillSettings settings = settingsOverride ?? layer.fillSettings;
            settings.Normalize();
            properties.Clear();
            properties.SetTexture("_FillSource", source);
            properties.SetColor("_FillColor", settings.color);
            properties.SetInt("_SourceKind", settings.source == TexturePaintBrushSource.Color ? 1 : 0);
            properties.SetInt("_Projection", (int)settings.projection);
            properties.SetVector("_Tiling", new Vector4(settings.tiling.x, settings.tiling.y, 0f, 0f));
            properties.SetVector("_Offset", new Vector4(settings.offset.x, settings.offset.y, 0f, 0f));
            properties.SetFloat("_Rotation", settings.rotation);
            properties.SetInt("_TriplanarBlend", (int)settings.triplanarBlend);
            properties.SetFloat("_BlendOffset", settings.blendOffset);
            properties.SetFloat("_BlendSharpness", settings.blendSharpness);

            Matrix4x4 localToWorld = set.surface.gameObject != null
                ? set.surface.gameObject.transform.localToWorldMatrix
                : Matrix4x4.identity;
            using (CommandBuffer command = new CommandBuffer { name = "Generate Texture Paint Fill" })
            {
                command.SetRenderTarget(target.Front);
                command.ClearRenderTarget(false, true, Color.clear);
                command.DrawMesh(set.surface.mesh, localToWorld, material, 0, 0, properties);
                Graphics.ExecuteCommandBuffer(command);
            }

            // The preview material samples the final composite bilinearly. Without a small gutter,
            // samples on a UV seam blend the generated fill with untouched base texels immediately
            // outside the island, which appears as a bright hairline in 3D. Grow the generated RGBA
            // coverage into transparent texels before compositing; these pixels are only padding and
            // do not alter the UV-space content inside an island.
            for (int pass = 0; pass < EdgePaddingPixels; pass++)
            {
                // Bind this explicitly instead of relying on Graphics.Blit's implicit _MainTex
                // convention. FillGenerator originally had no serialized _MainTex property, so
                // some backends sampled Unity's default white texture and replaced the entire fill.
                material.SetTexture("_MainTex", target.Front);
                Graphics.Blit(target.Front, target.Back, material, 1);
                target.Swap();
            }
            target.CopyFrontToBack();
            return true;
        }

        public void Dispose()
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
    }

    public sealed class TextureSet : IDisposable
    {
        public string Name => surface?.sourceMaterial != null ? surface.sourceMaterial.name : "Material";
        public ReconstructedSurface surface;
        public string persistentId;
        public Material previewMaterial;
        public UMAMaterial umaMaterial;
#if UNITY_EDITOR
        public TexturePaintMaterialCapabilityDescriptor materialCapability;
#endif
        public readonly Dictionary<TexturePaintChannel, TextureChannelTarget> channels = new Dictionary<TexturePaintChannel, TextureChannelTarget>();
        public readonly Dictionary<string, TexturePhysicalChannelGroup> physicalChannelGroups =
            new Dictionary<string, TexturePhysicalChannelGroup>(StringComparer.Ordinal);
        public readonly List<TextureSourceBinding> sources = new List<TextureSourceBinding>();
        public readonly List<TexturePaintLayer> layers = new List<TexturePaintLayer>();
        public readonly List<TexturePaintStrokeRecord> baseStrokes = new List<TexturePaintStrokeRecord>();
        public TangentSpaceMaps tangentSpaceMaps;
        public ProceduralMeshMaps proceduralMeshMaps;
        public int activeLayerIndex = -1;
        internal TextureLayerCompositor compositor;
        internal ComputeShader channelPackShader;
        internal TexturePaintFillGenerator fillGenerator;
        private RenderTexture selectedGroupPreview;
        private string selectedGroupPreviewId;
        private TexturePaintChannel selectedGroupPreviewChannel;
        private long compositeRevision;
        private long selectedGroupPreviewRevision = -1;
        public bool LayerEffectsAvailable => compositor?.EffectsAvailable == true;

        public TextureChannelTarget GetChannel(TexturePaintChannel channel)
        {
            channels.TryGetValue(channel, out TextureChannelTarget result);
            return result;
        }

        public EditableTextureTarget GetPaintTarget(TexturePaintChannel channel, TexturePaintSourceMode mode)
        {
            TextureChannelTarget baseChannel = GetChannel(channel);
            if (baseChannel == null) return null;
            if (mode == TexturePaintSourceMode.SourceTexture) return baseChannel.editable;
            TexturePaintLayer layer = EnsureActiveLayer();
            if (!layer.channels.TryGetValue(channel, out EditableTextureTarget target))
            {
                target = new EditableTextureTarget(layer.name + " " + channel, baseChannel.Texture.width, baseChannel.Texture.height,
                    baseChannel.format, null, Color.clear);
                layer.channels.Add(channel, target);
                layer.GetChannelSettings(channel);
            }
            return target;
        }

        public TexturePaintLayerMask AddLayerMask(TexturePaintLayer layer, float baseValue)
        {
            if (layer == null || !layers.Contains(layer)) return null;
            baseValue = Mathf.Clamp01(baseValue);
            if (layer.layerMask != null)
            {
                layer.layerMask.baseValue = baseValue;
                layer.layerMask.effects ??= new TexturePaintLayerMaskEffects();
                layer.layerMask.SetPaintValue(baseValue < 0.5f ? 1f : 0f);
                layer.layerMask.target?.Reset(null, MaskColor(baseValue));
                return layer.layerMask;
            }
            GetMaskResolution(out int width, out int height);
            if (width <= 0 || height <= 0) return null;
            TexturePaintChannelSourceSettings paintSource = TexturePaintLayerMask.DefaultSourceSettings();
            float initialPaintValue = baseValue < 0.5f ? 1f : 0f;
            paintSource.color = MaskColor(initialPaintValue);
            layer.layerMask = new TexturePaintLayerMask
            {
                baseValue = baseValue,
                effects = new TexturePaintLayerMaskEffects(),
                sourceSettings = paintSource,
                target = new EditableTextureTarget(layer.name + " Layer Mask", width, height,
                    RenderTextureFormat.ARGB32, null, MaskColor(baseValue))
            };
            return layer.layerMask;
        }

        public bool RemoveLayerMask(TexturePaintLayer layer)
        {
            if (layer?.layerMask == null) return false;
            layer.layerMask.Dispose();
            layer.layerMask = null;
            return true;
        }

        public void GetMaskResolution(out int width, out int height)
        {
            width = 0;
            height = 0;
            foreach (TextureChannelTarget channel in channels.Values)
            {
                if (channel?.editable?.Front == null) continue;
                width = Mathf.Max(width, channel.editable.Width);
                height = Mathf.Max(height, channel.editable.Height);
            }
        }

        internal static Color MaskColor(float value)
        {
            value = Mathf.Clamp01(value);
            return new Color(value, value, value, 1f);
        }

        public ProceduralMeshMaps GetProceduralMeshMaps(int maximumResolution = 512,
            TexturePaintOperationContext operation = default)
        {
            if (proceduralMeshMaps != null) return proceduralMeshMaps;
            int resolution = Mathf.Clamp(maximumResolution, 16, 1024);
            foreach (TextureChannelTarget channel in channels.Values)
            {
                if (channel?.Texture == null) continue;
                resolution = Mathf.Min(resolution, Mathf.Min(channel.Texture.width, channel.Texture.height));
                break;
            }
            operation.ThrowIfCancellationRequested();
            proceduralMeshMaps = ProceduralMeshMapBuilder.Build(surface, resolution, resolution, operation);
            return proceduralMeshMaps;
        }

        public TexturePaintLayer AddLayer(string layerName)
            => AddLayer(layerName, true);

        private TexturePaintLayer AddLayer(string layerName, bool inheritActiveGroup)
        {
            TexturePaintLayer parent = inheritActiveGroup && (uint)activeLayerIndex < (uint)layers.Count &&
                layers[activeLayerIndex].kind == TexturePaintLayerKind.Group
                    ? layers[activeLayerIndex]
                    : null;
            TexturePaintLayer layer = new TexturePaintLayer
            {
                name = string.IsNullOrWhiteSpace(layerName) ? $"Layer {layers.Count + 1}" : layerName,
                parentId = parent?.id,
                visible = true
            };
            if (parent != null)
            {
                // The layer list is composited bottom-to-top but displayed top-to-bottom. Insert
                // immediately before the folder so a newly created child is visibly nested below
                // it instead of appearing above its parent.
                int parentIndex = layers.IndexOf(parent);
                layers.Insert(Mathf.Max(0, parentIndex), layer);
                activeLayerIndex = layers.IndexOf(layer);
            }
            else
            {
                layers.Add(layer);
                activeLayerIndex = layers.Count - 1;
            }
            return layer;
        }

        public TexturePaintLayer AddFillLayer(string layerName, TexturePaintChannel channel, Color color)
            => AddFillLayer(layerName, channel, new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Color,
                color = color
            });

        public TexturePaintLayer AddFillLayer(string layerName, TexturePaintChannel channel,
            TexturePaintFillSettings settings)
        {
            TextureChannelTarget baseChannel = GetChannel(channel);
            TexturePaintFillSettings normalized = settings?.Clone() ?? new TexturePaintFillSettings();
            normalized.Normalize();
            if (baseChannel == null || !CanGenerateFill(channel, normalized)) return null;
            TexturePaintLayer layer = AddLayer(layerName);
            layer.kind = TexturePaintLayerKind.Fill;
            layer.fillChannel = channel;
            layer.fillSettings = normalized;
            layer.fillColor = normalized.color;
            layer.channels[channel] = new EditableTextureTarget(layer.name + " " + channel,
                baseChannel.Texture.width, baseChannel.Texture.height, baseChannel.format, null, Color.clear);
            layer.GetChannelSettings(channel).sourceSettings = ChannelSourceFromFill(normalized);
            if (!RegenerateFillLayer(layer))
            {
                layers.Remove(layer);
                layer.Dispose();
                activeLayerIndex = layers.Count - 1;
                return null;
            }
            BindPreviewTextures();
            return layer;
        }

        public bool UpdateFillLayer(TexturePaintLayer layer, TexturePaintChannel channel, Color color)
            => UpdateFillLayer(layer, channel, new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Color,
                color = color
            });

        public bool UpdateFillLayer(TexturePaintLayer layer, TexturePaintChannel channel,
            TexturePaintFillSettings settings)
        {
            if (layer == null || layer.kind != TexturePaintLayerKind.Fill) return false;
            TextureChannelTarget baseChannel = GetChannel(channel);
            TexturePaintFillSettings normalized = settings?.Clone() ?? new TexturePaintFillSettings();
            normalized.Normalize();
            if (baseChannel == null || !CanGenerateFill(channel, normalized)) return false;
            layer.fillChannel = channel;
            layer.fillSettings = normalized;
            layer.fillColor = normalized.color;
            if (!layer.channels.TryGetValue(channel, out EditableTextureTarget target))
            {
                target = new EditableTextureTarget(layer.name + " " + channel, baseChannel.Texture.width,
                    baseChannel.Texture.height, baseChannel.format, null, Color.clear);
                layer.channels[channel] = target;
            }
            layer.GetChannelSettings(channel).sourceSettings = ChannelSourceFromFill(normalized);
            if (!RegenerateFillLayer(layer)) return false;
            BindPreviewTextures();
            return true;
        }

        public bool RegenerateFillLayer(TexturePaintLayer layer)
        {
            if (layer == null || layer.kind != TexturePaintLayerKind.Fill || layer.channels.Count == 0)
                return false;
            layer.NormalizeKindPayload();
            SynchronizeFillChannelTransforms(layer);
            bool generatedAny = false;
            foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in layer.channels)
            {
                TexturePaintLayerChannelSettings channelSettings = layer.GetChannelSettings(pair.Key);
                TexturePaintFillSettings settings = channelSettings.sourceSettings != null
                    ? FillFromChannelSource(channelSettings.sourceSettings)
                    : pair.Key == layer.fillChannel ? layer.fillSettings : null;
                if (settings == null) continue;
                settings.Normalize();
                if (settings.source == TexturePaintBrushSource.Color)
                {
                    pair.Value.Reset(null, settings.color);
                    settings.generatorRevision = TexturePaintFillGenerator.CurrentRevision;
                    generatedAny = true;
                    continue;
                }
                Texture source = ResolveFillSource(settings, pair.Key);
                if (source == null || fillGenerator == null ||
                    !fillGenerator.Render(this, layer, pair.Value, source, settings)) return false;
                settings.generatorRevision = TexturePaintFillGenerator.CurrentRevision;
                generatedAny = true;
            }
            if (generatedAny && layer.fillSettings != null)
                layer.fillSettings.generatorRevision = TexturePaintFillGenerator.CurrentRevision;
            return generatedAny;
        }

        public static void SynchronizeFillChannelTransforms(TexturePaintLayer layer)
        {
            if (layer?.kind != TexturePaintLayerKind.Fill ||
                layer.fillSettings?.useFirstChannelTransform != true ||
                !layer.TryGetFirstAuthoredChannel(out TexturePaintChannel masterChannel)) return;

            TexturePaintChannelSourceSettings master =
                layer.GetChannelSettings(masterChannel, false)?.sourceSettings;
            if (master == null && masterChannel == layer.fillChannel)
                master = ChannelSourceFromFill(layer.fillSettings);
            if (master == null) return;

            foreach (TexturePaintChannel channel in layer.channels.Keys)
            {
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel);
                if (settings.sourceSettings == null) continue;
                settings.sourceSettings.tiling = master.tiling;
                settings.sourceSettings.offset = master.offset;
                settings.sourceSettings.rotation = master.rotation;
            }

            int revision = layer.fillSettings.generatorRevision;
            layer.fillSettings = FillFromChannelSource(master);
            layer.fillSettings.useFirstChannelTransform = true;
            layer.fillSettings.generatorRevision = revision;
            layer.fillChannel = masterChannel;
            layer.fillColor = master.color;
        }

        private static TexturePaintChannelSourceSettings ChannelSourceFromFill(
            TexturePaintFillSettings settings)
        {
            return new TexturePaintChannelSourceSettings
            {
                source = settings.source,
                sourceTexture = settings.sourceTexture,
                sourceSprite = settings.sourceSprite,
                sourceOverlay = settings.sourceOverlay,
                color = settings.color,
                normalConvention = settings.normalConvention,
                invert = settings.invert,
                tiling = settings.tiling,
                offset = settings.offset,
                rotation = settings.rotation,
                projection = settings.projection,
                triplanarBlend = settings.triplanarBlend,
                blendOffset = settings.blendOffset,
                blendSharpness = settings.blendSharpness
            };
        }

        private static TexturePaintFillSettings FillFromChannelSource(
            TexturePaintChannelSourceSettings source)
        {
            return new TexturePaintFillSettings
            {
                source = source.source,
                sourceTexture = source.sourceTexture,
                sourceSprite = source.sourceSprite,
                sourceOverlay = source.sourceOverlay,
                color = source.color,
                normalConvention = source.normalConvention,
                invert = source.invert,
                tiling = source.tiling,
                offset = source.offset,
                rotation = source.rotation,
                projection = source.projection,
                triplanarBlend = source.triplanarBlend,
                blendOffset = source.blendOffset,
                blendSharpness = source.blendSharpness
            };
        }

        internal Texture ResolveFillSource(TexturePaintFillSettings settings, TexturePaintChannel channel)
        {
            if (settings == null) return null;
            if (settings.source == TexturePaintBrushSource.Texture)
                return TexturePaintSpriteSource.Resolve(settings.sourceTexture, settings.sourceSprite,
                    channel, settings.normalConvention, settings.invert);
            if (settings.source != TexturePaintBrushSource.Overlay || settings.sourceOverlay == null) return null;
            for (int i = 0; i < sources.Count; i++)
            {
                TextureSourceBinding binding = sources[i];
                if (binding?.overlay?.asset != settings.sourceOverlay) continue;
                if (binding.textures.TryGetValue(channel, out Texture texture))
                    return TexturePaintSpriteSource.ResolveTexture(texture, channel,
                        settings.normalConvention, settings.invert);
            }
            return null;
        }

        private bool CanGenerateFill(TexturePaintChannel channel, TexturePaintFillSettings settings)
        {
            return settings.source == TexturePaintBrushSource.Color ||
                (fillGenerator?.IsAvailable == true && ResolveFillSource(settings, channel) != null);
        }

        public TexturePaintLayer AddGroup(string layerName)
        {
            TexturePaintLayer group = AddLayer(layerName, true);
            group.kind = TexturePaintLayerKind.Group;
            return group;
        }

        public TexturePaintLayer AddSplineLayer(string layerName = null)
        {
            TexturePaintLayer layer = AddLayer(string.IsNullOrWhiteSpace(layerName) ? $"Spline Layer {layers.Count + 1}" : layerName);
            layer.kind = TexturePaintLayerKind.Spline;
            layer.spline = new TexturePaintSpline { name = layer.name, worldSpace = false };
            layer.splineSettings = new TexturePaintSplineSettings();
            return layer;
        }

        public bool MoveLayer(int fromIndex, int toIndex)
        {
            if ((uint)fromIndex >= (uint)layers.Count || (uint)toIndex >= (uint)layers.Count || fromIndex == toIndex)
                return false;
            NormalizeLayerHierarchy();
            TexturePaintLayer activeLayer = (uint)activeLayerIndex < (uint)layers.Count ? layers[activeLayerIndex] : null;
            TexturePaintLayer movedLayer = layers[fromIndex];
            List<TexturePaintLayer> block = GetSubtree(movedLayer);
            if (block.Count > 1)
            {
                if (block.Contains(layers[toIndex])) return false;
                for (int i = 0; i < block.Count; i++) layers.Remove(block[i]);
                int insertion = Mathf.Clamp(toIndex - block.Count + 1, 0, layers.Count);
                layers.InsertRange(insertion, block);
            }
            else
            {
                layers.RemoveAt(fromIndex);
                layers.Insert(Mathf.Clamp(toIndex, 0, layers.Count), movedLayer);
            }
            NormalizeLayerHierarchy();
            activeLayerIndex = activeLayer != null ? layers.IndexOf(activeLayer) : -1;
            BindPreviewTextures();
            return true;
        }

        /// <summary>
        /// Restores the layer-list invariant used by both the UI and compositor: every group's
        /// children occupy one contiguous block immediately before the group in bottom-to-top
        /// storage order. Root layers found inside a saved child block are moved above the group.
        /// </summary>
        public bool NormalizeLayerHierarchy()
        {
            if (layers.Count == 0) return false;
            TexturePaintLayer activeLayer = (uint)activeLayerIndex < (uint)layers.Count
                ? layers[activeLayerIndex] : null;
            var groupsById = new Dictionary<string, TexturePaintLayer>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                TexturePaintLayer candidate = layers[i];
                if (candidate?.kind == TexturePaintLayerKind.Group && !string.IsNullOrEmpty(candidate.id))
                    groupsById[candidate.id] = candidate;
            }

            bool changed = false;
            var childrenByGroup = new Dictionary<TexturePaintLayer, List<TexturePaintLayer>>();
            for (int i = 0; i < layers.Count; i++)
            {
                TexturePaintLayer candidate = layers[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.parentId)) continue;
                if (string.Equals(candidate.id, candidate.parentId, StringComparison.Ordinal) ||
                    (groupsById.TryGetValue(candidate.parentId, out TexturePaintLayer knownParent) &&
                     WouldCreateHierarchyCycle(candidate, knownParent, groupsById)))
                {
                    candidate.parentId = null;
                    changed = true;
                    continue;
                }
                // A document restore can materialize children before their group. Keep an
                // unresolved id intact until the final bind instead of silently ungrouping an
                // otherwise valid child midway through reconstruction.
                if (!groupsById.TryGetValue(candidate.parentId, out TexturePaintLayer parent))
                    continue;
                if (!childrenByGroup.TryGetValue(parent, out List<TexturePaintLayer> children))
                {
                    children = new List<TexturePaintLayer>();
                    childrenByGroup.Add(parent, children);
                }
                children.Add(candidate);
            }

            var ordered = new List<TexturePaintLayer>(layers.Count);
            var emitted = new HashSet<TexturePaintLayer>();
            for (int i = 0; i < layers.Count; i++)
            {
                TexturePaintLayer candidate = layers[i];
                if (candidate == null || emitted.Contains(candidate)) continue;
                if (!string.IsNullOrEmpty(candidate.parentId) &&
                    groupsById.TryGetValue(candidate.parentId, out TexturePaintLayer parent))
                {
                    TexturePaintLayer root = parent;
                    int guard = 0;
                    while (!string.IsNullOrEmpty(root.parentId) && guard++ < layers.Count &&
                        groupsById.TryGetValue(root.parentId, out TexturePaintLayer ancestor))
                        root = ancestor;
                    EmitHierarchy(root, childrenByGroup, emitted, ordered);
                }
                else EmitHierarchy(candidate, childrenByGroup, emitted, ordered);
            }
            for (int i = 0; i < layers.Count; i++)
                EmitHierarchy(layers[i], childrenByGroup, emitted, ordered);

            if (!changed)
                for (int i = 0; i < layers.Count; i++)
                    if (!ReferenceEquals(layers[i], ordered[i])) { changed = true; break; }
            if (!changed) return false;
            layers.Clear();
            layers.AddRange(ordered);
            activeLayerIndex = activeLayer != null ? layers.IndexOf(activeLayer) : -1;
            return true;
        }

        private static bool WouldCreateHierarchyCycle(TexturePaintLayer child,
            TexturePaintLayer parent, Dictionary<string, TexturePaintLayer> groupsById)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { child.id };
            TexturePaintLayer cursor = parent;
            while (cursor != null)
            {
                if (string.IsNullOrEmpty(cursor.id) || !visited.Add(cursor.id)) return true;
                if (string.IsNullOrEmpty(cursor.parentId) ||
                    !groupsById.TryGetValue(cursor.parentId, out cursor)) return false;
            }
            return false;
        }

        private static void EmitHierarchy(TexturePaintLayer layer,
            Dictionary<TexturePaintLayer, List<TexturePaintLayer>> childrenByGroup,
            HashSet<TexturePaintLayer> emitted, List<TexturePaintLayer> ordered)
        {
            if (layer == null || emitted.Contains(layer)) return;
            if (childrenByGroup.TryGetValue(layer, out List<TexturePaintLayer> children))
                for (int i = 0; i < children.Count; i++)
                    EmitHierarchy(children[i], childrenByGroup, emitted, ordered);
            if (emitted.Add(layer)) ordered.Add(layer);
        }

        private List<TexturePaintLayer> GetSubtree(TexturePaintLayer root)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (root?.kind == TexturePaintLayerKind.Group && !string.IsNullOrEmpty(root.id))
                ids.Add(root.id);
            bool expanded;
            do
            {
                expanded = false;
                for (int i = 0; i < layers.Count; i++)
                {
                    TexturePaintLayer candidate = layers[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.parentId) ||
                        !ids.Contains(candidate.parentId) || string.IsNullOrEmpty(candidate.id) ||
                        !ids.Add(candidate.id)) continue;
                    expanded = true;
                }
            } while (expanded);
            var result = new List<TexturePaintLayer>();
            for (int i = 0; i < layers.Count; i++)
                if (ReferenceEquals(layers[i], root) || ids.Contains(layers[i]?.parentId ?? string.Empty) ||
                    (!string.IsNullOrEmpty(layers[i]?.id) && ids.Contains(layers[i].id)))
                    result.Add(layers[i]);
            return result;
        }

        public bool RemoveLayerAt(int layerIndex)
        {
            if ((uint)layerIndex >= (uint)layers.Count) return false;
            TexturePaintLayer removed = layers[layerIndex];
            List<TexturePaintLayer> removedLayers = GetSubtree(removed);
            for (int i = 0; i < removedLayers.Count; i++) layers.Remove(removedLayers[i]);
            for (int i = 0; i < removedLayers.Count; i++) removedLayers[i].Dispose();
            if (layers.Count == 0) activeLayerIndex = -1;
            else activeLayerIndex = Mathf.Clamp(layerIndex - removedLayers.Count + 1, 0, layers.Count - 1);
            BindPreviewTextures();
            return true;
        }

        public TexturePaintLayer DuplicateLayerAt(int layerIndex)
        {
            if ((uint)layerIndex >= (uint)layers.Count) return null;
            TexturePaintLayer source = layers[layerIndex];
            List<TexturePaintLayer> sourceBlock = GetSubtree(source);
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var copies = new List<TexturePaintLayer>(sourceBlock.Count);
            for (int i = 0; i < sourceBlock.Count; i++)
            {
                TexturePaintLayer original = sourceBlock[i];
                TexturePaintLayer copy = CloneLayer(original,
                    ReferenceEquals(original, source) ? source.name + " Copy" : original.name, false);
                if (!string.IsNullOrEmpty(original.id)) idMap[original.id] = copy.id;
                copies.Add(copy);
            }
            for (int i = 0; i < copies.Count; i++)
                if (!string.IsNullOrEmpty(copies[i].parentId) &&
                    idMap.TryGetValue(copies[i].parentId, out string copyParent))
                    copies[i].parentId = copyParent;
            int insertionIndex = layers.IndexOf(source) + 1;
            layers.InsertRange(insertionIndex, copies);
            TexturePaintLayer rootCopy = copies[sourceBlock.IndexOf(source)];
            activeLayerIndex = layers.IndexOf(rootCopy);
            NormalizeLayerHierarchy();
            BindPreviewTextures();
            return rootCopy;
        }

        /// <summary>
        /// Creates a GPU-side copy of a layer without inserting it into the stack. This is used by
        /// the editor's lightweight history so destructive operations never serialize texture data
        /// through Unity's object undo system.
        /// </summary>
        public TexturePaintLayer CloneLayer(TexturePaintLayer source, string copyName = null,
            bool preserveIdentity = false)
        {
            if (source == null) return null;
            TexturePaintLayer copy = new TexturePaintLayer
            {
                id = preserveIdentity ? source.id : Guid.NewGuid().ToString("N"),
                logicalLayerId = preserveIdentity ? source.logicalLayerId : null,
                paintTargetId = preserveIdentity ? source.paintTargetId : null,
                parentId = source.parentId,
                name = string.IsNullOrWhiteSpace(copyName) ? source.name : copyName,
                kind = source.kind,
                visible = source.visible,
                opacity = source.opacity,
                blendMode = source.blendMode,
                effects = source.effects?.Clone() ?? new TexturePaintLayerEffects(),
                fillChannel = source.fillChannel,
                fillColor = source.fillColor,
                fillSettings = source.fillSettings?.Clone(),
                paintSettings = source.paintSettings?.Clone(),
                spline = source.IsSplineLayer && source.spline != null
                    ? JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(source.spline)) : null,
                splineSettings = source.IsSplineLayer ? source.splineSettings?.Clone() : null,
                pluginId = source.pluginId,
                pluginVersion = source.pluginVersion,
                pluginParametersJson = source.pluginParametersJson,
                // A duplicate is independent. Sharing the procedural ownership key lets deleting
                // either copy delete the other copy's generated peers.
                proceduralGroupKey = preserveIdentity ? source.proceduralGroupKey : null
            };
            copy.NormalizeKindPayload();
            foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in source.channels)
            {
                TextureChannelTarget baseChannel = GetChannel(pair.Key);
                if (baseChannel == null) continue;
                copy.channels[pair.Key] = new EditableTextureTarget(copy.name + " " + pair.Key,
                    pair.Value.Width, pair.Value.Height, baseChannel.format, pair.Value.Front, Color.clear);
                TexturePaintLayerChannelSettings settings = source.GetChannelSettings(pair.Key, false);
                if (settings != null) copy.channelSettings[pair.Key] = settings.Clone();
            }
            if (source.layerMask?.target?.Front != null)
            {
                copy.layerMask = new TexturePaintLayerMask
                {
                    baseValue = source.layerMask.baseValue,
                    effects = source.layerMask.effects?.Clone() ?? new TexturePaintLayerMaskEffects(),
                    sourceSettings = source.layerMask.sourceSettings?.Clone() ??
                        TexturePaintLayerMask.DefaultSourceSettings(),
                    sourceChannel = source.layerMask.sourceChannel,
                    target = new EditableTextureTarget(copy.name + " Layer Mask",
                        source.layerMask.target.Width, source.layerMask.target.Height,
                        RenderTextureFormat.ARGB32, source.layerMask.target.Front,
                        MaskColor(source.layerMask.baseValue))
                };
                copy.layerMask.NormalizePaintSource();
            }
            for (int i = 0; i < source.strokes.Count; i++)
                copy.strokes.Add(JsonUtility.FromJson<TexturePaintStrokeRecord>(JsonUtility.ToJson(source.strokes[i])));
            return copy;
        }

        /// <summary>
        /// Builds the result of Merge Down without changing the live layer list. The caller can
        /// atomically swap this result with the two source layers for lightweight undo/redo.
        /// </summary>
        public TexturePaintLayer CreateMergedLayer(int upperLayerIndex)
        {
            if (!CanMergeLayerDown(upperLayerIndex, out _)) return null;
            TexturePaintLayer upper = layers[upperLayerIndex];
            TexturePaintLayer lower = layers[upperLayerIndex - 1];
            if (upper.kind == TexturePaintLayerKind.Group || lower.kind == TexturePaintLayerKind.Group) return null;
            TexturePaintLayer merged = new TexturePaintLayer
            {
                id = lower.id,
                logicalLayerId = lower.logicalLayerId,
                paintTargetId = lower.paintTargetId,
                parentId = lower.parentId,
                name = lower.name + " + " + upper.name,
                kind = TexturePaintLayerKind.Paint,
                visible = true,
                opacity = 1f,
                blendMode = TexturePaintBlendMode.Normal,
                effects = new TexturePaintLayerEffects()
            };
            HashSet<TexturePaintChannel> mergedChannels = new HashSet<TexturePaintChannel>(lower.channels.Keys);
            mergedChannels.UnionWith(upper.channels.Keys);
            foreach (TexturePaintChannel channel in mergedChannels)
            {
                TextureChannelTarget baseChannel = GetChannel(channel);
                if (baseChannel == null) continue;
                EditableTextureTarget mergedTarget = new EditableTextureTarget(merged.name + " " + channel,
                    baseChannel.editable.Width, baseChannel.editable.Height, baseChannel.format, null, Color.clear);
                merged.channels[channel] = mergedTarget;
                if (lower.visible && lower.channels.TryGetValue(channel, out EditableTextureTarget lowerTarget))
                {
                    TexturePaintLayerChannelSettings lowerSettings = lower.GetChannelSettings(channel, false);
                    if (lowerSettings == null || lowerSettings.enabled)
                    {
                        float opacity = lower.opacity * (lowerSettings != null ? lowerSettings.opacity : 1f);
                        TexturePaintBlendMode blend = lowerSettings != null ? lowerSettings.blendMode : lower.blendMode;
                        if (!compositor.CompositeLayerInto(mergedTarget.Front, this, lower, lowerTarget,
                            channel, opacity, blend)) { merged.Dispose(); return null; }
                    }
                }
                if (upper.visible && upper.channels.TryGetValue(channel, out EditableTextureTarget upperTarget))
                {
                    TexturePaintLayerChannelSettings upperSettings = upper.GetChannelSettings(channel, false);
                    if (upperSettings == null || upperSettings.enabled)
                    {
                        float opacity = upper.opacity * (upperSettings != null ? upperSettings.opacity : 1f);
                        TexturePaintBlendMode blend = upperSettings != null ? upperSettings.blendMode : upper.blendMode;
                        if (!compositor.CompositeLayerInto(mergedTarget.Front, this, upper, upperTarget,
                            channel, opacity, blend)) { merged.Dispose(); return null; }
                    }
                }
                if (!compositor.UnassociateAlpha(mergedTarget.Front))
                { merged.Dispose(); return null; }
                mergedTarget.CopyFrontToBack();
                merged.channelSettings[channel] = new TexturePaintLayerChannelSettings
                {
                    channel = channel,
                    enabled = true,
                    opacity = 1f,
                    blendMode = TexturePaintBlendMode.Normal
                };
            }
            merged.opacity = 1f;
            merged.blendMode = TexturePaintBlendMode.Normal;
            return merged;
        }

        public bool CanMergeLayerDown(int upperLayerIndex, out string reason)
        {
            reason = null;
            if (upperLayerIndex <= 0 || upperLayerIndex >= layers.Count || compositor == null)
            { reason = "There is no adjacent layer below to merge."; return false; }
            TexturePaintLayer upper = layers[upperLayerIndex];
            TexturePaintLayer lower = layers[upperLayerIndex - 1];
            if (upper?.kind == TexturePaintLayerKind.Group || lower?.kind == TexturePaintLayerKind.Group)
            { reason = "Groups cannot be merged down."; return false; }
            if (!string.Equals(upper?.parentId, lower?.parentId, StringComparison.Ordinal))
            { reason = "Layers must be siblings in the same group to merge."; return false; }
            if (!UsesOnlyNormalBlend(upper) || !UsesOnlyNormalBlend(lower))
            {
                reason = "Merge Down is only exact for Normal layer/channel blends. " +
                    "Change both layers to Normal before merging.";
                return false;
            }
            return true;
        }

        private static bool UsesOnlyNormalBlend(TexturePaintLayer layer)
        {
            if (layer == null || layer.blendMode != TexturePaintBlendMode.Normal) return false;
            foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> channel in layer.channels)
            {
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel.Key, false);
                if (settings != null && settings.blendMode != TexturePaintBlendMode.Normal) return false;
            }
            return true;
        }

        public bool MergeLayerDown(int layerIndex)
        {
            if (layerIndex <= 0 || layerIndex >= layers.Count || compositor == null) return false;
            TexturePaintLayer upper = layers[layerIndex];
            TexturePaintLayer lower = layers[layerIndex - 1];
            if (upper.kind == TexturePaintLayerKind.Group || lower.kind == TexturePaintLayerKind.Group) return false;
            TexturePaintLayer merged = CreateMergedLayer(layerIndex);
            if (merged == null) return false;
            layers[layerIndex - 1] = merged;
            layers.RemoveAt(layerIndex);
            lower.Dispose();
            upper.Dispose();
            activeLayerIndex = layerIndex - 1;
            BindPreviewTextures();
            return true;
        }

        public TexturePaintLayer AddLayerFromSource(TextureSourceBinding source)
        {
            if (source == null) return null;
            TexturePaintLayer layer = AddLayer(source.name);
            foreach (var pair in source.textures)
            {
                TextureChannelTarget baseChannel = GetChannel(pair.Key);
                if (baseChannel == null || pair.Value == null) continue;
                EditableTextureTarget target = new EditableTextureTarget(layer.name + " " + pair.Key,
                    baseChannel.Texture.width, baseChannel.Texture.height, baseChannel.format,
                    pair.Value, TextureSet.DefaultColor(pair.Key));
                layer.channels[pair.Key] = target;
                layer.GetChannelSettings(pair.Key);
            }
            BindPreviewTextures();
            return layer;
        }

        public TexturePaintLayer EnsureActiveLayer()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count || layers[activeLayerIndex].kind == TexturePaintLayerKind.Group)
                return AddLayer("Paint Layer");
            return layers[activeLayerIndex];
        }

        public bool SelectSource(int sourceIndex)
        {
            if ((uint)sourceIndex >= (uint)sources.Count) return false;
            TextureSourceBinding source = sources[sourceIndex];
            foreach (var pair in source.textures)
            {
                if (!channels.TryGetValue(pair.Key, out TextureChannelTarget channel)) continue;
                channel.ResetToSource(pair.Value);
            }
            return true;
        }

        public void BindPreviewTextures(bool recompose = true, RectInt dirtyRect = default)
        {
            NormalizeLayerHierarchy();
            RefreshOutdatedFillLayers();
            if (recompose) RecomposeAll();
            PackPhysicalChannels(dirtyRect);
            compositeRevision++;
            // Composite and packed textures are also consumed by the 2D view and export. They must
            // be refreshed even when a reconstructed surface has no preview material (notably
            // standalone/test data); only the final material binding depends on this object.
            if (previewMaterial == null) return;
            foreach (TextureChannelTarget target in channels.Values)
            {
                if (!string.IsNullOrEmpty(target.physicalProperty)) continue;
                if (!string.IsNullOrEmpty(target.materialProperty) && previewMaterial.HasProperty(target.materialProperty))
                {
                    previewMaterial.SetTexture(target.materialProperty, target.PreviewTexture);
                    EnablePreviewTextureKeyword(previewMaterial, target.materialProperty);
                }
            }
            foreach (TexturePhysicalChannelGroup group in physicalChannelGroups.Values)
                if (previewMaterial.HasProperty(group.materialProperty))
                {
                    previewMaterial.SetTexture(group.materialProperty, group.packed);
                    EnablePreviewTextureKeyword(previewMaterial, group.materialProperty);
                }
        }

        private static void EnablePreviewTextureKeyword(Material material, string property)
        {
            if (material == null || string.IsNullOrEmpty(property)) return;
            if (string.Equals(property, "_MetallicGlossMap", StringComparison.OrdinalIgnoreCase))
            {
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            else if (string.Equals(property, "_SpecGlossMap", StringComparison.OrdinalIgnoreCase))
            {
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            else if (string.Equals(property, "_MaskMap", StringComparison.OrdinalIgnoreCase))
                material.EnableKeyword("_MASKMAP");
            else if (string.Equals(property, "_BumpMap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property, "_NormalMap", StringComparison.OrdinalIgnoreCase))
                material.EnableKeyword("_NORMALMAP");
            else if (string.Equals(property, "_EmissionMap", StringComparison.OrdinalIgnoreCase))
                material.EnableKeyword("_EMISSION");
        }

        private void RefreshOutdatedFillLayers()
        {
            for (int i = 0; i < layers.Count; i++)
            {
                TexturePaintLayer layer = layers[i];
                if (layer?.kind != TexturePaintLayerKind.Fill) continue;
                layer.NormalizeKindPayload();
                if (layer.fillSettings.generatorRevision >= TexturePaintFillGenerator.CurrentRevision) continue;
                RegenerateFillLayer(layer);
            }
        }

        private void PackPhysicalChannels(RectInt dirtyRect = default)
        {
            if (channelPackShader == null || !SystemInfo.supportsComputeShaders) return;
            int kernel = channelPackShader.FindKernel("CSPackChannels");
            foreach (TexturePhysicalChannelGroup group in physicalChannelGroups.Values)
            {
                if (group.packed == null) continue;
                RectInt rect = ClampRect(dirtyRect, group.packed.width, group.packed.height);
                // A dirty rect belongs to the logical channel that was edited. Other packed
                // physical textures on the same material may be smaller or have a disjoint
                // coordinate range, so their clamped intersection can legitimately be empty.
                // Unity rejects a compute dispatch with a zero X or Y thread-group count.
                if (rect.width <= 0 || rect.height <= 0) continue;
                channelPackShader.SetInts("_TextureSize", group.packed.width, group.packed.height);
                channelPackShader.SetInts("_TileOffset", rect.x, rect.y);
                channelPackShader.SetInts("_DispatchSize", rect.width, rect.height);
                channelPackShader.SetTexture(kernel, "_Source", group.source != null ? group.source : Texture2D.whiteTexture);
                BindPackComponent(channelPackShader, kernel, group, 0, "Red");
                BindPackComponent(channelPackShader, kernel, group, 1, "Green");
                BindPackComponent(channelPackShader, kernel, group, 2, "Blue");
                BindPackComponent(channelPackShader, kernel, group, 3, "Alpha");
                channelPackShader.SetTexture(kernel, "_Destination", group.packed);
                channelPackShader.Dispatch(kernel, Mathf.CeilToInt(rect.width / 16f), Mathf.CeilToInt(rect.height / 16f), 1);
            }
        }

        private static void BindPackComponent(ComputeShader shader, int kernel, TexturePhysicalChannelGroup group,
            int component, string suffix)
        {
            TextureChannelTarget target = group.componentTargets[component];
            bool valid = target != null && target.PreviewTexture != null;
            shader.SetInt("_Has" + suffix, valid ? 1 : 0);
            shader.SetInt("_" + suffix + "SourceComponent", group.sourceComponents[component]);
            shader.SetInt("_" + suffix + "Invert", group.inverted[component] ? 1 : 0);
            shader.SetTexture(kernel, "_" + suffix, valid ? (Texture)target.PreviewTexture : Texture2D.blackTexture);
        }

        private static RectInt ClampRect(RectInt rect, int width, int height)
        {
            if (rect.width <= 0 || rect.height <= 0) return new RectInt(0, 0, width, height);
            int xMin = Mathf.Clamp(rect.xMin, 0, width), yMin = Mathf.Clamp(rect.yMin, 0, height);
            int xMax = Mathf.Clamp(rect.xMax, xMin, width), yMax = Mathf.Clamp(rect.yMax, yMin, height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        public RenderTexture GetVisibleTexture(TexturePaintChannel channel)
        {
            TextureChannelTarget target = GetChannel(channel);
            return target?.PreviewTexture;
        }

        public RenderTexture GetSelectedGroupPreview(TexturePaintChannel channel)
        {
            if ((uint)activeLayerIndex >= (uint)layers.Count) return null;
            TexturePaintLayer group = layers[activeLayerIndex];
            if (group?.kind != TexturePaintLayerKind.Group || compositor == null) return null;
            TextureChannelTarget target = GetChannel(channel);
            if (target?.editable?.Front == null) return null;
            if (selectedGroupPreview == null || selectedGroupPreview.width != target.editable.Width ||
                selectedGroupPreview.height != target.editable.Height || selectedGroupPreview.format != target.format)
            {
                DestroyPreview(selectedGroupPreview);
                selectedGroupPreview = EditableTextureTarget.Create(
                    Name + " Selected Group " + channel, target.editable.Width,
                    target.editable.Height, target.format);
                selectedGroupPreviewRevision = -1;
            }
            if (selectedGroupPreviewRevision != compositeRevision ||
                !string.Equals(selectedGroupPreviewId, group.id, StringComparison.Ordinal) ||
                selectedGroupPreviewChannel != channel)
            {
                compositor.ComposeGroupPreview(this, group, channel, selectedGroupPreview);
                selectedGroupPreviewId = group.id;
                selectedGroupPreviewChannel = channel;
                selectedGroupPreviewRevision = compositeRevision;
            }
            return selectedGroupPreview;
        }

        public Texture GetLayerMaskPreview(TexturePaintLayer layer)
        {
            TexturePaintLayerMask mask = layer?.layerMask;
            if (mask?.target?.Front == null) return null;
            return compositor?.GetEffectiveLayerMask(layer, mask.target.Width, mask.target.Height) ??
                mask.target.Front;
        }

        public void CompositeChannel(TexturePaintChannel channel, RectInt rect = default)
        {
            NormalizeLayerHierarchy();
            compositor?.Compose(this, channel, rect);
        }

        public void RecomposeAll()
        {
            foreach (TextureChannelTarget target in channels.Values) CompositeChannel(target.channel);
        }

        /// <summary>
        /// Restores this surface to its reconstructed source textures and removes all authored state.
        /// This includes direct base-texture painting, which is intentionally not owned by a layer.
        /// </summary>
        public void ClearModifications()
        {
            for (int i = 0; i < layers.Count; i++) layers[i]?.Dispose();
            layers.Clear();
            activeLayerIndex = -1;
            baseStrokes.Clear();
            foreach (TextureChannelTarget channel in channels.Values)
                channel?.ResetToSource(channel.sourceTexture);
            BindPreviewTextures();
        }

        public void Dispose()
        {
            foreach (TextureChannelTarget target in channels.Values) target.Dispose();
            foreach (TexturePhysicalChannelGroup group in physicalChannelGroups.Values) group.Dispose();
            for (int i = 0; i < layers.Count; i++) layers[i].Dispose();
            DestroyPreview(selectedGroupPreview);
            selectedGroupPreview = null;
            tangentSpaceMaps?.Dispose();
            proceduralMeshMaps?.Dispose();
            channels.Clear(); physicalChannelGroups.Clear(); layers.Clear(); sources.Clear(); baseStrokes.Clear();
            tangentSpaceMaps = null; proceduralMeshMaps = null;
        }

        private static void DestroyPreview(RenderTexture texture)
        {
            if (texture == null) return;
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        internal static Color DefaultColor(TexturePaintChannel channel)
        {
            switch (channel)
            {
                case TexturePaintChannel.Albedo: return Color.white;
                case TexturePaintChannel.Normal: return new Color(0.5f, 0.5f, 1f, 1f);
                case TexturePaintChannel.Roughness: return Color.white;
                case TexturePaintChannel.AmbientOcclusion: return Color.white;
                default: return Color.black;
            }
        }
    }

    public sealed class TexturePaintLogicalLayerMember
    {
        public TexturePaintLogicalTargetMember targetMember;
        public TextureSet textureSet;
        public TexturePaintLayer layer;
    }

    public sealed class TexturePaintLogicalLayerBinding
    {
        public TexturePaintLogicalTarget target;
        public string logicalLayerId;
        public readonly List<TexturePaintLogicalLayerMember> members = new List<TexturePaintLogicalLayerMember>();
        public bool complete;
        public string error;
    }

    public sealed class TexturePaintLogicalLayerController
    {
        private readonly TexturePaintLogicalTargetCatalog catalog;

        public TexturePaintLogicalLayerController(TexturePaintLogicalTargetCatalog catalog)
        {
            this.catalog = catalog;
        }

        public TexturePaintLogicalTarget FindTarget(TextureSet set)
        {
            if (set == null || catalog == null) return null;
            IReadOnlyList<TexturePaintLogicalTarget> targets = catalog.Targets;
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            for (int memberIndex = 0; memberIndex < targets[targetIndex].members.Count; memberIndex++)
                if (targets[targetIndex].members[memberIndex].textureSets.Contains(set)) return targets[targetIndex];
            return null;
        }

        public TexturePaintLogicalTargetMember FindMember(TexturePaintLogicalTarget target, TextureSet set)
        {
            if (target == null || set == null) return null;
            for (int i = 0; i < target.members.Count; i++)
                if (target.members[i].textureSets.Contains(set)) return target.members[i];
            return null;
        }

        public TexturePaintLogicalTargetMember FindMember(TexturePaintLogicalTarget target, string slotName)
        {
            return target?.FindMember(slotName);
        }

        public List<TextureSet> GetTextureSets(TexturePaintLogicalTarget target)
        {
            var result = new List<TextureSet>();
            if (target == null) return result;
            for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
            for (int setIndex = 0; setIndex < target.members[memberIndex].textureSets.Count; setIndex++)
            {
                TextureSet set = target.members[memberIndex].textureSets[setIndex];
                if (set != null && !result.Contains(set)) result.Add(set);
            }
            return result;
        }

        public TexturePaintLogicalLayerBinding Resolve(TexturePaintLogicalTarget target, string logicalLayerId)
        {
            var binding = new TexturePaintLogicalLayerBinding
            {
                target = target,
                logicalLayerId = logicalLayerId,
                complete = false
            };
            if (target == null) { binding.error = "No logical paint target is selected."; return binding; }
            if (string.IsNullOrEmpty(logicalLayerId)) { binding.error = "The selected layer has no logical identity."; return binding; }
            var resolvedSets = new HashSet<TextureSet>();
            for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
            {
                TexturePaintLogicalTargetMember targetMember = target.members[memberIndex];
                if (targetMember.textureSets.Count == 0)
                {
                    binding.error = $"Target member '{targetMember.slotName}' has no physical texture set.";
                    return binding;
                }
                for (int setIndex = 0; setIndex < targetMember.textureSets.Count; setIndex++)
                {
                    TextureSet set = targetMember.textureSets[setIndex];
                    if (!resolvedSets.Add(set)) continue;
                    TexturePaintLayer match = null;
                    for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                    {
                        TexturePaintLayer candidate = set.layers[layerIndex];
                        if (!string.Equals(candidate.logicalLayerId, logicalLayerId, StringComparison.Ordinal)) continue;
                        if (match != null)
                        {
                            binding.error = $"Texture set '{set.Name}' contains duplicate physical layers for logical layer {logicalLayerId}.";
                            return binding;
                        }
                        match = candidate;
                    }
                    if (match == null)
                    {
                        binding.error = $"Logical layer is missing from target member '{targetMember.slotName}'.";
                        return binding;
                    }
                    if (!string.Equals(match.paintTargetId, target.id, StringComparison.Ordinal))
                    {
                        binding.error = $"Layer '{match.name}' is bound to the wrong paint target.";
                        return binding;
                    }
                    binding.members.Add(new TexturePaintLogicalLayerMember
                    {
                        targetMember = targetMember,
                        textureSet = set,
                        layer = match
                    });
                }
            }
            binding.complete = true;
            return binding;
        }

        public bool LinkAndRepair(TexturePaintLogicalTarget target, TextureSet primarySet,
            TexturePaintLayer primaryLayer, List<TexturePaintLogicalLayerMember> created, out TexturePaintLogicalLayerBinding binding)
        {
            binding = null;
            if (target == null || primarySet == null || primaryLayer == null) return false;
            if (FindMember(target, primarySet) == null) return false;
            string previousLogicalId = primaryLayer.logicalLayerId;
            string previousTargetId = primaryLayer.paintTargetId;
            string logicalLayerId = string.IsNullOrEmpty(previousLogicalId) ? Guid.NewGuid().ToString("N") : previousLogicalId;
            primaryLayer.logicalLayerId = logicalLayerId;
            primaryLayer.paintTargetId = target.id;
            var newlyCreated = new List<TexturePaintLogicalLayerMember>();
            List<TextureSet> sets = GetTextureSets(target);
            for (int setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                TextureSet set = sets[setIndex];
                TexturePaintLayer existing = FindLayer(set, logicalLayerId);
                if (existing != null)
                {
                    if (!string.Equals(existing.paintTargetId, target.id, StringComparison.Ordinal) ||
                        existing.kind != primaryLayer.kind)
                    {
                        RollbackCreated(newlyCreated);
                        primaryLayer.logicalLayerId = previousLogicalId;
                        primaryLayer.paintTargetId = previousTargetId;
                        return false;
                    }
                    continue;
                }
                if (ReferenceEquals(set, primarySet)) continue;
                TexturePaintLayer linked = CreatePhysicalLayer(set, target, primarySet, primaryLayer, logicalLayerId);
                if (linked == null)
                {
                    RollbackCreated(newlyCreated);
                    primaryLayer.logicalLayerId = previousLogicalId;
                    primaryLayer.paintTargetId = previousTargetId;
                    return false;
                }
                var member = new TexturePaintLogicalLayerMember
                {
                    targetMember = FindMember(target, set), textureSet = set, layer = linked
                };
                newlyCreated.Add(member);
                created?.Add(member);
            }
            binding = Resolve(target, logicalLayerId);
            if (!binding.complete)
            {
                RollbackCreated(newlyCreated);
                primaryLayer.logicalLayerId = previousLogicalId;
                primaryLayer.paintTargetId = previousTargetId;
                return false;
            }
            return true;
        }

        public bool Activate(TexturePaintLogicalLayerBinding binding)
        {
            if (binding == null || !binding.complete) return false;
            for (int i = 0; i < binding.members.Count; i++)
            {
                TexturePaintLogicalLayerMember member = binding.members[i];
                int index = member.textureSet.layers.IndexOf(member.layer);
                if (index < 0) return false;
                member.textureSet.activeLayerIndex = index;
            }
            return true;
        }

        public bool ValidatePaintBinding(TexturePaintLogicalLayerBinding binding, TexturePaintChannel channel,
            out string error)
        {
            error = binding?.error;
            if (binding == null || !binding.complete) return false;
            for (int i = 0; i < binding.members.Count; i++)
            {
                TexturePaintLogicalLayerMember member = binding.members[i];
                if (member.layer.kind != TexturePaintLayerKind.Paint)
                {
                    error = $"Logical layer '{member.layer.name}' is not a Paint layer on '{member.targetMember.slotName}'.";
                    return false;
                }
                if (member.textureSet.GetChannel(channel) == null)
                {
                    error = $"Target member '{member.targetMember.slotName}' does not support {channel}.";
                    return false;
                }
                if (member.textureSet.activeLayerIndex != member.textureSet.layers.IndexOf(member.layer))
                {
                    error = $"Logical layer is not active on target member '{member.targetMember.slotName}'.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        public static TexturePaintLayer FindLayer(TextureSet set, string logicalLayerId)
        {
            for (int i = 0; i < set.layers.Count; i++)
                if (string.Equals(set.layers[i].logicalLayerId, logicalLayerId, StringComparison.Ordinal)) return set.layers[i];
            return null;
        }

        public static OverlayDataAsset ResolveMemberOverlay(TexturePaintLogicalTarget target,
            TextureSet sourceSet, OverlayDataAsset sourceAsset, TextureSet destinationSet)
        {
            if (target == null || sourceAsset == null || destinationSet == null) return null;
            TexturePaintLogicalTargetMember sourceMember = null;
            TexturePaintLogicalTargetMember destinationMember = null;
            for (int i = 0; i < target.members.Count; i++)
            {
                TexturePaintLogicalTargetMember member = target.members[i];
                if (member.textureSets.Contains(sourceSet)) sourceMember ??= member;
                if (member.textureSets.Contains(destinationSet)) destinationMember ??= member;
            }
            if (destinationMember == null) return null;
            for (int i = 0; i < destinationMember.sourceOverlays.Count; i++)
                if (destinationMember.sourceOverlays[i]?.asset == sourceAsset) return sourceAsset;

            int sourceOrdinal = -1;
            if (sourceMember != null)
                for (int i = 0; i < sourceMember.sourceOverlays.Count; i++)
                    if (sourceMember.sourceOverlays[i]?.asset == sourceAsset) { sourceOrdinal = i; break; }
            if (sourceOrdinal >= 0 && sourceOrdinal < destinationMember.sourceOverlays.Count)
                return destinationMember.sourceOverlays[sourceOrdinal]?.asset;

            string sourceName = sourceAsset.overlayName;
            if (!string.IsNullOrEmpty(sourceName))
                for (int i = 0; i < destinationMember.sourceOverlays.Count; i++)
                    if (string.Equals(destinationMember.sourceOverlays[i]?.asset?.overlayName, sourceName,
                        StringComparison.Ordinal)) return destinationMember.sourceOverlays[i].asset;
            return null;
        }

        private TexturePaintLayer CreatePhysicalLayer(TextureSet set, TexturePaintLogicalTarget target,
            TextureSet templateSet, TexturePaintLayer template, string logicalLayerId)
        {
            TexturePaintLayer layer;
            switch (template.kind)
            {
                case TexturePaintLayerKind.Fill:
                    TexturePaintFillSettings fillSettings = template.fillSettings?.Clone() ??
                        new TexturePaintFillSettings
                        {
                            source = TexturePaintBrushSource.Color,
                            color = template.fillColor
                        };
                    if (fillSettings.source == TexturePaintBrushSource.Overlay)
                    {
                        fillSettings.sourceOverlay = ResolveMemberOverlay(target, templateSet,
                            fillSettings.sourceOverlay, set);
                        if (fillSettings.sourceOverlay == null) return null;
                    }
                    layer = set.AddFillLayer(template.name, template.fillChannel, fillSettings);
                    break;
                case TexturePaintLayerKind.Group:
                    layer = set.AddGroup(template.name);
                    break;
                case TexturePaintLayerKind.Spline:
                    layer = set.AddSplineLayer(template.name);
                    break;
                default:
                    layer = set.AddLayer(template.name);
                    break;
            }
            if (layer == null) return null;
            layer.logicalLayerId = logicalLayerId;
            layer.paintTargetId = target.id;
            int templateIndex = templateSet.layers.IndexOf(template);
            int currentIndex = set.layers.IndexOf(layer);
            if (templateIndex >= 0 && currentIndex >= 0 && currentIndex != Mathf.Clamp(templateIndex, 0, set.layers.Count - 1))
                set.MoveLayer(currentIndex, Mathf.Clamp(templateIndex, 0, set.layers.Count - 1));
            layer.name = template.name;
            layer.visible = template.visible;
            layer.opacity = template.opacity;
            layer.blendMode = template.blendMode;
            layer.effects = template.effects?.Clone() ?? new TexturePaintLayerEffects();
            layer.fillChannel = template.fillChannel;
            layer.fillColor = template.fillColor;
            if (template.kind != TexturePaintLayerKind.Fill)
                layer.fillSettings = template.fillSettings?.Clone();
            layer.paintSettings = template.paintSettings?.Clone();
            layer.pluginId = template.pluginId;
            layer.pluginVersion = template.pluginVersion;
            layer.pluginParametersJson = template.pluginParametersJson;
            layer.proceduralGroupKey = template.proceduralGroupKey;
            layer.NormalizeKindPayload();
            layer.parentId = ResolvePhysicalParentId(templateSet, set, template.parentId);
            if (template.IsSplineLayer)
            {
                layer.spline = template.spline != null
                    ? JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(template.spline)) : new TexturePaintSpline();
                layer.splineSettings = template.splineSettings?.Clone() ?? new TexturePaintSplineSettings();
            }
            layer.channelSettings.Clear();
            foreach (KeyValuePair<TexturePaintChannel, TexturePaintLayerChannelSettings> pair in template.channelSettings)
                if (set.GetChannel(pair.Key) != null) layer.channelSettings[pair.Key] = pair.Value.Clone();
            if (template.layerMask?.target?.Front != null)
            {
                TexturePaintLayerMask mask = set.AddLayerMask(layer, template.layerMask.baseValue);
                if (mask != null)
                {
                    mask.effects = template.layerMask.effects?.Clone() ?? new TexturePaintLayerMaskEffects();
                    mask.sourceSettings = template.layerMask.sourceSettings?.Clone() ??
                        TexturePaintLayerMask.DefaultSourceSettings();
                    mask.sourceChannel = template.layerMask.sourceChannel;
                    mask.NormalizePaintSource();
                    mask.target.Reset(template.layerMask.target.Front,
                        TextureSet.MaskColor(template.layerMask.baseValue));
                }
            }
            set.BindPreviewTextures();
            return layer;
        }

        private static string ResolvePhysicalParentId(TextureSet templateSet, TextureSet destinationSet,
            string templateParentId)
        {
            if (string.IsNullOrEmpty(templateParentId) || templateSet == null || destinationSet == null) return null;
            TexturePaintLayer templateParent = null;
            for (int i = 0; i < templateSet.layers.Count; i++)
                if (string.Equals(templateSet.layers[i].id, templateParentId, StringComparison.Ordinal))
                { templateParent = templateSet.layers[i]; break; }
            if (templateParent == null || string.IsNullOrEmpty(templateParent.logicalLayerId)) return null;
            return FindLayer(destinationSet, templateParent.logicalLayerId)?.id;
        }

        private static void RollbackCreated(List<TexturePaintLogicalLayerMember> created)
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                TexturePaintLogicalLayerMember member = created[i];
                member.textureSet.layers.Remove(member.layer);
                member.layer.Dispose();
                member.textureSet.activeLayerIndex = Mathf.Clamp(member.textureSet.activeLayerIndex, -1,
                    member.textureSet.layers.Count - 1);
                member.textureSet.BindPreviewTextures();
            }
        }
    }

    public sealed class TextureStore : IDisposable
    {
        private readonly List<TextureSet> sets = new List<TextureSet>();
        public IReadOnlyList<TextureSet> Sets => sets;

        public void ClearModifications()
        {
            for (int i = 0; i < sets.Count; i++) sets[i]?.ClearModifications();
        }
        public int DefaultResolution { get; private set; }
        private TextureLayerCompositor compositor;
        private ComputeShader channelPackShader;
        private TexturePaintFillGenerator fillGenerator;

        public void Initialize(MeshReconstructionResult reconstruction, int defaultResolution = 2048,
            ComputeShader layerCompositeShader = null, ComputeShader channelPackShader = null,
            Shader fillShader = null)
        {
            if (reconstruction == null) throw new ArgumentNullException(nameof(reconstruction));
            Dispose();
            compositor = new TextureLayerCompositor(layerCompositeShader);
            this.channelPackShader = channelPackShader;
            fillGenerator = new TexturePaintFillGenerator(fillShader);
            DefaultResolution = Mathf.Clamp(defaultResolution, 128, 4096);
            for (int i = 0; i < reconstruction.surfaces.Count; i++) sets.Add(BuildSet(reconstruction.surfaces[i]));
        }

        public TextureSet FindSet(int surfaceIndex)
        {
            for (int i = 0; i < sets.Count; i++) if (sets[i].surface.index == surfaceIndex) return sets[i];
            return null;
        }

        public void Dispose()
        {
            for (int i = 0; i < sets.Count; i++) sets[i].Dispose();
            sets.Clear();
            compositor?.Dispose();
            compositor = null;
            fillGenerator?.Dispose();
            fillGenerator = null;
            channelPackShader = null;
        }

        private TextureSet BuildSet(ReconstructedSurface surface)
        {
            TextureSet set = new TextureSet
            {
                surface = surface,
                previewMaterial = surface.previewMaterial,
                umaMaterial = surface.umaMaterial,
                compositor = compositor,
                channelPackShader = channelPackShader,
                fillGenerator = fillGenerator
            };
            UMAData.GeneratedMaterial generated = surface.generatedMaterial;
            bool hasDeclaredUmaChannels = surface.umaMaterial != null &&
                                          surface.umaMaterial.channels != null &&
                                          surface.umaMaterial.channels.Length > 0;
            if (hasDeclaredUmaChannels)
            {
#if UNITY_EDITOR
                Texture[] declaredSources = new Texture[surface.umaMaterial.channels.Length];
                for (int channelIndex = 0; channelIndex < surface.umaMaterial.channels.Length; channelIndex++)
                {
                    UMAMaterial.MaterialChannel declared = surface.umaMaterial.channels[channelIndex];
                    declaredSources[channelIndex] = surface.sourceTextures != null &&
                                                    channelIndex < surface.sourceTextures.Length
                        ? surface.sourceTextures[channelIndex]
                        : generated == null
                            ? GetMaterialTexture(surface.previewMaterial, declared.materialPropertyName)
                            : null;
                }
                set.materialCapability = TexturePaintMaterialCapabilityService.Compile(surface.umaMaterial,
                    surface.previewMaterial, declaredSources, surface.allowMissingSourceTextures);
#endif
                for (int channelIndex = 0; channelIndex < surface.umaMaterial.channels.Length; channelIndex++)
                {
                    UMAMaterial.MaterialChannel umaChannel = surface.umaMaterial.channels[channelIndex];
                    string property = umaChannel.materialPropertyName;
                    TexturePaintNormalConvention sourceNormalConvention = TexturePaintNormalConvention.OpenGL;
#if UNITY_EDITOR
                    Texture source = declaredSources[channelIndex];
                    TexturePaintMaterialChannelCapability channelCapability =
                        set.materialCapability.GetChannel(channelIndex);
                    if (channelCapability != null && channelCapability.output.normalConvention ==
                        UMAMaterial.TextureChannelNormalConvention.DirectX)
                    {
                        sourceNormalConvention = TexturePaintNormalConvention.DirectX;
                    }
#else
                    Texture source = surface.sourceTextures != null && channelIndex < surface.sourceTextures.Length
                        ? surface.sourceTextures[channelIndex]
                        : generated == null ? GetMaterialTexture(surface.previewMaterial, property) : null;
#endif
                    bool sourceNormalIsUnityPacked = surface.sourceNormalIsUnityPacked != null &&
                        channelIndex < surface.sourceNormalIsUnityPacked.Length &&
                        surface.sourceNormalIsUnityPacked[channelIndex];
#if UNITY_EDITOR
                    if (TryAddMaterialChannelFromLayout(set, umaChannel, channelIndex, source,
                        channelCapability, sourceNormalConvention, sourceNormalIsUnityPacked))
                    {
                        continue;
                    }
#endif
                    AddChannel(set, ResolveChannel(property, umaChannel.channelType), property, umaChannel.sourceTextureName,
                        channelIndex, source, umaChannel.textureFormat, sourceNormalConvention,
                        sourceNormalIsUnityPacked);
                }
            }
            else
            {
                foreach (string property in surface.previewMaterial.GetTexturePropertyNames())
                {
                    TexturePaintChannel semantic = ResolveChannel(property, UMAMaterial.ChannelType.Texture);
                    if (semantic == TexturePaintChannel.Custom && set.channels.ContainsKey(TexturePaintChannel.Custom)) continue;
                    AddChannel(set, semantic, property, property, -1, surface.previewMaterial.GetTexture(property), RenderTextureFormat.ARGB32);
                }
            }
            BuildPackedChannelGroups(set);
            if (!hasDeclaredUmaChannels) EnsureMinimumChannels(set);
            BuildSourceBindings(set, generated, surface);
            TextureChannelTarget normal = set.GetChannel(TexturePaintChannel.Normal);
            int mapResolution = normal != null ? Mathf.Min(normal.Texture.width, 2048) : Mathf.Min(DefaultResolution, 2048);
            set.tangentSpaceMaps = TangentSpaceMapBuilder.Build(surface.mesh, mapResolution, mapResolution);
            set.BindPreviewTextures();
            return set;
        }

#if UNITY_EDITOR
        private struct LayoutComponentMapping
        {
            public int physicalComponent;
            public bool invert;
        }

        private bool TryAddMaterialChannelFromLayout(TextureSet set, UMAMaterial.MaterialChannel umaChannel,
            int channelIndex, Texture source, TexturePaintMaterialChannelCapability capability,
            TexturePaintNormalConvention sourceNormalConvention, bool sourceNormalIsUnityPacked)
        {
            UMAMaterial.TextureChannelLayout layout = capability != null ? capability.layout :
                UMAMaterial.GetTextureChannelLayout(umaChannel, set.previewMaterial);
            Dictionary<TexturePaintChannel, List<LayoutComponentMapping>> mappings =
                new Dictionary<TexturePaintChannel, List<LayoutComponentMapping>>();

            for (int component = 0; component < 4; component++)
            {
                TexturePaintPhysicalComponentCapability componentCapability = capability?.Components[component];
                bool resolved = componentCapability != null
                    ? componentCapability.editable
                    : TexturePaintMaterialCapabilityService.TryResolveUsage(layout.GetComponent(component),
                        out _, out _);
                TexturePaintChannel semantic;
                bool invert;
                if (componentCapability != null)
                {
                    semantic = componentCapability.logicalChannel;
                    invert = componentCapability.invert;
                }
                else
                {
                    TexturePaintMaterialCapabilityService.TryResolveUsage(layout.GetComponent(component),
                        out semantic, out invert);
                }
                if (!resolved)
                {
                    continue;
                }

                if (!mappings.TryGetValue(semantic, out List<LayoutComponentMapping> semanticMappings))
                {
                    semanticMappings = new List<LayoutComponentMapping>();
                    mappings.Add(semantic, semanticMappings);
                }
                semanticMappings.Add(new LayoutComponentMapping { physicalComponent = component, invert = invert });
            }

            if (mappings.Count == 0)
            {
                bool hasAuthoredMeaning = layout.red != UMAMaterial.TextureChannelUsage.Unused ||
                                          layout.green != UMAMaterial.TextureChannelUsage.Unused ||
                                          layout.blue != UMAMaterial.TextureChannelUsage.Unused ||
                                          layout.alpha != UMAMaterial.TextureChannelUsage.Unused;
                // The layout is intentional but contains meanings that do not yet have a
                // dedicated logical TexturePaintChannel. Keep the physical texture editable
                // as Custom instead of reverting to a contradictory property-name guess.
                if (hasAuthoredMeaning)
                {
                    AddChannel(set, TexturePaintChannel.Custom, umaChannel.materialPropertyName,
                        umaChannel.sourceTextureName, channelIndex, source, umaChannel.textureFormat);
                }
                return true;
            }

            if (mappings.Count == 1)
            {
                foreach (KeyValuePair<TexturePaintChannel, List<LayoutComponentMapping>> pair in mappings)
                {
                    if (CanUseDirectTexture(pair.Key, pair.Value))
                    {
                        AddChannel(set, pair.Key, umaChannel.materialPropertyName, umaChannel.sourceTextureName,
                            channelIndex, source, umaChannel.textureFormat, sourceNormalConvention,
                            sourceNormalIsUnityPacked);
                        return true;
                    }
                }
            }

            if (source == null || channelPackShader == null || !SystemInfo.supportsComputeShaders)
            {
                return false;
            }

            string property = umaChannel.materialPropertyName;
            if (string.IsNullOrEmpty(property))
            {
                return false;
            }

            TexturePhysicalChannelGroup group = CreatePhysicalChannelGroup(set, property, source);
            foreach (KeyValuePair<TexturePaintChannel, List<LayoutComponentMapping>> pair in mappings)
            {
                TextureChannelTarget target = set.GetChannel(pair.Key);
                if (target != null && !string.Equals(target.materialProperty, property, StringComparison.Ordinal))
                {
                    // A TextureSet currently exposes one editable target for each logical semantic.
                    // Preserve later duplicate physical uses from their source rather than binding
                    // them to an unrelated logical texture.
                    continue;
                }

                bool vectorChannel = IsVectorChannel(pair.Key);
                RenderTexture extracted = null;
                if (target == null)
                {
                    Texture initial = source;
                    if (!vectorChannel)
                    {
                        LayoutComponentMapping first = pair.Value[0];
                        extracted = ExtractChannel(source, first.physicalComponent, first.invert,
                            set.Name + " " + pair.Key + " Extracted");
                        initial = extracted;
                    }

                    AddChannel(set, pair.Key, property, umaChannel.sourceTextureName, channelIndex,
                        initial, umaChannel.textureFormat, sourceNormalConvention,
                        sourceNormalIsUnityPacked);
                    target = set.GetChannel(pair.Key);
                    DestroyTemporary(extracted);
                }

                if (target == null)
                {
                    continue;
                }

                target.sourceTexture = source;
                target.physicalProperty = property;
                target.packedComponent = pair.Value.Count == 1 ? pair.Value[0].physicalComponent : -1;
                target.packedInverted = pair.Value.Count == 1 && pair.Value[0].invert;
                for (int mappingIndex = 0; mappingIndex < pair.Value.Count; mappingIndex++)
                {
                    LayoutComponentMapping mapping = pair.Value[mappingIndex];
                    group.componentTargets[mapping.physicalComponent] = target;
                    group.sourceComponents[mapping.physicalComponent] = vectorChannel
                        ? mapping.physicalComponent
                        : 0;
                    group.inverted[mapping.physicalComponent] = mapping.invert;
                }
            }
            return true;
        }

        private static bool CanUseDirectTexture(TexturePaintChannel channel, List<LayoutComponentMapping> mappings)
        {
            if (mappings == null || mappings.Count == 0)
            {
                return false;
            }
            if (IsVectorChannel(channel))
            {
                bool hasRed = false, hasGreen = false, hasBlue = false;
                for (int i = 0; i < mappings.Count; i++)
                {
                    if (mappings[i].invert) return false;
                    if (mappings[i].physicalComponent == 0) hasRed = true;
                    else if (mappings[i].physicalComponent == 1) hasGreen = true;
                    else if (mappings[i].physicalComponent == 2) hasBlue = true;
                }
                return hasRed && hasGreen && hasBlue;
            }
            return mappings.Count == 1 && mappings[0].physicalComponent == 0 && !mappings[0].invert;
        }

        private static bool IsVectorChannel(TexturePaintChannel channel)
        {
            return channel == TexturePaintChannel.Albedo || channel == TexturePaintChannel.Normal ||
                   channel == TexturePaintChannel.Emission || channel == TexturePaintChannel.Custom;
        }

        private TexturePhysicalChannelGroup CreatePhysicalChannelGroup(TextureSet set, string property, Texture source)
        {
            if (set.physicalChannelGroups.TryGetValue(property, out TexturePhysicalChannelGroup existing))
            {
                return existing;
            }

            RenderTexture packed = EditableTextureTarget.Create(set.Name + " " + property + " Packed",
                source.width, source.height, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, packed);
            TexturePhysicalChannelGroup group = new TexturePhysicalChannelGroup
            {
                materialProperty = property,
                source = source,
                packed = packed
            };
            set.physicalChannelGroups.Add(property, group);
            return group;
        }

        private static void DestroyTemporary(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
#endif

        private void BuildPackedChannelGroups(TextureSet set)
        {
            if (set.previewMaterial == null || channelPackShader == null) return;
            string property = null;
            if (set.previewMaterial.HasProperty("_MaskMap")) property = "_MaskMap";
            else if (set.previewMaterial.HasProperty("_MetallicGlossMap")) property = "_MetallicGlossMap";
            if (string.IsNullOrEmpty(property)) return;
            if (set.physicalChannelGroups.ContainsKey(property)) return;
#if UNITY_EDITOR
            if (set.umaMaterial?.channels != null)
            {
                for (int i = 0; i < set.umaMaterial.channels.Length; i++)
                {
                    if (string.Equals(set.umaMaterial.channels[i].materialPropertyName, property,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // Editor UMA channels have an explicit automatic/custom layout. Do not
                        // replace an intentional direct or unsupported custom layout with legacy
                        // property-name packing.
                        return;
                    }
                }
            }
#endif
            Texture source = GetMaterialTexture(set.previewMaterial, property);
            if (source == null) return;
            RenderTexture packed = EditableTextureTarget.Create(set.Name + " " + property + " Packed",
                source.width, source.height, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, packed);
            TexturePhysicalChannelGroup group = new TexturePhysicalChannelGroup
            {
                materialProperty = property,
                source = source,
                packed = packed
            };
            set.physicalChannelGroups[property] = group;
            AddPackedSemantic(set, group, TexturePaintChannel.Metallic, property, source, 0, false);
            if (property == "_MaskMap") AddPackedSemantic(set, group, TexturePaintChannel.AmbientOcclusion, property, source, 1, false);
            AddPackedSemantic(set, group, TexturePaintChannel.Roughness, property, source, 3, true);
        }

        private void AddPackedSemantic(TextureSet set, TexturePhysicalChannelGroup group,
            TexturePaintChannel semantic, string property, Texture source, int component, bool invert)
        {
            RenderTexture extracted = ExtractChannel(source, component, invert, set.Name + " " + semantic + " Extracted");
            if (!set.channels.TryGetValue(semantic, out TextureChannelTarget target))
            {
                AddChannel(set, semantic, property, property, -1, extracted, RenderTextureFormat.ARGB32);
                target = set.GetChannel(semantic);
            }
            else target.editable.Reset(extracted, TextureSet.DefaultColor(semantic));
            target.sourceTexture = source;
            target.physicalProperty = property;
            target.packedComponent = component;
            target.packedInverted = invert;
            group.componentTargets[component] = target;
            group.sourceComponents[component] = 0;
            group.inverted[component] = invert;
            if (RenderTexture.active == extracted) RenderTexture.active = null;
            extracted.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(extracted); else UnityEngine.Object.DestroyImmediate(extracted);
        }

        private RenderTexture ExtractChannel(Texture source, int component, bool invert, string name)
        {
            RenderTexture result = EditableTextureTarget.Create(name, source.width, source.height,
                RenderTextureFormat.ARGB32);
            int kernel = channelPackShader.FindKernel("CSExtract");
            channelPackShader.SetInts("_TextureSize", source.width, source.height);
            channelPackShader.SetInts("_DispatchSize", source.width, source.height);
            channelPackShader.SetInt("_Component", component);
            channelPackShader.SetInt("_Invert", invert ? 1 : 0);
            channelPackShader.SetTexture(kernel, "_Source", source);
            channelPackShader.SetTexture(kernel, "_Destination", result);
            channelPackShader.Dispatch(kernel, Mathf.CeilToInt(source.width / 16f), Mathf.CeilToInt(source.height / 16f), 1);
            return result;
        }

        private void AddChannel(TextureSet set, TexturePaintChannel semantic, string property, string keyword,
            int umaIndex, Texture source, RenderTextureFormat requestedFormat,
            TexturePaintNormalConvention sourceNormalConvention = TexturePaintNormalConvention.OpenGL,
            bool sourceNormalIsUnityPacked = false)
        {
            if (set.channels.ContainsKey(semantic)) return;
            int width = source != null ? source.width : DefaultResolution;
            int height = source != null ? source.height : DefaultResolution;
            width = Mathf.Clamp(width, 16, 4096); height = Mathf.Clamp(height, 16, 4096);
            bool sRGB = semantic == TexturePaintChannel.Albedo || semantic == TexturePaintChannel.Emission;
            RenderTextureFormat format = UMAMaterial.GetCompatibleChannelTextureFormat(requestedFormat);
            // Unity normal-map assets are stored in a platform-dependent packed representation
            // (for example X in alpha). The painter's working contract is always linear,
            // OpenGL-style tangent-space RGB. Canonicalize material inputs at the same boundary
            // already used by brush and sprite sources so compositing and export never preserve
            // or re-import Unity's internal packing as authored RGB.
            Texture editableSource = semantic == TexturePaintChannel.Normal
                ? TexturePaintSpriteSource.ResolveTexture(source, semantic, sourceNormalConvention, false,
                    sourceNormalIsUnityPacked)
                : source;
            TextureChannelTarget target = new TextureChannelTarget
            {
                channel = semantic,
                materialProperty = property,
                sourceKeyword = keyword,
                umaChannelIndex = umaIndex,
                sourceTexture = source,
                sourceNormalConvention = sourceNormalConvention,
                sourceNormalIsUnityPacked = sourceNormalIsUnityPacked,
                sRGB = sRGB,
                format = format,
                editable = new EditableTextureTarget(set.Name + " " + semantic, width, height, format,
                    editableSource, TextureSet.DefaultColor(semantic))
            };
            target.composite = EditableTextureTarget.Create(set.Name + " " + semantic + " Composite", width, height, format);
            set.channels.Add(semantic, target);
        }

        private void EnsureMinimumChannels(TextureSet set)
        {
            EnsureChannel(set, TexturePaintChannel.Albedo, FindProperty(set.previewMaterial, "_BaseMap", "_MainTex"));
            EnsureChannel(set, TexturePaintChannel.Normal, FindProperty(set.previewMaterial, "_BumpMap", "_NormalMap"));
            EnsureChannel(set, TexturePaintChannel.Metallic, FindProperty(set.previewMaterial, "_MetallicGlossMap", "_MaskMap"));
            EnsureChannel(set, TexturePaintChannel.Roughness, FindProperty(set.previewMaterial, "_RoughnessMap"));
            EnsureChannel(set, TexturePaintChannel.AmbientOcclusion, FindProperty(set.previewMaterial, "_OcclusionMap"));
            EnsureChannel(set, TexturePaintChannel.Emission, FindProperty(set.previewMaterial, "_EmissionMap"));
        }

        private void EnsureChannel(TextureSet set, TexturePaintChannel channel, string property)
        {
            if (set.channels.ContainsKey(channel)) return;
            AddChannel(set, channel, property, property, -1, GetMaterialTexture(set.previewMaterial, property), RenderTextureFormat.ARGB32);
        }

        private static void BuildSourceBindings(TextureSet set, UMAData.GeneratedMaterial generated, ReconstructedSurface surface)
        {
            TextureSourceBinding reconstructedBinding = new TextureSourceBinding { name = "Reconstructed Overlay Stack" };
            foreach (var pair in set.channels) if (pair.Value.sourceTexture != null) reconstructedBinding.textures[pair.Key] = pair.Value.sourceTexture;
            if (surface?.slotNames != null)
                for (int slotIndex = 0; slotIndex < surface.slotNames.Count; slotIndex++)
                    if (!string.IsNullOrEmpty(surface.slotNames[slotIndex])) reconstructedBinding.slotNames.Add(surface.slotNames[slotIndex]);
            set.sources.Add(reconstructedBinding);
            if (surface?.standaloneSourceOverlay != null)
            {
                OverlayData standaloneOverlay = new OverlayData(surface.standaloneSourceOverlay);
                TextureSourceBinding binding = new TextureSourceBinding
                {
                    name = surface.standaloneSourceOverlay.overlayName,
                    overlay = standaloneOverlay
                };
                for (int slotIndex = 0; slotIndex < surface.slotNames.Count; slotIndex++)
                    binding.slotNames.Add(surface.slotNames[slotIndex]);
                foreach (KeyValuePair<TexturePaintChannel, TextureChannelTarget> pair in set.channels)
                    if (pair.Value.sourceTexture != null) binding.textures[pair.Key] = pair.Value.sourceTexture;
                set.sources.Add(binding);
            }
            if (generated?.materialFragments == null) return;
            Dictionary<OverlayData, TextureSourceBinding> bindings = new Dictionary<OverlayData, TextureSourceBinding>();
            for (int fragmentIndex = 0; fragmentIndex < generated.materialFragments.Count; fragmentIndex++)
            {
                UMAData.MaterialFragment fragment = generated.materialFragments[fragmentIndex];
                string fragmentSlotName = fragment?.slotData?.slotName;
                if (surface != null && !string.IsNullOrEmpty(fragmentSlotName) && !surface.ContainsSlot(fragmentSlotName)) continue;
                List<OverlayData> overlays = fragment?.overlayList;
                if (overlays == null) continue;
                for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                {
                    OverlayData overlay = overlays[overlayIndex];
                    if (overlay?.asset == null) continue;
                    if (bindings.TryGetValue(overlay, out TextureSourceBinding existing))
                    {
                        string existingSlot = fragment.slotData?.slotName;
                        if (!string.IsNullOrEmpty(existingSlot) && !existing.slotNames.Contains(existingSlot))
                            existing.slotNames.Add(existingSlot);
                        continue;
                    }
                    TextureSourceBinding binding = new TextureSourceBinding { name = overlay.overlayName, overlay = overlay };
                    string slotName = fragment.slotData?.slotName;
                    if (!string.IsNullOrEmpty(slotName)) binding.slotNames.Add(slotName);
                    Texture[] textures = overlay.asset.textureList;
                    for (int channelIndex = 0; channelIndex < textures.Length; channelIndex++)
                    {
                        TexturePaintChannel semantic = TexturePaintChannel.Custom;
                        if (set.umaMaterial?.channels != null && channelIndex < set.umaMaterial.channels.Length)
                        {
                            var channel = set.umaMaterial.channels[channelIndex];
#if UNITY_EDITOR
                            semantic = ResolveChannel(channel, set.previewMaterial);
#else
                            semantic = ResolveChannel(channel.sourceTextureName + " " + channel.materialPropertyName, channel.channelType);
#endif
                        }
                        binding.textures[semantic] = textures[channelIndex];
                    }
                    bindings.Add(overlay, binding);
                    set.sources.Add(binding);
                }
            }
        }

        public static TexturePaintChannel ResolveChannel(string property, UMAMaterial.ChannelType type)
        {
            string name = (property ?? string.Empty).ToLowerInvariant();
            if (type == UMAMaterial.ChannelType.NormalMap || type == UMAMaterial.ChannelType.DetailNormalMap || name.Contains("normal") || name.Contains("bump")) return TexturePaintChannel.Normal;
            if (name.Contains("rough")) return TexturePaintChannel.Roughness;
            if (name.Contains("occlusion") || name.Contains("ambient") || name == "ao") return TexturePaintChannel.AmbientOcclusion;
            if (name.Contains("emission") || name.Contains("emissive")) return TexturePaintChannel.Emission;
            if (name.Contains("metal") || name.Contains("maskmap")) return TexturePaintChannel.Metallic;
            if (name.Contains("base") || name.Contains("main") || name.Contains("diffuse") || name.Contains("albedo") || type == UMAMaterial.ChannelType.DiffuseTexture || type == UMAMaterial.ChannelType.TintedTexture) return TexturePaintChannel.Albedo;
            return TexturePaintChannel.Custom;
        }

#if UNITY_EDITOR
        public static TexturePaintChannel ResolveChannel(UMAMaterial.MaterialChannel channel, Material material)
        {
            UMAMaterial.TextureChannelLayout layout = UMAMaterial.GetTextureChannelLayout(channel, material);
            for (int component = 0; component < 4; component++)
            {
                if (TexturePaintMaterialCapabilityService.TryResolveUsage(layout.GetComponent(component),
                    out TexturePaintChannel semantic, out _))
                {
                    return semantic;
                }
            }
            return ResolveChannel(channel.sourceTextureName + " " + channel.materialPropertyName, channel.channelType);
        }
#endif

        private static Texture GetMaterialTexture(Material material, string property) => material != null && !string.IsNullOrEmpty(property) && material.HasProperty(property) ? material.GetTexture(property) : null;
        private static string FindProperty(Material material, params string[] candidates)
        {
            if (material == null) return candidates.Length > 0 ? candidates[0] : string.Empty;
            for (int i = 0; i < candidates.Length; i++) if (material.HasProperty(candidates[i])) return candidates[i];
            return candidates.Length > 0 ? candidates[0] : string.Empty;
        }
    }
}
