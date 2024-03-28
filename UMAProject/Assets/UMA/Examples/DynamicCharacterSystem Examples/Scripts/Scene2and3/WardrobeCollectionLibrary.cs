using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
#endif

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
		public UMAContextBase context;

		void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
				if (makePersistent)
                {
                    DontDestroyOnLoad(this.gameObject);
                }
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
            {
                if (!initialized && !updating)
				{
					Init();
				}
            }
        }

		void Start()
		{
			if (Instance == null)
			{
				Instance = this;
				if (makePersistent)
                {
                    DontDestroyOnLoad(this.gameObject);
                }
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
				context = UMAContextBase.FindInstance();
			}
			updating = true;

			collectionIndex.Clear();

			if (Application.isPlaying)
			{
				//StartCoroutine(StartGatherCoroutine());
			}

			initialized = true;
			updating = false;
		}



		public void AddCollectionsFromDAL(UMAWardrobeCollection[] uwcs)
		{
			AddCollections(uwcs, "");
		}

		//TODO maybe we should start indexing on GUID?
		public void AddCollections(UMAWardrobeCollection[] uwcs, string filename = "")
		{
            for (int i = 0; i < uwcs.Length; i++)
			{
                UMAWardrobeCollection uwc = uwcs[i];
                if (uwc == null)
                {
                    continue;
                }

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
