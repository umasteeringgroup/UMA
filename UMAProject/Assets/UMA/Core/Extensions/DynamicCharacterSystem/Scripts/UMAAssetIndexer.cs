//#define TIMEINDEXER
//#define DBLOGGER

using UnityEngine;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System;
using AsyncOp = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<System.Collections.Generic.IList<UnityEngine.Object>>;
using SlotRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>;
using RaceRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>>;
using System.Linq;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
#endif

#if DBLOGGER
using System.Data;
using MySql.Data;
using MySql.Data.MySqlClient;
#endif

namespace UMA
{
    [PreferBinarySerialization]
    public class UMAAssetIndexer : ScriptableObject, ISerializationCallbackReceiver
	{
        public static float DefaultLife = 5.0f;
        private class CachedOp
        {
            public AsyncOp Operation;
            public float OperationTime;
            public float Life; // life in seconds
            public string Info;

            public CachedOp(AsyncOp op, string info, float OpLife = 0.0f)
            {
                if (OpLife == 0.0f) OpLife = DefaultLife;
                Operation = op;
                OperationTime = Time.time;
                Life = OpLife;
                Info = info;
            }

            public bool Expired
            {
                get
                {
                    if (Time.time - OperationTime > Life)
                    {
                        return true;
                    }
                    return false;
                }
            }
        }

#if UNITY_EDITOR
        Dictionary<int, List<UMATextRecipe>> SlotTracker = new Dictionary<int, List<UMATextRecipe>>();
        Dictionary<int, List<UMATextRecipe>> OverlayTracker = new Dictionary<int, List<UMATextRecipe>>();
        Dictionary<int, List<UMATextRecipe>> TextureTracker = new Dictionary<int, List<UMATextRecipe>>();
        Dictionary<int, AddressableAssetGroup> GroupTracker = new Dictionary<int, AddressableAssetGroup>();
        Dictionary<int, string> AddressLookup = new Dictionary<int, string>();
#endif
		public Dictionary<string, bool> Preloads = new Dictionary<string, bool>();

        // private List<AsyncOp> LoadedItems = new List<AsyncOp>();
        private List<CachedOp> LoadedItems = new List<CachedOp>();

        RaceRecipes raceRecipes = new RaceRecipes();

		public delegate void OnCompleted(bool success, string name, string message);
		public delegate void OnRaceCompleted(bool success, RaceData theRace, string message);
		public delegate void OnRecipeCompleted(bool success, UMAWardrobeRecipe theRecipe, string message);

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
        static UMAAssetIndexer theIndexer = null;

		
		#endregion

#if DBLOGGER
		public static MySqlConnection conn;
#endif

		public static System.Diagnostics.Stopwatch StartTimer()
        {
#if TIMEINDEXER
            if(Debug.isDebugBuild)
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
            if(Debug.isDebugBuild)
                Debug.Log(Status + " Completed " + st.ElapsedMilliseconds + "ms");
            return;
#endif
        }

#if DBLOGGER
		public static void DBLogger(string message)
		{
			Debug.Log(message);
			if (conn == null)
			{
				string connstr = "server=mcserver;user=admin;database=AddressableData;port=3306;password=f2672b555bd2fc54c985";
				conn = new MySqlConnection(connstr);
			}
			if (conn == null)
			{
				Debug.Log("Unable to log to DB");
				return;
			}
			string sql = "insert values here)";
			MySqlCommand cmd = new MySqlCommand(sql, conn);
			cmd.ExecuteNonQuery();
		}
#endif

		public static UMAAssetIndexer Instance
        {
            get
            {
				if (theIndexer == null)
                {
                    var st = StartTimer();
                    theIndexer = Resources.Load("AssetIndexer") as UMAAssetIndexer;
                    theIndexer.UpdateSerializedDictionaryItems();
                    theIndexer.RebuildRaceRecipes();
                    if (theIndexer == null)
                    {
/*
                        if (Debug.isDebugBuild)
                        {
                            Debug.LogError("Unable to load the AssetIndexer. This item is used to index non-asset bundle resources and is required.");
                        }
*/
                    }
                    StopTimer(st,"Asset index load");
                }
                return theIndexer;
            }
        }

        private HashSet<CachedOp> Cleanup = new HashSet<CachedOp>();
        public void CheckCache()
        {
            Cleanup.Clear();

            for(int i=0;i<LoadedItems.Count;i++)
            {
                CachedOp c = LoadedItems[i]; 
                if (c.Expired)
                {
                    Debug.Log("Cleaning up: " + c.Info);
                    Addressables.Release(c.Operation);
                    Cleanup.Add(c); 
                }
            }
            if (Cleanup.Count > 0)
            {
                Debug.Log("Freeing " + Cleanup.Count + " Items");
                LoadedItems.RemoveAll(x => Cleanup.Contains(x));
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
                    if (Debug.isDebugBuild)
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
			EditorUtility.SetDirty(this);
            //EditorUtility.SetDirty(this.gameObject);
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
		public bool HasAsset<T>(string Name)
		{
			System.Type ot = typeof(T);
			System.Type theType = TypeToLookup[ot];
			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
			return TypeDic.ContainsKey(Name);
		}

		public bool HasAsset<T>(int NameHash)
		{
			System.Type ot = typeof(T);
			System.Type theType = TypeToLookup[ot];
			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
			
			// This honestly hurt my heart typing this.
			// Todo: replace this loop with a dictionary.
			foreach(string s in TypeDic.Keys)
			{
				if (UMAUtils.StringToHash(s) == NameHash) return true;
			}
			return false;
		}

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

		public List<AssetItem> GetAssetItems(UMAPackedRecipeBase recipe)
		{


			UMAPackedRecipeBase.UMAPackRecipe PackRecipe = recipe.PackedLoad(UMAContextBase.Instance);

			var Slots = PackRecipe.slotsV3;

			if (Slots == null)
				return GetAssetItemsV2(PackRecipe);

			List<AssetItem> returnval = new List<AssetItem>();

			foreach (var slot in Slots)
			{
				if (slot == null)
					continue;
				AssetItem s = GetAssetItem<SlotDataAsset>(slot.id);
				if (s != null)
				{
					returnval.Add(s);
					var overlays = slot.overlays;
					foreach(var overlay in overlays)
					{
						AssetItem o = GetAssetItem<OverlayDataAsset>(overlay.id);
						if (o != null)
						{
							returnval.Add(o);
						}
					}
				}
			}
			return returnval;
		}

		private List<AssetItem> GetAssetItemsV2(UMAPackedRecipeBase.UMAPackRecipe PackRecipe)
		{
			List<AssetItem> returnval = new List<AssetItem>();

			var Slots = PackRecipe.slotsV2;

			if (Slots == null)
			{
				return returnval;
			}

			foreach (var slot in Slots)
			{
				if (slot == null)
					continue;
				if (slot.id == null)
					continue;
				AssetItem s = GetAssetItem<SlotDataAsset>(slot.id);
				if (s != null)
				{
					returnval.Add(s);
					var overlays = slot.overlays;
					foreach (var overlay in overlays)
					{
						AssetItem o = GetAssetItem<OverlayDataAsset>(overlay.id);
						if (o != null)
						{
							returnval.Add(o);
						}
					}
				}
			}
			return returnval;
		}

		/// <summary>
		/// Gets the asset hash and name for the given object
		/// </summary>
		private void GetEvilAssetNameAndHash(System.Type type, UnityEngine.Object o, ref string assetName, int assetHash)
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
                            if (Debug.isDebugBuild)
                                Debug.Log("GetAsset 0 for type "+typeof(T).Name+" completed in " + st.ElapsedMilliseconds + "ms");
                        }
                        return (kp.Value.Item as T);
                    }
                    else
                    {
                        st.Stop();
                        if (st.ElapsedMilliseconds > 2)
                        {
                            if (Debug.isDebugBuild)
                                Debug.Log("GetAsset 1 for type " + typeof(T).Name + " completed in " + st.ElapsedMilliseconds + "ms");
                        }
                        return null;
                    }
                }
            }
            st.Stop();
            if (st.ElapsedMilliseconds > 2)
            {
                if (Debug.isDebugBuild)
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

		public List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot)
		{
			// This will get the aggregate for all compatible races with no duplicates.
			List<string> recipes = GetRecipeNamesForRaceSlot(race, slot);

			// Build a list of recipes to return.
			List<UMARecipeBase> results = new List<UMARecipeBase>();

			foreach(string recipeName in recipes)
			{
				UMAWardrobeRecipe uwr = GetAsset<UMAWardrobeRecipe>(recipeName);
				if (uwr != null)
				{
					results.Add(uwr);
				}
			}
			return results;
		}


		private void internalGetRecipes(string race, ref Dictionary<string, HashSet<UMATextRecipe>> results)
		{
			if (raceRecipes.ContainsKey(race))
			{
				SlotRecipes sr = raceRecipes[race];

				foreach(KeyValuePair<string,List<UMATextRecipe>> kp in sr)
				{
					if (!results.ContainsKey(kp.Key))
					{
						results.Add(kp.Key, new HashSet<UMATextRecipe>());
					}
					results[kp.Key].UnionWith(kp.Value);
				}
			}
			return;
		}

		public Dictionary<string, List<UMATextRecipe>> GetRecipes(string race)
		{
			Dictionary<string, HashSet<UMATextRecipe>> aggregate = new Dictionary<string, HashSet<UMATextRecipe>>();
			
			internalGetRecipes(race, ref aggregate);

			RaceData rc = GetAsset<RaceData>(race);
			if (rc != null)
			{
				foreach (string CompatRace in rc.GetCrossCompatibleRaces())
				{
					internalGetRecipes(CompatRace, ref aggregate);
				}
			}

			SlotRecipes results = new SlotRecipes();
			foreach(KeyValuePair<string, HashSet<UMATextRecipe>> kp in aggregate)
			{
				results.Add(kp.Key, kp.Value.ToList());
			}

			return results;
		}

		private HashSet<string> internalGetRecipeNamesForRaceSlot(string race, string slot)
		{
			HashSet<string> results = new HashSet<string>();

			if (raceRecipes.ContainsKey(race))
			{
				SlotRecipes sr = raceRecipes[race];
				if (sr.ContainsKey(slot))
				{
					foreach(UMAWardrobeRecipe uwr in sr[slot])
					{
						results.Add(uwr.name);
					}
				}
			}
			return results;
		}

		public List<string> GetRecipeNamesForRaceSlot(string race, string slot)
		{
			// Start with recipes that are directly marked for this race.
			HashSet<string> results = internalGetRecipeNamesForRaceSlot(race, slot);

			RaceData rc = GetAsset<RaceData>(race);
			if (rc != null)
			{
				foreach(string CompatRace in rc.GetCrossCompatibleRaces())
				{
					results.UnionWith(internalGetRecipeNamesForRaceSlot(CompatRace, slot));
				}
			}

			return results.ToList();
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

		#region Addressables

#if UNITY_EDITOR
		GameObject EditorUMAContextBase;
#endif
		public UMAContextBase GetContext()
		{
			UMAContextBase instance = UMAContextBase.FindInstance();
			if (instance != null)
			{
				return instance;
			}
#if UNITY_EDITOR
			EditorUMAContextBase = UMAContextBase.CreateEditorContext();
			return UMAContextBase.Instance;
#else
			return null;
#endif
		}

		private void DestroyEditorUMAContextBase()
		{
#if UNITY_EDITOR
			if (EditorUMAContextBase != null)
			{
				foreach (Transform child in EditorUMAContextBase.transform)
				{
					DestroyImmediate(child.gameObject);
				}
				DestroyImmediate(EditorUMAContextBase);
			}
#endif
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> PreloadWardrobe(DynamicCharacterAvatar avatar, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			RaceData race = GetAsset<RaceData>(avatar.activeRace.name);

			// preload any assigned recipes.
			foreach (var wr in avatar.WardrobeRecipes.Values) 
			{
				//Debug.Log("Adding Wardrobe recipe: " + wr.name);
				keys.Add(wr.name);
			}

			// preload utility recipes
			foreach (var tr in avatar.umaAdditionalRecipes)
			{
				//Debug.Log("Adding additional recipe: " + tr.name);
				keys.Add(tr.name);
			}

			return LoadLabelList(keys, keepLoaded);
		}

       
        public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(DynamicCharacterAvatar avatar, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			RaceData race = GetAsset<RaceData>(avatar.activeRace.name);
			
			// preload the race
			if (race != null)
			{
				keys.Add(race.baseRaceRecipe.name);
			}


			// preload any assigned recipes.
			foreach (var wr in avatar.WardrobeRecipes.Values)
			{
                if (wr != null)
    				keys.Add(wr.name);
			}

			foreach(var tr in avatar.umaAdditionalRecipes)
			{
                if (tr != null)
				    keys.Add(tr.name);
			}

			return LoadLabelList(keys,keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(RaceData theRace, bool keepLoaded = false)
		{
			return LoadLabel(theRace.baseRaceRecipe.name, keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(List<RaceData> theRaces, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			foreach(RaceData rc in theRaces)
			{
				string key = rc.baseRaceRecipe.name;

				if (keys.Contains(key))
					continue;
				keys.Add(key);
			}
			return LoadLabelList(keys, keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> LoadLabel(string label, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			keys.Add(label);
			return LoadLabelList(keys, keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(UMATextRecipe theRecipe, bool keepLoaded = false)
		{
#if SUPER_LOGGING
			Debug.Log("Preloading: " + theRecipe.name);
#endif
			List<string> keys = new List<string>();
			keys.Add(theRecipe.name);
			return LoadLabelList(keys, keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(List<UMATextRecipe> theRecipes, bool keepLoaded = false)
		{
			UMAContextBase context = UMAContextBase.Instance;
			if (!context)
			{
				Debug.LogError("No context to preload!");
				AsyncOperationHandle<IList<UnityEngine.Object>> ao = new AsyncOperationHandle<IList<UnityEngine.Object>>();
				return ao;
			}

			List<string> Keys = new List<string>();

			foreach (UMATextRecipe utr in theRecipes)
			{
				Keys.Add(utr.name);
			}

			return LoadLabelList(Keys,keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> LoadLabelList(List<string> Keys, bool keepLoaded)
		{
//#if SUPER_LOGGING
            string labels = "";
//#endif
            foreach (string label in Keys)
			{
				if (!Preloads.ContainsKey(label))
				{
					Preloads[label] = keepLoaded;
				}
				else
				{
					if (keepLoaded) // only overwrite if keepLoaded = true. All "keepLoaded" take precedence.
						Preloads[label] = keepLoaded;
				}
//#if SUPER_LOGGING
                labels += "'" + label + "';";
//#endif
			}

//#if SUPER_LOGGING
            Debug.Log("Loading Labels: " + labels);
//#endif
			var op = Addressables.LoadAssetsAsync<UnityEngine.Object>(Keys.ToArray(), result =>
			{

                // Debug.Log("Result type is " + result.GetType().ToString());
				if (result.GetType() == typeof(SlotDataAsset))
				{
					AssetItem ai = GetAssetItem<SlotDataAsset>((result as SlotDataAsset).slotName);
					if (ai != null)
					{
						if (keepLoaded) ai.IsAlwaysLoaded = keepLoaded;
						ai._SerializedItem = result;
#if SUPER_LOGGING
						Debug.Log("Cached Slot " + ai.EvilName);
#endif
					}
                    else
                    {
                        Debug.Log("Unable to find slot: " + ai.EvilName);
                    }
				}
				if (result.GetType() == typeof(OverlayDataAsset))
				{
					AssetItem ai = GetAssetItem<OverlayDataAsset>((result as OverlayDataAsset).overlayName);
					if (ai != null)
					{
						if (keepLoaded)
							ai.IsAlwaysLoaded = keepLoaded; // only set if true, so if any call sets it to always loaded, it is not cleared.
						ai._SerializedItem = result;
#if SUPER_LOGGING
						Debug.Log("Cached Overlay " + (ai._SerializedItem as OverlayDataAsset).overlayName);
#endif
					}
				}
			}, Addressables.MergeMode.Union);

			if (!keepLoaded)
			{
                string info = "";
                foreach (string s in Keys)
                    info += Keys + "; ";
				LoadedItems.Add(new CachedOp(op,info));
			}
			return op;
		}

		public void Unload(AsyncOperationHandle AssetOperation)
		{
            Debug.Log("Unloading AsyncOperationHandle in Indexer.Unload()");
			Addressables.Release(AssetOperation);
			//LoadedItems.RemoveAll(x => x.Operation.Equals(AssetOperation));
        }

        public void Unload(AsyncOperationHandle<IList<UnityEngine.Object>> AssetOperation)
        {
            Debug.Log("Unloading AsyncOperationHandle<> in Indexer.Unload()");
            Addressables.Release(AssetOperation);
            LoadedItems.RemoveAll(x => x.Operation.Equals(AssetOperation));
        }

        public void UnloadAll(bool forceResourceUnload)
		{
            Debug.Log("Unloading ALL AsyncOperationHandle in Indexer.UnloadAll()");

            foreach (CachedOp op in LoadedItems)
			{
				Addressables.Release(op.Operation);
			}
			Dictionary<string, AssetItem> SlotDic = GetAssetDictionary(typeof(SlotDataAsset));
			Dictionary<string, AssetItem> OverlayDic = GetAssetDictionary(typeof(OverlayDataAsset));

			foreach (AssetItem ai in SlotDic.Values)
			{
				if (ai._SerializedItem != null && ai.IsAddressable && ai.IsAlwaysLoaded == false)
				{
					ai.ReleaseItem();
				}
			}

            // Preloads is tracking if a loaded item is "keep" or not.
            // After freeing everything, we really only need to know about the "keeps".
            // This is necessary, because it's possible to request to "keep" something in one call
            // and NOT keep it in another call. In this case, the previous "Keep" needs to be kept, so
            // we can honor the keep. 
            //
			// cheesiest cheap way to clear the Preloads
			Dictionary<string, bool> newPreloads = new Dictionary<string, bool>();
			foreach(KeyValuePair<string,bool> kvp in Preloads)
			{
				if (kvp.Value == true)
					newPreloads.Add(kvp.Key, kvp.Value);
			}
			Preloads = newPreloads;

			foreach (AssetItem ai in OverlayDic.Values)
			{
				if (ai._SerializedItem != null && ai.IsAddressable && ai.IsAlwaysLoaded == false)
				{
					ai.ReleaseItem();
				}
			}
			LoadedItems.Clear();
			if (forceResourceUnload)
				{
					Resources.UnloadUnusedAssets();
				}
		}

#if UNITY_EDITOR
		private AddressableAssetSettings _AddressableSettings;

        public AddressableAssetSettings AddressableSettings
        {
            get
            {
                if (_AddressableSettings == null)
                {
                    Debug.Log("Loading addressable Settings");
                    string[] Settings = AssetDatabase.FindAssets("AddressableAssetSettings");
                    string path = AssetDatabase.GUIDToAssetPath(Settings[0]);
                    _AddressableSettings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(path);
                    Debug.Log("Loaded.");
                }
                return _AddressableSettings;
            }
        }

		private AddressableAssetEntry GetAddressableAssetEntry(AssetItem ai)
		{
            if (AddressableSettings == null)
            {
                return null;
            }

			foreach (var group in AddressableSettings.groups)
			{
				if (group.HasSchema<PlayerDataGroupSchema>())
					continue;

				foreach (AddressableAssetEntry e in group.entries)
				{
                    if (e.AssetPath == ai._Path)
					{
						return e;
					}
				}
			}

			// Not found
			return null;
		}

        public UMAData.UMARecipe GetRecipe(UMATextRecipe recipe, UMAContextBase context)
        {
            UMAPackedRecipeBase.UMAPackRecipe PackRecipe = recipe.PackedLoad(context);
			try
			{
				UMAData.UMARecipe TempRecipe = UMATextRecipe.UnpackRecipe(PackRecipe, context);
				return TempRecipe;
			}
			catch (Exception ex)
			{
				Debug.LogError("Error unpacking recipe: " + recipe.name + ". " + ex.Message);
			}
			return new UMAData.UMARecipe();
        }

		public bool IsUMAGroup(string GroupName)
		{
			if (GroupName.StartsWith("UMA_")) return true;
			if (GroupName.StartsWith("UTR_")) return true;
			if (GroupName.StartsWith("UWR_")) return true;
			return false;
		}

        public void CleanupAddressables(bool OnlyEmpty = false, bool RemoveFlags = false)
        {
            // delete all UMA groups
            // RemoveGroup.
            if (AddressableSettings == null)
            {
                EditorUtility.DisplayDialog("Warning", "Addressable Asset Settings not found", "OK");
                return;
            }
            List<AddressableAssetGroup> GroupsToDelete = new List<AddressableAssetGroup>();

            foreach (var group in AddressableSettings.groups)
            {
                if (IsUMAGroup(group.name))
                {
                    if (OnlyEmpty)
                    {
                        if (group.entries.Count > 0) continue;
                    }
                    GroupsToDelete.Add(group);
                }
            }

            float pos = 0.0f;
            float inc = 1.0f / GroupsToDelete.Count;

            foreach (AddressableAssetGroup group in GroupsToDelete)
            {
                int iPos = Mathf.CeilToInt(pos);
                EditorUtility.DisplayProgressBar("Cleanup", "Removing " + group.Name, iPos);
                AddressableSettings.RemoveGroup(group);
                pos += inc;
            }

			if (RemoveFlags)
			{
				UpdateSerializedList();
				foreach (AssetItem ai in SerializedItems)
				{
					ai.IsAddressable = false;
					ai.IsAlwaysLoaded = false;
				}
				UpdateSerializedDictionaryItems();
				ForceSave();
			}
			EditorUtility.ClearProgressBar();
        }

        private void GenerateLookups(UMAContextBase context, List<UMATextRecipe> wardrobe)
        {
            float pos = 0.0f;
            float inc = 1.0f / wardrobe.Count;

            // Get the slots, overlays, textures.
            // calculate the number of references for each of them.
            // Map the usage 
            foreach (UMATextRecipe uwr in wardrobe)
            {
                int iPos = Mathf.CeilToInt(pos);
                EditorUtility.DisplayProgressBar("Generating", "Calculating Usage: " + uwr.name, iPos);

                // todo: cache this
                UMAData.UMARecipe ur = GetRecipe(uwr, context);

                if (ur.slotDataList == null) continue;

                foreach (SlotData sd in ur.slotDataList)
                {
                    if (sd == null) continue;

					AssetItem ai = GetAssetItem<SlotDataAsset>(sd.slotName);

					if (ai != null && ai.IsAlwaysLoaded == false)
					{
						// is this a utility slot? if so, we need to not delete it as an orphan. 
						if (sd.asset.meshData == null && sd.OverlayCount == 0)
						{
							ai.IsAlwaysLoaded = true;
						}
					}

					//if (!(ai != null && ai.IsAlwaysLoaded))
					//{
						//AddToTracker = false;
					//}
					//else
					//{
						int slotInstance = sd.asset.GetInstanceID();

						if (!SlotTracker.ContainsKey(slotInstance))
						{
							ai.IsAddressable = true;
							SlotTracker.Add(slotInstance, new List<UMATextRecipe>());
						}
						SlotTracker[slotInstance].Add(uwr);
						if (!AddressLookup.ContainsKey(slotInstance))
						{
							AddressLookup.Add(slotInstance, "Slt-" + sd.slotName);
						}
					//}

					List<OverlayData> odList = sd.GetOverlayList();

					foreach (OverlayData od in odList)
                    {
                        if (od == null) continue;


						/* = GetAssetItem<OverlayDataAsset>(od.overlayName);

						if (ai != null && ai.IsAlwaysLoaded)
						{
							continue;
						}*/

						int OverlayInstance = od.asset.GetInstanceID();

                        if (!OverlayTracker.ContainsKey(OverlayInstance))
                        {
                            OverlayTracker.Add(OverlayInstance, new List<UMATextRecipe>());
                        }
                        OverlayTracker[OverlayInstance].Add(uwr);
                        if (!AddressLookup.ContainsKey(OverlayInstance))
                        {
							ai.IsAddressable = true;
							AddressLookup.Add(OverlayInstance, "Ovl-" + od.overlayName);
                        }
                        foreach (Texture tx in od.textureArray)
                        {
                            if (tx == null) continue;
                            int TextureID = tx.GetInstanceID();
                            if (!TextureTracker.ContainsKey(TextureID))
                            {
                                TextureTracker.Add(TextureID, new List<UMATextRecipe>());
                            }
                            TextureTracker[TextureID].Add(uwr);
                            if (!AddressLookup.ContainsKey(TextureID))
                            {
                                AddressLookup.Add(TextureID, "Tex-" + tx.name);
                            }
                        }
                    }
                }
                pos += inc;
            }
        }

        private void AddItemToSharedGroup(string GUID,string Address, List<string> labels, AddressableAssetGroup sharedGroup)
        {
            AddressableAssetEntry ae = AddressableSettings.CreateOrMoveEntry(GUID, sharedGroup, false, true);
            ae.SetAddress(Address);

            foreach (string s in labels)
            {
                ae.SetLabel(s, true, true, true);
            }
        }

        private void AddAddressableAssets(Dictionary<int, List<UMATextRecipe>> tracker, AddressableAssetGroup sharedGroup)
        {
            float pos = 0.0f;
            float inc = 1.0f / tracker.Keys.Count;

            // Go through the assets, and add them to the groups.
            foreach (KeyValuePair<int, List<UMATextRecipe>> kp in tracker)
            {
                int iPos = Mathf.CeilToInt(pos);
                pos += inc;
                bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(kp.Key, out string GUID, out long localid);

                if (found)
                {
                    EditorUtility.DisplayProgressBar("Generating", "Adding Asset " + GUID, iPos);
                    AddressableAssetEntry ae = null;

                    switch (kp.Value.Count)
                    {
                        case 0:
                            Debug.LogWarning("Warning: No wardrobe found for item: " + kp.Key);
                            continue;
                        case 1:
                            ae = AddressableSettings.CreateOrMoveEntry(GUID, GroupTracker[kp.Value[0].GetInstanceID()], false, true);
                            break;
                        default:
                            ae = AddressableSettings.CreateOrMoveEntry(GUID, sharedGroup, false, true);
                            break;
                    }

                    // modify ae here as needed...
                    ae.SetAddress(AddressLookup[kp.Key]);
					AssetReference ar = new AssetReference(ae.guid);
                    // get the name here
                    foreach (UMATextRecipe uwr in kp.Value)
                    {
                        ae.SetLabel(uwr.name, true, true, true);
                    }
                }
            }
        }

		private void GenerateCollectionLabels()
		{
			//**********************************************************************************************
			//* Add Wardrobe Collections
			//**********************************************************************************************
			Type theType = TypeToLookup[typeof(UMAWardrobeCollection)];
			var wardrobecollections = GetAssetDictionary(theType).Values;

			foreach (AssetItem ai in wardrobecollections)
			{
				UMAWardrobeCollection uwc = ai.Item as UMAWardrobeCollection;
				string Label = uwc.name;

				HashSet<string> collectionRecipes = new HashSet<string>();

				foreach(var recipeName in uwc.arbitraryRecipes)
				{
					AddCollectionRecipe(uwc,recipeName);
				}
				foreach (var ws in uwc.wardrobeCollection.sets)
				{
					foreach (var wsettings in ws.wardrobeSet)
					{
						string recipeName = wsettings.recipe;
						AddCollectionRecipe(uwc, recipeName);
					}
				}
			}
		}

		private void AddCollectionRecipe(UMAWardrobeCollection uwc, string recipeName)
		{
			if (string.IsNullOrEmpty(recipeName))
				return;

			AssetItem recipeAsset = GetAssetItem<UMAWardrobeRecipe>(recipeName);
			if (recipeAsset != null)
			{
				UMAWardrobeRecipe uwr = recipeAsset.Item as UMAWardrobeRecipe;
				if (uwr == null)
				{
					Debug.Log("Null recipe in wardrobe collection...");
				}
				List<AssetItem> items = GetAssetItems(uwr);
				foreach (AssetItem recipeitem in items)
				{
					if (recipeitem.Item is SlotDataAsset)
					{
						AddSlotFromCollection(recipeitem.Item as SlotDataAsset, uwc);
					}
					if (recipeitem.Item is OverlayDataAsset)
					{
						AddOverlayFromCollection(recipeitem.Item as OverlayDataAsset, uwc);
					}
				}
			}
		}

		private void AddOverlayFromCollection(OverlayDataAsset overlayDataAsset,UMAWardrobeCollection uwc)
		{
			if (!OverlayTracker.ContainsKey(overlayDataAsset.GetInstanceID()))
			{
				OverlayTracker.Add(overlayDataAsset.GetInstanceID(), new List<UMATextRecipe>());
			}
			OverlayTracker[overlayDataAsset.GetInstanceID()].Add(uwc);
			foreach(Texture tex in overlayDataAsset.textureList)
			{
				if (!TextureTracker.ContainsKey(tex.GetInstanceID()))
				{
					TextureTracker.Add(tex.GetInstanceID(), new List<UMATextRecipe>());
				}
				TextureTracker[tex.GetInstanceID()].Add(uwc);
			}
		}

		private void AddSlotFromCollection(SlotDataAsset slotDataAsset, UMAWardrobeCollection uwc)
		{
			if (!SlotTracker.ContainsKey(slotDataAsset.GetInstanceID()))
			{
				SlotTracker.Add(slotDataAsset.GetInstanceID(), new List<UMATextRecipe>());
			}
			SlotTracker[slotDataAsset.GetInstanceID()].Add(uwc);
		}

        /// <summary>
        /// Get all the UMATextRecipes/UMWardrobeRecipes
        /// </summary>
        /// <returns></returns>
        private List<UMATextRecipe> GetAddressableRecipes()
        {

            List<UMATextRecipe> theRecipes = new List<UMATextRecipe>();
            Type theType;
            //**********************************************************************************************
            //* Add Wardrobe Recipes
            //**********************************************************************************************

            theType = TypeToLookup[typeof(UMAWardrobeRecipe)];
            var wardrobe = GetAssetDictionary(theType).Values;

            foreach (AssetItem ai in wardrobe)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                theRecipes.Add(uwr);
            }

            theType = TypeToLookup[typeof(UMATextRecipe)];
            var trecipes = GetAssetDictionary(theType).Values;

            foreach (AssetItem ai in trecipes)
            {
                UMATextRecipe utr = ai.Item as UMATextRecipe;
                theRecipes.Add(utr);
            }
            return theRecipes;
        }

        public void GenerateSingleGroup()
        {
            // Find what recipe everything is in.
            // label everything with that recipe.
            // dictionary<asset,recipes>
            // 
            try
            {
                foreach (Type t in GetTypes())
                {
                    ClearAddressableFlags(t);
                }
                List<UMATextRecipe> theRecipes = GetAddressableRecipes();

                // Find Labels.
                Dictionary<AssetItem, List<string>> theItems = new Dictionary<AssetItem, List<string>>();

                float pos = 0.0f;
                float inc = 1.0f / theRecipes.Count;
                foreach (UMATextRecipe uwr in theRecipes)
                {
                    EditorUtility.DisplayProgressBar("Generating", "processing recipe: " + uwr.name, pos);
                    List<AssetItem> items = GetAssetItems(uwr);
                    foreach (AssetItem ai in items)
                    {
                        if (theItems.ContainsKey(ai) == false)
                        {
                            theItems.Add(ai, new List<string>());
                        }
                        theItems[ai].Add(uwr.name);
                    }
                    pos += inc;
                }
                /// Add to group
                /// add labels
                /// Finalize

                // Create the shared group that has each item packed separately.
                AddressableAssetGroup sharedGroup = AddressableSettings.CreateGroup("UMA_SharedItems", false, false, true, AddressableSettings.DefaultGroup.Schemas);
                sharedGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

                pos = 0.0f;
                inc = 1.0f / theItems.Count;

                StringBuilder sb = new StringBuilder();
                foreach (AssetItem ai in theItems.Keys)
                {
                    ai.IsAddressable = true;
                    ai.AddressableAddress = ai._Type.Name + "-" + ai.EvilName;
                    ai.AddressableGroup = sharedGroup.name;
                    EditorUtility.DisplayProgressBar("Generating", "Processing Asset: " + ai.Item.name, pos);

                    sb.Clear();
                    foreach (string s in theItems[ai])
                    {
                        sb.Append(s);
                        sb.Append(';'); 
                    }
                    ai.AddressableLabels = sb.ToString(); 

                    bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ai.Item.GetInstanceID(), out string itemGUID, out long localID);

                    AddItemToSharedGroup(itemGUID, ai.AddressableAddress, theItems[ai], sharedGroup);
                    if (ai._Type == typeof(OverlayDataAsset))
                    {
                        OverlayDataAsset od = ai.Item as OverlayDataAsset;
                        foreach (Texture tex in od.textureList)
                        {
                            if (tex == null) continue;
                            if (tex as Texture2D == null)
                            {
                                Debug.Log("Texture is not Texture2D!!! " + tex.name);
                                continue;
                            }
                            string Address = "Texture2D-" + tex.name+"-"+tex.GetInstanceID();

                            found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texlocalID);
                            if (found)
                            {
                                AddItemToSharedGroup(texGUID, Address, theItems[ai], sharedGroup);
                            }
                        }
                    }
                    pos += inc;
                }

                UpdateAssetItems();

                ReleaseReferences(TypeToLookup[typeof(SlotDataAsset)]);
                ReleaseReferences(TypeToLookup[typeof(OverlayDataAsset)]);

                CleanupAddressables(true);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public void GenerateAddressables()
        {
            try
            {
				//**********************************************************************************************
				//*  Clear out the old data
				//**********************************************************************************************
				SlotTracker = new Dictionary<int, List<UMATextRecipe>>();
				OverlayTracker = new Dictionary<int, List<UMATextRecipe>>();
				TextureTracker = new Dictionary<int, List<UMATextRecipe>>();
				GroupTracker = new Dictionary<int, AddressableAssetGroup>();
				
				ClearAddressableFlags(typeof(SlotDataAsset));
				ClearAddressableFlags(typeof(OverlayDataAsset));

				// Will generate an editor context if needed.
				UMAContextBase context = GetContext();

                // Create the shared group that has each item packed separately.
                AddressableAssetGroup sharedGroup = AddressableSettings.CreateGroup("UMA_SharedItems", false, false, true, AddressableSettings.DefaultGroup.Schemas);
                sharedGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

				List<UMATextRecipe> theRecipes = new List<UMATextRecipe>();

				//**********************************************************************************************
				//*  Add Races
				//**********************************************************************************************

				System.Type theType = TypeToLookup[typeof(RaceData)];
				var races = GetAssetDictionary(theType).Values;

				foreach (AssetItem ai in races) 
				{
					RaceData race = ai.Item as RaceData;
					theRecipes.Add(race.baseRaceRecipe as UMATextRecipe);
					if (ai.IsAlwaysLoaded)
					{
						AssetItem recipe = GetAssetItem<UMATextRecipe>(race.baseRaceRecipe.name);
						recipe.IsAlwaysLoaded = true;

						List<AssetItem> recipeItems = GetAssetItems(race.baseRaceRecipe as UMAPackedRecipeBase);
						foreach(AssetItem recipeitem in recipeItems)
						{
							recipeitem.IsAlwaysLoaded = true;
						}
					}
				}



                theRecipes = GetAddressableRecipes();

				GenerateCollectionLabels();

				GenerateLookups(context, theRecipes);

                float pos = 0.0f;
                float inc = 1.0f / theRecipes.Count;

				const string tprefix = "UTR_";
				const string wprefix = "UWR_";

				// Create the Addressable groups
				foreach (UMATextRecipe uwr in theRecipes)
                {
					
                    int iPos = Mathf.CeilToInt(pos);
                    EditorUtility.DisplayProgressBar("Generating", "Creating Group: " + uwr.name, iPos);
                    Debug.Log("Generating group: " + uwr.name);
					string groupName; 
					if (uwr is UMAWardrobeRecipe)
					{
						groupName = wprefix + uwr.name;
					}
					else
					{
						groupName = tprefix + uwr.name;
					}
                    AddressableAssetGroup recipeGroup = AddressableSettings.CreateGroup(groupName, false, false, true, AddressableSettings.DefaultGroup.Schemas);
                    recipeGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;

					if (GroupTracker.ContainsKey(uwr.GetInstanceID()))
					{
						Debug.Log("Group already exists????? " + uwr.name);
						continue;
					}
                    GroupTracker.Add(uwr.GetInstanceID(), recipeGroup);
                    pos += inc;
                }

                AddAddressableAssets(SlotTracker, sharedGroup);
                AddAddressableAssets(OverlayTracker, sharedGroup);
                AddAddressableAssets(TextureTracker, sharedGroup);

                UpdateAssetItems();

				ReleaseReferences(TypeToLookup[typeof(SlotDataAsset)]);
				ReleaseReferences(TypeToLookup[typeof(OverlayDataAsset)]);

				CleanupAddressables(true);

            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DestroyEditorUMAContextBase();
				ForceSave();
			}
        }

        private void UpdateAssetItems()
        {
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
                AddressableAssetEntry ae = GetAddressableAssetEntry(ai);
                if (ae != null)
                {
                    ai.AddressableAddress = ae.address;
                    ai.IsAddressable = true;
                    ai.AddressableGroup = ae.parentGroup.Name;
                    ai._SerializedItem = null;

                    ai.AddressableLabels = "";
                    foreach (string s in ae.labels)
                    {
                        ai.AddressableLabels += s + ";";
                    }
                }
                else
                {
                    ai.AddressableAddress = "";
                    ai.AddressableGroup = "";
                    ai.IsAddressable = false;
                    ai.AddressableLabels = "";
                }
            }
        }

		public void CleanupOrphans(Type type)
		{
			var items = GetAssetDictionary(type);

			List<string> toRemove = new List<string>();
			foreach (KeyValuePair<string, AssetItem> pair in items)
			{
				if (pair.Value.IsAddressable ==false && pair.Value.IsAlwaysLoaded == false)
				{
					toRemove.Add(pair.Key);
				}
			}

			foreach (var key in toRemove)
			{
				items.Remove(key);
			}
			ForceSave();
		}

		private void ReleaseReferences(Type type)
		{
			var items = GetAssetDictionary(type).Values;
			foreach(AssetItem ai in items)
			{
				//if (ai.IsAlwaysLoaded)
				//{
					//ai.CacheSerializedItem();
				//}
				//else
				//{
					ai._SerializedItem = null;
				//}
			}
		}

		private void ClearAddressableFlags(Type type)
		{
			var items = GetAssetDictionary(type).Values;
			foreach (AssetItem ai in items)
			{
				ai.IsAddressable = false;
                ai.AddressableAddress = "";
                ai.AddressableLabels = "";
                ai.ReleaseItem();
			}
		}

#endif
#endregion

#region Add Remove Assets

#if UNITY_EDITOR

        public void AddIfIndexed(UnityEngine.Object o)
        {
            System.Type type = o.GetType();
            if (IsIndexedType(type))
            {
                EvilAddAsset(type, o);
            }
        }

        public void RemoveIfIndexed(UnityEngine.Object o)
        {
            // Warning: This does stuff. Don't remove this, we need the evilname lookup.
            AssetItem ai = new AssetItem(o.GetType(), o);
            RemoveAsset(ai._Type, ai._Name);
        }

        public void RecursiveScanFoldersForAssets(string path)
        {
            var assetFiles = System.IO.Directory.GetFiles(path);

            foreach (var assetFile in assetFiles)
            {
                string Extension = System.IO.Path.GetExtension(assetFile).ToLower();
                if (Extension == ".asset" || Extension == ".controller" || Extension == ".txt")
                {
                    UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(assetFile);

                    if (o)
                    {
                        AddIfIndexed(o);
                    }
                }
            }
            foreach (var subFolder in System.IO.Directory.GetDirectories(path))
            {
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
            }
        }
        
        public void RecursiveScanFoldersForRemovingAssets(string path)
        {
            var assetFiles = System.IO.Directory.GetFiles(path);

            foreach (var assetFile in assetFiles)
            {
                string Extension = System.IO.Path.GetExtension(assetFile).ToLower();
                if (Extension == ".asset" || Extension == ".controller" || Extension == ".txt")
                {
                    UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(assetFile);

                    if (o)
                    {
                        RemoveIfIndexed(o);
                    }
                }
            }
            foreach (var subFolder in System.IO.Directory.GetDirectories(path))
            {
                RecursiveScanFoldersForRemovingAssets(subFolder.Replace('\\', '/'));
            }
        }
#endif
        /// <summary>
        /// Adds an asset to the index. Does NOT save the asset! you must do that separately.
        /// </summary>
        /// <param name="type">System Type of the object to add.</param>
        /// <param name="name">Name for the object.</param>
        /// <param name="path">Path to the object.</param>
        /// <param name="o">The Object to add.</param>
        /// <param name="skipBundleCheck">Option to skip checking Asset Bundles.</param>
        public void AddAsset(System.Type type, string name, string path, UnityEngine.Object o, bool skipBundleCheck = false)
        {
            if (o == null)
            {
                if (Debug.isDebugBuild)
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
		/// <returns>Whether the asset was added or not.</returns>
		private bool AddAssetItem(AssetItem ai, bool SkipBundleCheck = false)
        {
            try
            {
                System.Type theType = TypeToLookup[ai._Type];
                Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
                // Get out if we already have it.
                if (TypeDic.ContainsKey(ai._Name))
                {
                    // Debug.Log("Duplicate asset " + ai._Name + " was ignored.");
                    return false;
                }

                if (ai._Name.ToLower().Contains((ai._Type.Name + "placeholder").ToLower()))
                {
                    //Debug.Log("Placeholder asset " + ai._Name + " was ignored. Placeholders are not indexed.");
                    return false;
                }

				if (ai._Type == typeof(UMAWardrobeRecipe))
				{
					AddToRaceLookup(ai._SerializedItem as UMAWardrobeRecipe);
				}


#if UNITY_EDITOR
                // Debug.Log("Getting asset entry");
				AddressableAssetEntry ae = GetAddressableAssetEntry(ai);
				if (ae != null)
				{
					ai.AddressableAddress = ae.address;
					ai.IsAddressable = true;
					ai.AddressableGroup = ae.parentGroup.Name;
					ai._SerializedItem = null;
                    ai.AddressableLabels = "";
                    foreach (string s in ae.labels)
                    {
                        ai.AddressableLabels += s + ";";
                    }
                }
#endif
                TypeDic.Add(ai._Name, ai);
                if (GuidTypes.ContainsKey(ai._Guid))
                {
                    return false;
                }
                GuidTypes.Add(ai._Guid, ai);
            }
            catch (System.Exception ex)
            {
                    UnityEngine.Debug.LogWarning("Exception in UMAAssetIndexer.AddAssetItem: " + ex);
            }
            return true;
        }

		/// <summary>
		/// If we added a new AssetItem that is a Wardrobe Recipe, then it needs to be added to the tables.
		/// </summary>
		/// <param name="uwr"></param>
		private void AddToRaceLookup(UMAWardrobeRecipe uwr)
		{
			if (uwr == null)
				return;

			foreach (string raceName in uwr.compatibleRaces)
			{
				if (!raceRecipes.ContainsKey(raceName))
				{
					raceRecipes.Add(raceName, new SlotRecipes());
				}
				SlotRecipes sl = raceRecipes[raceName];
				if (!sl.ContainsKey(uwr.wardrobeSlot))
				{
					sl.Add(uwr.wardrobeSlot, new List<UMATextRecipe>());
				}
				List<UMATextRecipe> recipes = sl[uwr.wardrobeSlot];
				if (recipes.Contains(uwr)) // I'm hoping this function isn't called much outside of updates, editor.
					continue;
				recipes.Add(uwr);
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
        /// <returns>Whether the Asset was added or not.</returns>
        public bool EvilAddAsset(System.Type type, UnityEngine.Object o)
        {
            AssetItem ai = null;
            ai = new AssetItem(type, o);
            ai._Path = AssetDatabase.GetAssetPath(o.GetInstanceID());
            return AddAssetItem(ai);
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
				if (theType == typeof(UMAWardrobeRecipe))
				{
					// remove it from the race lookup.
					foreach (SlotRecipes sl in raceRecipes.Values)
					{
						foreach (List<UMATextRecipe> recipes in sl.Values)
						{
							recipes.Remove(ai.Item as UMATextRecipe);
						}
					}
				}
			}
		}
#endif
#endregion

#region Maintenance

        /// <summary>
        /// Updates the dictionaries from this list.
        /// Used when restoring items after modification, or after deserialization.
        /// </summary>
        public void UpdateSerializedDictionaryItems()
        {
            GuidTypes = new Dictionary<string, AssetItem>();
            foreach (System.Type type in Types)
            {
                CreateLookupDictionary(type);
            }
//             Debug.Log("Adding Items");
            foreach (AssetItem ai in SerializedItems)
            {
                // We null things out when we want to delete them. This prevents it from going back into 
                // the dictionary when rebuilt.
                if (ai == null)
                    continue;
                AddAssetItem(ai, true);
            }
        }

		private void RebuildRaceRecipes()
		{
			//Dictionary<string, RaceData> RaceLookup = new Dictionary<string, RaceData>();

			List<RaceData> races = GetAllAssets<RaceData>();

			/// Build Race Recipes and RaceLookup
			raceRecipes.Clear();

			/// Add all the directly assigned items. 
			var wardrobe = GetAllAssets<UMAWardrobeRecipe>();

			foreach(UMAWardrobeRecipe uwr in wardrobe)
			{
                if (!uwr) continue;
				foreach(string racename in uwr.compatibleRaces)
				{
					if (!raceRecipes.ContainsKey(racename))
					{
						raceRecipes.Add(racename, new SlotRecipes());
					}
					SlotRecipes sl = raceRecipes[racename];
					if (!sl.ContainsKey(uwr.wardrobeSlot))
					{
						sl.Add(uwr.wardrobeSlot, new List<UMATextRecipe>());
					}
					sl[uwr.wardrobeSlot].Add(uwr);
				}
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
                    for(int i = 0; i < guids.Length; i++)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                        string fileName = Path.GetFileName(assetPath);
                        EditorUtility.DisplayProgressBar("Adding Items to Global Library.", fileName, ((float)i / (float)guids.Length));

                        if (assetPath.ToLower().Contains(".shader"))
                        {
                            continue;
                        }
						UnityEngine.Object o = AssetDatabase.LoadAssetAtPath(assetPath, CurrentType);
                        if (o != null)
                        {
							if (o.GetType() == typeof(UMAWardrobeRecipe))
							{
								if (CurrentType == typeof(UMATextRecipe))
									continue;
								//if ((o as UMATextRecipe).recipeType == "Wardrobe")
								//	continue;
							}
                            AssetItem ai = new AssetItem(CurrentType, o);
                            AddAssetItem(ai);
                        }
                        else
                        {
                            if (assetPath == null)
                            {
                                if (Debug.isDebugBuild)
                                    Debug.LogWarning("Cannot instantiate item " + guids[i]);
                            }
                            else
                            {
                                if (Debug.isDebugBuild)
                                    Debug.LogWarning("Cannot instantiate item " + assetPath);
                            }
                        }
                    }
                    EditorUtility.ClearProgressBar();
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


		public bool IsRemoveableItem(AssetItem ai)
		{
			if (ai._SerializedItem != null)
			{
				if (ai._SerializedItem.GetType() == typeof(SlotDataAsset)) return true;
				if (ai._SerializedItem.GetType() == typeof(OverlayDataAsset)) return true;
			}
			return false;
		}
        /// <summary>
        /// Adds references to all items by accessing the item property.
        /// This forces Unity to load the item and return a reference to it.
        /// When building, Unity needs the references to the items because we 
        /// cannot demand load them without the AssetDatabase.
        /// </summary>
        public void AddReferences(bool force = false)
        {
            // Rebuild the tables
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
				if (force)
				{
					ai.CacheSerializedItem();
				}
				else if (!ai.IsAddressable)
				{
					ai.CacheSerializedItem();
				}
				else//if (IsRemoveableItem(ai))
				{
					ai._SerializedItem = null;
				}
            }
			if (!force)
			{
				UpdateSerializedDictionaryItems();
			}
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

        /// <summary>
        /// Repairs the index. Removes anything that it cannot find.
        /// </summary>
        public void RepairAndCleanup()
        {
            // Rebuild the tables
            UpdateSerializedList();

            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                if (!ai.IsAssetBundle)
                {
					// If we already have a reference to the item, let's verify that everything is correct on it.
					UnityEngine.Object obj = ai.Item;
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
                        ai.CacheSerializedItem();
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
                    if (Debug.isDebugBuild)
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
            //Debug.Log("Updating serialized items...");
            //UpdateSerializedDictionaryItems();
            //Debug.Log("Completed update of serialized Items");
            StopTimer(st, "Before Serialize");
        }
#endregion
    }
}
