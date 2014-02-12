using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

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
		public string condition;
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

	public static void Apply(UMA.UMAData umaData, CrowdRaceData race, Color skinColor, Color HairColor, HashSet<string> Keywords, SlotLibraryBase slotLibrary, OverlayLibraryBase overlayLibrary)
	{
		var slotParts = new HashSet<string>();
		umaData.umaRecipe.slotDataList = new SlotData[race.slotElements.Length];
		for (int i = 0; i < race.slotElements.Length; i++)
		{
			var currentElement = race.slotElements[i];
			if (!string.IsNullOrEmpty(currentElement.requirement) && !slotParts.Contains(currentElement.requirement)) continue;
			if (!string.IsNullOrEmpty(currentElement.condition))
			{
				if (currentElement.condition.StartsWith("!"))
				{
					if (Keywords.Contains(currentElement.condition.Substring(1))) continue;
				}
				else
				{
					if (!Keywords.Contains(currentElement.condition)) continue;
				}
			}
			if (currentElement.possibleSlots.Length == 0) continue;
			int randomResult = Random.Range(0, currentElement.possibleSlots.Length);
			var slot = currentElement.possibleSlots[randomResult];
			if (string.IsNullOrEmpty(slot.slotID)) continue;
			slotParts.Add(slot.slotID);
			SlotData slotData;
			if (slot.useSharedOverlayList && slot.overlayListSource >= 0 && slot.overlayListSource < i)
			{
				slotData = slotLibrary.InstantiateSlot(slot.slotID, umaData.umaRecipe.slotDataList[slot.overlayListSource].GetOverlayList());
			}
			else
			{
				if (slot.useSharedOverlayList)
				{
					Debug.LogError("UMA Crowd: Invalid overlayListSource for " + slot.slotID);
				}
				slotData = slotLibrary.InstantiateSlot(slot.slotID);
			}
			umaData.umaRecipe.slotDataList[i] = slotData;
			for (int overlayIdx = 0; overlayIdx < slot.overlayElements.Length; overlayIdx++)
			{
				var currentOverlayElement = slot.overlayElements[overlayIdx];
				randomResult = Random.Range(0, currentOverlayElement.possibleOverlays.Length);
				var overlay = currentOverlayElement.possibleOverlays[randomResult];
				if (string.IsNullOrEmpty(overlay.overlayID)) continue;
				slotParts.Add(overlay.overlayID);
				Color overlayColor;
				if (overlay.useSkinColor)
				{
					overlayColor = skinColor + new Color(Random.Range(overlay.minRGB.r, overlay.maxRGB.r), Random.Range(overlay.minRGB.g, overlay.maxRGB.g), Random.Range(overlay.minRGB.b, overlay.maxRGB.b), 1);
				}
				else if (overlay.useHairColor)
				{
					overlayColor = HairColor * overlay.hairColorMultiplier;
				}
				else
				{
					overlayColor = new Color(Random.Range(overlay.minRGB.r, overlay.maxRGB.r), Random.Range(overlay.minRGB.g, overlay.maxRGB.g), Random.Range(overlay.minRGB.b, overlay.maxRGB.b), 1);
				}
				slotData.AddOverlay(overlayLibrary.InstantiateOverlay(overlay.overlayID, overlayColor));
			}
			if (umaData.umaRecipe.slotDataList[i].GetOverlayList().Count == 0)
			{
				Debug.LogError("Slot without overlay: " + umaData.umaRecipe.slotDataList[i].slotName + " at index " + i + " of race: " + race.raceID);
			}
		}
	}
}
