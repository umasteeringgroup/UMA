using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;

namespace UMA
{
	/// <summary>
	/// Gloal container for various UMA objects in the scene. Marked as partial so the developer can add to this if necessary
	/// </summary>
	public abstract class UMAContextBase : MonoBehaviour
	{
		public static string IgnoreTag;

		private static UMAContextBase _instance;
		public static UMAContextBase Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = GameObject.FindObjectOfType<UMAContextBase>();
				}
				return _instance;
			}
			set
			{
				_instance = value;
			}
		}

#pragma warning disable 618
		public abstract void Start();

		/// <summary>
		/// Validates the library contents.
		/// </summary>
		public abstract void ValidateDictionaries();

		/// <summary>
		/// Gets a race by name, if it has been added to the library
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public abstract RaceData HasRace(string name);

		/// <summary>
		/// Gets a race by name hash, if it has been added to the library.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract RaceData HasRace(int nameHash);

		/// <summary>
		/// Ensure we have a race key
		/// </summary>
		/// <param name="name"></param>
		public abstract void EnsureRaceKey(string name);

		/// <summary>
		/// Gets a race by name, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="name">Name.</param>
		public abstract RaceData GetRace(string name);

		/// <summary>
		/// Gets a race by name hash, if the library is a DynamicRaceLibrary it will try to find it.
		/// </summary>
		/// <returns>The race.</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract RaceData GetRace(int nameHash);

		/// <summary>
		/// Get a race by name hash. possibly allowing updates.
		/// </summary>
		/// <param name="nameHash"></param>
		/// <param name="allowUpdate"></param>
		/// <returns></returns>
		public abstract RaceData GetRaceWithUpdate(int nameHash, bool allowUpdate);

		/// <summary>
		/// Array of all races in the context.
		/// </summary>
		/// <returns>The array of race data.</returns>
		public abstract RaceData[] GetAllRaces();

		/// <summary>
		/// return races with no download
		/// </summary>
		/// <returns></returns>
		public abstract RaceData[] GetAllRacesBase();

		/// <summary>
		/// Add a race to the context.
		/// </summary>
		/// <param name="race">New race.</param>
		public abstract void AddRace(RaceData race);

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

		// Get all DNA
		public abstract List<DynamicUMADnaAsset> GetAllDNA();

		// Get a DNA Asset By Name
		public abstract DynamicUMADnaAsset GetDNA(string Name);

		public abstract RuntimeAnimatorController GetAnimatorController(string Name);

		public abstract List<RuntimeAnimatorController> GetAllAnimatorControllers();

		public abstract void AddRecipe(UMATextRecipe recipe);

		public abstract bool HasRecipe(string Name);

		public abstract bool CheckRecipeAvailability(string recipeName);

		public abstract UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd);

		public abstract UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd);

		public abstract string GetCharacterRecipe(string filename);

		public abstract List<string> GetRecipeFiles();

		public abstract Dictionary<string, List<UMATextRecipe>> GetRecipes(string raceName);

		public abstract List<string> GetRecipeNamesForRaceSlot(string race, string slot);

		public abstract List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot);

#pragma warning restore 618
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

#if UNITY_EDITOR
		public static GameObject CreateEditorContext()
		{
			GameObject EditorUMAContextBase = null;
			if (UnityEditor.BuildPipeline.isBuildingPlayer)
				return null;
			if (Application.isPlaying)
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("There was no UMAContext in this scene. Please add the UMA context prefab  to this scene before you try to generate an UMA.");
				return null;
			}

			if (Debug.isDebugBuild)
				Debug.Log("UMA Recipe Editor created an UMAEditorContext to enable editing. This will auto delete once you have finished editing your recipe or you add a UMA prefab with a context to this scene.");

			//if there is already an EditorUMAContextBase use it
			if (UMAContextBase.FindInstance() != null)
			{
				if (UMAContextBase.FindInstance().gameObject.name == "UMAEditorContext")
				{
					EditorUMAContextBase = UMAContextBase.FindInstance().gameObject;
					//if the UMAContextBase itself is on this game object, it means this was created and not deleted by the previous version of 'CreateEditorContext'
					//(The new version creates the UMAContextBase on a child game object called 'UMAContextBase' so that UMAContextBase.FindInstance can find it properly)
					//so in this case delete all the components that would have been added from the found gameObject from the previous code
					if (EditorUMAContextBase.GetComponent<UMAContextBase>())
					{
						UMAUtils.DestroySceneObject(EditorUMAContextBase.GetComponent<UMAContextBase>());//should also make the instance null again
					}
				}
				else if (UMAContextBase.FindInstance().gameObject.transform.parent.gameObject.name == "UMAEditorContext")
				{
					EditorUMAContextBase = UMAContextBase.FindInstance().gameObject.transform.parent.gameObject;
				}
			}
			else if (GameObject.Find("UMAEditorContext"))
			{
				EditorUMAContextBase = GameObject.Find("UMAEditorContext");
			}
			else
			{
				EditorUMAContextBase = new GameObject();
				EditorUMAContextBase.name = "UMAEditorContext";
			}
			//Make this GameObject not show up in the scene or save
			EditorUMAContextBase.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
			//if this gameobject does not contain an UMAContextBase add it - we have to call it UMAContextBase because UMAContextBase.FindInstance searches for that game object
			var context = UMAContextBase.Instance = EditorUMAContextBase.GetComponentInChildren<UMAContextBase>();
			if (UMAContextBase.Instance == null)
			{
				var GO = new GameObject();
				GO.name = "UMAContext";
				GO.transform.parent = EditorUMAContextBase.transform;
				context = GO.AddComponent<UMAGlobalContext>();
				GO.AddComponent<UMADefaultMeshCombiner>();

				var gen = GO.AddComponent<UMAGenerator>();
				gen.fitAtlas = true;
				gen.SharperFitTextures = true;
				gen.AtlasOverflowFitMethod = UMAGeneratorBase.FitMethod.BestFitSquare;
				gen.convertRenderTexture = false;
				gen.editorAtlasResolution = 1024;
				gen.InitialScaleFactor = 2;
				gen.collectGarbage = false;
				gen.IterationCount = 1;
				gen.fastGeneration = true;
				gen.processAllPending = false;
				gen.NoCoroutines = true;
				UMAContextBase.Instance = context;
			}
			return EditorUMAContextBase;
		}
#endif
	}
}
