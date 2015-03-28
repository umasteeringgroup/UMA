using UnityEngine;
using System.Collections;

namespace UMA
{
	[System.Serializable]
#if !UMA_LEAN_AND_CLEAN
	public partial class OverlayData : System.IEquatable<OverlayData>
#else
	public class OverlayData : System.IEquatable<OverlayData>
#endif
	{
		public OverlayDataAsset asset;
		public Rect rect;

#if UMA2_LEAN_AND_CLEAN
		public string overlayName { get { return asset.overlayName; } }
#endif
		[System.NonSerialized]
		public OverlayColorData colorData;

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

		public OverlayData(OverlayDataAsset asset)
		{
			this.asset = asset;
			this.colorData = new OverlayColorData(asset.material.channels.Length);
			this.rect = asset.rect;
		}

		[System.Obsolete("useAdvancedMasks is obsolete, from now on we ALWAYS use advanced masks. Reduces code complexity.", false)]
	    public bool useAdvancedMasks { get { return true; } }
        public void SetColor(int channel, Color32 color)
	    {
            EnsureChannels(channel+1);
			colorData.channelMask[channel] = color;
	    }

        public Color32 GetColor(int channel)
        {
			EnsureChannels(channel + 1);
			return colorData.channelMask[channel];
        }

        public Color32 GetAdditive(int channel)
        {
            EnsureChannels(channel + 1);
			return colorData.channelAdditiveMask[channel];
        }

        public void SetAdditive(int overlay, Color32 color)
	    {
            EnsureChannels(overlay+1);
			colorData.channelAdditiveMask[overlay] = color;
	    }

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
		#endregion

	}
}