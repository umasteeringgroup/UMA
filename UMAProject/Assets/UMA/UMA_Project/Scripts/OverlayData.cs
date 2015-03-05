using UnityEngine;
using System.Collections;


namespace UMA
{
	[System.Serializable]
	public partial class OverlayData : ScriptableObject
	{
	    public string overlayName;
	    [System.NonSerialized]
	    public int listID;

	    public Color color = new Color(1, 1, 1, 1);
	    public Rect rect;
	    public Texture2D[] textureList;
	    public Color32[] channelMask;
	    public Color32[] channelAdditiveMask;
	    [System.NonSerialized]
	    public UMAData umaData;
        /// <summary>
        /// Use this to identify what kind of overlay this is and what it fits
        /// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
        /// </summary>
        public string[] tags;

	    public OverlayData Duplicate()
	    {
	        OverlayData tempOverlay = CreateInstance<OverlayData>();
	        tempOverlay.overlayName = overlayName;
	        tempOverlay.listID = listID;
	        tempOverlay.color = color;
	        tempOverlay.rect = rect;
	        tempOverlay.textureList = new Texture2D[textureList.Length];
	        for (int i = 0; i < textureList.Length; i++)
	        {
	            tempOverlay.textureList[i] = textureList[i];
	        }

	        return tempOverlay;
	    }

	    public OverlayData()
	    {

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
	        if (umaData != null)
	        {
	            umaData.Dirty(false, true, false);
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
	        if (umaData != null)
	        {
	            umaData.Dirty(false, true, false);
	        }
	    }

	    private void AllocateAdvancedMasks()
	    {
	        int channels = umaData != null ? umaData.umaGenerator.textureNameList.Length : 2;
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

	}
}