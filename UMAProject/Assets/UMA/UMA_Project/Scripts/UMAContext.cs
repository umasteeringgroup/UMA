using System;
using UnityEngine;
using System.Collections;
using UMA;
using System.Collections.Generic;

public class UMAContext : MonoBehaviour 
{
	public static UMAContext Instance;
	[Obsolete("raceLibrary will change type from RaceLibrary to RaceLibraryBase, use the helper functions while we migrate the type.", false)]
	public RaceLibrary raceLibrary;
	[Obsolete("slotLibrary will change type from SlotLibrary to SlotLibraryBase, use the helper functions while we migrate the type.", false)]
	public SlotLibrary slotLibrary;
	[Obsolete("overlayLibrary will change type from OverlayLibrary to OverlayLibraryBase, use the helper functions while we migrate the type.", false)]
	public OverlayLibrary overlayLibrary;

#pragma warning disable 618
	public void Start()
	{
		if (!slotLibrary)
		{
			slotLibrary = GameObject.Find("SlotLibrary").GetComponent("SlotLibrary") as SlotLibrary;
		}
		if (!raceLibrary)
		{
			raceLibrary = GameObject.Find("RaceLibrary").GetComponent("RaceLibrary") as RaceLibrary;
		}
		if (!overlayLibrary)
		{
			overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent("OverlayLibrary") as OverlayLibrary;
		}
		if (Instance == null) Instance = this;
	}

	[Obsolete("UpdateDictionaries will be removed use ValidateDictionaries instead.", false)]
	public void UpdateDictionaries()
	{
		slotLibrary.UpdateDictionary();
		raceLibrary.UpdateDictionary();
		overlayLibrary.UpdateDictionary();
	}

	public void ValidateDictionaries()
	{
		slotLibrary.ValidateDictionary();
		raceLibrary.ValidateDictionary();
		overlayLibrary.ValidateDictionary();
	}

	public RaceData GetRace(string name)
	{
		return raceLibrary.GetRace(name);
	}

	public RaceData GetRace(int nameHash)
	{
		return raceLibrary.GetRace(nameHash);
	}

	public SlotData InstantiateSlot(string name)
	{
		return slotLibrary.InstantiateSlot(name);
	}
	public SlotData InstantiateSlot(int nameHash)
	{
		return slotLibrary.InstantiateSlot(nameHash);
	}

	public SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
	{
		return slotLibrary.InstantiateSlot(name, overlayList);
	}

	public SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
	{
		return slotLibrary.InstantiateSlot(nameHash, overlayList);
	}

	public OverlayData InstantiateOverlay(string name)
	{
		return overlayLibrary.InstantiateOverlay(name);
	}

	public OverlayData InstantiateOverlay(int nameHash)
	{
		return overlayLibrary.InstantiateOverlay(nameHash);
	}

	public OverlayData InstantiateOverlay(string name, Color color)
	{
		return overlayLibrary.InstantiateOverlay(name, color);
	}

	public OverlayData InstantiateOverlay(int nameHash, Color color)
	{
		return overlayLibrary.InstantiateOverlay(nameHash, color);
	}

#pragma warning restore 618

	public static UMAContext FindInstance()
	{
		if (Instance == null)
		{
			var contextGO = GameObject.Find("UMAContext");
			if (contextGO != null)
				Instance = contextGO.GetComponent<UMAContext>();
		}
		return Instance;	
	}
}
