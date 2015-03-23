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
				res.colorData = colorData;
			else
				res.colorData = new OverlayColorData();
			return res;
	    }

		public OverlayData Copy()
		{
			var res = new OverlayData();
			res.asset = asset;
			res.rect = new Rect(rect);
			if (colorData != null)
				res.colorData = colorData.Copy();
			else
				res.colorData = new OverlayColorData();
			return res;
		}
		
		protected OverlayData()
		{
		}

		public OverlayData(OverlayDataAsset asset)
		{
			this.asset = asset;
			this.colorData = new OverlayColorData();
			this.rect = asset.rect;
		}

	    public bool useAdvancedMasks { get { return colorData.channelMask != null && colorData.channelMask.Length > 0; } }
        public void SetColor(int channel, Color32 color)
	    {
	        if (useAdvancedMasks)
	        {
                EnsureChannels(channel+1);
				colorData.channelMask[channel] = color;
	        }
	        else if (channel == 0)
	        {
				colorData.color = color;
	        }
	        else
	        {
	            AllocateAdvancedMasks();
                EnsureChannels(channel+1);
				colorData.channelMask[channel] = color;
	        }
	    }

        public Color32 GetColor(int channel)
        {
            if (useAdvancedMasks)
            {
                EnsureChannels(channel + 1);
				return colorData.channelMask[channel];
            }
            else if (channel == 0)
            {
				return colorData.color;
            }
            else
            {
                return new Color32(255, 255, 255, 255);
            }
        }

        public Color32 GetAdditive(int channel)
        {
            if (useAdvancedMasks)
            {
                EnsureChannels(channel + 1);
				return colorData.channelAdditiveMask[channel];
            }
            else
            {
                return new Color32(0, 0, 0, 0);
            }
        }

        public void SetAdditive(int overlay, Color32 color)
	    {
	        if (!useAdvancedMasks)
	        {
	            AllocateAdvancedMasks();
	        }
            EnsureChannels(overlay+1);
			colorData.channelAdditiveMask[overlay] = color;
	    }

	    private void AllocateAdvancedMasks()
	    {
			int channels = asset.textureList.Length;
			if (channels == 0) return;
            EnsureChannels(channels);
			colorData.channelMask[0] = colorData.color;
	    }

        public void CopyColors(OverlayData overlay)
        {
            if (overlay.useAdvancedMasks)
            {
				EnsureChannels(overlay.colorData.channelAdditiveMask.Length);
				for (int i = 0; i < overlay.colorData.channelAdditiveMask.Length; i++)
                {
                    SetColor(i, overlay.GetColor(i));
                    SetAdditive(i, overlay.GetAdditive(i));
                }
            }
            else
            {
				SetColor(0, overlay.colorData.color);
            }
        }

        public void EnsureChannels(int channels)
        {
			if (colorData.channelMask == null)
            {
				colorData.channelMask = new Color32[channels];
				colorData.channelAdditiveMask = new Color32[channels];
                for (int i = 0; i < channels; i++)
                {
					colorData.channelMask[i] = new Color32(255, 255, 255, 255);
					colorData.channelAdditiveMask[i] = new Color32(0, 0, 0, 0);
                }
            }
            else
            {
				if( colorData.channelMask.Length > channels ) return;

				var oldLenth = colorData.channelMask.Length;
				Color32[] newMask = new Color32[channels];
				Color32[] newAdditive = new Color32[channels];
				colorData.channelAdditiveMask = new Color32[channels];
				System.Array.Copy(colorData.channelMask, newMask, oldLenth);
				System.Array.Copy(colorData.channelAdditiveMask, newAdditive, oldLenth);
				for (int i = oldLenth; i < channels; i++)
                {
					newMask[i] = new Color32(255, 255, 255, 255);
					newAdditive[i] = new Color32(0, 0, 0, 0);
                }
				colorData.channelMask = newMask;
				colorData.channelAdditiveMask = newAdditive;
            }
        }

		public void RemoveChannels()
		{
			if (useAdvancedMasks)
			{
				colorData.color = colorData.channelMask[0];
				colorData.channelMask = null;
				colorData.channelAdditiveMask = null;
			}
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