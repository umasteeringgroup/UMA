using UnityEngine;
using System;
using UnityEngine.Serialization;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>
    /// UMA wrapper for Unity material.
    /// </summary>
    public class UMAMaterial : ScriptableObject
    {
        [Serializable]
        public class ShaderParms
        {
            public string ParameterName;
            public string ColorName;
        }

        public enum CompressionSettings { None, Fast, HighQuality };

        [SerializeField]
        [FormerlySerializedAs("material")]
        private Material _material;
        [SerializeField]
        private Material _HDRPMaterial;

        [SerializeField]
        [FormerlySerializedAs("secondPass")]
        private Material _secondPass;
        [SerializeField]
        private Material _HDRPSecondPass;

        public void Awake()
        {
        }

        private void OnValidate()
        {
            EnsureSupportedChannelTextureFormats(channels);
        }

        private bool checkedHDRPResult = false;

        private bool isHDRP
        {
            get
            {
                checkedHDRPResult = GraphicsSettings.currentRenderPipeline != null && GraphicsSettings.currentRenderPipeline.GetType().ToString().Contains("HDRenderPipelineAsset");
                return checkedHDRPResult;
            }
        }

        private string _thisName;
        public string objectName
        {
            get
            {
                if (string.IsNullOrEmpty(_thisName))
                {
                    _thisName = name;
                }
                return _thisName;
            }
            set
            {
                _thisName = value;
            }

        }
        public Material  material
        {
            get 
            {
                if (isHDRP && _HDRPMaterial != null)
                {
                    return _HDRPMaterial;
                }
                return _material;
            }
            set { _material = value; }
        }

        public Material secondPass
        {

            get
            {
                if (isHDRP && _HDRPSecondPass != null)
                {
                    return _HDRPSecondPass;
                }
                return _secondPass;
            }
        }


        public MaterialType materialType = MaterialType.Atlas;
        public MaterialChannel[] channels = new MaterialChannel[0];

        public bool generateMipMaps = true;

        [Range(-2.0f, 2.0f)]
        public float MipMapBias = 0.0f;
        [Range(1, 16)]
        public int AnisoLevel = 1;
        public FilterMode MatFilterMode = FilterMode.Bilinear;
        public CompressionSettings Compression = CompressionSettings.None;


        [Tooltip("(legacy)Shader parms can be used to pass colors to shaders. Each entry represents a parameter name and a color name. If neither exists, it is ignored.")]
        public ShaderParms[] shaderParms;

        [Tooltip("Shader keywords for use when copying the material. Editor only. Used when creating SharedColor variants in tables.")]
        public List<string> shaderKeywords = new List<string>();

        [Tooltip("If this is checked, the currently assigned color will be used as the background color so edges aren't darkened.")]
        public bool MaskWithCurrentColor;
        [Tooltip("The current color is multiplied by this color to determine the masking color when 'MaskWithCurrentColor' is checked.")]
        public Color maskMultiplier = Color.white;

        [Tooltip("Used by addressables when stripping materials")]
        public string MaterialName;
        [Tooltip("Used by addressables when stripping materials")]
        public string ShaderName;

        public enum MaterialType
        {
            Atlas = 1, 
            NoAtlas = 2,
            UseExistingMaterial = 4,
            UseExistingTextures = 8
        }

        public enum ChannelType
        {
            Texture = 0,
            NormalMap = 1,
            MaterialColor = 2,
            TintedTexture = 3,
            DiffuseTexture = 4,
            DetailNormalMap = 5,
        }

#if UNITY_EDITOR
        /// <summary>
        /// Describes how a shader consumes an individual component of a material texture.
        /// This authoring metadata is editor-only and is used by tools such as the Texture
        /// Modifications stage. Flags are used because a component can have more than one
        /// purpose (for example, base-map alpha can drive opacity and smoothness).
        /// </summary>
        [Flags]
        public enum TextureChannelUsage
        {
            Unused = 0,
            Albedo = 1 << 0,
            Normal = 1 << 1,
            Metallic = 1 << 2,
            Smoothness = 1 << 3,
            Roughness = 1 << 4,
            AmbientOcclusion = 1 << 5,
            Emission = 1 << 6,
            Opacity = 1 << 7,
            Specular = 1 << 8,
            DetailMask = 1 << 9,
            Height = 1 << 10,
            Thickness = 1 << 11,
            Custom = 1 << 12,
            DetailAlbedo = 1 << 13,
            DetailNormalX = 1 << 14,
            DetailNormalY = 1 << 15,
            DetailSmoothness = 1 << 16
        }

        public enum TextureChannelLayoutMode
        {
            /// <summary>Infer the component layout from the UMA channel type, property name, and material.</summary>
            Automatic = 0,
            /// <summary>Use the component layout serialized on the UMA Material.</summary>
            Custom = 1
        }

        public enum TextureChannelOutputMode
        {
            /// <summary>Infer encoding and importer behavior from the effective channel layout.</summary>
            Automatic = 0,
            /// <summary>Use the output and importer settings serialized on this material channel.</summary>
            Custom = 1
        }

        public enum TextureChannelOutputEncoding
        {
            Png8 = 0,
            Png16 = 1,
            ExrHalf = 2
        }

        public enum TextureChannelImporterType
        {
            Default = 0,
            NormalMap = 1
        }

        public enum TextureChannelColorSpace
        {
            Linear = 0,
            SRGB = 1
        }

        public enum TextureChannelAlphaSource
        {
            None = 0,
            FromInput = 1
        }

        public enum TextureChannelImportCompression
        {
            Uncompressed = 0,
            Compressed = 1,
            HighQuality = 2
        }

        public enum TextureChannelNormalConvention
        {
            OpenGL = 0,
            DirectX = 1
        }

        [Serializable]
        public struct TextureChannelPlatformOverrideSettings
        {
            public bool enabled;
            [Tooltip("Unity TextureImporter platform name, for example Standalone, Android, iPhone, or WebGL.")]
            public string platformName;
            [Range(32, 16384)] public int maxTextureSize;
            public TextureChannelImportCompression compression;
        }

        /// <summary>
        /// Editor-only RGBA layout used to unpack a physical shader texture into logical
        /// authoring channels and repack it for preview/export.
        /// </summary>
        [Serializable]
        public struct TextureChannelLayout
        {
            public TextureChannelLayoutMode mode;
            public TextureChannelUsage red;
            public TextureChannelUsage green;
            public TextureChannelUsage blue;
            public TextureChannelUsage alpha;

            public TextureChannelUsage GetComponent(int component)
            {
                switch (component)
                {
                    case 0: return red;
                    case 1: return green;
                    case 2: return blue;
                    case 3: return alpha;
                    default: return TextureChannelUsage.Unused;
                }
            }

            public bool Uses(TextureChannelUsage usage)
            {
                return (red & usage) != 0 || (green & usage) != 0 ||
                       (blue & usage) != 0 || (alpha & usage) != 0;
            }
        }

        /// <summary>
        /// Editor-only output and importer contract for the physical texture produced by a
        /// material channel. Automatic values are recomputed from the effective RGBA layout;
        /// Custom values are serialized for custom shaders and unusual production pipelines.
        /// </summary>
        [Serializable]
        public struct TextureChannelOutputSettings
        {
            public TextureChannelOutputMode mode;
            public TextureChannelOutputEncoding encoding;
            public TextureChannelImporterType importerType;
            public TextureChannelColorSpace colorSpace;
            public TextureChannelAlphaSource alphaSource;
            public TextureChannelImportCompression compression;
            public TextureChannelNormalConvention normalConvention;
            public bool generateMipMaps;
            public FilterMode filterMode;
            [Range(1, 16)] public int anisoLevel;
            [Range(32, 16384)] public int maxTextureSize;
            public TextureChannelPlatformOverrideSettings[] platformOverrides;
        }
#endif

		//The ChannelTypes index into this for it's corresponding background color.
		//Needed to have normalMaps have a grey background for proper blending
		static Color[] ChannelBackground =
		{
			new Color(0,0,0,0),
			Color.grey,
			new Color(0,0,0,0),
			new Color(0,0,0,0),
			new Color(0,0,0,0),
			new Color(0,0,0,0)
		};

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void StaticInitializeOnLoad()
        {
            ChannelBackground = new Color[]
            {
                new Color(0,0,0,0), // Texture
                Color.grey,        // NormalMap
                new Color(0,0,0,0), // MaterialColor
                new Color(0,0,0,0), // TintedTexture
                new Color(0,0,0,0), // DiffuseTexture
                new Color(0,0,0,0)  // DetailNormalMap
            };
        }

        static public Color GetBackgroundColor(ChannelType channelType)
		{
			return ChannelBackground[(int)channelType];
		}

        private static readonly RenderTextureFormat[] SupportedChannelTextureFormats = new RenderTextureFormat[]
        {
            RenderTextureFormat.ARGB32,
            RenderTextureFormat.RG16,
            RenderTextureFormat.R8,
            RenderTextureFormat.ARGB1555,
            RenderTextureFormat.RGB565
        };

        public static RenderTextureFormat DefaultChannelTextureFormat
        {
            get
            {
                return RenderTextureFormat.ARGB32;
            }
        }

        public static RenderTextureFormat[] GetSupportedChannelTextureFormats()
        {
            RenderTextureFormat[] formats = new RenderTextureFormat[SupportedChannelTextureFormats.Length];
            Array.Copy(SupportedChannelTextureFormats, formats, SupportedChannelTextureFormats.Length);
            return formats;
        }

        public static bool IsSupportedChannelTextureFormat(RenderTextureFormat format)
        {
            for (int i = 0; i < SupportedChannelTextureFormats.Length; i++)
            {
                if (SupportedChannelTextureFormats[i] == format)
                {
                    return true;
                }
            }
            return false;
        }

        public static RenderTextureFormat GetCompatibleChannelTextureFormat(RenderTextureFormat format)
        {
            if (IsSupportedChannelTextureFormat(format))
            {
                if (SystemInfo.SupportsRenderTextureFormat(format))
                {
                    return format;
                }
            }

            return DefaultChannelTextureFormat;
        }

        public static bool EnsureSupportedChannelTextureFormats(MaterialChannel[] materialChannels)
        {
            if (materialChannels == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < materialChannels.Length; i++)
            {
                if (!IsSupportedChannelTextureFormat(materialChannels[i].textureFormat))
                {
                    materialChannels[i].textureFormat = DefaultChannelTextureFormat;
                    changed = true;
                }
            }

            return changed;
        }



        [Serializable]
        public struct MaterialChannel
        {
            public ChannelType channelType;
            public RenderTextureFormat textureFormat;
            public string materialPropertyName;
			public string sourceTextureName;
            public CompressionSettings Compression;
            [Range(1,128)]
            public int DownSample;
            public bool ConvertRenderTexture;
            public bool UseExistingTextureForChannel;
            public bool NonShaderTexture;
#if UNITY_EDITOR
            [Tooltip("Describes the RGBA contents of this physical texture for editor authoring tools. Automatic follows known Unity/UMA shader conventions; Custom is fully editable in the UMA Material inspector.")]
            public TextureChannelLayout textureChannelLayout;
            [Tooltip("Controls the encoded file and TextureImporter settings produced by Overlay Painter. Automatic follows the effective channel layout; Custom is fully editable.")]
            public TextureChannelOutputSettings textureChannelOutput;
#endif
       }

#if UNITY_EDITOR
        /// <summary>
        /// Returns the serialized custom layout or an automatically inferred layout for an UMA material channel.
        /// </summary>
        public static TextureChannelLayout GetTextureChannelLayout(MaterialChannel channel, Material sourceMaterial)
        {
            return channel.textureChannelLayout.mode == TextureChannelLayoutMode.Custom
                ? channel.textureChannelLayout
                : InferTextureChannelLayout(channel, sourceMaterial);
        }

        /// <summary>Returns the serialized custom output contract or its deterministic automatic equivalent.</summary>
        public static TextureChannelOutputSettings GetTextureChannelOutputSettings(MaterialChannel channel,
            Material sourceMaterial, UMAMaterial owner)
        {
            return channel.textureChannelOutput.mode == TextureChannelOutputMode.Custom
                ? NormalizeTextureChannelOutputSettings(channel.textureChannelOutput, owner)
                : InferTextureChannelOutputSettings(channel, sourceMaterial, owner);
        }

        /// <summary>Infers physical file and importer behavior from the effective RGBA semantics.</summary>
        public static TextureChannelOutputSettings InferTextureChannelOutputSettings(MaterialChannel channel,
            Material sourceMaterial, UMAMaterial owner)
        {
            TextureChannelLayout layout = GetTextureChannelLayout(channel, sourceMaterial);
            TextureChannelUsage rgb = layout.red | layout.green | layout.blue;
            bool colorData = (rgb & (TextureChannelUsage.Albedo | TextureChannelUsage.Emission |
                                     TextureChannelUsage.DetailAlbedo)) != 0;
            bool ordinaryNormal = layout.red == TextureChannelUsage.Normal &&
                                  layout.green == TextureChannelUsage.Normal &&
                                  layout.blue == TextureChannelUsage.Normal &&
                                  (layout.alpha == TextureChannelUsage.Unused ||
                                   layout.alpha == TextureChannelUsage.Opacity);
            TextureChannelImportCompression compression = TextureChannelImportCompression.Uncompressed;
            if (owner != null && owner.Compression == CompressionSettings.Fast)
                compression = TextureChannelImportCompression.Compressed;
            else if (owner != null && owner.Compression == CompressionSettings.HighQuality)
                compression = TextureChannelImportCompression.HighQuality;

            return new TextureChannelOutputSettings
            {
                mode = TextureChannelOutputMode.Automatic,
                encoding = TextureChannelOutputEncoding.Png8,
                importerType = ordinaryNormal ? TextureChannelImporterType.NormalMap :
                    TextureChannelImporterType.Default,
                colorSpace = colorData && !EqualsProperty(channel.materialPropertyName, "_DetailMap")
                    ? TextureChannelColorSpace.SRGB
                    : TextureChannelColorSpace.Linear,
                alphaSource = layout.alpha == TextureChannelUsage.Unused
                    ? TextureChannelAlphaSource.None
                    : TextureChannelAlphaSource.FromInput,
                compression = compression,
                normalConvention = TextureChannelNormalConvention.OpenGL,
                generateMipMaps = owner == null || owner.generateMipMaps,
                filterMode = owner != null ? owner.MatFilterMode : FilterMode.Bilinear,
                anisoLevel = Mathf.Clamp(owner != null ? owner.AnisoLevel : 1, 1, 16),
                maxTextureSize = 8192,
                platformOverrides = Array.Empty<TextureChannelPlatformOverrideSettings>()
            };
        }

        private static TextureChannelOutputSettings NormalizeTextureChannelOutputSettings(
            TextureChannelOutputSettings settings, UMAMaterial owner)
        {
            settings.mode = TextureChannelOutputMode.Custom;
            settings.anisoLevel = Mathf.Clamp(settings.anisoLevel <= 0
                ? owner != null ? owner.AnisoLevel : 1
                : settings.anisoLevel, 1, 16);
            settings.maxTextureSize = Mathf.Clamp(settings.maxTextureSize <= 0 ? 8192 :
                settings.maxTextureSize, 32, 16384);
            TextureChannelPlatformOverrideSettings[] serializedOverrides = settings.platformOverrides;
            if (serializedOverrides == null || serializedOverrides.Length == 0)
                settings.platformOverrides = Array.Empty<TextureChannelPlatformOverrideSettings>();
            else
                settings.platformOverrides = (TextureChannelPlatformOverrideSettings[])serializedOverrides.Clone();
            for (int i = 0; i < settings.platformOverrides.Length; i++)
            {
                TextureChannelPlatformOverrideSettings platform = settings.platformOverrides[i];
                platform.platformName = (platform.platformName ?? string.Empty).Trim();
                platform.maxTextureSize = Mathf.Clamp(platform.maxTextureSize <= 0
                    ? settings.maxTextureSize
                    : platform.maxTextureSize, 32, 16384);
                settings.platformOverrides[i] = platform;
            }
            return settings;
        }

        /// <summary>
        /// Infers common Built-in, URP, HDRP, and UMA texture conventions. The returned layout
        /// remains in Automatic mode so future shader/property changes are re-evaluated unless
        /// the user copies it to a Custom layout in the inspector.
        /// </summary>
        public static TextureChannelLayout InferTextureChannelLayout(MaterialChannel channel, Material sourceMaterial)
        {
            TextureChannelLayout layout = new TextureChannelLayout
            {
                mode = TextureChannelLayoutMode.Automatic
            };

            if (channel.channelType == ChannelType.MaterialColor)
            {
                return layout;
            }

            string property = (channel.materialPropertyName ?? string.Empty).Trim();
            string source = (channel.sourceTextureName ?? string.Empty).Trim();
            string name = (property + " " + source).ToLowerInvariant();

            if (EqualsProperty(property, "_DetailMap"))
            {
                // HDRP detail maps use signed/neutral remapping and split the normal axes,
                // so these meanings remain distinct from ordinary albedo/normal/smoothness.
                layout.red = TextureChannelUsage.DetailAlbedo;
                layout.green = TextureChannelUsage.DetailNormalY;
                layout.blue = TextureChannelUsage.DetailSmoothness;
                layout.alpha = TextureChannelUsage.DetailNormalX;
                return layout;
            }

            if (channel.channelType == ChannelType.NormalMap ||
                channel.channelType == ChannelType.DetailNormalMap ||
                name.Contains("normal") || name.Contains("bump"))
            {
                SetRgb(ref layout, TextureChannelUsage.Normal);
                return layout;
            }

            if (EqualsProperty(property, "_MaskMap"))
            {
                layout.red = TextureChannelUsage.Metallic;
                layout.green = TextureChannelUsage.AmbientOcclusion;
                layout.blue = TextureChannelUsage.DetailMask;
                layout.alpha = TextureChannelUsage.Smoothness;
                return layout;
            }

            if (EqualsProperty(property, "_MetallicGlossMap"))
            {
                layout.red = TextureChannelUsage.Metallic;
                layout.alpha = TextureChannelUsage.Smoothness;
                return layout;
            }

            if (EqualsProperty(property, "_SpecGlossMap") || EqualsProperty(property, "_SpecularMap"))
            {
                SetRgb(ref layout, TextureChannelUsage.Specular);
                layout.alpha = TextureChannelUsage.Smoothness;
                return layout;
            }

            if (EqualsProperty(property, "_OcclusionMap") || name.Contains("occlusion") || name.Contains("ambient") || name == "ao")
            {
                // Unity's Built-in and SRP Lit shaders sample standalone occlusion from green.
                layout.green = TextureChannelUsage.AmbientOcclusion;
                return layout;
            }

            if (name.Contains("rough"))
            {
                layout.red = TextureChannelUsage.Roughness;
                return layout;
            }

            if (name.Contains("metal"))
            {
                layout.red = TextureChannelUsage.Metallic;
                if (name.Contains("gloss") || name.Contains("smooth"))
                {
                    layout.alpha = TextureChannelUsage.Smoothness;
                }
                return layout;
            }

            if (name.Contains("emission") || name.Contains("emissive") || name.Contains("emmission"))
            {
                SetRgb(ref layout, TextureChannelUsage.Emission);
                return layout;
            }

            if (name.Contains("thickness"))
            {
                layout.red = TextureChannelUsage.Thickness;
                return layout;
            }

            if (name.Contains("height") || name.Contains("parallax"))
            {
                layout.green = TextureChannelUsage.Height;
                return layout;
            }

            if (name.Contains("detailmask") || name.Contains("detail mask"))
            {
                layout.alpha = TextureChannelUsage.DetailMask;
                return layout;
            }

            bool isBaseColor = channel.channelType == ChannelType.DiffuseTexture ||
                               channel.channelType == ChannelType.TintedTexture ||
                               name.Contains("base") || name.Contains("main") ||
                               name.Contains("diffuse") || name.Contains("albedo");
            if (isBaseColor)
            {
                SetRgb(ref layout, TextureChannelUsage.Albedo);
                layout.alpha = TextureChannelUsage.Opacity;
                if (UsesBaseMapAlphaForSmoothness(sourceMaterial))
                {
                    layout.alpha |= TextureChannelUsage.Smoothness;
                }
                return layout;
            }

            // Unknown and non-shader textures are retained as one editable custom texture.
            SetRgba(ref layout, TextureChannelUsage.Custom);
            return layout;
        }

        private static bool UsesBaseMapAlphaForSmoothness(Material sourceMaterial)
        {
            if (sourceMaterial == null)
            {
                return false;
            }

            string[] candidates = { "_SmoothnessTextureChannel", "_SmoothnessSource" };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (sourceMaterial.HasProperty(candidates[i]) && sourceMaterial.GetFloat(candidates[i]) > 0.5f)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool EqualsProperty(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetRgb(ref TextureChannelLayout layout, TextureChannelUsage usage)
        {
            layout.red = usage;
            layout.green = usage;
            layout.blue = usage;
        }

        private static void SetRgba(ref TextureChannelLayout layout, TextureChannelUsage usage)
        {
            SetRgb(ref layout, usage);
            layout.alpha = usage;
        }
#endif

#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/Core/Material")]
		public static void CreateMaterialAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAMaterial>();
		}
#endif

        public int GetChannelIndex(string materialPropertyName)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i].materialPropertyName == materialPropertyName)
                    return i;
            }
            return -1;
        }

        public List<string> GetTexturePropertyNames()
        {
            List<string> names = new List<string>();


            foreach (MaterialChannel channel in channels)
            {
                if (channel.channelType == ChannelType.Texture || channel.channelType == ChannelType.TintedTexture || channel.channelType == ChannelType.DiffuseTexture)
                {
                    names.Add(channel.materialPropertyName);
                }
            }
            return names;
        }

        public bool IsGeneratedTextures
        {
            get
            {
                return materialType == MaterialType.Atlas || materialType == MaterialType.NoAtlas;
            }
        }

        public bool IsNoAtlas()
        {
            return materialType != MaterialType.Atlas;
        }

        /// <summary>
        /// Is the UMAMaterial based on a procedural material (substance)?
        /// </summary>
        public bool IsProcedural()
		{
			#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
			if ((material != null) && (material is ProceduralMaterial))
				return true;
            #endif

			return false;
		}

        public bool IsEmpty
        {
            get
            {
                return channels == null ? true : channels.Length == 0;
            }
        }

        /// <summary>
        /// Checks if UMAMaterials are effectively equal.
		/// Useful when comparing materials from asset bundles, that would otherwise say they are different to ones in the binary
		/// And procedural materials which can be output compatible even if they are generated from different sources
        /// </summary>
        /// <param name="material">The material to compare</param>
        /// <returns></returns>
        public bool Equals(UMAMaterial material)
        {
            return objectName == material.objectName;
        }

    }
}
