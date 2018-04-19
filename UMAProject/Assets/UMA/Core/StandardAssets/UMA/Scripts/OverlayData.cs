using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// Overlay data contains the textures and material properties for building atlases.
	/// </summary>
	[System.Serializable]
	public class OverlayData : System.IEquatable<OverlayData>
	{
		/// <summary>
		/// The asset contains the immutable portions of the overlay.
		/// </summary>
		public OverlayDataAsset asset;
		/// <summary>
		/// Destination rectangle for drawing overlay textures.
		/// </summary>
		public Rect rect;

		#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
        //https://docs.unity3d.com/Manual/ProceduralMaterials.html
		protected ProceduralTexture[] generatedTextures = null;
        #endif

		const string proceduralSizeProperty = "$outputsize";

		// Properties dependant on the underlying asset.
		public bool isProcedural { get { return asset.material.IsProcedural(); } }
		public string overlayName { get { return asset.overlayName; } }
		public OverlayDataAsset.OverlayType overlayType { get { return asset.overlayType; } }
		public Texture alphaMask
		{
			get
			{
				if (asset.alphaMask != null)
					return asset.alphaMask;
				
				#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
				if (this.isProcedural)
				{
					if ((generatedTextures == null) || (generatedTextures.Length != asset.textureCount))
					{
						Debug.LogWarning("Accessing empty texture array on procedural overlay. GenerateProceduralTextures() should have already been called!");
						GenerateProceduralTextures();
					}

					return generatedTextures[0];
				}
                #endif

				return asset.textureList[0];
			}
		}
		public Texture[] textureArray
		{
			get
			{
				#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
				if (this.isProcedural)
				{
					if ((generatedTextures == null) || (generatedTextures.Length != asset.textureCount))
					{
						Debug.LogWarning("Accessing empty texture array on procedural overlay. GenerateProceduralTextures() should have already been called!");
						GenerateProceduralTextures();
					}

					return generatedTextures;
				}
                #endif

				return asset.textureList;
			}
		}
		public int pixelCount
		{
			get
			{
				#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
				if (this.isProcedural)
				{
					ProceduralMaterial material = asset.material.material as ProceduralMaterial;
					if (material.HasProceduralProperty(proceduralSizeProperty))
					{
						Vector4 size = material.GetProceduralVector(proceduralSizeProperty);
						return (2 << Mathf.FloorToInt(size.x)) * (2 << Mathf.FloorToInt(size.y));
					}
					else
					{
						Debug.LogWarning("Unable to determine size for procedural material " + material.name);
						return 0;
					}
				}
                #endif

				return asset.textureList[0].width * asset.textureList[0].height;
			}
		}

		/// <summary>
		/// Color data for material channels.
		/// </summary>
		[System.NonSerialized]
		public OverlayColorData colorData;

		#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
		public class OverlayProceduralData
		{
			public string name;
			public ProceduralPropertyType type;

			public bool booleanValue;
			public float floatValue;
			public Color colorValue;
			public int enumValue;
			public Texture2D textureValue;
			public Vector4 vectorValue;
		}
		public OverlayProceduralData[] proceduralData;
        #endif

		/// <summary>
		/// Deep copy of the OverlayData.
		/// </summary>
		public OverlayData Duplicate()
		{
			var res = new OverlayData(asset);
			res.rect = rect;
			if (colorData != null)
				res.colorData = colorData.Duplicate();
			return res;
		}

		protected OverlayData()
		{
		}

		/// <summary>
		/// Constructor for overlay using the given asset.
		/// </summary>
		/// <param name="asset">Asset.</param>
		public OverlayData(OverlayDataAsset asset)
		{
			if (asset == null)
			{
				Debug.LogError("Overlay Data Asset is NULL!");
				return;
			}
			if (asset.material == null)
			{
				Debug.LogError("Error: Materials are missing on Asset: " + asset.name + ". Have you imported all packages?");
				this.colorData = new OverlayColorData(3); // Don't know. Just create it for standard PBR material size. 
			}
			else
			{
				this.colorData = new OverlayColorData(asset.material.channels.Length);
			}
			this.asset = asset;
			this.rect = asset.rect;

			#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
			if (this.isProcedural)
			{
				this.proceduralData = new OverlayProceduralData[0];
			}
            #endif
		}

		/// <summary>
		/// Validate the OverlayData against the requirements of a particular UMAMaterial.
		/// </summary>
		/// <param name="targetMaterial">UMAMaterial to try and match.</param>
		/// <param name="isBaseOverlay">Is this the first overlay being applied?</param>
		internal bool Validate(UMAMaterial targetMaterial, bool isBaseOverlay)
		{
			bool valid = true;

			if (asset.material != targetMaterial)
			{
				if (!asset.material.Equals(targetMaterial))
				{
					Debug.LogError(string.Format("Overlay '{0}' doesn't have the expected UMA Material: '{1}'", asset.overlayName, targetMaterial.name));
					valid = false;
				}
			}

			if (asset.textureCount != targetMaterial.channels.Length)
			{
				Debug.LogError(string.Format("Overlay '{0}' doesn't have the right number of channels", asset.overlayName));
				valid = false;
			}
			else
			{
				// HACK - Assume that procedural materials can always generate all channels
                if (this.isProcedural)
                {
                }
				// All channels must be initialized by the base overlay, only channel 0 in others
				else for (int i = 0; i < targetMaterial.channels.Length; i++)
                {
                    if ((asset.textureList[i] == null) && (targetMaterial.channels[i].channelType != UMAMaterial.ChannelType.MaterialColor))
                    {
                        Debug.LogError(string.Format("Overlay '{0}' missing required texture in channel {1}", asset.overlayName, i));
                        valid = false;
                    }

                    if (!isBaseOverlay)
                        break;
                }
			}

			if (colorData.channelMask.Length < targetMaterial.channels.Length)
			{
				// Fixup colorData if moving from Legacy to PBR materials
				int oldsize = colorData.channelMask.Length;

				System.Array.Resize(ref colorData.channelMask, targetMaterial.channels.Length);
				System.Array.Resize(ref colorData.channelAdditiveMask, targetMaterial.channels.Length);

				for (int i = oldsize; i > targetMaterial.channels.Length; i++)
				{
					colorData.channelMask[i] = Color.white;
					colorData.channelAdditiveMask[i] = Color.black;
				}


				Debug.LogWarning(string.Format("Overlay '{0}' missing required color data. Resizing and adding defaults", asset.overlayName));
			}

			return valid;
		}

		/// <summary>
		/// Sets the tint color for a channel.
		/// </summary>
		/// <param name="channel">Channel.</param>
		/// <param name="color">Color.</param>
		public void SetColor(int channel, Color32 color)
		{
			EnsureChannels(channel + 1);
			colorData.channelMask[channel] = color;
		}

		/// <summary>
		/// Gets the tint color for a channel.
		/// </summary>
		/// <returns>The color.</returns>
		/// <param name="channel">Channel.</param>
		public Color32 GetColor(int channel)
		{
			EnsureChannels(channel + 1);
			return colorData.channelMask[channel];
		}

		/// <summary>
		/// Gets the additive color for a channel.
		/// </summary>
		/// <returns>The additive color.</returns>
		/// <param name="channel">Channel.</param>
		public Color32 GetAdditive(int channel)
		{
			EnsureChannels(channel + 1);
			return colorData.channelAdditiveMask[channel];
		}

		/// <summary>
		/// Sets the additive color for a channel.
		/// </summary>
		/// <param name="channel">Channel.</param>
		/// <param name="color">Color.</param>
		public void SetAdditive(int channel, Color32 color)
		{
			EnsureChannels(channel + 1);
			colorData.channelAdditiveMask[channel] = color;
		}

		/// <summary>
		/// Copies the colors from another overlay.
		/// </summary>
		/// <param name="overlay">Source overlay.</param>
		public void CopyColors(OverlayData overlay)
		{
			colorData = overlay.colorData.Duplicate();
		}

		#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
		public void GenerateProceduralTextures()
		{
			if (!this.isProcedural)
				return;

			ProceduralMaterial material = asset.material.material as ProceduralMaterial;
//			ProceduralPropertyDescription[] properties = material.GetProceduralPropertyDescriptions();
//			for (int i = 0; i < properties.Length; i++)
//			{
//				Debug.Log(properties[i].name + " / " + properties[i].label + " / " + properties[i].type);
//			}

			if (proceduralData != null) foreach (OverlayProceduralData data in proceduralData)
			{
				switch (data.type)
				{
					case ProceduralPropertyType.Boolean:
						material.SetProceduralBoolean(data.name, data.booleanValue);
						break;
					case ProceduralPropertyType.Color3:
					case ProceduralPropertyType.Color4:
						material.SetProceduralColor(data.name, data.colorValue);
						break;
					case ProceduralPropertyType.Enum:
						material.SetProceduralEnum(data.name, data.enumValue);
						break;
					case ProceduralPropertyType.Float:
						material.SetProceduralFloat(data.name, data.floatValue);
						break;
					case ProceduralPropertyType.Texture:
						material.SetProceduralTexture(data.name, data.textureValue);
						break;
					case ProceduralPropertyType.Vector2:
					case ProceduralPropertyType.Vector3:
					case ProceduralPropertyType.Vector4:
						material.SetProceduralVector(data.name, data.vectorValue);
						break;
				}
			}
			material.RebuildTexturesImmediately();

			int channelCount = asset.material.channels.Length;
			generatedTextures = new ProceduralTexture[channelCount];
			for (int i = 0; i < channelCount; i++)
			{
				UMAMaterial.MaterialChannel channel = asset.material.channels[i];
				if (channel.channelType == UMAMaterial.ChannelType.MaterialColor)
				{
					generatedTextures[i] = null;
				}
				else
				{
					generatedTextures[i] = material.GetGeneratedTexture(channel.sourceTextureName);
				}
			}
		}
        #endif

		#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
		public void ReleaseProceduralTextures()
		{
			if (!this.isProcedural)
				return;
			if (generatedTextures == null)
				return;

			for (int i = 0; i < generatedTextures.Length; i++)
			{
//				ProceduralTexture texture = generatedTextures[i];
//				if (texture != null)
//					Object.Destroy(texture);

				generatedTextures[i] = null;
			}

			generatedTextures = null;
		}
        #endif

		public void EnsureChannels(int channels)
		{
			colorData.EnsureChannels(channels);
		}

		public static bool Equivalent(OverlayData overlay1, OverlayData overlay2)
		{
			if (overlay1)
			{
				if (overlay2)
				{
					return ((overlay1.asset == overlay2.asset) &&
							(overlay1.rect == overlay2.rect) &&
							(overlay1.colorData == overlay2.colorData));
				}
				return false;
			}
			return !((bool)overlay2);
		}

		/// <summary>
		/// Compares two overlay.assets and overlay.rects to see if they are the same. Mainly for comparing overlays from AssetBundles.
		/// </summary>
		/// <param name="overlay1"></param>
		/// <param name="overlay2"></param>
		/// <returns></returns>
		public static bool EquivalentAssetAndUse(OverlayData overlay1, OverlayData overlay2)
		{
			if (overlay1)
			{
				if (overlay2)
				{
					return ((overlay1.asset.overlayName == overlay2.asset.overlayName) &&
							(overlay1.asset.material.Equals(overlay2.asset.material)) &&
							(overlay1.rect == overlay2.rect));
				}
				return false;
			}
			return !((bool)overlay2);
		}

		#region operator ==, != and similar HACKS, seriously.....
		public static implicit operator bool(OverlayData obj)
		{
			return ((System.Object)obj) != null && obj.asset != null;
		}

		public bool Equals(OverlayData other)
		{
			return (this == other);
		}
		public override bool Equals(object other)
		{
			return Equals(other as OverlayData);
		}

		public static bool operator ==(OverlayData overlay, OverlayData obj)
		{
			if (overlay)
			{
				if (obj)
				{
					return System.Object.ReferenceEquals(overlay, obj);
				}
				return false;
			}
			return !((bool)obj);
		}

		public static bool operator !=(OverlayData overlay, OverlayData obj)
		{
			if (overlay)
			{
				if (obj)
				{
					return !System.Object.ReferenceEquals(overlay, obj);
				}
				return true;
			}
			return ((bool)obj);
		}
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
		#endregion

	}
}