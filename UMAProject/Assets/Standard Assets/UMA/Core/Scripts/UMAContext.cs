using System;
using UnityEngine;
using System.Collections;
using UMA;
using System.Collections.Generic;

/// <summary>
/// Gloal container for various UMA objects in the scene.
/// </summary>
public class UMAContext : MonoBehaviour 
{
	public static UMAContext Instance;
	/// <summary>
	/// The race library.
	/// </summary>
	public RaceLibraryBase raceLibrary;
	/// <summary>
	/// The slot library.
	/// </summary>
	public SlotLibraryBase slotLibrary;
	/// <summary>
	/// The overlay library.
	/// </summary>
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

	/// <summary>
	/// Validates the library contents.
	/// </summary>
	public void ValidateDictionaries()
	{
		slotLibrary.ValidateDictionary();
		raceLibrary.ValidateDictionary();
		overlayLibrary.ValidateDictionary();
	}

	/// <summary>
	/// Gets a race by name.
	/// </summary>
	/// <returns>The race.</returns>
	/// <param name="name">Name.</param>
	public RaceData GetRace(string name)
	{
		return raceLibrary.GetRace(name);
	}

	/// <summary>
	/// Gets a race by name hash.
	/// </summary>
	/// <returns>The race.</returns>
	/// <param name="nameHash">Name hash.</param>
	public RaceData GetRace(int nameHash)
	{
		return raceLibrary.GetRace(nameHash);
	}

	/// <summary>
	/// Instantiate a slot by name.
	/// </summary>
	/// <returns>The slot.</returns>
	/// <param name="name">Name.</param>
	public SlotData InstantiateSlot(string name)
	{
		return slotLibrary.InstantiateSlot(name);
	}
	/// <summary>
	/// Instantiate a slot by name hash.
	/// </summary>
	/// <returns>The slot.</returns>
	/// <param name="nameHash">Name hash.</param>
	public SlotData InstantiateSlot(int nameHash)
	{
		return slotLibrary.InstantiateSlot(nameHash);
	}

	/// <summary>
	/// Instantiate a slot by name, with overlays.
	/// </summary>
	/// <returns>The slot.</returns>
	/// <param name="name">Name.</param>
	/// <param name="overlayList">Overlay list.</param>
	public SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
	{
		return slotLibrary.InstantiateSlot(name, overlayList);
	}
	/// <summary>
	/// Instantiate a slot by name hash, with overlays.
	/// </summary>
	/// <returns>The slot.</returns>
	/// <param name="nameHash">Name hash.</param>
	/// <param name="overlayList">Overlay list.</param>
	public SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
	{
		return slotLibrary.InstantiateSlot(nameHash, overlayList);
	}

	/// <summary>
	/// Instantiate an overlay by name.
	/// </summary>
	/// <returns>The overlay.</returns>
	/// <param name="name">Name.</param>
	public OverlayData InstantiateOverlay(string name)
	{
		return overlayLibrary.InstantiateOverlay(name);
	}
	/// <summary>
	/// Instantiate an overlay by name hash.
	/// </summary>
	/// <returns>The overlay.</returns>
	/// <param name="nameHash">Name hash.</param>
	public OverlayData InstantiateOverlay(int nameHash)
	{
		return overlayLibrary.InstantiateOverlay(nameHash);
	}

	/// <summary>
	/// Instantiate a tinted overlay by name.
	/// </summary>
	/// <returns>The overlay.</returns>
	/// <param name="name">Name.</param>
	/// <param name="color">Color.</param>
	public OverlayData InstantiateOverlay(string name, Color color)
	{
		return overlayLibrary.InstantiateOverlay(name, color);
	}
	/// <summary>
	/// Instantiate a tinted overlay by name hash.
	/// </summary>
	/// <returns>The overlay.</returns>
	/// <param name="nameHash">Name hash.</param>
	/// <param name="color">Color.</param>
	public OverlayData InstantiateOverlay(int nameHash, Color color)
	{
		return overlayLibrary.InstantiateOverlay(nameHash, color);
	}

#pragma warning restore 618
	/// <summary>
	/// Finds the singleton context in the scene.
	/// </summary>
	/// <returns>The UMA context.</returns>
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
