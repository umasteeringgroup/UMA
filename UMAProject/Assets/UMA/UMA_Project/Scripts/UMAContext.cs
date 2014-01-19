using UnityEngine;
using System.Collections;

public class UMAContext : MonoBehaviour 
{
	public static UMAContext Instance;
	public RaceLibrary raceLibrary;
	public SlotLibrary slotLibrary;
	public OverlayLibrary overlayLibrary;
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

#if UNITY_EDITOR
	public void UpdateDictionaries()
	{
		slotLibrary.UpdateDictionary();
		raceLibrary.UpdateDictionary();
		overlayLibrary.UpdateDictionary();
	}
#endif

	public static UMAContext FindInstance()
	{
		return Instance != null ? Instance : GameObject.Find("UMAContext").GetComponent<UMAContext>();	
	}
}
