using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Context for wrapping UMA assets stored in an asset bundle.
	/// </summary>
	public class UMAAssetBundleContext : UMAContextBase
	{
		public string bundleName;
		public string bundleURL;
		public uint bundleCRC;

		protected AssetBundle bundle;

		#if UNITY_EDITOR
		protected AssetBundleBuild bundleMap;
		public AssetBundleBuild BundleBuildMap
		{
			get {
				// Build the map;
				bundleMap = new AssetBundleBuild();
				bundleMap.assetBundleName = bundleName;
				//bundleMap.assetBundleVariant = EditorUserBuildSettings.activeBuildTarget.ToString();

				// Add assets
				List<string> addressableNames = new List<string>();
				List<string> assetNames = new List<string>();
				foreach (AssetReference reference in raceDictionary.Values)
				{
					if ((reference.asset != null) && AssetDatabase.Contains(reference.asset))
					{
						assetNames.Add(AssetDatabase.GetAssetPath(reference.asset));
						addressableNames.Add(reference.path);
					}
				}
				foreach (AssetReference reference in slotDictionary.Values)
				{
					if ((reference.asset != null) && AssetDatabase.Contains(reference.asset))
					{
						assetNames.Add(AssetDatabase.GetAssetPath(reference.asset));
						addressableNames.Add(reference.path);
					}
				}
				foreach (AssetReference reference in overlayDictionary.Values)
				{
					if ((reference.asset != null) && AssetDatabase.Contains(reference.asset))
					{
						assetNames.Add(AssetDatabase.GetAssetPath(reference.asset));
						addressableNames.Add(reference.path);
					}
				}
				foreach (AssetReference reference in dnaDictionary.Values)
				{
					if ((reference.asset != null) && AssetDatabase.Contains(reference.asset))
					{
						assetNames.Add(AssetDatabase.GetAssetPath(reference.asset));
						addressableNames.Add(reference.path);
					}
				}
				foreach (AssetReference reference in occlusionDictionary.Values)
				{
					if ((reference.asset != null) && AssetDatabase.Contains(reference.asset))
					{
						assetNames.Add(AssetDatabase.GetAssetPath(reference.asset));
						addressableNames.Add(reference.path);
					}
				}
				bundleMap.addressableNames = addressableNames.ToArray();
				bundleMap.assetNames = assetNames.ToArray();

				return bundleMap;
			}
		}
		#endif

		[System.Serializable]
		public class AssetReference
		{
			public string path;
			public Object asset;

			[System.NonSerialized]
			public AssetBundleRequest request;
		}

		[SerializeField]
		protected AssetReferenceDictionary raceDictionary;

		[SerializeField]
		protected AssetReferenceDictionary slotDictionary;

		[SerializeField]
		protected AssetReferenceDictionary overlayDictionary;

		[SerializeField]
		protected AssetReferenceDictionary dnaDictionary;

		[SerializeField]
		protected AssetReferenceDictionary occlusionDictionary;

		public class BundleContextCallback : UnityEvent<UMAAssetBundleContext>
		{
		}

		public class BundleContextEvent : UnityEvent<UMAAssetBundleContext>
		{
			public BundleContextEvent()
			{
			}

			public BundleContextEvent(BundleContextEvent source)
			{
				for (int i = 0; i < source.GetPersistentEventCount(); i++)
				{
					var target = source.GetPersistentTarget(i);
					AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new System.Type[] { typeof(UMAAssetBundleContext) }));
				}
			}
		}

		public class BundleContextLoader
		{
			protected AssetBundleCreateRequest request;

			protected BundleContextEvent bundleContextLoaded;

			public event System.Action<UMAAssetBundleContext> OnBundleContextLoaded
			{
				add
				{
					if (bundleContextLoaded == null)
						bundleContextLoaded = new BundleContextEvent();
					
					bundleContextLoaded.AddListener(new UnityAction<UMAAssetBundleContext>(value));
				}
				remove
				{
					bundleContextLoaded.RemoveListener(new UnityAction<UMAAssetBundleContext>(value));
				}
			}
		}

		static public UMAAssetBundleContext LoadFromFile(string filePath)
		{
			return null;
		}

		static public BundleContextLoader LoadFromFileAsync(string filePath)
		{
			return null;
		}

		static public BundleContextLoader LoadFromWebAsync(string wwwURL)
		{
			return null;
		}

		protected static void BundleContextLoaded(UMAAssetBundleContext context)
		{
		}

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
			AssetReference reference = null;
			if (raceDictionary.TryGetValue(nameHash, out reference))
			{
				// Blocking version
				if (reference.asset == null)
				{
					reference.asset = bundle.LoadAsset<RaceData>(reference.path);
				}

				// Asynchronous version
//				if (reference.asset == null)
//				{
//					if (reference.request == null)
//					{
//						reference.request = bundle.LoadAssetAsync<RaceData>(reference.path);
//					}
//					else if (reference.request.isDone)
//					{
//						reference.asset = reference.request.asset;
//						reference.request = null;
//					}
//				}
					
				return reference.asset as RaceData;
			}

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
			int hash = race.GetNameHash();
			AssetReference reference;
			if (raceDictionary.ContainsKey(hash))
			{
				reference = raceDictionary[hash];
				if (reference.asset != race)
				{
					Debug.LogError("Tried to add non-matching asset with duplicate hash!");
				}
			}
			else
			{
				reference = new AssetReference();
				reference.asset = race;
				reference.path = "Race/" + race.raceName;
				raceDictionary.Add(hash, reference);
			}
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
			SlotData slot = null;

			AssetReference reference = null;
			if (slotDictionary.TryGetValue(nameHash, out reference))
			{
				// Blocking version
				if (reference.asset == null)
				{
					reference.asset = bundle.LoadAsset<SlotDataAsset>(reference.path);
				}

				slot = new SlotData(reference.asset as SlotDataAsset);
			}

			return slot;
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
			SlotData slot = null;

			AssetReference reference = null;
			if (slotDictionary.TryGetValue(nameHash, out reference))
			{
				// Blocking version
				if (reference.asset == null)
				{
					reference.asset = bundle.LoadAsset<SlotDataAsset>(reference.path);
				}

				slot = new SlotData(reference.asset as SlotDataAsset);
				slot.SetOverlayList(overlayList);
			}

			return slot;
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
			return slotDictionary.ContainsKey(nameHash);
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddSlotAsset(SlotDataAsset slot)
		{
			int hash = slot.nameHash;
			AssetReference reference;
			if (slotDictionary.ContainsKey(hash))
			{
				reference = slotDictionary[hash];
				if (reference.asset != slot)
				{
					Debug.LogError("Tried to add non-matching asset with duplicate hash!");
				}
			}
			else
			{
				reference = new AssetReference();
				reference.asset = slot;
				reference.path = "Slot/" + slot.slotName;
				slotDictionary.Add(hash, reference);
			}
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
			return occlusionDictionary.ContainsKey(nameHash);
		}

		/// <summary>
		/// Add an occlusion data asset to the context.
		/// </summary>
		/// <param name="asset">New occlusion asset.</param>
		public override void AddOcclusionAsset(MeshHideAsset asset)
		{
			// HACK
			int hash = asset.asset.nameHash;
			AssetReference reference;
			if (occlusionDictionary.ContainsKey(hash))
			{
				reference = occlusionDictionary[hash];
				if (reference.asset != asset)
				{
					Debug.LogError("Tried to add non-matching asset with duplicate hash!");
				}
			}
			else
			{
				reference = new AssetReference();
				reference.asset = asset;
				reference.path = "Occlusion/" + asset.asset.slotName;
				occlusionDictionary.Add(hash, reference);
			}
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
			int hash = overlay.nameHash;
			AssetReference reference;
			if (overlayDictionary.ContainsKey(hash))
			{
				reference = overlayDictionary[hash];
				if (reference.asset != overlay)
				{
					Debug.LogError("Tried to add non-matching asset with duplicate hash!");
				}
			}
			else
			{
				reference = new AssetReference();
				reference.asset = overlay;
				reference.path = "Overlay/" + overlay.overlayName;
				overlayDictionary.Add(hash, reference);
			}
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
			return null;
		}
			
		/// <summary>
		/// Add a DNA asset to the context.
		/// </summary>
		/// <param name="dna">New DNA asset.</param>
		public override void AddDNAAsset(DynamicUMADnaAsset dnaAsset)
		{
			int hash = dnaAsset.dnaTypeHash;
			AssetReference reference;
			if (dnaDictionary.ContainsKey(hash))
			{
				reference = dnaDictionary[hash];
				if (reference.asset != dnaAsset)
				{
					Debug.LogError("Tried to add non-matching asset with duplicate hash!");
				}
			}
			else
			{
				reference = new AssetReference();
				reference.asset = dnaAsset;
				// HACK
				reference.path = "DNA/" + dnaAsset.GetAssetName();
				dnaDictionary.Add(hash, reference);
			}
		}
	}
}
