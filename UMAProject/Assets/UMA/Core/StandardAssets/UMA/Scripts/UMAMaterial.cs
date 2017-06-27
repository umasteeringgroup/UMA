using UnityEngine;
using System;

namespace UMA
{
    /// <summary>
    /// UMA wrapper for Unity material.
    /// </summary>
    public class UMAMaterial : ScriptableObject
    {
        public Material material;
        public MaterialType materialType = MaterialType.Atlas;
        public MaterialChannel[] channels;
        public UMAClothProperties clothProperties;
        public bool RequireSeperateRenderer;

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
        }

        [Serializable]
        public struct MaterialChannel
        {
            public ChannelType channelType;
            public RenderTextureFormat textureFormat;
            public string materialPropertyName;
			public string sourceTextureName;
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
            #if UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE //supported platforms for procedural materials
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
				if (this.material.shader != material.material.shader)
					return false;
                if (this.material.renderQueue != material.material.renderQueue)
                    return false;
				if (this.materialType != material.materialType)
					return false;
				if (this.channels.Length != material.channels.Length)
					return false;
                if (this.clothProperties != material.clothProperties)
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
