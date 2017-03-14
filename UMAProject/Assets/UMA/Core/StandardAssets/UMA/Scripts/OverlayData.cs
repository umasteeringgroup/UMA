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

		public string overlayName { get { return asset.overlayName; } }
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
			if (asset == null)
			{
				Debug.LogError("Overlay Data Asset is NULL!");
				return;
			}
			if (asset.material == null)
			{
				Debug.LogError("Error: Materials are missing on Asset: " + asset.name + ". Have you imported all packages?");
				this.colorData = new OverlayColorData(3); // ?? Don't know. Just create it for standard PBR material size. 
			}
			else
			{
				this.colorData = new OverlayColorData(asset.material.channels.Length);
			}
			this.asset = asset;
			this.rect = asset.rect;
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