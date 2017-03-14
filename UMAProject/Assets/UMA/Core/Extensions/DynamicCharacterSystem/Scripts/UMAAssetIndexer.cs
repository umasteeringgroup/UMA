using UnityEngine;
using System.Collections.Generic;
using UMA;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

public class UMAAssetIndexer : MonoBehaviour, ISerializationCallbackReceiver
{
    #region constants and static strings
    public const string IndexPath = "/InternalDataStore/InGame/Resources/AssetIndexer";
    public static string SortOrder = "Name";
    public static string[] SortOrders = { "Name", "AssetName" };
    #endregion
    #region Internal Classes
    [System.Serializable]
    public class AssetItem
#if UNITY_EDITOR        
        : System.IEquatable<AssetItem>, System.IComparable<AssetItem>
#endif
    {
        #region Fields
        public string _QualifiedName;
        public string _Name;
        public Object _Item;
        public string _Path;
        public bool IsResource;
        public bool IsAssetBundle;
        #endregion
        #region Properties
        public System.Type _Type
        {
            get
            {
                if (_QualifiedName == null)
                    return null;
                return System.Type.GetType(_QualifiedName);
            }
        }

        public string _AssetBaseName
        {
            get
            {
                return System.IO.Path.GetFileNameWithoutExtension(_Path);
            }
        }

        public string AssetName
        {
            get
            {
                return _Item.name;
            }
        }

        public string EvilName
        {
            get
            {
                Object o = _Item;

                if (o is SlotDataAsset)
                {
                    SlotDataAsset sd = o as SlotDataAsset;
                    return sd.slotName;
                }
                if (o is OverlayDataAsset)
                {
                    OverlayDataAsset od = o as OverlayDataAsset;
                    return od.overlayName;
                }
                if (o is RaceData)
                {
                    return (o as RaceData).raceName;
                }
                return o.name;
            }
        }
        #endregion
        #region Methods (edit time)
#if UNITY_EDITOR

        public string ToString(string SortOrder)
        {
            if (SortOrder == "AssetName")
                return _AssetBaseName;
            if (SortOrder == "FilePath")
                return _Path;
            return _Name;
        }

        public bool Equals(AssetItem other)
        {
            if (other == null)
                return false;

            if (SortOrder == "AssetName")
            {
                if (this._AssetBaseName == other._AssetBaseName)
                    return true;
                else 
                    return false;
            }

            if (SortOrder == "FilePath")
            {
                if (this._Path == other._Path)
                    return true;
                else
                    return false;

            }

            if (this._Name == other._Name)
                return true;

            return false;
        }

        public int CompareTo(AssetItem other)
        {
            // A null value means that this object is greater.
            if (other == null)
                return 1;

            if (SortOrder == "AssetName")
            {
                return (this._AssetBaseName.CompareTo(other._AssetBaseName));
            }

            if (SortOrder == "FilePath")
            {
                return this._Path.CompareTo(other._Path);
            }

            return this._Name.CompareTo(other._Name);
        }

#endif
        #endregion
        #region Constructors
        public AssetItem(System.Type Type, string Name, string Path, Object Item)
        {
            if (Type == null) return;


            _QualifiedName = Type.AssemblyQualifiedName;
            _Name = Name;
            _Item = Item;
            _Path = Path;
        }
        public AssetItem(System.Type Type, Object Item)
        {
            if (Type == null) return;
            _QualifiedName = Type.AssemblyQualifiedName;
            _Item = Item;
            _Name = EvilName;
        }
        #endregion
    }
    #endregion
    #region Fields
    public Dictionary<System.Type, System.Type> TypeToLookup = new Dictionary<System.Type, System.Type>()
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
    public List<AssetItem> Items = new List<AssetItem>();
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

    public static UMAAssetIndexer Instance
    {
        get
        {
            if (theIndex == null)
            {
#if UNITY_EDITOR
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
        bool changed = false;

        // Build a dictionary of the items by path.
        Dictionary<string, AssetItem> ItemsByPath = new Dictionary<string, AssetItem>();
        UpdateList();
        foreach (AssetItem ai in Items)
        {
            ItemsByPath.Add(ai._Path, ai);
        }

        for (int i=0;i<importedAssets.Length;i++)
        {
            if (ItemsByPath.ContainsKey(importedAssets[i]))
            {
                try
                {
                    AssetItem ai = ItemsByPath[importedAssets[i]];
                    ai._Name = ai.EvilName;
                    ai._Path = AssetDatabase.GetAssetPath(ai._Item.GetInstanceID());
                    changed = true;
                }
                finally
                {

                }
            }
        }

        for (int i = 0; i < movedAssets.Length; i++)
        {
            string NewPath = movedAssets[i];
            string OldPath = movedFromAssetPaths[i];

            // Check to see if this is an indexed asset.
            if (ItemsByPath.ContainsKey(OldPath))
            {
                // One of our indexed assets.
                if (InAssetBundleFolder(NewPath))
                {
                    // Null it out, so we don't add it to the index...
                    ItemsByPath[OldPath] = null;
                    changed = true;
                    continue;
                }
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
        EditorUtility.SetDirty(this.gameObject);
        AssetDatabase.SaveAssets();
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
        Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(ot);
        if (TypeDic.ContainsKey(Name))
        {
            return TypeDic[Name];
        }
        foreach (AssetItem ai in TypeDic.Values)
        {
            if (Name == ai.EvilName)
            {
                RebuildIndex();
                return ai;
            }
        }
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
        var ret = new List<T>();
        System.Type ot = typeof(T);

        Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(ot);

        foreach (KeyValuePair<string, AssetItem> kp in TypeDic)
        {
            if (AssetFolderCheck(kp.Value, foldersToSearch))
                ret.Add((kp.Value._Item as T));
        }
        return ret;
    }

    public T GetAsset<T>(int nameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
    {
        System.Type ot = typeof(T);
        Dictionary<string, AssetItem> TypeDic = (Dictionary<string, AssetItem>)TypeLookup[ot];
        string assetName = "";
        int assetHash = -1;
        foreach (KeyValuePair<string, AssetItem> kp in TypeDic)
        {
            assetName = "";
            assetHash = -1;
            GetEvilAssetNameAndHash(typeof(T), kp.Value._Item, ref assetName, assetHash);
            if (assetHash == nameHash)
            {
                if (AssetFolderCheck(kp.Value, foldersToSearch))
                    return (kp.Value._Item as T);
                else
                    return null;
            }
        }
        return null;
    }

    public T GetAsset<T>(string name, string[] foldersToSearch = null) where T : UnityEngine.Object
    {
        var thisAssetItem = GetAssetItem<T>(name);
        if (thisAssetItem != null)
        {
            if (AssetFolderCheck(thisAssetItem, foldersToSearch))
                return (thisAssetItem._Item as T);
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
            foreach(string s in pathsInBundle)
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
    public void AddAsset(System.Type type, string Name, string Path, Object o, bool SkipBundleCheck = false)
    {
        AssetItem ai = new AssetItem(type, Name,Path, o);
        AddAssetItem(ai, SkipBundleCheck);
    }

    /// <summary>
    /// Adds an asset to the index. If the name already exists, it is not added. (Should we do this, or replace it?)
    /// </summary>
    /// <param name="ai"></param>
    public void AddAssetItem(AssetItem ai, bool SkipBundleCheck = false)
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
            string Path = AssetDatabase.GetAssetPath(ai._Item.GetInstanceID());
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
            if (ai._Item != null)
            {
                AddAsset(ai._Type, ai._Name, ai._Path, ai._Item, true);
            }
        }
    }

    /// <summary>
    /// Creates a lookup dictionary for a list. Used when reloading after deserialization
    /// </summary>
    /// <param name="type"></param>
    public void CreateLookupDictionary(System.Type type)
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

#if UNITY_EDITOR
    public void Clear()
    {
        // Rebuild the tables
        Items.Clear();
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
        UpdateList();
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
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

        // Add the additional Types.
        foreach (string s in IndexedTypeNames)
        {
            System.Type sType = System.Type.GetType(s);
            newTypes.Add(sType);
            if (!TypeToLookup.ContainsKey(sType))
            {
                TypeToLookup.Add(sType, sType);
            }
        }

        Types = newTypes.ToArray();

        UpdateDictionaries();
    }
    #endregion
}