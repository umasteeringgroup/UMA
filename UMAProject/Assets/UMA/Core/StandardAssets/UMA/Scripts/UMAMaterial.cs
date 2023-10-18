using UnityEngine;
using System;
using UnityEngine.Serialization;
using System.Collections.Generic;

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
        public bool translateSRP;

        [SerializeField]
        [FormerlySerializedAs("material")]
        private Material _material;

        [Serializable]
        public struct SRPMaterial
        {
            public UMAUtils.PipelineType SRP;
            public Material material;
        };

        public List<SRPMaterial> srpMaterials = new List<SRPMaterial>();
        private Dictionary<UMAUtils.PipelineType, Material> _srpMaterialLookup = new Dictionary<UMAUtils.PipelineType, Material>();

        public Material  material
        {
            get 
            {
                var pipe = UMAUtils.DetectPipeline();   
                if (_srpMaterialLookup.ContainsKey(pipe))
                {
                    return _srpMaterialLookup[pipe];
                }
                else
                {
                    // NO SrpMaterials in the list, so just return the material
                    if (srpMaterials.Count == 0)
                    {
                        _srpMaterialLookup.Add(pipe, _material);
                        return _material;
                    }

                    if (_srpMaterialLookup.Count == 0)
                    {
                        foreach (var srpMat in srpMaterials)
                        {
                            _srpMaterialLookup.Add(srpMat.SRP, srpMat.material);
                        }
                    }
                    if (_srpMaterialLookup.ContainsKey(pipe))
                    {
                        return _srpMaterialLookup[pipe];
                    }
                    else
                    {
                        // Just stick the default material in there.
                        _srpMaterialLookup.Add(pipe, _material);
                        return _material;
                    }
                }
            }
            set { _material = value; }
        }




        [Tooltip("Used as a second pass when 'Use Existing Textures' is set. Leave null for most cases.")]
        public Material secondPass;

        public MaterialType materialType = MaterialType.Atlas;
        public MaterialChannel[] channels;

        [Range(-2.0f, 2.0f)]
        public float MipMapBias = 0.0f;
        [Range(1, 16)]
        public int AnisoLevel = 1;
        public FilterMode MatFilterMode = FilterMode.Bilinear;
        public CompressionSettings Compression = CompressionSettings.None;


        [Tooltip("Shader parms can be used to pass colors to shaders. Each entry represents a parameter name and a color name. If neither exists, it is ignored.")]
        public ShaderParms[] shaderParms;

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

		static public Color GetBackgroundColor(ChannelType channelType)
		{
			return ChannelBackground[(int)channelType];
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

        private bool isGeneratedTextures
        {
            get
            {
                return materialType == MaterialType.Atlas || materialType == MaterialType.NoAtlas;
            }
        }

        public bool isNoAtlas()
        {
            return materialType == MaterialType.Atlas;
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
            return name == material.name;
        }

    }
}
