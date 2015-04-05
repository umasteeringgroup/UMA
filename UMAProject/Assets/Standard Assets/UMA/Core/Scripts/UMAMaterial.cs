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
		}

		[Serializable]
		public struct MaterialChannel
		{
			public ChannelType channelType;
			public RenderTextureFormat textureFormat;
			public string materialPropertyName;
		}

#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA Material")]
		public static void CreateMaterialAsset()
		{
			UMAEditor.CustomAssetUtility.CreateAsset<UMAMaterial>();
		}
#endif
	}
}
