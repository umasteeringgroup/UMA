using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// This ScriptableObject class is used for advanced mesh hiding with UMA and the DCS.  
    /// </summary>
    /// <remarks>
    /// This class simply stores a link to a SlotDataAsset (the slot to get hiding applied to) and a list of the slot's triangles as a BitArray.
    /// Each bit indicates a flag of whether the triangle should be hidden or not in the final generated UMA.
    /// After creating the MeshHideAsset, it can then be added to a list in a wardrobe recipes.  This makes it so when the wardrobe recipe is active and the slot associated
    /// with the MeshHideAsset is found in the UMA recipe, then apply the triangle hiding to the slot.  MeshHideAsset's are also unioned, so multiple MeshHideAssets with
    /// the same slotData can combine to hide their unioned list.
    /// </remarks>
    public class MeshHideAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// The asset we want to apply mesh hiding to if found in the generated UMA.
        /// </summary>
        /// <value>The SlotDataAsset.</value>
        [SerializeField]
        public SlotDataAsset asset
        {
            get
			{
				if (_asset != null)
				{
					_assetSlotName = _asset.slotName;
					_asset = null;
				}
				return UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(_assetSlotName);
			}
			set
            {
				if (value != null)
                {
                    _assetSlotName = value.slotName;
                }
                else
				{
					_assetSlotName = "";
				}
            }
        } 
        [SerializeField]
        private SlotDataAsset _asset;

		public bool HasReference
		{
			get { return _asset != null;  }
		}

        public string AssetSlotName
        {
            get {
				if (string.IsNullOrEmpty(_assetSlotName))
				{
					if (_asset != null)
					{
						_assetSlotName = _asset.slotName;
					}
				}
				return _assetSlotName; 
				}
            set
            {
                _assetSlotName = value;
            }
        }
        [SerializeField]
        private string _assetSlotName = "";

        /// <summary>
        /// BitArray of the triangle flags list. The list stores only the first index of the triangle vertex in the asset's triangle list.
        /// </summary>
        /// <value>The array of BitArrays. A BitArray for each submesh triangle list.</value>
        public BitArray[] triangleFlags { get { return _triangleFlags; }}
        private BitArray[] _triangleFlags; 


        [System.Serializable]
        public class serializedFlags
        {
            public int[] flags;
			public int Count;

            public serializedFlags(int count)
            {
				Count = count;
				flags = new int[(Count + 31) / 32];
            }
        }
        [SerializeField]
        private serializedFlags[] _serializedFlags;

        public int SubmeshCount
        {
            get
            {
                if (_triangleFlags != null)
                {
                    return _triangleFlags.Length;
                }
                else
                {
                    return 0;
                }
            }
        }

		/// <summary>
		/// If this contains a reference to an asset, it is freed.
		/// This asset reference is no longer needed, and 
		/// forces the asset to be included in the build.
		/// It is kept only for upgrading from earlier UMA versions
		/// </summary>
		public void FreeReference()
		{
			if (_asset != null)
			{
				_assetSlotName = _asset.slotName;
				_asset = null;
			}
		}

		/// <summary>
		/// Gets the total triangle count in the multidimensional triangleFlags.
		/// </summary>
		/// <value>The triangle count.</value>
		public int TriangleCount 
        { 
            get 
            {
                if (_triangleFlags != null)
                {
                    int total = 0;
                    for (int i = 0; i < _triangleFlags.Length; i++)
                    {
                        total += _triangleFlags[i].Count;
                    }

                    return total;
                }
                else
                {
                    return 0;
                }
            }
        }   

        /// <summary>
        /// Gets the hidden triangles count.
        /// </summary>
        /// <value>The hidden triangles count.</value>
        public int HiddenCount
        {
            get
            {
                if (_triangleFlags != null)
                {
                    int total = 0;
                    for (int i = 0; i < _triangleFlags.Length; i++)
                    {
                        total += UMAUtils.GetCardinality(_triangleFlags[i]);
                    }

                    return total;
                }
                else
                {
                    return 0;
                }
            }
        }

#if UNITY_EDITOR
        [ContextMenu("CopyToClipboard")]
        public void CopyToClipboard()
        {
            UnityEditor.EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(this);
        }

        [ContextMenu("PasteFromClipboard")]
        public void PasteFromClipboard()
        {
            JsonUtility.FromJsonOverwrite(UnityEditor.EditorGUIUtility.systemCopyBuffer, this);
        }
#endif

        /// <summary>
        /// Custom serialization to write the BitArray to a boolean array.
        /// </summary>
        public void OnBeforeSerialize()
        {
			// _asset = null; // Let's not save this!
            if (_triangleFlags == null)
            {
                return;
            }

            if (TriangleCount > 0)
            {
                _serializedFlags = new serializedFlags[_triangleFlags.Length];
                for (int i = 0; i < _triangleFlags.Length; i++)
                {
                    _serializedFlags[i] = new serializedFlags(_triangleFlags[i].Length);
                    _serializedFlags[i].flags.Initialize();
                }                    
            }

            for (int i = 0; i < _triangleFlags.Length; i++)
            {
                _triangleFlags[i].CopyTo(_serializedFlags[i].flags, 0);
            }

            if (_serializedFlags == null)
            {
                if(Debug.isDebugBuild)
                {
                    Debug.LogError("Serializing triangle flags failed!");
                }
            }
        }


        /// <summary>
        /// Custom deserialization to write the boolean array to the BitArray.
        /// </summary>
        public void OnAfterDeserialize()
        {
			//We're not logging an error here because we'll get spammed by it for empty/not-set assets.
			if (_asset == null && string.IsNullOrEmpty(_assetSlotName))
			{
				return;
			}

			if (_asset != null)
			{
				_assetSlotName = _asset.slotName;
			}
            
            if (_serializedFlags == null)
            {
                return;
            }

            if (_serializedFlags.Length > 0)
            {
                _triangleFlags = new BitArray[_serializedFlags.Length];
                for (int i = 0; i < _serializedFlags.Length; i++)
                {
                    _triangleFlags[i] = new BitArray(_serializedFlags[i].flags);
					_triangleFlags[i].Length = _serializedFlags[i].Count;
                }
            }
        }

        /// <summary>
        ///  Initialize this asset by creating a new boolean array that matches the triangle length in the asset triangle list.
        /// </summary>
        [ExecuteInEditMode]
        public void Initialize()
        {
			SlotDataAsset slot = asset;

            if (slot == null)
            {
                _triangleFlags = null;
                return;
            }

            if (slot.meshData == null)
            {
                return;
            }

            _triangleFlags = new BitArray[slot.meshData.subMeshCount];
            for (int i = 0; i < slot.meshData.subMeshCount; i++)
            {
                _triangleFlags[i] = new BitArray(slot.meshData.submeshes[i].GetTriangles().Length / 3);
            }
        }

        /// <summary>
        ///  Set the triangle flag's boolean value
        /// </summary>
        /// <param name="triangleIndex">The first index for the triangle to set.</param>
        /// <param name="flag">Bool to set the triangle flag to.</param>
        /// <param name="submesh">The submesh index to access. Default = 0.</param>
        [ExecuteInEditMode]
        public void SetTriangleFlag(int triangleIndex, bool flag, int submesh = 0)
        {
            if (_triangleFlags == null)
            {
                if(Debug.isDebugBuild)
                {
                    Debug.LogError("Triangle Array not initialized!");
                }

                return;
            }
                
            if (triangleIndex >= 0 && (_triangleFlags[submesh].Length - 3) > triangleIndex)
            {
                _triangleFlags[submesh][triangleIndex] = flag;
            }
        }

        /// <summary>
        /// Set the given BitArray to this object's triangleFlag's BitArray.
        /// </summary>
        /// <param name="selection">The BitArray selection.</param>
        [ExecuteInEditMode]
        public void SaveSelection( BitArray selection )
        {
            int submesh = asset.subMeshIndex;
            if (selection.Count != _triangleFlags[submesh].Count)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.Log("SaveSelection: counts don't match!");
                }

                return;
            }

            //Only works for submesh 0 for now
            _triangleFlags[submesh].SetAll(false);
            if (selection.Length == _triangleFlags[submesh].Length)
            {
                _triangleFlags[submesh] = new BitArray(selection);
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("SaveSelection: counts don't match!");
                }
            }

            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }

        /// <summary>
        /// Generates a final BitArray mask from a list of MeshHideAssets.
        /// </summary>
        /// <returns>The BitArray array mask.</returns>
        /// <param name="assets">List of MeshHideAssets.</param>
        public static BitArray[] GenerateMask( List<MeshHideAsset> assets )
        {
            List<BitArray[]> flags = new List<BitArray[]>();
            for (int i = 0; i < assets.Count; i++)
            {
                MeshHideAsset asset = assets[i];
                flags.Add(asset.triangleFlags);
            }

            return CombineTriangleFlags(flags);
        }

        /// <summary>
        /// Combines the list of BitArray arrays.
        /// </summary>
        /// <returns>The final combined BitArray array.</returns>
        /// <param name="flags">List of BitArray array flags.</param>
        public static BitArray[] CombineTriangleFlags( List<BitArray[]> flags)
        {
            if (flags == null || flags.Count <= 0)
            {
                return null;
            }

            BitArray[] final = new BitArray[flags[0].Length];
            for(int i = 0; i < flags[0].Length; i++)
            {
                final[i] = new BitArray(flags[0][i]);
            }

            BitArray[] baseSubmeshFlags = flags[0];

            for (int i = 1; i < flags.Count; i++)
            {              
                BitArray[] SubmeshFlags = flags[i];

                for (int j = 0; j < SubmeshFlags.Length; j++)
                {
                    if (j < baseSubmeshFlags.Length)
                    {
                        if (SubmeshFlags[j] != null && baseSubmeshFlags[j] != null && SubmeshFlags[j].Count == baseSubmeshFlags[j].Count)
                        {
                            final[j].Or(SubmeshFlags[j]);
                        }
                    }
                }
            }

            return final;
        }

        #if UNITY_EDITOR
		#if UMA_HOTKEYS
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/Mesh Hide Asset %#h")]
		#else
		[UnityEditor.MenuItem("Assets/Create/UMA/Misc/Mesh Hide Asset")]
		#endif
        public static void CreateMeshHideAsset()
        {
            UMA.CustomAssetUtility.CreateAsset<MeshHideAsset>();
        }
        #endif
    }
}