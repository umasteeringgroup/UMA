using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

		/// <summary>
		/// Color Component Adjusters are used by dna to adjust colors independently of shared colors, for things like temporary color effects and fading NormalMaps in and out
		/// NOTE: this list is cleared at the start of an applyDNA cycle
		/// </summary>
		public List<ColorComponentAdjuster> colorComponentAdjusters = new List<ColorComponentAdjuster>();

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
						if (Debug.isDebugBuild)
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
						if (Debug.isDebugBuild)
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
						if (Debug.isDebugBuild)
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
				if (Debug.isDebugBuild)
					Debug.LogError("Overlay Data Asset is NULL!");

				return;
			}
			if (asset.material == null)
			{
				if (Debug.isDebugBuild)
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
#if UNITY_EDITOR
                    Debug.LogError(string.Format("Overlay '{0}' doesn't have the expected UMA Material: '{1}'!\nCurrently it has '{2}' at '{3}'", asset.overlayName, targetMaterial.name, asset.material, UnityEditor.AssetDatabase.GetAssetPath(asset)));
#endif
                    valid = false;
				}
			}

			if (asset.textureCount != targetMaterial.channels.Length)
			{
				if (Debug.isDebugBuild)
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
						if (Debug.isDebugBuild)
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

				if (Debug.isDebugBuild)
					Debug.LogWarning(string.Format("Overlay '{0}' missing required color data. Resizing and adding defaults", asset.overlayName));
			}

			return valid;
		}

		/// <summary>
		/// Gets the result of all ColorComponentAdjusters that have been added to this overlay, for the given texture, color component, and additiveness
		/// </summary>
		/// <param name="inColor">The unadjusted color used by the overlay</param>
		/// <param name="channel">The affected texture channel to find adjustments for</param>
		/// <param name="component">The component of the color to find adjustments for (0= r, 1= g, 2= b, 3 = a)</param>
		/// <param name="additive">Whether adjustments for the additive color are required</param>
		/// <returns>Returns how much the overlays adjuster want to adjust the given component of the color</returns>
		public float GetComponentAdjustmentsForChannel(float inColor, int channel, int component, bool additive = false)
		{
			var resUnsigned = 0f;
			var resList = new List<float>();
			if (colorComponentAdjusters.Count == 0)
				return resUnsigned;
			//for each adjuster calculate how much difference it wants to make to the color
			for (int i = 0; i < colorComponentAdjusters.Count; i++)
			{
				if (colorComponentAdjusters[i].channel == channel && colorComponentAdjusters[i].colorComponent == component && colorComponentAdjusters[i].Additive == additive)
				{
					if (colorComponentAdjusters[i].adjustmentType == ColorComponentAdjuster.AdjustmentType.Adjust || colorComponentAdjusters[i].adjustmentType == ColorComponentAdjuster.AdjustmentType.AdjustAdditive)
					{
						resUnsigned += Mathf.Abs((inColor + colorComponentAdjusters[i].adjustment) - inColor);
						resList.Add((inColor + colorComponentAdjusters[i].adjustment) - inColor);
					}
					else //Absolute/BlendFactor - the adjustment is the color
					{
						resUnsigned += Mathf.Abs(colorComponentAdjusters[i].adjustment - inColor);
						resList.Add(colorComponentAdjusters[i].adjustment - inColor);
					}
				}
			}
			//Then get the weighted average of all of those results
			//we cant just get the average because adjusters that are not making any changes should not dilute the effect of the ones that are
			float weightedAveRes = 0f;
			if (resUnsigned != 0f)
			{
				for (int i = 0; i < resList.Count; i++)
				{
					weightedAveRes += resList[i] * (Mathf.Abs(resList[i]) / resUnsigned);
				}
			}
			return weightedAveRes;
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Equivalent(OverlayData overlay1, OverlayData overlay2)
		{
			if (overlay1)
			{
				if (overlay2)
				{
					if ((overlay1.asset != overlay2.asset) ||
						(overlay1.rect != overlay2.rect) ||
						(overlay1.colorData != overlay2.colorData))
                    {
						return false;
                    }
					return true;
				}
				return false;
				/*
				if (overlay2)
				{
					return ((overlay1.asset == overlay2.asset) &&
							(overlay1.rect == overlay2.rect) &&
							(overlay1.colorData == overlay2.colorData));
				}
				return false; */
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

		#region SPECIAL TYPES
		/// <summary>
		/// Color Component Adjusters are used by dna to adjust colors independently of shared colors, for things like temporary color effects and fading NormalMaps in and out
		/// </summary>
		public class ColorComponentAdjuster
		{
			public enum AdjustmentType
			{
				Absolute,
				Adjust,
				AbsoluteAdditive,
				AdjustAdditive,
				BlendFactor
			}
			/// <summary>
			/// the texture channel on the overlay to affect
			/// </summary>
			public int channel = 0;
			/// <summary>
			/// 0 = r, 1 = g, 2 = b, 3 = a
			/// </summary>
			public int colorComponent = 0;
			/// <summary>
			/// An adjustment can add or subtract from the component (r,g,b,a) of the color and should be in the range -1f -> 1f.
			/// If the 'adjustmentType' is set to absolute this should be the absolute value for the color in the range 0f -> 1f.
			/// </summary>
			public float adjustment = 0f;
			/// <summary>
			/// Adjust will adjust the component of the current color by the adjustment amount. Absolute will set the component of the current color TO the adjustment value
			/// This can also be set to affect the additive channel if desired.
			/// Use BlendFactor on the alpha component of a color to completely fade a texture in and out
			/// </summary>
			public AdjustmentType adjustmentType = AdjustmentType.Adjust;

			public bool Additive
			{
				get { return adjustmentType == AdjustmentType.AbsoluteAdditive || adjustmentType == AdjustmentType.AdjustAdditive; }
			}

			public ColorComponentAdjuster() { }

			public ColorComponentAdjuster(int channel, int colorComponent, float adjustment, AdjustmentType adjustmentType = AdjustmentType.Adjust)
			{
				this.channel = channel;
				this.colorComponent = colorComponent;
				this.adjustment = adjustment;
				this.adjustmentType = adjustmentType;
			}
			public ColorComponentAdjuster(ColorComponentAdjuster other)
			{
				this.channel = other.channel;
				this.colorComponent = other.colorComponent;
				this.adjustment = other.adjustment;
				this.adjustmentType = other.adjustmentType;
			}
		}
		#endregion

	}
}