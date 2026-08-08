using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA.TexturePaint
{
    public enum TexturePaintChannel
    {
        Albedo,
        Normal,
        Metallic,
        Roughness,
        AmbientOcclusion,
        Emission,
        Custom,
        SkinColorMask,
        Thickness,
        DetailMask,
        NormalControl
    }

    public static class TexturePaintChannelUtility
    {
        public static string DisplayName(TexturePaintChannel channel)
        {
            switch (channel)
            {
                case TexturePaintChannel.AmbientOcclusion: return "Ambient Occlusion";
                case TexturePaintChannel.SkinColorMask: return "Skin Color Mask";
                case TexturePaintChannel.DetailMask: return "Detail Mask";
                case TexturePaintChannel.NormalControl: return "Normal Control";
                default: return channel.ToString();
            }
        }

        public static bool IsColor(TexturePaintChannel channel)
        {
            return channel == TexturePaintChannel.Albedo ||
                   channel == TexturePaintChannel.Emission ||
                   channel == TexturePaintChannel.SkinColorMask;
        }

        public static bool IsVector(TexturePaintChannel channel)
        {
            return IsColor(channel) || channel == TexturePaintChannel.Normal ||
                   channel == TexturePaintChannel.Custom;
        }

        public static bool IsGrayscale(TexturePaintChannel channel)
        {
            return channel == TexturePaintChannel.Metallic ||
                   channel == TexturePaintChannel.Roughness ||
                   channel == TexturePaintChannel.AmbientOcclusion ||
                   channel == TexturePaintChannel.Thickness ||
                   channel == TexturePaintChannel.DetailMask ||
                   channel == TexturePaintChannel.NormalControl;
        }

        public static bool IsAuxiliary(TexturePaintChannel channel)
        {
            return channel == TexturePaintChannel.NormalControl;
        }

        public static Color ConstrainColor(TexturePaintChannel channel, Color color)
        {
            if (!IsGrayscale(channel)) return color;
            float value = ScalarValue(color);
            return new Color(value, value, value, color.a);
        }

        public static float ScalarValue(Color color)
        {
            return Mathf.Clamp01(color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f);
        }
    }
    public enum TexturePaintSourceMode { SourceTexture, SourceOverlay, BakeOverlays }
    public enum TexturePaintBrushSource { Texture, Overlay, Color }
    public enum TexturePaintTool { Paint, Erase, Blur, Smear, Clone, Dodge, Burn, NormalTouchup, Plugin }
    public enum TexturePaintBlendMode { Normal, Multiply, Add, Subtract, Screen, Overlay }
    internal enum TexturePaintGeometrySelectorKind { None, Slot, Polygon, UVIsland }
    public enum TexturePaintTangentMode { Corner, Smooth, Broken, Custom }
    public enum TexturePaintPathMode { Stamps, Continuous, Ribbon, Filled }
    public enum TexturePaintPathOrientation { FollowPath, FixedAxis }
    public enum TexturePaintPathCap { Round, Square, Butt }
    public enum TexturePaintRibbonSide { Left, Right, Both }
    public enum TexturePaintRibbonBevelTone { Light, Dark }
    public enum TexturePaintRibbonStitchRows { Single, Double }
    public enum TexturePaintNormalConvention { OpenGL, DirectX }

    /// <summary>
    /// Resolves direct textures and sprite-sheet regions into paint-source textures. Normal inputs
    /// are canonicalized into linear OpenGL-style RGB vectors before the painting engine sees them,
    /// independent of their Unity importer or source convention.
    /// </summary>
    public static class TexturePaintSpriteSource
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly int textureId;
            private readonly int spriteId;
            private readonly TexturePaintChannel channel;
            private readonly TexturePaintNormalConvention convention;
            private readonly bool unityNormalMap;
            private readonly bool sourceSrgb;
            private readonly bool invert;
            private readonly int component;

            public CacheKey(Texture texture, Sprite sprite, TexturePaintChannel channel,
                TexturePaintNormalConvention convention, bool unityNormalMap, bool invert,
                int component)
            {
                textureId = texture != null ? texture.GetInstanceID() : 0;
                spriteId = sprite != null ? sprite.GetInstanceID() : 0;
                this.channel = channel;
                this.convention = channel == TexturePaintChannel.Normal
                    ? convention
                    : TexturePaintNormalConvention.OpenGL;
                this.unityNormalMap = unityNormalMap;
                sourceSrgb = texture != null && texture.isDataSRGB;
                this.invert = invert;
                this.component = component;
            }

            public bool Equals(CacheKey other)
                => textureId == other.textureId && spriteId == other.spriteId && channel == other.channel &&
                   convention == other.convention && unityNormalMap == other.unityNormalMap &&
                   sourceSrgb == other.sourceSrgb && invert == other.invert &&
                   component == other.component;

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = textureId;
                    hash = hash * 397 ^ spriteId;
                    hash = hash * 397 ^ (int)channel;
                    hash = hash * 397 ^ (int)convention;
                    hash = hash * 397 ^ (unityNormalMap ? 1 : 0);
                    hash = hash * 397 ^ (sourceSrgb ? 1 : 0);
                    hash = hash * 397 ^ (invert ? 1 : 0);
                    return hash * 397 ^ component;
                }
            }
        }

        private static readonly Dictionary<CacheKey, Texture2D> Cache =
            new Dictionary<CacheKey, Texture2D>();
        private static Material extractionMaterial;

        public static Texture2D Resolve(Texture2D texture, Sprite sprite)
        {
            return Resolve(texture, sprite, TexturePaintChannel.Albedo,
                TexturePaintNormalConvention.OpenGL);
        }

        public static Texture2D Resolve(Texture2D texture, Sprite sprite,
            TexturePaintChannel channel, TexturePaintNormalConvention convention)
        {
            return Resolve(texture, sprite, channel, convention, false);
        }

        public static Texture2D Resolve(Texture2D texture, Sprite sprite,
            TexturePaintChannel channel, TexturePaintNormalConvention convention, bool invert)
        {
            Texture2D source = sprite != null ? sprite.texture : texture;
            if (source == null) return null;
            // Ordinary complete color/data textures need no extraction. Normal textures do,
            // because both Unity Normal Map assets and raw RGB normal data must be converted to
            // one predictable representation before vector blending.
            if (sprite == null && channel != TexturePaintChannel.Normal &&
                !TexturePaintChannelUtility.IsGrayscale(channel) && !invert) return texture;
            return Extract(source, sprite, channel, convention, invert);
        }

        public static Texture ResolveTexture(Texture texture, TexturePaintChannel channel,
            TexturePaintNormalConvention convention, bool invert)
        {
            return ResolveTexture(texture, channel, convention, invert, false);
        }

        /// <summary>
        /// Resolves a texture while explicitly identifying a Unity/UMA packed normal source.
        /// Generated UMA normal atlases have no TextureImporter, but still use Unity's normal
        /// packing (typically Y in green and X in alpha), so importer inspection alone cannot
        /// determine how they must be decoded.
        /// </summary>
        public static Texture ResolveTexture(Texture texture, TexturePaintChannel channel,
            TexturePaintNormalConvention convention, bool invert, bool forceUnityPackedNormal)
        {
            if (texture == null) return null;
            if (texture is Texture2D texture2D)
            {
                if (!forceUnityPackedNormal)
                    return Resolve(texture2D, null, channel, convention, invert);
                return Extract(texture2D, null, channel, convention, invert, true);
            }
            if (channel != TexturePaintChannel.Normal &&
                !TexturePaintChannelUtility.IsGrayscale(channel) && !invert) return texture;
            return Extract(texture, null, channel, convention, invert, forceUnityPackedNormal);
        }

        /// <summary>Extracts one scalar component from a packed physical source.</summary>
        public static Texture ResolveTextureComponent(Texture texture, TexturePaintChannel channel,
            TexturePaintNormalConvention convention, int component, bool invert)
        {
            if (texture == null) return null;
            return Extract(texture, null, channel, convention, invert, false,
                Mathf.Clamp(component, 0, 3));
        }

        public static Texture2D GetTexture(Sprite sprite)
        {
            return Resolve(null, sprite, TexturePaintChannel.Albedo,
                TexturePaintNormalConvention.OpenGL);
        }

        public static Texture2D GetTexture(Sprite sprite, TexturePaintChannel channel,
            TexturePaintNormalConvention convention)
        {
            return Resolve(null, sprite, channel, convention);
        }

        private static Texture2D Extract(Texture source, Sprite sprite,
            TexturePaintChannel channel, TexturePaintNormalConvention convention, bool invert,
            bool forceUnityPackedNormal = false, int component = -1)
        {
            bool normal = channel == TexturePaintChannel.Normal;
            bool grayscale = TexturePaintChannelUtility.IsGrayscale(channel);
            bool unityNormalMap = normal && (forceUnityPackedNormal || IsUnityNormalMap(source));
            CacheKey key = new CacheKey(source, sprite, channel, convention, unityNormalMap, invert,
                component);
            if (Cache.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            Rect sourceRect = new Rect(0f, 0f, source.width, source.height);
            SpritePackingRotation packingRotation = SpritePackingRotation.None;
            if (sprite != null)
            {
                packingRotation = sprite.packingRotation;
                try { sourceRect = sprite.packed ? sprite.textureRect : sprite.rect; }
                catch
                {
                    Vector2[] uv = sprite.uv;
                    if (uv == null || uv.Length == 0) return null;
                    Vector2 minimum = uv[0], maximum = uv[0];
                    for (int i = 1; i < uv.Length; i++)
                    {
                        minimum = Vector2.Min(minimum, uv[i]);
                        maximum = Vector2.Max(maximum, uv[i]);
                    }
                    sourceRect = Rect.MinMaxRect(minimum.x * source.width,
                        minimum.y * source.height, maximum.x * source.width,
                        maximum.y * source.height);
                }
            }

            int width = Mathf.Max(1, Mathf.RoundToInt(sourceRect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(sourceRect.height));
            Vector2 scale = new Vector2(sourceRect.width / source.width,
                sourceRect.height / source.height);
            Vector2 offset = new Vector2(sourceRect.x / source.width,
                sourceRect.y / source.height);
            switch (packingRotation)
            {
                case SpritePackingRotation.FlipHorizontal:
                    offset.x += scale.x;
                    scale.x = -scale.x;
                    break;
                case SpritePackingRotation.FlipVertical:
                    offset.y += scale.y;
                    scale.y = -scale.y;
                    break;
                case SpritePackingRotation.Rotate180:
                    offset += scale;
                    scale = -scale;
                    break;
            }

            bool linear = normal || grayscale || component >= 0 || !source.isDataSRGB;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, linear
                    ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Material material = GetExtractionMaterial();
                if (material == null)
                {
                    Debug.LogError("Overlay Painter source extraction shader is unavailable.");
                    return null;
                }
                material.SetVector("_ScaleOffset", new Vector4(scale.x, scale.y, offset.x, offset.y));
                material.SetInt("_SourceIsNormalMap", unityNormalMap ? 1 : 0);
                material.SetInt("_SourceIsSRGB", source.isDataSRGB ? 1 : 0);
                material.SetInt("_InvertGreen",
                    normal && convention == TexturePaintNormalConvention.DirectX ? 1 : 0);
                material.SetInt("_InvertChannels", invert ? 1 : 0);
                material.SetInt("_Grayscale", grayscale ? 1 : 0);
                material.SetInt("_SourceComponent", component);
                Graphics.Blit(source, temporary, material, component >= 0 ? 1 : normal ? 2 : 0);
                RenderTexture.active = temporary;
                result = new Texture2D(width, height, TextureFormat.RGBA32, false, linear)
                {
                    name = (sprite != null ? sprite.name : source.name) +
                           (component >= 0 ? $" (Overlay Painter {channel} Component {component})" :
                            normal ? " (Overlay Painter Normal Source)" :
                            grayscale ? " (Overlay Painter Grayscale Source)" :
                               invert ? " (Overlay Painter Inverted Source)" : " (Overlay Painter Sprite)"),
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = source.filterMode,
                    wrapMode = TextureWrapMode.Clamp
                };
                result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                // Keep the cache readable so the existing CPU fallback samples the same Sprite
                // region when compute shaders are unavailable.
                result.Apply(false, false);
                Cache[key] = result;
                return result;
            }
            catch
            {
                if (result != null) DestroyTexture(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        public static void ClearCache()
        {
            foreach (Texture2D texture in Cache.Values)
                if (texture != null) DestroyTexture(texture);
            Cache.Clear();
            if (extractionMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(extractionMaterial);
                else UnityEngine.Object.DestroyImmediate(extractionMaterial);
                extractionMaterial = null;
            }
        }

        private static Material GetExtractionMaterial()
        {
            if (extractionMaterial != null) return extractionMaterial;
            Shader shader = Shader.Find("Hidden/UMA/TexturePaint/SourceExtract");
            if (shader == null) return null;
            extractionMaterial = new Material(shader)
            {
                name = "Overlay Painter Source Extract",
                hideFlags = HideFlags.HideAndDontSave
            };
            return extractionMaterial;
        }

        private static bool IsUnityNormalMap(Texture texture)
        {
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(texture);
            return !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is TextureImporter importer &&
                   importer.textureType == TextureImporterType.NormalMap;
#else
            return false;
#endif
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Serializable]
    public struct TexturePaintSurfaceAnchor
    {
        public string surfaceId;
        public int surfaceIndex;
        public int triangleIndex;
        public Vector3 barycentric;
        public Vector3 normal;
        public float normalOffset;
    }

    [Serializable]
    public struct BrushProjection
    {
        public Vector4 uvToBrush;
        public Vector3 worldTangent;
        public Vector3 worldBitangent;
        public Vector2 triangleUV0;
        public Vector2 triangleUV1;
        public Vector2 triangleUV2;
        // Bits 0..2 correspond to UV0-UV1, UV1-UV2, and UV2-UV0. Only true UV/slot
        // boundaries receive conservative texel padding; shared edges use single ownership.
        public int triangleBoundaryMask;
        public float uvBoundsRadius;
        public bool restrictToTriangle;
        public bool valid;
    }

    [Serializable]
    public struct StrokeSample
    {
        public string surfaceId;
        public Vector3 worldPosition;
        public Vector3 previousWorldPosition;
        public Vector3 worldNormal;
        public Vector3 direction;
        public Vector3 projectionDirection;
        public Vector2 uv;
        public Vector2 previousUV;
        public Vector3 barycentric;
        public int surfaceIndex;
        public int triangleIndex;
        public int uvIsland;
        public string slotName;
        public float pressure;
        public float sizeMultiplier;
        public float flowMultiplier;
        public float time;
        public float rotation;
        public float surfaceOffset;
        // Optional brush-local transforms used by ribbon slices. Zero values from documents saved
        // before these fields existed are interpreted as identity by PaintingEngine.
        public Vector2 footprintScale;
        public Vector2 sourceUVScale;
        public Vector2 sourceUVOffset;
        public Color color;
        public bool hasColor;

        public StrokeSample(Vector3 worldPosition, Vector3 worldNormal, Vector2 uv, int surfaceIndex, int triangleIndex)
        {
            this.worldPosition = worldPosition;
            previousWorldPosition = worldPosition;
            this.worldNormal = worldNormal;
            direction = Vector3.zero;
            projectionDirection = -worldNormal;
            this.uv = uv;
            previousUV = uv;
            barycentric = Vector3.zero;
            this.surfaceIndex = surfaceIndex;
            this.triangleIndex = triangleIndex;
            surfaceId = string.Empty;
            uvIsland = -1;
            slotName = string.Empty;
            pressure = 1f;
            sizeMultiplier = 1f;
            flowMultiplier = 1f;
            time = Time.realtimeSinceStartup;
            rotation = 0f;
            surfaceOffset = 0f;
            footprintScale = Vector2.one;
            sourceUVScale = Vector2.one;
            sourceUVOffset = Vector2.zero;
            color = Color.white;
            hasColor = false;
        }
    }

    public readonly struct StrokeDispatchSample
    {
        public readonly StrokeSample sample;
        public readonly float uvRadius;
        public readonly BrushProjection projection;

        public StrokeDispatchSample(StrokeSample sample, float uvRadius, BrushProjection projection)
        {
            this.sample = sample;
            this.uvRadius = uvRadius;
            this.projection = projection;
        }
    }

    /// <summary>
    /// One quad of a continuous world-space ribbon. Neighboring segments reuse the exact same
    /// start/end cross section; the ribbon projection shader therefore cannot expose the gaps
    /// produced by independently oriented rectangular stamps.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TexturePaintRibbonSegment
    {
        // xyz = world-space corner, w = continuous longitudinal source coordinate.
        public Vector4 leftStartAlong;
        // xyz = world-space corner, w = per-point flow multiplier.
        public Vector4 rightStartFlow;
        public Vector4 leftEndAlong;
        public Vector4 rightEndFlow;
        // xyz = surface normal, w = pressure.
        public Vector4 normalStartPressure;
        public Vector4 normalEndPressure;
        public Vector4 colorStart;
        public Vector4 colorEnd;
    }

    public sealed class StrokeContext
    {
        public TextureSet textures;
        internal TexturePaintGeometrySelection geometrySelection;
        /// <summary>Paint directly in normalized texture UVs without mesh projection or clipping.</summary>
        public bool directUV;
        public bool editLayerMask;
        [Range(0f, 1f)] public float maskValue = 1f;
        public BrushPreset brush;
        public TexturePaintTool tool;
        public TexturePaintChannel channel;
        public Matrix4x4 modelToWorld;
        public bool mirrorEnabled;
        public Color color = Color.white;
        public TexturePaintBrushSource paintSource = TexturePaintBrushSource.Color;
        public Texture2D sourceTexture;
        public Sprite sourceSprite;
        public OverlayDataAsset sourceOverlay;
        public TexturePaintChannel maskSourceChannel = TexturePaintChannel.Albedo;
        public bool sourceInvert;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
        public readonly Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings> channelSources =
            new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings>();
        public readonly Dictionary<string, OverlayDataAsset> sourceOverlaysBySurfaceId =
            new Dictionary<string, OverlayDataAsset>(StringComparer.Ordinal);
        public float strength = 1f;
        public bool limitStrokeCoverage;
        public bool pressureAffectsFlow = true;
        public bool pressureAffectsSize;
        public float projectionDepth;
        public float normalAngleLimit = 90f;
        public bool paintBackfaces;
        // Ribbon-local side fade. Start and size are normalized against the distance from the
        // centerline to either side edge, independent of source and destination texture UVs.
        public bool ribbonEdgeFadeEnabled;
        public float ribbonEdgeFadeStart = 0.75f;
        public float ribbonEdgeFadeSize = 1f;
        public Texture2D ribbonBeginningTexture;
        public Texture2D ribbonEndTexture;
        public TexturePaintLayerEffects ribbonEffects;
        public Vector2 cloneSourceUV;
        public PluginHost pluginHost;
        public ITexturePaintBrushV2 brushPlugin;
        public TexturePaintPluginParameterSet brushPluginParameters;
        public CancellationToken cancellationToken;
        public string historyGroupKey;
        public TexturePaintLayer replaceLayer;
        public bool replaceHistoryGroup;
        // A procedural layer's pixels are a cache derived from its editable model (for example a
        // spline). Its model-level undo owns history, so capturing full-resolution pixel undo on
        // every preview regeneration is redundant and can dominate interactive edit time.
        public bool derivedLayerRaster;

        public OverlayDataAsset ResolveSourceOverlay(TextureSet set)
        {
            if (set != null)
            {
                string key = !string.IsNullOrEmpty(set.persistentId)
                    ? set.persistentId : set.surface?.index.ToString();
                if (!string.IsNullOrEmpty(key) &&
                    sourceOverlaysBySurfaceId.TryGetValue(key, out OverlayDataAsset memberOverlay))
                    return memberOverlay;
            }
            return sourceOverlay;
        }
    }

    [Serializable]
    public sealed class TexturePaintSpline
    {
        public const int CurrentWorldCurveVersion = 1;

        public string name = "Spline";
        public bool worldSpace = true;
        // Version zero paths used surface/topology-projected Bezier controls. Those controls made a
        // world-authored curve bend at reconstructed slot and UDIM boundaries. Version one stores
        // the curve itself exclusively in world space; surface resolution happens after sampling.
        public int worldCurveVersion;
        public bool closed;
        public bool useBezier = true;
        public bool showControls = true;
        public bool smoothHandles = true;
        public List<Vector3> worldPoints = new List<Vector3>();
        public List<Vector2> uvPoints = new List<Vector2>();
        public List<Vector3> worldInControls = new List<Vector3>();
        public List<Vector3> worldOutControls = new List<Vector3>();
        public List<Vector2> uvInControls = new List<Vector2>();
        public List<Vector2> uvOutControls = new List<Vector2>();
        public List<int> surfaceIndices = new List<int>();
        public List<int> triangleIndices = new List<int>();
        public List<Vector3> worldNormals = new List<Vector3>();
        public List<float> pressures = new List<float>();
        public List<float> widths = new List<float>();
        public List<float> flows = new List<float>();
        public List<float> rolls = new List<float>();
        public List<Color> colors = new List<Color>();
        public List<float> offsets = new List<float>();
        public List<TexturePaintTangentMode> tangentModes = new List<TexturePaintTangentMode>();
        public List<TexturePaintSurfaceAnchor> anchors = new List<TexturePaintSurfaceAnchor>();

        public int PointCount => Mathf.Min(worldPoints.Count, uvPoints.Count);
        public int SegmentCount => closed && PointCount > 2 ? PointCount : Mathf.Max(0, PointCount - 1);

        public void AddPoint(Vector3 worldPosition, Vector2 uv, int surfaceIndex, int triangleIndex, Vector3 normal)
        {
            UpgradeWorldCurve();
            EnsureControlPoints();
            int previous = PointCount - 1;
            worldPoints.Add(worldPosition);
            uvPoints.Add(uv);
            surfaceIndices.Add(surfaceIndex);
            triangleIndices.Add(triangleIndex);
            worldNormals.Add(normal);
            pressures.Add(1f); widths.Add(1f); flows.Add(1f); rolls.Add(0f); colors.Add(Color.white);
            offsets.Add(0f); tangentModes.Add(TexturePaintTangentMode.Smooth);
            anchors.Add(new TexturePaintSurfaceAnchor
            {
                surfaceIndex = surfaceIndex,
                triangleIndex = triangleIndex,
                normal = normal,
                normalOffset = 0f
            });
            if (previous >= 0)
            {
                Vector3 worldDelta = worldPosition - worldPoints[previous];
                Vector2 uvDelta = uv - uvPoints[previous];
                worldOutControls[previous] = worldPoints[previous] + worldDelta / 3f;
                uvOutControls[previous] = uvPoints[previous] + uvDelta / 3f;
                worldInControls.Add(worldPosition - worldDelta / 3f);
                uvInControls.Add(uv - uvDelta / 3f);
                worldOutControls.Add(worldPosition + worldDelta / 3f);
                uvOutControls.Add(uv + uvDelta / 3f);
            }
            else
            {
                worldInControls.Add(worldPosition);
                uvInControls.Add(uv);
                worldOutControls.Add(worldPosition);
                uvOutControls.Add(uv);
            }
        }

        public void Clear()
        {
            worldPoints.Clear(); uvPoints.Clear(); worldInControls.Clear(); worldOutControls.Clear();
            uvInControls.Clear(); uvOutControls.Clear(); surfaceIndices.Clear(); triangleIndices.Clear(); worldNormals.Clear();
            pressures.Clear(); widths.Clear(); flows.Clear(); rolls.Clear(); colors.Clear();
            offsets.Clear(); tangentModes.Clear(); anchors.Clear();
            worldCurveVersion = CurrentWorldCurveVersion;
        }

        /// <summary>
        /// Converts paths authored by the former surface-projected control implementation. The
        /// conversion is deliberately one-time: after it runs, user-edited world handles are never
        /// modified by reconstruction, UV topology, slot ownership, or UDIM membership.
        /// </summary>
        public bool UpgradeWorldCurve()
        {
            if (!worldSpace || worldCurveVersion >= CurrentWorldCurveVersion) return false;
            EnsureControlPoints();
            int count = PointCount;
            int segmentCount = SegmentCount;
            for (int segment = 0; segment < segmentCount; segment++)
            {
                int next = (segment + 1) % count;
                worldOutControls[segment] = Vector3.Lerp(worldPoints[segment], worldPoints[next], 1f / 3f);
                worldInControls[next] = Vector3.Lerp(worldPoints[segment], worldPoints[next], 2f / 3f);
            }
            if (!closed && count > 0)
            {
                worldInControls[0] = worldPoints[0];
                worldOutControls[count - 1] = worldPoints[count - 1];
            }
            worldCurveVersion = CurrentWorldCurveVersion;
            return true;
        }

        public void EnsureControlPoints()
        {
            int count = PointCount;
            Resize(worldInControls, count, i => worldPoints[i]);
            Resize(worldOutControls, count, i => worldPoints[i]);
            Resize(uvInControls, count, i => uvPoints[i]);
            Resize(uvOutControls, count, i => uvPoints[i]);
            Resize(surfaceIndices, count, _ => 0);
            Resize(triangleIndices, count, _ => -1);
            Resize(worldNormals, count, _ => Vector3.up);
            Resize(pressures, count, _ => 1f); Resize(widths, count, _ => 1f); Resize(flows, count, _ => 1f);
            Resize(rolls, count, _ => 0f); Resize(colors, count, _ => Color.white);
            Resize(offsets, count, _ => 0f);
            Resize(tangentModes, count, _ => TexturePaintTangentMode.Smooth);
            Resize(anchors, count, i => new TexturePaintSurfaceAnchor
            {
                surfaceIndex = surfaceIndices[i], triangleIndex = triangleIndices[i], normal = worldNormals[i]
            });
        }

        public void SetWorldControl(int pointIndex, bool incoming, Vector3 worldControl, Vector2 uvControl)
        {
            EnsureControlPoints();
            if ((uint)pointIndex >= (uint)PointCount) return;
            bool linked = tangentModes[pointIndex] == TexturePaintTangentMode.Smooth;
            if (incoming)
            {
                worldInControls[pointIndex] = worldControl;
                uvInControls[pointIndex] = uvControl;
                if (linked)
                {
                    worldOutControls[pointIndex] = worldPoints[pointIndex] * 2f - worldControl;
                    uvOutControls[pointIndex] = uvPoints[pointIndex] * 2f - uvControl;
                }
            }
            else
            {
                worldOutControls[pointIndex] = worldControl;
                uvOutControls[pointIndex] = uvControl;
                if (linked)
                {
                    worldInControls[pointIndex] = worldPoints[pointIndex] * 2f - worldControl;
                    uvInControls[pointIndex] = uvPoints[pointIndex] * 2f - uvControl;
                }
            }
        }

        public void SetTangentMode(int pointIndex, TexturePaintTangentMode mode)
        {
            EnsureControlPoints();
            if ((uint)pointIndex >= (uint)PointCount) return;
            tangentModes[pointIndex] = mode;
            if (mode == TexturePaintTangentMode.Corner)
            {
                worldInControls[pointIndex] = worldPoints[pointIndex];
                worldOutControls[pointIndex] = worldPoints[pointIndex];
                uvInControls[pointIndex] = uvPoints[pointIndex];
                uvOutControls[pointIndex] = uvPoints[pointIndex];
            }
            else if (mode == TexturePaintTangentMode.Smooth)
            {
                Vector3 direction = worldOutControls[pointIndex] - worldInControls[pointIndex];
                Vector2 uvDirection = uvOutControls[pointIndex] - uvInControls[pointIndex];
                float length = Mathf.Max(Vector3.Distance(worldPoints[pointIndex], worldInControls[pointIndex]),
                    Vector3.Distance(worldPoints[pointIndex], worldOutControls[pointIndex]));
                float uvLength = Mathf.Max(Vector2.Distance(uvPoints[pointIndex], uvInControls[pointIndex]),
                    Vector2.Distance(uvPoints[pointIndex], uvOutControls[pointIndex]));
                direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.right;
                uvDirection = uvDirection.sqrMagnitude > 0.000001f ? uvDirection.normalized : Vector2.right;
                worldInControls[pointIndex] = worldPoints[pointIndex] - direction * length;
                worldOutControls[pointIndex] = worldPoints[pointIndex] + direction * length;
                uvInControls[pointIndex] = uvPoints[pointIndex] - uvDirection * uvLength;
                uvOutControls[pointIndex] = uvPoints[pointIndex] + uvDirection * uvLength;
            }
        }

        public int InsertPointAfter(int pointIndex)
        {
            return InsertPointAfter(pointIndex, 0.5f);
        }

        public int InsertPointAfter(int pointIndex, float segmentT)
        {
            EnsureControlPoints();
            if ((uint)pointIndex >= (uint)PointCount || PointCount == 0) return -1;
            float t = Mathf.Clamp(segmentT, 0.001f, 0.999f);
            int next = pointIndex + 1;
            if (next >= PointCount)
            {
                if (!closed) return -1;
                next = 0;
            }
            Vector3 worldA = Vector3.Lerp(worldPoints[pointIndex], worldOutControls[pointIndex], t);
            Vector3 worldB = Vector3.Lerp(worldOutControls[pointIndex], worldInControls[next], t);
            Vector3 worldC = Vector3.Lerp(worldInControls[next], worldPoints[next], t);
            Vector3 worldD = Vector3.Lerp(worldA, worldB, t);
            Vector3 worldE = Vector3.Lerp(worldB, worldC, t);
            Vector3 worldPoint = Vector3.Lerp(worldD, worldE, t);
            Vector2 uvA = Vector2.Lerp(uvPoints[pointIndex], uvOutControls[pointIndex], t);
            Vector2 uvB = Vector2.Lerp(uvOutControls[pointIndex], uvInControls[next], t);
            Vector2 uvC = Vector2.Lerp(uvInControls[next], uvPoints[next], t);
            Vector2 uvD = Vector2.Lerp(uvA, uvB, t);
            Vector2 uvE = Vector2.Lerp(uvB, uvC, t);
            Vector2 uvPoint = Vector2.Lerp(uvD, uvE, t);
            worldOutControls[pointIndex] = worldA;
            uvOutControls[pointIndex] = uvA;
            worldInControls[next] = worldC;
            uvInControls[next] = uvC;
            int insertIndex = next == 0 ? PointCount : next;
            worldPoints.Insert(insertIndex, worldPoint);
            uvPoints.Insert(insertIndex, uvPoint);
            worldInControls.Insert(insertIndex, worldD);
            worldOutControls.Insert(insertIndex, worldE);
            uvInControls.Insert(insertIndex, uvD);
            uvOutControls.Insert(insertIndex, uvE);
            surfaceIndices.Insert(insertIndex, surfaceIndices[pointIndex]);
            triangleIndices.Insert(insertIndex, triangleIndices[pointIndex]);
            worldNormals.Insert(insertIndex, Vector3.Slerp(worldNormals[pointIndex], worldNormals[next], t).normalized);
            pressures.Insert(insertIndex, Mathf.Lerp(pressures[pointIndex], pressures[next], t));
            widths.Insert(insertIndex, Mathf.Lerp(widths[pointIndex], widths[next], t));
            flows.Insert(insertIndex, Mathf.Lerp(flows[pointIndex], flows[next], t));
            rolls.Insert(insertIndex, Mathf.LerpAngle(rolls[pointIndex], rolls[next], t));
            colors.Insert(insertIndex, Color.Lerp(colors[pointIndex], colors[next], t));
            offsets.Insert(insertIndex, Mathf.Lerp(offsets[pointIndex], offsets[next], t));
            tangentModes.Insert(insertIndex, TexturePaintTangentMode.Smooth);
            TexturePaintSurfaceAnchor anchor = t < 0.5f ? anchors[pointIndex] : anchors[next];
            anchor.normal = Vector3.Slerp(anchors[pointIndex].normal, anchors[next].normal, t).normalized;
            anchors.Insert(insertIndex, anchor);
            return insertIndex;
        }

        public bool RemovePoint(int pointIndex)
        {
            EnsureControlPoints();
            if ((uint)pointIndex >= (uint)PointCount) return false;
            worldPoints.RemoveAt(pointIndex); uvPoints.RemoveAt(pointIndex);
            worldInControls.RemoveAt(pointIndex); worldOutControls.RemoveAt(pointIndex);
            uvInControls.RemoveAt(pointIndex); uvOutControls.RemoveAt(pointIndex);
            surfaceIndices.RemoveAt(pointIndex); triangleIndices.RemoveAt(pointIndex); worldNormals.RemoveAt(pointIndex);
            pressures.RemoveAt(pointIndex); widths.RemoveAt(pointIndex); flows.RemoveAt(pointIndex);
            rolls.RemoveAt(pointIndex); colors.RemoveAt(pointIndex);
            offsets.RemoveAt(pointIndex); tangentModes.RemoveAt(pointIndex); anchors.RemoveAt(pointIndex);
            return true;
        }

        public void Reverse()
        {
            EnsureControlPoints();
            worldPoints.Reverse(); uvPoints.Reverse(); surfaceIndices.Reverse(); triangleIndices.Reverse(); worldNormals.Reverse();
            pressures.Reverse(); widths.Reverse(); flows.Reverse(); rolls.Reverse(); colors.Reverse();
            offsets.Reverse(); tangentModes.Reverse(); anchors.Reverse();
            worldInControls.Reverse(); worldOutControls.Reverse(); uvInControls.Reverse(); uvOutControls.Reverse();
            (worldInControls, worldOutControls) = (worldOutControls, worldInControls);
            (uvInControls, uvOutControls) = (uvOutControls, uvInControls);
        }

        public List<StrokeSample> Sample(float spacing, int surfaceIndex = 0)
        {
            EnsureControlPoints();
            List<StrokeSample> result = new List<StrokeSample>();
            int count = PointCount;
            if (count == 0) return result;
            if (count == 1)
            {
                result.Add(new StrokeSample(worldPoints[0], worldNormals[0], uvPoints[0], surfaceIndices[0], triangleIndices[0]));
                return result;
            }

            WorldSpaceStrokeSampler sampler = new WorldSpaceStrokeSampler
            {
                Spacing = Mathf.Max(0.0001f, spacing),
                DirectionSmoothing = 0.25f
            };
            int segmentCount = SegmentCount;
            for (int i = 0; i < segmentCount; i++)
            {
                int j = (i + 1) % count;
                List<float> parameters = new List<float> { 0f };
                EvaluateSegment(i, j, 0f, out Vector3 segmentStart, out _);
                EvaluateSegment(i, j, 1f, out Vector3 segmentEnd, out _);
                TessellateAdaptive(i, j, 0f, segmentStart, 1f, segmentEnd,
                    Mathf.Max(0.00001f, spacing * 0.1f), 0, parameters);
                int firstParameter = i == 0 ? 0 : 1;
                for (int parameterIndex = firstParameter; parameterIndex < parameters.Count; parameterIndex++)
                {
                    float t = parameters[parameterIndex];
                    EvaluateSegment(i, j, t, out Vector3 wp, out Vector2 uv);
                    int anchor = t < 0.5f ? i : j;
                    int sampleSurface = anchor < surfaceIndices.Count ? surfaceIndices[anchor] : surfaceIndex;
                    int triangle = anchor < triangleIndices.Count ? triangleIndices[anchor] : -1;
                    Vector3 normal = Vector3.Slerp(worldNormals[i], worldNormals[j], t).normalized;
                    float normalOffset = Mathf.Lerp(offsets[i], offsets[j], t);
                    wp += normal * normalOffset;
                    StrokeSample pathSample = new StrokeSample(wp, normal, uv, sampleSurface, triangle)
                    {
                        pressure = Mathf.Lerp(pressures[i], pressures[j], t),
                        sizeMultiplier = Mathf.Lerp(widths[i], widths[j], t),
                        flowMultiplier = Mathf.Lerp(flows[i], flows[j], t),
                        rotation = Mathf.LerpAngle(rolls[i], rolls[j], t),
                        surfaceOffset = normalOffset,
                        color = Color.Lerp(colors[i], colors[j], t),
                        hasColor = true
                    };
                    sampler.Add(pathSample, result);
                }
            }
            if (!closed) sampler.Flush(result);
            return result;
        }

        /// <summary>
        /// Samples one complete ribbon tile per fitted path interval. Tile centers sit at the
        /// midpoint of each interval instead of at every continuous-stroke deposit, so a source
        /// texture is reproduced once per tile and adjacent source edges meet without a duplicate
        /// stamp being placed on the final endpoint.
        /// </summary>
        public List<StrokeSample> SampleRibbon(float nominalTileLength, int surfaceIndex = 0)
            => SampleRibbonSlices(nominalTileLength, 1, false, false, surfaceIndex);

        /// <summary>
        /// Subdivides each complete ribbon image longitudinally. Every slice samples only its own
        /// contiguous portion of the image, allowing its narrow footprint to turn with the path
        /// while retaining one uninterrupted source image and a smooth outer ribbon edge.
        /// </summary>
        public List<StrokeSample> SampleRibbonSlices(float nominalTileLength, int slicesPerTile,
            bool sourceAlongY, bool reverseSourceAxis, int surfaceIndex = 0)
        {
            float requestedLength = Mathf.Max(0.0001f, nominalTileLength);
            int subdivisions = Mathf.Clamp(slicesPerTile, 1, 32);
            // Use a denser regular sampling only as the canonical polyline from which exact ribbon
            // centers are extracted. Ten subdivisions per requested tile keeps curved-path length
            // fitting stable without coupling ribbon density to the normal stroke spacing setting.
            List<StrokeSample> polyline = Sample(Mathf.Max(0.0001f, requestedLength * 0.1f), surfaceIndex);
            if (polyline.Count <= 1) return polyline;

            int polylineSegmentCount = closed ? polyline.Count : polyline.Count - 1;
            float totalLength = 0f;
            for (int i = 0; i < polylineSegmentCount; i++)
                totalLength += Vector3.Distance(polyline[i].worldPosition,
                    polyline[(i + 1) % polyline.Count].worldPosition);
            if (totalLength <= 0.000001f) return new List<StrokeSample> { polyline[0] };

            int tileCount = Mathf.Max(1, Mathf.RoundToInt(totalLength / requestedLength));
            float fittedTileLength = totalLength / tileCount;
            float fittedSize = fittedTileLength / requestedLength;
            int sliceCount = tileCount * subdivisions;
            float sliceLength = fittedTileLength / subdivisions;
            float sliceScale = 1f / subdivisions;
            List<StrokeSample> result = new List<StrokeSample>(sliceCount);
            int segmentIndex = 0;
            float segmentStartDistance = 0f;
            for (int sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
            {
                float targetDistance = (sliceIndex + 0.5f) * sliceLength;
                while (segmentIndex < polylineSegmentCount - 1)
                {
                    int next = (segmentIndex + 1) % polyline.Count;
                    float segmentLength = Vector3.Distance(polyline[segmentIndex].worldPosition,
                        polyline[next].worldPosition);
                    if (targetDistance <= segmentStartDistance + segmentLength) break;
                    segmentStartDistance += segmentLength;
                    segmentIndex++;
                }

                int nextIndex = (segmentIndex + 1) % polyline.Count;
                float currentLength = Vector3.Distance(polyline[segmentIndex].worldPosition,
                    polyline[nextIndex].worldPosition);
                float t = currentLength > 0.000001f
                    ? Mathf.Clamp01((targetDistance - segmentStartDistance) / currentLength)
                    : 0f;
                StrokeSample tile = WorldSpaceStrokeSampler.Interpolate(
                    polyline[segmentIndex], polyline[nextIndex], t);
                // Per-point width remains authoritative; the small global fit merely makes the
                // integer number of complete tiles end exactly at the two path ends.
                tile.sizeMultiplier *= fittedSize;
                if (subdivisions > 1)
                {
                    int sourceSlice = sliceIndex % subdivisions;
                    tile.footprintScale = sourceAlongY
                        ? new Vector2(1f, sliceScale) : new Vector2(sliceScale, 1f);
                    float sourceScale = reverseSourceAxis ? -sliceScale : sliceScale;
                    float sourceOffset = reverseSourceAxis
                        ? (sourceSlice + 1f) * sliceScale : sourceSlice * sliceScale;
                    tile.sourceUVScale = sourceAlongY
                        ? new Vector2(1f, sourceScale) : new Vector2(sourceScale, 1f);
                    tile.sourceUVOffset = sourceAlongY
                        ? new Vector2(0f, sourceOffset) : new Vector2(sourceOffset, 0f);
                }
                result.Add(tile);
            }

            for (int i = 0; i < result.Count; i++)
            {
                StrokeSample tile = result[i];
                int previous = closed ? (i + result.Count - 1) % result.Count : Mathf.Max(0, i - 1);
                int next = closed ? (i + 1) % result.Count : Mathf.Min(result.Count - 1, i + 1);
                Vector3 tangent = result[next].worldPosition - result[previous].worldPosition;
                if (tangent.sqrMagnitude > 0.00000001f) tile.direction = tangent.normalized;
                tile.previousWorldPosition = i > 0 ? result[i - 1].worldPosition : tile.worldPosition;
                tile.previousUV = i > 0 ? result[i - 1].uv : tile.uv;
                result[i] = tile;
            }
            return result;
        }

        private void TessellateAdaptive(int from, int to, float t0, Vector3 p0, float t1, Vector3 p1,
            float tolerance, int depth, List<float> parameters)
        {
            float midpointT = (t0 + t1) * 0.5f;
            EvaluateSegment(from, to, midpointT, out Vector3 midpoint, out _);
            EvaluateSegment(from, to, Mathf.Lerp(t0, t1, 0.25f), out Vector3 quarter, out _);
            EvaluateSegment(from, to, Mathf.Lerp(t0, t1, 0.75f), out Vector3 threeQuarter, out _);
            float deviation = Mathf.Max(Vector3.Distance(midpoint, Vector3.Lerp(p0, p1, 0.5f)),
                Mathf.Max(Vector3.Distance(quarter, Vector3.Lerp(p0, p1, 0.25f)),
                    Vector3.Distance(threeQuarter, Vector3.Lerp(p0, p1, 0.75f))));
            if (depth >= 10 || deviation <= tolerance)
            {
                parameters.Add(t1);
                return;
            }
            TessellateAdaptive(from, to, t0, p0, midpointT, midpoint, tolerance, depth + 1, parameters);
            TessellateAdaptive(from, to, midpointT, midpoint, t1, p1, tolerance, depth + 1, parameters);
        }

        public Vector3[] GetDisplayWorldPoints(int subdivisionsPerSegment = 24)
        {
            EnsureControlPoints();
            if (PointCount == 0) return Array.Empty<Vector3>();
            if (PointCount == 1) return new[] { worldPoints[0] };
            int segmentCount = SegmentCount;
            List<Vector3> result = new List<Vector3>(segmentCount * subdivisionsPerSegment + 1);
            for (int segment = 0; segment < segmentCount; segment++)
            {
                int next = (segment + 1) % PointCount;
                for (int step = 0; step < subdivisionsPerSegment; step++)
                {
                    EvaluateSegment(segment, next, step / (float)subdivisionsPerSegment, out Vector3 world, out _);
                    result.Add(world);
                }
            }
            EvaluateSegment(segmentCount - 1, closed ? 0 : PointCount - 1, 1f, out Vector3 end, out _);
            result.Add(end);
            return result.ToArray();
        }

        public void EvaluateSegment(int from, int to, float t, out Vector3 world, out Vector2 uv)
        {
            if (!useBezier)
            {
                world = Vector3.Lerp(worldPoints[from], worldPoints[to], t);
                uv = Vector2.Lerp(uvPoints[from], uvPoints[to], t);
                return;
            }
            world = Cubic(worldPoints[from], worldOutControls[from], worldInControls[to], worldPoints[to], t);
            uv = Cubic(uvPoints[from], uvOutControls[from], uvInControls[to], uvPoints[to], t);
        }

        private float EstimateSegmentLength(int from, int to)
        {
            if (!useBezier)
                return worldSpace ? Vector3.Distance(worldPoints[from], worldPoints[to]) : Vector2.Distance(uvPoints[from], uvPoints[to]);
            if (worldSpace)
                return Vector3.Distance(worldPoints[from], worldOutControls[from]) +
                    Vector3.Distance(worldOutControls[from], worldInControls[to]) +
                    Vector3.Distance(worldInControls[to], worldPoints[to]);
            return Vector2.Distance(uvPoints[from], uvOutControls[from]) +
                Vector2.Distance(uvOutControls[from], uvInControls[to]) +
                Vector2.Distance(uvInControls[to], uvPoints[to]);
        }

        private static Vector3 Cubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * oneMinusT * a + 3f * oneMinusT * oneMinusT * t * b +
                3f * oneMinusT * t * t * c + t * t * t * d;
        }

        private static Vector2 Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * oneMinusT * a + 3f * oneMinusT * oneMinusT * t * b +
                3f * oneMinusT * t * t * c + t * t * t * d;
        }

        private static void Resize<T>(List<T> list, int count, Func<int, T> factory)
        {
            while (list.Count < count) list.Add(factory(list.Count));
            if (list.Count > count) list.RemoveRange(count, list.Count - count);
        }
    }

    public static class TexturePaintMath
    {
        public static float ConsumeStrokeCoverage(float requestedCoverage, ref float accumulatedCoverage)
            => ConsumeStrokeCoverage(requestedCoverage, 1f, ref accumulatedCoverage);

        public static float ConsumeStrokeCoverage(float requestedCoverage, float maximumCoverage, ref float accumulatedCoverage)
        {
            float remaining = Mathf.Clamp01(maximumCoverage) - Mathf.Clamp01(accumulatedCoverage);
            float contribution = Mathf.Min(Mathf.Clamp01(requestedCoverage), remaining);
            contribution = Mathf.Max(0f, contribution);
            accumulatedCoverage = Mathf.Clamp01(accumulatedCoverage + contribution);
            return contribution;
        }

        public static float SourceOverAlpha(float destinationAlpha, float effectiveSourceAlpha)
        {
            float source = Mathf.Clamp01(effectiveSourceAlpha);
            return source + Mathf.Clamp01(destinationAlpha) * (1f - source);
        }

        public static Vector2 BarycentricToUV(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector3 barycentric)
        {
            return uv0 * barycentric.x + uv1 * barycentric.y + uv2 * barycentric.z;
        }

        public static Vector3 MirrorAcrossGlobalX(Vector3 point)
        {
            point.x = -point.x;
            return point;
        }

        public static Vector3 MirrorDirectionAcrossGlobalX(Vector3 direction)
        {
            direction.x = -direction.x;
            return direction;
        }

        public static Vector3 BendNormalTowardVertexNormal(
            Vector3 tangentSpaceNormal, Vector3 vertexNormal, Vector4 tangent, float strength)
        {
            Vector3 n = vertexNormal.sqrMagnitude > 0f ? vertexNormal.normalized : Vector3.forward;
            Vector3 t = tangent.sqrMagnitude > 0f ? new Vector3(tangent.x, tangent.y, tangent.z).normalized : Vector3.right;
            t = (t - n * Vector3.Dot(n, t)).normalized;
            Vector3 b = Vector3.Cross(n, t) * (Mathf.Approximately(tangent.w, 0f) ? 1f : tangent.w);
            Vector3 model = (tangentSpaceNormal.x * t + tangentSpaceNormal.y * b + tangentSpaceNormal.z * n).normalized;
            Vector3 bent = Vector3.Slerp(model, n, Mathf.Clamp01(strength)).normalized;
            return new Vector3(Vector3.Dot(bent, t), Vector3.Dot(bent, b), Vector3.Dot(bent, n)).normalized;
        }

        public static RectInt BrushPixelRect(Vector2 uv, float uvRadius, int width, int height, int padding = 1)
        {
            int xMin = Mathf.FloorToInt((uv.x - uvRadius) * width) - padding;
            int yMin = Mathf.FloorToInt((uv.y - uvRadius) * height) - padding;
            int xMax = Mathf.CeilToInt((uv.x + uvRadius) * width) + padding;
            int yMax = Mathf.CeilToInt((uv.y + uvRadius) * height) + padding;
            xMin = Mathf.Clamp(xMin, 0, width);
            yMin = Mathf.Clamp(yMin, 0, height);
            xMax = Mathf.Clamp(xMax, xMin, width);
            yMax = Mathf.Clamp(yMax, yMin, height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
}
