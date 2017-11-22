using UnityEngine;
using UMA.CharacterSystem;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Gloal container for various UMA objects in the scene. Marked as partial so the developer can add to this if necessary
	/// </summary>
	public class DynamicUMAContext : UMAContextBase
	{
		/// <summary>
		/// The race library.
		/// </summary>
		public DynamicRaceLibrary raceLibrary;
		/// <summary>
		/// The slot library.
		/// </summary>
		public DynamicSlotLibrary slotLibrary;
		/// <summary>
		/// The overlay library.
		/// </summary>
		public DynamicOverlayLibrary overlayLibrary;

		#pragma warning disable 618
		public void Start()
		{
			if (!slotLibrary)
			{
				slotLibrary = GameObject.Find("SlotLibrary").GetComponent<DynamicSlotLibrary>();
			}
			if (!raceLibrary)
			{
				raceLibrary = GameObject.Find("RaceLibrary").GetComponent<DynamicRaceLibrary>();
			}
			if (!overlayLibrary)
			{
				overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent<DynamicOverlayLibrary>();
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
		/// The DynamicCharacterSystem
		/// </summary>
		public UMA.CharacterSystem.DynamicCharacterSystemBase dynamicCharacterSystem;
		/// <summary>
		/// Gets a race by name.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public override RaceData GetRace(string name)
		{
			return raceLibrary.GetRace(name);
		}
		/// <summary>
		/// Gets a race by name hash.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override RaceData GetRace(int nameHash)
		{
			return raceLibrary.GetRace(nameHash);
		}

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public override RaceData[] GetAllRaces()
		{
			return raceLibrary.GetAllRaces();
		}

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public override void AddRace(RaceData race)
		{
			raceLibrary.AddRace(race);
		}

		/// <summary>
		/// Instantiate a slot by name.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		public override SlotData InstantiateSlot(string name)
		{
			return slotLibrary.InstantiateSlot(name);
		}

		/// <summary>
		/// Instantiate a slot by name hash.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override SlotData InstantiateSlot(int nameHash)
		{
			return slotLibrary.InstantiateSlot(nameHash);
		}

		/// <summary>
		/// Instantiate a slot by name, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="name">Name.</param>
		/// <param name="overlayList">Overlay list.</param>
		public override SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
		{
			return slotLibrary.InstantiateSlot(name, overlayList);
		}
		/// <summary>
		/// Instantiate a slot by name hash, with overlays.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="overlayList">Overlay list.</param>
		public override SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
		{
			return slotLibrary.InstantiateSlot(nameHash, overlayList);
		}

		/// <summary>
		/// Check for presence of a slot by name.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasSlot(string name)
		{
			return slotLibrary.HasSlot(name);
		}
		/// <summary>
		/// Check for presence of a slot by name hash.
		/// </summary>
		/// <returns><c>True</c> if the slot exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasSlot(int nameHash)
		{ 
			return slotLibrary.HasSlot(nameHash);
		}

		/// <summary>
		/// Add a slot asset to the context.
		/// </summary>
		/// <param name="slot">New slot asset.</param>
		public override void AddSlotAsset(SlotDataAsset slot)
		{
			slotLibrary.AddSlotAsset(slot);
		}

		/// <summary>
		/// Check for presence of an overlay by name.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="name">Name.</param>
		public override bool HasOverlay(string name)
		{
			return overlayLibrary.HasOverlay(name);
		}
		/// <summary>
		/// Check for presence of an overlay by name hash.
		/// </summary>
		/// <returns><c>True</c> if the overlay exists in this context.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasOverlay(int nameHash)
		{ 
			return overlayLibrary.HasOverlay(nameHash);
		}

		/// <summary>
		/// Instantiate an overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		public override OverlayData InstantiateOverlay(string name)
		{
			return overlayLibrary.InstantiateOverlay(name);
		}
		/// <summary>
		/// Instantiate an overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override OverlayData InstantiateOverlay(int nameHash)
		{
			return overlayLibrary.InstantiateOverlay(nameHash);
		}

		/// <summary>
		/// Instantiate a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Color.</param>
		public override OverlayData InstantiateOverlay(string name, Color color)
		{
			return overlayLibrary.InstantiateOverlay(name, color);
		}
		/// <summary>
		/// Instantiate a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Color.</param>
		public override OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			return overlayLibrary.InstantiateOverlay(nameHash, color);
		}

		/// <summary>
		/// Add an overlay asset to the context.
		/// </summary>
		/// <param name="overlay">New overlay asset.</param>
		public override void AddOverlayAsset(OverlayDataAsset overlay)
		{
			overlayLibrary.AddOverlayAsset(overlay);
		}

#if UNITY_EDITOR
		public static GameObject CreateEditorContext()
		{
			GameObject EditorUMAContext = null;
			if (UnityEditor.BuildPipeline.isBuildingPlayer)
				return null;
			if (Application.isPlaying)
			{
				Debug.LogWarning("There was no UMAContext in this scene. Please add the UMA_DCS prefab to this scene before you try to generate an UMA.");
				return null;
			}
			Debug.Log("UMA Recipe Editor created an UMAEditorContext to enable editing. This will auto delete once you have finished editing your recipe or you add the UMA_DCS prefab to this scene.");
			//if there is already an EditorUMAContext use it
			if (UMAContextBase.FindInstance() != null)
			{
				if (UMAContextBase.FindInstance().gameObject.name == "UMAEditorContext")
				{
					EditorUMAContext = UMAContextBase.FindInstance().gameObject;
					//if the UMAContext itself is on this game object, it means this was created and not deleted by the previous version of 'CreateEditorContext'
					//(The new version creates the UMAContext on a child game object called 'UMAContext' so that UMAContext.FindInstance can find it properly)
					//so in this case delete all the components that would have been added from the found gameObject from the previous code
					if (EditorUMAContext.GetComponent<UMAContextBase>())
					{
						UMAUtils.DestroySceneObject(EditorUMAContext.GetComponent<UMAContextBase>());//should also make the instance null again
						if (EditorUMAContext.GetComponent<DynamicRaceLibrary>())
							UMAUtils.DestroySceneObject(EditorUMAContext.GetComponent<DynamicRaceLibrary>());
						if (EditorUMAContext.GetComponent<DynamicSlotLibrary>())
							UMAUtils.DestroySceneObject(EditorUMAContext.GetComponent<DynamicSlotLibrary>());
						if (EditorUMAContext.GetComponent<DynamicOverlayLibrary>())
							UMAUtils.DestroySceneObject(EditorUMAContext.GetComponent<DynamicOverlayLibrary>());
						if (EditorUMAContext.GetComponent<DynamicCharacterSystem>())
							UMAUtils.DestroySceneObject(EditorUMAContext.GetComponent<DynamicCharacterSystem>());
						if (EditorUMAContext.GetComponent<DynamicAssetLoader>())
							UMAUtils.DestroySceneObject(EditorUMAContext.GetComponent<DynamicAssetLoader>());
					}
				}
				else if (UMAContextBase.FindInstance().gameObject.transform.parent.gameObject.name == "UMAEditorContext")
				{
					EditorUMAContext = UMAContextBase.FindInstance().gameObject.transform.parent.gameObject;
				}
			}
			else if (GameObject.Find("UMAEditorContext"))
			{
				EditorUMAContext = GameObject.Find("UMAEditorContext");
			}
			else
			{
				EditorUMAContext = new GameObject();
				EditorUMAContext.name = "UMAEditorContext";
			}
			//Make this GameObject not show up in the scene or save
			EditorUMAContext.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
			//if this gameobject does not contain an UMAContext add it - we have to call it UMAContext because UMAContext.FindInstance searches for that game object
			UMAContextBase.Instance = EditorUMAContext.GetComponentInChildren<UMAContextBase>();
			var thisUMAContext = UMAContextBase.Instance as DynamicUMAContext;
			if (UMAContextBase.Instance == null)
			{
				var thisUMAContextGO = new GameObject();
				thisUMAContextGO.name = "UMAContext";
				thisUMAContextGO.transform.parent = EditorUMAContext.transform;
				thisUMAContext = thisUMAContextGO.AddComponent<DynamicUMAContext>();
				UMAContextBase.Instance = thisUMAContext;
			}
			//we need to add the libraries as components of the game object too
			//and then set THOSE components to the umaContext component
			thisUMAContext.raceLibrary = thisUMAContext.gameObject.AddComponent<DynamicRaceLibrary>();
			(thisUMAContext.raceLibrary as DynamicRaceLibrary).dynamicallyAddFromResources = true;
			(thisUMAContext.raceLibrary as DynamicRaceLibrary).dynamicallyAddFromAssetBundles = true;
			thisUMAContext.overlayLibrary = thisUMAContext.gameObject.AddComponent<DynamicOverlayLibrary>();
			(thisUMAContext.overlayLibrary as DynamicOverlayLibrary).dynamicallyAddFromResources = true;
			(thisUMAContext.overlayLibrary as DynamicOverlayLibrary).dynamicallyAddFromAssetBundles = true;
			thisUMAContext.slotLibrary = thisUMAContext.gameObject.AddComponent<DynamicSlotLibrary>();
			(thisUMAContext.slotLibrary as DynamicSlotLibrary).dynamicallyAddFromResources = true;
			(thisUMAContext.slotLibrary as DynamicSlotLibrary).dynamicallyAddFromAssetBundles = true;
			thisUMAContext.dynamicCharacterSystem = thisUMAContext.gameObject.AddComponent<DynamicCharacterSystem>();
			(thisUMAContext.dynamicCharacterSystem as DynamicCharacterSystem).dynamicallyAddFromResources = true;
			(thisUMAContext.dynamicCharacterSystem as DynamicCharacterSystem).dynamicallyAddFromAssetBundles = true;
			var thisDAL = thisUMAContext.gameObject.AddComponent<DynamicAssetLoader>();
			DynamicAssetLoader.Instance = thisDAL;
			return EditorUMAContext;
		}
#endif
	}
}
