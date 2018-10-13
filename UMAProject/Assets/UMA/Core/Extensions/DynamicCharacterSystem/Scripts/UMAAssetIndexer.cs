using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace UMA
{
    public class UMAAssetIndexer : MonoBehaviour, ISerializationCallbackReceiver
    {
        #region constants and static strings
        public static string SortOrder = "Name";
        public static string[] SortOrders = { "Name", "AssetName" };
        public static Dictionary<string, System.Type> TypeFromString = new Dictionary<string, System.Type>();
        public static Dictionary<string, AssetItem> GuidTypes = new Dictionary<string, AssetItem>();
        #endregion
        #region Fields
        public bool AutoUpdate;

        private Dictionary<System.Type, System.Type> TypeToLookup = new Dictionary<System.Type, System.Type>()
        {
        { (typeof(SlotDataAsset)),(typeof(SlotDataAsset)) },
        { (typeof(OverlayDataAsset)),(typeof(OverlayDataAsset)) },
        { (typeof(RaceData)),(typeof(RaceData)) },
        { (typeof(UMATextRecipe)),(typeof(UMATextRecipe)) },
        { (typeof(UMAWardrobeRecipe)),(typeof(UMAWardrobeRecipe)) },
        { (typeof(UMAWardrobeCollection)),(typeof(UMAWardrobeCollection)) },
        { (typeof(RuntimeAnimatorController)),(typeof(RuntimeAnimatorController)) },
        { (typeof(AnimatorOverrideController)),(typeof(RuntimeAnimatorController)) },
#if UNITY_EDITOR
        { (typeof(AnimatorController)),(typeof(RuntimeAnimatorController)) },
#endif
        {  typeof(TextAsset), typeof(TextAsset) },
        { (typeof(DynamicUMADnaAsset)), (typeof(DynamicUMADnaAsset)) }
        };


        // The names of the fully qualified types.
        public List<string> IndexedTypeNames = new List<string>();
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
        (typeof(AnimatorOverrideController)),
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
                if (theIndex == null || theIndexer == null)
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
                    StopTimer(st,"Asset index load");
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
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
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
            SerializedItems.Clear();
            foreach (AssetItem ai in ItemsByPath.Values)
            {
                // We null things out when we want to delete them. This prevents it from going back into 
                // the dictionary when rebuilt.
                if (ai == null)
                    continue;
                SerializedItems.Add(ai);
            }

            UpdateSerializedDictionaryItems();
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
            BuildStringTypes();
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
            BuildStringTypes();
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
                            Debug.Log("GetAsset 0 for type "+typeof(T).Name+" completed in " + st.ElapsedMilliseconds + "ms");
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
        /// <param name="type">System Type of the object to add.</param>
        /// <param name="name">Name for the object.</param>
        /// <param name="path">Path to the object.</param>
        /// <param name="o">The Object to add.</param>
        /// <param name="skipBundleCheck">Option to skip checking Asset Bundles.</param>
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
        /// <param name="SkipBundleCheck"></param>
        private void AddAssetItem(AssetItem ai, bool SkipBundleCheck = false)
        {
            try
            {
                System.Type theType = TypeToLookup[ai._Type];
                Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
                // Get out if we already have it.
                if (TypeDic.ContainsKey(ai._Name))
                {
                    // Debug.Log("Duplicate asset " + ai._Name + " was ignored.");
                    return;
                }

                if (ai._Name.ToLower().Contains((ai._Type.Name + "placeholder").ToLower()))
                {
                    //Debug.Log("Placeholder asset " + ai._Name + " was ignored. Placeholders are not indexed.");
                    return;
                }
#if UNITY_EDITOR
                if (!SkipBundleCheck)
                {
                    string Path = AssetDatabase.GetAssetPath(ai.Item.GetInstanceID());
                    if (InAssetBundle(Path))
                    {
                        // Debug.Log("Asset " + ai._Name + "is in Asset Bundle, and was not added to the index.");
                        return;
                    }
                }
#endif
                TypeDic.Add(ai._Name, ai);
                if (GuidTypes.ContainsKey(ai._Guid))
                {
                    return;
                }
                GuidTypes.Add(ai._Guid, ai);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("Exception in UMAAssetIndexer.AddAssetItem: " + ex);
            }
        }

#if UNITY_EDITOR

        public AssetItem FromGuid(string GUID)
        {
            if (GuidTypes.ContainsKey(GUID))
            {
                return GuidTypes[GUID];
            }
            return null;
        }
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
            System.Type theType = TypeToLookup[type];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            if (TypeDic.ContainsKey(Name))
            {
                AssetItem ai = TypeDic[Name];
                TypeDic.Remove(Name);
                GuidTypes.Remove(Name);
            }
        }
#endif
#endregion

#region Maintenance

        /// <summary>
        /// Updates the dictionaries from this list.
        /// Used when restoring items after modification, or after deserialization.
        /// </summary>
        private void UpdateSerializedDictionaryItems()
        {
            GuidTypes = new Dictionary<string, AssetItem>();
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
        /// Updates the list so all items can be processed at once, or for 
        /// serialization.
        /// </summary>
        private void UpdateSerializedList()
        {
            SerializedItems.Clear();
			foreach (System.Type type in TypeToLookup.Keys)
            {
				if (type == TypeToLookup[type])
				{
                	Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
                	foreach (AssetItem ai in TypeDic.Values)
                	{
                    	// Don't add asset bundle or resource items to index. They are loaded on demand.
                    	if (ai.IsAssetBundle == false && ai.IsResource == false)
                    	{
                       		SerializedItems.Add(ai);
                    	}
                	}
				}
            }

        }

        /// <summary>
        /// Builds a list of types and a string to look them up.
        /// </summary>
		private void BuildStringTypes()
		{
			TypeFromString.Clear();
			foreach (System.Type st in Types)
			{
				TypeFromString.Add(st.Name, st);
			}
		}

#if UNITY_EDITOR

		public void RepairAndCleanup()
		{
			// Rebuild the tables
			UpdateSerializedList();

			for(int i=0;i<SerializedItems.Count;i++)
			{
				AssetItem ai = SerializedItems[i];
				if (!ai.IsAssetBundle)
				{
					// If we already have a reference to the item, let's verify that everything is correct on it.
					Object obj = ai.Item;
					if (obj != null)
					{
						ai._Name = ai.EvilName;
						ai._Path = AssetDatabase.GetAssetPath(obj.GetInstanceID());
						ai._Guid = AssetDatabase.AssetPathToGUID(ai._Path);
					}
					else
					{
						// Clear out the item reference so we will attempt to fix it if it's broken.
						ai._SerializedItem = null;
						// This will attempt to load the item, using the path, guid or name (in that order).
						// This is in case we didn't have a reference to the item, and it was moved
						ai.CachSerializedItem();
						// If an item can't be found and we didn't ahve a reference to it, then we need to delete it.
						if (ai._SerializedItem == null)
						{
							// Can't be found or loaded
							// null it out, so it doesn't get added back.
							SerializedItems[i] = null;
						}
					}
				}
			}

			UpdateSerializedDictionaryItems();
			ForceSave();
		}

        public void AddEverything(bool includeText)
        {
            Clear(false);

            foreach(string s in TypeFromString.Keys)
            {
                System.Type CurrentType = TypeFromString[s];
                if (!includeText)
                {
                    if (CurrentType == typeof(TextAsset))
                    {
                        continue;
                    }
                }
                if (s != "AnimatorController")
                {
                    string[] guids = AssetDatabase.FindAssets("t:" + s);
                    foreach (string guid in guids)
                    {
                        string Path = AssetDatabase.GUIDToAssetPath(guid);
                        if (Path.ToLower().Contains(".shader"))
                        {
                            continue;
                        }
                        Object o = AssetDatabase.LoadAssetAtPath(Path, CurrentType);
                        if (o != null)
                        {
                            AssetItem ai = new AssetItem(CurrentType, o);
                            AddAssetItem(ai);
                        }
                        else
                        {
                            if (Path == null)
                            {
                                Debug.LogWarning("Cannot instantiate item " + guid);
                            }
                            else
                            {
                                Debug.LogWarning("Cannot instantiate item " + Path);
                            }
                        }
                    }
                }
            }
            ForceSave();
        }

        /// <summary>
        /// Clears the index
        /// </summary>
		public void Clear(bool forceSave = true)
        {
            // Rebuild the tables
            GuidTypes.Clear();
            ClearReferences();
            SerializedItems.Clear();
            UpdateSerializedDictionaryItems();
            if (forceSave)
               ForceSave();
        }

        /// <summary>
        /// Adds references to all items by accessing the item property.
        /// This forces Unity to load the item and return a reference to it.
        /// When building, Unity needs the references to the items because we 
        /// cannot demand load them without the AssetDatabase.
        /// </summary>
        public void AddReferences()
        {
            // Rebuild the tables
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
				if (!ai.IsAssetBundle)
					ai.CachSerializedItem();
            }
            UpdateSerializedDictionaryItems();
            ForceSave();
        }

        /// <summary>
        /// This releases items by dereferencing them so they can be 
        /// picked up by garbage collection.
        /// This also makes working with the index much faster.
        /// </summary>
        public void ClearReferences()
        {
            // Rebuild the tables
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
                ai.ReleaseItem();
            }
            UpdateSerializedDictionaryItems();
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
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
                ai._Name = ai.EvilName;
            }
            UpdateSerializedDictionaryItems();
        }

#endregion

#region Serialization
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            UpdateSerializedList();// this.SerializeAllObjects);
        }
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            var st = StartTimer();
            #region typestuff
            List<System.Type> newTypes = new List<System.Type>()
        {
        (typeof(SlotDataAsset)),
        (typeof(OverlayDataAsset)),
        (typeof(RaceData)),
        (typeof(UMATextRecipe)),
        (typeof(UMAWardrobeRecipe)),
        (typeof(UMAWardrobeCollection)),
        (typeof(RuntimeAnimatorController)),
        (typeof(AnimatorOverrideController)),
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
        { (typeof(AnimatorOverrideController)),(typeof(RuntimeAnimatorController)) },
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
            BuildStringTypes();
            #endregion
            UpdateSerializedDictionaryItems();
            StopTimer(st, "Before Serialize");
        }
        #endregion
    }
}
