using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Global factory for instantiating various UMA objects.
	/// </summary>
	public abstract class UMAContextBase : MonoBehaviour
	{
		public static UMAContextBase Instance;

		/// <summary>
		/// Gets a race by name.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public abstract RaceDataAsset GetRace(string name);
		/// <summary>
		/// Gets a race by name hash.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract RaceDataAsset GetRace(int nameHash);

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public abstract RaceDataAsset[] GetAllRaces();

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public abstract void AddRace(RaceDataAsset race);

		/// <summary>
		/// Instantiate a slot by name.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		public abstract SlotData InstantiateSlot(string name);
		/// <summary>
		/// Instantiate a slot by name hash.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract SlotData InstantiateSlot(int nameHash);

		/// <summary>
		/// Instantiate a slot by name, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		/// <param name="overlayList">Overlay list.</param>
		public abstract SlotData InstantiateSlot(string name, List<OverlayData> overlayList);
		/// <summary>
		/// Instantiate a slot by name hash, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="overlayList">Overlay list.</param>
		public abstract SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList);

		/// <summary>
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public abstract bool HasSlot(string name);
		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract bool HasSlot(int nameHash);

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public abstract void AddSlotAsset(SlotDataAsset slot);

		/// <summary>
		/// Check for presence of slot occlusion data by name.
		/// </summary>
		/// <returns><c>True</c> if there is occlusion data for the slot in this context.</returns>
		/// <param name="name">Name.</param>
		public abstract bool HasOcclusion(string name);
		/// <summary>
		/// Check for presence of slot occlusion data by name hash.
		/// </summary>
		/// <returns><c>True</c> if occlusion data for the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract bool HasOcclusion(int nameHash);

		/// <summary>
		/// Add a ocllusion data asset to the context.
		/// </summary>
		/// <param name="slot">New occluysion asset.</param>
		public abstract void AddOcclusionAsset(OcclusionDataAsset asset);

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public abstract bool HasOverlay(string name);
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract bool HasOverlay(int nameHash);

		/// <summary>
		/// Instantiate an overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public abstract OverlayData InstantiateOverlay(string name);
		/// <summary>
		/// Instantiate an overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract OverlayData InstantiateOverlay(int nameHash);

		/// <summary>
		/// Instantiate a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Color.</param>
		public abstract OverlayData InstantiateOverlay(string name, Color color);
		/// <summary>
		/// Instantiate a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Color.</param>
		public abstract OverlayData InstantiateOverlay(int nameHash, Color color);

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public abstract void AddOverlayAsset(OverlayDataAsset overlay);

		/// <summary>
		/// Check for presence of a DNA type by name.
		/// </summary>
		/// <returns><c>True</c> if the DNA exists in this context.</returns>
		/// <param name="name">Name.</param>
		public abstract bool HasDNA(string name);
		/// <summary>
		/// Check for presence of an DNA type by name hash.
		/// </summary>
		/// <returns><c>True</c> if the DNA exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract bool HasDNA(int nameHash);

		/// <summary>
		/// Instantiate DNA by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public abstract UMADnaBase InstantiateDNA(string name);
		/// <summary>
		/// Instantiate DNA by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract UMADnaBase InstantiateDNA(int nameHash);
			
		/// <summary>
		/// Add a DNA asset to the context.
		/// </summary>
		/// <param name="dna">New DNA asset.</param>
		public abstract void AddDNAAsset(DNADataAsset dnaAsset);

		/// <summary>
		/// Finds the singleton context in the scene.
		/// </summary>
		/// <returns>The UMA context.</returns>
		public static UMAContextBase FindInstance()
		{
			if (Instance == null)
			{
				var contextGO = GameObject.Find("UMAContext");
				if (contextGO != null)
					Instance = contextGO.GetComponent<UMAContextBase>();
			}
			if (Instance == null)
			{
				Instance = Component.FindObjectOfType<UMAContextBase>();
			}
			return Instance;	
		}
	}
}
