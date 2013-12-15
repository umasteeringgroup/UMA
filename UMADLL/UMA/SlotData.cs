using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	[System.Serializable]
	public class SlotData : ScriptableObject
	{
	    public string slotName;
	    public int listID = -1;

	    public SkinnedMeshRenderer meshRenderer;
	    public Material materialSample;
	    public float overlayScale = 1.0f;
	    public Transform[] umaBoneData;
        public Transform[] animatedBones = new Transform[0];
	    public DnaConverterBehaviour slotDNA;
		public BoneWeight[] boneWeights;
			
		private List<OverlayData> overlayList = new List<OverlayData>();

	    public SlotData Duplicate()
	    {
	        SlotData tempSlotData = CreateInstance<SlotData>();

	        tempSlotData.slotName = slotName;
	        tempSlotData.listID = listID;
	        tempSlotData.materialSample = materialSample;
	        tempSlotData.overlayScale = overlayScale;
	        tempSlotData.slotDNA = slotDNA;

	        // All this data is passed as reference
			tempSlotData.boneWeights = boneWeights;
	        tempSlotData.meshRenderer = meshRenderer;
			tempSlotData.boneWeights = boneWeights;
	        tempSlotData.umaBoneData = umaBoneData;
	        //Overlays are duplicated, to lose reference
	        for (int i = 0; i < overlayList.Count; i++)
	        {
	            tempSlotData.AddOverlay(overlayList[i].Duplicate());
	        }

	        return tempSlotData;
	    }


	    public SlotData()
	    {

	    }

		public bool RemoveOverlay(params string[] names)
	    {
	        bool changed = false;
	        foreach (var name in names)
	        {
	            for (int i = 0; i < overlayList.Count; i++)
	            {
	                if (overlayList[i].overlayName == name)
	                {
	                    overlayList.RemoveAt(i);
	                    changed = true;
	                    break;
	                }
	            }
	        }
	        return changed;
	    }

		public bool SetOverlayColor(Color32 color, params string[] names)
	    {
	        bool changed = false;
	        foreach (var name in names)
	        {
	            foreach (var overlay in overlayList)
	            {
	                if (overlay.overlayName == name)
	                {
	                    overlay.color = color;
	                    changed = true;
	                }
	            }
	        }
	        return changed;
	    }

		public OverlayData GetOverlay(params string[] names)
	    {
	        foreach (var name in names)
	        {
	            foreach (var overlay in overlayList)
	            {
	                if (overlay.overlayName == name)
	                {
	                    return overlay;
	                }
	            }
	        }
	        return null;
	    }

	    public OverlayData GetOverlay(int index)
	    {
	        if (index < 0 || index >= overlayList.Count) return null;
	        return overlayList[index];
	    }

	    public int OverlayCount { get { return overlayList.Count; } }

	    public void SetOverlayList(List<OverlayData> overlayList)
	    {
	        this.overlayList = overlayList;
	    }

	    public void AddOverlay(OverlayData overlayData)
	    {
	        overlayList.Add(overlayData);
	    }

	    public List<OverlayData> GetOverlayList()
	    {
	        return overlayList;
	    }
	}
}