
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif



namespace UMA
{
    [System.Serializable]
    public class AssetItem
#if UNITY_EDITOR
        : System.IEquatable<AssetItem>, System.IComparable<AssetItem>, ISerializationCallbackReceiver
#endif
    {
        #region Fields
        public const string AddressableFolder = "UMA/";
        private System.Type _TheType;
        public string _BaseTypeName;
        public string _Name;
        public Object _SerializedItem;
        public string _Path;
		public string _Guid;
        public string _Address;
        public bool IsResource;
        public bool IsAssetBundle;
		public bool IsAddressable;
		public bool IsAlwaysLoaded;
        public bool Ignore; // does not go into adressables or resources.
		public string AddressableGroup;
		public string AddressableAddress
        {
            get
            {
                if (IsAddressable && string.IsNullOrEmpty(_Address))
                {
                   return AddressableFolder + _Type.Name + "-" + EvilName;
                }
                return _Address;
            }
            set
            {
                _Address = value;
            }
        }
        public string AddressableLabels;
		public int ReferenceCount;

        #endregion
        #region Properties
        public System.Type _Type
        {
            get
            {
                if (_TheType != null && _TheType != typeof(UnityEngine.Object))
                {
                    return _TheType;
                }

                if (!UMAAssetIndexer.TypeFromString.ContainsKey(_BaseTypeName))
                {
                    if (_BaseTypeName.Contains("SlotData"))
                    {
                        _TheType = typeof(SlotDataAsset);
                    }
                    else if (_BaseTypeName.Contains("OverlayData"))
                    {
                        _TheType = typeof(OverlayDataAsset);
                    }
                    else if (_BaseTypeName.Contains("Animator"))  // for some reason the animatorcontrollers were blowing up in 2019.3
                    {
                        _TheType = typeof(RuntimeAnimatorController);
                    }
                    else if (_BaseTypeName.Contains("RaceData"))
                    {
                        _TheType = typeof(RaceData);
                    }
                }
                else
                {
                    _TheType = UMAAssetIndexer.TypeFromString[_BaseTypeName];
                }
                return _TheType;
            }
        }

        public AssetItem CreateSerializedItem(bool ForceItemSave)
        {
            if (ForceItemSave)
            {
                // If this flag is set, then we must serialize the item also (this is used when building the executable).
                return new AssetItem(this._Type, this._Name, this._Path, this.Item);
            }
            else
            {
                return new AssetItem(this._Type, this._Name, this._Path, null);
            }
        }

        public T GetItem<T>() where T : Object
        {
            return Item as T;
        }

        public Object Item
        {
            get
            {
#if UNITY_EDITOR
                if (_SerializedItem != null)
                {
                    return _SerializedItem;
                }

                // Items that are addressable should not be cached.
                // but the editors still need them, so we'll load them from
                // the assetdatabase as needed.
#if !UMA_ALWAYSGETADDR_NO_PROD
                if (IsAddressable)
                {
                    if (Application.isPlaying)
                    {
                        return null;
                    }
                    else
                    {
                        return GetItem();
                    }
                }
#endif

                CacheSerializedItem(); 
                return _SerializedItem;
#else
                return _SerializedItem;
#endif
            }
        }

        public string _AssetBaseName
        {
            get
            {
                return System.IO.Path.GetFileNameWithoutExtension(_Path);
            }
        }

		private Object GetItem()
		{
#if UNITY_EDITOR
			Object itemObject = AssetDatabase.LoadAssetAtPath(_Path, _Type);
			if (itemObject == null)
			{
				// uhoh. It's gone.
				if (!string.IsNullOrEmpty(_Guid))
				{
					// at least we have a guid. Let's try to find it from that...
					_Path = AssetDatabase.GUIDToAssetPath(_Guid);
					if (!string.IsNullOrEmpty(_Path))
					{
						itemObject = AssetDatabase.LoadAssetAtPath(_Path, _Type);
					}
				}
				// No guid, or couldn't even find by GUID.
				// Let's search for it?
				if (itemObject == null)
				{
					string s = _Type.Name;
					string[] guids = AssetDatabase.FindAssets(_Name + " t:" + s);
					if (guids.Length > 0)
					{
						_Guid = guids[0];
						_Path = AssetDatabase.GUIDToAssetPath(_Guid);
						itemObject = AssetDatabase.LoadAssetAtPath(_Path, _Type);
					}
				}
			}
#else
			Object itemObject = null;
#endif
			return itemObject;
		}

		public Object CacheSerializedItem()
		{
#if UNITY_EDITOR
			if (_SerializedItem != null) return _SerializedItem;
#if SUPER_LOGGING
            Debug.Log("Loading item in AssetItem: " + _Name);
#endif
            //if (IsAddressable) return;

            _SerializedItem = GetItem();
            return _SerializedItem;
#else
            // This function does nothing in a build.
            return null;
#endif
        }

        public static  string TranslatedName(string Name)
        {
#if UMA_INDEX_LC
            return Name.ToLower();
#else
            return Name;
#endif
        }

        public static string GetEvilName(Object o)
        {
            if (!o)
            {
                return "<Not Found!>";
            }
            if (o is SlotDataAsset)
            {
                SlotDataAsset sd = o as SlotDataAsset;
                if (!string.IsNullOrEmpty(sd.slotName))
                {
                    return TranslatedName(sd.slotName);
                }
            }
            if (o is OverlayDataAsset)
            {
                OverlayDataAsset od = o as OverlayDataAsset;
                if (!string.IsNullOrEmpty(od.overlayName))
                {
                    return TranslatedName(od.overlayName);
                }
            }
            if (o is RaceData)
            {
                RaceData rd = o as RaceData;
                if (!string.IsNullOrEmpty(rd.raceName))
                {
                    return TranslatedName(rd.raceName);
                }
            }

            return TranslatedName(o.name);
        }

        public string EvilName
        {
            get
            {
                Object o = Item;
                return GetEvilName(o);
            }
        }
#endregion

        public void AddReference()
        {
            ReferenceCount++;
        }

        public void FreeReference()
        {
            ReferenceCount = 0;
            _SerializedItem = null;
        }

		public void ReleaseItem()
		{
            if (IsAddressable)
            {
                ReferenceCount--;
                if (ReferenceCount < 1)
                {
                    if (ReferenceCount < 0)
                    {
                        Debug.LogError("Reference count is negative on AssetItem " + this._Name + " of Type " + this._TheType+". This should not happen.");
                        return;
                    }
                    FreeReference();
                }
            }
            else
            {
                FreeReference();
            }
        }

        public bool IsLoaded
        {
            get
            {
                if (IsAddressable)
                {
                    return _SerializedItem != null;
                }
                return true;
            }
        }

        public bool IsOverlayDataAsset
        {
            get
            {
                return _Type == typeof(OverlayDataAsset);
            }
        }

        public bool IsSlotDataAsset
        {
            get
            {
                return _Type == typeof(SlotDataAsset);
            }
        }

#region Methods (edit time)
#if UNITY_EDITOR

        public string ToString(string SortOrder)
        {
            if (SortOrder == "AssetName")
            {
                return _AssetBaseName;
            }

            if (SortOrder == "FilePath")
            {
                return _Path;
            }

            return _Name;
        }

        public bool Equals(AssetItem other)
        {
            if (other == null)
            {
                return false;
            }

            if (UMAAssetIndexer.SortOrder == "AssetName")
            {
                if (this._AssetBaseName == other._AssetBaseName)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (UMAAssetIndexer.SortOrder == "FilePath")
            {
                if (this._Path == other._Path)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (this._Name == other._Name)
            {
                return true;
            }

            return false;
        }

        public int CompareTo(AssetItem other)
        {
            // A null value means that this object is greater.
            if (other == null)
            {
                return 1;
            }

            if (UMAAssetIndexer.SortOrder == "AssetName")
            {
                return (this._AssetBaseName.CompareTo(other._AssetBaseName));
            }

            if (UMAAssetIndexer.SortOrder == "FilePath")
            {
                return this._Path.CompareTo(other._Path);
            }

            return this._Name.CompareTo(other._Name);
        }

        public void OnBeforeSerialize()
        {
            if (IsAddressable)
            {
                _SerializedItem = null;
            }
        }

        public void OnAfterDeserialize()
        {
            if (IsAddressable)
            {
                _SerializedItem = null;
            }
        }

#endif
#endregion
#region Constructors
        public AssetItem(System.Type Type, string Name, string Path, Object Item)
        {
            if (Type == null)
            {
                return;
            }

            _TheType = Type;
            _BaseTypeName = Type.Name;
            _Name = Name;
            _SerializedItem = Item;
            _Path = Path;
#if UNITY_EDITOR
			_Guid = AssetDatabase.AssetPathToGUID(_Path);
#endif
        }
        public AssetItem(System.Type Type, Object Item)
        {
            if (Type == null)
            {
                return;
            }
#if UNITY_EDITOR
            _Path = AssetDatabase.GetAssetPath(Item.GetInstanceID());
			_Guid = AssetDatabase.AssetPathToGUID(_Path);
#endif
            _TheType = Type;
            _BaseTypeName = Type.Name;
            _SerializedItem = Item;
            _Name = EvilName;
        }
#endregion
    }
}
