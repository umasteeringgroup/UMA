using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;

#if UMA_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using AsyncOp = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<System.Collections.Generic.IList<UnityEngine.Object>>;
#endif
using PackSlot = UMA.UMAPackedRecipeBase.PackedSlotDataV3;
using SlotRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>;
using RaceRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>>;
using System.Linq;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UMA;
#endif

namespace UMA
{
    [PreferBinarySerialization]
    public class UMAAssetIndexer : ScriptableObject, ISerializationCallbackReceiver
	{
        public static float DefaultLife = 5.0f;

#if UMA_ADDRESSABLES
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
#endif
#if UMA_ADDRESSABLES
        public Dictionary<string, bool> Preloads = new Dictionary<string, bool>();
        private List<CachedOp> LoadedItems = new List<CachedOp>();
#endif

        RaceRecipes raceRecipes = new RaceRecipes();

#region constants and static strings
		public static string SortOrder = "Name";
        public static string[] SortOrders = { "Name", "AssetName" };
        public static Dictionary<string, System.Type> TypeFromString = new Dictionary<string, System.Type>();
        public static Dictionary<string, AssetItem> GuidTypes = new Dictionary<string, AssetItem>();
#endregion
#region Fields
        protected Dictionary<System.Type, System.Type> TypeToLookup = new Dictionary<System.Type, System.Type>()
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
        { (typeof(DynamicUMADnaAsset)), (typeof(DynamicUMADnaAsset)) },
        {(typeof(UMAMaterial)), (typeof(UMAMaterial)) }
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
        (typeof(TextAsset)),
        (typeof(UMAMaterial))
    };


#endregion
#region Static Fields
        static UMAAssetIndexer theIndexer = null;


        #endregion

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

        public static UMAAssetIndexer Instance
        {
            get
            {
                if (theIndexer == null)
                {
                    //var st = StartTimer();
                    theIndexer = Resources.Load("AssetIndexer") as UMAAssetIndexer;
                    if (theIndexer == null)
                        return null;
                    theIndexer.UpdateSerializedDictionaryItems();
                    theIndexer.RebuildRaceRecipes();
#if UNITY_EDITOR
                    EditorSceneManager.sceneSaving += EditorSceneManager_sceneSaving;
                    EditorSceneManager.sceneSaved += EditorSceneManager_sceneSaved;
#endif
                    //StopTimer(st,"Asset index load");
                }
                return theIndexer;
            }
        }

#if UNITY_EDITOR
        public const string ConfigToggle_LeanMeanSceneFiles = "UMA_CLEANUP_GENERATED_DATA_ON_SAVE";

        public static bool LeanMeanSceneFiles()
        {
            return EditorPrefs.GetBool(ConfigToggle_LeanMeanSceneFiles, true);
        }

        private static void EditorSceneManager_sceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            if (!LeanMeanSceneFiles())
                return;

            GameObject[] sceneObjs = scene.GetRootGameObjects();
            foreach (GameObject go in sceneObjs)
            {
                DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
                if (dcas.Length > 0)
                {
                    foreach (DynamicCharacterAvatar dca in dcas)
                    {
                        dca.GenerateSingleUMA();
                    }
                }
            }
        }

    private static void EditorSceneManager_sceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (!LeanMeanSceneFiles())
                return;

            // Cleanup any editor generated UMAS
            GameObject[] sceneObjs = scene.GetRootGameObjects();
            foreach(GameObject go in sceneObjs)
            {
                DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
                if (dcas.Length > 0)
                {
                    foreach(DynamicCharacterAvatar dca in dcas)
                    {
                        // Free all the generated data so we don't junk up the scene file.
                        // it will be regenerated later.
                        dca.CleanupGeneratedData();
                    }
                }
            }
        }
 
        public struct IndexBackup
        {
            public DateTime BackupTime;
            public AssetItem[] Items;
        }

        public string Backup()
        {
            try
            {
                RepairAndCleanup();

                IndexBackup backup = new IndexBackup();
                backup.BackupTime = DateTime.Now;
                backup.Items = UpdateSerializedList().ToArray();

                return JsonUtility.ToJson(backup);
            }
            catch(Exception ex)
            {
                Debug.LogException(ex);
                return "";
            }
        }

        public bool Restore(string s, bool quiet=false)
        {
            try
            {
                IndexBackup restore = JsonUtility.FromJson<IndexBackup>(s);
                SerializedItems.Clear();
                SerializedItems.AddRange(restore.Items);
                if (!quiet) EditorUtility.DisplayProgressBar("Restore", "Restoring index", 0.33f);
                UpdateSerializedDictionaryItems();
                if (!quiet) EditorUtility.DisplayProgressBar("Restore", "Restoring index", 0.66f);
                RepairAndCleanup();
                if (!quiet) EditorUtility.DisplayProgressBar("Restore", "Restoring index", 1.0f);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }
#endif

        public Type GetRuntimeType(Type type)
        {
            return TypeToLookup[type];
        }

#if UMA_ADDRESSABLES
        private HashSet<CachedOp> Cleanup = new HashSet<CachedOp>();
        public void CheckCache()
        {
            Cleanup.Clear();

            for(int i=0;i<LoadedItems.Count;i++)
            {
                CachedOp c = LoadedItems[i]; 
                if (c.Expired)
                {
                    Addressables.Release(c.Operation);
                    Cleanup.Add(c); 
                }
            }
            if (Cleanup.Count > 0)
            {
                LoadedItems.RemoveAll(x => Cleanup.Contains(x));
            }
        }
#endif
#if UNITY_EDITOR
        public void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
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

        public System.Type GetIndexedType(System.Type type)
        {
            if (TypeToLookup.ContainsKey(type))
            {
                return TypeToLookup[type];
            }
            return type;
        }

        public Dictionary<System.Type, System.Type>.ValueCollection GetIndexedTypeValues()
        {
            return TypeToLookup.Values;
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
        public AssetItem GetRecipeItem(UMAPackedRecipeBase recipe)
        {
            if (recipe is UMAWardrobeCollection)
                return GetAssetItem<UMAWardrobeCollection>(recipe.name);
            if (recipe is UMAWardrobeRecipe)
                return GetAssetItem<UMAWardrobeRecipe>(recipe.name);
            if (recipe is UMATextRecipe)
                return GetAssetItem<UMATextRecipe>(recipe.name);
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

        /// <summary>
        /// Return the asset specified, if it exists.
        /// if it can't be found by name, then we do a scan of the assets to see if 
        /// we can find the name directly on the object, and return that. 
        /// We then rebuild the index to make sure it's up to date.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="Name"></param>
        /// <returns></returns>
        public AssetItem GetAssetItemForObject(UnityEngine.Object o)
        {
            System.Type ot = o.GetType();
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            string Name = AssetItem.GetEvilName(o);

            if (TypeDic.ContainsKey(Name))
            {
                return TypeDic[Name];
            }
            return null;
        }

        
        public List<AssetItem> GetAssetItems(string recipe, bool LookForLODs = false)
        {
            AssetItem ai = GetAssetItem<UMAWardrobeRecipe>(recipe);
            if (ai != null)
            {
                return GetAssetItems(ai.Item as UMAWardrobeRecipe, LookForLODs);
            }
            return new List<AssetItem>();
        }

        public List<AssetItem> GetAssetItems(UMAPackedRecipeBase recipe, bool LookForLODs = false)
		{
            if (recipe is UMAWardrobeCollection)
            {
                return new List<AssetItem>();
            }
			UMAPackedRecipeBase.UMAPackRecipe PackRecipe = recipe.PackedLoad(UMAContextBase.Instance);

			var Slots = PackRecipe.slotsV3;

			if (Slots == null)
				return GetAssetItemsV2(PackRecipe, LookForLODs);

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(typeof(SlotDataAsset));
            List<AssetItem> returnval = new List<AssetItem>();

			foreach (var slot in Slots)
			{
                // We are getting extra blank slots. That's weird. 

                if (string.IsNullOrWhiteSpace(slot.id)) continue;

				AssetItem s = GetAssetItem<SlotDataAsset>(slot.id);
				if (s != null)
				{
                    returnval.Add(s);
                    string LodIndicator = slot.id.Trim() + "_LOD";
                    if (slot.id.Contains("_LOD"))
                    {
                        // LOD is directly in the base recipe. 
                        LodIndicator = slot.id.Substring(0, slot.id.Length-1);
                    }

                    if (slot.overlays != null)
                    {
                        foreach (var overlay in slot.overlays)
                        {
                            AssetItem o = GetAssetItem<OverlayDataAsset>(overlay.id);
                            if (o != null)
                            {
                                returnval.Add(o);
                            }
                        }
                    }
                    if (LookForLODs)
                    {
                        foreach (string slod in TypeDic.Keys)
                        {
                            if (slod.StartsWith(LodIndicator))
                            {
                                AssetItem lodSlot = GetAssetItem<SlotDataAsset>(slod);
                                returnval.Add(lodSlot);
                            } 
                        }
                    }
                }
			}
			return returnval;
		}

		private List<AssetItem> GetAssetItemsV2(UMAPackedRecipeBase.UMAPackRecipe PackRecipe, bool LookForLods)
		{
			List<AssetItem> returnval = new List<AssetItem>();

            var Slots = PackRecipe.slotsV2;

			if (Slots == null)
			{
				return returnval;
			}

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(typeof(SlotDataAsset));

            foreach (var slot in Slots)
			{
				if (slot == null)
					continue;
				if (string.IsNullOrEmpty(slot.id))
					continue;
                string LodIndicator = slot.id.Trim() + "_LOD";
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
                if (LookForLods)
                {
                    foreach (string slod in TypeDic.Keys)
                    {
                        if (slod.StartsWith(LodIndicator))
                        {
                            AssetItem lodSlot = GetAssetItem<SlotDataAsset>(slod);
                            returnval.Add(lodSlot);
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

        public List<AssetItem> GetAssetItems<T>()
        {
            List<AssetItem> Items = new List<AssetItem>();
            System.Type ot = typeof(T);
            System.Type theType = TypeToLookup[ot];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            Items.AddRange(TypeDic.Values);

            return Items;
        }
        public List<AssetItem> GetAssetItems(Type t)
        {
            List<AssetItem> Items = new List<AssetItem>();
            System.Type theType = TypeToLookup[t];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            Items.AddRange(TypeDic.Values);

            return Items;
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

        public T GetAsset<T>(string name, string[] foldersToSearch) where T : UnityEngine.Object
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

        public T GetAsset<T>(string name) where T : UnityEngine.Object
        {
            var thisAssetItem = GetAssetItem<T>(name);
            if (thisAssetItem != null)
            {
                return (thisAssetItem.Item as T);
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
        /// Load all items from the asset bundle into the index.
        /// </summary>
        /// <param name="ab"></param>
        public void AddFromAssetBundle(AssetBundle ab)
        {
            foreach(Type t in Types)
            {
                var objs = ab.LoadAllAssets(t);
                
                foreach(UnityEngine.Object o in objs)
                {
                    ProcessNewItem(o, false, false);
                }
            }
        }

        /// <summary>
        /// Load all items from the asset bundle into the index.
        /// </summary>
        /// <param name="ab"></param>
        public void UnloadBundle(AssetBundle ab)
        {
            foreach (Type t in Types)
            {
                var objs = ab.LoadAllAssets(t);

                foreach (UnityEngine.Object o in objs)
                {
                    RemoveItem(o);
                }
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

#endregion

#region Addressables

#if UNITY_EDITOR
		GameObject EditorUMAContextBase;
#endif
		public UMAContextBase GetContext()
		{
			UMAContextBase instance = UMAContextBase.Instance;
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

		public void DestroyEditorUMAContextBase()
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

#if UMA_ADDRESSABLES
        public string GetLabel(UMARecipeBase recipe)
        {
            return recipe.AssignedLabel;
        }

        public AsyncOperationHandle<IList<UnityEngine.Object>> PreloadWardrobe(DynamicCharacterAvatar avatar, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			RaceData race = GetAsset<RaceData>(avatar.activeRace.name);

			// preload any assigned recipes.
			foreach (var wr in avatar.WardrobeRecipes.Values) 
			{
                //Debug.Log("Adding Wardrobe recipe: " + wr.name);
                if (wr != null)
                    keys.Add(GetLabel(wr));
			}

			// preload utility recipes
			foreach (var tr in avatar.umaAdditionalRecipes)
			{
                if (tr != null)
			        keys.Add(GetLabel(tr));
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
                if (race.baseRaceRecipe != null)
                    keys.Add(GetLabel(race.baseRaceRecipe));
			}


			// preload any assigned recipes.
			foreach (var wr in avatar.WardrobeRecipes.Values)
			{
                if (wr != null)
                    keys.Add(GetLabel(wr));
            }

            if (avatar.umaAdditionalRecipes != null)
            {
                foreach (var tr in avatar.umaAdditionalRecipes)
                {
                    if (tr != null)
                        keys.Add(GetLabel(tr));
                }
            }
			var op = LoadLabelList(keys, keepLoaded);
			return op;
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(RaceData theRace, bool keepLoaded = false)
		{
			return LoadLabel(GetLabel(theRace.baseRaceRecipe), keepLoaded);
		}

		public AsyncOperationHandle<IList<UnityEngine.Object>> Preload(List<RaceData> theRaces, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			foreach(RaceData rc in theRaces)
			{
				string key = GetLabel(rc.baseRaceRecipe);

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
			keys.Add(GetLabel(theRecipe));
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
				Keys.Add(GetLabel(utr));
			}

			return LoadLabelList(Keys,keepLoaded);
		}
#if UNITY_EDITOR
        async void ValidateSingleKey(string s) 
        {
            var result = await Addressables.LoadResourceLocationsAsync(s).Task;

            string info = "Label "+s+" has "+result.Count + " Resource Locations.";

            Debug.Log(info);
        }
#endif

        //static List<UnityEngine.Object> ProcessedItems = new List<UnityEngine.Object>();

        public AsyncOperationHandle<IList<UnityEngine.Object>> LoadLabelList(List<string> Keys, bool keepLoaded)
        {
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
            }

            var op = Addressables.LoadAssetsAsync<UnityEngine.Object>(Keys, result =>
            {
				// The last items is now passed here AFTER the completed event, breaking everything. 
				// change to event model here.
                //ProcessedItems.Add(result);
                //ProcessNewItem(result, true, keepLoaded);
            }, Addressables.MergeMode.Union, true);
			op.Completed += ProcessItems;
            if (!keepLoaded)
            {
                string info = "";
                foreach (string s in Keys)
                    info += Keys + "; ";
                LoadedItems.Add(new CachedOp(op, info));
            }
            return op;
        }

		private void ProcessItems(AsyncOp Op) {
			if (Op.IsDone) {
				foreach(var o in Op.Result) {
					//ProcessedItems.Add(o);
					ProcessNewItem(o, true, true);
				}
			}
		}

#endif

        private void RemoveItem(UnityEngine.Object ob)
        {
            if (!IsIndexedType(ob.GetType()))
                return;
            System.Type ot = ob.GetType();
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            string Name = AssetItem.GetEvilName(ob);

            if (TypeDic.ContainsKey(Name))
            {
                TypeDic.Remove(Name);
            }
            if (GuidTypes.ContainsKey(Name))
            {
                GuidTypes.Remove(Name);
            }
        }

        public void ProcessNewItem(UnityEngine.Object result, bool isAddressable, bool keepLoaded)
        {
            if (!IsIndexedType(result.GetType())) // JRRM
                return;

            AssetItem resultItem = GetAssetItemForObject(result);
            if (resultItem == null)
            {
                AssetItem ai = new AssetItem(result.GetType(), result);
                ai.IsAddressable = isAddressable;
                ai.IsAlwaysLoaded = keepLoaded;
                AddAssetItem(ai);
                ai.AddReference();
            }
            else
            {
                if (keepLoaded) resultItem.IsAlwaysLoaded = keepLoaded;
                resultItem._SerializedItem = result;
                resultItem.AddReference();
            }

            if (result is UMAWardrobeRecipe)
            {
                AddRaceRecipe(result as UMAWardrobeRecipe);
            }
            else if (result is SlotDataAsset)
            {
                SlotDataAsset sd = result as SlotDataAsset;
                if (sd.material == null)
                {
                    if (!string.IsNullOrEmpty(sd.materialName))
                    {
                        sd.material = Instance.GetAsset<UMAMaterial>(sd.materialName);
                    }
                }
            }
            else if (result is OverlayDataAsset)
            {
                OverlayDataAsset od = result as OverlayDataAsset;
                if (od.material == null)
                {
                    if (!string.IsNullOrEmpty(od.materialName))
                    {
                        od.material = Instance.GetAsset<UMAMaterial>(od.materialName);
                    }
                }
            }
        }

        public void PostBuildMaterialFixup()
        {
#if UNITY_EDITOR
            var slots = GetAllAssets<SlotDataAsset>();
            var overlays = GetAllAssets<OverlayDataAsset>();
            foreach(SlotDataAsset sd in slots)
            {
                if (sd.material == null)
                {
                    if (!string.IsNullOrEmpty(sd.materialName))
                    {
                        sd.material = Instance.GetAsset<UMAMaterial>(sd.materialName);

                        if (sd.material == null)
                        {
                            Debug.LogWarning("Unable to find material '" + sd.materialName + "' for slot: " + sd.name);
                        }
                        EditorUtility.SetDirty(sd);
                    }
                    else
                    {
                        Debug.LogWarning("Material name is null on slot: " + sd.name);
                    }
                }
            }
            foreach (OverlayDataAsset od in overlays)
            {
                if (od.material == null)
                {
                    if (!string.IsNullOrEmpty(od.materialName))
                    {
                        od.material = Instance.GetAsset<UMAMaterial>(od.materialName);
                        if (od.material == null)
                        {
                            Debug.LogWarning("Unable to find material '" + od.materialName + "' for overlay: " + od.name);
                        }
                        EditorUtility.SetDirty(od);
                    }
                    else
                    {
                        Debug.LogWarning("Material name is null on overlay: " + od.name);
                    }
                }
            }
            ForceSave();
#endif
        }
#if UMA_ADDRESSABLES
        public void Unload(AsyncOperationHandle<IList<UnityEngine.Object>> AssetOperation)
        {
#if SUPER_LOGGING
            Debug.Log("Unloading AsyncOperationHandle<> in Indexer.Unload()");
#endif
            foreach(UnityEngine.Object obj in AssetOperation.Result)
            {
                ReleaseReference(obj);
            }
            Addressables.Release(AssetOperation);
            LoadedItems.RemoveAll(x => x.Operation.Equals(AssetOperation));
        }

        public void UnloadAll(bool forceResourceUnload)
		{

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
                    ai.ReferenceCount = 0;
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
                    ai.ReferenceCount = 0;
				}
			}
			LoadedItems.Clear();
			if (forceResourceUnload)
				{
					Resources.UnloadUnusedAssets();
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
            RemoveAsset(o.GetType(), AssetItem.GetEvilName(o));
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
                if (!TypeToLookup.ContainsKey(ai._Type))
                {
                    Debug.LogError("Unable to get Lookup Type for Type: " + ai._Type.ToString() + " for Object " + ai._Name);
                    return false;
                }

                System.Type theType = TypeToLookup[ai._Type];
                Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

                if (TypeDic == null)
                {
                    Debug.Log("Unable to add asset item!. Unable to get Type Dictionary of type " + theType.ToString() + "For object " + ai._Name);
                    return false;
                }

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
                if (string.IsNullOrWhiteSpace(ai._Name))
                {
                    throw new Exception("Invalid name on Asset type "+ai._Type.ToString()+" - asset is: "+ai.Item.name);
                }
                if (ai.IsAddressable)
                {
                    ai._SerializedItem = null;
                }
#if UMA_ADDRESSABLES
                // Debug.Log("Getting asset entry");
                //UMAAddressablesSupport.instance.EditorAddressableUpdateItem(ai);

                AddressableInfo ainfo = AddressableUtility.GetAddressableInfo(ai._Path);
                if (ainfo != null)
                {
                    ai.IsAddressable = true;
                    ai.AddressableAddress = ainfo.AddressableAddress;
                    ai.AddressableGroup = ainfo.AddressableGroup;
                    ai.AddressableLabels = ainfo.AddressableLabels;
                }
#endif
                if (!string.IsNullOrEmpty(ai._Guid))
                {
                    if (!GuidTypes.ContainsKey(ai._Guid))
                    {
                        GuidTypes.Add(ai._Guid, ai);
                    }
                }
#endif

                if (!TypeDic.ContainsKey(ai._Name))
                {
                    TypeDic.Add(ai._Name, ai);
                }
                else
                {
                    // warning?
                }
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

        /// <summary>
        /// releases an asset an asset reference
        /// </summary>
        /// <param name="type"></param>
        /// <param name="Name"></param>
        public void ReleaseReference(UnityEngine.Object obj)
        {
            string Name = AssetItem.GetEvilName(obj);

            // Leave if this is an unreferenced type - for example, a texture (etc).
            // This can happen because these are referenced by the Overlay.
            if (!TypeToLookup.ContainsKey(obj.GetType()))
                return;

            System.Type theType = TypeToLookup[obj.GetType()];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            if (TypeDic.ContainsKey(Name))
            {
                AssetItem ai = TypeDic[Name];
                ai.ReleaseItem();
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
            ai = new AssetItem(TypeToLookup[type], o);
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
                GuidTypes.Remove(ai._Guid);
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

#if UNITY_EDITOR
        public void ClearAddressableFlags()
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
#endif
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

        class recipeEqualityComparer : IEqualityComparer<UMAWardrobeRecipe>
        {
            public bool Equals(UMAWardrobeRecipe b1, UMAWardrobeRecipe b2)
            {
                if (b2 == null && b1 == null)
                    return true;
                else if (b1 == null || b2 == null)
                    return false;
                else if (b1.name == b2.name)
                    return true;
                else
                    return false;
            }

            public int GetHashCode(UMAWardrobeRecipe bx)
            {
                return bx.GetHashCode();
            }
        }

        private recipeEqualityComparer req;
        
        private void AddRaceRecipe(UMAWardrobeRecipe uwr)
        {
            if (!uwr) return;
           // if (req == null)
           //     req = new recipeEqualityComparer();

            foreach (string racename in uwr.compatibleRaces)
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
                if (!sl[uwr.wardrobeSlot].Contains(uwr))//, req))
                {
                    sl[uwr.wardrobeSlot].Add(uwr);
                }
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
                AddRaceRecipe(uwr);
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
        public List<AssetItem> UpdateSerializedList()
        {
            SerializedItems.Clear();
			foreach (System.Type type in TypeToLookup.Keys)
            {
				if (type == TypeToLookup[type])
				{
                	Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
                    if (TypeDic != null)
                    {
                        foreach (AssetItem ai in TypeDic.Values)
                        {
                            if (ai.IsAddressable)
                            {
                                ai._SerializedItem = null;
                            }
                            SerializedItems.Add(ai);
                        }
                    }
				}
            }
            return SerializedItems;
        }

        /// <summary>
        /// Builds a list of types and a string to look them up.
        /// </summary>
		public void BuildStringTypes()
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
                    if (IsText(CurrentType))
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
                            if (SkipDuplicateType(o, CurrentType)) continue;
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

        private static bool IsText(Type CurrentType)
        {
            return CurrentType == typeof(TextAsset);
        }

        private bool SkipDuplicateType(UnityEngine.Object o, Type currentType)
        {
            if (o.GetType() == typeof(UMAWardrobeRecipe) && currentType == typeof(UMATextRecipe)) return true;
            if (o.GetType() == typeof(UMAWardrobeCollection) && currentType == typeof(UMATextRecipe)) return true;
            if (o.GetType() == typeof(UMAWardrobeCollection) && currentType == typeof(UMAWardrobeRecipe)) return true;
            return false;
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
        public void AddReferences()
        {
            // Rebuild the tables
            UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
                if (!ai.IsAddressable)
				{
					ai.CacheSerializedItem();
				}
				else
				{
                    ai.FreeReference();
				}
            }
			UpdateSerializedDictionaryItems();
            ForceSave();
        }

		public void UpdateReferences() {
			// Rebuild the tables
			UpdateSerializedList();
			foreach(AssetItem ai in SerializedItems) {
				if(!ai.IsAddressable) {
					ai.CacheSerializedItem();
				} else {
					ai.FreeReference();
				}
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
                ai.FreeReference();
            }
            UpdateSerializedDictionaryItems();
            ForceSave();
            Resources.UnloadUnusedAssets();
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
                ai.IsAddressable = false;
                ai.AddressableLabels = "";
                ai.AddressableGroup = "";
                ai.AddressableAddress = "";
#if UNITY_EDITOR
#if UMA_ADDRESSABLES
                AddressableInfo ainfo = AddressableUtility.GetAddressableInfo(ai._Path);
                if (ainfo != null)
                {
                    ai.AddressableAddress = ainfo.AddressableAddress;
                    ai.IsAddressable = true;
                    ai.AddressableGroup = ainfo.AddressableGroup;
                    ai._SerializedItem = null;
                    ai.AddressableLabels = ainfo.AddressableLabels;
                }
                else
#endif
#endif

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
                        ai.FreeReference();
                    }
                }
            }

            UpdateSerializedDictionaryItems();
            RebuildRaceRecipes();
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
        (typeof(TextAsset)),
        (typeof(UMAMaterial))
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
        { (typeof(DynamicUMADnaAsset)), (typeof(DynamicUMADnaAsset)) },
        { (typeof(UMAMaterial)),(typeof(UMAMaterial)) }
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
