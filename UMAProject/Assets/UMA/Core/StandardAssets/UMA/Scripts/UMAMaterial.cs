using UnityEngine;
using System;

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
        public Material material;
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
        public enum MaterialType
        {
            Atlas = 1,
            NoAtlas = 2,
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
			new Color(0.5f,0.5f,1,0.5f)
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

        /// <summary>
        /// Checks if UMAMaterials are effectively equal.
		/// Useful when comparing materials from asset bundles, that would otherwise say they are different to ones in the binary
		/// And procedural materials which can be output compatible even if they are generated from different sources
        /// </summary>
        /// <param name="material">The material to compare</param>
        /// <returns></returns>
        public bool Equals(UMAMaterial material)
        {
            if (this.GetInstanceID() == material.GetInstanceID())
            {
                return true;
            }
            else
            {
				if (this.material.name != material.material.name)
					return false;
				//if (this.material.shader != material.material.shader)
				//	return false;
                if (this.material.renderQueue != material.material.renderQueue)
                    return false;
				if (this.materialType != material.materialType)
					return false;
				if (this.channels.Length != material.channels.Length)
					return false;
				for (int i = 0; i < this.channels.Length; i++)
				{
					MaterialChannel thisChannel = this.channels[i];
					MaterialChannel otherChannel = material.channels[i];
					if (thisChannel.channelType != otherChannel.channelType)
						return false;
					if (thisChannel.materialPropertyName != otherChannel.materialPropertyName)
						return false;
				}

				return true;
            }
        }

    }
}