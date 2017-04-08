using UnityEngine;
using System.Collections.Generic;
using UMA;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#if UNITY_5_6_OR_NEWER
using UnityEditor.Build;
#endif
#endif
namespace UMA
{
#if UNITY_EDITOR
	[InitializeOnLoad]
#endif
	public class UMAAssetIndexer : MonoBehaviour, ISerializationCallbackReceiver
#if UNITY_EDITOR
#if UNITY_5_6_OR_NEWER
     , IPreprocessBuild
#endif
#endif
	{
		#region constants and static strings
		public static string SortOrder = "Name";
		public static string[] SortOrders = { "Name", "AssetName" };
		public static Dictionary<string, System.Type> TypeFromString = new Dictionary<string, System.Type>();
		#endregion
		#region Fields
		public bool AutoUpdate;
		public bool SerializeAllObjects;

		private Dictionary<System.Type, System.Type> TypeToLookup = new Dictionary<System.Type, System.Type>()
		{
		{ (typeof(SlotDataAsset)),(typeof(SlotDataAsset)) },
		{ (typeof(OverlayDataAsset)),(typeof(OverlayDataAsset)) },
		{ (typeof(RaceData)),(typeof(RaceData)) },
		{ (typeof(UMATextRecipe)),(typeof(UMATextRecipe)) },
		{ (typeof(UMAWardrobeRecipe)),(typeof(UMAWardrobeRecipe)) },
		{ (typeof(UMAWardrobeCollection)),(typeof(UMAWardrobeCollection)) },
		{ (typeof(RuntimeAnimatorController)),(typeof(RuntimeAnimatorController)) },
#if UNITY_EDITOR
        { (typeof(AnimatorController)),(typeof(RuntimeAnimatorController)) },
#endif
        {  typeof(TextAsset), typeof(TextAsset) },
		{ (typeof(DynamicUMADnaAsset)), (typeof(DynamicUMADnaAsset)) }
		};


		// The names of the fully qualified types.
		public List<string> IndexedTypeNames = new List<string>();
		// These list is used so Unity will serialize the data
		private List<AssetItem> Items = new List<AssetItem>();
		// These list is used so Unity will serialize the data
		public List<AssetItem> SerializedItems = new List<AssetItem>();
		// This is really where we keep the data.
		private Dictionary<System.Type, Dictionary<string, AssetItem>> TypeLookup = new Dictionary<System.Type, Dictionary<string, AssetItem>>();
		// This list tracks the types for use in iterating through the dictionaries
		private System.Type[] Types =
		{
		(typeof(SlotDataAsset)),
		(typeof(OverlayDataAsset)),
		(typeof(RaceData)),
		(typeof(UMATextRecipe)),
		(typeof(UMAWardrobeRecipe)),
		(typeof(UMAWardrobeCollection)),
		(typeof(RuntimeAnimatorController)),
#if UNITY_EDITOR
        (typeof(AnimatorController)),
#endif
        (typeof(DynamicUMADnaAsset)),
		(typeof(TextAsset))
	};
		#endregion
		#region Static Fields
		static GameObject theIndex = null;
		static UMAAssetIndexer theIndexer = null;
		#endregion

		public static System.Diagnostics.Stopwatch StartTimer()
		{
#if TIMEINDEXER

            Debug.Log("Timer started at " + Time.realtimeSinceStartup + " Sec");
            System.Diagnostics.Stopwatch st = new System.Diagnostics.Stopwatch();
            st.Start();

            return st;
#else
			return null;
#endif
		}

		public static void StopTimer(System.Diagnostics.Stopwatch st, string Status)
		{
#if TIMEINDEXER
            st.Stop();
            Debug.Log(Status + " Completed " + st.ElapsedMilliseconds + "ms");
            return;
#endif
		}

		public static UMAAssetIndexer Instance
		{
			get
			{
				if (theIndex == null)
				{
#if UNITY_EDITOR
					var st = StartTimer();
					theIndex = Resources.Load("AssetIndexer") as GameObject;
					if (theIndex == null)
					{
						return null;
					}
					theIndexer = theIndex.GetComponent<UMAAssetIndexer>();
					if (theIndexer == null)
					{
						return null;
					}
					StopTimer(st, "Asset index load");
#else
                theIndex = GameObject.Instantiate(Resources.Load<GameObject>("AssetIndexer")) as GameObject;
                theIndex.hideFlags = HideFlags.HideAndDontSave;
                theIndexer = theIndex.GetComponent<UMAAssetIndexer>();
#endif
				}
				return theIndexer;
			}
		}

#if UNITY_EDITOR
		public void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			if (!AutoUpdate)
			{
				return;
			}
			bool changed = false;

			// Build a dictionary of the items by path.
			Dictionary<string, AssetItem> ItemsByPath = new Dictionary<string, AssetItem>();
			UpdateList();
			foreach (AssetItem ai in Items)
			{
				if (ItemsByPath.ContainsKey(ai._Path))
				{
					Debug.Log("Duplicate path for item: " + ai._Path);
					continue;
				}
				ItemsByPath.Add(ai._Path, ai);
			}

			// see if they moved it in the editor.
			for (int i = 0; i < movedAssets.Length; i++)
			{
				string NewPath = movedAssets[i];
				string OldPath = movedFromAssetPaths[i];

				// Check to see if this is an indexed asset.
				if (ItemsByPath.ContainsKey(OldPath))
				{
					changed = true;
					// If they moved it into an Asset Bundle folder, then we need to "unindex" it.
					if (InAssetBundleFolder(NewPath))
					{
						// Null it out, so we don't add it to the index...
						ItemsByPath[OldPath] = null;
						continue;
					}
					// 
					ItemsByPath[OldPath]._Path = NewPath;
				}
			}

			// Rebuild the tables
			Items.Clear();
			foreach (AssetItem ai in ItemsByPath.Values)
			{
				// We null things out when we want to delete them. This prevents it from going back into 
				// the dictionary when rebuilt.
				if (ai == null)
					continue;
				Items.Add(ai);
			}

			UpdateDictionaries();
			if (changed)
			{
				ForceSave();
			}
		}

		/// <summary>
		/// Force the Index to save and reload
		/// </summary>
		public void ForceSave()
		{
			var st = StartTimer();
			EditorUtility.SetDirty(this.gameObject);
			AssetDatabase.SaveAssets();
			StopTimer(st, "ForceSave");
		}
#endif


		#region Manage Types
		/// <summary>
		/// Returns a list of all types that we know about.
		/// </summary>
		/// <returns></returns>
		public System.Type[] GetTypes()
		{
			return Types;
		}

		public bool IsIndexedType(System.Type type)
		{

			foreach (System.Type check in TypeToLookup.Keys)
			{
				if (check == type)
					return true;
			}
			return false;
		}

		public bool IsAdditionalIndexedType(string QualifiedName)
		{
			foreach (string s in IndexedTypeNames)
			{
				if (s == QualifiedName)
					return true;
			}
			return false;
		}
		/// <summary>
		/// Add a type to the types tracked
		/// </summary>
		/// <param name="sType"></param>
		public void AddType(System.Type sType)
		{
			string QualifiedName = sType.AssemblyQualifiedName;
			if (IsAdditionalIndexedType(QualifiedName)) return;

			List<System.Type> newTypes = new List<System.Type>();
			newTypes.AddRange(Types);
			newTypes.Add(sType);
			Types = newTypes.ToArray();
			TypeToLookup.Add(sType, sType);
			IndexedTypeNames.Add(sType.AssemblyQualifiedName);
#if UNITY_EDITOR
			BuildStringTypes();
#endif
		}

		public void RemoveType(System.Type sType)
		{
			string QualifiedName = sType.AssemblyQualifiedName;
			if (!IsAdditionalIndexedType(QualifiedName)) return;

			TypeToLookup.Remove(sType);

			List<System.Type> newTypes = new List<System.Type>();
			newTypes.AddRange(Types);
			newTypes.Remove(sType);
			Types = newTypes.ToArray();
			TypeLookup.Remove(sType);
			IndexedTypeNames.Remove(sType.AssemblyQualifiedName);
#if UNITY_EDITOR
			BuildStringTypes();
#endif
		}
		#endregion

		#region Access the index
		/// <summary>
		/// Return the asset specified, if it exists.
		/// if it can't be found by name, then we do a scan of the assets to see if 
		/// we can find the name directly on the object, and return that. 
		/// We then rebuild the index to make sure it's up to date.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="Name"></param>
		/// <returns></returns>
		public AssetItem GetAssetItem<T>(string Name)
		{
			System.Type ot = typeof(T);
			System.Type theType = TypeToLookup[ot];
			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
			if (TypeDic.ContainsKey(Name))
			{
				return TypeDic[Name];
			}
			/*
            foreach (AssetItem ai in TypeDic.Values)
            {
                if (Name == ai.EvilName)
                {
                    RebuildIndex();
                    return ai;
                }
            }*/
			return null;
		}

		/// <summary>
		/// Gets the asset hash and name for the given object
		/// </summary>
		private void GetEvilAssetNameAndHash(System.Type type, Object o, ref string assetName, int assetHash)
		{
			if (o is SlotDataAsset)
			{
				SlotDataAsset sd = o as SlotDataAsset;
				assetName = sd.slotName;
				assetHash = sd.nameHash;
			}
			else if (o is OverlayDataAsset)
			{
				OverlayDataAsset od = o as OverlayDataAsset;
				assetName = od.overlayName;
				assetHash = od.nameHash;
			}
			else if (o is RaceData)
			{
				RaceData rd = o as RaceData;
				assetName = rd.raceName;
				assetHash = UMAUtils.StringToHash(assetName);
			}
			else
			{
				assetName = o.name;
				assetHash = UMAUtils.StringToHash(assetName);
			}
		}



		public List<T> GetAllAssets<T>(string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			var st = StartTimer();

			var ret = new List<T>();
			System.Type ot = typeof(T);
			System.Type theType = TypeToLookup[ot];

			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

			foreach (KeyValuePair<string, AssetItem> kp in TypeDic)
			{
				if (AssetFolderCheck(kp.Value, foldersToSearch))
					ret.Add((kp.Value.Item as T));
			}
			StopTimer(st, "GetAllAssets type=" + typeof(T).Name);
			return ret;
		}

		public T GetAsset<T>(int nameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			System.Diagnostics.Stopwatch st = new System.Diagnostics.Stopwatch();
			st.Start();
			System.Type ot = typeof(T);
			Dictionary<string, AssetItem> TypeDic = (Dictionary<string, AssetItem>)TypeLookup[ot];
			string assetName = "";
			int assetHash = -1;
			foreach (KeyValuePair<string, AssetItem> kp in TypeDic)
			{
				assetName = "";
				assetHash = -1;
				GetEvilAssetNameAndHash(typeof(T), kp.Value.Item, ref assetName, assetHash);
				if (assetHash == nameHash)
				{
					if (AssetFolderCheck(kp.Value, foldersToSearch))
					{
						st.Stop();
						if (st.ElapsedMilliseconds > 2)
						{
							Debug.Log("GetAsset 0 for type " + typeof(T).Name + " completed in " + st.ElapsedMilliseconds + "ms");
						}
						return (kp.Value.Item as T);
					}
					else
					{
						st.Stop();
						if (st.ElapsedMilliseconds > 2)
						{
							Debug.Log("GetAsset 1 for type " + typeof(T).Name + " completed in " + st.ElapsedMilliseconds + "ms");
						}
						return null;
					}
				}
			}
			st.Stop();
			if (st.ElapsedMilliseconds > 2)
			{
				Debug.Log("GetAsset 2 for type " + typeof(T).Name + " completed in " + st.ElapsedMilliseconds + "ms");
			}
			return null;
		}

		public T GetAsset<T>(string name, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			var thisAssetItem = GetAssetItem<T>(name);
			if (thisAssetItem != null)
			{
				if (AssetFolderCheck(thisAssetItem, foldersToSearch))
					return (thisAssetItem.Item as T);
				else
					return null;
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// Checks if the given asset path resides in one of the given folder paths. Returns true if foldersToSearch is null or empty and no check is required
		/// </summary>
		private bool AssetFolderCheck(AssetItem itemToCheck, string[] foldersToSearch = null)
		{
			if (foldersToSearch == null)
				return true;
			if (foldersToSearch.Length == 0)
				return true;
			for (int i = 0; i < foldersToSearch.Length; i++)
			{
				if (itemToCheck._Path.IndexOf(foldersToSearch[i]) > -1)
					return true;
			}
			return false;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Check to see if something is an an assetbundle. If so, don't add it
		/// </summary>
		/// <param name="path"></param>
		/// <param name="assetName"></param>
		/// <returns></returns>
		public bool InAssetBundle(string path)
		{
			// path = System.IO.Path.GetDirectoryName(path);
			string[] assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
			List<string> pathsInBundle;
			for (int i = 0; i < assetBundleNames.Length; i++)
			{
				pathsInBundle = new List<string>(AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleNames[i]));
				if (pathsInBundle.Contains(path))
					return true;
			}
			return false;
		}

		public bool InAssetBundleFolder(string path)
		{
			path = System.IO.Path.GetDirectoryName(path);
			string[] assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
			List<string> pathsInBundle;
			for (int i = 0; i < assetBundleNames.Length; i++)
			{
				pathsInBundle = new List<string>(AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleNames[i]));
				foreach (string s in pathsInBundle)
				{
					if (System.IO.Path.GetDirectoryName(s) == path)
						return true;
				}
			}
			return false;
		}
#endif
		#endregion

		#region Add Remove Assets
		/// <summary>
		/// Adds an asset to the index. Does NOT save the asset! you must do that separately.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="o"></param>
		public void AddAsset(System.Type type, string name, string path, Object o, bool skipBundleCheck = false)
		{
			if (o == null)
			{
				Debug.Log("Skipping null item");
				return;
			}
			if (type == null)
			{
				type = o.GetType();
			}

			AssetItem ai = new AssetItem(type, name, path, o);
			AddAssetItem(ai, skipBundleCheck);
		}

		/// <summary>
		/// Adds an asset to the index. If the name already exists, it is not added. (Should we do this, or replace it?)
		/// </summary>
		/// <param name="ai"></param>
		private void AddAssetItem(AssetItem ai, bool SkipBundleCheck = false)
		{
			System.Type theType = TypeToLookup[ai._Type];
			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
			// Get out if we already have it.
			if (TypeDic.ContainsKey(ai._Name))
			{
				Debug.Log("Duplicate asset " + ai._Name + " was ignored.");
				return;
			}
#if UNITY_EDITOR
			if (!SkipBundleCheck)
			{
				string Path = AssetDatabase.GetAssetPath(ai.Item.GetInstanceID());
				if (InAssetBundle(Path))
				{
					Debug.Log("Asset " + ai._Name + "is in Asset Bundle, and was not added to the index.");
					return;
				}
			}
#endif
			TypeDic.Add(ai._Name, ai);
		}

#if UNITY_EDITOR
		/// <summary>
		/// This is the evil version of AddAsset. This version cares not for the good of the project, nor
		/// does it care about readability, expandibility, and indeed, hates goodness with every beat of it's 
		/// tiny evil shrivelled heart. 
		/// I started going down the good path - I created an interface to get the name info, added it to all the
		/// classes. Then we ran into RuntimeAnimatorController. I would have had to wrap it. And Visual Studio kept
		/// complaining about the interface, even though Unity thought it was OK.
		/// 
		/// So in the end, good was defeated. And would never raise it's sword in the pursuit of chivalry again.
		/// 
		/// And EvilAddAsset doesn't save either. You have to do that manually. 
		/// </summary>
		/// <param name="type"></param>
		/// <param name="Name"></param>
		/// <param name="o"></param>
		public void EvilAddAsset(System.Type type, Object o)
		{
			AssetItem ai = null;
			ai = new AssetItem(type, o);
			ai._Path = AssetDatabase.GetAssetPath(o.GetInstanceID());
			AddAssetItem(ai);
		}

		/// <summary>
		/// Removes an asset from the index
		/// </summary>
		/// <param name="type"></param>
		/// <param name="Name"></param>
		public void RemoveAsset(System.Type type, string Name)
		{
			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
			TypeDic.Remove(Name);
		}
#endif
		#endregion

		#region Maintenance

		/// <summary>
		/// Updates the dictionaries after deserialization.
		/// </summary>
		private void UpdateDictionaries(bool SkipBundleCheck = false)
		{
			foreach (System.Type type in Types)
			{
				CreateLookupDictionary(type);
			}
			foreach (AssetItem ai in Items)
			{
				// We null things out when we want to delete them. This prevents it from going back into 
				// the dictionary when rebuilt.
				if (ai == null)
					continue;
				// Make sure Unity hasn't lost a reference to it somehow.
				if (ai.Item != null)
				{
					AddAsset(ai._Type, ai._Name, ai._Path, ai.Item, true);
				}
			}
		}

		/// <summary>
		/// Updates the dictionaries after deserialization.
		/// </summary>
		private void UpdateSerializedDictionaryItems()
		{
			foreach (System.Type type in Types)
			{
				CreateLookupDictionary(type);
			}
			foreach (AssetItem ai in SerializedItems)
			{
				// We null things out when we want to delete them. This prevents it from going back into 
				// the dictionary when rebuilt.
				if (ai == null)
					continue;
				AddAssetItem(ai, true);
			}
		}
		/// <summary>
		/// Creates a lookup dictionary for a list. Used when reloading after deserialization
		/// </summary>
		/// <param name="type"></param>
		private void CreateLookupDictionary(System.Type type)
		{
			Dictionary<string, AssetItem> dic = new Dictionary<string, AssetItem>();
			if (TypeLookup.ContainsKey(type))
			{
				TypeLookup[type] = dic;
			}
			else
			{
				TypeLookup.Add(type, dic);
			}
		}

		/// <summary>
		/// Updates the List so it can be serialized.
		/// </summary>
		private void UpdateList()
		{
			Items.Clear();
			foreach (System.Type type in Types)
			{
				Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
				foreach (AssetItem ai in TypeDic.Values)
				{
					// Don't add asset bundle or resource items to index. They are loaded on demand.
					if (ai.IsAssetBundle == false && ai.IsResource == false)
					{
						Items.Add(ai);
					}
				}
			}
		}

		private void UpdateSerializedList(bool ForceItemSave)
		{
			SerializedItems.Clear();
			foreach (System.Type type in Types)
			{
				Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
				foreach (AssetItem ai in TypeDic.Values)
				{
					// Don't add asset bundle or resource items to index. They are loaded on demand.
					if (ai.IsAssetBundle == false && ai.IsResource == false)
					{
						AssetItem ais = ai.CreateSerializedItem(ForceItemSave);
						SerializedItems.Add(ais);
					}
				}
			}
		}



#if UNITY_EDITOR
		private void BuildStringTypes()
		{
			TypeFromString.Clear();
			foreach (System.Type st in Types)
			{
				TypeFromString.Add(st.Name, st);
			}
		}

		public void Clear()
		{
			// Rebuild the tables
			Items.Clear();
			UpdateDictionaries();
			ForceSave();
		}

		public void ClearReferences()
		{
			// Rebuild the tables
			UpdateList();
			foreach (AssetItem ai in Items)
			{
				ai._SerializedItem = null;
			}
			UpdateDictionaries();
			ForceSave();
		}

#endif
		/// <summary>
		/// returns the entire lookup dictionary for a specific type.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public Dictionary<string, AssetItem> GetAssetDictionary(System.Type type)
		{
			System.Type LookupType = TypeToLookup[type];
			if (TypeLookup.ContainsKey(LookupType) == false)
			{
				TypeLookup[LookupType] = new Dictionary<string, AssetItem>();
			}
			return TypeLookup[LookupType];
		}

		/// <summary>
		/// Rebuilds the name indexes by dumping everything back to the list, updating the name, and then rebuilding 
		/// the dictionaries.
		/// </summary>
		public void RebuildIndex()
		{
			UpdateList();
			foreach (AssetItem ai in Items)
			{
				ai._Name = ai.EvilName;
			}
			UpdateDictionaries();
		}

		#endregion

		#region Serialization
		void ISerializationCallbackReceiver.OnBeforeSerialize()
		{
			UpdateSerializedList(this.SerializeAllObjects);
		}
		void ISerializationCallbackReceiver.OnAfterDeserialize()
		{
			var st = StartTimer();

			List<System.Type> newTypes = new List<System.Type>()
		{
		(typeof(SlotDataAsset)),
		(typeof(OverlayDataAsset)),
		(typeof(RaceData)),
		(typeof(UMATextRecipe)),
		(typeof(UMAWardrobeRecipe)),
		(typeof(UMAWardrobeCollection)),
		(typeof(RuntimeAnimatorController)),
#if UNITY_EDITOR
        (typeof(AnimatorController)),
#endif
            (typeof(DynamicUMADnaAsset)),
		(typeof(TextAsset))
		};

			TypeToLookup = new Dictionary<System.Type, System.Type>()
		{
		{ (typeof(SlotDataAsset)),(typeof(SlotDataAsset)) },
		{ (typeof(OverlayDataAsset)),(typeof(OverlayDataAsset)) },
		{ (typeof(RaceData)),(typeof(RaceData)) },
		{ (typeof(UMATextRecipe)),(typeof(UMATextRecipe)) },
		{ (typeof(UMAWardrobeRecipe)),(typeof(UMAWardrobeRecipe)) },
		{ (typeof(UMAWardrobeCollection)),(typeof(UMAWardrobeCollection)) },
		{ (typeof(RuntimeAnimatorController)),(typeof(RuntimeAnimatorController)) },
#if UNITY_EDITOR
        { (typeof(AnimatorController)),(typeof(RuntimeAnimatorController)) },
#endif
        {  typeof(TextAsset), typeof(TextAsset) },
		{ (typeof(DynamicUMADnaAsset)), (typeof(DynamicUMADnaAsset)) }
		};

			List<string> invalidTypeNames = new List<string>();
			// Add the additional Types.
			foreach (string s in IndexedTypeNames)
			{
				if (s == "")
					continue;
				System.Type sType = System.Type.GetType(s);
				if (sType == null)
				{
					invalidTypeNames.Add(s);
					Debug.LogWarning("Could not find type for " + s);
					continue;
				}
				newTypes.Add(sType);
				if (!TypeToLookup.ContainsKey(sType))
				{
					TypeToLookup.Add(sType, sType);
				}
			}

			Types = newTypes.ToArray();

			if (invalidTypeNames.Count > 0)
			{
				foreach (string ivs in invalidTypeNames)
				{
					IndexedTypeNames.Remove(ivs);
				}
			}
#if UNITY_EDITOR
			BuildStringTypes();
#endif
			UpdateSerializedDictionaryItems();
			StopTimer(st, "Before Serialize");
		}
#if UNITY_EDITOR
#if UNITY_5_6_OR_NEWER
    int IOrderedCallback.callbackOrder
    {
        get
        {
            return 0;
        }
    }

    void IPreprocessBuild.OnPreprocessBuild(BuildTarget target, string path)
    {
        bool wasSet = SerializeAllObjects;
        SerializeAllObjects = true;
        ForceSave();
        SerializeAllObjects = wasSet;
    }
#endif
#endif
		#endregion
	}
}
