using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Simple scene based container for instantiating various UMA objects.
	/// </summary>
	public class UMASceneContext : UMAContextBase
	{
		/// <summary>
		/// The race library.
		/// </summary>
		[SerializeField]
		protected RaceLibraryBase raceLibrary;
		/// <summary>
		/// The slot library.
		/// </summary>
		[SerializeField]
		protected SlotLibraryBase slotLibrary;
		/// <summary>
		/// The overlay library.
		/// </summary>
		[SerializeField]
		protected OverlayLibraryBase overlayLibrary;

		// HACK is this going to stay? Maybe ditch the libraries completely?
		[SerializeField]
		protected DNALibrary dnaLibrary;

		[SerializeField]
		protected RaceAssetDictionary raceDictionary;

		[SerializeField]
		protected SlotAssetDictionary slotDictionary;

		[SerializeField]
		protected OverlayAssetDictionary overlayDictionary;

		[SerializeField]
		protected DNAAssetDictionary dnaDictionary;

		[SerializeField]
		protected OcclusionAssetDictionary occlusionDictionary;

		public void Start()
		{
			if (!raceLibrary && (raceDictionary == null))
			{
				raceLibrary = GameObject.Find("RaceLibrary").GetComponent<RaceLibraryBase>();
			}
			if (!slotLibrary && (slotDictionary == null))
			{
				slotLibrary = GameObject.Find("SlotLibrary").GetComponent<SlotLibraryBase>();
			}
			if (!overlayLibrary && (overlayDictionary == null))
			{
				overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent<OverlayLibraryBase>();
			}
			if (!dnaLibrary && (dnaDictionary == null))
			{
				dnaLibrary = GameObject.Find("DNALibrary").GetComponent<DNALibrary>();
			}
			// Note: Removed null check so that this is always assigned if you have a UMAContext in your scene
			// This will avoid those occasions where someone drops in a bogus context in a test scene, and then 
			// later loads a valid scene (and everything breaks)
			Instance = this;

			#if UNITY_EDITOR
//			if (raceDictionary == null)
			{
				raceDictionary = new RaceAssetDictionary();
				if (raceLibrary != null)
				{
					Debug.LogWarning("Updating race library on " + this.name);
					RaceDataAsset[] races = raceLibrary.GetAllRaces();
					foreach (RaceDataAsset race in races)
					{
						if (race == null) continue;
						raceDictionary.Add(race.GetNameHash(), race);
					}
//					raceLibrary = null;
				}
			}
//			if (slotDictionary == null)
			{
				slotDictionary = new SlotAssetDictionary();
				if (slotLibrary != null)
				{
					Debug.LogWarning("Updating slot library on " + this.name);
					SlotDataAsset[] slots = slotLibrary.GetAllSlotAssets();
					foreach (SlotDataAsset slot in slots)
					{
						if (slot == null) continue;
						slotDictionary.Add(slot.nameHash, slot);
					}
//					slotLibrary = null;
				}
			}
//			if (overlayDictionary == null)
			{
				overlayDictionary = new OverlayAssetDictionary();
				if (overlayLibrary != null)
				{
					Debug.LogWarning("Updating overlay library on " + this.name);
					OverlayDataAsset[] overlays = overlayLibrary.GetAllOverlayAssets();
					foreach (OverlayDataAsset overlay in overlays)
					{
						if (overlay == null) continue;
						overlayDictionary.Add(overlay.nameHash, overlay);
					}
//					overlayLibrary = null;
				}
			}
//			if (dnaDictionary == null)
			{
				dnaDictionary = new DNAAssetDictionary();
				if (dnaLibrary != null)
				{
					Debug.LogWarning("Updating DNA library on " + this.name);
					DynamicUMADnaAsset[] DNAs = dnaLibrary.GetAllDNAAssets();
					foreach (DynamicUMADnaAsset dna in DNAs)
					{
						if (dna == null) continue;
						dnaDictionary.Add(dna.dnaTypeHash, dna);
					}
//					dnaLibrary = null;
				}
			}
//			if (occlusionDictionary == null)
			{
				occlusionDictionary = new OcclusionAssetDictionary();
			}
			#endif

			UMAGlobal.Context.AddContext(this, 0);
		}

		/// <summary>
		/// Validates the library contents.
		/// </summary>
//		public void ValidateDictionaries()
//		{
//			slotLibrary.ValidateDictionary();
//			raceLibrary.ValidateDictionary();
//			overlayLibrary.ValidateDictionary();
//			dnaLibrary.ValidateDictionary();
//		}

		/// <summary>
		/// Gets a race by name.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceDataAsset GetRace(string name)
		{
			return GetRace(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Gets a race by name hash.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceDataAsset GetRace(int nameHash)
		{
//			return raceLibrary.GetRace(nameHash);
			RaceDataAsset race = null;
			raceDictionary.TryGetValue(nameHash, out race);

			return race;
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public override RaceDataAsset[] GetAllRaces()
		{
//			return raceLibrary.GetAllRaces();
			// HACK
			RaceDataAsset[] raceArray = new RaceDataAsset[raceDictionary.Count];
			raceDictionary.Values.CopyTo(raceArray, 0);

			return raceArray;
		}

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public override void AddRace(RaceDataAsset race)
		{
//			raceLibrary.AddRace(race);
			if (race == null) return;
			raceDictionary.Add(race.GetNameHash(), race);
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
//			return slotLibrary.InstantiateSlot(nameHash);
			SlotDataAsset asset;
			if (slotDictionary.TryGetValue(nameHash, out asset))
			{
				return new SlotData(asset);
			}

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
			//return slotLibrary.InstantiateSlot(nameHash, overlayList);
			SlotDataAsset asset;
			if (slotDictionary.TryGetValue(nameHash, out asset))
			{
				SlotData slot = new SlotData(asset);
				slot.SetOverlayList(overlayList);
				return slot;
			}

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
//			return slotLibrary.HasSlot(nameHash);
			return slotDictionary.ContainsKey(nameHash);
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddSlotAsset(SlotDataAsset slot)
		{
//			slotLibrary.AddSlotAsset(slot);
			slotDictionary.Add(slot.nameHash, slot);
		}

		/// <summary>
		/// Check for presence of slot occlusion data by name.
		/// </summary>
		/// <returns><c>True</c> if there is occlusion data for the slot in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasOcclusion(string name)
		{
			return HasOcclusion(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of slot occlusion data by name hash.
		/// </summary>
		/// <returns><c>True</c> if occlusion data for the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasOcclusion(int nameHash)
		{
			return occlusionDictionary.ContainsKey(nameHash);
		}

		/// <summary>
		/// Add a ocllusion data asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddOcclusionAsset(OcclusionDataAsset asset)
		{
			// HACK
			occlusionDictionary.Add(asset.umaHash, asset);
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
//			return overlayLibrary.HasOverlay(nameHash);
			return overlayDictionary.ContainsKey(nameHash);
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
//			return overlayLibrary.InstantiateOverlay(nameHash);
			OverlayDataAsset asset;
			if (overlayDictionary.TryGetValue(nameHash, out asset))
			{
				return new OverlayData(asset);
			}

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
//			return overlayLibrary.InstantiateOverlay(nameHash, color);
			OverlayDataAsset asset;
			if (overlayDictionary.TryGetValue(nameHash, out asset))
			{
				OverlayData overlay = new OverlayData(asset);
				overlay.colorData.color = color;
				return overlay;
			}

			return null;
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public override void AddOverlayAsset(OverlayDataAsset overlay)
		{
//			overlayLibrary.AddOverlayAsset(overlay);
			overlayDictionary.Add(overlay.nameHash, overlay);
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
//			return dnaLibrary.HasDNA(nameHash);
			return dnaDictionary.ContainsKey(nameHash);
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
//			return dnaLibrary.InstantiateDNA(nameHash);
			DynamicUMADnaAsset asset;
			if (dnaDictionary.TryGetValue(nameHash, out asset))
			{
				DynamicUMADna dna = new DynamicUMADna(nameHash);
				// HACK - really???
				dna.dnaAsset = asset;

				return dna;
			}

			return null;
		}
			
		/// <summary>
		/// Add a DNA asset to the context.
		/// </summary>
		/// <param name="dna">New DNA asset.</param>
		public override void AddDNAAsset(DynamicUMADnaAsset dnaAsset)
		{
//			dnaLibrary.AddDNAAsset(dnaAsset);
			dnaDictionary.Add(dnaAsset.dnaTypeHash, dnaAsset);
		}
	}
}
