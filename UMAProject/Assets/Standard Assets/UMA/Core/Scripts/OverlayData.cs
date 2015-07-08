using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// Overlay data contains the textures and material properties for building atlases.
	/// </summary>
	[System.Serializable]
#if !UMA_LEAN_AND_CLEAN
	public partial class OverlayData : System.IEquatable<OverlayData>
#else
	public class OverlayData : System.IEquatable<OverlayData>
#endif
	{
		/// <summary>
		/// The asset contains the immutable portions of the overlay.
		/// </summary>
		public OverlayDataAsset asset;
		/// <summary>
		/// Destination rectangle for drawing overlay textures.
		/// </summary>
		public Rect rect;

#if UMA2_LEAN_AND_CLEAN
		public string overlayName { get { return asset.overlayName; } }
#endif
		/// <summary>
		/// Color data for material channels.
		/// </summary>
		[System.NonSerialized]
		public OverlayColorData colorData;

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
			this.asset = asset;
			this.colorData = new OverlayColorData(asset.material.channels.Length);
			this.rect = asset.rect;
		}

		[System.Obsolete("useAdvancedMasks is obsolete, from now on we ALWAYS use advanced masks. Reduces code complexity.", false)]
	    public bool useAdvancedMasks { get { return true; } }

		/// <summary>
		/// Sets the tint color for a channel.
		/// </summary>
		/// <param name="channel">Channel.</param>
		/// <param name="color">Color.</param>
        public void SetColor(int channel, Color32 color)
	    {
            EnsureChannels(channel+1);
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
			EnsureChannels(channel+1);
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

#if !UMA2_LEAN_AND_CLEAN 
		#region obsolete junk from version 1
		[System.Obsolete("OverlayData.overlayName is obsolete use asset.overlayName!", false)]
		public string overlayName;
		
		[System.Obsolete("OverlayData.listID is obsolete.", false)]
		[System.NonSerialized]
		public int listID;
		
		[System.Obsolete("OverlayData.color is obsolete. Please refer to the OverlayColorData.", false)]
		public Color color = new Color(1, 1, 1, 1);

		[System.Obsolete("OverlayData.textureList is obsolete. Please refer to the OverlayDataAsset.", false)]
		public Texture[] textureList;
		[System.Obsolete("OverlayData.channelMask is obsolete. Please refer to the OverlayColorData.", false)]
		public Color32[] channelMask;
		[System.Obsolete("OverlayData.channelAdditiveMask is obsolete. Please refer to the OverlayColorData.", false)]
		public Color32[] channelAdditiveMask;
		
		[System.Obsolete("OverlayData.umaData is obsolete.", false)]
		[System.NonSerialized]
		public UMAData umaData;
		/// <summary>
		/// Use this to identify what kind of overlay this is and what it fits
		/// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
		/// </summary>
		[System.Obsolete("OverlayData.tags is obsolete use asset.tags!", false)]
		public string[] tags;
		#endregion
#endif

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