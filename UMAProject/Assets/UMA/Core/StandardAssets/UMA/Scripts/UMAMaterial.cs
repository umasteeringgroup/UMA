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

        private bool hdrpchecked = false;
        private bool checkedHDRPResult = false;

        private bool isHDRP
        {
            get
            {
                //if (!hdrpchecked)
                //{
                    checkedHDRPResult = GraphicsSettings.currentRenderPipeline != null && GraphicsSettings.currentRenderPipeline.GetType().ToString().Contains("HDRenderPipelineAsset");
                  //  hdrpchecked = true;
                //}
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
            public bool NonShaderTexture;
       }

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
