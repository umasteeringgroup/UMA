using System;
using UnityEngine;
using System.Collections;
using UMA;
using System.Collections.Generic;

public class UMAContext : MonoBehaviour 
{
	public static UMAContext Instance;
	public RaceLibraryBase raceLibrary;
	public SlotLibraryBase slotLibrary;
	public OverlayLibraryBase overlayLibrary;

#pragma warning disable 618
	public void Start()
	{
		if (!slotLibrary)
		{
			slotLibrary = GameObject.Find("SlotLibrary").GetComponent<SlotLibraryBase>();
		}
		if (!raceLibrary)
		{
			raceLibrary = GameObject.Find("RaceLibrary").GetComponent<RaceLibraryBase>();
		}
		if (!overlayLibrary)
		{
			overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent<OverlayLibraryBase>();
		}
		if (Instance == null) Instance = this;
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
