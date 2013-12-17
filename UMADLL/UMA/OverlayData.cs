using UnityEngine;
using System.Collections;


namespace UMA
{
	[System.Serializable]
	public class OverlayData : ScriptableObject
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
	    public void SetColor(int overlay, Color32 color)
	    {
	        if (useAdvancedMasks)
	        {
	            channelMask[overlay] = color;
	        }
	        else if (overlay == 0)
	        {
	            this.color = color;
	        }
	        else
	        {
	            AllocateAdvancedMasks();
	            channelMask[overlay] = color;
	        }
	        if (umaData != null)
	        {
	            umaData.Dirty(false, true, false);
	        }
	    }

	    public void SetAdditive(int overlay, Color32 color)
	    {
	        if (!useAdvancedMasks)
	        {
	            AllocateAdvancedMasks();
	        }
	        channelAdditiveMask[overlay] = color;
	        if (umaData != null)
	        {
	            umaData.Dirty(false, true, false);
	        }
	    }

	    private void AllocateAdvancedMasks()
	    {
	        int channels = umaData != null ? umaData.umaGenerator.textureNameList.Length : 2;
	        channelMask = new Color32[channels];
	        channelAdditiveMask = new Color32[channels];
	        for (int i = 0; i < channels; i++)
	        {
	            channelMask[i] = new Color32(255, 255, 255, 255);
	            channelAdditiveMask[i] = new Color32(0, 0, 0, 0);
	        }
	        channelMask[0] = color;

	    }

	}
}