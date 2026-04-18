#define UMA_COMPRESS_HIDDENVERTEXESBYUV
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;
using System.Collections.Specialized;
using Unity.Collections;
using System;
#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
#endif


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
        const int bitArraySize = 512;   // bit array dimensions are 512x512. We will index into this as if it were a texture, u*512 + (v * *512x512), 
                                        // This will let us find the bit for any vertex on the triangle,and we will store the visibility of the vertex.
                                        // Then we can determine the visibility of the triangle by checking the visibility of its vertices.
                                        // We can use the same strict flags as we do when calculating the MeshHideAsset LOD levels -- TriangleHideStrategy. 
                                        //                     "Strict: hide only if ALL 3 vertices were previously hidden.\n" +
                                        //                     "Weighted: hide if 2 or more vertices were previously hidden.\n" +
                                        //                     "Conservative: hide if ANY 1 vertex was previously hidden."),
        public enum TriangleHideStrategy
        {
            Strict,
            Conservative,
            Weighted
        }

        public static TriangleHideStrategy HideStrategy = TriangleHideStrategy.Conservative;

#if UNITY_EDITOR
        public bool NeedsRebuildFromUV()
        {
            var sda = asset;
            if (sda == null || sda.meshData == null)
            {
                return false;
            }
            if (AssetHash == 0)
            {
                return false;
            }

            ulong current = sda.meshData.CalculateHashCode();
            return current != AssetHash;
        }

        public void UpdateEditorHashAndUVMaskFromFlags()
        {
            var sda = asset;
            if (sda == null || sda.meshData == null)
            {
                return;
            }

            AssetHash = sda.meshData.CalculateHashCode();

            if (HiddenVertexesByUV == null || HiddenVertexesByUV.Length != bitArraySize * bitArraySize)
            {
                HiddenVertexesByUV = new BitArray(bitArraySize * bitArraySize);
            }
            else
            {
                HiddenVertexesByUV.SetAll(false);
            }

            if (_triangleFlags == null || _triangleFlags.Length == 0)
            {
                return;
            }

            var uvs = sda.meshData.uv;
            if (uvs == null || uvs.Length == 0)
            {
                return;
            }

            int submeshCount = Mathf.Min(_triangleFlags.Length, sda.meshData.subMeshCount);
            for (int sm = 0; sm < submeshCount; sm++)
            {
                var flags = _triangleFlags[sm];
                if (flags == null)
                {
                    continue;
                }

                var tris = sda.meshData.submeshes[sm].GetBaseTriangles();
                if (tris == null || tris.Length == 0)
                {
                    continue;
                }

                int triCount = Mathf.Min(flags.Count, tris.Length / 3);
                for (int t = 0; t < triCount; t++)
                {
                    if (!flags[t])
                    {
                        continue;
                    }

                    int ti = t * 3;
                    MarkUV(uvs, tris[ti + 0]);
                    MarkUV(uvs, tris[ti + 1]);
                    MarkUV(uvs, tris[ti + 2]);
                }
            }
        }

        private void MarkUV(Vector2[] uvs, int vertexIndex)
        {
            if (uvs == null || vertexIndex < 0 || vertexIndex >= uvs.Length)
            {
                return;
            }

            Vector2 uv = uvs[vertexIndex];
            float uf = uv.x;
            float vf = uv.y;
            if (!float.IsFinite(uf) || !float.IsFinite(vf))
            {
                return;
            }

            uf = uf - Mathf.Floor(uf);
            vf = vf - Mathf.Floor(vf);

            int u = Mathf.Clamp((int)(uf * (bitArraySize - 1)), 0, bitArraySize - 1);
            int v = Mathf.Clamp((int)(vf * (bitArraySize - 1)), 0, bitArraySize - 1);
            int idx = u + (v * bitArraySize);
            if (idx >= 0 && idx < HiddenVertexesByUV.Length)
            {
                HiddenVertexesByUV[idx] = true;
            }
        }

        public void RebuildFlagsFromEditorUVMask()
        {
            var sda = asset;
            if (sda == null || sda.meshData == null)
            {
                return;
            }

            if (HiddenVertexesByUV == null || HiddenVertexesByUV.Length != bitArraySize * bitArraySize)
            {
                return;
            }

            if (_triangleFlags == null || _triangleFlags.Length != sda.meshData.subMeshCount)
            {
                _triangleFlags = new BitArray[sda.meshData.subMeshCount];
            }

            var uvs = sda.meshData.uv;
            if (uvs == null || uvs.Length == 0)
            {
                return;
            }

            for (int sm = 0; sm < sda.meshData.subMeshCount; sm++)
            {
                var tris = sda.meshData.submeshes[sm].GetBaseTriangles();
                int triCount = (tris != null) ? (tris.Length / 3) : 0;
                if (triCount <= 0)
                {
                    _triangleFlags[sm] = new BitArray(0);
                    continue;
                }

                BitArray flags = _triangleFlags[sm];
                if (flags == null || flags.Count != triCount)
                {
                    flags = new BitArray(triCount);
                    _triangleFlags[sm] = flags;
                }
                else
                {
                    flags.SetAll(false);
                }

                for (int t = 0; t < triCount; t++)
                {
                    int ti = t * 3;
                    bool v0 = IsUVMarked(uvs, tris[ti + 0]);
                    bool v1 = IsUVMarked(uvs, tris[ti + 1]);
                    bool v2 = IsUVMarked(uvs, tris[ti + 2]);

                    bool hide;
                    if (HideStrategy == TriangleHideStrategy.Strict)
                    {
                        hide = v0 && v1 && v2;
                    }
                    else if (HideStrategy == TriangleHideStrategy.Weighted)
                    {
                        int c = 0;
                        if (v0) c++;
                        if (v1) c++;
                        if (v2) c++;
                        hide = c >= 2;
                    }
                    else
                    {
                        hide = v0 || v1 || v2;
                    }

                    if (hide)
                    {
                        flags[t] = true;
                    }
                }
            }

            AssetHash = sda.meshData.CalculateHashCode();
        }

        private bool IsUVMarked(Vector2[] uvs, int vertexIndex)
        {
            if (uvs == null || vertexIndex < 0 || vertexIndex >= uvs.Length)
            {
                return false;
            }

            Vector2 uv = uvs[vertexIndex];
            float uf = uv.x;
            float vf = uv.y;
            if (!float.IsFinite(uf) || !float.IsFinite(vf))
            {
                return false;
            }

            uf = uf - Mathf.Floor(uf);
            vf = vf - Mathf.Floor(vf);

            int u = Mathf.Clamp((int)(uf * (bitArraySize - 1)), 0, bitArraySize - 1);
            int v = Mathf.Clamp((int)(vf * (bitArraySize - 1)), 0, bitArraySize - 1);
            int idx = u + (v * bitArraySize);
            return idx >= 0 && idx < HiddenVertexesByUV.Length && HiddenVertexesByUV[idx];
        }
#endif

        /// <summary>
        /// The asset we want to apply mesh hiding to if found in the generated UMA.
        /// </summary>
        /// <value>The SlotDataAsset.</value>
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
        [FormerlySerializedAs("asset")]
        [SerializeField]
        private SlotDataAsset _asset;

        public bool HasReference
        {
            get { return _asset != null; }
        }
#if UNITY_EDITOR
        // The hash of the meshdata in the asset. This is used to detect when the mesh has changed and the hide asset needs to be reinitialized.
        public ulong AssetHash = 0;
        // A UV map of the vertices in the mesh. This is used to determine which vertices are hidden based on the triangle hiding.
        // This is only needed when the Hash changes, and we have to recreate the hide flags based on the UVs, so we can determine which vertices are hidden and then hide the triangles based on the vertex hiding.
        public BitArray HiddenVertexesByUV = new BitArray(bitArraySize * bitArraySize); 

        // Serialized backing for HiddenVertexesByUV (BitArray is not Unity-serializable)
        [SerializeField]
        private serializedFlags _serializedHiddenVertexesByUV;

        // Optional compressed backing for HiddenVertexesByUV. Enabled with `UMA_COMPRESS_HIDDENVERTEXESBYUV`.
        // Editor-only because it is only used for authoring/reimport scenarios.
        [SerializeField]
        private byte[] _serializedHiddenVertexesByUVCompressed;
#endif
        public string AssetSlotName
        {
            get
            {
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
        public BitArray[] triangleFlags
        {
            get { return _triangleFlags; }
        }

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
        [FormerlySerializedAs("_serializedFlags")]
        private serializedFlags[] _serializedFlags;

        // Per-LOD storage (Unity 6000.2 or newer). Backward compatible when not present.
#if UNITY_6000_2_OR_NEWER
        [SerializeField]
        private List<serializedFlags[]> _serializedFlagsPerLOD = new List<serializedFlags[]>(); // index = LOD level

        // runtime cache of per LOD bitarrays, not serialized
        private readonly Dictionary<int, BitArray[]> _triangleFlagsPerLOD = new Dictionary<int, BitArray[]>();
#endif

        [SerializeField]
        private List<UMALodRange> lodOffsets = new List<UMALodRange>();

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
            if (_triangleFlags != null && TriangleCount > 0)
            {
                _serializedFlags = new serializedFlags[_triangleFlags.Length];
                for (int i = 0; i < _triangleFlags.Length; i++)
                {
                    _serializedFlags[i] = new serializedFlags(_triangleFlags[i].Length);
                    _serializedFlags[i].flags.Initialize();
                    _triangleFlags[i].CopyTo(_serializedFlags[i].flags, 0);
                }
            }

#if UNITY_EDITOR
            if (HiddenVertexesByUV != null && HiddenVertexesByUV.Length == bitArraySize * bitArraySize)
            {
#if UMA_COMPRESS_HIDDENVERTEXESBYUV
                _serializedHiddenVertexesByUV = null; // keep old field empty when using compressed storage
                _serializedHiddenVertexesByUVCompressed = CompressHiddenVertexesByUV(HiddenVertexesByUV);
#else
                _serializedHiddenVertexesByUV = new serializedFlags(HiddenVertexesByUV.Length);
                _serializedHiddenVertexesByUV.flags.Initialize();
                HiddenVertexesByUV.CopyTo(_serializedHiddenVertexesByUV.flags, 0);
                _serializedHiddenVertexesByUVCompressed = null;
#endif
            }
            else
            {
                _serializedHiddenVertexesByUV = null;
                _serializedHiddenVertexesByUVCompressed = null;
            }
#endif

#if UNITY_600_2_OR_NEWER // guard against accidental symbol typo
#endif
#if UNITY_6000_2_OR_NEWER
            // serialize per-LOD if present
            if (_triangleFlagsPerLOD != null && _triangleFlagsPerLOD.Count > 0)
            {
                int maxLOD = 0;
                foreach (var k in _triangleFlagsPerLOD.Keys)
                {
                    if (k > maxLOD) maxLOD = k;
                }
                // ensure list capacity
                while (_serializedFlagsPerLOD.Count <= maxLOD)
                {
                    _serializedFlagsPerLOD.Add(null);
                }

                for (int lod = 0; lod <= maxLOD; lod++)
                {
                    BitArray[] lodFlags;
                    if (!_triangleFlagsPerLOD.TryGetValue(lod, out lodFlags) || lodFlags == null)
                    {
                        continue;
                    }
                    var ser = new serializedFlags[lodFlags.Length];
                    for (int i = 0; i < lodFlags.Length; i++)
                    {
                        ser[i] = new serializedFlags(lodFlags[i].Length);
                        ser[i].flags.Initialize();
                        lodFlags[i].CopyTo(ser[i].flags, 0);
                    }
                    _serializedFlagsPerLOD[lod] = ser;
                }
            }
#endif

            if (_serializedFlags == null)
            {
                _serializedFlags = new serializedFlags[0];
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

            if (_serializedFlags != null && _serializedFlags.Length > 0)
            {
                _triangleFlags = new BitArray[_serializedFlags.Length];
                for (int i = 0; i < _serializedFlags.Length; i++)
                {
                    _triangleFlags[i] = new BitArray(_serializedFlags[i].flags);
                    _triangleFlags[i].Length = _serializedFlags[i].Count;
                }
            }

#if UNITY_EDITOR
#if UMA_COMPRESS_HIDDENVERTEXESBYUV
            // Prefer compressed representation when present; fall back to legacy int[] representation for backward compatibility.
            if (_serializedHiddenVertexesByUVCompressed != null && _serializedHiddenVertexesByUVCompressed.Length > 0)
            {
                HiddenVertexesByUV = DecompressHiddenVertexesByUV(_serializedHiddenVertexesByUVCompressed, bitArraySize * bitArraySize);
            }
            else if (_serializedHiddenVertexesByUV != null && _serializedHiddenVertexesByUV.flags != null && _serializedHiddenVertexesByUV.flags.Length > 0)
            {
                HiddenVertexesByUV = new BitArray(_serializedHiddenVertexesByUV.flags);
                HiddenVertexesByUV.Length = _serializedHiddenVertexesByUV.Count;
            }
            else if (HiddenVertexesByUV == null || HiddenVertexesByUV.Length != bitArraySize * bitArraySize)
            {
                HiddenVertexesByUV = new BitArray(bitArraySize * bitArraySize);
            }
#else
            if (_serializedHiddenVertexesByUV != null && _serializedHiddenVertexesByUV.flags != null && _serializedHiddenVertexesByUV.flags.Length > 0)
            {
                HiddenVertexesByUV = new BitArray(_serializedHiddenVertexesByUV.flags);
                HiddenVertexesByUV.Length = _serializedHiddenVertexesByUV.Count;
            }
            else if (HiddenVertexesByUV == null || HiddenVertexesByUV.Length != bitArraySize * bitArraySize)
            {
                HiddenVertexesByUV = new BitArray(bitArraySize * bitArraySize);
            }
#endif
#endif

#if UNITY_6000_2_OR_NEWER
            // rebuild cache from serialized LODs if present
            if (_serializedFlagsPerLOD != null && _serializedFlagsPerLOD.Count > 0)
            {
                _triangleFlagsPerLOD.Clear();
                for (int lod = 0; lod < _serializedFlagsPerLOD.Count; lod++)
                {
                    var ser = _serializedFlagsPerLOD[lod];
                    if (ser == null) continue;
                    var arr = new BitArray[ser.Length];
                    for (int i = 0; i < ser.Length; i++)
                    {
                        arr[i] = new BitArray(ser[i].flags);
                        arr[i].Length = ser[i].Count;
                    }
                    _triangleFlagsPerLOD[lod] = arr;
                }
            }
#endif
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
#if UNITY_6000_2_OR_NEWER
                // default initialize against base LOD (0)
                int triCount = slot.meshData.submeshes[i].GetTriangles(0).Length / 3;
#else
                int triCount = slot.meshData.submeshes[i].GetTriangles().Length / 3;
#endif
                _triangleFlags[i] = new BitArray(triCount);
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
                if (Debug.isDebugBuild)
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
        public void SaveSelection(BitArray selection)
        {
            int submesh = asset.subMeshIndex;
            if (_triangleFlags == null || submesh < 0 || submesh >= _triangleFlags.Length)
            {
                return;
            }
            if (selection.Count != _triangleFlags[submesh].Count)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.Log("SaveSelection: counts don't match!");
                }

                return;
            }

            _triangleFlags[submesh].SetAll(false);
            if (selection.Length == _triangleFlags[submesh].Length)
            {
                _triangleFlags[submesh] = new BitArray(selection);
            }
#if UNITY_EDITOR
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("SaveSelection: counts don't match!");
                }
            }
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_6000_2_OR_NEWER
        // LOD helpers
        public int GetLODCount()
        {
            if (asset == null || asset.meshData == null || asset.subMeshIndex < 0 || asset.subMeshIndex >= asset.meshData.subMeshCount)
            {
                return 1;
            }
            var ranges = asset.meshData.submeshes[asset.subMeshIndex].lodRanges;
            return (ranges != null && ranges.Count > 0) ? ranges.Count : 1;
        }

        public bool HasStoredLODMask(int lod)
        {
            if (lod <= 0)
            {
                return true;
            }

            if (_triangleFlagsPerLOD != null && _triangleFlagsPerLOD.ContainsKey(lod))
            {
                return true;
            }

            // If we have serialized data for this LOD, it exists even if not yet cached.
            if (_serializedFlagsPerLOD != null && lod >= 0 && lod < _serializedFlagsPerLOD.Count)
            {
                return _serializedFlagsPerLOD[lod] != null;
            }

            return false;
        }

        private void EnsureLODAllocated(int lod)
        {
            if (asset == null || asset.meshData == null) return;
            if (_triangleFlagsPerLOD.ContainsKey(lod)) return;

            var perSubmesh = new BitArray[asset.meshData.subMeshCount];
            for (int sm = 0; sm < asset.meshData.subMeshCount; sm++)
            {
                int triCount = asset.meshData.submeshes[sm].GetTriangles(lod).Length / 3;
                perSubmesh[sm] = new BitArray(triCount);
            }
            _triangleFlagsPerLOD[lod] = perSubmesh;
        }

        private static HashSet<int> BuildHiddenVertexSet(BitArray[] mask, UMAMeshData meshData, int submesh, int lod)
        {
            var set = new HashSet<int>();
            if (mask == null || meshData == null) return set;
#if UNITY_6000_2_OR_NEWER
            var tris = meshData.submeshes[submesh].GetTriangles(lod);
#else
            var tris = meshData.submeshes[submesh].GetTriangles();
#endif
            if (mask[submesh] == null) return set;
            int triCount = tris.Length / 3;
            int mcount = Mathf.Min(mask[submesh].Length, triCount);
            for (int i = 0; i < mcount; i++)
            {
                if (mask[submesh][i])
                {
                    int t = i * 3;
                    set.Add(tris[t + 0]);
                    set.Add(tris[t + 1]);
                    set.Add(tris[t + 2]);
                }
            }
            return set;
        }

        public BitArray[] GetTriangleFlagsForLOD(int lod)
        {
            if (lod <= 0)
            {
                return _triangleFlags;
            }

            if (asset == null || asset.meshData == null)
            {
                return _triangleFlags;
            }

            BitArray[] lodFlags;
            if (_triangleFlagsPerLOD.TryGetValue(lod, out lodFlags) && lodFlags != null)
            {
                return lodFlags;
            }

            // Generate from base selection by vertex mapping.
            EnsureLODAllocated(lod);
            lodFlags = _triangleFlagsPerLOD[lod];

            for (int sm = 0; sm < asset.meshData.subMeshCount; sm++)
            {
                // Build set of hidden vertices from base (LOD0)
                var hiddenVerts = BuildHiddenVertexSet(_triangleFlags, asset.meshData, sm, 0);
                var tris = asset.meshData.submeshes[sm].GetTriangles(lod);
                var dest = lodFlags[sm];
                int triCount = tris.Length / 3;
                for (int i = 0; i < triCount; i++)
                {
                    int t = i * 3;
                    bool hide = hiddenVerts.Contains(tris[t]) || hiddenVerts.Contains(tris[t + 1]) || hiddenVerts.Contains(tris[t + 2]);
                    if (i < dest.Length) dest[i] = hide;
                }
            }

            return lodFlags;
        }

        public void SaveSelectionForLOD(BitArray selection, int lod)
        {
            if (asset == null || asset.meshData == null) return;

            if (lod <= 0)
            {
                SaveSelection(selection);
                return;
            }

            EnsureLODAllocated(lod);
            int submesh = asset.subMeshIndex;
            var perSm = _triangleFlagsPerLOD[lod];
            if (selection != null && submesh >= 0 && submesh < perSm.Length)
            {
                if (selection.Length == perSm[submesh].Length)
                {
                    perSm[submesh] = new BitArray(selection);
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(this);
#endif
                }
            }
        }

        public void CopyLODMask(int fromLOD, int toLOD, bool replaceDestination)
        {
            CopyLODMask(fromLOD, toLOD, replaceDestination, TriangleHideStrategy.Conservative);
        }

        public void CopyLODMask(int fromLOD, int toLOD, bool replaceDestination, TriangleHideStrategy mode)
        {
            if (asset == null || asset.meshData == null) return;
            if (fromLOD == toLOD) return;

            EnsureLODAllocated(toLOD);

            // Build hidden vertex set from source LOD mask (or base flags when fromLOD == 0)
            BitArray[] srcMask = (fromLOD <= 0) ? _triangleFlags : GetTriangleFlagsForLOD(fromLOD);
            int submesh = asset.subMeshIndex;
            var hiddenVerts = BuildHiddenVertexSet(srcMask, asset.meshData, submesh, Mathf.Max(0, fromLOD));

            // Apply to destination LOD triangles
            var tris = asset.meshData.submeshes[submesh].GetTriangles(toLOD);
            var dest = _triangleFlagsPerLOD[toLOD][submesh];
            if (replaceDestination)
            {
                dest.SetAll(false);
            }

            int triCount = tris.Length / 3;
            for (int i = 0; i < triCount && i < dest.Length; i++)
            {
                int t = i * 3;
                bool v0 = hiddenVerts.Contains(tris[t]);
                bool v1 = hiddenVerts.Contains(tris[t + 1]);
                bool v2 = hiddenVerts.Contains(tris[t + 2]);

                bool hide = false;
                if (mode == TriangleHideStrategy.Strict)
                {
                    hide = v0 && v1 && v2;
                }
                else if (mode == TriangleHideStrategy.Weighted)
                {
                    int count = 0;
                    if (v0) count++;
                    if (v1) count++;
                    if (v2) count++;
                    hide = count >= 2;
                }
                else
                {
                    // Conservative
                    hide = v0 || v1 || v2;
                }

                if (hide)
                {
                    dest[i] = true;
                }
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
#endif

        /// <summary>
        /// Generates a final BitArray mask from a list of MeshHideAssets.
        /// </summary>
        /// <returns>The BitArray array mask.</returns>
        /// <param name="assets">List of MeshHideAssets.</param>
        public static BitArray[] GenerateMask(List<MeshHideAsset> assets)
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
        /// Generates a final BitArray mask for a specific LOD from a list of MeshHideAssets.
        /// </summary>
        public static BitArray[] GenerateMask(List<MeshHideAsset> assets, int lod)
        {
            if (assets == null || assets.Count == 0) return null;
            var flags = new List<BitArray[]>();
            for (int i = 0; i < assets.Count; i++)
            {
                var a = assets[i];
#if UNITY_6000_2_OR_NEWER
                flags.Add(a.GetTriangleFlagsForLOD(Mathf.Max(0, lod)) ?? a.triangleFlags);
#else
                flags.Add(a.triangleFlags);
#endif
            }
            return CombineTriangleFlags(flags);
        }

        /// <summary>
        /// Combines the list of BitArray arrays.
        /// </summary>
        /// <returns>The final combined BitArray array.</returns>
        /// <param name="flags">List of BitArray array flags.</param>
        public static BitArray[] CombineTriangleFlags(List<BitArray[]> flags)
        {
            if (flags == null || flags.Count <= 0)
            {
                return null;
            }

            BitArray[] final = new BitArray[flags[0].Length];
            for (int i = 0; i < flags[0].Length; i++)
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

#if UMA_COMPRESS_HIDDENVERTEXESBYUV
        private static byte[] CompressHiddenVertexesByUV(BitArray bits)
        {
            if (bits == null || bits.Length <= 0)
            {
                return null;
            }

            int byteCount = (bits.Length + 7) / 8;
            var raw = new byte[byteCount];
            bits.CopyTo(raw, 0);

            using (var ms = new MemoryStream())
            {
                // Store original bit length first so we can restore exact length even if not fixed-size in future.
                // Don't use BinaryWriter without leaveOpen here (it would dispose the MemoryStream in some Unity profiles).
                var header = BitConverter.GetBytes(bits.Length);
                ms.Write(header, 0, header.Length);

                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                {
                    gz.Write(raw, 0, raw.Length);
                }

                return ms.ToArray();
            }
        }

        private static BitArray DecompressHiddenVertexesByUV(byte[] blob, int fallbackBitLength)
        {
            if (blob == null || blob.Length == 0)
            {
                return new BitArray(fallbackBitLength);
            }

            using (var ms = new MemoryStream(blob))
            {
                int bitLength;
                using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    bitLength = br.ReadInt32();
                }
                if (bitLength <= 0)
                {
                    bitLength = fallbackBitLength;
                }

                int byteCount = (bitLength + 7) / 8;
                var raw = new byte[byteCount];
                using (var gz = new GZipStream(ms, CompressionMode.Decompress, leaveOpen: true))
                {
                    int offset = 0;
                    while (offset < raw.Length)
                    {
                        int read = gz.Read(raw, offset, raw.Length - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                }

                var bits = new BitArray(raw);
                bits.Length = bitLength;
                return bits;
            }
        }
#endif
#endif
    }
}