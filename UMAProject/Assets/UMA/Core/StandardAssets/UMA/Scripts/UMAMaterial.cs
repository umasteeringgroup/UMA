using UnityEngine;
using System.Collections;
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
        }

#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/Core/Material")]
		public static void CreateMaterialAsset()
		{
			UMAEditor.CustomAssetUtility.CreateAsset<UMAMaterial>();
		}
#endif
        /// <summary>
        /// Checks if UMAMaterials are effectively equal. Useful when comparing materials from asset bundles, that would otherwise say they are different to ones in the binary
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
                if (this.name == material.name &&
                    this.material.name == material.material.name &&
                    this.materialType == material.materialType &&
                    this.channels.Length == material.channels.Length)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

    }
}
