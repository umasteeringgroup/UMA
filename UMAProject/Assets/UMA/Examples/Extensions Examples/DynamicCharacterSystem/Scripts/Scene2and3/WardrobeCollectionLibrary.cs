using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UMA;
using UMA.AssetBundles;

namespace UMA.CharacterSystem.Examples
{
	/// <summary>
	/// Gets a list of all the available 'UMAWardrobeCollection' assets that are available in Resources and (optionally) in any available assetBundles
	/// Primarly used for creating a GUI for 'Wardrobe Collections' that are available for users to (down)load and apply to their characters.
	/// Once a collection asset has been (down)loaded it is added to the DynamicCharacterSystem dictionaries as a 'FullOutfit' for any races it is compatible with
	/// </summary>
	public class WardrobeCollectionLibrary : MonoBehaviour
	{
		public static WardrobeCollectionLibrary Instance = null;

		public Dictionary<string, UMAWardrobeCollection> collectionIndex = new Dictionary<string, UMAWardrobeCollection>();
		//just for the inspector really- TODO one or other of these is not really needed, work out which
		public List<UMAWardrobeCollection> collectionList = new List<UMAWardrobeCollection>();

		public bool initializeOnAwake = true;
		public bool makePersistent = true;

		[HideInInspector]
		[System.NonSerialized]
		public bool initialized = false;
		private bool updating = false;

		public bool dynamicallyAddFromResources;
		[Tooltip("Limit the Resources search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesCollectionsFolder = "";
		public bool dynamicallyAddFromAssetBundles;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForCollectionsToSearch = "";
		[Space]
		[Tooltip("If true will store the download status of any collections in playerPrefs. Downloaded collections are immediately added to DynamicCharacterSystem libraries and remain available to your characters across sessions.")]
		public bool storeDownloadedStatus = false;


		[HideInInspector]
		public UMAContext context;

		//This is a ditionary of asset bundles that were loaded into the library. This can be queried to store a list of active assetBundles that might be useful to preload etc
		public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();

		private bool allResourcesScanned = false;

		void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
				if (makePersistent)
					DontDestroyOnLoad(this.gameObject);

			}
			else if (Instance != this)
			{
				if (Instance.makePersistent)
				{
					Destroy(this.gameObject);
				}
				else
				{
					Instance = this;
				}
			}
			if (initializeOnAwake)
				if (!initialized && !updating)
				{
					Init();
				}
		}

		void Start()
		{
			if (Instance == null)
			{
				Instance = this;
				if (makePersistent)
					DontDestroyOnLoad(this.gameObject);

			}
			else if (Instance != this)
			{
				if (Instance.makePersistent)
				{
					Destroy(this.gameObject);
				}
				else
				{
					Instance = this;
				}
			}
			if (!initialized && !updating)
			{
				Init();
			}

		}

		void Init()
		{
			if (initialized || updating)
			{
				return;
			}
			if (context == null)
			{
				context = UMAContext.FindInstance();
			}
			updating = true;

			collectionIndex.Clear();

			if (Application.isPlaying)
			{
				StartCoroutine(StartGatherCoroutine());
			}

			initialized = true;
			updating = false;
		}

		IEnumerator StartGatherCoroutine()
		{
			if (DynamicAssetLoader.Instance == null)
			{
				Debug.LogWarning("WardrobeCollectionLibrary requires an instance of DynamicAssetLoader to gather collection assets");
				yield break;
			}
			while (!DynamicAssetLoader.Instance.isInitialized)
			{
				yield return null;
			}
			GatherCollections();
		}

		/// <summary>
		/// Gets ALL the Wardrobe collections that are listed in the AssetBundleIndex (if dynamicallyAddFromAssetbundles), regardless of whether the bundles has been downloaded yet or not
		/// </summary>
		/// <param name="filename"></param>
		/// <param name="bundleToGather"></param>
		private void GatherCollections(string filename = "", string bundleToGather = "")
		{
			if (allResourcesScanned && filename == "" && bundleToGather == "")
				return;

			if (bundleToGather == "" && filename == "")
				allResourcesScanned = true;

			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForCollectionsToSearch;

			DynamicAssetLoader.Instance.AddAssets<UMAWardrobeCollection>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, true, assetBundleToGather, resourcesCollectionsFolder, null, filename, AddCollectionsFromDAL, true);
		}

		public void AddCollectionsFromDAL(UMAWardrobeCollection[] uwcs)
		{
			AddCollections(uwcs, "");
		}

		//TODO maybe we should start indexing on GUID?
		public void AddCollections(UMAWardrobeCollection[] uwcs, string filename = "")
		{
			foreach (UMAWardrobeCollection uwc in uwcs)
			{
				if (uwc == null)
					continue;
				if (filename == "" || (filename != "" && filename.Trim() == uwc.name))
				{
					if (!collectionIndex.ContainsKey(uwc.name))
					{
						collectionIndex.Add(uwc.name, uwc);
					}
					else
					{
						collectionIndex[uwc.name] = uwc;
					}
				}
			}
			//sync up the list
			collectionList.Clear();
			foreach (KeyValuePair<string, UMAWardrobeCollection> kp in collectionIndex)
			{
				collectionList.Add(kp.Value);
			}
			//TODO if the collection is 'unlocked/downloaded' add its contents to DCS
			//for this though we would need to maintain a list in player prefs so I'm not going to do that in this demo
		}
	}
}
