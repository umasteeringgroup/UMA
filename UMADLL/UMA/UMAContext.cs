using UnityEngine;
using System.Collections;

namespace UMA {
public class UMAContext : MonoBehaviour 
{
	public static UMAContext Instance;
	public RaceLibraryBase raceLibrary;
	public SlotLibraryBase slotLibrary;
	public OverlayLibraryBase overlayLibrary;
	public void Start()
	{
		if (!slotLibrary)
		{
			slotLibrary = GameObject.Find("SlotLibrary").GetComponent("SlotLibrary") as SlotLibraryBase;
		}
		if (!raceLibrary)
		{
			raceLibrary = GameObject.Find("RaceLibrary").GetComponent("RaceLibrary") as RaceLibraryBase;
		}
		if (!overlayLibrary)
		{
			overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent("OverlayLibrary") as OverlayLibraryBase;
		}
		if (Instance == null) Instance = this;
	}

	public void UpdateDictionaries()
	{
		slotLibrary.UpdateDictionary();
		raceLibrary.UpdateDictionary();
		overlayLibrary.UpdateDictionary();
	}

	public static UMAContext FindInstance()
	{
		return Instance != null ? Instance : GameObject.Find("UMAContext").GetComponent<UMAContext>();	
	}
}
}
