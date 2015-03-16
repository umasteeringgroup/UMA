using UnityEngine;
using System.Collections;


namespace UMA
{
	[System.Serializable]
#if !UMA_LEAN_AND_CLEAN
	public partial class OverlayData 
#else
	public class OverlayData
#endif
	{
		public OverlayDataAsset asset;

#if !UMA2_LEAN_AND_CLEAN 

		[System.Obsolete("OverlayData.overlayName is obsolete use asset.overlayName!", false)]
		public string overlayName;

		[System.Obsolete("OverlayData.listID is obsolete.", false)]
		[System.NonSerialized]
	    public int listID;

	    public Color color = new Color(1, 1, 1, 1);
		public Rect rect;

		[System.Obsolete("OverlayData.textureList is obsolete. Please refer to the OverlayDataAsset.", false)]
		public Texture[] textureList;
		public Color32[] channelMask;
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
#else
		public string overlayName { get { return asset.overlayName; } }
		public Color color = new Color(1, 1, 1, 1);
		public Rect rect;
		public Color32[] channelMask;
		public Color32[] channelAdditiveMask;
#endif

		[System.Obsolete("OverlayData.Duplicate is obsolete.", false)]
		public OverlayData Duplicate()
	    {
			var res = new OverlayData();
			res.asset = asset;
			res.rect = rect;
			res.color = color;
			res.channelMask = channelMask;
			res.channelAdditiveMask = channelAdditiveMask;
			return res;
	    }

	    public OverlayData()
	    {

	    }

		public OverlayData(OverlayDataAsset asset)
		{
			this.asset = asset;
			rect = asset.rect;			
		}

	    public bool useAdvancedMasks { get { return channelMask != null && channelMask.Length > 0; } }
        public void SetColor(int channel, Color32 color)
	    {
	        if (useAdvancedMasks)
	        {
                EnsureChannels(channel+1);
	            channelMask[channel] = color;
	        }
	        else if (channel == 0)
	        {
	            this.color = color;
	        }
	        else
	        {
	            AllocateAdvancedMasks();
                EnsureChannels(channel+1);
                channelMask[channel] = color;
	        }
	    }

        public Color32 GetColor(int channel)
        {
            if (useAdvancedMasks)
            {
                EnsureChannels(channel + 1);
                return channelMask[channel];
            }
            else if (channel == 0)
            {
                return this.color;
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
                return channelAdditiveMask[channel];
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
            channelAdditiveMask[overlay] = color;
	    }

	    private void AllocateAdvancedMasks()
	    {
			int channels = asset.textureList.Length;
			if (channels == 0) return;
            EnsureChannels(channels);
	        channelMask[0] = color;

	    }

        public void CopyColors(OverlayData overlay)
        {
            if (overlay.useAdvancedMasks)
            {
                EnsureChannels(overlay.channelAdditiveMask.Length);
                for (int i = 0; i < overlay.channelAdditiveMask.Length; i++)
                {
                    SetColor(i, overlay.GetColor(i));
                    SetAdditive(i, overlay.GetAdditive(i));
                }
            }
            else
            {
                SetColor(0, overlay.color);
            }
        }

        public void EnsureChannels(int channels)
        {
            if (channelMask == null)
            {
                channelMask = new Color32[channels];
                channelAdditiveMask = new Color32[channels];
                for (int i = 0; i < channels; i++)
                {
                    channelMask[i] = new Color32(255, 255, 255, 255);
                    channelAdditiveMask[i] = new Color32(0, 0, 0, 0);
                }
            }
            else
            {
                if( channelMask.Length > channels ) return;

                var oldMask = channelMask;
                var oldAdditive = channelAdditiveMask;
                channelMask = new Color32[channels];
                channelAdditiveMask = new Color32[channels];
                for (int i = 0; i < channels; i++)
                {
                    if (oldMask.Length > i)
                    {
                        channelMask[i] = oldMask[i];
                        channelAdditiveMask[i] = oldAdditive[i];
                    }
                    else
                    {
                        channelMask[i] = new Color32(255, 255, 255, 255);
                        channelAdditiveMask[i] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

		public static implicit operator bool(OverlayData obj) { return obj != null; }

	}
}