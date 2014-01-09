using UnityEngine;
using System.Collections;
using UnityEditor;

public class UMACrowdRandomSet : ScriptableObject 
{
	public CrowdRaceData data;

	[System.Serializable]
	public class CrowdRaceData
	{
		public string raceID;
		public CrowdSlotElement[] slotElements;
	}

	[System.Serializable]
	public class CrowdSlotElement
	{
		public CrowdSlotData[] possibleSlots;
		public string requirement;
	}

	[System.Serializable]
	public class CrowdSlotData
	{
		public string slotID;
		public bool useSharedOverlayList;
		public int overlayListSource;
		public CrowdOverlayElement[] overlayElements;
	}

	[System.Serializable]
	public class CrowdOverlayElement
	{
		public CrowdOverlayData[] possibleOverlays;
	}

	[System.Serializable]
	public class CrowdOverlayData
	{
		public string overlayID;
		public Color maxRGB;
		public Color minRGB;
		public bool useSkinColor;
		public bool useHairColor;
		public float hairColorMultiplier;
	}
}
