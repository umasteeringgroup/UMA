using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Gloal container for various UMA objects in the scene. Marked as partial so the developer can add to this if necessary
	/// </summary>
	public partial class UMAContext : MonoBehaviour
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
			// Note: Removed null check so that this is always assigned if you have a UMAContext in your scene
			// This will avoid those occasions where someone drops in a bogus context in a test scene, and then 
			// later loads a valid scene (and everything breaks)
			Instance = this;
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
		/// Gets a race by name, if it has been added to the library
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public RaceData HasRace(string name)
		{
			return raceLibrary.HasRace(name);
		}
		/// <summary>
		/// Gets a race by name hash, if it has been added to the library.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public RaceData HasRace(int nameHash)
		{
			return raceLibrary.HasRace(nameHash);
		}

		/// <summary>
		/// Gets a race by name, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public RaceData GetRace(string name)
		{
			return raceLibrary.GetRace(name);
		}
		/// <summary>
		/// Gets a race by name hash, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public RaceData GetRace(int nameHash)
		{
			return raceLibrary.GetRace(nameHash);
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public RaceData[] GetAllRaces()
		{
			return raceLibrary.GetAllRaces();
		}

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public void AddRace(RaceData race)
		{
			raceLibrary.AddRace(race);
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
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasSlot(string name)
		{
			return slotLibrary.HasSlot(name);
		}
		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasSlot(int nameHash)
		{ 
			return slotLibrary.HasSlot(nameHash);
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public void AddSlotAsset(SlotDataAsset slot)
		{
			slotLibrary.AddSlotAsset(slot);
		}

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasOverlay(string name)
		{
			return overlayLibrary.HasOverlay(name);
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasOverlay(int nameHash)
		{ 
			return overlayLibrary.HasOverlay(nameHash);
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

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public void AddOverlayAsset(OverlayDataAsset overlay)
		{
			overlayLibrary.AddOverlayAsset(overlay);
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
			if (Instance == null)
			{
				Instance = Component.FindObjectOfType<UMAContext>();
			}
			return Instance;	
		}
	}
}
