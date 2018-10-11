using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// UMA context that delegates to a set of active subcontexts
	/// </summary>
	public class UMACompoundContext
	{
		#if UNITY_EDITOR
		public bool allowAssetSearch = true;

		protected RaceAssetDictionary raceDictionary = null;
		protected SlotAssetDictionary slotDictionary = null;
		protected OverlayAssetDictionary overlayDictionary = null;
		protected DNAAssetDictionary dnaDictionary = null;
		protected OcclusionAssetDictionary occlusionDictionary = null;

		protected void BuildRaceAssetDictionary()
		{
			string type = typeof(RaceDataAsset).Name;
			Debug.LogWarning(string.Format("Searching asset database for {0} missing from context.", type));
			raceDictionary = new RaceAssetDictionary();

			string[] assetGUIDs = AssetDatabase.FindAssets("t:"+type);
			foreach (string guid in assetGUIDs)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				RaceDataAsset asset = AssetDatabase.LoadAssetAtPath<RaceDataAsset>(path);
				raceDictionary.Add(asset.GetNameHash(), asset);
			}
		}

		protected void BuildSlotAssetDictionary()
		{
			string type = typeof(SlotDataAsset).Name;
			Debug.LogWarning(string.Format("Searching asset database for {0} missing from context.", type));
			slotDictionary = new SlotAssetDictionary();

			string[] assetGUIDs = AssetDatabase.FindAssets("t:"+type);
			foreach (string guid in assetGUIDs)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				SlotDataAsset asset = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
				slotDictionary.Add(asset.umaHash, asset);
			}
		}

		protected void BuildOverlayAssetDictionary()
		{
			string type = typeof(OverlayDataAsset).Name;
			Debug.LogWarning(string.Format("Searching asset database for {0} missing from context.", type));
			overlayDictionary = new OverlayAssetDictionary();

			string[] assetGUIDs = AssetDatabase.FindAssets("t:"+type);
			foreach (string guid in assetGUIDs)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				OverlayDataAsset asset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(path);
				overlayDictionary.Add(asset.umaHash, asset);
			}
		}

		protected void BuildDNAAssetDictionary()
		{
			string type = typeof(DNADataAsset).Name;
			Debug.LogWarning(string.Format("Searching asset database for {0} missing from context.", type));
			dnaDictionary = new DNAAssetDictionary();

			string[] assetGUIDs = AssetDatabase.FindAssets("t:"+type);
			foreach (string guid in assetGUIDs)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				DNADataAsset asset = AssetDatabase.LoadAssetAtPath<DNADataAsset>(path);
				dnaDictionary.Add(asset.umaHash, asset);
			}
		}

		protected void BuildOcclusionAssetDictionary()
		{
			string type = typeof(OcclusionDataAsset).Name;
			Debug.LogWarning(string.Format("Searching asset database for {0} missing from context.", type));
			occlusionDictionary = new OcclusionAssetDictionary();

			string[] assetGUIDs = AssetDatabase.FindAssets("t:"+type);
			foreach (string guid in assetGUIDs)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				OcclusionDataAsset asset = AssetDatabase.LoadAssetAtPath<OcclusionDataAsset>(path);
				occlusionDictionary.Add(asset.umaHash, asset);
			}
		}
		#endif

		protected List<UMAContextBase> contexts = new List<UMAContextBase>();

		/// <summary>
		/// Add an additional subcontext.
		/// </summary>
		/// <param name="context">Context.</param>
		public void AddContext(UMAContextBase context, int priority)
		{
			if (!contexts.Contains(context))
			{
				contexts.Add(context);
			}
		}

		/// <summary>
		/// Remove a subcontext.
		/// </summary>
		/// <param name="context">Context.</param>
		public void RemoveContext(UMAContextBase context)
		{
			if (contexts.Contains(context))
			{
				contexts.Remove(context);
			}
		}

		#region RaceData
		/// <summary>
		/// Gets a race by name.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public RaceDataAsset GetRace(string name)
		{
			RaceDataAsset race = null;
			foreach (UMAContextBase context in contexts)
			{
				race = context.GetRace(name);
				if (race != null) return race;
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (raceDictionary == null) BuildRaceAssetDictionary();

				raceDictionary.TryGetValue(UMAUtils.StringToHash(name), out race);
			}
			#endif

			return race;
		}
		/// <summary>
		/// Gets a race by name hash.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public RaceDataAsset GetRace(int nameHash)
		{
			RaceDataAsset race = null;
			foreach (UMAContextBase context in contexts)
			{
				race = context.GetRace(nameHash);
				if (race != null) return race;
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (raceDictionary == null) BuildRaceAssetDictionary();

				raceDictionary.TryGetValue(nameHash, out race);
			}
			#endif

			return race;
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public RaceDataAsset[] GetAllRaces()
		{
			// HACK - is this needed? If so combine
			return null;
		}

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public void AddRace(RaceDataAsset race)
		{
			Debug.LogError("Cannot include assets in compound context, add to scene or asset bundle!");
		}
		#endregion

		#region SlotData
		/// <summary>
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasSlot(string name)
		{
			return HasSlot(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasSlot(int nameHash)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasSlot(nameHash)) return true;
			}

			return false;
		}

		/// <summary>
		/// Instantiate a slot by name.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		public SlotData InstantiateSlot(string name)
		{
			return InstantiateSlot(UMAUtils.StringToHash(name));
		}

		/// <summary>
		/// Instantiate a slot by name hash.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		public SlotData InstantiateSlot(int nameHash)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasSlot(nameHash))
				{
					return context.InstantiateSlot(nameHash);
				}
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (slotDictionary == null) BuildSlotAssetDictionary();

				SlotDataAsset asset;
				if (slotDictionary.TryGetValue(nameHash, out asset))
				{
					return new SlotData(asset);
				}
			}
			#endif

			return null;
		}

		/// <summary>
		/// Instantiate a slot by name, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		/// <param name="overlayList">Overlay list.</param>
		public SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
		{
			return InstantiateSlot(UMAUtils.StringToHash(name), overlayList);
		}
		/// <summary>
		/// Instantiate a slot by name hash, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="overlayList">Overlay list.</param>
		public SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasSlot(nameHash))
				{
					return context.InstantiateSlot(nameHash, overlayList);
				}
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (slotDictionary == null) BuildSlotAssetDictionary();

				SlotDataAsset asset;
				if (slotDictionary.TryGetValue(nameHash, out asset))
				{
					SlotData slot = new SlotData(asset);
					slot.SetOverlayList(overlayList);
				}
			}
			#endif

			return null;
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public void AddSlotAsset(SlotDataAsset slot)
		{
			Debug.LogError("Cannot include assets in compound context, add to scene or asset bundle!");
		}
		#endregion

		#region OcclusionData
		/// <summary>
		/// Check for presence of slot occlusion data by name.
		/// </summary>
		/// <returns><c>True</c> if there is occlusion data for the slot in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasOcclusion(string name)
		{
			return HasOcclusion(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of slot occlusion data by name hash.
		/// </summary>
		/// <returns><c>True</c> if occlusion data for the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasOcclusion(int nameHash)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasOcclusion(nameHash)) return true;
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (occlusionDictionary == null) BuildOcclusionAssetDictionary();

				return occlusionDictionary.ContainsKey(nameHash);
			}
			#endif

			return false;
		}

		/// <summary>
		/// Add a ocllusion data asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public void AddOcclusionAsset(MeshHideAsset asset)
		{
			Debug.LogError("Cannot include assets in compound context, add to scene or asset bundle!");
		}
		#endregion

		#region OverlayData
		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasOverlay(string name)
		{
			return HasOverlay(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasOverlay(int nameHash)
		{ 
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasOverlay(nameHash)) return true;
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (overlayDictionary == null) BuildOverlayAssetDictionary();

				return overlayDictionary.ContainsKey(nameHash);
			}
			#endif

			return false;
		}

		/// <summary>
		/// Instantiate an overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public OverlayData InstantiateOverlay(string name)
		{
			return InstantiateOverlay(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Instantiate an overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public OverlayData InstantiateOverlay(int nameHash)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasOverlay(nameHash))
				{
					return context.InstantiateOverlay(nameHash);
				}
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (overlayDictionary == null) BuildOverlayAssetDictionary();

				OverlayDataAsset asset;
				if (overlayDictionary.TryGetValue(nameHash, out asset))
				{
					return new OverlayData(asset);
				}
			}
			#endif

			return null;
		}

		/// <summary>
		/// Instantiate a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Color.</param>
		public OverlayData InstantiateOverlay(string name, Color color)
		{
			return InstantiateOverlay(UMAUtils.StringToHash(name), color);
		}
		/// <summary>
		/// Instantiate a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Color.</param>
		public OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasOverlay(nameHash))
				{
					return context.InstantiateOverlay(nameHash, color);
				}
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (overlayDictionary == null) BuildOverlayAssetDictionary();

				OverlayDataAsset asset;
				if (overlayDictionary.TryGetValue(nameHash, out asset))
				{
					OverlayData overlay = new OverlayData(asset);
					overlay.colorData.color = color;
					return overlay;
				}
			}
			#endif

			return null;
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public void AddOverlayAsset(OverlayDataAsset overlay)
		{
			Debug.LogError("Cannot include assets in compound context, add to scene or asset bundle!");
		}
		#endregion

		#region DNAData
		/// <summary>
		/// Check for presence of a DNA type by name.
		/// </summary>
		/// <returns><c>True</c> if the DNA exists in this context.</returns>
		/// <param name="name">Name.</param>
		public bool HasDNA(string name)
		{
			return HasDNA(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Check for presence of an DNA type by name hash.
		/// </summary>
		/// <returns><c>True</c> if the DNA exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public bool HasDNA(int nameHash)
		{ 
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasDNA(nameHash)) return true;
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (dnaDictionary == null) BuildDNAAssetDictionary();

				return dnaDictionary.ContainsKey(nameHash);
			}
			#endif

			return false;
		}

		/// <summary>
		/// Instantiate DNA by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public UMADnaBase InstantiateDNA(string name)
		{
			return InstantiateDNA(UMAUtils.StringToHash(name));
		}
		/// <summary>
		/// Instantiate DNA by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public UMADnaBase InstantiateDNA(int nameHash)
		{
			foreach (UMAContextBase context in contexts)
			{
				if (context.HasDNA(nameHash))
				{
					return context.InstantiateDNA(nameHash);
				}
			}

			#if UNITY_EDITOR
			if (allowAssetSearch)
			{
				if (dnaDictionary == null) BuildDNAAssetDictionary();

				if (dnaDictionary.ContainsKey(nameHash))
				{
					// HACK
					return null;
				}
			}
			#endif

			return null;
		}
			
		/// <summary>
		/// Add a DNA asset to the context.
		/// </summary>
		/// <param name="dna">New DNA asset.</param>
		public void AddDNAAsset(DynamicUMADnaAsset dnaAsset)
		{
			Debug.LogError("Cannot include assets in compound context, add to scene or asset bundle!");
		}
		#endregion
	}
}
