using UnityEngine;
using System;
using UnityEngine.Serialization;
using System.Collections.Generic;
using System.IO.Pipes;

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
        private bool srpSetup = false;
        public bool AutoSetSRPMaterials = true;

        [SerializeField]
        [FormerlySerializedAs("material")]
        private Material _material;
        [SerializeField]
        [FormerlySerializedAs("secondPass")]
        private Material _secondPass;

        [Serializable]
        public struct SRPMaterial
        {
            [Tooltip("The SRP this material is used for.")]
            public UMAUtils.PipelineType SRP;
            [Tooltip("The material to use for this SRP. If 'Use Existing Textures' is set, this is the first pass material.")]
            public Material material;
            [Tooltip("Used as a second pass when 'Use Existing Textures' is set. Leave null for most cases.")]
            public Material secondPass;
            [Tooltip("The keywords to use for this material")]
            public List<string> alternateKeywords;
            private Dictionary<string, string> _alternateKeywordsLookup;
            public SRPMaterial(UMAUtils.PipelineType SRP, Material material, Material secondPass, List<string> alternateKeywords)
            {
                this.SRP = SRP;
                this.material = material;
                this.secondPass = secondPass;
                this.alternateKeywords = alternateKeywords;
                _alternateKeywordsLookup = new Dictionary<string, string>();
                for (int i = 0; i < alternateKeywords.Count; i++)
                {
                    _alternateKeywordsLookup.Add(alternateKeywords[i], alternateKeywords[i]);
                }
            }
        };

        public void Awake()
        {
            SetupSRP();
        }

        public void SetupSRP(bool forceSetup = false)
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                srpSetup = false;
            }
#endif
            if (forceSetup || srpSetup == false)
            {
                srpSetup = true;
                var pipe = UMAUtils.DetectPipeline();

                for (int i = 0; i < srpMaterials.Count; i++)
                {
                    if (srpMaterials[i].SRP == pipe)
                    {
                        _material = srpMaterials[i].material;
                        _secondPass = srpMaterials[i].secondPass;
                        if (channels.Length != srpMaterials[i].alternateKeywords.Count)
                        {
                            List<MaterialChannel> newChannels = new List<MaterialChannel>();
                            newChannels.AddRange(channels);
                            if (newChannels.Count > srpMaterials[i].alternateKeywords.Count)
                            {
                                newChannels.RemoveRange(srpMaterials[i].alternateKeywords.Count, newChannels.Count - srpMaterials[i].alternateKeywords.Count);
                            }
                            else
                            {
                                for (int j = newChannels.Count; j < srpMaterials[i].alternateKeywords.Count; j++)
                                {
                                    newChannels.Add(new MaterialChannel());
                                }
                            }
                            channels = newChannels.ToArray();
                        }
                        for (int j = 0; j < srpMaterials[i].alternateKeywords.Count; j++)
                        {
                            channels[j].materialPropertyName = srpMaterials[i].alternateKeywords[j];
                        }
                        return;
                    }
                }
            }
        }

        public List<SRPMaterial> srpMaterials = new List<SRPMaterial>();
        //private Dictionary<UMAUtils.PipelineType, SRPMaterial> _srpMaterialLookup = new Dictionary<UMAUtils.PipelineType, SRPMaterial>();
        
        public SRPMaterial CreateSRPMaterial(UMAUtils.PipelineType SRP)
        {
            List<string> _alternateKeywords = new List<string>();
            foreach(var chan in this.channels)
            {
                _alternateKeywords.Add(chan.materialPropertyName);
            }
            return new SRPMaterial(SRP, _material,_secondPass, _alternateKeywords);
        }

      /*  private void SetupMaterialLookup(UMAUtils.PipelineType pipe)
        {
            if (_srpMaterialLookup.Count == 0)
            {
                for (int i = 0; i < srpMaterials.Count; i++)
                {
                    SRPMaterial srpMat = srpMaterials[i];
                    _srpMaterialLookup.Add(srpMat.SRP, srpMat);
                }
            }
            if (!_srpMaterialLookup.ContainsKey(pipe))
            {
                srpMaterials.Add(CreateSRPMaterial(pipe));
                _srpMaterialLookup.Add(pipe, srpMaterials[srpMaterials.Count - 1]);
            }
        } */


        public Material  material
        {
            get 
            {
                if (AutoSetSRPMaterials)
                {
                    SetupSRP();
                }

                return _material;
            }
            set { _material = value; }
        }

        public Material secondPass
        {

            get
            {
                if (AutoSetSRPMaterials)
                {
                    SetupSRP();
                }
                return _secondPass;
                /*
                var pipe = UMAUtils.DetectPipeline();
                if (_srpMaterialLookup.ContainsKey(pipe))
                {
                    return _srpMaterialLookup[pipe].secondPass;
                }
                else
                {
                    SetupMaterialLookup(pipe);
                    return _srpMaterialLookup[pipe].secondPass;
                }*/
            }
        }


        [Tooltip("Used as a second pass when 'Use Existing Textures' is set. Leave null for most cases.")]
        //public Material secondPass;

        public MaterialType materialType = MaterialType.Atlas;
        public MaterialChannel[] channels = new MaterialChannel[0];

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
            return name == material.name;
        }

    }
}
