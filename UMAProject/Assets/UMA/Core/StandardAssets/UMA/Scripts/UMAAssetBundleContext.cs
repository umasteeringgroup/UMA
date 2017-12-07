using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Simple scene based container for instantiating various UMA objects.
	/// </summary>
	public class UMAAssetBundleContext : UMAContextBase
	{
		public string bundleName;

		public string bundleURL;

		[SerializeField]
		protected AssetBundle bundle;

#if UNITY_EDITOR
		protected AssetBundleBuild bundleBuild;
#endif

		/// <summary>
		/// Gets a race by name.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceData GetRace(string name)
		{
			return GetRace(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Gets a race by name hash.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceData GetRace(int nameHash)
		{
			return null;
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public override RaceData[] GetAllRaces()
		{
			return null;
		}

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public override void AddRace(RaceData race)
		{
		}

		/// <summary>
		/// Instantiate a slot by name.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		public override SlotData InstantiateSlot(string name)
		{
			return InstantiateSlot(UMAUtils.StringToHash(name));
		}

		/// <summary>
		/// Instantiate a slot by name hash.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override SlotData InstantiateSlot(int nameHash)
		{
			return null;
		}

		/// <summary>
		/// Instantiate a slot by name, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		/// <param name="overlayList">Overlay list.</param>
		public override SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
		{
			return InstantiateSlot(UMAUtils.StringToHash(name), overlayList);
		}
		/// <summary>
		/// Instantiate a slot by name hash, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="overlayList">Overlay list.</param>
		public override SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
		{
			return null;
		}

		/// <summary>
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasSlot(string name)
		{
			return HasSlot(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasSlot(int nameHash)
		{ 
			return false;
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddSlotAsset(SlotDataAsset slot)
		{
		}

		/// <summary>
		/// Check for presence of slot occlusion data by name.
		/// </summary>
		/// <returns><c>True</c> if there is occlusion data for the slot in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasOcclusion(string name)
		{
			return false;
		}
		/// <summary>
		/// Check for presence of slot occlusion data by name hash.
		/// </summary>
		/// <returns><c>True</c> if occlusion data for the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasOcclusion(int nameHash)
		{
			return false;
		}

		/// <summary>
		/// Add a ocllusion data asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddOcclusionAsset(MeshHideAsset asset)
		{
		}

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasOverlay(string name)
		{
			return HasOverlay(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasOverlay(int nameHash)
		{ 
			return false;
		}

		/// <summary>
		/// Instantiate an overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public override OverlayData InstantiateOverlay(string name)
		{
			return InstantiateOverlay(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Instantiate an overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override OverlayData InstantiateOverlay(int nameHash)
		{
			return null;
		}

		/// <summary>
		/// Instantiate a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Color.</param>
		public override OverlayData InstantiateOverlay(string name, Color color)
		{
			return InstantiateOverlay(UMAUtils.StringToHash(name), color);
		}
		/// <summary>
		/// Instantiate a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Color.</param>
		public override OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			return null;
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public override void AddOverlayAsset(OverlayDataAsset overlay)
		{
		}

		/// <summary>
		/// Check for presence of a DNA type by name.
		/// </summary>
		/// <returns><c>True</c> if the DNA exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasDNA(string name)
		{
			return HasDNA(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of an DNA type by name hash.
		/// </summary>
		/// <returns><c>True</c> if the DNA exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasDNA(int nameHash)
		{ 
			return false;
		}

		/// <summary>
		/// Instantiate DNA by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public override UMADnaBase InstantiateDNA(string name)
		{
			return InstantiateDNA(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Instantiate DNA by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override UMADnaBase InstantiateDNA(int nameHash)
		{
			return null;
		}
			
		/// <summary>
		/// Add a DNA asset to the context.
		/// </summary>
		/// <param name="dna">New DNA asset.</param>
		public override void AddDNAAsset(DynamicUMADnaAsset dnaAsset)
		{
		}
	}
}
